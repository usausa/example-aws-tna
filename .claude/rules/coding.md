# コーディング (TnA)

AGENTS.md に加えて、このリポジトリで守ること。

- このリポジトリに端末のアプリはない。AGENTS.md の端末と XAML の記述 (`terminal/Settings.XamlStyler`、`[Scope]`、terminal.md、エミュレータ、端末の Debug ビルド) は当てはまらない
- ソリューションは `Tna.slnx` の1つ。Release ビルドと InspectCode はこのソリューションで行う
- コンストラクタの引数とフィールドの基盤には、`ILogger<T>`、`TimeProvider` に続けて AWS のクライアント (`IAmazonDynamoDB` など) を置く (`IDbProvider`、`IDialect` はない)
- Lambda のハンドラは、サービスをハンドラの引数で受ける
- 画面の文言は日本語。ログのメッセージは英語
- Lambda の関数は Native AOT で発行する。リフレクションに頼らない (JSON はソース生成のコンテキストを使う)
- SDK の版を `global.json` で固定しない
