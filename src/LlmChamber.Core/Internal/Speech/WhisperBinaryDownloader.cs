using System.IO;
using System.IO.Compression;
using System.Net.Http;
using LlmChamber.Speech;
using SuperLightLogger;

namespace LlmChamber.Internal.Speech;

/// <summary>
/// whisper.cpp プリビルドバイナリ（whisper-cli）をダウンロード・展開する。
/// Windows AMD64 は ggerganov/whisper.cpp の公式リリースから取得。
/// 他プラットフォームは <see cref="SpeechBinaryNotFoundException"/> を投げる。
/// </summary>
internal sealed class WhisperBinaryDownloader
{
    internal const string DefaultWhisperVersion = "v1.7.4";
    private const string WhisperReleaseUrlTemplate =
        "https://github.com/ggerganov/whisper.cpp/releases/download/{0}/{1}";

    private static readonly ILog _logger = LogManager.GetLogger<WhisperBinaryDownloader>();
    private readonly HttpClient _httpClient;

    public WhisperBinaryDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// whisper-cli バイナリの絶対パスを返す。必要ならダウンロード・展開する。
    /// </summary>
    public async Task<string> EnsureBinaryAsync(
        string targetDirectory,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var os = PlatformInfo.GetCurrentOs();
        var arch = PlatformInfo.GetCurrentArchitecture();

        string binaryName = GetExecutableName(os);
        string whisperDir = Path.Combine(targetDirectory, "whisper");
        string binaryPath = Path.Combine(whisperDir, binaryName);
        string versionMarkerPath = Path.Combine(whisperDir, ".version");

        // 既存バイナリチェック
        if (File.Exists(binaryPath) && File.Exists(versionMarkerPath))
        {
            string installedVersion = (await File.ReadAllTextAsync(versionMarkerPath, cancellationToken)).Trim();
            if (installedVersion == DefaultWhisperVersion)
            {
                _logger.Debug($"Whisperバイナリが既に存在します: {binaryPath} ({DefaultWhisperVersion})");
                return binaryPath;
            }
        }

        string? archiveName = GetReleaseAssetName(os, arch);
        if (archiveName is null)
        {
            throw new SpeechBinaryNotFoundException(
                binaryName,
                $"Whisperバイナリの自動ダウンロードはこのプラットフォーム({os}/{arch})では未対応です。" +
                "SpeechOptions.WhisperBinaryPath で whisper-cli への絶対パスを明示的に設定してください。" +
                " (whisper.cpp を `make` でビルドしたバイナリへのパスを指定できます)");
        }

        Directory.CreateDirectory(whisperDir);

        string downloadUrl = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            WhisperReleaseUrlTemplate, DefaultWhisperVersion, archiveName);

        _logger.Info($"Whisperバイナリをダウンロード中: {downloadUrl}");
        progress?.Report(new DownloadProgress(0, null, null, $"ダウンロード開始: {archiveName}"));

        string uniqueId = Guid.NewGuid().ToString("N")[..8];
        string archivePath = Path.Combine(whisperDir, $"download.{uniqueId}.zip");
        string extractDir = Path.Combine(whisperDir, $"extract.{uniqueId}");

        try
        {
            await DownloadFileAsync(downloadUrl, archivePath, progress, cancellationToken);

            progress?.Report(new DownloadProgress(0, null, null, "展開中..."));
            Directory.CreateDirectory(extractDir);
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, extractDir, overwriteFiles: true), cancellationToken);

            // バイナリ + 同梱DLLを whisperDir に展開
            MergeExtractedContent(extractDir, whisperDir, binaryName, os);

            await File.WriteAllTextAsync(versionMarkerPath, DefaultWhisperVersion, cancellationToken);
            _logger.Info($"Whisperバイナリのインストール完了: {binaryPath}");
            return binaryPath;
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
        }
    }

    /// <summary>プラットフォーム別のリリースアセット名（拡張子込み）。未対応はnull。</summary>
    internal static string? GetReleaseAssetName(OsPlatform os, CpuArchitecture arch)
    {
        return (os, arch) switch
        {
            (OsPlatform.Windows, CpuArchitecture.X64) => "whisper-bin-x64.zip",
            // Linux/macOS は公式リリースに含まれないため未対応
            _ => null,
        };
    }

    internal static string GetExecutableName(OsPlatform os)
    {
        // whisper.cpp v1.7+ は whisper-cli が新ファイル名（main から rename）
        // 旧バージョン互換性のために main も検索対象にする
        return os == OsPlatform.Windows ? "whisper-cli.exe" : "whisper-cli";
    }

    /// <summary>展開ディレクトリから whisperDir へバイナリと同梱 DLL を集約する。</summary>
    private static void MergeExtractedContent(string extractDir, string whisperDir, string expectedBinaryName, OsPlatform os)
    {
        // 展開後のディレクトリ構造は zip により異なるため、全ファイルをコピー
        // 例: whisper-bin-x64.zip → Release/{whisper-cli.exe, ggml-*.dll, whisper.dll, ...}
        // 同名ファイル衝突（サブディレクトリの重複）は最初に見つかったものを採用し、
        // それ以降は警告ログを出してスキップする（後勝ちで誤った DLL が上書きされるのを防ぐ）
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(extractDir, "*", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(file);
            if (!seenFiles.Add(fileName))
            {
                _logger.Warn($"Whisper 展開時に同名ファイル '{fileName}' を検出。最初に見つかったものを採用します: {file}");
                continue;
            }
            string targetPath = Path.Combine(whisperDir, fileName);
            File.Copy(file, targetPath, overwrite: true);
        }

        // whisper-cli が無いがmainがある場合は rename
        string expectedPath = Path.Combine(whisperDir, expectedBinaryName);
        if (!File.Exists(expectedPath))
        {
            string mainName = os == OsPlatform.Windows ? "main.exe" : "main";
            string mainPath = Path.Combine(whisperDir, mainName);
            if (File.Exists(mainPath))
            {
                File.Move(mainPath, expectedPath, overwrite: true);
            }
        }

        // Linux/macOSで実行権限を付与
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
            progress?.Report(new DownloadProgress(totalRead, totalBytes, pct, "Whisperバイナリをダウンロード中..."));
        }
    }
}
