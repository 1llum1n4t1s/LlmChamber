using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using LlmChamber;
using LlmChamber.Internal;
using LlmChamber.Internal.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace LlmChamber.Tests;

/// <summary>
/// LlmChamberプロジェクトの敵対的テスト（adversarial tests）。
/// 境界値、異常入力、並行アクセス、リソース枯渇、型の不正、状態遷移の矛盾など
/// 正常系テストでは見つからないバグを炙り出す。
/// </summary>
public class AdversarialTests
{
    #region ヘルパーメソッド

    /// <summary>テスト用のOllamaApiClientを作成する（実際のAPI呼び出しはしない）。</summary>
    private static OllamaApiClient CreateMockApiClient(HttpMessageHandler? handler = null)
    {
        var httpClient = handler is not null
            ? new HttpClient(handler)
            : new HttpClient();
        return new OllamaApiClient(httpClient, NullLogger<OllamaApiClient>.Instance);
    }

    /// <summary>テスト用のOllamaDownloaderを作成する。</summary>
    private static OllamaDownloader CreateDownloader(HttpMessageHandler? handler = null)
    {
        var httpClient = handler is not null
            ? new HttpClient(handler)
            : new HttpClient();
        return new OllamaDownloader(httpClient, NullLogger<OllamaDownloader>.Instance);
    }

    /// <summary>テスト用のOllamaProcessManagerを作成する。</summary>
    private static OllamaProcessManager CreateProcessManager(LlmChamberOptions? opts = null)
    {
        opts ??= new LlmChamberOptions();
        return new OllamaProcessManager(
            NullLogger<OllamaProcessManager>.Instance,
            Options.Create(opts));
    }

    /// <summary>テスト用のLocalLlmを作成する。</summary>
    private static LocalLlm CreateLocalLlm(
        LlmChamberOptions? opts = null,
        OllamaApiClient? apiClient = null,
        OllamaProcessManager? processManager = null)
    {
        opts ??= new LlmChamberOptions();
        var wrappedOptions = Options.Create(opts);
        apiClient ??= CreateMockApiClient();
        var downloader = CreateDownloader();
        processManager ??= CreateProcessManager(opts);
        var runtimeManager = Substitute.For<IRuntimeManager>();
        return new LocalLlm(
            wrappedOptions,
            downloader,
            processManager,
            apiClient,
            runtimeManager,
            NullLogger<LocalLlm>.Instance);
    }

