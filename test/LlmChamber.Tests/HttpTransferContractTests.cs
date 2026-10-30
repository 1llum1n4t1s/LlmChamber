using System.Net;
using LlmChamber.Internal.Speech;
using LlmChamber.Speech;
using Xunit;

namespace LlmChamber.Tests;

public sealed class HttpTransferContractTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "transfer-contract-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData(5L)]
    [InlineData(null)]
    [InlineData(0L)]
    public async Task WhisperDownload_PreservesBytesAndProgress(long? length)
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        using var client = Client(() => Response(bytes, length));
        var updates = new List<DownloadProgress>();
        var progress = new InlineProgress(updates.Add);
        string directory = _directory;

        string path = await new WhisperModelDownloader(client).EnsureModelAsync(directory, WhisperModelSize.Base, progress, TestContext.Current.CancellationToken);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(4, updates.Count);
        Assert.Equal(0, updates[0].BytesDownloaded);
        Assert.Equal(new long[] { 2, 4, 5 }, updates.Skip(1).Select(p => p.BytesDownloaded));
        Assert.All(updates.Skip(1), p => Assert.Equal(length, p.TotalBytes));
        Assert.All(updates.Skip(1), p => Assert.Equal("Whisperモデル ggml-base.bin ダウンロード中...", p.StatusMessage));
        Assert.Equal(length is null ? null : length == 0 ? double.PositiveInfinity : 100d, updates[^1].Percentage);
    }

    [Fact]
    public async Task WhisperDownload_HttpFailureDoesNotCreateModel()
    {
        using var client = Client(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        string directory = _directory;
        await Assert.ThrowsAsync<HttpRequestException>(() => new WhisperModelDownloader(client).EnsureModelAsync(directory, WhisperModelSize.Base, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task WhisperDownload_CancellationDuringTransferRemovesPartialFile()
    {
        using var client = Client(() => Response([1, 2, 3, 4, 5], 5));
        using var cts = new CancellationTokenSource();
        var updates = new List<DownloadProgress>();
        var progress = new InlineProgress(p => { updates.Add(p); if (p.BytesDownloaded > 0) cts.Cancel(); });
        string directory = _directory;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WhisperModelDownloader(client).EnsureModelAsync(directory, WhisperModelSize.Base, progress, cts.Token));
        Assert.Equal(2, updates[^1].BytesDownloaded);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task PiperVoiceDownload_SavesModelAndConfiguration()
    {
        int request = 0;
        using var client = Client(() => Response([(byte)++request], 1));
        string directory = _directory;
        string path = await new PiperVoiceDownloader(client).EnsureVoiceAsync(directory, "en_US-amy-medium", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 2 }, await File.ReadAllBytesAsync(path + ".json", TestContext.Current.CancellationToken));
        Assert.Equal(2, request);
    }

    private static HttpClient Client(Func<HttpResponseMessage> response) => new(new Handler(response));
    private static HttpResponseMessage Response(byte[] bytes, long? length)
    {
        var content = new StreamContent(new ChunkStream(bytes));
        content.Headers.ContentLength = length;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
    private sealed class InlineProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response());
    }
    private sealed class ChunkStream(byte[] bytes) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(2, bytes.Length - _position);
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return ValueTask.FromResult(count);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
