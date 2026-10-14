# API 仕様

仕様の正本は OpenAPI 3.1 の YAML（[openapi.yaml](openapi.yaml) と、そこから参照するファイル）で、この文書は規約と一覧をまとめたもの。食い違ったら YAML を正とする。

システムの構成は [全体構成](../design/architecture.md)、データの持ち方は [データ設計](../design/data-model.md) を参照する。

## 1. ファイルの構成

機能ごとのファイルに分けて書き、結合したものをコード生成と表示に使う。

| パス | 内容 |
| --- | --- |
| [openapi.yaml](openapi.yaml) | テナント向けの API のルート。パスと各ファイルの対応、セキュリティの定義 |
| [paths/](paths/) | 操作。`auth`、`me`、`requests`、`kiosk`、`admin-attendance`、`admin-shifts`、`admin-closing`、`admin-leave`、`admin-settings` |
| [components/schemas/](components/schemas/) | 型。`common`（共通）、`auth`、`attendance`、`leave`、`workflow`、`organization`、`rules`、`admin` |
| [components/](components/) | 共通のパラメータ（`parameters.yaml`）、エラー応答（`responses.yaml`）、ヘッダー（`headers.yaml`） |
| [ops/openapi.yaml](ops/openapi.yaml) | 運営者の API（IAM 署名） |
| [redocly.yaml](redocly.yaml) | 検証（lint）と結合（bundle）の設定。ファイル名はツールの決まりによる |
| `dist/` | 結合した仕様（生成物。手で直さない） |
| `__tooling.md` | 検証・結合・コード生成の手順と、生成ツールの扱える書き方。製品名を含むため git に入れず、手元にだけ置く |

## 2. 規約

### 2.1 入口と版

- アプリと同じオリジン（CloudFront）で、`/auth/v1/*` を認証、`/api/v1/*` を業務の API に振り分ける。運営者の API は別の入口にし、CloudFront からは公開しない。
- パスは利用者ごとにまとめる。本人は `/api/v1/me`、承認は `/api/v1/approvals`、申請の詳細と操作は `/api/v1/requests`、共有端末は `/api/v1/kiosk`、管理は `/api/v1/admin`。
- 版はパスの `v1` で表す。互換を壊す変更のときだけ `v2` を作る。
- 項目の追加は互換を壊さない変更とする。画面は知らない項目を無視する。
- 列挙に値を加える変更は、古い画面が値を読めないことがあるため、画面を先に配布してから API で使い始める。

### 2.2 認証と権限

| 経路 | 認証 | 備考 |
| --- | --- | --- |
| `/api/v1/*` | `Authorization: Bearer <アクセストークン>` | HTTP API の JWT オーソライザーで、署名・発行者・対象者・期限とスコープを確かめる |
| `/auth/v1/token`、`/auth/v1/logout` | リフレッシュトークンの Cookie（`__Secure-tna_rt`、`Path=/auth`） | CSRF 対策として `X-TNA-Request: 1` ヘッダーを必須にする |
| `/auth/v1/kiosk/token` | 共有端末の Cookie（`__Secure-tna_device`、`Path=/auth/v1/kiosk`） | 同上 |
| そのほかの `/auth/v1/*` | なし（公開）、または Bearer | ログイン、パスワードの再設定、共有端末の登録とトークンは、CloudFront と WAF で IP ごとにレート制限をかける。ほかの公開の操作は、トークンとアカウントのロックで守る |

- アクセストークンのスコープ（操作の `security` に書いた値）:
  - `employee`: 従業員としてログインした。
  - `admin`: 管理機能の再認証（パスワードと TOTP）が有効。`employee` と一緒に入る。有効なのは12時間、または最後の管理操作から1時間。
  - `kiosk`: 登録した共有端末。
- `/api/v1/admin/*` はすべて `admin` スコープが必要。勤怠管理者の操作は管理範囲の従業員に限る。テナント管理者だけの操作は、操作の `x-required-roles: [tenantAdmin]` に示す（表の「権限」の列では「（テナント管理者）」）。
- 権限の範囲外のリソースは、存在を知らせないよう 404 を返す。自分自身への操作の禁止などは 403 で返す。
- 本文のある要求は、操作の仕様の形式の `Content-Type`（`application/json`。部分更新は `application/merge-patch+json`、CSV の取込は `text/csv`。`charset` などの引数は見ない）を付ける。違えば 415（`UNSUPPORTED_MEDIA_TYPE`）を返す。ほかのサイトのフォームからの送信（ログインの CSRF など）を断るため。
- JWT オーソライザーが返す 401・403 の本文は Problem Details ではなく、`{"message": "Unauthorized"}` のような固定の形になる。画面は状態コードで判断する。401 はトークンを更新して1回だけ再送し、`/api/v1/admin/*` の 403 は管理機能の再認証の画面（AU05）に移る。

### 2.3 JSON の表し方

| 対象 | 表し方 | 型 |
| --- | --- | --- |
| 名前 | 項目は lowerCamelCase。列挙の値も lowerCamelCase の文字列 | — |
| null | 値のない項目は省略せずに `null` を返す | — |
| ID | ULID（26文字） | `Ulid` |
| 時点 | RFC 3339。応答はテナントのタイムゾーンのオフセット付き（例 `2026-10-07T09:00:00+09:00`）。要求はどのオフセットでもよいが、オフセット（`Z` か `±hh:mm`）と秒は必須。ないものは `INVALID_BODY`（動いている環境の地方時として読まないため） | `string`（`date-time`） |
| 型の決まらない値 | 監査の変更前後、アラートの詳細、ジョブの要約は、決まった形のない JSON の値。項目は増えることがあるため、知らない項目は読み飛ばす | `object` |
| 日付 | `YYYY-MM-DD`（勤務日など。タイムゾーンを持たない） | `string`（`date`） |
| 時間の量 | 分の整数。マイナスのある量（過不足など）は符号付き | `Minutes`、`SignedMinutes` |
| 勤務日の中の時刻 | 勤務日の 0:00 からの分（0〜2879。翌2:00 は 1560） | `MinuteOfDay` |
| 給与の締め期間 | 期間の末日が属する年月（`YYYY-MM`） | `PeriodId` |
| 休暇の量 | 日数（0.5日単位）と時間（分）の組 | `LeaveAmount` |
| 文字列 | 必須の文字列は、前後の空白を除いて1文字以上。長さは文字数で数える | — |

### 2.4 エラー

