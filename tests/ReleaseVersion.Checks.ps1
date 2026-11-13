$ErrorActionPreference = 'Stop'
$resolver = Join-Path (Split-Path $PSScriptRoot -Parent) 'installer/Get-ReleaseVersion.ps1'
$global:FlyGuyReleaseTestScenario = @{}
# Fake git responses without creating or changing repository tags.
function git {
    $global:LASTEXITCODE = 0
    switch ($args[0]) {
        'tag' { $global:FlyGuyReleaseTestScenario.Tags }
        'rev-list' { $global:FlyGuyReleaseTestScenario.ReleaseCommit }
        'rev-parse' { $global:FlyGuyReleaseTestScenario.CurrentCommit }
        'merge-base' { $global:LASTEXITCODE = $global:FlyGuyReleaseTestScenario.AncestorResult }
        default { throw "Unexpected git command: $args" }
    }
}
try {
    $global:FlyGuyReleaseTestScenario = @{ Tags=@('beta', 'beta-0.0.12') }
    if ((& $resolver) -ne '1.0.0') { throw 'First production version must be 1.0.0.' }
    $global:FlyGuyReleaseTestScenario = @{ Tags=@('v1.0.9','v1.0.10','v2.0.0-beta'); ReleaseCommit='old'; CurrentCommit='new'; AncestorResult=1 }
    if ((& $resolver) -ne '1.0.11') { throw 'Patch versions must sort numerically and ignore prereleases.' }
    $global:FlyGuyReleaseTestScenario.CurrentCommit = 'old'
    if ((& $resolver) -ne '1.0.10') { throw 'A rerun must reuse its existing version.' }
    $global:FlyGuyReleaseTestScenario.CurrentCommit = 'earlier'
    $global:FlyGuyReleaseTestScenario.AncestorResult = 0
    if ($null -ne (& $resolver)) { throw 'A superseded commit must be skipped.' }
    Write-Host 'PASS: first production version, numeric patch increment, prerelease exclusion, reruns and superseded commits.'
}
finally { Remove-Item Function:git; Remove-Variable FlyGuyReleaseTestScenario -Scope Global }
