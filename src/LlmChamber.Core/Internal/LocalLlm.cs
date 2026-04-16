using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using SuperLightLogger;

namespace LlmChamber.Internal;

/// <summary>ILocalLlmの実装。全体のオーケストレーション。</summary>
internal sealed class LocalLlm : ILocalLlm
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly OllamaDownloader _downloader;
    private readonly OllamaProcessManager _processManager;
    private readonly OllamaApiClient _apiClient;
    private readonly LlmChamberOptions _options;
    private static readonly ILog _logger = LogManager.GetLogger<LocalLlm>();
    private volatile bool _initialized;
    private volatile bool _disposed;

    public LocalLlm(
        IOptions<LlmChamberOptions> options,
        OllamaDownloader downloader,
        OllamaProcessManager processManager,
        OllamaApiClient apiClient,
        IRuntimeManager runtimeManager)
    {
        _options = options.Value;
        _downloader = downloader;
        _processManager = processManager;
        _apiClient = apiClient;
        Runtime = runtimeManager;
    }

    public bool IsReady => _initialized && _processManager.IsRunning;
    public IRuntimeManager Runtime { get; }

    public event EventHandler<DownloadProgress>? RuntimeDownloadProgress;
    public event EventHandler<DownloadProgress>? ModelDownloadProgress;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized && _processManager.IsRunning) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized && _processManager.IsRunning) return;

            // 1. ランタイムを確保
            var runtimeProgress = new Progress<DownloadProgress>(p =>
                RuntimeDownloadProgress?.Invoke(this, p));

            string binaryPath = await Runtime.EnsureRuntimeAsync(runtimeProgress, cancellationToken);

            // 2. プロセス起動
            await _processManager.StartAsync(binaryPath, cancellationToken);
            _apiClient.SetBaseUrl(_processManager.BaseUrl);

            // 3. デフォルトモデルをpull
            if (_options.AutoPullModel)
            {
                var modelProgress = new Progress<DownloadProgress>(p =>
                    ModelDownloadProgress?.Invoke(this, p));

                string modelTag = OllamaModels.ResolveModelTag(_options.DefaultModel);
                await Runtime.EnsureModelAsync(modelTag, modelProgress, cancellationToken);
            }

            _initialized = true;
            _logger.Info($"LlmChamber初期化完了。モデル: {_options.DefaultModel}");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        InferenceOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        string model = OllamaModels.ResolveModelTag(_options.DefaultModel);
        var mergedOptions = MergeOptions(options, model);

        await foreach (string chunk in _apiClient.GenerateStreamAsync(
            model, prompt, mergedOptions, cancellationToken))
        {
            yield return chunk;
        }
    }

    public async Task<string> GenerateCompleteAsync(
        string prompt,
        InferenceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        string model = OllamaModels.ResolveModelTag(_options.DefaultModel);
        var mergedOptions = MergeOptions(options, model);

        return await _apiClient.GenerateCompleteAsync(model, prompt, mergedOptions, cancellationToken);
    }

    public IChatSession CreateChatSession(ChatOptions? options = null)
    {
        string model = OllamaModels.ResolveModelTag(_options.DefaultModel);
        return new ChatSession(_apiClient, model, options, ensureInitialized: EnsureInitializedAsync);
    }

    public async Task<float[]> GetEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        string model = OllamaModels.ResolveModelTag(_options.DefaultModel);
        return await _apiClient.GetEmbeddingAsync(model, text, cancellationToken);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized || !_processManager.IsRunning)
        {
            await InitializeAsync(cancellationToken);
        }
    }

    private static InferenceOptions? MergeOptions(InferenceOptions? userOptions, string model)
    {
        if (userOptions is not null) return userOptions;

        // プリセットのデフォルトを使用
        var preset = OllamaModels.FindPreset(model);
        return preset?.DefaultInferenceOptions;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _processManager.DisposeAsync();
        _initLock.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _processManager.Dispose();
        _initLock.Dispose();
    }
}