- エラーは RFC 9457 の Problem Details（`application/problem+json`）に、アプリのコード `code` と個々の誤り `errors`（`ValidationIssue` の配列）を加えて返す。
- `title` は利用者向けの短い文言。画面は `code` を見て、ユースケースに書いた文言と次の操作を出す。
- `type` は `/problems/` にコードを小文字のハイフン区切りにして付けた相対の URI（例 `PERIOD_CLOSED` は `/problems/period-closed`）。`instance` には調査用の要求の ID（API Gateway の要求の ID）を入れる。値のない `detail` と `errors` は null で返す。
- 引数（経路・クエリ・ヘッダー・本文）の読み取りと検証の誤りも、同じ形で 400（`VALIDATION_FAILED`）を返す。`errors` の `field` は、本文なら JSON Pointer、経路・ヘッダー・クエリなら名前（例 `employeeId`、`Idempotency-Key`、`nextToken`）。
- 本文と、経路・クエリの引数は、この仕様の制約（必須、長さ、範囲、形式、列挙、パターン、件数）で確かめる。検証は仕様から生成し、サーバーと画面で同じものを使う。項目の誤りは、下の表のコードと日本語の文言で返す。文言は画面の項目の下にそのまま出せる。
- 本文の JSON が壊れているとき、型の合わない値（文字列の項目に数値など）、仕様の必須の項目がないときは、項目の位置なし（`field` は null）で `INVALID_BODY` を返す。本文がないときも同じく `field` は null で `REQUIRED` を返す。画面はすべての項目を送るため、これは仕様に合わない要求にだけ出る。
- 仕様にない項目は読み飛ばし、書き戻さない。
- 長さは文字数（Unicode のコードポイント）で数える。必須の文字列は、前後の空白を除いて1文字以上（§2.3）。
- コードの一覧は §5。

| 項目の誤りのコード | 制約 | 文言の例 |
| --- | --- | --- |
| `REQUIRED` | 必須（null、空や空白だけの文字列、本文がない） | 必須です。 |
| `TOO_SHORT`、`TOO_LONG` | 文字列の長さ（`minLength`、`maxLength`） | 3文字以上で入力してください。／50文字以内で入力してください。 |
| `OUT_OF_RANGE` | 数値の範囲（`minimum`、`maximum` と、その exclusive） | 1以上100以下で入力してください。 |
| `NOT_MULTIPLE` | 数値の刻み（`multipleOf`） | 0.5単位で入力してください。 |
| `INVALID_FORMAT` | 形式（`pattern`、`format` の `date`・`date-time`・`email`・`uri`） | 形式が正しくありません。／メールアドレスの形式が正しくありません。 |
| `INVALID_VALUE` | 列挙と固定値（`enum`、`const`）、引数の型の変換 | 選べない値です。 |
| `TOO_FEW_ITEMS`、`TOO_MANY_ITEMS` | 配列の件数（`minItems`、`maxItems`） | 1件以上にしてください。／7件以内にしてください。 |
| `INVALID_BODY` | 本文の JSON の誤り（壊れた JSON、型の違い、必須の項目がない） | 本文の形式が正しくありません。 |

パスワードの条件の誤り（422 `PASSWORD_POLICY_VIOLATION`）は、`errors` の `field` を新しいパスワードの項目（例 `/newPassword`）にし、次のコードで理由を返す。

| パスワードの誤りのコード | 理由 | 文言の例 |
| --- | --- | --- |
| `TOO_SHORT`、`TOO_LONG` | 長さ（TOTP を登録していなければ15文字以上、していれば8文字以上。128文字まで） | 15文字以上で入力してください。 |
| `PASSWORD_BREACHED` | 漏えい・頻出のパスワードの一覧に一致する | よく使われているか、漏えいしたことのあるパスワードです。 |
| `PASSWORD_PERSONAL` | 企業コード・ログインID・氏名を含む | 企業コード、ログインID、氏名を含まないパスワードにしてください。 |
| `PASSWORD_PATTERN` | 同じ文字の繰り返しや、連続した文字だけ | 同じ文字の繰り返しや、連続した文字だけのパスワードは使えません。 |

| 状態 | 使う場面 |
| --- | --- |
| 400 | 入力の形式の誤り（`VALIDATION_FAILED`） |
| 401 | 認証されていない、トークンの期限切れ、認証情報の誤り |
| 403 | スコープ・ロールがない、自分自身への禁止された操作、CSRF ヘッダーがない |
| 404 | 対象がない、または権限の範囲外 |
| 409 | 状態との競合（締め済み、打刻の順序、申請の状態など） |
| 410 | 期限切れ・使用済みのリンクや検証の結果 |
| 412 | `If-Match` が一致しない（`VERSION_CONFLICT`） |
| 413 | 取り込むファイルが大きすぎる |
| 422 | 業務規則の違反（残りの不足など）、冪等キーの再利用 |
| 423 | PIN の誤りが続いたための一時的な停止 |
| 428 | `If-Match` がない |
| 429 | レート制限（`Retry-After` 付き） |
| 503 | 一時的に利用できない（`Retry-After` 付き） |

### 2.5 冪等キー

- 作成・実行の操作（201・202 を返すもの）には `Idempotency-Key` ヘッダーを必須にする。値は画面が操作ごとに作る UUID または ULID（16〜64文字の英数字とハイフン）。ないとき、形式が違うときは 400（`VALIDATION_FAILED`。`errors` の `field` は `Idempotency-Key`）。
  - 例外: 版や条件を加える操作は `If-Match` で二重の実行を防ぐため不要。認証の公開の操作も不要。
- サーバーはキーを、テナント・利用者・操作ごとに24時間保持する。
  - 同じキー・同じ内容の再送には、最初の結果（状態と本文）をそのまま返す。
  - ただし、一度だけ返す秘密（従業員の登録の初期 PIN、共有端末の登録コード）は記録に残さない。再送には、その項目を null にした結果を返し、画面は PIN のリセット・登録コードの発行し直しを案内する。
  - 同じキーで内容が違えば 422（`IDEMPOTENCY_KEY_REUSED`）を返す。
  - 最初の要求を処理している間に届いた再送には 409（`IDEMPOTENCY_KEY_IN_PROGRESS`）を返す。
- 画面は、通信の失敗・429・503 のときに同じキーで再送する。考え方は IETF の Idempotency-Key ヘッダーの草案に合わせる。

