using System.IO;
using System.Net.Http;
using LlmChamber.Speech;
using SuperLightLogger;

namespace LlmChamber.Internal.Speech;

/// <summary>
/// HuggingFace から Whisper ggml モデル（.bin）をダウンロードする。
/// </summary>
internal sealed class WhisperModelDownloader
{
    private const string HuggingFaceUrlTemplate =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{0}";

    private static readonly ILog _logger = LogManager.GetLogger<WhisperModelDownloader>();
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
        CancellationToken cancellationToken = default)
    {
        string fileName = GetModelFileName(modelSize);
        Directory.CreateDirectory(modelsDirectory);
        string modelPath = Path.Combine(modelsDirectory, fileName);

        if (File.Exists(modelPath))
        {
            _logger.Debug($"Whisperモデルが既に存在します: {modelPath}");
            return modelPath;
        }

        string url = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            HuggingFaceUrlTemplate, fileName);

        _logger.Info($"Whisperモデルをダウンロード中: {url}");
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

            _logger.Info($"Whisperモデルのダウンロード完了: {modelPath}");
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
