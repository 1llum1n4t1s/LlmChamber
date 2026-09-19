# LlmChamber

**NuGet一発、ゼロ設定、環境汚染なし** — .NETアプリにローカルLLMを組み込むライブラリ

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-blue)](https://dotnet.microsoft.com/)

## 特徴

- **NuGet一発で動く** — `dotnet add package LlmChamber` だけ。Python不要、GPU不要
- **グローバルインストール不要** — Ollama / Whisper / Piper / FFmpeg バイナリを指定キャッシュへ自動配置
- **モデル自動管理** — 初回実行時にランタイムDL + モデルpullが全て自動
- **マルチモーダル全部入り** — Text + Vision (画像) + Speech (音声入出力) + Video (動画解析) を 1 パッケージで提供
- **必要な機能だけ取得** — 音声・動画用バイナリとモデルは、各機能の初回処理時に取得
- **型安全なC# API** — `IAsyncEnumerable<string>` でストリーミング応答
- **GPU自動検出** — GPU ベンダーに応じて通常版または ROCm 版を選択。専用 NPU 推論には未対応
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

初回の推論時にOllamaランタイムとGemma 4 E2Bモデルが自動でダウンロードされます。取得済みで要求する版と一致する場合はキャッシュを再利用します。

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
    new HttpClient(new HttpClientHandler { Proxy = new WebProxy("http://proxy:8080") })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    });

// Ollama APIクライアントのカスタマイズ
services.AddKeyedSingleton<HttpClient>(LlmChamberHttpClients.Api, (sp, key) =>
    new HttpClient(customHandler)
    {
        Timeout = Timeout.InfiniteTimeSpan,
    });

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
| `qwen3.5-2b` | Qwen 3.5 2B | ~2.7 GB | 4 GB | 日本語・多言語が優秀 |
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
| `Auto` (デフォルト) | Nvidia / Intel は `Full`、AMD は `Rocm`、未検出・その他は `CpuOnly` を選択 |
| `Full` | 通常配布のバイナリ |
| `Rocm` | Windows/Linux では通常版に AMD ROCm 用追加アセットを重ねて配置 |
| `CpuOnly` | `Full` と同じ通常アセット。GPU を強制無効化する設定ではない |

macOS はいずれの指定でも共通アセットを使用します。GPU が利用できるかは Ollama と実行環境に依存します。

## 設定オプション

```csharp
var llm = LlmChamberFactory.Create(options =>
{
    options.DefaultModel = "gemma4-e2b";       // デフォルトモデル
    options.RuntimeVariant = RuntimeVariant.Auto; // GPU自動検出
    options.CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".llmchamber");
    options.AutoDownloadRuntime = true;         // ランタイム自動DL
    options.AutoPullModel = true;              // モデル自動pull
    options.StartupTimeout = TimeSpan.FromSeconds(30);
    options.SharedModelDirectory = null;        // グローバルOllamaとモデル共有する場合に設定
});
```

既定キャッシュはユーザープロファイル配下の `.llmchamber` です。アプリごとに分離する場合は `CacheDirectory` に専用のパスを設定してください。文字列の `~` は展開されません。

`OllamaVersion` を指定しなければライブラリ内蔵の既定版を使います。`AutoDownloadRuntime=false` は取得済みバイナリ・版・バリアントの一致を要求します。`AutoPullModel=false` は初期化時のモデル取得を抑止するため、必要なモデルを事前に用意するか、`llm.Runtime.EnsureModelAsync()` で明示的に取得してください。

## ダウンロード進捗

```csharp
llm.RuntimeDownloadProgress += (_, p) =>
    Console.Write($"\rランタイム: {p.Percentage:F1}%");

llm.ModelDownloadProgress += (_, p) =>
    Console.Write($"\rモデル: {p.Percentage:F1}%");

await llm.InitializeAsync();
```

## エラー処理

LlmChamber は標準出力や独自ログへエラーを出力しません。ランタイムの取得・起動、API、音声、動画処理の失敗は `LlmChamberException` 系の例外または原因となったシステム例外として呼び出し元へ伝播するため、呼び出し側で必要な記録や UI 通知を行ってください。

```csharp
try
{
    await llm.InitializeAsync();
}
catch (Exception ex)
{
    appLogger.LogError(ex, "ローカル LLM の初期化に失敗しました。");
    throw;
}
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

`UseSpeech()` はセッションを作成します。初回の文字起こしで whisper.cpp とモデル、初回の読み上げで Piper と音声モデルを取得します。同じ LLM インスタンスではセッションを再利用し、最初に渡した `SpeechOptions` が使われます:

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

- **Whisper STT**: Windows x64 は自動DL対応。それ以外は `SpeechOptions.WhisperBinaryPath` で whisper-cli への明示パス指定が必要
- **Piper TTS**: Windows x64、Linux/macOS x64・arm64 は自動DL対応。それ以外は `SpeechOptions.PiperBinaryPath` で明示パス指定が必要

## 🎬 動画解析 (Media)

`UseMedia()` はセッションを作成し、初回の解析またはフレーム抽出で FFmpeg を取得します。同じ LLM インスタンスではセッションを再利用し、最初に渡した `MediaOptions` が使われます。フレーム抽出後、Vision モデルの解析結果を `IAsyncEnumerable` で順番に返します:

```csharp
await using var llm = LlmChamberFactory.Create(o => o.DefaultModel = "gemma3-4b");
var media = llm.UseMedia();

// 動画を2秒ごとに切り出して各フレームをVisionモデルで解析
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
- Windows / macOS / Linux（x64 / arm64）。機能ごとの自動取得範囲は Speech / Media の節を参照。MAUI パッケージはモバイル OS でのランタイム動作を保証しません
- Linux での Ollama 自動取得には `.tar.zst` を展開できる `tar` が必要
- 未取得のランタイム・モデル・音声リソースの取得にはインターネット接続が必要
- 必要なリソースが揃い、要求するランタイム版とバリアントがキャッシュに一致する場合はオフラインで動作

## 既存Ollamaとの共存

LlmChamberは独自のポートとモデルディレクトリで動作するため、グローバルにインストール済みのOllamaに一切干渉しません。既存モデルを共有したい場合は `SharedModelDirectory` を設定してください。

## ライセンス

[MIT License](LICENSE)

## 参考リンク

- [開発・検証手順](AGENTS.md) / [内部構造と設計](DESIGN.md)
- [Ollama](https://ollama.com/) — ローカルLLMランタイム
- [Ollama HTTP API](https://github.com/ollama/ollama/blob/main/docs/api.md)