### 2.6 同時更新（ETag と If-Match）

- 更新できるリソースは版を持つ。単一の取得では `ETag` ヘッダーで、本文では `etag` 項目で返す（同じ値）。一覧の要素から更新するときは、要素の `etag` を使う。
- 更新・削除・状態を変える操作には `If-Match` を必須にする。一致しなければ 412（`VERSION_CONFLICT`）、なければ 428（`PRECONDITION_REQUIRED`）。版の形でない値（弱い ETag、`*`、複数の値）も、どの版にも一致しないため 412 にする。412 を受けた画面は、最新の内容を取り直して表示する。
- シフト表の下書きとカレンダーの年の保存（PUT）は、まだ存在しないものを作るときだけ `If-Match` を省略できる。
- 日別勤怠の詳細の版は、その日の入力（打刻、補正、休暇、勤務予定）が変わったときだけ進む。非同期の再計算では進まないため、補正の画面が再計算のたびに 412 にならない（[データ設計](../design/data-model.md) §7.1）。

### 2.7 部分更新（PATCH）

JSON Merge Patch（RFC 7396、`application/merge-patch+json`）で送る。

- 送らなかった項目は変えない。`null` は値を消す（null を許す項目だけ。許さない項目に送ると 400）。
- 配列は丸ごと置き換える。
- 読み取り専用の項目（ID、`etag`、`builtIn` など）は無視する。
- 画面は、取得したリソースを編集してそのまま送ってよい。新しい版の API で増えた項目を古い画面は送らないため、消えない。
- 反映した結果がスキーマ（必須の項目など）を満たさなければ 400 を返す。反映した結果を、本文と同じ検証（§2.4）で確かめる。

### 2.8 一覧とページング

- 件数の多い一覧は、`limit`（既定 50、最大 100）と `nextToken` でページを送る。応答は `items` と `nextToken`（続きがなければ `null`）。
- `nextToken` は中身を解釈しない文字列として扱う。絞り込みの条件を変えたら最初から取り直す。
- `nextToken` は暗号化してあり、そのテナント・その操作・その絞り込みの条件でだけ使える。有効期限は1時間。期限を過ぎたもの、ほかの一覧や条件で得たもの、壊れたものは 400（`VALIDATION_FAILED`。`errors` の `field` は `nextToken`）を返す。画面は最初のページから取り直す（[データ設計](../design/data-model.md) §6）。
- 並び順は操作ごとに決め、説明に書く。
- 件数の少ないマスタ（部署、雇用区分、就業ルールなど）は、ページを分けずにすべて返す。

### 2.9 非同期のジョブ

- 時間のかかる処理（CSV の出力、従業員の取込の反映、締め、再オープン、再計算）は 202 で受け付け、`Location` ヘッダーと本文でジョブを返す。
- 画面は `GET /api/v1/admin/jobs/{jobId}` で状況を確かめる。間隔は1秒から始めて最大10秒まで延ばす。
- 出力の結果は署名付きの URL（10分で失効）で返す。ジョブは作成から7日間取得できる。

### 2.10 レート制限と再送

- 429（`RATE_LIMITED`）と 503（`SERVICE_UNAVAILABLE`）には `Retry-After`（秒）を付ける。WAF のレート制限も 429 を返すように設定する。
- 画面が自動で再送してよいのは、取得（GET）と、冪等キーか `If-Match` の付いた操作だけ。間隔は指数的に延ばしてゆらぎを加え、3回までとする。打刻は、通信エラーと 429 のとき、同じ冪等キーで間隔を広げながら最大5回まで自動で再送する（10秒たっても応答がないときも通信エラーとする）。それでも失敗したら、もう一度押すよう案内する。

### 2.11 CSV

- 取込は本文に `text/csv` で CSV を送り、文字コードをクエリの `encoding`（`utf8`・`shiftJis`）で示す。上限は 4MB（従業員の取込は 10,000 行まで）。API Gateway は Shift_JIS などの本文を Base64 にして関数に渡し（4/3 倍）、関数の要求の上限は 6MB のため、5MB は受け取れない。
- CSV の本文（取込と出力）はバイト列として扱う（仕様では `type: string`、`format: binary`）。Shift_JIS のこともあるため、文字列として扱わない。
- 取込はまず検証し、誤りが1つでもあれば何も反映しない（シフト表）。従業員の取込は検証と反映を分ける（反映はジョブ）。
- 出力の形式（文字コード、時間の表し方）は省略するとテナントの既定。
- 従業員の取込のテンプレートは、画面の静的なファイルとして配る（API は持たない）。

### 2.12 時刻とキャッシュ

- 本人の打刻の時刻はサーバーの受信時刻を正とし、端末の時刻は参考として受け取る。
- MVP のタイムゾーンは Asia/Tokyo に固定する。
- 業務の API の応答には `Cache-Control: no-store` を付ける。公開鍵（JWKS）と発行者の情報は S3 と CloudFront から配る（API には含めない）。

### 2.13 仕様の書き方の約束

C# の型、クライアント、操作の表を生成するため、生成ツールが正しく扱える書き方にそろえる。どの書き方で生成ツールが誤るかは、手元の `__tooling.md`（§1）に書いてある。

| 対象 | 書き方 | 避ける書き方 |
| --- | --- | --- |
| 種類で形が変わる型 | 基底の型に `discriminator`（`propertyName` と `mapping`）を書き、派生の型は `allOf: [基底, {type: object, ...}]` にする。派生の型では判別の項目を書き直さない。追加の項目がない派生の型にも空の `{type: object, properties: {}}` を書く | `oneOf` と `discriminator` だけの型 |
| null を許す基本型 | `type: [string, "null"]` のように型の配列で書くか、`NullableUlid`・`NullableMinuteOfDay` を参照する | `oneOf: [{$ref: Ulid}, {type: "null"}]`、`anyOf` |
| null を許すオブジェクト・列挙 | `oneOf: [{$ref: ...}, {type: "null"}]` | — |
| 本文・一覧・列挙の型 | すべて名前を付けて `components` に置く。一覧は `XxxList`（`items` と、ページを分けるなら `nextToken`） | パスのファイルに直接書いた型 |
| 成功の応答 | 1つの操作の成功の応答は1つの型にする。結果の違うものは操作を分ける（例 就業ルールの版の作成と影響の確認） | 200 と 201 で違う型を返す |
| 操作の名前 | `operationId` は動詞で始まる lowerCamelCase（例 `createMyPunch`） | — |
| ファイルの本文（CSV など） | `type: string`、`format: binary`。上限などは `description` に書く | `type: string` だけ（生成したクライアントが JSON として読み書きする） |
| 実装のモジュール | 各操作に `x-module`（`Auth`・`Punch`・`Self`・`AdminOps`・`AdminSettings`・`Ops`）を書く。その操作を実装する関数のプロジェクトで（[全体構成](../design/architecture.md) §4.2）、操作の表と関数の一覧の生成に使う（同 §4.4）。検証で、書き忘れと誤った値を誤りにする | — |

