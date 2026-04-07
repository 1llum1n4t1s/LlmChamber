# LlmChamber — プロジェクトプラン

> **作成日**: 2026年4月6日
> **対象**: .NET 8+ / Windows（将来的にクロスプラットフォーム）
> **ライセンス候補**: MIT or Apache 2.0

---

## 1. コンセプト — 「NuGet一発、ゼロ設定、環境汚染なし」

### 1.1 エレベーターピッチ

.NET開発者が `dotnet add package` 一つで、Python不要・GPU不要でGemma 4 E2Bをはじめとする軽量LLMをアプリに組み込めるライブラリ。Ollamaバイナリをアプリローカルに自動配置し、モデルの自動pull・プロセス管理・推論をすべてC#の型安全なAPIで制御できる。ユーザーの環境には一切触れない。

「Chamber（チェンバー）」＝ 密閉された小部屋。LLMを外部に漏らさず、アプリの中に閉じ込めて動かすイメージ。

### 1.2 設計思想

Ollamaの便利さ（モデル管理・推論エンジン・GGUF互換性・新モデル追従）を丸ごと活用しつつ、Ollamaをグローバルインストールせずアプリローカルに閉じ込める。C#開発者は `IAsyncEnumerable<string>` でストリーミング応答を受け取るだけ。llama.cppの破壊的変更やP/Invokeの保守は一切不要。

### 1.3 解決する課題

| 課題 | 現状 | LlmChamberで |
|---|---|---|
| Ollamaのグローバルインストール | PATHに追加、サービス登録、他アプリと競合の可能性 | アプリローカルにバイナリ配置。Dispose時に自動終了 |
| モデルの手動pull | ターミナルで `ollama pull` を実行 | 初回実行時に自動pull。進捗コールバック付き |
| Ollama HTTP APIの型安全性なし | HttpClientで生JSONをやり取り | C#のrecord型で厳密に型付け |
| 推論パラメータの調整が難しい | JSONで手動指定 | モデル別プリセットを内蔵 |
| Ollamaプロセスのライフサイクル | 手動で起動・停止 | `using` で自動管理。アプリ終了時にクリーンアップ |
| LLamaSharpのセットアップが煩雑 | バックエンド別パッケージ、llama.cppコミット互換性 | Ollamaに委譲するため一切不要 |

### 1.4 ターゲットユーザー

1. **C#/.NETデスクトップアプリ開発者** — WPF / Avalonia UI / MAUI にローカルLLMを組み込みたい
2. **業務アプリ開発者** — 社内ツールにオフラインAI機能を追加したい（データが外部に出ない）
3. **.NET Web API開発者** — セルフホストのLLM APIエンドポイントを立てたい
4. **ホビイスト** — GPUなしのPCでAIアプリを作ってみたい

### 1.5 非ターゲット

- GPU推論の最高性能を追求するユーザー → Ollama + CUDA直接 or LLamaSharp推奨
- 70B以上の大型モデルを動かしたいユーザー → vLLM / Ollama CLI 直接推奨
- Python/Jupyter環境で作業するユーザー → llama-cpp-python推奨

---

## 2. アーキテクチャ

### 2.1 全体構成

ユーザーのC#アプリケーションからLlmChamberの高レベルAPIを呼び出す。LlmChamberは内部でOllama HTTP Client経由でlocalhostの動的ポートに接続する。Ollamaバイナリはアプリローカル（`~/.llmchamber/`）に自動配置され、モデルファイルも同ディレクトリにキャッシュされる。

### 2.2 Ollamaプロセス管理

LlmChamberのインスタンス生成時に以下を自動実行する:

1. Ollamaバイナリがローカルにあるか確認。なければGitHub Releasesから自動DL
2. 空きポートを動的に確保（既存Ollamaとの競合回避）
3. Ollamaプロセスを子プロセスとして起動。環境変数で `OLLAMA_HOST` と `OLLAMA_MODELS` をアプリローカルに指定。グローバルなOllamaの設定・モデルに一切触れない
4. ヘルスチェック（/api/version）でReady待ち
5. モデルがpull済みか確認。未pullなら自動実行（進捗コールバック対応）
6. Dispose時にOllamaプロセスをグレースフル停止（タイムアウト付き）

### 2.3 ポート競合回避

ユーザーが既にOllamaをグローバルインストールしていても、LlmChamberは別ポート・別モデルディレクトリで起動するため一切干渉しない。

### 2.4 NuGetパッケージ構成

