# 作業の単位の検証: Release ビルド (警告 0)、テスト (dotnet run)、InspectCode (0 件)、改行 (CRLF)
# 手順の説明は同じフォルダの SKILL.md
$ErrorActionPreference = 'Stop'
$root = (git -C $PSScriptRoot rev-parse --show-toplevel).Trim()
$solution = Join-Path $root 'Tna.slnx'
$failures = [Collections.Generic.List[string]]::new()

function Write-Step([string] $name) {
    Write-Host "==> $name"
}

#--------------------------------------------------------------------------------
# Build
#--------------------------------------------------------------------------------

# プロジェクトのないソリューションはビルドできないため、1〜3 を飛ばす
$hasProjects = Select-String -Path $solution -Pattern '<Project ' -Quiet
if ($hasProjects) {
    Write-Step 'Build'
    dotnet build $solution -c Release -warnaserror -nologo
    if ($LASTEXITCODE -ne 0) {
        $failures.Add('Build')
    }
}

#--------------------------------------------------------------------------------
# Test
#--------------------------------------------------------------------------------

if ($hasProjects -and ($failures.Count -eq 0)) {
    Write-Step 'Test'
    $tests = @(Get-ChildItem (Join-Path $root 'tests') -Filter '*.csproj' -Recurse -ErrorAction SilentlyContinue)
    foreach ($project in $tests) {
        # Microsoft Testing Platform の実行ファイルとして動かす (dotnet test は使わない)
        dotnet run --project $project.FullName -c Release --no-build
        if ($LASTEXITCODE -ne 0) {
            $failures.Add("Test $($project.BaseName)")
        }
    }

    Write-Host "Test projects: $($tests.Count)"
}

#--------------------------------------------------------------------------------
# InspectCode
#--------------------------------------------------------------------------------

if ($hasProjects -and ($failures.Count -eq 0)) {
    Write-Step 'InspectCode'
    # 前の結果のキャッシュに左右されないよう、毎回新しいキャッシュで解析する
    $work = Join-Path ([IO.Path]::GetTempPath()) "tna-inspect-$([Guid]::NewGuid().ToString('N'))"
    $report = Join-Path $work 'report.sarif'
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        jb inspectcode $solution --no-build --swea "--caches-home=$work" "--output=$report" --format=Sarif
        if ($LASTEXITCODE -ne 0) {
            $failures.Add('InspectCode (run)')
        }
        else {
            $results = @((Get-Content $report -Raw | ConvertFrom-Json).runs | ForEach-Object { $_.results })
            foreach ($result in $results) {
                $location = $result.locations[0].physicalLocation
                Write-Host "$($location.artifactLocation.uri):$($location.region.startLine) $($result.ruleId) $($result.message.text)"
            }

            Write-Host "InspectCode issues: $($results.Count)"
            if ($results.Count -gt 0) {
                $failures.Add('InspectCode')
            }
        }
    }
    finally {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

#--------------------------------------------------------------------------------
# Line endings
#--------------------------------------------------------------------------------

Write-Step 'Line endings'
# 追跡中と、無視していない新しいファイルのうち、作業ツリーで LF か混在のもの (2進のファイルは w/-text)。
# 属性で LF と決めたファイルは除く
$wrong = @(git -C $root ls-files --eol --cached --others --exclude-standard |
    Where-Object { ($_ -match '\bw/(lf|mixed)\s') -and ($_ -notmatch 'attr/[^\t]*eol=lf') })
foreach ($line in $wrong) {
    Write-Host $line
}

Write-Host "Files not CRLF: $($wrong.Count)"
if ($wrong.Count -gt 0) {
    $failures.Add('Line endings')
}

#--------------------------------------------------------------------------------
# Summary
#--------------------------------------------------------------------------------

if ($failures.Count -gt 0) {
    Write-Host "FAILED: $($failures -join ', ')"
    exit 1
}

Write-Host 'PASSED'
