# データ設計（DynamoDB）

DynamoDB と S3 のデータの設計（キー、索引、アクセスパターン、書き込みの単位、権限、保存期間、費用）。全体の構成は [全体構成](architecture.md)、項目の形は [API 仕様](../api/README.md) に合わせる。

## 1. 方針

| 項目 | 方針 |
| --- | --- |
| テーブル | 業務・認証・監査の3つに分ける。業務のデータは1つのテーブルにすべてのエンティティを入れる（単一テーブル設計） |
| テナントの分離 | パーティションキー（索引を含む）をすべて `T#{tenantId}` で始め、IAM の `dynamodb:LeadingKeys` で、要求のテナント以外のキーに触れられないようにする |
| キー | 従業員ごとのパーティションに、勤務日の記録を日付でまとめて置く。日付だけ、テナントと日付だけをパーティションキーにしない |
| 容量 | オンデマンド。本番の前に warm throughput を設定する |
| 保存と削除 | 保存期間を項目ごとに計算して TTL で削除する。削除したデータも PITR で35日間は戻せる |
| 古いデータ | 移さない。保存の費用が読み書きの費用の半分を超えたら、テーブルクラスを Standard-IA に切り替える |
| 暗号化 | 用途ごとのカスタマーマネージドキー |
| 締めの確定値 | 締めるたびに、従業員ごとの集計を版として残す |

## 2. テーブルとバケット

| 論理名 | 用途 | キー | 索引 | Streams | TTL | PITR | テーブルクラス | 暗号化の鍵 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `main` | 業務データ（テナントのすべてのエンティティ）と、サービス全体の設定（法定値、国民の祝日、テナントの台帳） | `PK`、`SK`（文字列） | `GSI1`、`GSI2`、`GSI3`（名簿） | 新旧の両方（`NEW_AND_OLD_IMAGES`） | `ttl` | 有効 | Standard（§13.2 の条件で Standard-IA） | `main` |
| `auth` | 認証（アカウント、資格情報、セッション、各種トークン、共有端末の資格情報、署名鍵の状態） | `PK`、`SK` | なし | なし | `ttl` | 有効 | Standard | `auth` |
| `audit` | 監査ログ | `PK`、`SK` | `GSI1`（操作者）、`GSI2`（対象の従業員） | なし | `ttl` | 有効 | Standard-IA（作成時から。保存が主な費用のため） | `audit` |
| S3 `files` | CSV の出力と取込の一時ファイル | プレフィックス（§11） | — | — | ライフサイクル | — | — | `files` |
| S3 `public-keys` | JWT の公開鍵（discovery と JWKS）。CloudFront だけが読む | — | — | — | — | — | — | S3 の管理の鍵 |
| S3 `access-logs` | 2つのバケットのアクセスログ（90日） | プレフィックス（バケット名） | — | — | ライフサイクル | — | — | S3 の管理の鍵 |

- 物理名は環境ごとに付ける（例 `tna-prod-main`）。バケットは名前が世界で一意のため、末尾にアカウント ID を付ける（例 `tna-prod-files-{アカウント}`）。名前の決め方とスタックの間の受け渡しは [全体構成](architecture.md) §8.1。
- 検証と本番のテーブルは削除保護を有効にし、スタックを消してもテーブル・バケット・鍵を残す。開発だけは使い捨てで、スタックと一緒に消える。
- ローカルセカンダリインデックス（LSI）は使わない（パーティションの大きさに 10GB の上限がつくため）。

## 3. キーの約束

### 3.1 書き方

- 区切りは `#`。ID は ULID（大文字26文字）、日付は `YYYY-MM-DD`、給与の締め期間は期間 ID（`YYYY-MM`）、時点は UTC の ISO 8601（ミリ秒まで、`Z` 付き）で書く。
- テナントに属する項目の `PK` は、必ず `T#{tenantId}` で始める。索引のキー（`GSI1PK`、`GSI2PK`、`GSI3PK`）も同じ。
- 時刻の順に並べたい項目は、ULID を `SK` に使う（ULID は作成時刻の順に並ぶ）。
- 利用者が入力するコード（社員番号など）は、一意性を確かめる項目（§4.2 の `UNIQ#`）の `SK` の最後にだけ使う。コードでの並べ替えや部分一致の検索は、読んだ後にメモリで行う。

### 3.2 共通の属性

| 属性 | 型 | 内容 |
| --- | --- | --- |
| `PK`、`SK` | S | キー |
| `entity` | S | 項目の種類（例 `Punch`）。Streams の振り分けと、読んだ項目の型の判断に使う |
| `schemaVersion` | N | 項目の形の版（§16） |
| `version` | N | 楽観ロックの版。更新のたびに1増やす。API の ETag と `etag` 項目は `"{version}"`（§7.1） |
| `createdAt`、`createdBy`、`updatedAt`、`updatedBy` | S | 記録 |
| `GSI1PK`、`GSI1SK`、`GSI2PK`、`GSI2SK`、`GSI3PK`、`GSI3SK` | S | 索引のキー。索引に載せる項目だけが持つ（疎な索引） |
| `ttl` | N | 削除する時点（エポック秒）。§13 |

ほかの属性の名前は、API の項目名（lowerCamelCase）に合わせる。列挙の値も API と同じ lowerCamelCase の文字列にする。値のない属性は書かない（null の属性を持たない）。集合（SS、NS）とバイナリは使わない（一覧は List、バイト列は Base64 の文字列）。

項目と型の変換は、型を JSON（ソース生成）にして項目の属性に写す汎用の変換で行う。型ごとの変換のコードは書かない。大量に読む名簿（GSI3）だけ、速さのため手書きの変換にする（変換は1件あたり、汎用で読む 9µs・書く 11µs、手書きで読む 1.1µs・書く 4.1µs。手元の計測）。

### 3.3 パーティションの一覧（業務テーブル）

| パーティションキー | 中身 | 大きさの目安 |
| --- | --- | --- |
| `SYS#STATUTORY` | 法定値の版 | 数件 |
| `SYS#HOLIDAYS` | 国民の祝日（年ごと） | 年に1件 |
| `SYS#TENANTS` | テナントの台帳（運営者用） | テナントの数 |
| `SYS#IDEM` | 運営者の API の冪等キーの記録 | 24時間分 |
| `T#{t}` | テナントのルート。設定、マスタ、一意性の確認、締め | 数千件、数MB |
| `T#{t}#E#{e}` | 従業員。プロフィール、条件の履歴、勤務日ごとの記録、期間の集計、締めの版、休暇、承認の受信箱 | 1人の6年分で約1万件、約10MB |
| `T#{t}#R#{r}` | 申請と承認の履歴 | 1件の申請で数件 |
| `T#{t}#SP#{d}#{p}` | シフト表（部署×給与の締め期間） | 部署の人数＋1件 |
| `T#{t}#ALERT` | アラート | 1人あたり月に数件 |
| `T#{t}#JOB#{j}` | ジョブ | 1件 |
| `T#{t}#IMPORT#{i}` | 従業員の取込の検証結果 | 1件 |
| `T#{t}#IDEM#{principal}` | 冪等キーの記録 | 24時間分 |

`{t}` はテナントの ID、`{e}` は従業員の ID、`{r}` は申請の ID、`{d}` は部署の ID、`{p}` は期間 ID、`{j}` はジョブの ID、`{i}` は取込の ID。

## 4. 業務テーブル（main）の項目

### 4.1 サービス全体

| entity | PK | SK | 主な属性 | 書く役割 |
| --- | --- | --- | --- | --- |
| StatutoryParametersVersion | `SYS#STATUTORY` | `V#{effectiveFrom}#{versionId}` | 法定値（運営者 API の `StatutoryParameters`）、根拠、状態 | 運営者 |
| NationalHolidayYear | `SYS#HOLIDAYS` | `Y#{year}` | 祝日の一覧、出典 | 運営者 |
| TenantRegistry | `SYS#TENANTS` | `T#{tenantId}` | 企業コード、企業名、状態、在籍者の数、作成日時 | 運営者 |
| OperatorIdempotencyRecord | `SYS#IDEM` | `{operationId}#{key}` | 運営者の API の冪等キーの記録（属性は §4.6 の IdempotencyRecord と同じ。運営者は少なく、キーは操作ごとに作る UUID か ULID のため、1つのパーティションにまとめる） | 運営者 |
| AuthSettings | `SYS#AUTH` | `SETTINGS` | トークンの発行時刻の下限（`revokeTokensIssuedBefore`。全体。緊急時の失効） | 運営者 |

計算は、勤務日に有効な法定値の版を使う（`SK` を逆順に読み、施行日が勤務日以前の最初の版）。

### 4.2 テナントのルート `T#{t}`

| entity | SK | 主な属性 | 備考 |
| --- | --- | --- | --- |
| TenantSettings | `TENANT` | TenantSettings の項目（保存期間の延長 `retentionExtensionYears` を含む）、状態（`status`。`active` / `suspended`）、トークンの発行時刻の下限（`revokeTokensIssuedBefore`。テナントの緊急時の失効） | 共通の処理はこの3つの属性だけを読む（[全体構成](architecture.md) §4.3） |
| Department | `DEPT#{departmentId}` | Department | |
| EmploymentType | `ETYPE#{id}` | EmploymentType | |
| WorkRule | `WRULE#{id}` | コード、名前、最新の版の施行日 | 版は別の項目 |
| WorkRuleVersion | `WRULE#{id}#V#{validFrom}` | WorkRuleSettings | 追加し、施行日が締めていない期間にある版は置き換えられる。施行日で探す |
| WorkPattern | `WPAT#{id}` | WorkPattern | |
| Calendar | `CAL#{id}` | Calendar | |
| CalendarYear | `CAL#{id}#Y#{year}` | 例外の日 | 366日分でも約15KB |
| LeaveType | `LTYPE#{id}` | LeaveType | |
| LeavePolicy | `LPOL#{id}` | LeavePolicy | |
| ApprovalRoute | `AROUTE#{id}` | ApprovalRoute | |
| OvertimeAgreement | `OTA#{id}` | OvertimeAgreement | |
| Device | `DEV#{id}` | 名前、設置場所、状態 | 資格情報は認証テーブル |
| UniqueGuard | `UNIQ#{kind}#{value}` | 対象の ID | kind は `EMPCODE`、`EMAIL`、`DEPTCODE`、`ETYPECODE`、`WRULECODE`、`WPATCODE`、`LTYPECODE`、`LPOLCODE`。value は、全角と半角の違いをそろえて大文字にしたもの（大文字と小文字を区別しない） |
| PeriodClose | `CLOSE#{periodId}#{scope}` | 状態（`open` / `closing` / `closed` / `reopening`）、範囲、実行中のジョブ、履歴 | scope は `ALL`、または `D#{departmentId}` |

