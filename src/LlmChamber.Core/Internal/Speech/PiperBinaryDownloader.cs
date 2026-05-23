using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using System.Net.Http;
using LlmChamber.Speech;
using SuperLightLogger;

namespace LlmChamber.Internal.Speech;

/// <summary>
/// Piper TTS バイナリ（rhasspy/piper の公式リリース）を自動 DL・展開する。
/// 全プラットフォーム対応（Windows AMD64 / Linux x86_64 / Linux Arm64 / macOS x64 / macOS Arm64）。
/// </summary>
internal sealed class PiperBinaryDownloader
{
    internal const string DefaultPiperVersion = "2023.11.14-2";
    private const string PiperReleaseUrlTemplate =
        "https://github.com/rhasspy/piper/releases/download/{0}/{1}";

    private static readonly ILog _logger = LogManager.GetLogger<PiperBinaryDownloader>();
    private readonly HttpClient _httpClient;

    public PiperBinaryDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>Piperバイナリの絶対パスを返す。必要ならダウンロード・展開する。</summary>
    public async Task<string> EnsureBinaryAsync(
        string targetDirectory,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var os = PlatformInfo.GetCurrentOs();
        var arch = PlatformInfo.GetCurrentArchitecture();

        string binaryName = GetExecutableName(os);
        string piperDir = Path.Combine(targetDirectory, "piper");
        string binaryPath = Path.Combine(piperDir, binaryName);
        string versionMarkerPath = Path.Combine(piperDir, ".version");

        // 既存バイナリチェック
        if (File.Exists(binaryPath) && File.Exists(versionMarkerPath))
        {
            string installedVersion = (await File.ReadAllTextAsync(versionMarkerPath, cancellationToken)).Trim();
            if (installedVersion == DefaultPiperVersion)
            {
                _logger.Debug($"Piperバイナリが既に存在します: {binaryPath} ({DefaultPiperVersion})");
                return binaryPath;
            }
        }

        (string assetName, string archiveExt) = GetReleaseAsset(os, arch)
            ?? throw new SpeechBinaryNotFoundException(
                binaryName,
                $"Piperバイナリの自動ダウンロードはこのプラットフォーム({os}/{arch})では未対応です。" +
                "SpeechOptions.PiperBinaryPath で piper への絶対パスを明示的に設定してください。");

        Directory.CreateDirectory(piperDir);

        string downloadUrl = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            PiperReleaseUrlTemplate, DefaultPiperVersion, assetName);

        _logger.Info($"Piperバイナリをダウンロード中: {downloadUrl}");
        progress?.Report(new DownloadProgress(0, null, null, $"ダウンロード開始: {assetName}"));

        string uniqueId = Guid.NewGuid().ToString("N")[..8];
        string archivePath = Path.Combine(piperDir, $"download.{uniqueId}{archiveExt}");
        string extractDir = Path.Combine(piperDir, $"extract.{uniqueId}");

        try
        {
            await DownloadFileAsync(downloadUrl, archivePath, progress, cancellationToken);

            progress?.Report(new DownloadProgress(0, null, null, "展開中..."));
            Directory.CreateDirectory(extractDir);
            await ExtractAsync(archivePath, extractDir, archiveExt, cancellationToken);

            MergeExtractedContent(extractDir, piperDir, binaryName, os);

            await File.WriteAllTextAsync(versionMarkerPath, DefaultPiperVersion, cancellationToken);
            _logger.Info($"Piperバイナリのインストール完了: {binaryPath}");
            return binaryPath;
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
        }
    }

    /// <summary>プラットフォーム別のリリースアセット名と拡張子。未対応はnull。</summary>
    internal static (string FileName, string Extension)? GetReleaseAsset(OsPlatform os, CpuArchitecture arch)
    {
        return (os, arch) switch
        {
            (OsPlatform.Windows, CpuArchitecture.X64) => ("piper_windows_amd64.zip", ".zip"),
            (OsPlatform.Linux, CpuArchitecture.X64) => ("piper_linux_x86_64.tar.gz", ".tar.gz"),
            (OsPlatform.Linux, CpuArchitecture.Arm64) => ("piper_linux_aarch64.tar.gz", ".tar.gz"),
            (OsPlatform.MacOS, CpuArchitecture.X64) => ("piper_macos_x64.tar.gz", ".tar.gz"),
            (OsPlatform.MacOS, CpuArchitecture.Arm64) => ("piper_macos_aarch64.tar.gz", ".tar.gz"),
            _ => null,
        };
    }

    internal static string GetExecutableName(OsPlatform os)
        => os == OsPlatform.Windows ? "piper.exe" : "piper";

    private static async Task ExtractAsync(string archivePath, string extractDir, string archiveExt, CancellationToken cancellationToken)
    {
        if (archiveExt.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, extractDir, overwriteFiles: true), cancellationToken);
        }
        else if (archiveExt.Equals(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            await using var fileStream = File.OpenRead(archivePath);
            await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gzipStream, extractDir, overwriteFiles: true, cancellationToken);
        }
        else
        {
            throw new SpeechException($"未対応のアーカイブ形式: {archiveExt}");
        }
    }

    /// <summary>展開後ディレクトリから piperDir へバイナリと同梱ライブラリを集約する。</summary>
    private static void MergeExtractedContent(string extractDir, string piperDir, string binaryName, OsPlatform os)
    {
        // 公式リリースは "piper/" サブフォルダに展開される
        string? piperSubDir = Directory.EnumerateDirectories(extractDir, "piper", SearchOption.AllDirectories).FirstOrDefault()
            ?? extractDir;

        foreach (string file in Directory.EnumerateFiles(piperSubDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(piperSubDir, file);
            string targetPath = Path.Combine(piperDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }

        string expectedPath = Path.Combine(piperDir, binaryName);
        if (os != OsPlatform.Windows && !OperatingSystem.IsWindows() && File.Exists(expectedPath))
        {
            File.SetUnixFileMode(expectedPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
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
            progress?.Report(new DownloadProgress(totalRead, totalBytes, pct, "Piperバイナリをダウンロード中..."));
        }
    }
}
