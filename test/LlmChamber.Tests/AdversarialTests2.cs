using System.IO;
using System.Text;
using LlmChamber;
using LlmChamber.Internal;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace LlmChamber.Tests;

/// <summary>
/// LlmChamberの追加adversarialテスト。
/// GpuDetector, RuntimeManager, PlatformInfo, ChatSession の未カバー領域を対象。
/// </summary>
public class AdversarialTests2
{
    // ===================================================================
    // 🗡️ 境界値・極端入力（Boundary Assault）
    // ===================================================================

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity High
    /// ParseCsvLine: RFC 4180 エスケープされた引用符 "" を正しくパースすること。
    /// </summary>
    [Fact]
    public void GpuDetector_ParseCsvLine_EscapedQuotes_ParsesCorrectly()
    {
        // "NVIDIA","GeForce RTX ""Ada"" 4090","12345"
        string line = "\"NVIDIA\",\"GeForce RTX \"\"Ada\"\" 4090\",\"12345\"";
        var result = InvokeParseCsvLine(line);

        Assert.Equal(3, result.Length);
        Assert.Equal("NVIDIA", result[0]);
        Assert.Equal("GeForce RTX \"Ada\" 4090", result[1]);
        Assert.Equal("12345", result[2]);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Medium
    /// ParseCsvLine: 空文字列を渡した場合。
    /// </summary>
    [Fact]
    public void GpuDetector_ParseCsvLine_EmptyString_ReturnsSingleEmptyElement()
    {
        var result = InvokeParseCsvLine("");
        Assert.Single(result);
        Assert.Equal("", result[0]);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Medium
    /// ParseCsvLine: カンマのみの行。
    /// </summary>
    [Fact]
    public void GpuDetector_ParseCsvLine_OnlyCommas_ReturnsEmptyFields()
    {
        var result = InvokeParseCsvLine(",,,");
        Assert.Equal(4, result.Length);
        Assert.All(result, f => Assert.Equal("", f));
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity High
    /// ParseCsvLine: 引用符で囲まれたカンマを含むフィールド。
    /// </summary>
    [Fact]
    public void GpuDetector_ParseCsvLine_QuotedComma_DoesNotSplit()
    {
        string line = "\"value,with,commas\",normal";
        var result = InvokeParseCsvLine(line);
        Assert.Equal(2, result.Length);
        Assert.Equal("value,with,commas", result[0]);
        Assert.Equal("normal", result[1]);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Medium
    /// ParseCsvLine: Unicode文字（日本語GPU名）。
    /// </summary>
    [Fact]
    public void GpuDetector_ParseCsvLine_UnicodeContent_ParsesCorrectly()
    {
        string line = "\"NVIDIA\",\"テスト GPU 🎮\",\"1024\"";
        var result = InvokeParseCsvLine(line);
        Assert.Equal(3, result.Length);
        Assert.Equal("テスト GPU 🎮", result[1]);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity High
    /// PlatformInfo.GetDesktopBinaryInfo: 全バリアント×全アーキテクチャの組み合わせ。
    /// </summary>
    [Fact]
    public void PlatformInfo_GetOllamaBinaryInfo_AllCombinations()
    {
        var cases = new (OsPlatform os, CpuArchitecture arch, RuntimeVariant variant, string name, string ext)[]
        {
            (OsPlatform.Windows, CpuArchitecture.X64, RuntimeVariant.Full, "ollama-windows-amd64", ".zip"),
            (OsPlatform.Windows, CpuArchitecture.Arm64, RuntimeVariant.Full, "ollama-windows-arm64", ".zip"),
            (OsPlatform.Windows, CpuArchitecture.X64, RuntimeVariant.Rocm, "ollama-windows-amd64-rocm", ".zip"),
            (OsPlatform.Linux, CpuArchitecture.X64, RuntimeVariant.Full, "ollama-linux-amd64", ".tar.zst"),
            (OsPlatform.Linux, CpuArchitecture.Arm64, RuntimeVariant.Rocm, "ollama-linux-arm64-rocm", ".tar.zst"),
        };

        foreach (var (os, arch, variant, expectedName, expectedExt) in cases)
        {
            var (name, ext) = PlatformInfo.GetOllamaBinaryInfo(os, arch, variant);
            Assert.Equal(expectedName, name);
            Assert.Equal(expectedExt, ext);
        }
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Medium
    /// ChatSession.TrimHistory: MaxHistoryMessages=1 で大量メッセージ追加後も1件のみ保持。
    /// </summary>
    [Fact]
    public void ChatSession_TrimHistory_MaxOne_KeepsOnlyLatest()
    {
        var apiClient = CreateMockApiClient();
        var session = new ChatSession(apiClient, "test-model",
            new ChatOptions { MaxHistoryMessages = 1 });

        // lockの中で直接_historyを操作するため、リフレクションで内部メソッド呼び出し
        var addUser = typeof(ChatSession).GetMethod("AddUserMessage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var addAssistant = typeof(ChatSession).GetMethod("AddAssistantMessage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        for (int i = 0; i < 50; i++)
        {
            addUser.Invoke(session, [$"user-{i}"]);
            addAssistant.Invoke(session, [$"assistant-{i}"]);
        }

        var history = session.History;
        // システムメッセージなし、非システムは1件のみ
        Assert.Equal(1, history.Count);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Medium
    /// ChatSession.TrimHistory: システムプロンプト付きでMaxHistory制限。
    /// </summary>
    [Fact]
    public void ChatSession_TrimHistory_WithSystemPrompt_PreservesSystem()
    {
        var apiClient = CreateMockApiClient();
        var session = new ChatSession(apiClient, "test-model",
            new ChatOptions
            {
                SystemPrompt = "あなたはテスト用AIです",
                MaxHistoryMessages = 2,
            });

        var addUser = typeof(ChatSession).GetMethod("AddUserMessage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var addAssistant = typeof(ChatSession).GetMethod("AddAssistantMessage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        for (int i = 0; i < 10; i++)
        {
            addUser.Invoke(session, [$"user-{i}"]);
            addAssistant.Invoke(session, [$"assistant-{i}"]);
        }

        var history = session.History;
        // システムメッセージは常に保持
        Assert.Contains(history, m => m.Role == ChatRole.System);
        // 非システムメッセージは2件以下
        Assert.True(history.Count(m => m.Role != ChatRole.System) <= 2);
    }

    // ===================================================================
    // ⚡ 並行性・レースコンディション（Concurrency Chaos）
    // ===================================================================

    /// <summary>
    /// @adversarial @category ConcurrencyChaos @severity Critical
    /// RuntimeManager.GetCacheSizeBytesAsync: キャンセルトークンが尊重されること。
    /// </summary>
    [Fact]
    public async Task RuntimeManager_GetCacheSizeBytesAsync_CancellationToken_Respected()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"llmchamber-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            // 多数のファイルを作成
            for (int i = 0; i < 100; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(tempDir, $"file-{i}.bin"), new string('x', 1024));
            }

            var opts = new LlmChamberOptions { CacheDirectory = tempDir };
            var rm = CreateRuntimeManager(opts);

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // 即座にキャンセル

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => rm.GetCacheSizeBytesAsync(cts.Token));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// @adversarial @category ConcurrencyChaos @severity High
    /// ChatSession.TrimHistory: 並行アクセス中にTrimが走ってもデッドロックしないこと。
    /// </summary>
    [Fact]
    public async Task ChatSession_ConcurrentAddAndTrim_NoDeadlock()
    {
        var apiClient = CreateMockApiClient();
        var session = new ChatSession(apiClient, "test-model",
            new ChatOptions { MaxHistoryMessages = 5 });

        var addUser = typeof(ChatSession).GetMethod("AddUserMessage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var addAssistant = typeof(ChatSession).GetMethod("AddAssistantMessage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
        {
            for (int j = 0; j < 20; j++)
            {
                cts.Token.ThrowIfCancellationRequested();
                addUser.Invoke(session, [$"user-{i}-{j}"]);
                addAssistant.Invoke(session, [$"assistant-{i}-{j}"]);
            }
        }, cts.Token)).ToArray();

        await Task.WhenAll(tasks);

        // デッドロックせずに完了し、Historyが取得可能
        var history = session.History;
        Assert.NotNull(history);
    }

    // ===================================================================
    // 💀 リソース枯渇・DoS耐性（Resource Exhaustion）
    // ===================================================================

    /// <summary>
    /// @adversarial @category ResourceExhaustion @severity Medium
    /// RuntimeManager.GetCacheSizeBytesAsync: 空ディレクトリ（ファイルなし）で0を返すこと。
    /// </summary>
    [Fact]
    public async Task RuntimeManager_GetCacheSizeBytesAsync_EmptyDir_ReturnsZero()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"llmchamber-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var opts = new LlmChamberOptions { CacheDirectory = tempDir };
            var rm = CreateRuntimeManager(opts);

            long size = await rm.GetCacheSizeBytesAsync();
            Assert.Equal(0L, size);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// @adversarial @category ResourceExhaustion @severity High
    /// RuntimeManager.GetCacheSizeBytesAsync: 存在しないディレクトリで0を返すこと。
    /// </summary>
    [Fact]
    public async Task RuntimeManager_GetCacheSizeBytesAsync_NonExistentDir_ReturnsZero()
    {
        var opts = new LlmChamberOptions { CacheDirectory = Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid()) };
        var rm = CreateRuntimeManager(opts);

        long size = await rm.GetCacheSizeBytesAsync();
        Assert.Equal(0L, size);
    }

    /// <summary>
    /// @adversarial @category ResourceExhaustion @severity Medium
    /// RuntimeManager.GetCacheSizeBytesAsync: ネストされたサブディレクトリのファイルも含むこと。
    /// </summary>
    [Fact]
    public async Task RuntimeManager_GetCacheSizeBytesAsync_NestedDirs_IncludesAll()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"llmchamber-test-{Guid.NewGuid():N}");
        try
        {
            string subDir = Path.Combine(tempDir, "sub1", "sub2");
            Directory.CreateDirectory(subDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "root.bin"), "12345"); // 5 bytes
            await File.WriteAllTextAsync(Path.Combine(subDir, "nested.bin"), "abcdefghij"); // 10 bytes

            var opts = new LlmChamberOptions { CacheDirectory = tempDir };
            var rm = CreateRuntimeManager(opts);

            long size = await rm.GetCacheSizeBytesAsync();
            Assert.True(size >= 15, $"期待: >=15 bytes, 実際: {size}");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    // ===================================================================
    // 🔀 状態遷移の矛盾（State Machine Abuse）
    // ===================================================================

    /// <summary>
    /// @adversarial @category StateMachineAbuse @severity High
    /// ChatSession: ClearHistory後にHistoryがシステムメッセージのみになること。
    /// </summary>
    [Fact]
    public void ChatSession_ClearHistory_ThenCheckHistory_SystemOnly()
    {
        var apiClient = CreateMockApiClient();
        var session = new ChatSession(apiClient, "test-model",
            new ChatOptions { SystemPrompt = "system prompt" });

        var addUser = typeof(ChatSession).GetMethod("AddUserMessage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        addUser.Invoke(session, ["hello"]);
        addUser.Invoke(session, ["world"]);

        session.ClearHistory();

        var history = session.History;
        Assert.Single(history);
        Assert.Equal(ChatRole.System, history[0].Role);
    }

    /// <summary>
    /// @adversarial @category StateMachineAbuse @severity High
    /// ChatSession: ClearHistory後に再度メッセージ追加可能なこと。
    /// </summary>
    [Fact]
    public void ChatSession_ClearHistory_ThenAddMessages_Works()
    {
        var apiClient = CreateMockApiClient();
        var session = new ChatSession(apiClient, "test-model");

        var addUser = typeof(ChatSession).GetMethod("AddUserMessage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        addUser.Invoke(session, ["first"]);
        session.ClearHistory();
        addUser.Invoke(session, ["after-clear"]);

        var history = session.History;
        Assert.Single(history);
        Assert.Equal("after-clear", history[0].Content);
    }

    // ===================================================================
    // 🎭 型パンチ・プロトコル違反（Type Punching）
    // ===================================================================

    /// <summary>
    /// @adversarial @category TypePunching @severity High
    /// GpuDetector.ParseCsvLine: 閉じられていない引用符。
    /// </summary>
    [Fact]
    public void GpuDetector_ParseCsvLine_UnclosedQuote_DoesNotThrow()
    {
        // 引用符が閉じられていないケース — クラッシュしないこと
        var result = InvokeParseCsvLine("\"unclosed,field\",normal");
        Assert.NotNull(result);
        Assert.True(result.Length >= 1);
    }

    /// <summary>
    /// @adversarial @category TypePunching @severity Medium
    /// GpuDetector.ParseCsvLine: ヌルバイト・制御文字入り。
    /// </summary>
    [Fact]
    public void GpuDetector_ParseCsvLine_ControlCharacters_DoesNotThrow()
    {
        string line = "\"NVIDIA\",\"GPU\x00Name\",\"1024\"";
        var result = InvokeParseCsvLine(line);
        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
    }

    /// <summary>
    /// @adversarial @category TypePunching @severity High
    /// GpuDetector.ParseCsvLine: 連続引用符のみのフィールド。
    /// </summary>
    [Fact]
    public void GpuDetector_ParseCsvLine_ConsecutiveQuotes_ParsesAsEscaped()
    {
        // フィールド全体が引用符: "" → " (エスケープ)
        string line = "\"\"\"\"";
        var result = InvokeParseCsvLine(line);
        Assert.Single(result);
        Assert.Equal("\"", result[0]);
    }

    /// <summary>
    /// @adversarial @category TypePunching @severity Medium
    /// PlatformInfo: macOS用バイナリ情報は固定値。
    /// </summary>
    [Fact]
    public void PlatformInfo_GetOllamaBinaryInfo_MacOS_IgnoresVariant()
    {
        // macOSはバリアントに関係なく固定値
        var (name, ext) = PlatformInfo.GetOllamaBinaryInfo(OsPlatform.MacOS, CpuArchitecture.Arm64, RuntimeVariant.Full);
        Assert.Equal("ollama-darwin", name);
        Assert.Equal(".tgz", ext);
    }

    /// <summary>
    /// @adversarial @category TypePunching @severity High
    /// PlatformInfo: サポート外プラットフォームで例外。
    /// </summary>
    [Fact]
    public void PlatformInfo_GetOllamaBinaryInfo_UnsupportedOs_ThrowsException()
    {
        Assert.Throws<UnsupportedPlatformException>(
            () => PlatformInfo.GetOllamaBinaryInfo((OsPlatform)999, CpuArchitecture.X64, RuntimeVariant.Full));
    }

    // ===================================================================
    // 🌪️ 環境異常・カオステスト（Environmental Chaos）
    // ===================================================================

    /// <summary>
    /// @adversarial @category EnvironmentalChaos @severity High
    /// OllamaDownloader.FindExistingBinary: バージョンマーカーにバリアント不一致。
    /// </summary>
    [Fact]
    public void OllamaDownloader_FindExistingBinary_VariantMismatch_ReturnsNull()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"llmchamber-test-{Guid.NewGuid():N}");
        try
        {
            string runtimeDir = Path.Combine(tempDir, "runtime");
            Directory.CreateDirectory(runtimeDir);

            // Full variant でインストール
            File.WriteAllText(Path.Combine(tempDir, ".version"), "0.20.2:Full");
            string execName = PlatformInfo.GetOllamaExecutableName(PlatformInfo.GetCurrentOs());
            File.WriteAllText(Path.Combine(runtimeDir, execName), "dummy");

            var downloader = new OllamaDownloader(new System.Net.Http.HttpClient());

            // Rocm variant で検索 → 不一致でnull
            string? result = downloader.FindExistingBinary(tempDir, "0.20.2", RuntimeVariant.Rocm);
            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// @adversarial @category EnvironmentalChaos @severity Medium
    /// OllamaDownloader.FindExistingBinary: バイナリファイルが存在しない。
    /// </summary>
    [Fact]
    public void OllamaDownloader_FindExistingBinary_MissingBinary_ReturnsNull()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"llmchamber-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            // バージョンマーカーはあるがバイナリがない
            File.WriteAllText(Path.Combine(tempDir, ".version"), "0.20.2:Full");

            var downloader = new OllamaDownloader(new System.Net.Http.HttpClient());
            string? result = downloader.FindExistingBinary(tempDir, "0.20.2", RuntimeVariant.Full);
            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// @adversarial @category EnvironmentalChaos @severity High
    /// OllamaDownloader.FindExistingBinary: バリアント一致でバイナリも存在する場合は返す。
    /// </summary>
    [Fact]
    public void OllamaDownloader_FindExistingBinary_VariantMatch_ReturnsBinary()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"llmchamber-test-{Guid.NewGuid():N}");
        try
        {
            string runtimeDir = Path.Combine(tempDir, "runtime");
            Directory.CreateDirectory(runtimeDir);

            File.WriteAllText(Path.Combine(tempDir, ".version"), "0.20.2:Full");
            string execName = PlatformInfo.GetOllamaExecutableName(PlatformInfo.GetCurrentOs());
            File.WriteAllText(Path.Combine(runtimeDir, execName), "dummy");

            var downloader = new OllamaDownloader(new System.Net.Http.HttpClient());
            string? result = downloader.FindExistingBinary(tempDir, "0.20.2", RuntimeVariant.Full);
            Assert.NotNull(result);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    // ===================================================================
    // ヘルパーメソッド
    // ===================================================================

    /// <summary>GpuDetector.ParseCsvLine をリフレクションで呼び出す。</summary>
    private static string[] InvokeParseCsvLine(string line)
    {
        var method = typeof(GpuDetector).GetMethod("ParseCsvLine",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (string[])method.Invoke(null, [line])!;
    }

    /// <summary>テスト用のOllamaApiClientを作成する。</summary>
    private static OllamaApiClient CreateMockApiClient()
    {
        return new OllamaApiClient(new System.Net.Http.HttpClient());
    }

    /// <summary>テスト用のRuntimeManagerを作成する。</summary>
    private static RuntimeManager CreateRuntimeManager(LlmChamberOptions? opts = null)
    {
        opts ??= new LlmChamberOptions();
        var downloader = new OllamaDownloader(new System.Net.Http.HttpClient());
        var apiClient = new OllamaApiClient(new System.Net.Http.HttpClient());
        var processManager = new OllamaProcessManager(Options.Create(opts));
        return new RuntimeManager(downloader, apiClient, processManager, Options.Create(opts));
    }
}
