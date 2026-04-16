using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Options;
using SuperLightLogger;

namespace LlmChamber.Internal;

/// <summary>
/// Ollamaプロセスのライフサイクル管理。
/// アプリローカルに隔離された環境でOllamaを起動・停止する。
/// </summary>
internal sealed class OllamaProcessManager : IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private static readonly ILog _logger = LogManager.GetLogger<OllamaProcessManager>();
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

            _logger.Info($"Ollamaプロセスを起動します: port={_port}, models={modelDir}");

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

            _process = new Process { StartInfo = startInfo };
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) _logger.Debug($"[ollama] {e.Data}");
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) _logger.Debug($"[ollama:err] {e.Data}");
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
                await WaitForReadyAsync(cancellationToken);
            }
            catch
            {
                // ヘルスチェック失敗時はプロセスをクリーンアップして再試行可能にする
                _logger.Warn("Ollamaヘルスチェック失敗。プロセスをクリーンアップします。");
                try { _process.Kill(entireProcessTree: true); } catch { /* ベストエフォート */ }
                _process.Dispose();
                _process = null;
                throw;
            }

            _logger.Info($"Ollamaプロセスが準備完了: PID={_process.Id}, Port={_port}");
        }
        finally
        {
            _startLock.Release();
        }
    }

    /// <summary>Ollamaプロセスを停止する。</summary>
    public async Task StopAsync()
    {
        if (_process is null || _process.HasExited) return;

        _logger.Info($"Ollamaプロセスを停止します: PID={_process.Id}");

        try
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().WaitAsync(_options.ShutdownTimeout);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Ollamaプロセスの停止中にエラーが発生しました。: {ex.Message}");
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private async Task WaitForReadyAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.StartupTimeout);

        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var response = await client.GetAsync("/api/version", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    _logger.Debug("Ollamaヘルスチェック成功");
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
            throw new ProcessStartException(
                $"Ollamaプロセスがクラッシュしました (exit code: {_process.ExitCode})。");
        }

        throw new ProcessStartException(
            $"Ollamaプロセスのヘルスチェックがタイムアウトしました ({_options.StartupTimeout.TotalSeconds}秒)。");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
        _startLock.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _process?.Kill(entireProcessTree: true); } catch { /* Disposeでは例外を飲む */ }
        _process?.Dispose();
        _startLock.Dispose();
    }
}
