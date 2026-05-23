# LlmChamber

**NuGet一発、ゼロ設定、環境汚染なし** — .NETアプリにローカルLLMを組み込むライブラリ

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-blue)](https://dotnet.microsoft.com/)

## 特徴

- **NuGet一発で動く** — `dotnet add package LlmChamber` だけ。Python不要、GPU不要
- **環境汚染なし** — Ollama / Whisper / Piper / FFmpeg バイナリをアプリローカルに自動配置。グローバルインストール不要
- **モデル自動管理** — 初回実行時にランタイムDL + モデルpullが全て自動
- **マルチモーダル全部入り** — Text + Vision (画像) + Speech (音声入出力) + Video (動画解析) を 1 パッケージで提供
- **オプトイン・ゼロコスト** — `UseSpeech()` / `UseMedia()` を呼ぶまで Whisper/Piper/FFmpeg はダウンロードされず、メモリも消費しない
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

### テキストモデル

| プリセットID | モデル | DLサイズ | 推奨RAM | 特徴 |
|---|---|---|---|---|
| `gemma4-e2b` | Gemma 4 E2B | ~3 GB | 5 GB | 最軽量。CPU推論に最適 |
| `gemma4-e4b` | Gemma 4 E4B | ~5 GB | 8 GB | 中型。バランス型 |
| `qwen3.5-2b` | Qwen 3.5 2B | ~2 GB | 4 GB | 日本語・多言語が優秀 |
| `phi4-mini` | Phi-4 Mini | ~3 GB | 6 GB | 数学・コーディングに強い |

### マルチモーダル Vision モデル（画像入力対応）

| プリセットID | モデル | DLサイズ | 推奨RAM | 特徴 |
|---|---|---|---|---|
| `gemma3-4b` | Gemma 3 4B (Vision) | ~3 GB | 6 GB | Google製。汎用マルチモーダル |
| `qwen2.5vl-3b` | Qwen 2.5 VL 3B (Vision) | ~3 GB | 6 GB | OCR・画像理解に強い |
| `llava-7b` | LLaVA 7B (Vision) | ~4 GB | 8 GB | 定番Vision LLM |

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

## 🖼️ 画像入力 (Vision)

multimodal モデルを使えば画像 + テキストで質問できます。**追加のNuGet依存ゼロ**:

```csharp
await using var llm = LlmChamberFactory.Create(o => o.DefaultModel = "gemma3-4b");

byte[] imageBytes = await File.ReadAllBytesAsync("photo.jpg");

// テキスト生成（GenerateAsync）に画像を渡す
await foreach (var chunk in llm.GenerateAsync(
    "この画像に何が写っていますか？",
    images: new[] { imageBytes }))
{
    Console.Write(chunk);
}

// チャットセッションでも画像を送れる
var session = llm.CreateChatSession();
await foreach (var chunk in session.SendAsync(
    "詳しく説明してください",
    images: new[] { imageBytes }))
{
    Console.Write(chunk);
}
```

## 🎤🔊 音声入出力 (Speech)

`UseSpeech()` を呼ぶと whisper.cpp と Piper のバイナリ・モデルが自動DLされます。**呼ぶまでは何もダウンロードされません**:

```csharp
await using var llm = LlmChamberFactory.Create();
var speech = llm.UseSpeech(new SpeechOptions
{
    WhisperModel = WhisperModelSize.Small,  // tiny/base/small/medium/large から選択
    DefaultVoice = "ja_JP-takumi-medium",   // Piper voice (HuggingFace から自動DL)
});

// STT: 音声ファイル → テキスト
var result = await speech.TranscribeFileAsync("input.wav");
Console.WriteLine($"言語: {result.DetectedLanguage}");
Console.WriteLine($"全文: {result.Text}");
foreach (var seg in result.Segments ?? Array.Empty<TranscriptionSegment>())
{
    Console.WriteLine($"[{seg.Start:hh\\:mm\\:ss}] {seg.Text}");
}

// TTS: テキスト → WAV
byte[] wavBytes = await speech.SpeakAsync("こんにちは、クロちゃんです。");
await File.WriteAllBytesAsync("output.wav", wavBytes);

// ダウンロード進捗を購読
speech.ResourceDownloadProgress += (_, p) =>
    Console.WriteLine($"[{p.Status}] {p.Percentage:F1}%");
```

サポートプラットフォーム:
- **Whisper STT**: Windows AMD64 は自動DL対応。Linux/macOS は `SpeechOptions.WhisperBinaryPath` で whisper-cli への明示パス指定が必要
- **Piper TTS**: Windows/Linux x64・arm64、macOS x64・arm64 全て自動DL対応

## 🎬 動画解析 (Media)

`UseMedia()` で FFmpeg が自動DL されます。フレーム抽出 → Vision モデル解析を `IAsyncEnumerable` でストリーミング:

```csharp
await using var llm = LlmChamberFactory.Create(o => o.DefaultModel = "gemma3-4b");
var media = llm.UseMedia();

// 動画を1秒ごとに切り出して各フレームをVisionモデルで解析
await foreach (var frame in media.AnalyzeAsync("video.mp4",
    prompt: "この動画はどんなシーンですか？",
    options: new VideoAnalysisOptions
    {
        FrameIntervalSeconds = 2.0,  // 2秒ごとに1フレーム
        MaxFrames = 30,              // 最大30枚
        MaxFrameWidth = 1280,
    }))
{
    Console.WriteLine($"[{frame.Timestamp}] {frame.Description}");
}

// 解析なしのフレーム抽出のみ
await foreach (var frame in media.ExtractFramesAsync("video.mp4"))
{
    await File.WriteAllBytesAsync($"frame-{frame.FrameIndex:D5}.jpg", frame.ImageBytes);
}
```

サポートプラットフォーム:
- **FFmpeg**: Windows x64 / Linux x64・arm64 は BtbN/FFmpeg-Builds から自動DL
- macOS は `brew install ffmpeg` 推奨、`MediaOptions.FFmpegBinaryPath` で明示パス指定

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
