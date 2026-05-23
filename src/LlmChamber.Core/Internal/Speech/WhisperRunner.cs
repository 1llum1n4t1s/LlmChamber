using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using LlmChamber.Speech;
using SuperLightLogger;

namespace LlmChamber.Internal.Speech;

/// <summary>
/// whisper-cli プロセスを起動して文字起こしを実行する。
/// 入力WAVは16kHzモノラル想定（whisper.cppの制約）。WAV以外は ffmpeg 経由で変換が必要。
/// </summary>
internal sealed class WhisperRunner
{
    private static readonly ILog _logger = LogManager.GetLogger<WhisperRunner>();
    private readonly string _binaryPath;
    private readonly string _modelPath;

    public WhisperRunner(string binaryPath, string modelPath)
    {
        _binaryPath = binaryPath;
        _modelPath = modelPath;
    }

    /// <summary>
    /// 音声ファイルを文字起こしする。
    /// whisper.cpp は WAV 16kHz モノラル PCM を期待する。
    /// </summary>
    public async Task<TranscriptionResult> TranscribeAsync(
        string audioFilePath,
        TranscribeOptions? options,
        CancellationToken cancellationToken = default)
    {
        options ??= new TranscribeOptions();

        // JSON 出力用の一時ファイル
        string outputBase = Path.Combine(Path.GetTempPath(), $"whisper-{Guid.NewGuid():N}");
        string jsonOutputPath = outputBase + ".json";

        var args = new List<string>
        {
            "-m", _modelPath,
            "-f", audioFilePath,
            "-t", options.Threads.ToString(CultureInfo.InvariantCulture),
            "-oj", // JSON 出力
            "-of", outputBase,
            "--no-prints", // verbose ログを抑制
        };

        if (!string.IsNullOrWhiteSpace(options.Language) &&
            !options.Language.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-l");
            args.Add(options.Language);
        }
        else
        {
            args.Add("-l");
            args.Add("auto");
        }

        if (options.TranslateToEnglish)
        {
            args.Add("-tr");
        }

        var psi = new ProcessStartInfo
        {
            FileName = _binaryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_binaryPath) ?? Environment.CurrentDirectory,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi)
                ?? throw new SpeechRuntimeException("whisper-cli プロセスの起動に失敗しました。");

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdoutBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderrBuilder.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // キャンセル時に子プロセスが孤児化しないように強制終了
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                throw;
            }

            // 非同期出力ハンドラが完全にフラッシュされるよう、同期 WaitForExit() で確実に待つ
            try { process.WaitForExit(); } catch { /* ignore */ }

            if (process.ExitCode != 0)
            {
                throw new SpeechRuntimeException(
                    $"whisper-cli の実行に失敗しました (exit {process.ExitCode})。",
                    process.ExitCode, stderrBuilder.ToString());
            }

            if (!File.Exists(jsonOutputPath))
            {
                throw new SpeechRuntimeException(
                    "whisper-cli の出力JSONが見つかりません。",
                    process.ExitCode, stderrBuilder.ToString());
            }

            string json = await File.ReadAllTextAsync(jsonOutputPath, cancellationToken);
            return ParseWhisperJson(json, options.IncludeSegments);
        }
        finally
        {
            // 一時ファイルクリーンアップ
            if (File.Exists(jsonOutputPath))
            {
                try { File.Delete(jsonOutputPath); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>
    /// whisper.cpp の JSON 出力をパースする。
    /// 構造例: { "transcription": [ { "timestamps": { "from": "00:00:00,000", "to": "..." }, "text": "..." } ] }
    /// </summary>
    internal static TranscriptionResult ParseWhisperJson(string json, bool includeSegments)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 言語検出結果
        string? language = null;
        if (root.TryGetProperty("result", out var resultEl) &&
            resultEl.TryGetProperty("language", out var langEl) &&
            langEl.ValueKind == JsonValueKind.String)
        {
            language = langEl.GetString();
        }

        var segments = new List<TranscriptionSegment>();
        var fullText = new StringBuilder();

        if (root.TryGetProperty("transcription", out var transcriptionEl) &&
            transcriptionEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in transcriptionEl.EnumerateArray())
            {
                string text = item.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(text)) continue;

                fullText.Append(text);

                if (includeSegments && item.TryGetProperty("timestamps", out var tsEl))
                {
                    TimeSpan start = ParseTimestamp(tsEl.TryGetProperty("from", out var fromEl) ? fromEl.GetString() : null);
                    TimeSpan end = ParseTimestamp(tsEl.TryGetProperty("to", out var toEl) ? toEl.GetString() : null);
                    segments.Add(new TranscriptionSegment(start, end, text.Trim()));
                }
            }
        }

        TimeSpan? duration = segments.Count > 0 ? segments[^1].End : null;

        return new TranscriptionResult(
            fullText.ToString().Trim(),
            duration,
            includeSegments && segments.Count > 0 ? segments : null,
            language);
    }

    /// <summary>"00:00:01,500" 形式のタイムスタンプを TimeSpan に変換する。</summary>
    internal static TimeSpan ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return TimeSpan.Zero;

        // "00:00:01,500" → hh:mm:ss,fff
        string normalized = value.Replace(',', '.');
        if (TimeSpan.TryParseExact(normalized, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out var ts))
            return ts;
        if (TimeSpan.TryParse(normalized, CultureInfo.InvariantCulture, out ts))
            return ts;

        return TimeSpan.Zero;
    }
}
