using LlmChamber.Internal;
using LlmChamber.Internal.Speech;
using LlmChamber.Speech;
using Xunit;

namespace LlmChamber.Tests;

/// <summary>Speech (STT/TTS) オプトイン機能のユニットテスト（ネットワーク不要）。</summary>
public class SpeechTests
{
    // ── Whisper Binary Downloader ──
    // 注: OsPlatform/CpuArchitecture が internal なため、テストパラメータには int を使い内部でキャストする

    [Theory]
    [InlineData((int)OsPlatform.Windows, (int)CpuArchitecture.X64, "whisper-bin-x64.zip")]
    public void WhisperBinaryDownloader_KnownPlatform_ReturnsAssetName(int osIdx, int archIdx, string expected)
    {
        var result = WhisperBinaryDownloader.GetReleaseAssetName((OsPlatform)osIdx, (CpuArchitecture)archIdx);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData((int)OsPlatform.Linux, (int)CpuArchitecture.X64)]
    [InlineData((int)OsPlatform.MacOS, (int)CpuArchitecture.Arm64)]
    public void WhisperBinaryDownloader_UnsupportedPlatform_ReturnsNull(int osIdx, int archIdx)
    {
        var result = WhisperBinaryDownloader.GetReleaseAssetName((OsPlatform)osIdx, (CpuArchitecture)archIdx);
        Assert.Null(result);
    }

    [Theory]
    [InlineData((int)OsPlatform.Windows, "whisper-cli.exe")]
    [InlineData((int)OsPlatform.Linux, "whisper-cli")]
    [InlineData((int)OsPlatform.MacOS, "whisper-cli")]
    public void WhisperBinaryDownloader_ExecutableName_MatchesPlatform(int osIdx, string expected)
    {
        Assert.Equal(expected, WhisperBinaryDownloader.GetExecutableName((OsPlatform)osIdx));
    }

    // ── Whisper Model Downloader ──

    [Theory]
    [InlineData(WhisperModelSize.Tiny, "ggml-tiny.bin")]
    [InlineData(WhisperModelSize.Base, "ggml-base.bin")]
    [InlineData(WhisperModelSize.Small, "ggml-small.bin")]
    [InlineData(WhisperModelSize.Medium, "ggml-medium.bin")]
    [InlineData(WhisperModelSize.Large, "ggml-large-v3.bin")]
    public void WhisperModelDownloader_FileName_MatchesSize(WhisperModelSize size, string expected)
    {
        Assert.Equal(expected, WhisperModelDownloader.GetModelFileName(size));
    }

    // ── Piper Binary Downloader ──

    [Theory]
    [InlineData((int)OsPlatform.Windows, (int)CpuArchitecture.X64, "piper_windows_amd64.zip", ".zip")]
    [InlineData((int)OsPlatform.Linux, (int)CpuArchitecture.X64, "piper_linux_x86_64.tar.gz", ".tar.gz")]
    [InlineData((int)OsPlatform.Linux, (int)CpuArchitecture.Arm64, "piper_linux_aarch64.tar.gz", ".tar.gz")]
    [InlineData((int)OsPlatform.MacOS, (int)CpuArchitecture.X64, "piper_macos_x64.tar.gz", ".tar.gz")]
    [InlineData((int)OsPlatform.MacOS, (int)CpuArchitecture.Arm64, "piper_macos_aarch64.tar.gz", ".tar.gz")]
    public void PiperBinaryDownloader_KnownPlatform_ReturnsAsset(int osIdx, int archIdx, string expectedFile, string expectedExt)
    {
        var result = PiperBinaryDownloader.GetReleaseAsset((OsPlatform)osIdx, (CpuArchitecture)archIdx);
        Assert.NotNull(result);
        Assert.Equal(expectedFile, result.Value.FileName);
        Assert.Equal(expectedExt, result.Value.Extension);
    }

    [Theory]
    [InlineData((int)OsPlatform.Windows, "piper.exe")]
    [InlineData((int)OsPlatform.Linux, "piper")]
    [InlineData((int)OsPlatform.MacOS, "piper")]
    public void PiperBinaryDownloader_ExecutableName_MatchesPlatform(int osIdx, string expected)
    {
        Assert.Equal(expected, PiperBinaryDownloader.GetExecutableName((OsPlatform)osIdx));
    }

    // ── Piper Voice Downloader: voice 名パース ──

