# 進め方 (TnA)

- 作業は動作を確かめられる単位で進める。最低限の動作確認ができるところから始め、機能を足していく
- 単位ごとに、検証 (`.claude/skills/verify/`) と動作の確認の結果を報告し、確認を得てからコミットする。プッシュも確認してから行う
- コーディング規約と AI 用の設定ファイル (AGENTS.md、`.editorconfig`、`Analyzers.ruleset`、`Directory.Build.props`、`Tna.sln.DotSettings`、`.claude/`) の変更は、確認してから行う
- 設計の判断が要るときは、根拠を添えた選択肢で問い合わせて決める。決めたことは `docs/decisions.md` に今の方針として書く
- AWS を使う前に確認する。ほかのスタックには触れない
