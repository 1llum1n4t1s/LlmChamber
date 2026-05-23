using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using LlmChamber.Media;
using SuperLightLogger;

namespace LlmChamber.Internal.Media;

/// <summary>
/// FFmpeg バイナリ（BtbN/FFmpeg-Builds の公式 latest ビルド）を自動 DL・展開する。
/// Windows x64 / Linux x64・arm64 対応。macOS は homebrew 推奨。
/// </summary>
internal sealed class FFmpegBinaryDownloader
{
    private const string FFmpegLatestBaseUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/";
    /// <summary>マーカーファイルに書き込む固定値。autobuild は日付が変わるため特定バージョンの代わりに使う。</summary>
    internal const string MarkerValue = "latest";

    private static readonly ILog _logger = LogManager.GetLogger<FFmpegBinaryDownloader>();
    private readonly HttpClient _httpClient;

    public FFmpegBinaryDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>FFmpegバイナリの絶対パスを返す。必要ならダウンロード・展開する。</summary>
    public async Task<string> EnsureBinaryAsync(
        string targetDirectory,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var os = PlatformInfo.GetCurrentOs();
        var arch = PlatformInfo.GetCurrentArchitecture();
        string executableName = GetExecutableName(os);
        string ffmpegDir = Path.Combine(targetDirectory, "ffmpeg");
        string binaryPath = Path.Combine(ffmpegDir, executableName);
        string markerPath = Path.Combine(ffmpegDir, ".version");

        // マーカー内容を Whisper/Piper と同じパターンで検証する
        // （File.Exists だけでは破損ファイルや古いマーカーを検出できない）
        if (File.Exists(binaryPath) && File.Exists(markerPath))
        {
            string installedMarker = (await File.ReadAllTextAsync(markerPath, cancellationToken)).Trim();
            if (installedMarker == MarkerValue)
            {
                _logger.Debug($"FFmpegバイナリが既に存在します: {binaryPath}");
                return binaryPath;
            }
            _logger.Info($"FFmpegマーカーが期待値と不一致 ('{installedMarker}' != '{MarkerValue}')。再ダウンロードします。");
        }

        (string assetName, string archiveExt) = GetReleaseAsset(os, arch)
            ?? throw new FFmpegBinaryNotFoundException(
                $"FFmpegの自動ダウンロードはこのプラットフォーム({os}/{arch})では未対応です。" +
                "MediaOptions.FFmpegBinaryPath で ffmpeg への絶対パスを明示的に設定してください。" +
                " (macOSなら `brew install ffmpeg` 推奨)");

        Directory.CreateDirectory(ffmpegDir);
        string downloadUrl = FFmpegLatestBaseUrl + assetName;

        _logger.Info($"FFmpegをダウンロード中: {downloadUrl}");
        progress?.Report(new DownloadProgress(0, null, null, $"ダウンロード開始: {assetName}"));

        string uniqueId = Guid.NewGuid().ToString("N")[..8];
        string archivePath = Path.Combine(ffmpegDir, $"download.{uniqueId}{archiveExt}");
        string extractDir = Path.Combine(ffmpegDir, $"extract.{uniqueId}");

        try
        {
            await DownloadFileAsync(downloadUrl, archivePath, progress, cancellationToken);

            progress?.Report(new DownloadProgress(0, null, null, "展開中..."));
            Directory.CreateDirectory(extractDir);
            await ExtractAsync(archivePath, extractDir, archiveExt, cancellationToken);

            // 展開済み構造: ffmpeg-master-latest-XXX/bin/{ffmpeg,ffprobe}[.exe]
            MergeExtractedContent(extractDir, ffmpegDir, executableName, os);

            // FFmpegは autobuild で日付が変わるので固定マーカー値を使用
            await File.WriteAllTextAsync(markerPath, MarkerValue, cancellationToken);
            _logger.Info($"FFmpegのインストール完了: {binaryPath}");
            return binaryPath;
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
        }
    }

    /// <summary>プラットフォーム別のアセット名と拡張子。未対応はnull。</summary>
    internal static (string FileName, string Extension)? GetReleaseAsset(OsPlatform os, CpuArchitecture arch)
    {
        return (os, arch) switch
        {
            (OsPlatform.Windows, CpuArchitecture.X64) => ("ffmpeg-master-latest-win64-gpl.zip", ".zip"),
            (OsPlatform.Linux, CpuArchitecture.X64) => ("ffmpeg-master-latest-linux64-gpl.tar.xz", ".tar.xz"),
            (OsPlatform.Linux, CpuArchitecture.Arm64) => ("ffmpeg-master-latest-linuxarm64-gpl.tar.xz", ".tar.xz"),
            // macOS: BtbN は提供していない。homebrew 経由を推奨
            _ => null,
        };
    }

    internal static string GetExecutableName(OsPlatform os)
        => os == OsPlatform.Windows ? "ffmpeg.exe" : "ffmpeg";

    private static async Task ExtractAsync(string archivePath, string extractDir, string archiveExt, CancellationToken cancellationToken)
    {
        if (archiveExt.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, extractDir, overwriteFiles: true), cancellationToken);
        }
        else if (archiveExt.Equals(".tar.xz", StringComparison.OrdinalIgnoreCase))
        {
            // .tar.xz は外部 tar コマンドで展開（Linux/macOSなら標準）
            var psi = new ProcessStartInfo
            {
                FileName = "tar",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-xJf");
            psi.ArgumentList.Add(archivePath);
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(extractDir);

            using var process = Process.Start(psi)
                ?? throw new MediaException("tar プロセスの起動に失敗しました。");

            // stderr を WaitForExitAsync 前に非同期で吸い出す。
            // 後読みだと tar が大量にエラー出力した時に pipe バッファ満杯でデッドロックする
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                throw;
            }

            if (process.ExitCode != 0)
            {
                string err = await stderrTask;
                throw new MediaException($"tar 展開に失敗しました (exit {process.ExitCode}): {err}");
            }
        }
        else
        {
            throw new MediaException($"未対応のアーカイブ形式: {archiveExt}");
        }
    }

    /// <summary>展開ディレクトリの bin/ 配下を ffmpegDir 直下にフラット化する。</summary>
    private static void MergeExtractedContent(string extractDir, string ffmpegDir, string executableName, OsPlatform os)
    {
        // bin ディレクトリを探す
        string? binDir = Directory.EnumerateDirectories(extractDir, "bin", SearchOption.AllDirectories).FirstOrDefault();
        string sourceDir = binDir ?? extractDir;

        foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(file);
            string targetPath = Path.Combine(ffmpegDir, fileName);
            File.Copy(file, targetPath, overwrite: true);
        }

        string expectedPath = Path.Combine(ffmpegDir, executableName);
        if (os != OsPlatform.Windows && !OperatingSystem.IsWindows() && File.Exists(expectedPath))
        {
            // ffmpeg / ffprobe 両方に実行権限を付与
            foreach (string file in Directory.EnumerateFiles(ffmpegDir))
            {
                string fileName = Path.GetFileName(file);
                if (fileName is "ffmpeg" or "ffprobe" or "ffplay")
                {
                    File.SetUnixFileMode(file,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
            }
        }
    }

    private async Task DownloadFileAsync(
        string url, string targetPath, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;
            double? pct = totalBytes.HasValue ? (double)totalRead / totalBytes.Value * 100.0 : null;
            progress?.Report(new DownloadProgress(totalRead, totalBytes, pct, "FFmpegをダウンロード中..."));
        }
    }
}