    [Theory]
    [InlineData("en_US-amy-medium", "en_US", "medium")]
    [InlineData("ja_JP-takumi-medium", "ja_JP", "medium")]
    [InlineData("de_DE-thorsten-low", "de_DE", "low")]
    [InlineData("en_GB-northern-english-male-medium", "en_GB", "medium")]
    public void PiperVoiceDownloader_ParseVoiceName_Succeeds(string voiceName, string expectedLang, string expectedQuality)
    {
        var (lang, quality) = PiperVoiceDownloader.ParseVoiceName(voiceName);
        Assert.Equal(expectedLang, lang);
        Assert.Equal(expectedQuality, quality);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("notenough-dashes")]
    [InlineData("../en_US-amy-medium")]
    [InlineData("en_US/amy-medium")]
    [InlineData("en_US\\amy-medium")]
    [InlineData("en_US-..-medium")]
    [InlineData("en_US--medium")]
    public void PiperVoiceDownloader_ParseVoiceName_InvalidFormat_Throws(string voiceName)
    {
        Assert.Throws<ArgumentException>(() => PiperVoiceDownloader.ParseVoiceName(voiceName));
    }

    [Theory]
    [InlineData("en_US-amy-medium", "en/en_US/amy/medium")]
    [InlineData("ja_JP-takumi-medium", "ja/ja_JP/takumi/medium")]
    [InlineData("en_GB-northern-english-male-medium", "en/en_GB/northern-english-male/medium")]
    public void PiperVoiceDownloader_BuildHuggingFaceVoicePath_Format(string voiceName, string expected)
    {
        var (lang, quality) = PiperVoiceDownloader.ParseVoiceName(voiceName);
        string result = PiperVoiceDownloader.BuildHuggingFaceVoicePath(voiceName, lang, quality);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task SpeechSession_AutoDownloadDisabled_MissingWhisperBinary_DoesNotSendHttp()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string directory = CreateTempDirectory();
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        await using var session = CreateSession(new SpeechOptions { AutoDownload = false }, directory, httpClient);

        try
        {
            await Assert.ThrowsAsync<SpeechBinaryNotFoundException>(
                () => session.TranscribeFileAsync(Path.Combine(directory, "input.wav"), cancellationToken: cancellationToken));
            Assert.Equal(0, handler.SendCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SpeechSession_AutoDownloadDisabled_MissingWhisperModel_DoesNotSendHttp()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string directory = CreateTempDirectory();
        string binaryPath = Path.Combine(directory, "whisper-cli");
        await File.WriteAllBytesAsync(binaryPath, [], cancellationToken);
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        await using var session = CreateSession(
            new SpeechOptions { AutoDownload = false, WhisperBinaryPath = binaryPath }, directory, httpClient);

        try
        {
            await Assert.ThrowsAsync<SpeechModelNotFoundException>(
                () => session.TranscribeFileAsync(Path.Combine(directory, "input.wav"), cancellationToken: cancellationToken));
            Assert.Equal(0, handler.SendCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SpeechSession_AutoDownloadDisabled_MissingPiperBinary_DoesNotSendHttp()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string directory = CreateTempDirectory();
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        await using var session = CreateSession(new SpeechOptions { AutoDownload = false }, directory, httpClient);

        try
        {
            await Assert.ThrowsAsync<SpeechBinaryNotFoundException>(
                () => session.SpeakAsync("hello", cancellationToken: cancellationToken));
            Assert.Equal(0, handler.SendCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SpeechSession_AutoDownloadDisabled_MissingPiperVoice_DoesNotSendHttp()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string directory = CreateTempDirectory();
        string binaryPath = Path.Combine(directory, "piper");
        await File.WriteAllBytesAsync(binaryPath, [], cancellationToken);
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        await using var session = CreateSession(
            new SpeechOptions { AutoDownload = false, PiperBinaryPath = binaryPath }, directory, httpClient);

        try
        {
            await Assert.ThrowsAsync<SpeechModelNotFoundException>(
                () => session.SpeakAsync("hello", cancellationToken: cancellationToken));
            Assert.Equal(0, handler.SendCount);
            Assert.False(Directory.Exists(Path.Combine(directory, "speech", "voices")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Downloaders_AutoDownloadDisabled_ValidCachesReturnWithoutHttp()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string directory = CreateTempDirectory();
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);

        try
        {
            var os = PlatformInfo.GetCurrentOs();
            string speechDirectory = Path.Combine(directory, "speech");
            string whisperDirectory = Path.Combine(speechDirectory, "whisper");
            string piperDirectory = Path.Combine(speechDirectory, "piper");
            string modelsDirectory = Path.Combine(speechDirectory, "models");
            string voicesDirectory = Path.Combine(speechDirectory, "voices");
            Directory.CreateDirectory(whisperDirectory);
            Directory.CreateDirectory(piperDirectory);
            Directory.CreateDirectory(modelsDirectory);
            Directory.CreateDirectory(voicesDirectory);

            string whisperPath = Path.Combine(whisperDirectory, WhisperBinaryDownloader.GetExecutableName(os));
            string piperPath = Path.Combine(piperDirectory, PiperBinaryDownloader.GetExecutableName(os));
            string modelPath = Path.Combine(modelsDirectory, WhisperModelDownloader.GetModelFileName(WhisperModelSize.Base));
            string voicePath = Path.Combine(voicesDirectory, "en_US-amy-medium.onnx");
            await File.WriteAllBytesAsync(whisperPath, [], cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(whisperDirectory, ".version"), WhisperBinaryDownloader.DefaultWhisperVersion, cancellationToken);
            await File.WriteAllBytesAsync(piperPath, [], cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(piperDirectory, ".version"), PiperBinaryDownloader.DefaultPiperVersion, cancellationToken);
            await File.WriteAllBytesAsync(modelPath, [], cancellationToken);
            await File.WriteAllBytesAsync(voicePath, [], cancellationToken);
            await File.WriteAllBytesAsync(voicePath + ".json", [], cancellationToken);

            Assert.Equal(whisperPath, await new WhisperBinaryDownloader(httpClient).EnsureBinaryAsync(
                speechDirectory, cancellationToken: cancellationToken, allowDownload: false));
            Assert.Equal(piperPath, await new PiperBinaryDownloader(httpClient).EnsureBinaryAsync(
                speechDirectory, cancellationToken: cancellationToken, allowDownload: false));
            Assert.Equal(modelPath, await new WhisperModelDownloader(httpClient).EnsureModelAsync(
                modelsDirectory, WhisperModelSize.Base, cancellationToken: cancellationToken, allowDownload: false));
            Assert.Equal(voicePath, await new PiperVoiceDownloader(httpClient).EnsureVoiceAsync(
                voicesDirectory, "en_US-amy-medium", cancellationToken: cancellationToken, allowDownload: false));
            Assert.Equal(0, handler.SendCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ── Whisper JSON Parser ──

    [Fact]
    public void WhisperRunner_ParseWhisperJson_BasicSegments()
    {
        string json = """
        {
          "result": { "language": "ja" },
          "transcription": [
            { "timestamps": { "from": "00:00:00,000", "to": "00:00:02,500" }, "text": " こんにちは" },
            { "timestamps": { "from": "00:00:02,500", "to": "00:00:05,000" }, "text": " 今日はいい天気ですね" }
          ]
        }
        """;

        var result = WhisperRunner.ParseWhisperJson(json, includeSegments: true);

        Assert.Equal("ja", result.DetectedLanguage);
        Assert.Contains("こんにちは", result.Text);
        Assert.Contains("今日はいい天気ですね", result.Text);
        Assert.NotNull(result.Segments);
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Duration);
    }

    [Fact]
    public void WhisperRunner_ParseWhisperJson_SegmentsDisabled_ReturnsNullSegments()
    {
        string json = """
        {
          "transcription": [
            { "timestamps": { "from": "00:00:00,000", "to": "00:00:01,000" }, "text": "hello" }
          ]
        }
        """;

        var result = WhisperRunner.ParseWhisperJson(json, includeSegments: false);

        Assert.Null(result.Segments);
        Assert.Contains("hello", result.Text);
    }

    [Theory]
    [InlineData("00:00:00,000", 0, 0, 0, 0)]
    [InlineData("00:00:01,500", 0, 0, 1, 500)]
    [InlineData("00:01:30,250", 0, 1, 30, 250)]
    [InlineData("01:00:00,000", 1, 0, 0, 0)]
    public void WhisperRunner_ParseTimestamp_KnownFormat(string input, int h, int m, int s, int ms)
    {
        var ts = WhisperRunner.ParseTimestamp(input);
        Assert.Equal(new TimeSpan(0, h, m, s, ms), ts);
    }

    [Fact]
    public void WhisperRunner_ParseTimestamp_NullOrEmpty_ReturnsZero()
    {
        Assert.Equal(TimeSpan.Zero, WhisperRunner.ParseTimestamp(null));
        Assert.Equal(TimeSpan.Zero, WhisperRunner.ParseTimestamp(""));
    }

    private static SpeechSession CreateSession(SpeechOptions options, string cacheDirectory, HttpClient httpClient)
        => new(
            options,
            cacheDirectory,
            new WhisperBinaryDownloader(httpClient),
            new WhisperModelDownloader(httpClient),
            new PiperBinaryDownloader(httpClient),
            new PiperVoiceDownloader(httpClient));

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"llmchamber-speech-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
