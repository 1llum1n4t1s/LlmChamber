using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace LlmChamber.SuperResolution;

internal sealed record ModelInfo(string FileName, string Url, long Size, string Sha256)
{
    internal static ModelInfo Get(SuperResolutionModel model) => model switch
    {
        SuperResolutionModel.RealEsrgan => new("realesrgan_x4plus.onnx", "https://huggingface.co/anakhiu/realesrgan-onnx/resolve/e2a8b86b2d947b18e024c3975aa13eadcd7751e6/realesrgan_x4plus.onnx", 67051616, "1c0fc21d1d6d7df7f39c1fa23c73138a87a01ea0b3ac56ebd0ea943e62e12800"),
        SuperResolutionModel.RealHatGan => new("real_hat_gan_srx4_fp32.onnx", "https://huggingface.co/HGhog/DeepSR/resolve/c5024d35e1d02be804c6bc8aaba2bfeccc8915f0/models/real_hat_gan_srx4_fp32.onnx", 161884767, "1987067ccf26353c8c4081fd2610347d4a223c9493c57b81777ee6e329a9739a"),
        _ => throw new ArgumentOutOfRangeException(nameof(model))
    };
}

internal static class ModelCache
{
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    internal static async Task<string> GetAsync(string directory, ModelInfo model, IProgress<SuperResolutionProgress>? progress, CancellationToken token, HttpClient? client = null)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, model.FileName);
        var gate = Locks.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (await IsValidAsync(path, model, token).ConfigureAwait(false)) return path;
            string temporary = Path.Combine(directory, $".{model.FileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                using var response = await (client ?? Client).GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    byte[] buffer = new byte[81920];
                    long count = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                    {
                        count += read;
                        if (count > model.Size) throw new InvalidDataException("モデルのサイズが一致しません。");
                        await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        progress?.Report(new(SuperResolutionStage.DownloadingModel, Math.Min(1, (double)count / model.Size), 0, 0));
                    }
                }
                if (!await IsValidAsync(temporary, model, token).ConfigureAwait(false)) throw new InvalidDataException("モデルのサイズまたはハッシュが一致しません。");
                token.ThrowIfCancellationRequested();
                File.Move(temporary, path, true);
                return path;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { gate.Release(); }
    }

    internal static async Task<bool> IsValidAsync(string path, ModelInfo model, CancellationToken token)
    {
        if (!File.Exists(path)) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (stream.Length != model.Size) return false;
        byte[] hash = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(model.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