- マスタの一覧は、`SK` の接頭辞で Query する（件数が少ないため、ページを分けない）。
- 社員番号の UniqueGuard（`UNIQ#EMPCODE#{社員番号}`）は、共有端末で社員番号から従業員を探すのにも使う。
- 社員番号とメールアドレスの UniqueGuard は、退職の処理（UC-BA-11）で消し、再雇用で同じ番号とアドレスを使えるようにする。退職を取り消すときは付け直す（§7.8）。
- 関数はマスタとテナントの設定を60秒まで手元にキャッシュしてよい。勤怠の再計算は、処理の始めに強い整合性で読み直す。

### 4.3 従業員のパーティション `T#{t}#E#{e}`

#### 4.3.1 従業員と条件

| entity | SK | 主な属性 | 索引 |
| --- | --- | --- | --- |
| Employee | `PROFILE` | Employee の項目（入社日と退職日を含む。打刻の API が在籍を確かめる）。今日の時点の条件（部署、雇用区分、就業ルール、付与ルール）の写し、ロールと管理範囲、アカウントの要約（認証テーブルの写し）、PIN の状態の要約 | GSI3（名簿） |
| EmployeeAssignment | `ASG#{validFrom}#{assignmentId}` | EmployeeAssignment | — |

- 名簿（GSI3）: 在籍中（`active`、`onLeaveOfAbsence`）は `GSI3PK = T#{t}#DIR#A`、退職者は `T#{t}#DIR#X`。`GSI3SK = E#{employeeId}`。GSI3 には一覧と絞り込みに要る属性だけを載せる（射影 INCLUDE。§5）。
- 従業員の一覧（listEmployees）と、管理範囲の従業員の列挙（勤怠の一覧、ダッシュボード、締め、出力、定時処理）は、名簿を読んでメモリで絞り込む（部分一致、部署の配下、状態）。名簿の項目は約0.3KB（PROFILE は約0.8KB）で、3,000人で約0.8MB（1ページ）、1万人で約2.8MB（4ページ）。読み直しは 3,000人で 0.16秒、1万人で 0.60秒（計測した p50。PROFILE をすべて載せた場合は 0.48秒と 1.7秒で、時間の多くは受け取った項目の変換の CPU）。関数は名簿を60秒までキャッシュしてよい（当たれば数ms）。
- 将来の日付の条件（`validFrom` が明日以降）は、その日になったら定時処理（UC-BA-11）が PROFILE の写しを更新する。

#### 4.3.2 勤務日ごとの記録

勤務日の入力と結果を `D#{workDate}#` の下にまとめる。日付の範囲を1回の Query で読むと、その範囲の予定・打刻・補正・休暇・結果がそろう。

| entity | SK | 主な属性 | 書く処理 |
| --- | --- | --- | --- |
| DailyPunchState | `D#{date}#STATE` | 当日の状態（`notStarted` / `working` / `onBreak` / `finished`）、最後の打刻、入力の版（`version`） | 打刻、補正、休暇の反映、予定の変更（同期） |
| WorkSchedule | `D#{date}#SCH` | 日の種類、勤務パターン、時間帯、所定、出どころ（`rule` / `shiftPlan` / `manual`）、1か月単位の変形の基準（`baseline`） | シフト表の公開、予定の生成、予定の変更 |
| WorkScheduleRevision | `D#{date}#SCHREV#{revisionId}` | 変更前後、変更事由、基準を変えたか、変更者 | 予定の変更（追加だけ） |
| Punch | `D#{date}#P#{punchId}` | 種類、`occurredAt`、`effectiveAt`、`clientTime`、ずれの印、打刻手段、端末、IP、位置、冪等キー、メモ | 打刻（追加だけ） |
| PunchAdjustment | `D#{date}#ADJ#{adjustmentId}` | 操作（`add` / `void`）、理由、申請の ID、補正した人 | 補正、打刻修正申請の反映（追加だけ） |
| DayLeave | `D#{date}#LV#{requestId}` | 休暇種別、単位、時間帯、量、申請の ID、時季指定か | 休暇の反映と取消 |
| DailyAttendance | `D#{date}#DAY` | 予定の写し、区間、日の結果、休暇、エラー、警告、暫定か、勤務中か（`working`。期限までの勤務中の日は値を持たない）、使ったルールの版、入力の要約（digest。計算の版を含む） | 再計算 |
| DailyAttendanceRevision | `D#{date}#DREV#{revisionId}` | 原因（補正、申請、予定の変更、再オープン）、変更前後の結果、操作者、理由 | 再計算（人の操作が原因のとき） |

- 当日の状態（getMyToday）と打刻は、前日と当日の2日分を1回の Query（`SK BETWEEN 'D#{前日}#' AND 'D#{当日}#~'`）で読む。日をまたぐ勤務の判定に前日が要る。
- 再計算は、週・変形期間・清算期間の範囲を1回の Query で読む（31日分で約250KB）。
- 日別勤怠（DAY）は再計算だけが書く。打刻の API は STATE だけを同期で更新する。
- 締め前のチェックで止まるエラーが確定した DAY は、GSI2 に載せる（§5）。
- まだ何も記録のない日（未来の日など）の DAY は作らない。勤怠の表示は、DAY がない日を予定（SCH）から組み立てる。

#### 4.3.3 期間の集計と締め

| entity | SK | 主な属性 |
| --- | --- | --- |
| PeriodSummary | `PS#{kind}#{periodStart}` | kind は `payroll`（安衛法の時間外・休日労働時間も持つ）、`flexSettlement`、`variablePeriod`、`agreementMonth`。集計（端数処理の前後）、状態（`provisional` / `final`）、件数（未解決のエラー、承認待ちの申請、未対応のアラート） |
| ClosedPeriodSummary | `CS#{periodId}#{closeVersion}` | 締めた時点の写し（給与の締め期間の集計、端数処理の前後、フレックスの清算、休暇の日数）、締めの ID、締めた人と日時。`closeVersion` は3桁（`001` から） |
| PeriodLock | `LOCK#{periodId}` | 締めの ID と版。この項目があれば、その期間は締め済み |

- 締め直すと `closeVersion` が1つ増え、前の版はそのまま残る。月次集計の出力は、指定がなければ最新の版を使う。

#### 4.3.4 休暇

| entity | SK | 主な属性 | 索引 |
| --- | --- | --- | --- |
| LeaveBalance | `LB#{leaveTypeId}` | 残り（`available`）、承認待ちの量（`pending`）、時間単位の年の使用量と上限 | — |
| LeaveGrant | `LG#{leaveTypeId}#{grantDate}#{grantId}` | 付与日、最終の使用可能日、付与量、残り、理由、根拠 | GSI2（残りのあるロット） |
| LeaveLedgerEntry | `LL#{leaveTypeId}#{entryId}` | 種類（`grant` / `use` / `breakDay` / `expire` / `adjust` / `reversal`）、増減、ロット、申請、理由 | — |
| LeaveObligation | `LO#{obligationId}` | 期間、必要な日数、取得済み、状態 | GSI1 |
| LeaveGrantPlan | `LP#{planId}` | 付与日、日数、判定の根拠、状態、理由 | GSI1 |
| StatutoryLeaveEntitlement | `SE#{leaveTypeId}#{fiscalYear}` | 日数、1日の時間数、対象外 | — |
| LeaveOfAbsence | `LOA#{startDate}#{id}` | 休業の種類と期間、休職の出勤率での扱い | — |

- 付与ロットの `SK` は付与日の順に並ぶため、古いロットから使う消化順は Query の順のまま読める。
- LeaveBalance は台帳から導ける値だが、申請の検証で毎回台帳を集計しないよう、台帳を書くトランザクションで同時に更新する。

#### 4.3.5 承認の受信箱と重複防止の印

| entity | SK | 主な属性 |
| --- | --- | --- |
| ApprovalInboxEntry | `INBOX#{requestId}` | 承認者が持つ、承認待ちの申請の要約（RequestSummary の項目） |
| AlertKey | `ALK#{kind}#{periodKey}#{threshold}` | 同じ基準のアラートを1回だけ出すための印 |
| NotificationKey | `NTF#{key}` | 同じ通知を2回送らないための印（TTL 60日） |

### 4.4 申請 `T#{t}#R#{requestId}`

| entity | SK | 主な属性 | 索引 |
| --- | --- | --- | --- |
| Request | `REQ` | 種類、内容、対象日、理由、状態、承認ルートの写し、現在の段、周回、申請時の部署、反映の状態（`applyState`） | GSI1（申請者の一覧）、GSI2（未完了の申請） |
| ApprovalAction | `ACT#{actionId}` | 操作、操作者、段、周回、コメント、日時 | — |

