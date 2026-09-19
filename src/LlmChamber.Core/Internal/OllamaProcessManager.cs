using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Options;

namespace LlmChamber.Internal;

/// <summary>
/// Ollamaプロセスのライフサイクル管理。
/// アプリローカルに隔離された環境でOllamaを起動・停止する。
/// </summary>
internal sealed class OllamaProcessManager : IAsyncDisposable, IDisposable
{
    private const int MaxStartupStandardErrorChars = 64 * 1024;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly LlmChamberOptions _options;
    private Process? _process;
    private int _port;
    private volatile bool _disposed;

    public OllamaProcessManager(IOptions<LlmChamberOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Ollamaプロセスが使用中のポート。</summary>
    public int Port => _port;

    /// <summary>プロセスが稼働中かどうか。</summary>
    public bool IsRunning => !_disposed && _process is { HasExited: false };

    /// <summary>Ollama APIのベースURL。</summary>
    public string BaseUrl => $"http://localhost:{_port}";

    /// <summary>
    /// Ollamaプロセスを起動する。既に稼働中の場合はno-op。
    /// </summary>
    public async Task StartAsync(string binaryPath, CancellationToken cancellationToken = default)
    {
        await _startLock.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning) return;

            _port = PortFinder.FindAvailablePort();
            string modelDir = _options.SharedModelDirectory
                ?? Path.Combine(_options.CacheDirectory, "models");

            Directory.CreateDirectory(modelDir);

            var startInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            startInfo.Environment["OLLAMA_HOST"] = $"localhost:{_port}";
            startInfo.Environment["OLLAMA_MODELS"] = modelDir;

            var standardErrorLock = new object();
            StringBuilder? standardError = new();
            _process = new Process { StartInfo = startInfo };
            // 子プロセスの停止を防がないよう標準出力は読み捨てる。
            _process.OutputDataReceived += static (_, _) => { };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                lock (standardErrorLock)
                {
                    if (standardError is not null)
                    {
                        AppendStartupStandardError(standardError, e.Data);
                    }
                }
            };

            if (!_process.Start())
            {
                _process.Dispose();
                _process = null;
                throw new ProcessStartException("Ollamaプロセスの起動に失敗しました。");
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // ヘルスチェック
            try
            {
                await WaitForReadyAsync(standardError, standardErrorLock, cancellationToken);
                lock (standardErrorLock)
                {
                    // 起動成功後は stderr を drain しつつ、内部に保持しない。
                    standardError = null;
                }
            }
            catch
            {
                // ヘルスチェック失敗時はプロセスをクリーンアップして再試行可能にする
                try { _process.Kill(entireProcessTree: true); } catch { /* ベストエフォート */ }
                _process.Dispose();
                _process = null;
                throw;
            }
        }
        finally
        {
            _startLock.Release();
        }
    }

    /// <summary>Ollamaプロセスを停止する。</summary>
    public async Task StopAsync()
    {
        if (_process is null) return;
        if (_process.HasExited)
        {
            _process.Dispose();
            _process = null;
            return;
        }

        try
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().WaitAsync(_options.ShutdownTimeout);
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private async Task WaitForReadyAsync(
        StringBuilder standardError,
        object standardErrorLock,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.StartupTimeout);

        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                using var response = await client.GetAsync("/api/version", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // まだ準備できていない
            }
            catch (TaskCanceledException) when (cts.Token.IsCancellationRequested)
            {
                break;
            }

            if (_process?.HasExited == true)
            {
                break;
            }

            try
            {
                await Task.Delay(250, cts.Token);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // StartupTimeoutによるキャンセル → ループを抜けてProcessStartExceptionへ
                break;
            }
        }

        // ユーザーキャンセルはそのまま伝播
        cancellationToken.ThrowIfCancellationRequested();

        // プロセスがクラッシュしていないか確認
        if (_process?.HasExited == true)
        {
            await _process.WaitForExitAsync(CancellationToken.None);
            // 非同期出力イベントの最終行までフラッシュする。
            _process.WaitForExit();
            string? standardErrorText = GetStartupStandardError(standardError, standardErrorLock);

            throw new ProcessStartException(
                $"Ollamaプロセスがクラッシュしました (exit code: {_process.ExitCode})。",
                standardErrorText);
        }

        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // 起動タイムアウトを主例外として保つため、停止失敗は後段の cleanup に委ねる。
            }

            try
            {
                await _process.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(_options.ShutdownTimeout);
                _process.WaitForExit();
            }
            catch
            {
                // 終了待機に失敗しても、取得済みの stderr を伴う起動タイムアウトを返す。
            }
        }

        string? timeoutStandardError = GetStartupStandardError(standardError, standardErrorLock);
        throw new ProcessStartException(
            $"Ollamaプロセスのヘルスチェックがタイムアウトしました ({_options.StartupTimeout.TotalSeconds}秒)。",
            timeoutStandardError);
    }

    private static void AppendStartupStandardError(StringBuilder destination, string line)
    {
        int remaining = MaxStartupStandardErrorChars - destination.Length;
        if (remaining <= 0) return;

        int characterCount = Math.Min(line.Length, remaining);
        destination.Append(line.AsSpan(0, characterCount));
        if (characterCount == line.Length && destination.Length < MaxStartupStandardErrorChars)
        {
            destination.AppendLine();
        }
    }

    private static string? GetStartupStandardError(StringBuilder standardError, object standardErrorLock)
    {
        lock (standardErrorLock)
        {
            return standardError.Length == 0
                ? null
                : standardError.ToString().TrimEnd();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await StopAsync();
        }
        finally
        {
            _startLock.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _process?.Kill(entireProcessTree: true); } catch { /* Disposeでは例外を飲む */ }
        _process?.Dispose();
        _process = null;
        _startLock.Dispose();
    }
}
