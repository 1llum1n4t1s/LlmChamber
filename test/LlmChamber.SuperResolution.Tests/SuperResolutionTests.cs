using System.Net;
using System.Security.Cryptography;
using LlmChamber.SuperResolution;
using Xunit;

namespace LlmChamber.SuperResolution.Tests;

public sealed class SuperResolutionTests
{
    private static SuperResolutionOptions Options => new() { CacheDirectory = Path.GetTempPath() };

    [Fact]
    public async Task RejectsInvalidImageAndOutputLimitBeforeDownloading()
    {
        using var session = new SuperResolutionSession(new() { CacheDirectory = Path.GetTempPath(), MaximumOutputPixels = 15 });
        await Assert.ThrowsAsync<ArgumentException>(() => session.UpscaleAsync(new byte[4], 1, 1, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => session.UpscaleAsync(new byte[4], int.MaxValue, int.MaxValue, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Null(session.ExecutionProvider);
    }

    [Fact]
    public async Task AlreadyCancelledDoesNotStartDownload()
    {
        using var session = new SuperResolutionSession(Options);
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.UpscaleAsync(new byte[4], 1, 1, cancellationToken: source.Token));
        Assert.Null(session.ExecutionProvider);
    }

    [Theory]
    [InlineData(SuperResolutionModel.RealEsrgan, 67051616L)]
    [InlineData(SuperResolutionModel.RealHatGan, 161884767L)]
    public void ReportsModelDownloadSize(SuperResolutionModel model, long expected)
    {
        using var session = new SuperResolutionSession(new() { CacheDirectory = Path.GetTempPath(), Model = model });
        Assert.Equal(expected, session.ModelDownloadSizeBytes);
    }

    [Fact]
    public async Task EnsureModelAlreadyCancelledDoesNotCreateCacheOrInitializeInference()
    {
        string directory = Path.Combine(Path.GetTempPath(), "llmchamber-superresolution-" + Guid.NewGuid().ToString("N"));
        using var session = new SuperResolutionSession(new() { CacheDirectory = directory });
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.EnsureModelAsync(cancellationToken: source.Token));
        Assert.False(Directory.Exists(directory));
        Assert.Null(session.ExecutionProvider);
    }

    [Fact]
    public async Task EnsureModelAfterDisposeDoesNotCreateCache()
    {
        string directory = Path.Combine(Path.GetTempPath(), "llmchamber-superresolution-" + Guid.NewGuid().ToString("N"));
        var session = new SuperResolutionSession(new() { CacheDirectory = directory });
        session.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.EnsureModelAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(directory));
        Assert.Null(session.ExecutionProvider);
    }

    [Theory]
    [InlineData(-1, 4, 1)]
    [InlineData(-4, 4, 2)]
    [InlineData(4, 4, 2)]
    [InlineData(100, 1, 0)]
    public void ImageEdgesReflect(int coordinate, int length, int expected) => Assert.Equal(expected, SuperResolutionSession.Reflect(coordinate, length));

    [Fact]
    public void AlphaIsBilinearAndClampedAtEdges()
    {
        byte[] rgba = [0,0,0,0, 0,0,0,255];
        Assert.Equal(0, SuperResolutionSession.Alpha(rgba, 2, 1, 0, 0));
        Assert.Equal(96, SuperResolutionSession.Alpha(rgba, 2, 1, 3, 0));
        Assert.Equal(255, SuperResolutionSession.Alpha(rgba, 2, 1, 7, 0));
    }

    [Fact]
    public async Task CorruptCacheIsReplacedAndConcurrentRequestsReuseVerifiedModel()
    {
        string dir = Path.Combine(Path.GetTempPath(), "llmchamber-superresolution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            byte[] data = [1, 2, 3, 4];
            var model = new ModelInfo("model.onnx", "https://example.test/model", data.Length, Convert.ToHexString(SHA256.HashData(data)));
            string path = Path.Combine(dir, model.FileName);
            await File.WriteAllBytesAsync(path, [9, 9, 9, 9], TestContext.Current.CancellationToken);
            var handler = new ResponseHandler(data);
            using var client = new HttpClient(handler);
            string[] results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => ModelCache.GetAsync(dir, model, null, TestContext.Current.CancellationToken, client)));
            Assert.All(results, result => Assert.Equal(path, result));
            Assert.Equal(1, handler.Count);
            Assert.Equal(data, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task InvalidDownloadDoesNotReplaceExistingCache()
    {
        string dir = Path.Combine(Path.GetTempPath(), "llmchamber-superresolution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            byte[] expected = [1, 2, 3];
            byte[] corrupt = [9, 9, 9];
            var model = new ModelInfo("model.onnx", "https://example.test/model", expected.Length, Convert.ToHexString(SHA256.HashData(expected)));
            string path = Path.Combine(dir, model.FileName);
            await File.WriteAllBytesAsync(path, corrupt, TestContext.Current.CancellationToken);
            using var client = new HttpClient(new ResponseHandler(corrupt));
            await Assert.ThrowsAsync<InvalidDataException>(() => ModelCache.GetAsync(dir, model, null, TestContext.Current.CancellationToken, client));
            Assert.Equal(corrupt, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task CancelledDownloadLeavesNoModelOrTemporaryFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "llmchamber-superresolution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            byte[] data = [1, 2, 3, 4];
            var model = new ModelInfo("model.onnx", "https://example.test/model", data.Length, Convert.ToHexString(SHA256.HashData(data)));
            using var client = new HttpClient(new ResponseHandler(data));
            using var source = new CancellationTokenSource();
            var progress = new CancelProgress(source);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ModelCache.GetAsync(dir, model, progress, source.Token, client));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class CancelProgress(CancellationTokenSource source) : IProgress<SuperResolutionProgress>
    {
        public void Report(SuperResolutionProgress value) => source.Cancel();
    }

    private sealed class ResponseHandler(byte[] data) : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Count);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
        }
    }
}

