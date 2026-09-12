# 変更履歴

Git のバージョン記録・コミット差分と既存の変更履歴をもとに、確認できた版ごとの変更点をまとめています。「Git 記録日」は公開日ではありません。番号の欠番だけから未確認のリリースは補っていません。

## 未リリース

## [1.0.4] — 2026-09-12

- Qwen 3.5 2B プリセットの取得タグを修正し、モデルをダウンロードできるようにした。
- 自動取得する Ollama を 0.34.0、whisper.cpp を 1.9.2 に更新。
- Factory で作成したインスタンスの破棄時に、API 通信用の HttpClient も解放するよう修正。
- ランタイムのバージョン取得中のキャンセルが、版不明として処理される問題を修正。

## [1.0.3] — Git 記録日: 2026-05-24

- Ollama の画像入力に対応し、音声と動画のセッション API を追加。必要になった時点で初期化する方式に変更。
- 依存ライブラリとエラー処理を更新。

出典: [版の記録](https://github.com/1llum1n4t1s/LlmChamber/commit/422a8031d655dfc3afa15598b21bd359709a5fa4) / [変更差分](https://github.com/1llum1n4t1s/LlmChamber/compare/5c2630052a354e7d0c7e39382a1e58466805b1ef...422a8031d655dfc3afa15598b21bd359709a5fa4)。

## [1.0.2] — Git 記録日: 2026-04-16

- SuperLightLoggerへの移行、パフォーマンス改善、コード品質向上
- ROCm 2段階DL修正・HttpClient Keyed Services化・例外階層改善

出典: [版の記録](https://github.com/1llum1n4t1s/LlmChamber/commit/5c2630052a354e7d0c7e39382a1e58466805b1ef) / [変更差分](https://github.com/1llum1n4t1s/LlmChamber/compare/ad42d06f39731b802728ead934d7ebf62d638a57...5c2630052a354e7d0c7e39382a1e58466805b1ef)。

## [1.0.0] — Git 記録日: 2026-04-08

- LlmChamber v1.0.0 — Ollama内蔵ローカルLLMライブラリの全実装

出典: [版の記録](https://github.com/1llum1n4t1s/LlmChamber/commit/ad42d06f39731b802728ead934d7ebf62d638a57)。