## 3. エンドポイント一覧

テナント向けは 122 操作。この表は結合した仕様から作る（仕様を変えたら作り直す）。「権限」の列の `employee`・`admin`・`kiosk` はスコープ、「（テナント管理者）」はテナント管理者だけの操作を表す。

<!-- endpoints:begin -->
### 3.1 認証（/auth/v1）

| メソッド | パス | 操作 | 概要 | 権限 | UC・画面 |
| --- | --- | --- | --- | --- | --- |
| POST | `/auth/v1/login` | login | ログインする | 公開 | UC-AU-02 |
| POST | `/auth/v1/login/mfa` | verifyMfa | 二要素認証のコードを確認する | 公開 | UC-AU-02 |
| POST | `/auth/v1/token` | refreshToken | アクセストークンを更新する | リフレッシュ Cookie | UC-AU-09 |
| POST | `/auth/v1/logout` | logout | ログアウトする | リフレッシュ Cookie | UC-AU-03 |
| POST | `/auth/v1/password/change` | changePassword | パスワードを変更する | employee | UC-AU-04 |
| POST | `/auth/v1/password/initial` | changeInitialPassword | 初期パスワードを変更する | 公開 | UC-AU-02 |
| POST | `/auth/v1/password/forgot` | forgotPassword | パスワードの再設定を依頼する | 公開 | UC-AU-05 |
| POST | `/auth/v1/password/reset` | resetPassword | パスワードを再設定する | 公開 | UC-AU-05 |
| POST | `/auth/v1/invitations/inspect` | inspectInvitation | 招待の内容を確認する | 公開 | UC-AU-01 |
| POST | `/auth/v1/invitations/accept` | acceptInvitation | 招待を受けてパスワードを設定する | 公開 | UC-AU-01 |
| POST | `/auth/v1/step-up` | stepUp | 管理機能のために再認証する | employee | UC-AU-07 |
| POST | `/auth/v1/mfa/totp/setup` | setupTotp | TOTP の登録を始める | employee | UC-AU-06 |
| POST | `/auth/v1/mfa/totp/confirm` | confirmTotp | TOTP の登録を完了する | employee | UC-AU-06 |
| POST | `/auth/v1/kiosk/register` | registerKiosk | 共有端末を登録する | 公開 | UC-KI-01 |
| POST | `/auth/v1/kiosk/token` | issueKioskToken | 共有端末のアクセストークンを発行する | 端末 Cookie | UC-KI-02 |

### 3.2 本人（/api/v1/me）

| メソッド | パス | 操作 | 概要 | 権限 | UC・画面 |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/v1/me` | getMe | 本人の情報を取得する | employee | 全画面 |
| GET | `/api/v1/me/today` | getMyToday | 当日の状態を取得する | employee | UC-EM-01 |
| POST | `/api/v1/me/punches` | createMyPunch | 打刻する | employee | UC-EM-02 |
| GET | `/api/v1/me/timesheets/{period}` | getMyTimesheet | 月の勤怠を取得する | employee | UC-EM-03 |
| GET | `/api/v1/me/daily-attendances/{workDate}` | getMyDailyAttendance | 日別の詳細を取得する | employee | UC-EM-04 |
| GET | `/api/v1/me/schedules` | listMySchedules | 自分のシフトを取得する | employee | UC-EM-05 |
| GET | `/api/v1/me/leave` | getMyLeave | 休暇の残りを取得する | employee | UC-EM-06 |
| GET | `/api/v1/me/requests` | listMyRequests | 自分の申請の一覧を取得する | employee | EM08 |
| POST | `/api/v1/me/requests` | createMyRequest | 申請する | employee | UC-WF-01、UC-WF-02 |
| POST | `/api/v1/me/requests/preview` | previewMyRequest | 申請を試算する | employee | EM06、EM07 |
| PUT | `/api/v1/me/kiosk-pin` | setMyKioskPin | 共有端末用の PIN を設定する | employee | UC-AU-08 |

### 3.3 申請・承認（/api/v1/requests、/api/v1/approvals）

| メソッド | パス | 操作 | 概要 | 権限 | UC・画面 |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/v1/requests/{requestId}` | getRequest | 申請の詳細を取得する | employee | UC-WF-03〜09、EM09、AP02 |
| POST | `/api/v1/requests/{requestId}/actions` | actOnRequest | 申請を操作する | employee | UC-WF-03、UC-WF-04、UC-WF-05、UC-WF-07、UC-WF-08、UC-WF-09 |
| GET | `/api/v1/approvals` | listApprovals | 承認待ちの申請の一覧を取得する | employee | UC-WF-06 |

### 3.4 共有端末（/api/v1/kiosk）

| メソッド | パス | 操作 | 概要 | 権限 | UC・画面 |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/v1/kiosk/sessions` | createKioskSession | 社員番号と PIN を照合し、受付と当日の状態を取得する | kiosk | UC-KI-02 |
| POST | `/api/v1/kiosk/pin` | changeKioskPin | 初期 PIN を変更する | kiosk | UC-KI-03 |
| POST | `/api/v1/kiosk/punches` | createKioskPunch | 共有端末で打刻する | kiosk | UC-KI-02 |

### 3.5 管理: 勤怠・アラート・監査ログ・出力・ジョブ（/api/v1/admin）

| メソッド | パス | 操作 | 概要 | 権限 | UC・画面 |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/v1/admin/dashboard` | getDashboard | ダッシュボードを取得する | admin | UC-AD-01 |
| GET | `/api/v1/admin/timesheets` | listTimesheets | 勤怠を一覧で取得する | admin | UC-AD-02 |
| GET | `/api/v1/admin/employees/{employeeId}/timesheets/{period}` | getEmployeeTimesheet | 従業員の月の勤怠を取得する | admin | AD03 |
| GET | `/api/v1/admin/employees/{employeeId}/daily-attendances/{workDate}` | getEmployeeDailyAttendance | 従業員の日別の詳細を取得する | admin | AD03 |
| POST | `/api/v1/admin/employees/{employeeId}/daily-attendances/{workDate}/adjustments` | createAdjustment | 勤怠を補正する | admin | UC-AD-03 |
| GET | `/api/v1/admin/alerts` | listAlerts | アラートの一覧を取得する | admin | UC-AD-09 |
| POST | `/api/v1/admin/alerts/{alertId}/acknowledge` | acknowledgeAlert | アラートを対応済みにする | admin | UC-AD-09 |
| GET | `/api/v1/admin/audit-logs` | listAuditLogs | 監査ログを検索する | admin | UC-AD-14 |
| POST | `/api/v1/admin/exports` | createExport | CSV の出力を依頼する | admin | UC-AD-08 |
| GET | `/api/v1/admin/jobs` | listJobs | ジョブの一覧を取得する | admin | AD06 |
| GET | `/api/v1/admin/jobs/{jobId}` | getJob | ジョブの状況を取得する | admin | UC-AD-08、AD06 |

