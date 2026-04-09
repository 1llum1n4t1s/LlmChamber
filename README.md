# LlmChamber

**NuGet一発、ゼロ設定、環境汚染なし** — .NETアプリにローカルLLMを組み込むライブラリ

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-blue)](https://dotnet.microsoft.com/)

## 特徴

- **NuGet一発で動く** — `dotnet add package LlmChamber` だけ。Python不要、GPU不要
- **環境汚染なし** — Ollamaバイナリをアプリローカルに自動配置。グローバルインストール不要
- **モデル自動管理** — 初回実行時にOllama DL + モデルpullが全て自動
- **型安全なC# API** — `IAsyncEnumerable<string>` でストリーミング応答
- **GPU自動検出** — Nvidia(CUDA) / AMD(ROCm) / Intel / NPU を自動検出して最適なバイナリを選択
- **UIコントロール付き** — WPF / Avalonia / WinForms / MAUI 用のチャットコントロールを同梱

## クイックスタート

### 5行で動く最小コード

```csharp
await using var llm = LlmChamberFactory.Create();

await foreach (var chunk in llm.GenerateAsync("日本の首都は？"))
{
    Console.Write(chunk);
}
```

初回実行時にOllamaランタイムとGemma 4 E2Bモデルが自動でダウンロードされます。2回目以降はキャッシュから即座に起動します。

### チャットセッション

```csharp
await using var llm = LlmChamberFactory.Create(options =>
{
    options.DefaultModel = "gemma4-e2b";
    options.RuntimeVariant = RuntimeVariant.Auto;
});

var session = llm.CreateChatSession(new ChatOptions
{
    SystemPrompt = "あなたは親切なアシスタントです。日本語で回答してください。",
});

// ストリーミング応答
await foreach (var chunk in session.SendAsync("こんにちは！"))
{
    Console.Write(chunk);
}

// 会話履歴は自動管理される
await foreach (var chunk in session.SendAsync("さっき何を聞いた？"))
{
    Console.Write(chunk);
}
```

### DI（Dependency Injection）

```csharp
services.AddLlmChamber(options =>
{
    options.DefaultModel = "qwen3.5-2b";
    options.RuntimeVariant = RuntimeVariant.Full;
});

// コンストラクタインジェクションで使用
public class MyService(ILocalLlm llm)
{
    public async Task<string> AskAsync(string question)
        => await llm.GenerateCompleteAsync(question);
}
```

#### HttpClientのカスタマイズ（プロキシ・証明書等）

LlmChamberは内部で2つの `HttpClient` を Keyed Services で登録しています。`AddLlmChamber()` の前に独自の `HttpClient` を登録すれば差し替え可能です:

```csharp
// プロキシ経由でGitHub Releasesからダウンロードする例
services.AddKeyedSingleton<HttpClient>(LlmChamberHttpClients.Downloader, (sp, key) =>
    new HttpClient(new HttpClientHandler { Proxy = new WebProxy("http://proxy:8080") }));

// Ollama APIクライアントのカスタマイズ
services.AddKeyedSingleton<HttpClient>(LlmChamberHttpClients.Api, (sp, key) =>
    new HttpClient(customHandler));

services.AddLlmChamber();
```

## NuGetパッケージ

| パッケージ | 用途 |
|---|---|
| `LlmChamber` | コンソール・WebAPI・ヘッドレス用 |
| `LlmChamber.Wpf` | WPF用チャットコントロール |
| `LlmChamber.Avalonia` | Avalonia UI用チャットコントロール（Win/macOS/Linux） |
| `LlmChamber.WinForms` | WinForms用チャットパネル |
| `LlmChamber.Maui` | .NET MAUI用チャットビュー |

UIパッケージにはCoreが内蔵されているため、追加でCoreパッケージを参照する必要はありません。

## 組込みモデルプリセット

| プリセットID | モデル | DLサイズ | 推奨RAM | 特徴 |
|---|---|---|---|---|
| `gemma4-e2b` | Gemma 4 E2B | ~3 GB | 5 GB | 最軽量。CPU推論に最適。マルチモーダル |
| `gemma4-e4b` | Gemma 4 E4B | ~5 GB | 8 GB | 中型。音声入力対応 |
| `qwen3.5-2b` | Qwen 3.5 2B | ~2 GB | 4 GB | 日本語・多言語が優秀 |
| `phi4-mini` | Phi-4 Mini | ~3 GB | 6 GB | 数学・コーディングに強い |

カスタムモデルも直接Ollamaタグで指定可能:

```csharp
var llm = LlmChamberFactory.Create(o => o.DefaultModel = "llama3.2:1b");
```

## RuntimeVariant（GPU選択）

| バリアント | 説明 |
|---|---|
| `Auto` (デフォルト) | GPU/NPUを自動検出して最適なバイナリを選択 |
| `Full` | CUDA対応フルバイナリ（Nvidia GPU向け） |
| `Rocm` | AMD ROCm対応バイナリ |
| `CpuOnly` | CPU-only（GPUなし環境向け） |

## 設定オプション

```csharp
var llm = LlmChamberFactory.Create(options =>
{
    options.DefaultModel = "gemma4-e2b";       // デフォルトモデル
    options.RuntimeVariant = RuntimeVariant.Auto; // GPU自動検出
    options.CacheDirectory = "~/.llmchamber";  // キャッシュ先
    options.AutoDownloadRuntime = true;         // ランタイム自動DL
    options.AutoPullModel = true;              // モデル自動pull
    options.StartupTimeout = TimeSpan.FromSeconds(30);
    options.SharedModelDirectory = null;        // グローバルOllamaとモデル共有する場合に設定
});
```

## ダウンロード進捗

```csharp
llm.RuntimeDownloadProgress += (_, p) =>
    Console.Write($"\rランタイム: {p.Percentage:F1}%");

llm.ModelDownloadProgress += (_, p) =>
    Console.Write($"\rモデル: {p.Percentage:F1}%");

await llm.InitializeAsync();
```

## WPF / Avalonia でのUI利用

```xml
<!-- WPF -->
<controls:ChatControl x:Name="ChatControl"/>

<!-- Avalonia -->
<controls:ChatControl x:Name="ChatControl"/>
```

```csharp
// LLMインスタンスをコントロールに接続するだけでチャットUI完成
ChatControl.LlmInstance = llm;
```

## 例外ハンドリング

```csharp
try
{
    await llm.InitializeAsync();
}
catch (UnsupportedPlatformException ex)
{
    // サポート外のOS/アーキテクチャ（PlatformNotSupportedException派生）
    Console.WriteLine($"未対応: {ex.DetectedOs} / {ex.DetectedArchitecture}");
}
catch (RuntimeInstallException ex)
{
    // ランタイムのダウンロード・展開失敗
    Console.WriteLine($"インストール失敗: {ex.Message}");
}
catch (ProcessStartException ex)
{
    // Ollamaプロセスの起動失敗
}
catch (OllamaApiException ex)
{
    // Ollama APIエラー（モデルpull失敗等）
}
```

全ての例外は `LlmChamberException` を基底クラスとしています（`UnsupportedPlatformException` のみ `PlatformNotSupportedException` 派生）。

## 動作要件

- .NET 8.0 以上
- Windows / macOS / Linux
- 初回のみインターネット接続が必要（Ollamaランタイム + モデルダウンロード）
- 2回目以降は完全オフラインで動作

## 既存Ollamaとの共存

LlmChamberは独自のポートとモデルディレクトリで動作するため、グローバルにインストール済みのOllamaに一切干渉しません。既存モデルを共有したい場合は `SharedModelDirectory` を設定してください。

## ライセンス

[MIT License](LICENSE)

## 参考リンク

- [Ollama](https://ollama.com/) — ローカルLLMランタイム
- [Ollama HTTP API](https://github.com/ollama/ollama/blob/main/docs/api.md)