- **LlmChamber** — メインパッケージ（これだけで動く）。Ollama HTTPクライアント、プロセスマネージャ、モデルプリセットを含む。パッケージサイズは数十KB。Ollamaバイナリ（約70MB）は初回実行時にDL
- **LlmChamber.Avalonia** — Avalonia UI用ヘルパー（将来）
- **LlmChamber.SemanticKernel** — Semantic Kernel統合（将来）

---

## 3. 公開API設計方針

### 3.1 最小APIの目標

モデル識別子を渡してインスタンス生成し、プロンプトを渡すだけで推論開始。初回はOllama自動DL → モデル自動pull → 推論が全て透過的に実行される。5行以内で動くことを目指す。

### 3.2 主要インターフェース

| インターフェース | 責務 |
|---|---|
| `ILocalLlm` | エントリポイント。モデルのロード、テキスト生成（ストリーミング）、チャットセッション作成、Embedding取得。内部でOllamaプロセスのライフサイクルを管理。`IAsyncDisposable` と `IDisposable` を実装 |
| `IChatSession` | マルチターン対話の管理。会話履歴の自動管理、ストリーミング応答、一括応答、履歴クリア |
| `IRuntimeManager` | Ollamaバイナリとモデルファイルの管理。ランタイムのDL・バージョン確認、モデルのpull・削除、キャッシュサイズ取得 |

### 3.3 設定・オプション型

| 型 | 内容 |
|---|---|
| `LlmChamberOptions` | ライブラリ全体設定。デフォルトモデルID、キャッシュディレクトリ（デフォルト: `~/.llmchamber/`）、自動ダウンロード有無、起動/停止タイムアウト |
| `InferenceOptions` | 推論パラメータ。MaxTokens, Temperature, TopP, TopK, RepeatPenalty, Seed, StopSequences |
| `ChatOptions` | チャットセッション設定。SystemPrompt、InferenceOptions、最大履歴メッセージ数 |
| `ChatMessage` | メッセージのrecord型。Role（System/User/Assistant）、Content、Timestamp |
| `ModelPreset` | モデルプリセット定義。ライブラリ内ID、OllamaタグTags、表示名、ファミリ、概算DLサイズ、推奨最小RAM、デフォルト推論パラメータ、説明文 |
| `DownloadProgress` | DL進捗。BytesDownloaded、TotalBytes、Percentage、StatusMessage |

### 3.4 イベント

`ILocalLlm` はOllamaランタイムのDL進捗とモデルのDL進捗をイベントとして公開する。UIと連携してプログレスバー等を表示可能にする。

### 3.5 DI（Dependency Injection）対応

`services.AddLlmChamber()` 拡張メソッドでDIコンテナに登録可能にする。オプションはラムダで設定。

### 3.6 初期同梱プリセット

| プリセットID | Ollamaタグ | ファミリ | 概算サイズ | 推奨最小RAM | 特徴 |
|---|---|---|---|---|---|
| `gemma4-e2b` | `gemma4:e2b` | Gemma 4 | ~3GB | 5GB | 最軽量。CPU推論に最適。マルチモーダル |
| `gemma4-e4b` | `gemma4:e4b` | Gemma 4 | ~5GB | 8GB | 中型エッジモデル。音声入力対応 |
| `qwen3.5-2b` | `qwen3:2b` | Qwen 3.5 | ~2GB | 4GB | 日本語・多言語が特に優秀 |
| `phi4-mini` | `phi:3.8b` | Phi-4 | ~3GB | 6GB | 数学・コーディングに強い |

推論パラメータはモデルごとに公式推奨値をプリセットとして内蔵する。Gemma 4はTemperature=1.0, TopP=0.95, TopK=64。

---

## 4. 開発ロードマップ

### Phase 0: 基盤構築（1〜2週間）

- リポジトリ作成（GitHub）、CI/CD構成（GitHub Actions）
- プロジェクト構造の作成
- Ollamaバイナリの自動DLロジック（GitHub Releases API）
- Ollamaプロセスマネージャ（起動・停止・ヘルスチェック・ポート管理）
- 基本的な動作確認: プロセス起動 → pull → generate のE2E

### Phase 1: MVP — Gemma 4 E2B 対応（2〜3週間）

- `ILocalLlm` / `IChatSession` の実装
- Ollama HTTP APIクライアント（/api/generate, /api/chat, /api/pull）
- ストリーミング応答（NDJSON → `IAsyncEnumerable<string>`）
- Gemma 4 E2Bプリセット
- ダウンロード進捗イベント
- ユニットテスト＆インテグレーションテスト
- NuGet α版パブリッシュ
- READMEとクイックスタートガイド

### Phase 2: モデル拡充＆品質向上（3〜4週間）