- 申請の詳細は申請の ID だけで読めるよう、申請ごとのパーティションにする。
- 申請者の一覧は GSI1（`GSI1PK = T#{t}#E#{applicantId}#REQ`、`GSI1SK = R#{requestId}`）を新しい順に読む。
- 状態が `pending`、`sentBack`、`cancellationPending` の間だけ、GSI2（`GSI2PK = T#{t}#OPENREQ`、`GSI2SK = {targetFrom}#{requestId}`）に載せる。締め前のチェックとダッシュボードに使う。
- 承認者の一覧（listApprovals）は、承認者のパーティションの `INBOX#` を読む。受信箱は Request の変化から作る写しで、段が進むと入れ替える（§8）。承認の権限は、受信箱ではなく Request の承認ルートの写しで判定する。

### 4.5 シフト表 `T#{t}#SP#{departmentId}#{periodId}`

| entity | SK | 主な属性 |
| --- | --- | --- |
| ShiftPlanHead | `HEAD` | 状態（`draft` / `published`）、版、下書きの版、検証の結果（500件まで）、公開した人と日時、勤務予定への反映の状態 |
| ShiftPlanRow | `ROW#{draftVersion}#{employeeId}` | その従業員の期間のセル（日付、勤務パターン、日の種類） |

- 行を従業員ごとに分け、項目の上限（400KB）にかからないようにする（31日分で1行約2KB）。
- 下書きの保存は、新しい版の行をすべて書いてから、HEAD の版を条件付きで進める。読む側は HEAD の版の行だけを読むため、保存の途中の状態は見えない。古い版の行には TTL（1日）を付けて消す。
- 公開は HEAD を `published` にするだけにし、勤務予定（`D#{date}#SCH`）への反映は Streams から起動する処理が行う（§7.5）。

### 4.6 アラート・ジョブ・取込・冪等キー

| entity | PK | SK | 主な属性 | 索引 | TTL |
| --- | --- | --- | --- | --- | --- |
| Alert | `T#{t}#ALERT` | `A#{alertId}` | 種類、重さ、従業員、部署、対象日、メッセージ、詳細、状態、対応のメモ | GSI2（未対応） | 作成から6年（§13） |
| Job | `T#{t}#JOB#{jobId}` | `JOB` | 種類、状態、進捗、結果（S3 のキー）、エラー、作成者 | GSI1（作成者） | 終了から7日 |
| EmployeeImport | `T#{t}#IMPORT#{importId}` | `IMPORT` | 検証の結果、S3 のキー、有効期限（1時間）、作成者 | — | 作成から1日 |
| IdempotencyRecord | `T#{t}#IDEM#{principal}` | `{operationId}#{key}` | 操作（`operationId`）、キー（`idempotencyKey`）、要求の要約（`digest`。操作・パス・クエリ・本文の SHA-256）、状態（`state`。`inProgress` / `completed`）、処理中の期限（`leaseExpiresAt`）、応答（`response`。`status`、`headers`、`body`、`isBase64Encoded`） | — | 作成から2日（有効なのは24時間） |

`principal` は従業員の ID。共有端末からの要求は `DEV#{deviceId}#{employeeId}`。

### 4.7 項目の例

打刻（Punch）。`ttl` は、給与の締め期間（10月、末日締め）の賃金の支払日（翌月25日）から6年後の 2032-11-25 0:00（日本時間）:

```json
{
  "PK": "T#01J9ZK3M8Q6W2E4R5T7Y9V1H0P#E#01JA0B1C2D3E4F5G6H7J8K9M0N",
  "SK": "D#2026-10-07#P#01JA2X4Y6Z8A0B2C4D6E8F0G2H",
  "entity": "Punch",
  "schemaVersion": 1,
  "punchId": "01JA2X4Y6Z8A0B2C4D6E8F0G2H",
  "type": "clockIn",
  "occurredAt": "2026-10-06T23:58:41.513Z",
  "effectiveAt": "2026-10-06T23:58:00.000Z",
  "clientTime": "2026-10-06T23:58:40.000Z",
  "clientTimeSkewed": false,
  "workDate": "2026-10-07",
  "source": "web",
  "ip": "203.0.113.10",
  "idempotencyKey": "01JA2X4Y5V7W9X1Y3Z5A7B9C1D",
  "createdAt": "2026-10-06T23:58:41.540Z",
  "ttl": 1984921200
}
```

当日の状態（DailyPunchState）:

```json
{
  "PK": "T#01J9ZK3M8Q6W2E4R5T7Y9V1H0P#E#01JA0B1C2D3E4F5G6H7J8K9M0N",
  "SK": "D#2026-10-07#STATE",
  "entity": "DailyPunchState",
  "schemaVersion": 1,
  "status": "working",
  "lastPunch": { "punchId": "01JA2X4Y6Z8A0B2C4D6E8F0G2H", "type": "clockIn", "effectiveAt": "2026-10-06T23:58:00.000Z" },
  "version": 1,
  "ttl": 1984921200
}
```

## 5. 索引（GSI）

GSI1 と GSI2 の射影は ALL（すべての属性）。GSI3 は INCLUDE で、従業員の ID、社員番号、姓、名、姓と名のかな、部署、雇用区分、状態、アカウントの状態、入社日、退職日だけを載せる。属性の名前は `employeeId`、`employeeCode`、`familyName`、`givenName`、`familyNameKana`、`givenNameKana`、`departmentId`、`employmentTypeId`、`status`、`accountStatus`、`hireDate`、`terminationDate`（`PROFILE` に、この名前で書く。かなと退職日は任意）。入社日と退職日は、締めと出力の範囲（期間の中に1日でも在籍した人）を名簿だけで絞るため（射影は後から変えられない）。

| 索引 | 使い方 | PK | SK |
| --- | --- | --- | --- |
| GSI1 | 申請者の申請 | `T#{t}#E#{e}#REQ` | `R#{requestId}` |
| GSI1 | 作成者のジョブ | `T#{t}#E#{e}#JOB` | `J#{jobId}` |
| GSI1 | 有給の付与予定 | `T#{t}#LPLAN` | `{grantDate}#{employeeId}#{planId}` |
| GSI1 | 年5日の取得義務 | `T#{t}#LOBL` | `{status}#{periodEnd}#{employeeId}#{obligationId}` |
| GSI2 | 未完了の申請 | `T#{t}#OPENREQ` | `{targetFrom}#{requestId}` |
| GSI2 | 未解決の勤怠のエラー | `T#{t}#ERR` | `{workDate}#{employeeId}` |
| GSI2 | 未対応のアラート | `T#{t}#ALERTOPEN` | `A#{alertId}` |
| GSI2 | 残りのある付与ロット（失効の待ち行列） | `T#{t}#LEXP` | `{lastUsableDate}#{employeeId}#{grantId}` |
| GSI3 | 名簿（在籍） | `T#{t}#DIR#A` | `E#{employeeId}` |
| GSI3 | 名簿（退職） | `T#{t}#DIR#X` | `E#{employeeId}` |

- GSI1 は一覧、GSI2 は「片づけるべきもの」の待ち行列、GSI3 は名簿。GSI2 は条件を満たす間だけ項目が載る（疎な索引）。
- 朝の打刻の経路（Punch、STATE、DAY の再計算）は、ふつう索引を書かない。DAY が GSI2 に載るのは、締め前のチェックで止まるエラーが確定したときだけ。そのため、索引のパーティションに書き込みが集中しない。
- 索引は結果整合（ふつう1秒未満の遅れ）。一覧は、遅れてもよい用途だけに使う。

## 6. アクセスパターン

API の操作と定時処理が、どの項目をどう読み書きするか。書き込みの詳細は §7。

- 一覧の続き（API の `nextToken`）は、DynamoDB の `LastEvaluatedKey` に、テナント・操作・絞り込みの条件の要約と期限を加えて暗号化した文字列にする。改ざん、別のテナントや別の条件での使い回し、キーの形が外に見えることを防ぐ。一覧の続き用の KMS の鍵（`tokens`）で毎回暗号化し、暗号化コンテキストに、テナント（運営者の API は `ops`）、操作、絞り込みの条件の要約（ハッシュ。CloudTrail に記録されるため値は入れない）を入れる。違うコンテキストでは復号できない。キーポリシーで、テナント用の役割は自分のテナントのコンテキストでしか鍵を使えない。有効期限は1時間。

### 6.1 本人・申請・共有端末

| 操作 | 読む | 書く |
| --- | --- | --- |
| getMe | `PROFILE`、テナントの設定（キャッシュ）、自分の `INBOX#` の件数 | — |
| getMyToday | 前日と当日の `D#` を1回の Query（強い整合性。ほかの端末の打刻の後に取り直す画面のため）、`INBOX#` の件数、テナントの設定（キャッシュ） | — |
| createMyPunch | 前日と当日の `D#`（強い整合性）、`PROFILE`（在籍の確かめ）、テナントの設定（キャッシュ。打刻の規則と保存期間） | §7.2 |
| getMyTimesheet | 期間の `D#` を Query、`PS#payroll#`・`PS#flexSettlement#`、`LOCK#`、最新の `CS#` | — |
| getMyDailyAttendance | その日の `D#{date}#` を Query（ETag は STATE の版） | — |
| listMySchedules | 期間（最大62日）の `D#` を Query し、SCH だけを返す | — |
| getMyLeave | `LB#`、`LG#`（残りのあるもの）、`LO#` を Query、休暇種別（キャッシュ） | — |
| listMyRequests | GSI1 `T#{t}#E#{e}#REQ`（新しい順。状態と種類はフィルター） | — |
| createMyRequest、previewMyRequest | `LB#`・`LG#`、対象期間の `D#`（予定と日の休暇）、GSI1 の自分の未完了の申請、`LOCK#`、承認ルート・部署・名簿（キャッシュ） | §7.3（試算は書かない） |
| setMyKioskPin | 認証テーブル | 認証テーブル、`PROFILE` のアカウントの要約 |
| getRequest | `T#{t}#R#{r}` を Query（REQ と ACT）。参考情報は申請者の `LB#` と対象日の `D#` | — |
| actOnRequest | `REQ`（強い整合性） | §7.3 |
| listApprovals | 自分の `INBOX#` を Query | — |
| createKioskSession | 認証テーブル（端末、PIN）、`UNIQ#EMPCODE#{社員番号}`、従業員の当日の `D#` | 認証テーブル（失敗回数、受付） |
| changeKioskPin | 認証テーブル | 認証テーブル |
| createKioskPunch | 認証テーブル（受付）、当日の `D#` | §7.2（principal は端末と従業員） |

