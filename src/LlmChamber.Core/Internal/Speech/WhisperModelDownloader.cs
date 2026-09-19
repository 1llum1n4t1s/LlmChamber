using System.IO;
using System.Net.Http;
using LlmChamber.Speech;

namespace LlmChamber.Internal.Speech;

/// <summary>
/// HuggingFace から Whisper ggml モデル（.bin）をダウンロードする。
/// </summary>
internal sealed class WhisperModelDownloader
{
    private const string HuggingFaceUrlTemplate =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{0}";

    private readonly HttpClient _httpClient;

    public WhisperModelDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// 指定モデルサイズの ggml モデルファイルパスを返す。必要ならダウンロードする。
    /// </summary>
    public async Task<string> EnsureModelAsync(
        string modelsDirectory,
        WhisperModelSize modelSize,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool allowDownload = true)
    {
        string fileName = GetModelFileName(modelSize);
        Directory.CreateDirectory(modelsDirectory);
        string modelPath = Path.Combine(modelsDirectory, fileName);

        if (File.Exists(modelPath))
        {
            return modelPath;
        }

        if (!allowDownload)
        {
            throw new SpeechModelNotFoundException(
                modelSize.ToString(),
                $"Whisperモデルがキャッシュに見つかりません: {modelPath}。" +
                "SpeechOptions.AutoDownload を有効にするか、WhisperModelPath を指定してください。");
        }

        string url = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            HuggingFaceUrlTemplate, fileName);

        progress?.Report(new DownloadProgress(0, null, null, $"モデルダウンロード開始: {fileName}"));

        // アトミック書き込み: .tmp に書いて成功時に rename
        string tmpPath = modelPath + ".tmp";

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
                    progress?.Report(new DownloadProgress(totalRead, totalBytes, pct, $"Whisperモデル {fileName} ダウンロード中..."));
                }
            }

            // アトミックリネーム（File.Delete→File.Move の TOCTOU を避ける）
            File.Move(tmpPath, modelPath, overwrite: true);

            return modelPath;
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

    /// <summary>モデルサイズ → ggml ファイル名。</summary>
    internal static string GetModelFileName(WhisperModelSize size) => size switch
    {
        WhisperModelSize.Tiny => "ggml-tiny.bin",
        WhisperModelSize.Base => "ggml-base.bin",
        WhisperModelSize.Small => "ggml-small.bin",
        WhisperModelSize.Medium => "ggml-medium.bin",
        WhisperModelSize.Large => "ggml-large-v3.bin",
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "未対応のモデルサイズです。"),
    };
}
