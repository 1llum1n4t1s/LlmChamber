# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

LlmChamberは「NuGet一発、ゼロ設定、環境汚染なし」で.NETアプリにローカルLLMを組み込むライブラリ。Ollamaバイナリをアプリローカルに自動配置し、モデルの自動pull・プロセス管理・推論をC#の型安全なAPIで提供する。

## ビルド・テスト

```bash
# ソリューション全体ビルド
dotnet build LlmChamber.slnx

# ユニットテスト実行
dotnet test test/LlmChamber.Tests/LlmChamber.Tests.csproj --framework net10.0

# 特定テストクラス実行
dotnet test test/LlmChamber.Tests/LlmChamber.Tests.csproj --filter "FullyQualifiedName~OllamaModelsTests"

# NuGetパッケージ生成
dotnet pack LlmChamber.slnx -o artifacts
```

## ターゲットフレームワーク

net8.0 / net10.0 のマルチターゲット。WPF・WinFormsは `-windows` TFM。

## アーキテクチャ: CRDebugger方式（ソースインクルード）

**重要**: `LlmChamber.Core` は `IsPackable=false` の内部プロジェクト。NuGetには公開されない。

各公開パッケージ（LlmChamber, .Wpf, .Avalonia, .WinForms, .Maui）が Core の `.cs` ファイルを `<Compile Include>` で直接取り込む。これにより NuGet に "Core" パッケージが出ない。

```xml
<!-- 各公開パッケージの.csprojに記載 -->
<Compile Include="..\LlmChamber.Core\**\*.cs"
         Link="Core\%(RecursiveDir)%(Filename)%(Extension)"
         Exclude="..\LlmChamber.Core\obj\**;..\LlmChamber.Core\bin\**" />
```

**結果**: コードは全て `src/LlmChamber.Core/` に書く。公開パッケージのプロジェクトにはUI固有コードのみ配置。

## NuGetパッケージ構成（5種）

| PackageId | 用途 | TFM |
|---|---|---|
| `LlmChamber` | ヘッドレス（コンソール/WebAPI） | net8.0;net10.0 |
| `LlmChamber.Wpf` | WPFチャットコントロール | net8.0-windows;net10.0-windows |
| `LlmChamber.Avalonia` | Avaloniaチャットコントロール | net8.0;net10.0 |
| `LlmChamber.WinForms` | WinFormsチャットパネル | net8.0-windows;net10.0-windows |
| `LlmChamber.Maui` | MAUIチャットビュー | net8.0;net10.0 |

## Core内部の層構造

### 公開API（`LlmChamber` 名前空間）
- `ILocalLlm` — メインエントリポイント。IAsyncDisposable
- `IChatSession` — マルチターン対話。IAsyncEnumerable<string>でストリーミング
- `IRuntimeManager` — Ollamaバイナリ・モデル管理
- `LlmChamberFactory.Create()` — 非DI用ファクトリ
- `ServiceCollectionExtensions.AddLlmChamber()` — DI登録

### 内部実装（`LlmChamber.Internal` 名前空間）
- `LocalLlm` → `OllamaProcessManager` → `OllamaApiClient` の順で初期化を自動実行
- `OllamaDownloader` — GitHub Releasesからバイナリをアトミックにダウンロード
- `GpuDetector` — PowerShell CIM (Get-CimInstance) でGPU/NPU検出
- `NdjsonStreamReader` — Ollama NDJSON → IAsyncEnumerable変換
- `OllamaModels` — 組込みプリセット4種（gemma4-e2b, gemma4-e4b, qwen3.5-2b, phi4-mini）

### Ollama API DTO（`LlmChamber.Internal.Api` 名前空間）
- `OllamaJsonContext` — System.Text.Json Source Generator
- Request/Response型はOllama HTTP APIの各エンドポイントに対応

## 重要な設計判断

- **WPFプロジェクトでは `System.IO` 等の暗黙インポートが効かない**。Core内のコードには明示的 `using` が必要
- **DI登録はファクトリデリゲート方式**。`ILogger<T>` の有無に依存しない（GetService + NullLoggerフォールバック）
- **ChatSession.SendAsync失敗時はユーザーメッセージをロールバック**する（ゴーストコンテキスト防止）
- **HttpClient.Timeout = InfiniteTimeSpan**。非ストリーミング推論は30分CancellationTokenで制御
- **バージョンマーカー形式は `version:variant`**（例: `0.20.2:Full`）

## Avalonia UI ルール

- AXAML構文を使用（WPF XAMLではない）
- `Trigger` / `DataTrigger` は存在しない → Style セレクターを使う
- `Visibility.Collapsed` は存在しない → `IsVisible=false` を使う
- `StyledProperty<T>` パターンを使用（DependencyPropertyではない）

## テスト

- xUnit + NSubstitute
- `InternalsVisibleTo` で Core 内部クラスもテスト可能
- インテグレーションテストは `[Trait("Category", "Integration")]` で分離
