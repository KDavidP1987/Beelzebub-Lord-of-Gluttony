<#
.SYNOPSIS
    Preflight gate for Beelzebub. Run before every chore(release) commit; the commit waits for PREFLIGHT OK.

.DESCRIPTION
    Blocking checks (exit 1 on any failure):
      versions   csproj <Version> == thunderstore.toml versionNumber
      changelog  CHANGELOG.md has a "## [<version>]" entry, and is under the size cap (Thunderstore ships it)
      readme     the Thunderstore README status line names v<version>; the root README is in sync
                 (python tools/sync_github_readme.py --check)
      api        docs/BCH_INTEGRATION_HANDOFF.md banner "ApiVersion = N" == ApiCommands.cs ApiVersion
      audits     every docs/audits/<slug>.md holds "## Pre-audit", "## Post-audit" and "Codex verdict:"
      build      dotnet build Beelzebub.sln -c Release: 0 errors
      tests      dotnet test Beelzebub.Tests: passes, and at least one test ran
      bar-reset  python tools/check_bar_reset.py all (v0.137 bar-reset dod plan D5 D13-D18 D21 D23 D26 D28)

    -SkipBuild skips build + tests (for a fast docs-only check). -LogCheck instead reads the live server logs
    (BepInEx/LogOutput.log and logs/NyarDev.log) and fails on a Beelzebub stack frame or an empty/missing log;
    run it after every in-game session, BEFORE the next boot overwrites the logs. -LogDir <dir> reads
    <dir>\LogOutput.log and <dir>\NyarDev.log instead (a copied session, or the fault harness's fixture logs).
    Adapted from Nyarlathotep's tools/preflight.ps1 (minimal port; no fixture self-test yet).
#>
param(
    [switch]$SkipBuild,
    [switch]$LogCheck,
    [string]$ServerPath = 'C:\Program Files (x86)\Steam\steamapps\common\VRisingDedicatedServer',
    [string]$LogDir = '',
    [int]$ChangelogMaxKB = 64
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot          # Beelzebub/
$Proj = Join-Path $Root 'Beelzebub'
$RepoRoot = Split-Path -Parent $Root
$results = [System.Collections.Generic.List[object]]::new()
function Add-Result([string]$Name, [bool]$Pass, [string]$Line) { $results.Add([pscustomobject]@{ Name = $Name; Pass = $Pass; Line = $Line }) }

if ($LogCheck) {
    $logs = if ($LogDir) { @((Join-Path $LogDir 'LogOutput.log'), (Join-Path $LogDir 'NyarDev.log')) }
            else { @((Join-Path $ServerPath 'BepInEx\LogOutput.log'), (Join-Path $ServerPath 'logs\NyarDev.log')) }
    foreach ($l in $logs) {
        if (-not (Test-Path $l) -or (Get-Item $l).Length -eq 0) { Add-Result 'log' $false "missing or empty: $l"; continue }
        $text = Get-Content $l -Raw
        $frames = ([regex]::Matches($text, '(?m)^\s*at Beelzebub\.')).Count
        $errors = ([regex]::Matches($text, '(?m)^\[Error\s*:\s*Beelzebub\]')).Count
        $warns  = ([regex]::Matches($text, '(?m)^\[Warning\s*:\s*Beelzebub\]')).Count
        $beelz  = ([regex]::Matches($text, '\[Beelz')).Count
        Add-Result ("log " + (Split-Path $l -Leaf)) ($frames -eq 0) "$frames Beelzebub stack frame(s), $errors error(s), $warns warning(s), $beelz [Beelz lines"
    }
} else {
    # versions
    $csproj = Get-Content (Join-Path $Proj 'Beelzebub.csproj') -Raw
    $ver = [regex]::Match($csproj, '<Version>([^<]+)</Version>').Groups[1].Value
    $toml = [regex]::Match((Get-Content (Join-Path $Proj 'thunderstore.toml') -Raw), 'versionNumber\s*=\s*"([^"]+)"').Groups[1].Value
    Add-Result 'versions' ($ver -and $ver -eq $toml) "csproj $ver, thunderstore.toml $toml"

    # changelog
    $clPath = Join-Path $Proj 'CHANGELOG.md'
    $cl = Get-Content $clPath -Raw
    $kb = [math]::Round((Get-Item $clPath).Length / 1KB)
    Add-Result 'changelog' ($cl.Contains("## [$ver]")) "entry for [$ver]: $($cl.Contains("## [$ver]"))"
    Add-Result 'changelog size' ($kb -le $ChangelogMaxKB) "$kb KB (cap $ChangelogMaxKB KB; older entries belong in docs/CHANGELOG_FULL.md)"

    # readme
    $readme = Get-Content (Join-Path $Proj 'README.md') -Raw
    $statusOk = $readme -match "\*\*Status:\*\*[^\r\n]*v$([regex]::Escape($ver))\b"
    Add-Result 'readme status' $statusOk "Thunderstore README status line names v$ver`: $statusOk"
    $sync = & python (Join-Path $Root 'tools\sync_github_readme.py') --check 2>&1
    Add-Result 'readme sync' ($LASTEXITCODE -eq 0) ("root README: " + ($sync -join ' '))

    # api banner
    $api = [regex]::Match((Get-Content (Join-Path $Proj 'Commands\ApiCommands.cs') -Raw), 'const int ApiVersion\s*=\s*(\d+)').Groups[1].Value
    $handoff = [regex]::Match((Get-Content (Join-Path $Proj 'docs\BCH_INTEGRATION_HANDOFF.md') -Raw), 'ApiVersion = (\d+)').Groups[1].Value
    Add-Result 'api banner' ($api -and $api -eq $handoff) "ApiCommands.cs $api, handoff banner $handoff"

    # audits
    $auditDir = Join-Path $Proj 'docs\audits'
    $bad = @()
    if (Test-Path $auditDir) {
        foreach ($f in Get-ChildItem $auditDir -Filter *.md | Where-Object Name -ne 'README.md') {
            $t = Get-Content $f.FullName -Raw
            foreach ($m in '## Pre-audit', '## Post-audit', 'Codex verdict:') { if (-not $t.Contains($m)) { $bad += "$($f.Name) lacks '$m'" } }
        }
    }
    Add-Result 'audits' ($bad.Count -eq 0) ($(if ($bad) { $bad -join '; ' } else { 'every audit record has its markers' }))

    # bar-reset (v0.137 dod plan) evidence checks — rollback / session / selftest run on their own
    $br = & python (Join-Path $Root 'tools\check_bar_reset.py') all 2>&1
    Add-Result 'bar-reset' ($LASTEXITCODE -eq 0) (($br | ForEach-Object { "$_" }) -join ' | ')

    if (-not $SkipBuild) {
        $sln = Join-Path $Root 'Beelzebub.sln'
        # A gate never deploys: point the server path somewhere absent so BuildToServer no-ops.
        $b = & dotnet build $sln -c Release -nologo '-p:VRisingServerPath=Z:\no-deploy' 2>&1
        $errLine = ($b | Select-String -Pattern '^\s*(\d+) Error\(s\)' | Select-Object -Last 1)
        $nErr = if ($errLine) { [int]$errLine.Matches[0].Groups[1].Value } else { -1 }
        Add-Result 'build' ($LASTEXITCODE -eq 0 -and $nErr -eq 0) "dotnet build Release: exit $LASTEXITCODE, $nErr error(s)"

        $t = & dotnet test (Join-Path $Root 'Beelzebub.Tests\Beelzebub.Tests.csproj') -nologo 2>&1
        $sum = ($t | Select-String -Pattern 'Passed!|Failed!' | Select-Object -Last 1)
        $total = if ($sum -and $sum.Line -match 'Total:\s*(\d+)') { [int]$Matches[1] } else { 0 }
        Add-Result 'tests' ($LASTEXITCODE -eq 0 -and $total -gt 0) ("dotnet test: " + $(if ($sum) { $sum.Line.Trim() } else { 'no summary line (0 tests run?)' }))
    }
}

foreach ($r in $results) { Write-Host ("{0} {1,-15} {2}" -f $(if ($r.Pass) { 'ok  ' } else { 'FAIL' }), $r.Name, $r.Line) }
$failed = @($results | Where-Object { -not $_.Pass })
if ($failed.Count) { Write-Host "PREFLIGHT FAILED ($($failed.Count) of $($results.Count))"; exit 1 }
Write-Host "PREFLIGHT OK ($($results.Count) checks)"
exit 0
