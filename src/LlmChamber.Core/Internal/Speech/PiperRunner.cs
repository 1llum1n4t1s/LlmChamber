using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using LlmChamber.Speech;
using SuperLightLogger;

namespace LlmChamber.Internal.Speech;

/// <summary>
/// Piper TTS プロセスを起動して音声合成を実行する。
/// </summary>
internal sealed class PiperRunner
{
    private static readonly ILog _logger = LogManager.GetLogger<PiperRunner>();
    private readonly string _binaryPath;

    public PiperRunner(string binaryPath)
    {
        _binaryPath = binaryPath;
    }

    /// <summary>
    /// テキストを音声合成して WAV バイトを返す。
    /// </summary>
    /// <param name="text">読み上げるテキスト。</param>
    /// <param name="voiceModelPath">Piper voice (.onnx) への絶対パス。</param>
    /// <param name="options">TTSオプション。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    public async Task<byte[]> SpeakAsync(
        string text,
        string voiceModelPath,
        SpeakOptions? options,
        CancellationToken cancellationToken = default)
    {
        options ??= new SpeakOptions();
        string tmpWavPath = Path.Combine(Path.GetTempPath(), $"piper-{Guid.NewGuid():N}.wav");

        try
        {
            await SpeakToFileAsync(text, voiceModelPath, tmpWavPath, options, cancellationToken);
            return await File.ReadAllBytesAsync(tmpWavPath, cancellationToken);
        }
        finally
        {
            if (File.Exists(tmpWavPath))
            {
                try { File.Delete(tmpWavPath); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>テキストを音声合成して WAV ファイルに書き出す。</summary>
    public async Task SpeakToFileAsync(
        string text,
        string voiceModelPath,
        string outputWavPath,
        SpeakOptions? options,
        CancellationToken cancellationToken = default)
    {
        options ??= new SpeakOptions();

        // length_scale = 1.0 / SpeakingRate （早口にしたいなら length_scale を小さく）
        float lengthScale = options.SpeakingRate > 0 ? 1.0f / options.SpeakingRate : 1.0f;

        var args = new List<string>
        {
            "--model", voiceModelPath,
            "--output_file", outputWavPath,
            "--length_scale", lengthScale.ToString("0.###", CultureInfo.InvariantCulture),
            "--noise_scale", options.NoiseScale.ToString("0.###", CultureInfo.InvariantCulture),
            "--quiet",
        };

        var psi = new ProcessStartInfo
        {
            FileName = _binaryPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Piper は stdin の UTF-8 を要求する。明示的に固定しないと
            // 日本語/中国語/韓国語 Windows の Console.InputEncoding (CP932/936/949) に
            // 引きずられて非ASCII テキストが化ける
            StandardInputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_binaryPath) ?? Environment.CurrentDirectory,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        Directory.CreateDirectory(Path.GetDirectoryName(outputWavPath)!);

        using var process = Process.Start(psi)
            ?? throw new SpeechRuntimeException("piper プロセスの起動に失敗しました。");

        var stderrBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderrBuilder.AppendLine(e.Data); };
        // RedirectStandardOutput=true だが piper の出力は --output_file で WAV に書き出すので
        // 通常 stdout は空。ただし起動初期に診断メッセージが出ると pipe バッファが詰まって
        // WaitForExitAsync がデッドロックするため、ハンドラ登録 + BeginOutputReadLine で drain する
        process.OutputDataReceived += (_, _) => { /* stdout を drain */ };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        // テキストを stdin に流す
        await process.StandardInput.WriteAsync(text.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // キャンセル時に piper を孤児化させない
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw;
        }

        // 非同期出力ハンドラのフラッシュを保証
        try { process.WaitForExit(); } catch { /* ignore */ }

        if (process.ExitCode != 0)
        {
            throw new SpeechRuntimeException(
                $"piper の実行に失敗しました (exit {process.ExitCode})。",
                process.ExitCode, stderrBuilder.ToString());
        }

        if (!File.Exists(outputWavPath))
        {
            throw new SpeechRuntimeException(
                "piper の出力WAVファイルが見つかりません。",
                process.ExitCode, stderrBuilder.ToString());
        }
    }
}