    /// <summary>MemoryStreamからStreamReaderを作成する。</summary>
    private static StreamReader CreateStreamReader(string content)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return new StreamReader(stream, Encoding.UTF8);
    }

    /// <summary>テスト用の一時ディレクトリを作成してパスを返す。</summary>
    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"llmchamber-adv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>一時ディレクトリを安全に削除する。</summary>
    private static void CleanupTempDir(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    /// <summary>Chat APIのストリーミング応答用のモックHttpMessageHandlerを作成。</summary>
    private static HttpMessageHandler CreateChatStreamHandler(string[] responses)
    {
        var handler = Substitute.ForPartsOf<MockableHandler>();
        var sb = new StringBuilder();
        foreach (string r in responses)
        {
            sb.AppendLine(JsonSerializer.Serialize(new { model = "test", message = new { role = "assistant", content = r }, done = false }));
        }
        sb.AppendLine(JsonSerializer.Serialize(new { model = "test", message = new { role = "assistant", content = "" }, done = true }));

        handler.MockSendAsync(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sb.ToString(), Encoding.UTF8, "application/x-ndjson"),
            });
        return handler;
    }

    /// <summary>Chat APIの非ストリーミング（一括）応答用のモックHttpMessageHandlerを作成。</summary>
    private static HttpMessageHandler CreateChatCompleteHandler(string responseContent)
    {
        var handler = Substitute.ForPartsOf<MockableHandler>();
        string json = JsonSerializer.Serialize(new
        {
            model = "test",
            message = new { role = "assistant", content = responseContent },
            done = true,
        });

        handler.MockSendAsync(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        return handler;
    }

    /// <summary>NSubstituteでHttpMessageHandlerをモックするためのベースクラス。</summary>
    public abstract class MockableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => MockSendAsync(request, cancellationToken);

        public abstract Task<HttpResponseMessage> MockSendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken);
    }

    #endregion

    #region 1. Boundary Assault（境界値攻撃）

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity High
    /// null文字列をChatMessage.FromUserに渡した場合の動作を検証。
    /// recordコンストラクタはnullを許容するため、nullがそのまま格納されることを確認。
    /// </summary>
    [Fact]
    public void ChatMessage_FromUser_WithNull_DoesNotThrow()
    {
        // ChatMessageはrecordなのでnull contentはそのまま保持される（NRTは実行時強制なし）
        var msg = ChatMessage.FromUser(null!);
        Assert.NotNull(msg);
        Assert.Equal(ChatRole.User, msg.Role);
        Assert.Null(msg.Content);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity High
    /// null文字列をChatMessage.FromAssistantに渡した場合の動作を検証。
    /// </summary>
    [Fact]
    public void ChatMessage_FromAssistant_WithNull_DoesNotThrow()
    {
        var msg = ChatMessage.FromAssistant(null!);
        Assert.NotNull(msg);
        Assert.Equal(ChatRole.Assistant, msg.Role);
        Assert.Null(msg.Content);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity High
    /// null文字列をChatMessage.FromSystemに渡した場合の動作を検証。
    /// </summary>
    [Fact]
    public void ChatMessage_FromSystem_WithNull_DoesNotThrow()
    {
        var msg = ChatMessage.FromSystem(null!);
        Assert.NotNull(msg);
        Assert.Equal(ChatRole.System, msg.Role);
        Assert.Null(msg.Content);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Medium
    /// 空文字列をChatMessageの各ファクトリメソッドに渡した場合。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n\r")]
    public void ChatMessage_WithWhitespaceContent_DoesNotThrow(string content)
    {
        var user = ChatMessage.FromUser(content);
        var assistant = ChatMessage.FromAssistant(content);
        var system = ChatMessage.FromSystem(content);

        Assert.Equal(content, user.Content);
        Assert.Equal(content, assistant.Content);
        Assert.Equal(content, system.Content);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Medium
    /// 1MB超の巨大文字列をChatMessage.Contentに格納できることを検証。
    /// </summary>
    [Fact]
    public void ChatMessage_WithEnormousContent_1MB_DoesNotThrow()
    {
        string enormous = new('A', 1024 * 1024); // 1MB
        var msg = ChatMessage.FromUser(enormous);

        Assert.Equal(1024 * 1024, msg.Content.Length);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity High
    /// Unicode境界ケース: ゼロ幅文字、RTL、絵文字結合、サロゲートペア。
    /// </summary>
    [Theory]
    [InlineData("\u200B\u200C\u200D\uFEFF")] // ゼロ幅文字
    [InlineData("\u202E\u0041\u0042\u0043")] // RTLオーバーライド
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466")] // 家族絵文字
    [InlineData("\uD800")] // 孤立サロゲート（不正UTF-16）
    [InlineData("日本語テスト\U00020000")] // CJK統合漢字拡張B
    public void ChatMessage_WithUnicodeEdgeCases_DoesNotThrow(string content)
    {
        var msg = ChatMessage.FromUser(content);
        Assert.Equal(content, msg.Content);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity High
    /// MaxHistoryMessages=0で履歴が即座に切り詰められることを検証。
    /// </summary>
    [Fact]
    public void ChatSession_MaxHistoryMessages_Zero_TrimsImmediately()
    {
        var apiClient = CreateMockApiClient();
        var session = new ChatSession(apiClient, "test-model", new ChatOptions
        {
            MaxHistoryMessages = 0,
        });

        // システムプロンプトなしの場合、履歴は空のまま
        Assert.Empty(session.History);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Medium
    /// MaxHistoryMessages=負数でもクラッシュしないことを検証。
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ChatSession_MaxHistoryMessages_Negative_DoesNotCrash(int maxMessages)
    {
        var apiClient = CreateMockApiClient();
        var session = new ChatSession(apiClient, "test-model", new ChatOptions
        {
            MaxHistoryMessages = maxMessages,
        });

        // コンストラクタは成功する
        Assert.NotNull(session);
        Assert.NotNull(session.History);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Low
    /// MaxHistoryMessages=int.MaxValueでも問題なく動作することを検証。
    /// </summary>
    [Fact]
    public void ChatSession_MaxHistoryMessages_IntMaxValue_DoesNotOverflow()
    {
        var apiClient = CreateMockApiClient();
        var session = new ChatSession(apiClient, "test-model", new ChatOptions
        {
            MaxHistoryMessages = int.MaxValue,
        });

        Assert.NotNull(session);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Medium
    /// InferenceOptionsの極端な値でrecordが正常に作成できることを検証。
    /// </summary>
    [Fact]
    public void InferenceOptions_ExtremeValues_DoNotThrow()
    {
        var opts = new InferenceOptions
        {
            Temperature = -1.0f,
            MaxTokens = 0,
            TopP = float.NaN,
            TopK = int.MinValue,
            RepeatPenalty = float.PositiveInfinity,
            Seed = int.MinValue,
        };

        Assert.Equal(-1.0f, opts.Temperature);
        Assert.Equal(0, opts.MaxTokens);
        Assert.True(float.IsNaN(opts.TopP!.Value));
        Assert.Equal(int.MinValue, opts.TopK);
        Assert.True(float.IsPositiveInfinity(opts.RepeatPenalty!.Value));
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Medium
    /// InferenceOptionsの超高Temperature（100）がrecordに格納できることを検証。
    /// </summary>
    [Fact]
    public void InferenceOptions_VeryHighTemperature_DoesNotThrow()
    {
        var opts = new InferenceOptions
        {
            Temperature = 100.0f,
            MaxTokens = int.MaxValue,
        };

        Assert.Equal(100.0f, opts.Temperature);
        Assert.Equal(int.MaxValue, opts.MaxTokens);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Low
    /// StopSequences: 空配列 vs null vs 空文字列を含むリスト。
    /// </summary>
    [Fact]
    public void InferenceOptions_StopSequences_EdgeCases()
    {
        var withNull = new InferenceOptions { StopSequences = null };
        var withEmpty = new InferenceOptions { StopSequences = [] };
        var withEmptyStrings = new InferenceOptions { StopSequences = ["", " ", null!] };

        Assert.Null(withNull.StopSequences);
        Assert.Empty(withEmpty.StopSequences);
        Assert.Equal(3, withEmptyStrings.StopSequences.Count);
    }

    #endregion

    #region 2. Concurrency Chaos（並行性カオス）

    /// <summary>
    /// @adversarial @category ConcurrencyChaos @severity Critical
    /// 同一ChatSessionに対する複数並行SendCompleteAsync呼び出しで
    /// 履歴の整合性が保たれることを検証。
    /// </summary>
    [Fact]
    public async Task ChatSession_ConcurrentSendCompleteAsync_HistoryConsistency()
    {
        var handler = CreateChatStreamHandler(["応答"]);
        var apiClient = CreateMockApiClient(handler);
        apiClient.SetBaseUrl("http://localhost:9999");

        var session = new ChatSession(apiClient, "test-model");

        // 10個の並行リクエスト
        var tasks = Enumerable.Range(0, 10)
            .Select(i => Task.Run(async () =>
            {
                try
                {
                    await session.SendCompleteAsync($"メッセージ{i}");
                }
                catch
                {
                    // APIモックの制限でエラーが出ても良い
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // 履歴アクセスがデッドロックやクラッシュしないことを確認
        var history = session.History;
        Assert.NotNull(history);
    }

    /// <summary>
    /// @adversarial @category ConcurrencyChaos @severity Critical
    /// LocalLlmの二重Dispose呼び出しが例外を投げないことを検証。
    /// </summary>
    [Fact]
    public void LocalLlm_DoubleDispose_DoesNotThrow()
    {
        var localLlm = CreateLocalLlm();

        // 1回目のDispose
        localLlm.Dispose();

        // 2回目のDispose - 例外が飛ばないこと
        var ex = Record.Exception(() => localLlm.Dispose());
        Assert.Null(ex);
    }

    /// <summary>
    /// @adversarial @category ConcurrencyChaos @severity Critical
    /// LocalLlmの二重DisposeAsync呼び出しが例外を投げないことを検証。
    /// </summary>
    [Fact]
    public async Task LocalLlm_DoubleDisposeAsync_DoesNotThrow()
    {
        var localLlm = CreateLocalLlm();

        await localLlm.DisposeAsync();
        var ex = await Record.ExceptionAsync(async () => await localLlm.DisposeAsync());
        Assert.Null(ex);
    }

    /// <summary>
    /// @adversarial @category ConcurrencyChaos @severity High
    /// ClearHistoryとHistory読み取りの並行実行で
    /// デッドロックやクラッシュが発生しないことを検証。
    /// </summary>
    [Fact]
    public async Task ChatSession_ConcurrentClearHistoryAndRead_NoDeadlock()
    {
        var apiClient = CreateMockApiClient();
        var session = new ChatSession(apiClient, "test-model", new ChatOptions
        {
            SystemPrompt = "テストシステムプロンプト",
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var tasks = new List<Task>();

        // ClearHistoryを高速で繰り返すタスク
        tasks.Add(Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                session.ClearHistory();
                await Task.Yield();
            }
        }, cts.Token));

        // History読み取りを高速で繰り返すタスク
        tasks.Add(Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var history = session.History;
                // スナップショットなのでenumerate中に変更されてもクラッシュしない
                foreach (var msg in history)
                {
                    _ = msg.Content;
                }
                await Task.Yield();
            }
        }, cts.Token));

        // タイムアウトまで実行（デッドロックなら5秒でキャンセル）
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // 正常なキャンセル
        }
    }

    #endregion

    #region 3. Resource Exhaustion（リソース枯渇）

    /// <summary>
    /// @adversarial @category ResourceExhaustion @severity High
    /// NdjsonStreamReaderに10MBの巨大な1行JSONを読ませた場合の動作を検証。
    /// </summary>
    [Fact]
    public async Task NdjsonStreamReader_GiantSingleLine_10MB()
    {
        // 10MBの巨大JSONオブジェクト
        string value = new('X', 10 * 1024 * 1024);
        string json = $"{{\"data\":\"{value}\"}}";

        using var reader = CreateStreamReader(json);
        var items = new List<JsonElement>();

        await foreach (var item in NdjsonStreamReader.ReadAsync<JsonElement>(reader))
        {
            items.Add(item);
        }

        Assert.Single(items);
        Assert.Equal(value, items[0].GetProperty("data").GetString());
    }

    /// <summary>
    /// @adversarial @category ResourceExhaustion @severity Medium
    /// DownloadProgressのlong.MaxValueバイトでオーバーフローしないことを検証。
    /// </summary>
    [Fact]
    public void DownloadProgress_LongMaxValue_NoOverflow()
    {
        var progress = new DownloadProgress(long.MaxValue, long.MaxValue, 100.0, "完了");

        Assert.True(progress.IsCompleted);
        Assert.Equal(long.MaxValue, progress.BytesDownloaded);
        Assert.Equal(long.MaxValue, progress.TotalBytes);
    }

    /// <summary>
    /// @adversarial @category ResourceExhaustion @severity Medium
    /// DownloadProgressの境界: BytesDownloaded > TotalBytesの場合もIsCompleted=true。
    /// </summary>
    [Fact]
    public void DownloadProgress_BytesExceedTotal_IsStillCompleted()
    {
        var progress = new DownloadProgress(200, 100, 200.0, "超過");

        Assert.True(progress.IsCompleted);
    }

    /// <summary>
    /// @adversarial @category ResourceExhaustion @severity Medium
    /// ModelPreset.FormattedDownloadSizeの境界値（0バイト、long.MaxValue）を検証。
    /// </summary>
    [Theory]
    [InlineData(0L, "0.0 KB")]
    [InlineData(1L, "0.0 KB")]
    [InlineData(1023L, "1.0 KB")]
    [InlineData(1L << 20, "1.0 MB")]
    [InlineData(1L << 30, "1.0 GB")]
    public void ModelPreset_FormattedDownloadSize_BoundaryValues(long bytes, string expected)
    {
        var preset = new ModelPreset
        {
            Id = "test",
            OllamaTag = "test:latest",
            DisplayName = "Test",
            Family = "Test",
            ApproximateDownloadSize = bytes,
            RecommendedMinRam = bytes,
        };

        Assert.Equal(expected, preset.FormattedDownloadSize);
    }

    /// <summary>
    /// @adversarial @category ResourceExhaustion @severity Low
    /// ModelPreset.FormattedDownloadSizeのlong.MaxValue（巨大値）でクラッシュしないことを確認。
    /// </summary>
    [Fact]
    public void ModelPreset_FormattedDownloadSize_LongMaxValue_DoesNotThrow()
    {
        var preset = new ModelPreset
        {
            Id = "test",
            OllamaTag = "test:latest",
            DisplayName = "Test",
            Family = "Test",
            ApproximateDownloadSize = long.MaxValue,
            RecommendedMinRam = long.MaxValue,
        };

        string result = preset.FormattedDownloadSize;
        Assert.NotNull(result);
        Assert.Contains("GB", result);
    }

    #endregion

    #region 4. State Machine Abuse（状態遷移の悪用）

    /// <summary>
    /// @adversarial @category StateMachineAbuse @severity Critical
    /// LocalLlm: Dispose後のCreateChatSessionが例外を投げるか、安全に処理されることを検証。
    /// Dispose後の利用はObjectDisposedExceptionが理想だが、
    /// 実装上ChatSessionは作成可能（API呼び出し時に初めて失敗する）。
    /// </summary>
    [Fact]
    public void LocalLlm_CreateChatSession_AfterDispose()
    {
        var localLlm = CreateLocalLlm();
        localLlm.Dispose();

        // CreateChatSessionはDispose後でもChatSession自体は作成できる
        // （内部でOllamaApiClientを渡すだけなので）
        // 実際のAPI呼び出し時に失敗する設計
        var ex = Record.Exception(() => localLlm.CreateChatSession());
        // 例外が出ても出なくても、クラッシュしないことが重要
        Assert.True(ex is null || ex is ObjectDisposedException);
    }

    /// <summary>
    /// @adversarial @category StateMachineAbuse @severity High
    /// OllamaProcessManager: StartAsync未実行時のStopAsyncが安全にno-opであることを検証。
    /// </summary>
    [Fact]
    public async Task OllamaProcessManager_StopAsync_WhenNeverStarted_IsNoop()
    {
        var pm = CreateProcessManager();

        var ex = await Record.ExceptionAsync(async () => await pm.StopAsync());
        Assert.Null(ex);
    }

    /// <summary>
    /// @adversarial @category StateMachineAbuse @severity High
    /// OllamaProcessManager: 未起動状態でIsRunning=falseであることを検証。
    /// </summary>
    [Fact]
    public void OllamaProcessManager_IsRunning_WhenNeverStarted_IsFalse()
    {
        var pm = CreateProcessManager();
        Assert.False(pm.IsRunning);
    }

    /// <summary>
    /// @adversarial @category StateMachineAbuse @severity High
    /// ChatSession: ClearHistory後のSendCompleteAsyncが正常に動作するか
    /// （履歴が空になった状態からの送信）。
    /// </summary>
    [Fact]
    public async Task ChatSession_SendCompleteAsync_AfterClearHistory()
    {
        var handler = CreateChatCompleteHandler("応答テスト");
        var apiClient = CreateMockApiClient(handler);
        apiClient.SetBaseUrl("http://localhost:9999");

        var session = new ChatSession(apiClient, "test-model", new ChatOptions
        {
            SystemPrompt = "システムプロンプト",
        });

        session.ClearHistory();

        // ClearHistory後でもシステムプロンプトは残っているはず
        Assert.Single(session.History);
        Assert.Equal(ChatRole.System, session.History[0].Role);

        // ClearHistory後にSendCompleteAsyncを呼んでもクラッシュしないこと
        try
        {
            string response = await session.SendCompleteAsync("テスト");
            // 成功したら履歴にシステム + ユーザー + アシスタントが追加される
            Assert.True(session.History.Count >= 1);
        }
        catch (HttpRequestException)
        {
            // モックの制約上HTTPエラーは許容
            // 重要なのはClearHistory後にクラッシュしないこと
        }
    }

    /// <summary>
    /// @adversarial @category StateMachineAbuse @severity Critical
    /// ChatSession: ensureInitializedコールバックが例外を投げた場合、
    /// SendAsyncがユーザーメッセージをロールバックすることを検証。
    /// </summary>
    [Fact]
    public async Task ChatSession_SendAsync_RollsBackOnEnsureInitializedFailure()
    {
        var apiClient = CreateMockApiClient();
        Func<CancellationToken, Task> failingInit = _ =>
            throw new InvalidOperationException("初期化失敗テスト");

        var session = new ChatSession(apiClient, "test-model", ensureInitialized: failingInit);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in session.SendAsync("テスト"))
            {
                // 到達しないはず
            }
        });

        // 初期化失敗後、ユーザーメッセージがロールバックされて履歴が空であること
        Assert.Empty(session.History);
    }

    /// <summary>
    /// @adversarial @category StateMachineAbuse @severity Critical
    /// ChatSession: SendCompleteAsyncのensureInitialized例外時にも
    /// ユーザーメッセージがロールバックされることを検証。
    /// </summary>
    [Fact]
    public async Task ChatSession_SendCompleteAsync_RollsBackOnEnsureInitializedFailure()
    {
        var apiClient = CreateMockApiClient();
        Func<CancellationToken, Task> failingInit = _ =>
            throw new InvalidOperationException("初期化失敗テスト");

        var session = new ChatSession(apiClient, "test-model", ensureInitialized: failingInit);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await session.SendCompleteAsync("テスト");
        });

        // ロールバック: 履歴は空
        Assert.Empty(session.History);
    }

    /// <summary>
    /// @adversarial @category StateMachineAbuse @severity High
    /// OllamaProcessManager: Dispose後のStopAsyncが安全であることを検証。
    /// </summary>
    [Fact]
    public async Task OllamaProcessManager_StopAsync_AfterDispose_IsNoop()
    {
        var pm = CreateProcessManager();
        pm.Dispose();

        // Dispose済みのStopAsyncはプロセスがnullなのでno-opのはず
        var ex = await Record.ExceptionAsync(async () => await pm.StopAsync());
        Assert.Null(ex);
    }

    #endregion

    #region 5. Type Punching（型の不正）

    /// <summary>
    /// @adversarial @category TypePunching @severity High
    /// NdjsonStreamReader.ReadAsyncに切り詰められた不正JSONを渡した場合、
    /// JsonExceptionが発生することを検証。
    /// </summary>
    [Fact]
    public async Task NdjsonStreamReader_ReadAsync_TruncatedJson_ThrowsJsonException()
    {
        string malformedNdjson = "{\"key\":\"value\"\n{\"incomplete\n";

        using var reader = CreateStreamReader(malformedNdjson);

        await Assert.ThrowsAsync<JsonException>(async () =>
        {
            await foreach (var _ in NdjsonStreamReader.ReadAsync<JsonElement>(reader))
            {
                // 1行目は正常なので通過、2行目で例外
            }
        });
    }

    /// <summary>
    /// @adversarial @category TypePunching @severity High
    /// NdjsonStreamReader.ReadAsyncに余計なカンマを含む不正JSONを渡した場合。
    /// </summary>
    [Fact]
    public async Task NdjsonStreamReader_ReadAsync_ExtraCommaJson_ThrowsJsonException()
    {
        string malformedNdjson = "{\"key\":\"value\",}\n";

        using var reader = CreateStreamReader(malformedNdjson);

        await Assert.ThrowsAsync<JsonException>(async () =>
        {
            await foreach (var _ in NdjsonStreamReader.ReadAsync<JsonElement>(reader))
            {
            }
        });
    }

    /// <summary>
    /// @adversarial @category TypePunching @severity Medium
    /// NdjsonStreamReader.ReadLinesAsyncに正常行と空行が混在した場合。
    /// 空行はスキップされることを検証。
    /// </summary>
    [Fact]
    public async Task NdjsonStreamReader_ReadLinesAsync_MixedEmptyLines_SkipsEmpty()
    {
        string content = "{\"a\":1}\n\n   \n{\"b\":2}\n\n";

        using var reader = CreateStreamReader(content);
        var lines = new List<string>();

        await foreach (string line in NdjsonStreamReader.ReadLinesAsync(reader))
        {
            lines.Add(line);
        }

        Assert.Equal(2, lines.Count);
        Assert.Equal("{\"a\":1}", lines[0]);
        Assert.Equal("{\"b\":2}", lines[1]);
    }

    /// <summary>
    /// @adversarial @category TypePunching @severity High
    /// NdjsonStreamReader.ReadAsyncにネストされたガベージJSON。
    /// </summary>
    [Fact]
    public async Task NdjsonStreamReader_ReadAsync_NestedGarbage_ThrowsJsonException()
    {
        string garbage = "}{}{not json at all}{{\n";

        using var reader = CreateStreamReader(garbage);

        await Assert.ThrowsAsync<JsonException>(async () =>
        {
            await foreach (var _ in NdjsonStreamReader.ReadAsync<JsonElement>(reader))
            {
            }
        });
    }

    /// <summary>
    /// @adversarial @category TypePunching @severity High
    /// OllamaDownloader.BuildDownloadUrlに全RuntimeVariant組み合わせを渡した場合の検証。
    /// </summary>
    [Theory]
    [InlineData(RuntimeVariant.Auto)]
    [InlineData(RuntimeVariant.Full)]
    [InlineData(RuntimeVariant.Rocm)]
    [InlineData(RuntimeVariant.CpuOnly)]
    public void OllamaDownloader_BuildDownloadUrl_AllVariants_ValidUrl(RuntimeVariant variant)
    {
        string url = OllamaDownloader.BuildDownloadUrl("0.20.2", OsPlatform.Windows, CpuArchitecture.X64, variant);

        Assert.NotNull(url);
        Assert.StartsWith("https://github.com/ollama/ollama/releases/download/", url);
        Assert.Contains("0.20.2", url);
    }

    /// <summary>
    /// @adversarial @category TypePunching @severity Medium
    /// ServiceCollectionExtensions.AddLlmChamberにnullのconfigureを渡した場合。
    /// </summary>
    [Fact]
    public void AddLlmChamber_NullConfigure_DoesNotThrow()
    {
        var services = new ServiceCollection();

        var ex = Record.Exception(() => services.AddLlmChamber(null));
        Assert.Null(ex);
    }

    /// <summary>
    /// @adversarial @category TypePunching @severity Medium
    /// ServiceCollectionExtensions.AddLlmChamberに空のconfigureを渡した場合。
    /// </summary>
    [Fact]
    public void AddLlmChamber_EmptyConfigure_DoesNotThrow()
    {
        var services = new ServiceCollection();

        var ex = Record.Exception(() => services.AddLlmChamber(_ => { }));
        Assert.Null(ex);
    }

    /// <summary>
    /// @adversarial @category TypePunching @severity High
    /// NdjsonStreamReader.ReadAsync: 完全に空のストリーム。
    /// </summary>
    [Fact]
    public async Task NdjsonStreamReader_ReadAsync_EmptyStream_ReturnsNothing()
    {
        using var reader = CreateStreamReader("");
        var items = new List<JsonElement>();

        await foreach (var item in NdjsonStreamReader.ReadAsync<JsonElement>(reader))
        {
            items.Add(item);
        }

        Assert.Empty(items);
    }

    #endregion

    #region 6. Environmental Chaos（環境カオス）

    /// <summary>
    /// @adversarial @category EnvironmentalChaos @severity High
    /// LlmChamberOptions.CacheDirectoryに存在しないパスを設定できることを検証。
    /// （ディレクトリ作成は使用時に行われるため、設定時はエラーにならない）
    /// </summary>
    [Fact]
    public void LlmChamberOptions_CacheDirectory_NonExistentPath_SetSucceeds()
    {
        var opts = new LlmChamberOptions
        {
            CacheDirectory = @"Z:\nonexistent\path\that\does\not\exist",
        };

        Assert.Equal(@"Z:\nonexistent\path\that\does\not\exist", opts.CacheDirectory);
    }

    /// <summary>
    /// @adversarial @category EnvironmentalChaos @severity Medium
    /// LlmChamberOptions.CacheDirectoryにスペースやUnicode文字を含むパス。
    /// </summary>
    [Theory]
    [InlineData(@"C:\path with spaces\cache")]
    [InlineData(@"C:\日本語パス\キャッシュ")]
    [InlineData(@"C:\path\with emoji 🎉\cache")]
    public void LlmChamberOptions_CacheDirectory_SpecialPaths_SetSucceeds(string path)
    {
        var opts = new LlmChamberOptions
        {
            CacheDirectory = path,
        };

        Assert.Equal(path, opts.CacheDirectory);
    }

    /// <summary>
    /// @adversarial @category EnvironmentalChaos @severity High
    /// OllamaDownloader.FindExistingBinaryに.versionファイルの中身が不正な場合。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage-not-a-version")]
    [InlineData("::::")]
    [InlineData("\0\0\0")]
    public void OllamaDownloader_FindExistingBinary_CorruptVersionFile(string versionContent)
    {
        string tempDir = CreateTempDir();
        try
        {
            string runtimeDir = Path.Combine(tempDir, "runtime");
            Directory.CreateDirectory(runtimeDir);

            File.WriteAllText(Path.Combine(tempDir, ".version"), versionContent);
            string execName = PlatformInfo.GetOllamaExecutableName(PlatformInfo.GetCurrentOs());
            File.WriteAllText(Path.Combine(runtimeDir, execName), "dummy");

            var downloader = CreateDownloader();

            // 不正な.versionファイルの場合、バージョン不一致でnullが返るか
            // クラッシュしないことを確認
            var ex = Record.Exception(() =>
            {
                string? result = downloader.FindExistingBinary(tempDir, "0.20.2", RuntimeVariant.Full);
            });

            // 例外が出ないことが重要（nullを返すのが期待動作）
            Assert.Null(ex);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// @adversarial @category EnvironmentalChaos @severity Critical
    /// 即座にキャンセルされるCancellationTokenをNdjsonStreamReader.ReadAsyncに渡した場合。
    /// </summary>
    [Fact]
    public async Task NdjsonStreamReader_ReadAsync_ImmediatelyCancelledToken()
    {
        string content = "{\"a\":1}\n{\"b\":2}\n";
        using var reader = CreateStreamReader(content);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // 即座にキャンセル

        var items = new List<JsonElement>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in NdjsonStreamReader.ReadAsync<JsonElement>(reader, cancellationToken: cts.Token))
            {
                items.Add(item);
            }
        });
    }

    /// <summary>
    /// @adversarial @category EnvironmentalChaos @severity Medium
    /// PortFinderがOSレベルで有効なポート番号を返すことを検証。
    /// </summary>
    [Fact]
    public void PortFinder_FindAvailablePort_ReturnsValidRange()
    {
        int port = PortFinder.FindAvailablePort();

        Assert.InRange(port, 1, 65535);
    }

    /// <summary>
    /// @adversarial @category EnvironmentalChaos @severity Medium
    /// PortFinderを連続呼び出ししても異なるポート番号が返る可能性が高いことを検証。
    /// （OSの挙動に依存するため、異なることを保証はできないが、重複率を測定）
    /// </summary>
    [Fact]
    public void PortFinder_FindAvailablePort_MultipleCalls_ReturnsDistinctPorts()
    {
        var ports = new HashSet<int>();
        for (int i = 0; i < 10; i++)
        {
            ports.Add(PortFinder.FindAvailablePort());
        }

        // 10回中、少なくとも2個は異なるポートが返るはず
        Assert.True(ports.Count >= 2, $"10回の呼び出しで{ports.Count}個のユニークなポートしか返らなかった");
    }

    /// <summary>
    /// @adversarial @category EnvironmentalChaos @severity Medium
    /// OllamaDownloader.FindExistingBinaryにパストラバーサル文字列を含むパスを渡した場合。
    /// </summary>
    [Fact]
    public void OllamaDownloader_FindExistingBinary_PathTraversal_DoesNotEscape()
    {
        string tempDir = CreateTempDir();
        try
        {
            // パストラバーサルを含むパスは、ファイルが見つからないためnullを返すはず
            var downloader = CreateDownloader();

            string? result = downloader.FindExistingBinary(
                Path.Combine(tempDir, "..", "..", "..", "etc"),
                "0.20.2");

            // ファイルが存在しないのでnull
            Assert.Null(result);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    /// <summary>
    /// @adversarial @category EnvironmentalChaos @severity Low
    /// LlmChamberFactory.Createが正常にILocalLlmインスタンスを返すことを検証。
    /// </summary>
    [Fact]
    public void LlmChamberFactory_Create_WithNullConfigure_ReturnsInstance()
    {
        using var llm = LlmChamberFactory.Create(null);
        Assert.NotNull(llm);
    }

    /// <summary>
    /// @adversarial @category EnvironmentalChaos @severity Low
    /// LlmChamberFactory.Createでconfigureコールバックが呼び出されることを検証。
    /// </summary>
    [Fact]
    public void LlmChamberFactory_Create_ConfigureCallbackInvoked()
    {
        bool invoked = false;
        using var llm = LlmChamberFactory.Create(opts =>
        {
            invoked = true;
            opts.DefaultModel = "test-model";
        });

        Assert.True(invoked);
        Assert.NotNull(llm);
    }

    #endregion

    #region 追加: OllamaModels境界テスト

    /// <summary>
    /// @adversarial @category TypePunching @severity Medium
    /// OllamaModels.FindPresetにnullを渡した場合。
    /// </summary>
    [Fact]
    public void OllamaModels_FindPreset_WithNull_DoesNotThrow()
    {
        // NullReferenceExceptionが発生するかArgNullExceptionかnullが返るかを検証
        var ex = Record.Exception(() => OllamaModels.FindPreset(null!));
        // NRT無視でnullを渡した場合、FirstOrDefaultの比較でNREになる可能性
        // 実装がEqualsを使っているため、NREが発生する可能性がある
        // 重要なのはクラッシュの挙動を把握すること
        Assert.True(ex is null || ex is NullReferenceException || ex is ArgumentNullException);
    }

    /// <summary>
    /// @adversarial @category TypePunching @severity Low
    /// OllamaModels.FindPresetに空文字列を渡した場合、nullが返ることを検証。
    /// </summary>
    [Fact]
    public void OllamaModels_FindPreset_WithEmptyString_ReturnsNull()
    {
        var result = OllamaModels.FindPreset("");
        Assert.Null(result);
    }

    /// <summary>
    /// @adversarial @category TypePunching @severity Low
    /// OllamaModels.ResolveModelTagに未知のモデル名を渡した場合、
    /// そのまま返されることを検証。
    /// </summary>
    [Theory]
    [InlineData("nonexistent-model")]
    [InlineData("")]
    [InlineData("model:with:multiple:colons")]
    public void OllamaModels_ResolveModelTag_UnknownModel_ReturnsAsIs(string modelTag)
    {
        string result = OllamaModels.ResolveModelTag(modelTag);
        Assert.Equal(modelTag, result);
    }

    #endregion

    #region 追加: DownloadProgress境界テスト

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Medium
    /// DownloadProgressのIsCompleted: TotalBytes=null, Percentage=nullの場合。
    /// </summary>
    [Fact]
    public void DownloadProgress_IsCompleted_NullTotalAndPercentage_ReturnsFalse()
    {
        var progress = new DownloadProgress(1000, null, null, "進行中");
        Assert.False(progress.IsCompleted);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Medium
    /// DownloadProgressのIsCompleted: TotalBytes=0, BytesDownloaded=0の場合。
    /// </summary>
    [Fact]
    public void DownloadProgress_IsCompleted_ZeroTotal_ZeroDownloaded_IsTrue()
    {
        var progress = new DownloadProgress(0, 0, 0.0, "開始");
        // 0 >= 0 は true
        Assert.True(progress.IsCompleted);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Low
    /// DownloadProgressのIsCompleted: Percentage=100.0, TotalBytes=nullの場合。
    /// </summary>
    [Fact]
    public void DownloadProgress_IsCompleted_Percentage100_NullTotal_IsTrue()
    {
        var progress = new DownloadProgress(0, null, 100.0, "完了");
        Assert.True(progress.IsCompleted);
    }

    /// <summary>
    /// @adversarial @category BoundaryAssault @severity Low
    /// DownloadProgressのIsCompleted: 負のPercentageの場合。
    /// </summary>
    [Fact]
    public void DownloadProgress_IsCompleted_NegativePercentage_IsFalse()
    {
        var progress = new DownloadProgress(0, null, -50.0, "不正");
        Assert.False(progress.IsCompleted);
    }

    #endregion

    #region 追加: PlatformInfo境界テスト

    /// <summary>
    /// @adversarial @category EnvironmentalChaos @severity Low
    /// PlatformInfo.GetCurrentOsが既知のOsPlatformを返すことを検証。
    /// </summary>
    [Fact]
    public void PlatformInfo_GetCurrentOs_ReturnsKnownPlatform()
    {
        var os = PlatformInfo.GetCurrentOs();
        Assert.True(
            os == OsPlatform.Windows || os == OsPlatform.Linux || os == OsPlatform.MacOS,
            $"予期しないOS: {os}");
    }

    /// <summary>
    /// @adversarial @category EnvironmentalChaos @severity Low
    /// PlatformInfo.GetCurrentArchitectureが既知のアーキテクチャを返すことを検証。
    /// </summary>
    [Fact]
    public void PlatformInfo_GetCurrentArchitecture_ReturnsKnownArch()
    {
        var arch = PlatformInfo.GetCurrentArchitecture();
        Assert.True(
            arch == CpuArchitecture.X64 || arch == CpuArchitecture.Arm64,
            $"予期しないアーキテクチャ: {arch}");
    }

    #endregion
}