- Gemma 4 E4B / Qwen 3.5 2B / Phi-4-mini プリセット追加
- Embedding API実装（/api/embed）
- DI対応: `services.AddLlmChamber()`
- 非ストリーミング応答（SendAndWaitAsync）
- linux-x64 / osx-arm64 のOllamaバイナリ対応
- ダウンロード進捗のUI連携サンプル（Avalonia UI）
- NuGet β版パブリッシュ
- 日本語READMEとZenn/Qiita記事

### Phase 3: エコシステム拡張（将来）

- Semantic Kernel統合: `LlmChamber.SemanticKernel`
- RAG（簡易ベクトル検索 + Embedding）
- Function Calling / Tool Use対応
- Avalonia UI用チャットコンポーネント: `LlmChamber.Avalonia`
- カスタムモデル対応（ユーザーがプリセットを追加登録）
- Ollamaバージョンの自動アップデート機構
- NativeAOTパブリッシュ対応

---

## 5. 技術的な注意点・リスク

### 5.1 Ollamaバイナリの配布

OllamaのライセンスはMITなので再配布は問題なし。NuGetパッケージにバイナリを同梱すると100MB超になるため、初回実行時にGitHub Releasesからダウンロードする方式とする。配置先は `~/.llmchamber/runtime/` 。

### 5.2 オフライン環境への対応

初回はインターネット接続が必須（Ollama DL + モデルpull）。2回目以降は完全オフラインで動作する。オフライン配布が必要な場合はキャッシュディレクトリごとコピーすれば可。

### 5.3 既存Ollamaとの共存

LlmChamberは別ポート・別モデルディレクトリで起動するため、ユーザーの既存Ollamaに一切干渉しない。既存モデルを共有したい場合は `OLLAMA_MODELS` を同じパスに設定するオプションを提供する。

### 5.4 Ollama APIの安定性

Ollama HTTP API（/api/generate, /api/chat等）は比較的安定しているが、バージョン間で微妙な変更がある可能性。サポートするOllamaバージョンを明示し、互換性テストをCIに組み込む。

### 5.5 Windows Defenderの警告

Ollamaバイナリを自動DL＆実行するとWindows Defenderが警告を出す可能性がある。READMEに注意書きを記載。コード署名は将来的に検討。

### 5.6 Ollamaバイナリのサイズ

Windows版Ollamaは約70MB。初回DLに時間がかかるため、進捗コールバックとキャンセル対応は必須。

---

## 6. 競合分析

| ライブラリ | 言語 | セットアップ | モデル自動管理 | 環境汚染 | Ollama依存 |
|---|---|---|---|---|---|
| **LLamaSharp** | C# | 中 | ✗ | 低 | ✗ |
| **Ollama CLI** | Go | 高 | ✓ | 中 | 本体 |
| **OllamaSharp** | C# | 低 | ✗ | 中 | 要インストール |
| **llama-cpp-python** | Python | 中 | ✗ | 高 | ✗ |
| **LM Studio** | Electron | 高 | ✓ | 中 | ✗ |
| **LlmChamber** | C# | **最高** | **✓** | **最低** | 内蔵（自動管理） |

OllamaSharpはOllama HTTP APIのC#クライアントだが、Ollamaのインストール・プロセス管理は対象外でユーザーが自分でセットアップする前提。LlmChamberとの最大の差別化ポイントは「Ollamaのセットアップ自体を自動化し、環境を一切汚さない」こと。

---

## 7. SDD（仕様駆動開発）での進め方

このプロジェクト自体をSDDワークフローで開発する。

1. 本プラン（仕様書）をCLAUDE.mdに要約して記載
2. セクション3のインターフェース設計に基づきC#のインターフェース・型定義を生成
3. インターフェースに対するユニットテストを生成
4. テストを通す実装をClaude Codeで生成
5. テストパス → コミット → 次のフェーズへ

人間の最高価値貢献ポイント:
- 本プラン（仕様）のレビュー ← 今ここ
- インターフェース定義のレビュー
- テストケースの妥当性確認
- 実際にアプリに組み込んでのUXフィードバック

---

## 付録: 参考リンク

- [Ollama](https://ollama.com/) — ローカルLLMランタイム（MIT License）
- [Ollama HTTP API](https://github.com/ollama/ollama/blob/main/docs/api.md) — 公式APIドキュメント
- [OllamaSharp](https://github.com/awaescher/OllamaSharp) — 既存のC# Ollamaクライアント（参考）
- [Gemma 4 on Ollama](https://ollama.com/library/gemma4) — Gemma 4モデルページ
- [LLamaSharp](https://github.com/SciSharp/LLamaSharp) — C#/.NET LLMライブラリ（競合参考）
- [Gemma 4 公式ブログ](https://huggingface.co/blog/gemma4) — HuggingFace