### 3.6 管理: シフト（/api/v1/admin）

| メソッド | パス | 操作 | 概要 | 権限 | UC・画面 |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/v1/admin/departments/{departmentId}/shift-plans/{period}` | getShiftPlan | シフト表を取得する | admin | AD04 |
| PUT | `/api/v1/admin/departments/{departmentId}/shift-plans/{period}` | saveShiftPlanDraft | シフト表の下書きを保存する | admin | UC-AD-04 |
| POST | `/api/v1/admin/departments/{departmentId}/shift-plans/{period}/publish` | publishShiftPlan | シフト表を公開する | admin | UC-AD-04 |
| GET | `/api/v1/admin/departments/{departmentId}/shift-plans/{period}/csv` | exportShiftPlanCsv | シフト表を CSV で出力する | admin | UC-AD-05 |
| PUT | `/api/v1/admin/departments/{departmentId}/shift-plans/{period}/csv` | importShiftPlanCsv | シフト表を CSV で取り込む | admin | UC-AD-05 |
| PATCH | `/api/v1/admin/employees/{employeeId}/schedules/{workDate}` | changeSchedule | 公開後の勤務予定を変更する | admin | UC-AD-06 |

### 3.7 管理: 締め（/api/v1/admin）

| メソッド | パス | 操作 | 概要 | 権限 | UC・画面 |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/v1/admin/period-closes/{period}` | getPeriodClose | 締めの状況と締める前のチェックを取得する | admin | UC-AD-07 |
| POST | `/api/v1/admin/period-closes/{period}/close` | closePeriod | 締める | admin | UC-AD-07 |
| POST | `/api/v1/admin/period-closes/{period}/reopen` | reopenPeriod | 再オープンする | admin | UC-AD-07 |

### 3.8 管理: 休暇（/api/v1/admin）