### 6.2 管理

| 操作 | 読む | 書く |
| --- | --- | --- |
| getDashboard | 名簿を管理範囲で絞り、現在の期間の `PS#payroll#` を BatchGetItem（3,000人で30回）、`CLOSE#` | — |
| listTimesheets | 名簿を絞ってページを切り、`PS#payroll#` を BatchGetItem。絞り込み（エラーありなど）は PS の件数で判定 | — |
| getEmployeeTimesheet、getEmployeeDailyAttendance | 本人用と同じ（管理範囲は `PROFILE` の部署で判定） | — |
| createAdjustment | その日の `D#{date}#`、`LOCK#` | §7.4 |
| listAlerts | `T#{t}#ALERT` を新しい順（期間は ULID の範囲）、未対応だけなら GSI2 `T#{t}#ALERTOPEN`。種類と部署はフィルター | — |
| acknowledgeAlert | `A#{alertId}` | 状態を更新し GSI2 から外す、監査 |
| listAuditLogs | 監査テーブル（§10） | — |
| createExport | — | Job、冪等キー、監査（ファイルは S3。§11） |
| listJobs、getJob | GSI1 `T#{t}#E#{e}#JOB`、`T#{t}#JOB#{j}` | — |
| getShiftPlan、exportShiftPlanCsv | `HEAD` を読み、その版の `ROW#{v}#` を Query | — |
| saveShiftPlanDraft、importShiftPlanCsv | 同上、勤務パターン（キャッシュ） | §7.5 |
| publishShiftPlan | `HEAD` | §7.5 |
| changeSchedule | その日の `D#{date}#`、`LOCK#` | §7.5 |
| getPeriodClose | `CLOSE#{p}#…`、GSI2 `T#{t}#ERR`（期間の日付の範囲）と `T#{t}#OPENREQ`（期間にかかる申請）、警告は範囲の DAY | — |
| closePeriod、reopenPeriod | `CLOSE#` | §7.6 |
| listLeaveGrantPlans、decideLeaveGrantPlan | GSI1 `T#{t}#LPLAN`（付与日の範囲）、`LP#` | `LP#`（条件: 状態が `held`）、監査 |
| listLeaveBalances | 名簿を絞ってページを切り、`LB#{leaveTypeId}` を BatchGetItem | — |
| listLeaveObligations | GSI1 `T#{t}#LOBL`（状態の接頭辞） | — |
| getEmployeeLeave | 本人用と同じ | — |
| createLeaveGrant、createLeaveAdjustment | `LB#`、`LG#` | §7.7 |
| createLeaveDesignation | 申請の作成と同じ検証 | 承認済みの Request と ACT（→ 反映。§7.3） |
| putStatutoryEntitlement | — | `SE#` |
| listEmployees、getEmployee | 名簿、`PROFILE` | — |
| createEmployee、updateEmployee、createEmployeeAssignment、terminateEmployee、updateEmployeeRoles | `PROFILE`、`ASG#` | §7.8 |
| アカウントの操作（招待の再送など6つ） | 認証テーブル | 認証テーブル、`PROFILE` のアカウントの要約、監査（認証の処理が行う。§9） |
| previewEmployeeImport、applyEmployeeImport | S3、`IMPORT` | `IMPORT`、Job |
| マスタ（部署、雇用区分、就業ルール、勤務パターン、カレンダー、休暇種別、付与ルール、承認ルート、36協定、共有端末） | `T#{t}` を接頭辞で Query | §7.8 |
| getTenantSettings、updateTenantSettings | `TENANT` | 条件付きの更新、監査 |

### 6.3 定時処理と運営者

| 処理 | 読む | 書く |
| --- | --- | --- |
| UC-BA-01 打刻漏れなどの通知 | 名簿（在籍）→ 前日の `D#` | 再計算（エラーを確定し GSI2 に載せる）、`NTF#` |
| UC-BA-02 週・期間の確定 | 名簿 → 週と期間の `D#` | 再計算 |
| UC-BA-03 有給の付与 | GSI1 `T#{t}#LPLAN`（当日） | §7.7 |
| UC-BA-04 休暇の失効 | GSI2 `T#{t}#LEXP`（前日まで） | §7.7 |
| UC-BA-05 年5日の進捗 | GSI1 `T#{t}#LOBL`（`inProgress`） | `NTF#` |
| UC-BA-06 36協定の判定 | 再計算の中で `PS#agreementMonth#` | Alert、`ALK#` |
| UC-BA-07 安衛法の80時間 | 名簿 → `PS#payroll#` | `NTF#`、Alert |
| UC-BA-08 勤務予定の生成 | 名簿（固定時間制の従業員） | `D#{date}#SCH` |
| UC-BA-09 保存期間を過ぎたデータの削除 | テナントの設定、`CLOSE#` | 予告の通知（削除は TTL。§13） |
| UC-BA-11 従業員の状態の切り替え | 名簿、`ASG#` | `PROFILE`、認証テーブル（退職したアカウントの無効化） |
| 運営者 API | `SYS#TENANTS`、`SYS#STATUTORY`、`SYS#HOLIDAYS`、認証テーブルの `TC#` | 同じ項目と、テナントの初期データ |

定時処理は、`SYS#TENANTS` からテナントを列挙し、テナントごとにテナント用の認証情報（§12）で処理する。

## 7. 書き込みの単位

複数の項目をそろえて変える必要があるときは TransactWriteItems（100項目・4MB まで）を使う。トランザクションの書き込みは、1KB ごとに2単位を消費する。監査ログは、業務の書き込みと同じトランザクションで監査テーブルに書く（トランザクションはテーブルをまたげる）。

条件で書き込みが失敗したときの応答は、次のように決める。

- DynamoDB は「どれかの条件を満たさなかった」ことしか返さない。1つの項目に条件が複数あるとき（例: 承認の版・状態・承認者）は、失敗のときの項目を受け取り（`ReturnValuesOnConditionCheckFailure` を `ALL_OLD`。読み込みの単位は消費しない）、条件を並べた順に見て、その項目が満たさない最初の条件で応答を決める（版なら 412、状態なら 409、承認者なら 403 など）。条件が1つなら、それで決まるため項目を受け取らない。
- トランザクションでも同じ。取消の理由（項目ごと）が条件の失敗なら、その項目の条件で決める。取消の理由が throttle（`ThrottlingError`、`ProvisionedThroughputExceeded`）や同時のトランザクション（`TransactionConflict`）なら 503 を返し、画面が再送する（[API の約束](../api/README.md) §2.10）。
- トランザクションの `ClientRequestToken`（36文字まで、10分間）は使わない。再送は冪等キーの記録（§7.9）で受ける。冪等キーは64文字まであるため、そのままでは入らない。

### 7.1 版と ETag

| リソース | ETag にする版 |
| --- | --- |
| 従業員、マスタ、テナントの設定、カレンダーの年、申請、シフト表 | その項目（`PROFILE`、マスタの項目、`TENANT`、`CAL#…#Y#…`、`REQ`、`HEAD`）の `version` |
| 就業ルール | `WRULE#{id}`（版を加えると進む）の `version` |
| 日別勤怠の詳細 | その日の STATE の `version`（入力の版）。再計算で DAY が変わっても進まないため、補正の画面が再計算のたびに 412 にならない |
| 勤務予定 | `D#{date}#SCH` の `version` |

更新は `version = :If-Match の値` を条件にし、成功したら `version` を1増やす。

### 7.2 打刻

1. 前日と当日の `D#` を強い整合性で読み、勤務日を決める。STATE と打刻の種類で順序を確かめる。
2. TransactWriteItems で次をまとめて書く。
   - IdempotencyRecord を「完了」の状態で Put（`attribute_not_exists`）。応答の本文も入れる。
   - Punch を Put（`attribute_not_exists`）。
   - STATE を Update（条件: 読んだ版と同じ、またはまだない）。状態、最後の打刻、`version` + 1。
   - 終わった期間への打刻（受付期限内の遅れた打刻）だけ、`LOCK#{periodId}` がないことを ConditionCheck する。
3. 冪等キーの条件で失敗したら記録を読み、要求の要約が同じなら保存した応答を返す（違えば 422）。STATE の条件で失敗したら（同時の打刻）、読み直して1回だけやり直す。状態に合わない・受付期限を過ぎた・在籍の外の打刻は、断る前に冪等キーの記録を読み、あれば同じように答える（記録された打刻の再送が、その打刻で変わった状態に断られないように）。
4. 日別勤怠の再計算は Streams から非同期に行う（§8）。応答の当日の状態は、書いた内容からメモリで組み立てる。

書き込みの量は3〜4項目で6〜8単位。

### 7.3 申請・承認・反映

| 操作 | 書く内容 |
| --- | --- |
| 申請 | REQ を Put（`attribute_not_exists`。GSI1・GSI2 のキー付き）、ACT（`submit`）、残りを管理する休暇なら `LB#` の `pending` を加算（条件: `available - pending >= 量`）、冪等キー |
| 途中の段の承認 | REQ を Update（条件: 版が If-Match と同じ、状態が `pending`、呼び出した人が現在の段の承認者）。現在の段 + 1。ACT |
| 最終の承認 | REQ を `approved`、`applyState = pending` にし GSI2 から外す。ACT。対象日の期間ごとに `LOCK#` がないことを ConditionCheck（あれば PERIOD_CLOSED） |
| 差戻し | REQ を `sentBack`。ACT。`pending` を戻す |
| 再申請 | REQ の内容を差し替えて `pending`、周回 + 1。ACT。`pending` を加算し直す（条件付き） |
| 取下げ・却下 | REQ を最終の状態にし GSI2 から外す。ACT。`pending` を戻す |
| 取消の申請 | REQ を `cancellationPending` にし GSI2 に載せる。ACT |
| 取消の承認・管理者の取消 | REQ を `cancelled`、`applyState = reverting` にし GSI2 から外す。ACT |

