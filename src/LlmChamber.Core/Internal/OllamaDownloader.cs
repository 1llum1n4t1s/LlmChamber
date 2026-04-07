using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace LlmChamber.Internal;

/// <summary>
/// Ollamaバイナリのダウンロードとアトミックな展開。
/// バージョンマーカーで再ダウンロードをスキップする。
/// </summary>
internal sealed class OllamaDownloader
{
    internal const string DefaultOllamaVersion = "0.20.2";
    private const string GithubReleaseUrlTemplate = "https://github.com/ollama/ollama/releases/download/v{0}/{1}";
    private const int DownloadBufferSize = 81920; // 80KB

    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaDownloader> _logger;

    public OllamaDownloader(HttpClient httpClient, ILogger<OllamaDownloader> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Ollamaバイナリをダウンロード・展開する。
    /// バージョンマーカーが一致する場合はスキップ。
    /// </summary>
    /// <returns>Ollamaバイナリのパス。</returns>
    public async Task<string> DownloadAsync(
        string targetDirectory,
        RuntimeVariant variant,
        string? version = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        version ??= DefaultOllamaVersion;
        var os = PlatformInfo.GetCurrentOs();
        var arch = PlatformInfo.GetCurrentArchitecture();
        string executableName = PlatformInfo.GetOllamaExecutableName(os);
        string binaryPath = Path.Combine(targetDirectory, "runtime", executableName);

        // バージョンマーカーチェック（バージョン+バリアント）
        string? existing = FindExistingBinary(targetDirectory, version, variant);
        if (existing is not null)
        {
            _logger.LogDebug("Ollamaバイナリが既に存在します: {Path} (v{Version})", existing, version);
            return existing;
        }

        string downloadFileName = PlatformInfo.GetDownloadFileName(os, arch, variant);
        string downloadUrl = string.Format(GithubReleaseUrlTemplate, version, downloadFileName);

        _logger.LogInformation("Ollamaランタイムをダウンロード中: {Url}", downloadUrl);
        progress?.Report(new DownloadProgress(0, null, null, $"ダウンロード開始: {downloadFileName}"));

        string runtimeDir = Path.Combine(targetDirectory, "runtime");
        // アーカイブ拡張子を保持 + GUID付与で並行ダウンロードの競合防止
        var (_, archiveExt) = PlatformInfo.GetOllamaBinaryInfo(os, arch, variant);
        string uniqueId = Guid.NewGuid().ToString("N")[..8];
        string tmpDownloadPath = Path.Combine(targetDirectory, $"download.{uniqueId}{archiveExt}");
        string tmpExtractDir = Path.Combine(targetDirectory, $"extract.{uniqueId}");

        try
        {
            Directory.CreateDirectory(runtimeDir);

            // ダウンロード
            await DownloadFileAsync(downloadUrl, tmpDownloadPath, progress, cancellationToken);

            // 展開
            progress?.Report(new DownloadProgress(0, null, null, "展開中..."));
            await ExtractAsync(tmpDownloadPath, tmpExtractDir, os, cancellationToken);

            // バイナリをランタイムディレクトリに移動
            MoveExtractedBinary(tmpExtractDir, runtimeDir, executableName, os);

            // バージョンマーカー書き込み（バージョン:バリアント）
            string versionMarkerPath = Path.Combine(targetDirectory, ".version");
            await File.WriteAllTextAsync(versionMarkerPath, $"{version}:{variant}", cancellationToken);

            _logger.LogInformation("Ollamaランタイム v{Version} のインストール完了: {Path}", version, binaryPath);
            progress?.Report(new DownloadProgress(0, null, 100, "インストール完了"));

            return binaryPath;
        }
        finally
        {
            // 一時ファイルの削除
            if (File.Exists(tmpDownloadPath)) File.Delete(tmpDownloadPath);
            if (Directory.Exists(tmpExtractDir)) Directory.Delete(tmpExtractDir, recursive: true);
        }
    }

    /// <summary>バージョン+バリアントマーカーが一致するバイナリが存在すればパスを返す。</summary>
    public string? FindExistingBinary(string targetDirectory, string? version = null, RuntimeVariant? variant = null)
    {
        version ??= DefaultOllamaVersion;
        string versionMarkerPath = Path.Combine(targetDirectory, ".version");
        string executableName = PlatformInfo.GetOllamaExecutableName(PlatformInfo.GetCurrentOs());
        string binaryPath = Path.Combine(targetDirectory, "runtime", executableName);

        if (!File.Exists(versionMarkerPath) || !File.Exists(binaryPath))
            return null;

        string installedMarker = File.ReadAllText(versionMarkerPath).Trim();
        string expectedMarker = variant.HasValue ? $"{version}:{variant.Value}" : version;

        // 旧形式（バージョンのみ）との後方互換性: バリアント指定なしならバージョン部分を完全一致比較
        if (!variant.HasValue)
        {
            // "version:variant" 形式からバージョン部分を抽出して完全一致
            int colonIdx = installedMarker.IndexOf(':');
            string installedVersion = colonIdx >= 0 ? installedMarker[..colonIdx] : installedMarker;
            return string.Equals(installedVersion, version, StringComparison.Ordinal) ? binaryPath : null;
        }

        return installedMarker == expectedMarker ? binaryPath : null;
    }

    /// <summary>ダウンロードURLを構築する（テスト用に公開）。</summary>
    internal static string BuildDownloadUrl(string version, OsPlatform os, CpuArchitecture arch, RuntimeVariant variant)
    {
        string fileName = PlatformInfo.GetDownloadFileName(os, arch, variant);
        return string.Format(GithubReleaseUrlTemplate, version, fileName);
    }

    private async Task DownloadFileAsync(
        string url, string targetPath, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, DownloadBufferSize, useAsync: true);

        byte[] buffer = new byte[DownloadBufferSize];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;

            double? percentage = totalBytes.HasValue ? (double)totalRead / totalBytes.Value * 100.0 : null;
            progress?.Report(new DownloadProgress(totalRead, totalBytes, percentage, "ダウンロード中..."));
        }
    }

    private static async Task ExtractAsync(string archivePath, string extractDir, OsPlatform os, CancellationToken cancellationToken)
    {
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
        Directory.CreateDirectory(extractDir);

        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, extractDir);
        }
        else if (archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            // .tgz (gzipped tar)
            await using var fileStream = File.OpenRead(archivePath);
            await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gzipStream, extractDir, overwriteFiles: true, cancellationToken);
        }
        else if (archivePath.EndsWith(".tar.zst", StringComparison.OrdinalIgnoreCase))
        {
            // .tar.zst — zstd圧縮。標準ライブラリにはzstdがないため外部コマンドで展開
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"--zstd -xf \"{archivePath}\" -C \"{extractDir}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(psi)!;
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new LlmChamberException($"tar展開に失敗しました (exit {process.ExitCode}): {error}");
            }
        }
        else
        {
            throw new LlmChamberException($"未対応のアーカイブ形式: {archivePath}");
        }
    }

    private static void MoveExtractedBinary(string extractDir, string runtimeDir, string executableName, OsPlatform os)
    {
        // 展開されたディレクトリ内からollamaバイナリを検索
        string? foundBinary = Directory.EnumerateFiles(extractDir, executableName, SearchOption.AllDirectories)
            .FirstOrDefault();

        if (foundBinary is null)
        {
            // バイナリ名で見つからない場合、ディレクトリ全体をコピー
            CopyDirectory(extractDir, runtimeDir);
            return;
        }

        // バイナリの親ディレクトリごとコピー（companion DLL/ライブラリを保持）
        string? binaryParent = Path.GetDirectoryName(foundBinary);
        if (binaryParent is not null)
        {
            CopyDirectory(binaryParent, runtimeDir);
        }
        else
        {
            File.Copy(foundBinary, Path.Combine(runtimeDir, executableName), overwrite: true);
        }

        // Linux/macOSでは実行権限を付与
        string finalBinaryPath = Path.Combine(runtimeDir, executableName);
        if (os != OsPlatform.Windows && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(finalBinaryPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
        }
        foreach (string dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
        }
    }
}