| メソッド | パス | 操作 | 概要 | 権限 | UC・画面 |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/v1/admin/leave-grant-plans` | listLeaveGrantPlans | 有給の付与予定を取得する | admin | UC-AD-10 |
| POST | `/api/v1/admin/leave-grant-plans/{planId}/decision` | decideLeaveGrantPlan | 保留の付与予定について判断する | admin | UC-AD-10 |
| GET | `/api/v1/admin/leave-balances` | listLeaveBalances | 従業員別の休暇の残りを取得する | admin | AD08 |
| GET | `/api/v1/admin/leave-obligations` | listLeaveObligations | 年5日の取得義務の進捗を取得する | admin | UC-AD-12 |
| GET | `/api/v1/admin/employees/{employeeId}/leave` | getEmployeeLeave | 従業員の休暇の残りを取得する | admin | UC-AD-11、AD08 |
| POST | `/api/v1/admin/employees/{employeeId}/leave-grants` | createLeaveGrant | 休暇を手動で付与する | admin | UC-AD-11 |
| POST | `/api/v1/admin/employees/{employeeId}/leave-adjustments` | createLeaveAdjustment | 休暇の残りを調整する | admin | UC-AD-11 |
| POST | `/api/v1/admin/employees/{employeeId}/leave-designations` | createLeaveDesignation | 時季指定を登録する | admin | UC-AD-12 |
| PUT | `/api/v1/admin/employees/{employeeId}/statutory-entitlements/{leaveTypeId}/{fiscalYear}` | putStatutoryEntitlement | 子の看護等休暇・介護休暇の年度の日数を登録する | admin | UC-AD-13 |
| GET | `/api/v1/admin/employees/{employeeId}/leaves-of-absence` | listLeavesOfAbsence | 従業員の休業の一覧を取得する | admin | UC-SE-17 |
| POST | `/api/v1/admin/employees/{employeeId}/leaves-of-absence` | createLeaveOfAbsence | 休業を登録する | admin | UC-SE-17 |
| PATCH | `/api/v1/admin/employees/{employeeId}/leaves-of-absence/{leaveOfAbsenceId}` | updateLeaveOfAbsence | 休業を変更する | admin | UC-SE-17 |
| DELETE | `/api/v1/admin/employees/{employeeId}/leaves-of-absence/{leaveOfAbsenceId}` | deleteLeaveOfAbsence | 休業を削除する | admin | UC-SE-17 |

### 3.9 管理: 設定（/api/v1/admin）

| メソッド | パス | 操作 | 概要 | 権限 | UC・画面 |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/v1/admin/employees` | listEmployees | 従業員の一覧を取得する | admin | SE01 |
| POST | `/api/v1/admin/employees` | createEmployee | 従業員を登録する | admin（テナント管理者） | UC-SE-01 |
| GET | `/api/v1/admin/employees/{employeeId}` | getEmployee | 従業員を取得する | admin | SE01 |
| PATCH | `/api/v1/admin/employees/{employeeId}` | updateEmployee | 従業員の基本情報を変更する | admin（テナント管理者） | UC-SE-01、UC-SE-16 |
| DELETE | `/api/v1/admin/employees/{employeeId}` | deleteEmployee | 従業員を削除する | admin（テナント管理者） | UC-SE-15 |
| GET | `/api/v1/admin/employees/{employeeId}/assignments` | listEmployeeAssignments | 従業員の条件の履歴を取得する | admin | UC-SE-02 |
| POST | `/api/v1/admin/employees/{employeeId}/assignments` | createEmployeeAssignment | 従業員の条件を変更する | admin（テナント管理者） | UC-SE-02 |
| POST | `/api/v1/admin/employees/{employeeId}/terminate` | terminateEmployee | 従業員を退職させる | admin（テナント管理者） | UC-SE-03 |
| POST | `/api/v1/admin/employees/{employeeId}/reinstate` | reinstateEmployee | 退職を取り消す | admin（テナント管理者） | UC-SE-03 |
| PUT | `/api/v1/admin/employees/{employeeId}/roles` | updateEmployeeRoles | ロールと管理範囲を設定する | admin（テナント管理者） | UC-SE-05 |
| POST | `/api/v1/admin/employees/{employeeId}/account/invitation` | resendInvitation | 招待を再送する | admin | UC-AU-10 |
| POST | `/api/v1/admin/employees/{employeeId}/account/initial-password` | issueInitialPassword | 初期パスワードを発行する | admin（テナント管理者） | UC-AU-10 |
| POST | `/api/v1/admin/employees/{employeeId}/account/unlock` | unlockAccount | アカウントのロックを解除する | admin（テナント管理者） | UC-AU-10 |
| POST | `/api/v1/admin/employees/{employeeId}/account/mfa-reset` | resetMfa | TOTP を解除する | admin（テナント管理者） | UC-AU-10 |
| POST | `/api/v1/admin/employees/{employeeId}/account/pin-reset` | resetPin | 共有端末用の PIN をリセットする | admin | UC-AU-10 |
| POST | `/api/v1/admin/employees/{employeeId}/account/disable` | disableAccount | アカウントを無効にする | admin（テナント管理者） | UC-AU-10 |
| POST | `/api/v1/admin/employee-imports` | previewEmployeeImport | 従業員の CSV を検証する | admin（テナント管理者） | UC-SE-04 |
| POST | `/api/v1/admin/employee-imports/{importId}/apply` | applyEmployeeImport | 従業員の CSV を反映する | admin（テナント管理者） | UC-SE-04 |
| GET | `/api/v1/admin/departments` | listDepartments | 部署の一覧を取得する | admin | UC-SE-06 |
| POST | `/api/v1/admin/departments` | createDepartment | 部署を登録する | admin（テナント管理者） | UC-SE-06 |
| PATCH | `/api/v1/admin/departments/{departmentId}` | updateDepartment | 部署を変更する | admin（テナント管理者） | UC-SE-06 |
| GET | `/api/v1/admin/employment-types` | listEmploymentTypes | 雇用区分の一覧を取得する | admin | UC-SE-06 |
| POST | `/api/v1/admin/employment-types` | createEmploymentType | 雇用区分を登録する | admin（テナント管理者） | UC-SE-06 |
| PATCH | `/api/v1/admin/employment-types/{employmentTypeId}` | updateEmploymentType | 雇用区分を変更する | admin（テナント管理者） | UC-SE-06 |
| GET | `/api/v1/admin/work-rules` | listWorkRules | 就業ルールの一覧を取得する | admin | UC-SE-07 |
| POST | `/api/v1/admin/work-rules` | createWorkRule | 就業ルールを登録する | admin（テナント管理者） | UC-SE-07 |
| GET | `/api/v1/admin/work-rules/{workRuleId}` | getWorkRule | 就業ルールを取得する | admin | UC-SE-07 |
| POST | `/api/v1/admin/work-rules/{workRuleId}/versions` | createWorkRuleVersion | 就業ルールの新しい版を作る | admin（テナント管理者） | UC-SE-07 |
| POST | `/api/v1/admin/work-rules/{workRuleId}/versions/preview` | previewWorkRuleVersion | 就業ルールの新しい版の影響を確認する | admin（テナント管理者） | UC-SE-07 |
| PUT | `/api/v1/admin/work-rules/{workRuleId}/versions/{validFrom}` | replaceWorkRuleVersion | 就業ルールの版を置き換える | admin（テナント管理者） | UC-SE-07 |
| GET | `/api/v1/admin/work-patterns` | listWorkPatterns | 勤務パターンの一覧を取得する | admin | UC-SE-08 |
| POST | `/api/v1/admin/work-patterns` | createWorkPattern | 勤務パターンを登録する | admin（テナント管理者） | UC-SE-08 |
| PATCH | `/api/v1/admin/work-patterns/{workPatternId}` | updateWorkPattern | 勤務パターンを変更する | admin（テナント管理者） | UC-SE-08 |
| GET | `/api/v1/admin/calendars` | listCalendars | 会社カレンダーの一覧を取得する | admin | UC-SE-09 |
| POST | `/api/v1/admin/calendars` | createCalendar | 会社カレンダーを登録する | admin（テナント管理者） | UC-SE-09 |
| GET | `/api/v1/admin/calendars/{calendarId}/years/{year}` | getCalendarYear | カレンダーの年を取得する | admin | UC-SE-09 |
| PUT | `/api/v1/admin/calendars/{calendarId}/years/{year}` | putCalendarYear | カレンダーの年を保存する | admin（テナント管理者） | UC-SE-09 |
| POST | `/api/v1/admin/calendars/{calendarId}/years/{year}/national-holidays` | applyNationalHolidays | 国民の祝日を一括で設定する | admin（テナント管理者） | UC-SE-09 |
| GET | `/api/v1/admin/leave-types` | listLeaveTypes | 休暇種別の一覧を取得する | admin | UC-SE-10 |
| POST | `/api/v1/admin/leave-types` | createLeaveType | 休暇種別を登録する | admin（テナント管理者） | UC-SE-10 |
| PATCH | `/api/v1/admin/leave-types/{leaveTypeId}` | updateLeaveType | 休暇種別を変更する | admin（テナント管理者） | UC-SE-10 |
| GET | `/api/v1/admin/leave-policies` | listLeavePolicies | 有給の付与ルールの一覧を取得する | admin | UC-SE-10 |
| POST | `/api/v1/admin/leave-policies` | createLeavePolicy | 有給の付与ルールを登録する | admin（テナント管理者） | UC-SE-10 |
| PATCH | `/api/v1/admin/leave-policies/{leavePolicyId}` | updateLeavePolicy | 有給の付与ルールを変更する | admin（テナント管理者） | UC-SE-10 |
| GET | `/api/v1/admin/approval-routes` | listApprovalRoutes | 承認ルートの一覧を取得する | admin | UC-SE-11 |
| POST | `/api/v1/admin/approval-routes` | createApprovalRoute | 承認ルートを登録する | admin（テナント管理者） | UC-SE-11 |
| PATCH | `/api/v1/admin/approval-routes/{approvalRouteId}` | updateApprovalRoute | 承認ルートを変更する | admin（テナント管理者） | UC-SE-11 |
| DELETE | `/api/v1/admin/approval-routes/{approvalRouteId}` | deleteApprovalRoute | 承認ルートを削除する | admin（テナント管理者） | UC-SE-11 |
| GET | `/api/v1/admin/overtime-agreements` | listOvertimeAgreements | 36協定の一覧を取得する | admin | UC-SE-12 |
| POST | `/api/v1/admin/overtime-agreements` | createOvertimeAgreement | 36協定を登録する | admin（テナント管理者） | UC-SE-12 |
| PATCH | `/api/v1/admin/overtime-agreements/{agreementId}` | updateOvertimeAgreement | 36協定を変更する | admin（テナント管理者） | UC-SE-12 |
| GET | `/api/v1/admin/devices` | listDevices | 共有端末の一覧を取得する | admin | UC-SE-13 |
| POST | `/api/v1/admin/devices` | createDevice | 共有端末を登録し、登録コードを発行する | admin（テナント管理者） | UC-KI-01、UC-SE-13 |
| POST | `/api/v1/admin/devices/{deviceId}/registration-code` | reissueDeviceRegistrationCode | 登録コードを発行し直す | admin（テナント管理者） | UC-SE-13 |
| POST | `/api/v1/admin/devices/{deviceId}/revoke` | revokeDevice | 共有端末を失効させる | admin（テナント管理者） | UC-SE-13 |
| GET | `/api/v1/admin/tenant-settings` | getTenantSettings | テナントの設定を取得する | admin | UC-SE-14 |
| PATCH | `/api/v1/admin/tenant-settings` | updateTenantSettings | テナントの設定を変更する | admin（テナント管理者） | UC-SE-14 |
<!-- endpoints:end -->