反映（承認の後）と戻し（取消の後）は、REQ の変化を受けた Streams の処理が行う。`applyState` を条件にして、二重に反映しない。

- **休暇の反映**: 1つのトランザクションで、使うロットの残りを減らし（古い順。条件: 残り ≥ 使う量）、台帳に `use`（必要なら `breakDay`）を追記し、`LB#` の `available` と `pending` を減らし、対象日ごとに DayLeave を Put して STATE の版を進め、REQ を `applyState = applied` にする。31日分で70項目ほど。それより長い申請は、30日ずつに分けて進み具合を REQ に記録する。
- **打刻修正の反映**: PunchAdjustment（申請の ID 付き）を Put し、STATE の版を進め、REQ を `applied` にする。
- **休暇の戻し**: 台帳に `reversal` を追記してロットの残りを戻し（失効済みのロットは、戻した直後に同じ量を `expire`）、`LB#` を戻し、DayLeave を消し、STATE の版を進め、REQ を `reverted` にする。
- **打刻修正の戻し**: 元の補正を打ち消す PunchAdjustment を Put する。

受信箱（`INBOX#`）は、REQ の変化を受けた Streams の処理が、現在の段の承認者ごとに作り、段が進むか申請が終わると消す。

### 7.4 勤怠の補正

TransactWriteItems で、PunchAdjustment を Put、STATE を Update（条件: 版が If-Match と同じ。`version` + 1）、`LOCK#` がないことを ConditionCheck、監査ログと冪等キーを書く。変更前後の結果は、再計算が DailyAttendanceRevision に残す。

### 7.5 勤務予定

| 操作 | 書く内容 |
| --- | --- |
| シフト表の下書きの保存 | 新しい版の行を BatchWriteItem で書き、HEAD を Update（条件: 版が If-Match と同じ）。古い版の行に TTL（1日）を付ける |
| シフト表の公開 | HEAD を `published` に Update（条件: 版が If-Match と同じ、検証のエラーがない）、反映の状態を `pending` にする |
| 公開の反映（Streams） | セルごとに SCH を Put（締め済みの日は飛ばす）、STATE の版を進め、1か月単位の変形で変形期間が始まる前なら `baseline` も写す。終わったら HEAD の反映の状態を `done` にし、本人に知らせる |
| 予定の変更 | SCH を Update（条件: 版が If-Match と同じ）、SCHREV を Put、STATE の版を進め、`LOCK#` がないことを ConditionCheck。`baseline` は、就業規則に定める変更事由にもとづき対象日より前に行った変更のときだけ変える |
| 予定の生成（UC-BA-08） | SCH を Put（条件: まだないか、出どころが `rule`。締め済みの日は飛ばす） |

### 7.6 締めと再オープン

- **締め（API）**: 期間の末日の打刻の受付期限を過ぎていることを確かめ（`CLOSE_TOO_EARLY`）、`CLOSE#` を `closing` にし（条件: `open` かまだない）、Job と冪等キーを書く。
- **締め（ジョブ）**:
  1. 範囲の従業員を決める。期間の中に1日でも在籍した人で、部署は期間の末日（それより前に退職した人は退職日）の所属（`ASG#` と退職日で判定。名簿は在籍の人と、期間の中に退職した人を読む）。全社の締めは、まだ `LOCK#` のない人だけ。
  2. 従業員ごとに `LOCK#{periodId}` を Put する（条件: ない）。これより後、その期間の入力は変えられない。
  3. 締め前のチェックをやり直す。受付期限を過ぎた勤務中の日も、退勤なしのエラーとして数える。エラーか申請が残っていたら、このジョブが書いた `LOCK#` を消して `CLOSE#` を `open` に戻し、ジョブを失敗（CLOSE_PRECHECK_FAILED）にする。
  4. 従業員ごとに期間を確定させ（フレックスの清算、変形期間の時間外）、`PS#` を `final` にし、`CS#{periodId}#{前の版 + 1}` を書き、`LOCK#` に版を記録する。
  5. `CLOSE#` を `closed` にし、履歴を加える。
- **再オープン**: `CLOSE#` を `reopening` にし、ジョブが範囲の従業員の `LOCK#` を消して `PS#` を `provisional` に戻す。`CS#` は残す。終わったら `CLOSE#` を `open` にし、履歴を加える。全社で締めた期間も、部署の範囲で再オープンできる（その部署の人の `LOCK#` だけ消す）。
- 範囲の状態（`open` / `partiallyClosed` / `closed`）は、範囲の従業員の `LOCK#` から求める（すべてあれば `closed`、一部なら `partiallyClosed`）。`CLOSE#` は実行中のジョブと履歴を持つ。

### 7.7 休暇の付与・調整・失効

| 操作 | 書く内容 |
| --- | --- |
| 付与（定時処理・手動） | LeaveGrant を Put（GSI2 の失効の待ち行列に載せる）、台帳に `grant`、`LB#` の `available` を加算、付与予定（`LP#`）を `granted` に、冪等キー（手動のとき） |
| 調整 | 対象のロットの残りを Update（条件: 残り + 増減 ≥ 0）、台帳に `adjust`、`LB#` を Update（条件: `available` + 増減 ≥ 0）、監査ログ |
| 失効（UC-BA-04） | ロットの残りを 0 にして GSI2 から外し、台帳に `expire`、`LB#` の `available` を減算 |

### 7.8 従業員とマスタ

| 操作 | 書く内容 |
| --- | --- |
| 従業員の登録 | `PROFILE`（名簿に載せる）、`ASG#`、`UNIQ#EMPCODE#…`、メールがあれば `UNIQ#EMAIL#…`（どれも `attribute_not_exists`）、監査ログ、冪等キー。続けて認証の処理がアカウントを作る（§7.9、§9） |
| テナントの開設（UC-OP-01） | (1) 認証テーブルの `TC#` を「開設中」で作る（`attribute_not_exists`。テナントと最初の管理者の ID、要求の要約を持つ。同じ要求のやり直しなら、記録した ID で続ける） (2) テナント用の役割で、テナントの設定（`TENANT`）、既定のデータ（部署、雇用区分、勤務パターン、就業ルールの見本と版、カレンダーと開設の年と翌年の祝日、既定の付与ルール、法定の休暇種別、既定の承認ルートと、それぞれのコードの `UNIQ#…`。それぞれを作る機能の作業で足す。祝日の一覧がまだない年は祝日なしで作る）、最初の管理者の `PROFILE`（既定のデータの条件の写しを持ち、名簿に載せる）と `ASG#`、`UNIQ#EMPCODE#…`、`UNIQ#EMAIL#…` を1つのトランザクションで作る（30項目ほど。すでにあれば作り済みとして続ける） (3) 認証のアカウントの作成の関数を呼ぶ（§9） (4) `TC#` を `active` にし（条件: 開設中で同じテナント）、`SYS#TENANTS` の台帳と運営者の監査ログを書く。冪等キーは処理中の記録（§7.9）。(4) より前に止まっても、企業コードは同じ要求でしかやり直せないため、途中の項目がほかのテナントと混ざらない。途中の失敗で 503 を返すときは、処理中の冪等キーの記録を消してから返す（5秒後の再送が、30秒の処理中の期限で 409 にならないように）。最初の管理者の入社日は、指定がなければ開設の日（テナントのタイムゾーン） |
| 社員番号・メールアドレスの変更 | 古い UniqueGuard を Delete、新しいものを Put（条件: ない）、`PROFILE` を Update（条件: 版） |
| 条件の変更 | `ASG#` を Put、適用日が今日以前なら `PROFILE` の写しを更新、適用日からの再計算のジョブを書く |
| 退職 | `PROFILE` に退職日を記録（条件: 版）。退職日を過ぎたら UC-BA-11 が状態を `terminated` にし、名簿を `DIR#X` に移し、アカウントを無効にし（退職で無効にしたことを `disabledBy: termination` で残す）、社員番号とメールアドレスの UniqueGuard と認証テーブルの `LOGIN#` を消し、保存期間の TTL を付ける |
| 退職日の変更と退職の取消 | 処理の前: `PROFILE` の退職日を変えるか消す（条件: 版。前と新しい退職日が入社日以降で、締めていない期間の日）。処理の後の取消（認証の処理）: 状態を `active`（休業の期間なら `onLeaveOfAbsence`）に戻し、名簿を戻し、保存期間の TTL を外し、UniqueGuard と `LOGIN#` を付け直し（条件: ない。あれば `EMPLOYEE_CODE_DUPLICATED`、`EMAIL_DUPLICATED`）、`disabledBy: termination` のアカウントを有効に戻す。どれも1つのトランザクションで書き、監査ログに残す |
| マスタの登録・変更 | 登録は Put（`attribute_not_exists`）とコードの UniqueGuard、変更は Update（条件: 版）。監査ログ |
| 就業ルールの新しい版 | `WRULE#{id}#V#{validFrom}` を Put（`attribute_not_exists`）、`WRULE#{id}` の版を進め、施行日からの再計算のジョブを書く |
| 就業ルールの版の置き換え | 施行日が締めていない期間にあることを確かめ、`WRULE#{id}#V#{validFrom}` を Put（条件: ある）、`WRULE#{id}` の版を進め、施行日からの再計算のジョブを書く。監査ログに変更前後を残す |
| 給与の締め日の変更 | `TENANT` を更新する（条件: 版と、最初の締めの日時 `firstClosedAt` がないこと。最初の締めのときに締めの処理が書く）。締めていない期間の集計をやり直すジョブを書く |
| 従業員の削除（UC-SE-15） | 認証の処理が、従業員のパーティションに `PROFILE` と `ASG#` のほかの項目がないこと、申請の索引（GSI1 の `T#{t}#E#{e}#REQ`）が空なこと、ログインしたことがないこと、ロールがなく、部署の所属長と承認ルートの承認者に指定されていないことを確かめ、`PROFILE`（条件: 版）、`ASG#`、`UNIQ#EMPCODE#…`、`UNIQ#EMAIL#…`、認証テーブルの `ACC#` と `LOGIN#` を1つのトランザクションで消し、監査ログを書く。招待の `INV#` は期限で消え、使われてもアカウントがないため受け付けない |
| 入社日の訂正（UC-SE-16） | 旧い入社日と新しい入社日の間の `D#`、休暇の付与、申請の索引に記録がなく、その間が締め済みの期間にかからないことを確かめ、`PROFILE` を更新し（条件: 版）、最初の条件の `ASG#` を新しい適用日のキーに付け替える（Delete と Put） |

