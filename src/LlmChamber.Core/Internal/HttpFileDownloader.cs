using System.IO;

namespace LlmChamber.Internal;

/// <summary>HTTP 応答をファイルへ転送する。</summary>
internal static class HttpFileDownloader
{
    private const int BufferSize = 81920;

    public static async Task DownloadAsync(
        Stream contentStream, long? totalBytes, string targetPath, IProgress<DownloadProgress>? progress,
        string progressMessage, CancellationToken cancellationToken)
    {
        await using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            byte[] buffer = new byte[BufferSize];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;
                double? percentage = totalBytes.HasValue ? (double)totalRead / totalBytes.Value * 100.0 : null;
                progress?.Report(new DownloadProgress(totalRead, totalBytes, percentage, progressMessage));
            }
        }
    }
}
