using System.IO;
using Microsoft.Extensions.Options;
using SuperLightLogger;

namespace LlmChamber.Internal;

/// <summary>IRuntimeManagerの実装。</summary>
internal sealed class RuntimeManager : IRuntimeManager
{
    private readonly OllamaDownloader _downloader;
    private readonly OllamaApiClient _apiClient;
    private readonly OllamaProcessManager _processManager;
    private readonly LlmChamberOptions _options;
    private static readonly ILog _logger = LogManager.GetLogger<RuntimeManager>();

    public RuntimeManager(
        OllamaDownloader downloader,
        OllamaApiClient apiClient,
        OllamaProcessManager processManager,
        IOptions<LlmChamberOptions> options)
    {
        _downloader = downloader;
        _apiClient = apiClient;
        _processManager = processManager;
        _options = options.Value;
    }

    public async Task<string> EnsureRuntimeAsync(
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var variant = _options.RuntimeVariant == RuntimeVariant.Auto
            ? GpuDetector.DetectRecommendedVariant()
            : _options.RuntimeVariant;

        // AutoDownloadRuntimeが無効の場合、既存バイナリのみチェック（バリアントも一致確認）
        if (!_options.AutoDownloadRuntime)
        {
            string? existingBinary = _downloader.FindExistingBinary(_options.CacheDirectory, _options.OllamaVersion, variant);
            if (existingBinary is not null) return existingBinary;

            throw new RuntimeNotFoundException(
                "Ollamaランタイムが見つかりません。AutoDownloadRuntimeが無効のため自動ダウンロードできません。");
        }

        return await _downloader.DownloadAsync(
            _options.CacheDirectory, variant, _options.OllamaVersion, progress, cancellationToken);
    }

    public async Task<string?> GetRuntimeVersionAsync(CancellationToken cancellationToken = default)
    {
        // プロセスが稼働中ならAPIから取得
        if (_processManager.IsRunning)
        {
            try
            {
                return await _apiClient.GetVersionAsync(cancellationToken);
            }
            catch
            {
                // API失敗時はキャッシュから取得にフォールバック
            }
        }

        // キャッシュの.versionマーカーからバージョンを読み取る
        string versionMarkerPath = Path.Combine(_options.CacheDirectory, ".version");
        if (!File.Exists(versionMarkerPath)) return null;

        string marker = (await File.ReadAllTextAsync(versionMarkerPath, cancellationToken)).Trim();
        // マーカー形式: "version:variant" または "version"
        int colonIndex = marker.IndexOf(':');
        return colonIndex >= 0 ? marker[..colonIndex] : marker;
    }

    public async Task EnsureModelAsync(
        string modelTag,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureProcessRunningAsync(cancellationToken);
        string resolvedTag = OllamaModels.ResolveModelTag(modelTag);

        // モデルが既にあるか確認（完全一致またはタグ付き一致）
        var tags = await _apiClient.ListModelsAsync(cancellationToken);
        if (tags.Models.Any(m => IsModelMatch(m.Name, resolvedTag)))
        {
            _logger.Debug($"モデル '{resolvedTag}' は既にpull済みです。");
            return;
        }

        _logger.Info($"モデル '{resolvedTag}' をpull中...");

        await foreach (var chunk in _apiClient.PullModelAsync(resolvedTag, cancellationToken))
        {
            if (chunk.Total.HasValue && chunk.Completed.HasValue)
            {
                double pct = (double)chunk.Completed.Value / chunk.Total.Value * 100.0;
                progress?.Report(new DownloadProgress(chunk.Completed.Value, chunk.Total.Value, pct, chunk.Status));
            }
            else
            {
                progress?.Report(new DownloadProgress(0, null, null, chunk.Status));
            }
        }

        _logger.Info($"モデル '{resolvedTag}' のpull完了。");
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureProcessRunningAsync(cancellationToken);
        var tags = await _apiClient.ListModelsAsync(cancellationToken);
        return tags.Models.Select(m => new ModelInfo
        {
            Name = m.Name,
            Size = m.Size,
            ModifiedAt = m.ModifiedAt,
            Digest = m.Digest,
        }).ToList();
    }

    public async Task DeleteModelAsync(string modelTag, CancellationToken cancellationToken = default)
    {
        await EnsureProcessRunningAsync(cancellationToken);
        string resolvedTag = OllamaModels.ResolveModelTag(modelTag);
        await _apiClient.DeleteModelAsync(resolvedTag, cancellationToken);
        _logger.Info($"モデル '{resolvedTag}' を削除しました。");
    }

    public async Task<long> GetCacheSizeBytesAsync(CancellationToken cancellationToken = default)
    {
        string cacheDir = _options.CacheDirectory;
        if (!Directory.Exists(cacheDir)) return 0L;

        return await Task.Run(() =>
        {
            long size = 0;
            foreach (var file in new DirectoryInfo(cacheDir).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                size += file.Length;
            }
            return size;
        }, cancellationToken);
    }

    public IReadOnlyList<ModelPreset> GetAvailablePresets() => OllamaModels.Presets;

    /// <summary>Ollamaプロセスが起動していなければ起動する。</summary>
    private async Task EnsureProcessRunningAsync(CancellationToken cancellationToken)
    {
        if (_processManager.IsRunning) return;

        string binaryPath = await EnsureRuntimeAsync(cancellationToken: cancellationToken);
        await _processManager.StartAsync(binaryPath, cancellationToken);
        _apiClient.SetBaseUrl(_processManager.BaseUrl);
    }

    /// <summary>モデル名の一致判定。"model:tag"形式と"model"形式の両方に対応。</summary>
    private static bool IsModelMatch(string installedName, string requestedTag)
    {
        // 完全一致
        if (string.Equals(installedName, requestedTag, StringComparison.OrdinalIgnoreCase))
            return true;
        // "model:latest" と "model" の一致（双方向）
        // installed="model" requested="model:latest" → 一致
        if (string.Equals($"{installedName}:latest", requestedTag, StringComparison.OrdinalIgnoreCase))
            return true;
        // installed="model:latest" requested="model" → 一致
        if (string.Equals(installedName, $"{requestedTag}:latest", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