### 7.9 冪等キー

- 1つのトランザクションで終わる操作（打刻、申請、補正、付与、調整、時季指定、出力・締めの受付、マスタの登録）は、IdempotencyRecord を「完了」の状態で同じトランザクションに入れる。
- いくつかの段階に分かれる操作（従業員の登録とアカウントの作成）は、先に「処理中」で書き（`attribute_not_exists`、処理中の期限は30秒）、終わったら「完了」にして応答を保存する。処理中の再送には 409（`IDEMPOTENCY_KEY_IN_PROGRESS`）を返し、期限を過ぎた「処理中」の記録は最初からやり直す。
- 有効なのは24時間。TTL の削除は遅れることがあるため、作成日時で判定する。
- 一度だけ返す秘密（初期 PIN、共有端末の登録コード）は、記録の応答に入れない。業務テーブル・Streams・バックアップに平文で残さないため。再送には、その項目を null にした応答を返す。

## 8. 再計算とイベント（Streams）

業務テーブルの Streams（新旧の両方の値）から、次の処理を起動する。

| 変化 | 処理 |
| --- | --- |
| Punch・PunchAdjustment の追加、DayLeave の追加と削除、WorkSchedule の追加と変更、EmployeeAssignment の追加と変更、LeaveOfAbsence の追加・変更・削除 | 日別勤怠の再計算（その従業員の、影響する勤務日・週・期間） |
| DailyAttendance の変更（再計算の中で続けて行う） | 期間の集計（PS）の更新、36協定の判定（UC-BA-06）、GSI2 の付け外し |
| Employee の退職日の追加・変更・取消 | 前と新しい退職日を含む締めていない期間の集計の再計算（変形・フレックスの総枠を在籍した日で按分する） |
| Request の追加と変更 | 受信箱の入れ替え、通知、休暇・打刻修正の反映と戻し（§7.3）、PS の件数 |
| ShiftPlanHead が `published` になった | 勤務予定への反映、本人への通知（§7.5） |
| LeaveGrant の追加、Alert の追加と変更 | 通知、PS の件数 |
| Job の追加 | ジョブの実行（出力、取込の反映、締め、再オープン、再計算） |
| TTL による削除 | 無視する（レコードの `userIdentity` が DynamoDB のサービス） |

- 処理は冪等にする。再計算は入力から結果を作り直すため、何度動いても結果は同じ。反映と戻しは REQ の `applyState` を条件にする。
- 同じ従業員のイベントは、受け取ったまとまりの中で1回の再計算にまとめる。
- 再計算は強い整合性で読み、DAY の `version` を条件に書く（ほかの再計算と競合したら読み直す）。入力の要約（digest）が変わらなければ書かない。
- 人の操作が原因の再計算（補正、申請の反映、予定の変更、再オープンの後の変更）では、DailyAttendanceRevision に変更前後を残す。
- `entity` でイベントを振り分ける（イベントソースのフィルター）。Streams は2つの関数（再計算／それ以外のイベント）で読む（[全体構成](architecture.md) §5.1）。U-04 の再計算は `Punch` の追加だけを受け取り、ほかの入力は F2-03 で足す（[実装計画と進捗](../plan.md)）。
- 朝の打刻の集中時は、再計算が数秒遅れてもよい（当日の表示は STATE で返す）。

## 9. 認証テーブル（auth）

| entity | PK | SK | 主な属性 | TTL |
| --- | --- | --- | --- | --- |
| TenantCodeIndex | `TC#{企業コード（大文字）}` | `TC` | テナントの ID、状態（`opening`（開設中）/ `active`）、開設中は最初の管理者の ID と要求の要約（`openingDigest`。§7.8） | — |
| LoginIndex | `LOGIN#{t}#{loginId}` | `LOGIN` | 従業員の ID（loginId は、メールか社員番号を、全角と半角の違いをそろえて大文字にしたもの。大文字と小文字を区別しない） | — |
| UserAccount | `ACC#{t}#{e}` | `ACCOUNT` | ログインID、パスワードのハッシュ（PHC 形式）、変更日時、初回の変更が必要か、状態（`invited` / `active` / `locked`（連続100回の失敗。管理者が解除する） / `disabled`）、ログインの失敗回数（パスワードと TOTP の誤りを合わせて数える。照合の前に条件付きの加算で1つ増やし、正しければ戻す）とロックの期限、ロック中の試行を監査ログに残したロック（ロックごとに最初の1件だけ残す）、TOTP（秘密情報の暗号文、登録日時、最後に使ったステップ）、登録中の TOTP（秘密情報の暗号文と期限10分）、PIN（鍵付きハッシュ、初期 PIN か、失敗回数とロックの期限）、全セッションを失効させた日時、現在の招待の ID、`version` | 従業員と同じ（§13） |
| RefreshTokenFamily | `ACC#{t}#{e}` | `RTF#{familyId}` | 現在のトークンのハッシュ、世代、絶対期限、アイドル期限、状態（`sessionStatus`。`active` / `revoked`。アカウントの `status` と名前を分ける）、失効の理由、管理機能の再認証の時刻（`reauthenticatedAt`）と最後の管理操作の時刻（`lastAdminActivityAt`） | 絶対期限 + 7日 |
| PasswordResetToken | `PRT#{ハッシュ}` | `PRT` | テナント、従業員、期限（30分）、使った日時 | 期限 + 1日 |
| InvitationToken | `INV#{ハッシュ}` | `INV` | テナント、従業員、招待の ID、期限（72時間）、使った日時 | 期限 + 1日 |
| MfaChallenge | `MFA#{ハッシュ}` | `MFA` | テナント、従業員、期限（5分）、試行回数（5回まで） | 期限 + 1日 |
| PasswordChangeChallenge | `PWC#{ハッシュ}` | `PWC` | テナント、従業員、期限 | 期限 + 1日 |
| DeviceCredential | `DEV#{deviceId}` | `CRED` | テナント、資格情報のハッシュ、状態、最終利用日時 | — |
| DevicePinThrottle | `DEV#{deviceId}` | `PINFAIL` | 端末単位の PIN の失敗回数、ロックの期限 | 期限 + 1日 |
| DeviceRegistrationCode | `DREG#{ハッシュ}` | `DREG` | テナント、端末、期限（10分） | 期限 + 1日 |
| KioskSession | `KS#{ハッシュ}` | `KS` | テナント、端末、従業員、期限（60秒） | 期限 + 1日 |
| SigningKey | `SIGNKEY` | `{kid}` | 世代、KMS の鍵の ARN、公開鍵（JWK の `n` と `e`）、状態（`next` / `active` / `retiring` / `retired`）、公開・使用の開始・退役の開始・退役の日時 | — |
| OutboxMail | `OUTBOX` | `{送った時刻}#{mailId}` | 宛先、件名、本文（メールの送り方が `outbox` の環境だけ） | 作成 + 1日 |

- トークンや資格情報は、乱数の値を発行し、SHA-256 のハッシュだけを保存する。リフレッシュトークンの値には、系列を探すためのテナント・従業員・系列の ID と、256ビットの乱数を入れる。
- PIN は桁数が少なく、ハッシュだけでは総当たりを防げないため、KMS の HMAC の鍵（外に出ない）で鍵付きハッシュにし、失敗回数で止める。TOTP の秘密情報は、KMS の `auth` の鍵で暗号化して保存する（暗号化コンテキストにテナントと従業員を入れる）。
- 期限はアプリが `expiresAt` で判定する（TTL の削除は遅れるため）。
- 認証の状態を変えるとき（招待の受諾、ロック、無効化、TOTP の登録など）は、業務テーブルの `PROFILE` のアカウントの要約も同じトランザクションで更新する。要約は `PROFILE` の `account`（API の AccountSummary）と、名簿に載せる `accountStatus`。状態を変えるときは `version` を1増やす（従業員の更新と競合させるため）。最終ログインの時刻（`account.lastLoginAt`）だけは版を進めない。
- アカウントの操作の API（招待の再送、初期パスワード、ロックの解除、TOTP の解除、PIN のリセット、無効化）、従業員の削除、本人の PIN の設定、共有端末の登録コードの発行と端末の失効は、認証テーブルを書くため、認証の処理が受け持つ。従業員の登録に続くアカウントの作成も、認証の処理を同期で呼んで行う（初期 PIN を応答で返すため）。これらの操作の関数は Auth のプロジェクトにまとめる（[全体構成](architecture.md) §4.2）。
- アカウントの作成は、認証の内部の関数（`tna-{env}-provision-account`）の同期の呼び出し（Lambda の Invoke）で行う。テナントの開設（運営者）も同じ関数を使う。関数は、アカウントがなければ `ACC#`・`LOGIN#`・`INV#` を作り、招待中ならば新しい招待を作り直して（古い招待は無効になる）メールを送る。どちらも `PROFILE` の要約と監査ログを同じトランザクションで書く。
- 署名鍵の状態（`SIGNKEY`）は、鍵の管理の関数（`tna-{env}-signing-keys`）だけが書く。ほかの認証の関数は、使用中の鍵を読んで5分キャッシュする。
- 業務の関数は、共有端末の受付（`KS#`）を読むことと、自分のテナントのセッション（`RTF#`）の最後の管理操作の時刻を更新することだけを許す（共有端末の打刻と、管理機能の無操作の判定のため）。更新は IAM で、キー（`ACC#{自分のテナント}#…`）と属性（`sessionStatus`、`reauthenticatedAt`、`lastAdminActivityAt`）に限る。同じパーティションのアカウントには、これらの属性がないため、業務の関数はアカウントの状態を変えられない。

