using System.IO;
using System.Net.Http;
using LlmChamber.Speech;

namespace LlmChamber.Internal.Speech;

/// <summary>
/// HuggingFace から Piper voice (.onnx + .onnx.json) をダウンロードする。
/// Voice 命名規則: {locale}-{name}-{quality} 例: "en_US-amy-medium", "ja_JP-takumi-medium"
/// </summary>
internal sealed class PiperVoiceDownloader
{
    private const string HuggingFaceUrlTemplate =
        "https://huggingface.co/rhasspy/piper-voices/resolve/main/{0}";

    private readonly HttpClient _httpClient;

    public PiperVoiceDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// 指定 voice の .onnx ファイルパスを返す。必要なら .onnx と .onnx.json をダウンロードする。
    /// </summary>
    public async Task<string> EnsureVoiceAsync(
        string voicesDirectory,
        string voiceName,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool allowDownload = true)
    {
        var (lang, quality) = ParseVoiceName(voiceName);
        string fullVoicesDirectory = Path.GetFullPath(voicesDirectory);

        string onnxFileName = $"{voiceName}.onnx";
        string jsonFileName = $"{voiceName}.onnx.json";
        string onnxPath = GetContainedPath(fullVoicesDirectory, onnxFileName);
        string jsonPath = GetContainedPath(fullVoicesDirectory, jsonFileName);

        if (File.Exists(onnxPath) && File.Exists(jsonPath))
        {
            return onnxPath;
        }

        if (!allowDownload)
        {
            throw new SpeechModelNotFoundException(
                voiceName,
                $"Piper voice がキャッシュに見つかりません: {voiceName}。" +
                "SpeechOptions.AutoDownload を有効にするか、PiperVoicesDirectory に voice ファイルを配置してください。");
        }

        Directory.CreateDirectory(fullVoicesDirectory);

        // HuggingFace 上のパス: {lang_short}/{lang}/{name}/{quality}/{voiceName}.onnx
        // 例: en/en_US/amy/medium/en_US-amy-medium.onnx
        string hfBase = BuildHuggingFaceVoicePath(voiceName, lang, quality);

        if (!File.Exists(onnxPath))
        {
            await DownloadOneAsync(
                string.Format(System.Globalization.CultureInfo.InvariantCulture, HuggingFaceUrlTemplate, $"{hfBase}/{onnxFileName}"),
                onnxPath, $"Voice {voiceName} (.onnx)", progress, cancellationToken);
        }

        if (!File.Exists(jsonPath))
        {
            await DownloadOneAsync(
                string.Format(System.Globalization.CultureInfo.InvariantCulture, HuggingFaceUrlTemplate, $"{hfBase}/{jsonFileName}"),
                jsonPath, $"Voice {voiceName} (.onnx.json)", progress, cancellationToken);
        }

        return onnxPath;
    }

    /// <summary>"en_US-amy-medium" → ("en_US", "medium")。</summary>
    internal static (string Language, string Quality) ParseVoiceName(string voiceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceName);
        if (voiceName.Contains("..", StringComparison.Ordinal) ||
            voiceName.IndexOfAny(['/', '\\']) >= 0 ||
            voiceName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                $"Piper voice 名にパスとして解釈される文字は使用できません: '{voiceName}'。",
                nameof(voiceName));
        }

        // 形式: {locale}-{name}-{quality}
        // 注意: name 自体にハイフンを含むこともある (例: "en_GB-southern_english_female-low")
        int lastDash = voiceName.LastIndexOf('-');
        if (lastDash <= 0)
            throw new ArgumentException(
                $"Piper voice 名の形式が不正です: '{voiceName}'。期待形式: 'locale-name-quality' (例: 'en_US-amy-medium')",
                nameof(voiceName));

        string quality = voiceName[(lastDash + 1)..];
        string rest = voiceName[..lastDash];

        int firstDash = rest.IndexOf('-');
        if (firstDash <= 0)
            throw new ArgumentException(
                $"Piper voice 名の形式が不正です: '{voiceName}'。期待形式: 'locale-name-quality' (例: 'en_US-amy-medium')",
                nameof(voiceName));

        string language = rest[..firstDash];
        string speakerName = rest[(firstDash + 1)..];
        if (speakerName.Length == 0 || quality.Length == 0)
        {
            throw new ArgumentException(
                $"Piper voice 名の形式が不正です: '{voiceName}'。期待形式: 'locale-name-quality' (例: 'en_US-amy-medium')",
                nameof(voiceName));
        }
        return (language, quality);
    }

    private static string GetContainedPath(string directory, string fileName)
    {
        string fullDirectory = Path.GetFullPath(directory);
        string fullPath = Path.GetFullPath(Path.Combine(fullDirectory, fileName));
        string directoryPrefix = Path.EndsInDirectorySeparator(fullDirectory)
            ? fullDirectory
            : fullDirectory + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(directoryPrefix, comparison))
        {
            throw new ArgumentException("Piper voice の保存先が voice ディレクトリ外を指しています。", nameof(fileName));
        }

        return fullPath;
    }

    /// <summary>HuggingFace 上の voice ディレクトリパス: en/en_US/amy/medium/</summary>
    internal static string BuildHuggingFaceVoicePath(string voiceName, string language, string quality)
    {
        // 例: en_US-amy-medium → en/en_US/amy/medium
        // 言語コード短縮 (en_US → en)
        string shortLang = language.Length >= 2 ? language[..2] : language;

        // voice 名から locale と quality を除いた部分が voice 個体名
        // "en_US-amy-medium" → "amy"
        string remaining = voiceName.Substring(language.Length + 1);
        int lastDash = remaining.LastIndexOf('-');
        string speakerName = lastDash > 0 ? remaining[..lastDash] : remaining;

        return $"{shortLang}/{language}/{speakerName}/{quality}";
    }

    private async Task DownloadOneAsync(
        string url, string targetPath, string label,
        IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        string tmpPath = targetPath + ".tmp";

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var fileStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                byte[] buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalRead += bytesRead;
                    double? pct = totalBytes.HasValue ? (double)totalRead / totalBytes.Value * 100.0 : null;
                    progress?.Report(new DownloadProgress(totalRead, totalBytes, pct, $"{label} ダウンロード中..."));
                }
            }

            // アトミックリネーム（File.Delete→File.Move の TOCTOU を避ける）
            File.Move(tmpPath, targetPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tmpPath))
            {
                try { File.Delete(tmpPath); } catch { /* ignore */ }
            }
            throw;
        }
    }
}
