using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using LlmChamber.Media;
using SuperLightLogger;

namespace LlmChamber.Internal.Media;

/// <summary>
/// FFmpeg プロセスで動画からフレーム画像を抽出する。
/// </summary>
internal sealed class FFmpegFrameExtractor
{
    private static readonly ILog _logger = LogManager.GetLogger<FFmpegFrameExtractor>();
    private readonly string _ffmpegBinaryPath;

    public FFmpegFrameExtractor(string ffmpegBinaryPath)
    {
        _ffmpegBinaryPath = ffmpegBinaryPath;
    }

    /// <summary>
    /// 動画からフレームを順次抽出する。フレーム単位で <see cref="VideoFrameAnalysis"/> を返す（Description は null）。
    /// </summary>
    public async IAsyncEnumerable<VideoFrameAnalysis> ExtractAsync(
        string videoFilePath,
        VideoAnalysisOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!File.Exists(videoFilePath))
        {
            throw new FileNotFoundException($"動画ファイルが見つかりません: {videoFilePath}", videoFilePath);
        }

        // 一時ディレクトリにフレームを書き出す
        string uniqueId = Guid.NewGuid().ToString("N")[..8];
        string framesDir = Path.Combine(Path.GetTempPath(), $"llmchamber-frames-{uniqueId}");
        Directory.CreateDirectory(framesDir);

        try
        {
            string formatExt = options.FrameFormat == VideoFrameFormat.Png ? "png" : "jpg";
            string outputPattern = Path.Combine(framesDir, $"frame-%05d.{formatExt}");

            // FFmpeg 引数構築
            // -vf fps=1/interval : 指定秒ごとに1フレーム
            // -frames:v N        : 最大N枚（MaxFrames>0 のとき）
            // -vf scale=W:-2     : 最大幅指定
            // -q:v               : JPEG品質
            var args = new List<string>
            {
                "-y", // 上書き許可
                "-i", videoFilePath,
            };

            // フィルタチェイン: fps + scale を 1 つの -vf で渡す
            var filterChain = new List<string>();
            filterChain.Add($"fps=1/{options.FrameIntervalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}");
            if (options.MaxFrameWidth > 0)
            {
                filterChain.Add($"scale={options.MaxFrameWidth}:-2");
            }
            args.Add("-vf");
            args.Add(string.Join(",", filterChain));

            if (options.MaxFrames > 0)
            {
                args.Add("-frames:v");
                args.Add(options.MaxFrames.ToString(CultureInfo.InvariantCulture));
            }

            if (options.FrameFormat == VideoFrameFormat.Jpeg)
            {
                args.Add("-q:v");
                args.Add(options.JpegQuality.ToString(CultureInfo.InvariantCulture));
            }

            args.Add(outputPattern);

            await RunFFmpegAsync(args, cancellationToken);

            // 抽出されたフレームを順に yield
            var frameFiles = Directory.EnumerateFiles(framesDir, $"frame-*.{formatExt}")
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            int frameIndex = 0;
            foreach (string framePath in frameFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] imageBytes = await File.ReadAllBytesAsync(framePath, cancellationToken);
                var timestamp = TimeSpan.FromSeconds(frameIndex * options.FrameIntervalSeconds);
                yield return new VideoFrameAnalysis(frameIndex, timestamp, imageBytes, Description: null);
                frameIndex++;
            }
        }
        finally
        {
            // 一時ディレクトリをクリーンアップ
            try { Directory.Delete(framesDir, recursive: true); } catch { /* ignore */ }
        }
    }

    private async Task RunFFmpegAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegBinaryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_ffmpegBinaryPath) ?? Environment.CurrentDirectory,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new FFmpegRuntimeException("ffmpeg プロセスの起動に失敗しました。");

        var stderrBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderrBuilder.AppendLine(e.Data); };
        process.OutputDataReceived += (_, _) => { /* stdout を drain（パイプ詰まり防止） */ };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // キャンセル時に ffmpeg を孤児化させない
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw;
        }

        // 非同期出力ハンドラのフラッシュを保証
        try { process.WaitForExit(); } catch { /* ignore */ }

        if (process.ExitCode != 0)
        {
            throw new FFmpegRuntimeException(
                $"ffmpeg の実行に失敗しました (exit {process.ExitCode})。",
                process.ExitCode, stderrBuilder.ToString());
        }
    }
}