## 10. 監査テーブル（audit）

| 項目 | 内容 |
| --- | --- |
| キー | `PK = T#{t}#AUD#{YYYY-MM}`（操作の月）、`SK = {occurredAt}#{auditId}`。テナントのない運営者の操作は `OPS#AUD#{YYYY-MM}` |
| GSI1（操作者） | `GSI1PK = T#{t}#ACT#{actorId}`、`GSI1SK = {occurredAt}#{auditId}` |
| GSI2（対象の従業員） | `GSI2PK = T#{t}#TGT#{employeeId}`、`GSI2SK = {occurredAt}#{auditId}` |
| 主な属性 | 操作（`action`。例 `attendance.adjust`）、操作者（`actor`。種類 `type`（`user`・`system`・`operator`・`device`）、従業員、その時点の氏名、端末、運営者の IAM の主体）、対象（`target`。種類、ID、従業員、その時点の部署）、変更前後（`before`・`after`。変えた項目の JSON の文字列。合わせて100KBまで。超えたら両方を省いて `changesOmitted` を付ける）、要約、理由、相関 ID（`correlationId`。API Gateway の要求の ID）、IP、ユーザーエージェント、出どころ（`source`。`api`・`batch`・`ops`）、`ttl`（操作日から5年＋1年＋テナントの延長）。月はサービスのタイムゾーン（Asia/Tokyo）で決める |
| 索引の射影 | GSI1 と GSI2 は INCLUDE で、一覧に要る `auditId`、`occurredAt`、`action`、`actor`、`target`、`summary`、`reason`、`source` だけを載せる。変更前後（最大100KB）は、詳細を開いたときに索引のキーから元の項目を読む。ALL では、変更前後を2つの索引にも写すため、書き込みと保存が最大で3倍になる |
| 検索（listAuditLogs） | 対象の従業員の指定があれば GSI2、操作者の指定があれば GSI1、なければ月のパーティションを新しい月から読む。操作の種類はフィルター。勤怠管理者には、対象の部署が管理範囲のものだけを返す |
| 追記だけにする | 業務と認証の関数の役割には PutItem だけを許す（読めない）。検索の役割には Query と GetItem だけを許す。更新と削除は、保存期間の処理の役割（`ttl` の付け替え）と緊急用の役割だけに許す。キーには ULID を含むため、読めない役割が既存の項目のキーを知ることはできない |
| 書き方 | 業務の操作と同じトランザクションで書く（操作と監査ログがずれない） |

ログインの成否などの認証の出来事は、認証の処理が書く。

## 11. S3 のファイル

| プレフィックス | 中身 | 削除 |
| --- | --- | --- |
| `exports/{tenantId}/{jobId}/` | 出力した CSV | 7日（ライフサイクル） |
| `imports/{tenantId}/{importId}/` | 取り込んだ CSV（`source.csv`）と、検証済みの行（`parsed.json`） | 1日（ライフサイクル） |

- バケットは非公開にし、TLS の通信だけを許す。暗号化は SSE-KMS（`files` の鍵）とバケットキー（KMS の要求を減らす）。
- 関数は、テナント用の認証情報（§12）で、自分のテナントのプレフィックスだけを読み書きできる。
- ダウンロードは署名付き URL（10分）で渡す。
- 公開鍵（JWKS）は、テナントのデータを置かない別の公開用バケットに置く。discovery は置かず、CloudFront Functions が返す。
- アクセスログのバケットには、各バケットの S3 のアクセスログ（バケットの名前の接頭辞）と、CloudFront のアクセスログ（`cloudfront/`）を置き、90日で消す。CloudFront のログは CloudWatch Logs の配信の仕組みが書くため、バケットのポリシーで `delivery.logs.amazonaws.com` に `cloudfront/` への書き込みを許す。

## 12. テナントの分離と権限

### 12.1 テナント用の認証情報

業務の関数（API、Streams の処理、定時処理）は、JWT で確かめたテナントの ID をセッションタグ（`tenantId`）に付けて「テナント用の役割」を引き受け（AssumeRole）、その一時的な認証情報で DynamoDB と S3 を呼ぶ。認証情報はテナントごとに関数の中でキャッシュする（最長1時間）。

テナント用の役割の業務テーブルへのポリシー（要点）:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "TenantItems",
      "Effect": "Allow",
      "Action": [
        "dynamodb:GetItem", "dynamodb:BatchGetItem", "dynamodb:Query",
        "dynamodb:PutItem", "dynamodb:UpdateItem", "dynamodb:DeleteItem",
        "dynamodb:BatchWriteItem", "dynamodb:ConditionCheckItem"
      ],
      "Resource": [
        "arn:aws:dynamodb:ap-northeast-1:{account}:table/{main}",
        "arn:aws:dynamodb:ap-northeast-1:{account}:table/{main}/index/*"
      ],
      "Condition": {
        "ForAllValues:StringLike": {
          "dynamodb:LeadingKeys": [
            "T#${aws:PrincipalTag/tenantId}",
            "T#${aws:PrincipalTag/tenantId}#*"
          ]
        }
      }
    },
    {
      "Sid": "ServiceWideReadOnly",
      "Effect": "Allow",
      "Action": ["dynamodb:GetItem", "dynamodb:Query"],
      "Resource": "arn:aws:dynamodb:ap-northeast-1:{account}:table/{main}",
      "Condition": {
        "ForAllValues:StringEquals": {
          "dynamodb:LeadingKeys": ["SYS#STATUTORY", "SYS#HOLIDAYS", "SYS#AUTH"]
        }
      }
    }
  ]
}
```

- Scan、PartiQL、ストリームの直接の読み取りは許さない。`ForAllValues` の条件は、キーを持たない要求（Scan など）で真になるため。
- 条件は、索引への Query では索引のパーティションキーに効く。TransactWriteItems と BatchGetItem は、ほかのテナントのキーが1つでも混ざると全体が拒否される。セッションタグのない引き受けは断る。
- 監査テーブルには、`T#${aws:PrincipalTag/tenantId}#AUD#` で始まるキーへの PutItem だけを許す。S3 は `exports/${aws:PrincipalTag/tenantId}/*` などの自分のプレフィックスだけを許す。
- 認証テーブルには、共有端末の受付（`KS#`）の GetItem と、自分のテナントのセッションの最後の管理操作の時刻の UpdateItem（キーは `ACC#${aws:PrincipalTag/tenantId}#*`、属性は `PK`・`SK`・`sessionStatus`・`reauthenticatedAt`・`lastAdminActivityAt`、戻り値なし）だけを許す。

### 12.2 役割

| 役割 | 使う処理 | できること |
| --- | --- | --- |
| テナント用 | 業務の API、Streams の処理、定時処理のテナントごとの処理 | 自分のテナントの項目の読み書き（§12.1）、サービス全体の設定（法定値、祝日、認証の設定）の読み取り、監査ログの追記、共有端末の受付の読み取り、自分のテナントのセッションの最後の管理操作の時刻の更新 |
| 認証 | 認証の API、アカウントの操作 | 認証テーブルの読み書き、業務テーブルの `PROFILE` の読み取りとアカウントの要約の更新（`dynamodb:Attributes` で属性を限る。更新の戻り値は `NONE` か更新した属性だけにし、全属性を読めないようにする。`dynamodb:ReturnValues`。この条件キーは、条件が外れたときに返す項目（`ReturnValuesOnConditionCheckFailure`）も止めるため、この更新の条件は1つにする）、テナントと認証の設定の状態の読み取り（共通の処理。`status`・`revokeTokensIssuedBefore`・`retentionExtensionYears` に限る）、監査ログの追記、公開鍵のバケットの JWKS の書き込み（鍵の管理）、SES の送信（送り方が `ses` の環境だけ） |
| 定時処理の起動 | テナントの列挙 | `SYS#TENANTS` の読み取りと、テナント用の役割の引き受け |
| 運営者 | 運営者の API | `SYS#` の項目と認証テーブルの `TC#` の読み書き、テナントの初期データの作成（テナント用の役割）、アカウントの作成の関数の呼び出し |
| 監査ログの検索 | listAuditLogs | 監査テーブルの Query と GetItem（テナントの接頭辞で限る） |
| 保存期間の処理 | UC-BA-09 | `ttl` の付け替え（UpdateItem） |
| 移行 | スキーマの一括の変更（§16） | Scan を含む読み書き。普段は使わない |
| 緊急用 | 障害対応 | すべて。使ったら記録して確認する |

- 移行と緊急用を除くすべての役割（実行の役割とテナント用の役割）に、`dynamodb:Scan` と PartiQL（`dynamodb:PartiQL*`）の明示的な拒否を付ける。許可を付け間違えても、テーブル全体を読めないようにするため。
- 実行の役割は、関数のプロジェクトごとに作る（`tna-{env}-fn-{プロジェクト}`。`auth`、`punch`、`self`、`admin-ops`、`admin-settings`、`ops`、`async`。[全体構成](architecture.md) §4.2）。テナント用の役割（`tna-{env}-tenant`）は、認証を除くプロジェクトの実行の役割が、`tenantId` のセッションタグ（このタグだけ）を付けて引き受ける。運営者は、新しいテナントの初期データを作るときに使う。
- 実行の役割の土台は、ログ、トレース、テナント用の役割の引き受け、認証テーブル（認証）、`SYS#` と `TC#`（運営者）、ストリームの読み取り（非同期）。定時処理の起動・監査ログの検索・保存期間の処理・移行の役割は、その機能と一緒に足す。緊急用は IAM Identity Center の権限セット（Emergency）。
- 共通の処理の権限: テナント用の役割の `SYS#AUTH` の読み取りとセッションの更新、認証の役割のテナントの状態の読み取り、CloudFront の後ろの API の実行の役割（認証、打刻、本人、管理の2つ）の合言葉の読み取り（[全体構成](architecture.md) §3.3）。合言葉は Secrets Manager に置き、読み取りは `secretsmanager:GetSecretValue`（`tna/{env}/origin-verify`）にする。

