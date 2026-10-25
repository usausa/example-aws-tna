---
description: 作業の単位を閉じる前とコミットを確かめる前に回す検証。Release ビルド (警告 0)、生成物、テスト (dotnet run)、InspectCode (0 件)、OpenAPI、改行 (CRLF) を確かめる
---

# 検証

作業の単位を閉じる前と、コミットを確かめる前に回す。

```powershell
pwsh -NoProfile -File .claude/skills/verify/verify.ps1
```

1. Release ビルド: `Tna.slnx` を Release でビルドし、警告を誤りとして扱う (警告 0)
2. 生成物: 生成の道具 (`tools/Tna.Tools.Generator`) を `--verify` で動かし、コミットした生成物が OpenAPI から作り直したものと同じかを確かめる
3. テスト: `tests/` の各テストのプロジェクトを `dotnet run --project <csproj> -c Release` で動かす (`dotnet test` は使わない)
4. InspectCode: `jb inspectcode Tna.slnx --no-build --swea --caches-home=<毎回新しい一時フォルダ>` の結果 (SARIF) が 0 件
5. OpenAPI: `docs/api` で `npx --yes @redocly/cli@2.59.0 lint` を動かし、仕様が規則に合うかを確かめる (Node が要る)
6. 改行: git の対象 (追跡中と、無視していない新しいファイル) のテキストファイルが、作業ツリーで CRLF

- ソリューションにプロジェクトがない間は、1〜4 を飛ばす。生成の道具がない間は 2 を、仕様の設定 (`docs/api/redocly.yaml`) がない間は 5 を飛ばす
- どれかが失敗したら、直してから回し直す
- 報告には、各段の成否と件数 (テストの数、InspectCode の件数、改行の誤り) を書く
