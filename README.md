# example-aws-tna

AWS のサーバーレス構成と .NET で構築する勤怠管理（Time & Attendance）システム。

## 基本方針

| 領域 | 方針 | 状態 |
| --- | --- | --- |
| フロントエンド | Blazor WebAssembly を S3（+ CloudFront）でホスト | 方針確定 |
| 認証 | 独自 JWT を発行する（RS256、秘密鍵は KMS）。リフレッシュトークンは HttpOnly Cookie、アクセストークンはメモリ。API は CloudFront 経由でアプリと同一オリジン | 決定（2026-10-07）。設計方針は [論点と決定事項](docs/research/12-open-issues.md) §1.7 |
| 想定顧客 | 中堅企業まで（1テナント〜3,000人）。オフィス、店舗・飲食・小売、工場・物流、医療・介護 | 決定（2026-10-07） |
| 労働時間制度 | 固定時間制、フレックスタイム制（清算期間1か月まで）、シフト制、1か月単位の変形労働時間制を MVP で扱う。1年単位の変形は P2 | 決定（2026-10-07） |
| データ | DynamoDB | 方針確定 |
| ロジック | Lambda（.NET）。始業・終業の打刻集中に備えオンデマンドでスケール | 方針確定 |
| IaC | AWS CDK（C#） | 方針確定 |

## 主な利用者と作業

- 従業員: 毎日の出勤・退勤の打刻、必要に応じた休暇申請
- 承認者・管理者: 申請の承認、勤怠の確認・締め、マスタ設定

## ドキュメント

[docs/README.md](docs/README.md) を参照。

## リポジトリの構成

| フォルダ | 中身 |
| --- | --- |
| `docs/` | 調査・設計の文書（OpenAPI を含む）。進み具合は [計画と進捗](docs/plan/progress.md) |
| `src/` | アプリケーション（`Tna.*`。構成は [実装計画](docs/plan/implementation-plan.md) §5.1） |
| `tests/` | テスト |
| `tools/` | 生成の道具（操作の表と関数の一覧）、手元の環境の準備 |
| `scripts/` | 品質の関門と配備のスクリプト |
| `poc/` | PoC のコード（本実装とは別にビルドする） |

## 開発

- 要るもの: .NET 10 SDK（`global.json`）、Node.js 22 以上、PowerShell 7、コンテナの実行環境（Native AOT の Linux 向けのビルドと、手元の DynamoDB）。品質の関門のテストは、結合テストが DynamoDB Local のコンテナを起こすため、コンテナの実行環境を起動してから回す。
- ハンドラのライブラリの拡張版は公開前のため、パッケージを `local-feed/` に置いて使う（git には入れない）。
- 依頼ごとに品質の関門を通す（ビルド（警告なし）、生成物が最新か、テスト、OpenAPI の検証、CDK の synth。AWS は使わない）。

```powershell
./scripts/check.ps1
```

- OpenAPI を直したとき（`docs/api` で結合した後）と、関数のハンドラを足したとき（`dotnet build -c Release` の後）は、操作の表と関数の一覧を作り直してコミットする。型と JSON のコンテキストはビルドの中で作られる。

```powershell
dotnet run --project tools/Tna.Tools.Generator -c Release
```

- 配備の前に、関数のパッケージ（`artifacts/functions/{モジュール}.zip`。git の対象外）を作る。Native AOT の arm64 のビルドを、Amazon Linux 2023 のビルド用のコンテナの中で行う（初回は約3分。NuGet などはコンテナのボリュームに残る）。AWS は使わない。

```powershell
./scripts/package.ps1
```

- 画面は、発行して環境の画面のバケットに置く（CSP の `script-src` のハッシュ、`.br`、キャッシュの設定。CloudFront の `index.html` のキャッシュも消す）。`-DryRun` では AWS を使わずに、発行と置くファイルだけを確かめる。

```powershell
./scripts/publish-web.ps1 -Environment dev -DryRun
```

### 手元での起動

画面・API（Lambda と HTTP API の模擬）・手元の DynamoDB・Streams の再計算をまとめて起動する（[全体構成](docs/design/architecture.md) §8.3）。AWS は使わない。

- 要るもの: コンテナの実行環境と、Lambda の模擬の道具（AWS の .NET のグローバルツール）。

```powershell
dotnet tool install -g Amazon.Lambda.TestTool --version 0.16.1
```

```powershell
dotnet run --project src/Tna.AppHost
```

- 入口は http://localhost:5100（画面と API を同じオリジンで）。起動の仕組みの管理の画面は http://localhost:15100 で、そのログインのリンクは起動時の出力に出る（関数ごとのログもそこで見る）。
- 起動のたびに、空の DynamoDB に表とテナント（企業コード `LOCAL`、ログインID `admin@example.com`）を作る。パスワードは `tools/Tna.Tools.Local/appsettings.json` の `Demo:Password`（手元専用の値）。
- 動かすモジュールは `src/Tna.AppHost/appsettings.json` の `Local:Modules`（関数は1つずつ別のプロセスで動く）。止めるときは Ctrl+C（後片づけで、関数のプロセスとコンテナが消える）。
