param([Parameter(Mandatory=$true)][string]$Version)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a three-part version, such as 1.1.0.' }
$repo = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $repo "artifacts/velopack-publish/$Version"
$output = Join-Path $repo "artifacts/velopack/$Version"
$vpk = Join-Path $repo 'artifacts/tools-velopack/vpk.exe'
if (-not (Test-Path -LiteralPath $vpk)) {
    dotnet tool install vpk --version 1.2.161 --tool-path (Split-Path $vpk -Parent)
    if ($LASTEXITCODE -ne 0) { throw 'Velopack tool installation failed.' }
}
dotnet publish (Join-Path $repo 'FlyGuy/FlyGuy.Desktop/FlyGuy.Desktop.csproj') -c Release -r win-x64 --self-contained true "-p:Version=$Version" -p:DebugType=None -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$assets = Get-Content (Join-Path $repo 'FlyGuy/FlyGuy.Desktop/obj/project.assets.json') -Raw | ConvertFrom-Json
$runtime = Get-Content (Join-Path $publish 'FlyGuy.Desktop.runtimeconfig.json') -Raw | ConvertFrom-Json
$notices = Join-Path $publish 'licenses'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
    $packageName = $framework.name.ToLowerInvariant()+'.runtime.win-x64'
    $package = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder ($packageName+'/'+$framework.version)
        if (Test-Path -LiteralPath $candidate) { $package=$candidate; break }
    }
    if (-not $package) { throw "Runtime notices not found for $packageName" }
    foreach ($notice in (Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$' })) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $notices ($framework.name+'-'+$notice.Name))
    }
}
# Keep the install directory separate from persistent settings in %LOCALAPPDATA%\FlyGuy.
& $vpk pack --packId FlyGuyDesktop --packVersion $Version --packDir $publish --mainExe FlyGuy.Desktop.exe --packTitle 'Fly Guy' --packAuthors 'Fly Guy' --runtime win-x64 --shortcuts StartMenuRoot --outputDir $output
if ($LASTEXITCODE -ne 0) { throw 'Velopack packaging failed.' }
Write-Host "Installer and update feed: $output"