## 4. 運営者の API

[ops/openapi.yaml](ops/openapi.yaml)。10 操作。AWS IAM の署名（SigV4）で認証し、運営者の IAM ロールにだけ呼び出しを許す。操作は運営の監査ログに残す。

<!-- ops:begin -->
### 4.1 テナント

| メソッド | パス | 操作 | 概要 | 権限 | UC・画面 |
| --- | --- | --- | --- | --- | --- |
| GET | `/ops/v1/tenants` | listTenants | テナントの一覧を取得する | IAM 署名 |  |
| POST | `/ops/v1/tenants` | createTenant | テナントを開設する | IAM 署名 | UC-OP-01 |
| GET | `/ops/v1/tenants/{tenantId}` | getTenant | テナントを取得する | IAM 署名 |  |
| POST | `/ops/v1/tenants/{tenantId}/suspend` | suspendTenant | テナントを停止する | IAM 署名 | UC-OP-02 |
| POST | `/ops/v1/tenants/{tenantId}/resume` | resumeTenant | テナントを再開する | IAM 署名 | UC-OP-02 |
| POST | `/ops/v1/tenants/{tenantId}/employees/{employeeId}/mfa-reset` | resetTenantAdminMfa | テナント管理者の TOTP を解除する | IAM 署名 | UC-OP-03 |

### 4.2 サービス全体の設定

| メソッド | パス | 操作 | 概要 | 権限 | UC・画面 |
| --- | --- | --- | --- | --- | --- |
| GET | `/ops/v1/statutory-parameters` | listStatutoryParameterVersions | 法定値の版の一覧を取得する | IAM 署名 |  |
| POST | `/ops/v1/statutory-parameters` | createStatutoryParameterVersion | 法定値の新しい版を作る | IAM 署名 |  |
| GET | `/ops/v1/national-holidays/{year}` | getNationalHolidays | 国民の祝日の一覧を取得する | IAM 署名 |  |
| PUT | `/ops/v1/national-holidays/{year}` | putNationalHolidays | 国民の祝日の一覧を登録する | IAM 署名 |  |
<!-- ops:end -->

## 5. エラーコード

`code` の一覧。画面の文言はユースケースのエラーの表に書く。

### 5.1 共通

| コード | 状態 | 意味 |
| --- | --- | --- |
| `VALIDATION_FAILED` | 400 | 入力の形式の誤り。`errors` に項目ごとの誤り（`field` は本文なら JSON Pointer、引数なら名前。項目の誤りのコードは §2.4） |
| `UNAUTHENTICATED` | 401 | 認証されていない、またはアクセストークンの期限切れ |
| `FORBIDDEN` | 403 | ロール・管理範囲の外 |
| `REAUTH_REQUIRED` | 403 | 管理機能の再認証が必要（最後の管理操作から1時間を過ぎた） |
| `TENANT_SUSPENDED` | 403 | テナントが停止中（UC-OP-02） |
| `CSRF_HEADER_MISSING` | 403 | Cookie で認証する経路で `X-TNA-Request` がない |
| `SELF_OPERATION_NOT_ALLOWED` | 403 | 自分自身の勤怠の補正・休暇の操作など |
| `NOT_FOUND` | 404 | 対象がない、または権限の範囲外 |
| `IDEMPOTENCY_KEY_IN_PROGRESS` | 409 | 同じ冪等キーの要求を処理している |
| `VERSION_CONFLICT` | 412 | `If-Match` が一致しない |
| `PAYLOAD_TOO_LARGE` | 413 | 取り込むファイルが大きすぎる |
| `UNSUPPORTED_MEDIA_TYPE` | 415 | 本文の形式（`Content-Type`）が操作の仕様と違う（§2.2） |
| `IDEMPOTENCY_KEY_REUSED` | 422 | 同じ冪等キーで内容の違う要求 |
| `PRECONDITION_REQUIRED` | 428 | `If-Match` がない |
| `RATE_LIMITED` | 429 | レート制限 |
| `INTERNAL_ERROR` | 500 | 想定外のエラー（調査用の ID を `instance` に入れる） |
| `SERVICE_UNAVAILABLE` | 503 | 一時的に利用できない |

### 5.2 認証・アカウント