### 12.3 暗号化の鍵

| 鍵 | 対象 | 使える役割 |
| --- | --- | --- |
| `main` | 業務テーブル | テナント用、認証（PROFILE の読み書き）、運営者、非同期の処理（ストリームの読み取り）。保存期間の処理と移行は、その機能と一緒に足す |
| `auth` | 認証テーブル、TOTP の秘密情報 | 認証（TOTP は直接の暗号化）、運営者（`TC#`）、テナント用（`KS#` の読み取りと、セッションの最後の管理操作の時刻の更新） |
| `audit` | 監査テーブル | 監査ログを書く役割（テナント用、認証、運営者）。検索の役割と保存期間の処理は、その機能と一緒に足す |
| `files` | S3 のファイル | テナント用 |
| `hmac` | PIN の鍵付きハッシュ（§9） | 認証（GenerateMac、VerifyMac） |
| `tokens` | 一覧の続き（`nextToken`。§6） | テナント用（暗号化コンテキストが自分のテナントのときだけ）、運営者（運営者の範囲だけ） |
| 署名（世代ごと） | JWT の署名 | 認証（Sign、GetPublicKey）。世代は環境ごとの設定に書き、世代ごとに鍵を作る |

- 鍵の利用は、キーポリシーだけで許す（役割の IAM のポリシーには KMS の権限を書かない）。DynamoDB と S3 を通した利用（`kms:ViaService`）と直接の利用を、役割の ARN で絞る。DynamoDB の権限があっても、鍵の権限がなければ読めない。管理（エイリアス、ローテーション、ポリシーなど）は IAM に任せるが、利用と権限の付与（`CreateGrant`）は含めない。テーブルを作る CloudFormation（CDK の実行の役割）には、DynamoDB を通した利用を許す。
- 人は、緊急用の権限セット（Emergency）だけが DynamoDB と S3 を通して鍵を使える。開発だけは、試験用のデータをコンソールで見るため、管理者と開発者（Admin、Developer）にも許す。
- 自動ローテーション（1年）を有効にする（対称鍵だけ。HMAC の鍵は、保存したハッシュが鍵に依存するため回さない）。検証と本番では、鍵の無効化と削除の予約を緊急用の権限セットだけに許す（キーポリシーで拒否する）。鍵を無効にすると、7日でテーブルが使えなくなるため。権限の付与の取り消し（`kms:RevokeGrant`。テーブルの付与を消すと読めなくなる）も同じく緊急用だけにし、キーポリシーの書き換え（`kms:PutKeyPolicy`）は緊急用と CDK の実行の役割だけに許す（ほかの管理者が拒否を外せないように）。開発の鍵はスタックと一緒に消える（削除の待ち期間は7日）。
- 鍵の利用は CloudTrail で監査する。

## 13. 保存期間と削除

### 13.1 削除する時点

保存期間は、起算日から5年に猶予の1年を加え、テナントの延長年数を足す。

| データ | 起算日 | `ttl` を付けるとき |
| --- | --- | --- |
| 勤怠の記録（`D#` の項目、`PS#`、`CS#`、`LOCK#`、シフト表） | 勤務日が属する給与の締め期間の、締め日と賃金の支払日の遅い方 | 書くとき（テナントの設定から計算する） |
| 有給の付与ロットと台帳 | ロットの最終の使用可能日 | 付与のとき（台帳はロットに合わせる） |
| 申請と承認の履歴 | 最終の状態になった日 | 最終の状態になったとき（承認の後の取消で付け替える） |
| 従業員（`PROFILE`、`ASG#`、`LB#`、`LO#`、`LP#`、`SE#`）とアカウント | 退職日が属する給与の締め期間の、締め日と賃金の支払日の遅い方（勤怠の記録より先に消えないように） | 退職を処理したとき（UC-BA-11）。退職を取り消したら外す |
| アラート | 作成日 | 書くとき |
| 監査ログ | 操作日 | 書くとき |
| ジョブ、取込、冪等キー、通知の印、シフト表の古い版の行 | — | 7日、1日、2日、60日、1日 |
| マスタ | — | 削除しない（無効化で扱う。テナントの解約時の削除は P2） |

- テナントが延長年数を変えたら、保存期間の処理（UC-BA-09）が、そのテナントの項目の `ttl` を付け替える。
- UC-BA-09 は、翌月に削除の時点を迎える期間を求めて、30日前にテナント管理者に知らせる。削除そのものは TTL に任せる（書き込みの費用がかからない）。
- TTL の削除は数日遅れることがある。期限を過ぎた項目は、画面と出力から除く。
- 削除したデータは PITR で35日間は戻せる。それを過ぎると戻せない。

### 13.2 テーブルクラス

- 毎月、テーブルごとに保存（ストレージ）の費用と読み書きの費用を比べ、保存が読み書きの半分を超えたら Standard-IA に切り替える。切り替えは無停止で、30日に2回まで。データは移さない（UC-BA-10 はこの確認にする）。
- 業務テーブルは、従業員1万人で開始から1年半ほどで条件を満たす見込み（§15）。監査テーブルは作成時から Standard-IA にする。
- PITR の費用はテーブルクラスでは変わらない。

## 14. 容量と性能

| 項目 | 見込み・方針 |
| --- | --- |
| 打刻1件の書き込み | 同期のトランザクションで6〜8単位、非同期の再計算（DAY と PS）で約8単位、合わせて約14単位 |
| 朝のピーク | 全体1万人なら秒間ピーク約100件で、同期の書き込みは約800単位/秒。10万人なら約8,000単位/秒 |
| パーティションの集中 | 打刻は従業員ごとのパーティションに分かれる。テナントのルートは、キャッシュしたマスタと共有端末の社員番号の検索だけで、上限（読み込み 3,000 単位/秒）に遠い |
| warm throughput | 本番の前に、業務テーブルの書き込みを 10,000 単位/秒以上に上げる（値は負荷試験で決める）。索引は朝に書かないため既定のまま |
| 項目の大きさ | 大きいのは申請（承認ルートの写し）とシフト表の HEAD（検証の結果）で、数十KB。400KB の上限に余裕を持たせる |
| 1回の Query | 1MB まで。名簿は続けて読む（GSI3 で1万人 約2.8MB、4ページ）。1万人を超えるテナントは想定の外 |
| 再計算の読み込み | 1回で1日分（約8KB）から期間（約250KB）まで |
| キャッシュ | マスタ・テナントの設定・名簿は60秒、法定値と祝日は1時間 |

## 15. 費用の見積もり

DynamoDB と KMS の月額（USD、東京）。

仮定:
- 業務テーブルの保存は1人1か月 140KB（索引を含む。PITR の対象は 100KB）、監査ログは 15KB。
- 書き込みは1人1か月 1,600 単位（打刻80回 × 約14単位と、そのほかの書き込み）、読み込みは 2,200 単位（API の呼び出しと再計算）。
- KMS は鍵4つ（ローテーション後の版を含めて1つ月$1〜$3）と、呼び出す役割ごとの鍵の更新（5分ごと）の要求。

| 項目 | 1,000人・1年目 | 1万人・1年目 | 1万人・6年目 | 10万人・6年目 |
| --- | ---: | ---: | ---: | ---: |
| 書き込み | 1.1 | 11.4 | 14.2 | 142 |
| 読み込み | 0.3 | 3.1 | 3.9 | 39 |
| 保存（業務） | 0.5 | 4.8 | 11.5 | 115 |
| PITR（業務） | 0.3 | 2.7 | 16.4 | 164 |
| 監査（保存と PITR） | 0.1 | 0.6 | 3.7 | 37 |
| KMS | 約5 | 約10 | 約12 | 約40 |
| **合計** | **約7** | **約33** | **約62** | **約540** |

- 6年目は、業務テーブルを Standard-IA にした後の値（書き込みと読み込みは約25%高く、保存は約6割安い）。Standard のままなら、1万人で月 約$14 高い。
- 保存の費用が読み書きの半分を超えるのは、1万人で約26GB（開始から約1年半）。
- 書き込みが多いのは、打刻をトランザクションで書き、再計算で日別勤怠と集計を書くため。

## 16. スキーマの変更

- 項目の形を変えるときは `schemaVersion` を上げる。読む側は1つ前の版も読めるようにし、書く側は新しい版で書く（読んで書き直したときに移る）。
- 一括で直す必要があるときは、移行の役割でテナントごとに処理するジョブを使う。前にオンデマンドバックアップを取る。
- 索引は無停止で加えられる（既存の項目は自動で載る）。キーの形を変えるときは、新旧の両方を書く期間を置いてから切り替える。
- 1つのテナントだけを戻す必要があるときは、PITR で別のテーブルに戻し、そのテナントの項目だけを写す（移行の役割で行う）。

## 17. まだ確かめていないこと

→ は確かめる時期（[実装計画と進捗](../plan.md)）。

- テナント用の認証情報の発行（AssumeRole とセッションタグ）の、朝のピークでの呼び出しの数と上限（キャッシュの効果を含む）→ PoC-05
- カスタマーマネージドキーを使うときの KMS の要求の数（呼び出す役割ごとの鍵の更新）→ 本番の前
- 打刻のトランザクション（3〜4項目）の p99 と、再計算の遅れ（Streams から DAY の更新まで）→ PoC-05
- warm throughput の値（業務テーブルの書き込み）→ PoC-05