| コード | 状態 | 意味 |
| --- | --- | --- |
| `INVALID_CREDENTIALS` | 401 | ログインID・パスワードの誤り。アカウントの有無やロックを区別しない |
| `MFA_INVALID` | 401 | TOTP のコードの誤り |
| `MFA_EXPIRED` | 401 | 二要素認証の途中の状態の期限切れ（5分） |
| `SESSION_EXPIRED` | 401 | リフレッシュトークンが無効・期限切れ・再利用。ログイン画面に戻す |
| `MFA_ENROLLMENT_REQUIRED` | 403 | 管理者ロールで TOTP が未登録 |
| `ACCOUNT_STATE_INVALID` | 409 | アカウントの状態に合わない操作（有効なアカウントへの招待の再送など） |
| `LAST_TENANT_ADMIN` | 409 | テナント管理者がいなくなる |
| `TOKEN_EXPIRED` | 410 | 招待・パスワード再設定のリンク、共有端末の登録コードの期限切れ・使用済み |
| `PASSWORD_POLICY_VIOLATION` | 422 | パスワードの条件を満たさない（短すぎる、漏えい・頻出、企業コード・ログインID・氏名を含む、繰り返しや連続した文字。`errors` の理由のコードは §2.4） |
| `PIN_TOO_WEAK` | 422 | 単純すぎる PIN |
| `IP_NOT_ALLOWED` | 403 | 許可していない IP からの利用（IP 制限は P2。MVP では返さない） |

### 5.3 打刻・勤怠・共有端末

| コード | 状態 | 意味 |
| --- | --- | --- |
| `KIOSK_PIN_INVALID` | 401 | 社員番号または PIN の誤り |
| `KIOSK_SESSION_EXPIRED` | 401 | 共有端末の受付（60秒）の期限切れ |
| `DEVICE_REVOKED` | 401 | 共有端末が失効している |
| `PUNCH_SEQUENCE_INVALID` | 409 | 状態に合わない打刻（出勤済みの出勤など） |
| `PUNCH_WINDOW_EXPIRED` | 409 | 打刻の受付期限を過ぎた |
| `PERIOD_CLOSED` | 409 | 対象の期間が締め済み |
| `EMPLOYEE_INACTIVE` | 409 | 在籍の期間の外 |
| `CORRECTION_ALREADY_PENDING` | 409 | 同じ日に承認待ちの打刻修正申請がある |
| `CORRECTION_SEQUENCE_INVALID` | 422 | 修正・補正の後の打刻の順序の誤り |
| `KIOSK_PIN_LOCKED` | 423 | PIN の誤りが続いたため一時的に受け付けない |

### 5.4 申請・休暇

| コード | 状態 | 意味 |
| --- | --- | --- |
| `ACTION_NOT_ALLOWED` | 403 | その申請に、その操作はできない（自分の申請の承認など） |
| `REQUEST_STATE_INVALID` | 409 | 申請の状態に合わない操作 |
| `LEAVE_BALANCE_INSUFFICIENT` | 422 | 休暇の残りが足りない（承認待ちの分を含む） |
| `LEAVE_UNIT_NOT_ALLOWED` | 422 | その休暇種別では使えない単位 |
| `LEAVE_NOT_WORKDAY` | 422 | 勤務予定のない日 |
| `LEAVE_OVERLAP` | 422 | 同じ日・時間帯にほかの休暇がある |
| `LEAVE_HOURLY_CAP_EXCEEDED` | 422 | 時間単位で取れる上限（年5日分）を超える |
| `LEAVE_OF_ABSENCE_OVERLAP` | 409 | ほかの休業と期間が重なる |

### 5.5 管理

| コード | 状態 | 意味 |
| --- | --- | --- |
| `NATIONAL_HOLIDAYS_NOT_AVAILABLE` | 404 | その年の国民の祝日の一覧がまだない |
| `CODE_DUPLICATED` | 409 | マスタのコードの重複 |
| `EMPLOYEE_CODE_DUPLICATED` | 409 | 社員番号の重複 |
| `EMAIL_DUPLICATED` | 409 | メールアドレスの重複 |
| `DEPARTMENT_IN_USE` | 409 | 所属している従業員か、有効な配下の部署がある部署は無効にできない |
| `EMPLOYMENT_TYPE_IN_USE` | 409 | いまの条件で使っている従業員がいる雇用区分は無効にできない |
| `EMPLOYEE_HAS_RECORDS` | 409 | 記録があるか、ログインしたことがあるか、ロールや所属長・承認者の指定がある従業員は削除できない |
| `HIRE_DATE_LOCKED` | 409 | 入社日を訂正できない（旧い入社日と新しい入社日の間に記録がある、締め済みの期間にかかる、有給を付与した） |
| `TERMINATION_LOCKED` | 409 | 退職日を変えられない・取り消せない（退職日がない、前か新しい退職日が締め済みの期間にあるか入社日より前、退職を処理した後の変更） |
| `PAYROLL_CLOSING_DAY_LOCKED` | 409 | 締めた期間があるため、給与の締め日を変えられない |
| `DEFAULT_ROUTE_REQUIRED` | 409 | 既定の承認ルートは削除できない |
| `AGREEMENT_OVERLAP` | 409 | 同じ部署か同じ雇用区分を同じ細かさで対象にした36協定と、有効期間が重なる |
| `SHIFT_PLAN_NOT_FOUND` | 409 | 公開するシフト表の下書きがない |
| `SCHEDULE_NOT_PUBLISHED` | 409 | 未公開の勤務予定は変更できない（下書きを直す） |
| `CLOSE_PRECHECK_FAILED` | 409 | 締めを止めるエラーや申請が残っている |
| `PERIOD_ALREADY_CLOSED` | 409 | すでに締めている |
| `PERIOD_NOT_CLOSED` | 409 | 締めていない期間は再オープンできない |
| `CLOSE_TOO_EARLY` | 409 | 期間の末日の打刻の受付期限を過ぎていないため、まだ締められない |
| `IMPORT_EXPIRED` | 410 | 従業員の取込の検証結果の期限切れ（1時間） |
| `SHIFT_PLAN_INVALID` | 422 | シフト表の検証のエラー（`errors` に内容） |
| `DEPARTMENT_PARENT_INVALID` | 422 | 自分自身や配下の部署を親にした |

### 5.6 運営者

| コード | 状態 | 意味 |
| --- | --- | --- |
| `TENANT_CODE_DUPLICATED` | 409 | 企業コードの重複 |
| `TENANT_STATE_INVALID` | 409 | テナントの状態に合わない操作（停止中の停止など） |
| `EFFECTIVE_DATE_INVALID` | 422 | 法定値の版の施行日が今日以前 |
