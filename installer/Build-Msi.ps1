param([string]$Version = '1.0.0', [switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $repo 'artifacts/publish/win-x64'
$output = Join-Path $repo 'artifacts/installer'
$wix = Join-Path $repo 'artifacts/tools-wix5/wix.exe'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a three-part MSI version, such as 1.0.1.' }
if (-not (Test-Path -LiteralPath $wix)) {
    dotnet tool install wix --version 5.0.2 --tool-path (Split-Path $wix -Parent)
    if ($LASTEXITCODE -ne 0) { throw 'WiX installation failed.' }
}
if (-not $SkipPublish) {
    dotnet publish (Join-Path $repo 'FlyGuy/FlyGuy.Desktop/FlyGuy.Desktop.csproj') -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}
if (-not (Test-Path (Join-Path $publish 'coreclr.dll'))) { throw 'A self-contained publish is required.' }
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
New-Item -ItemType Directory -Path $output -Force | Out-Null
[xml]$document = @'
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="Fly Guy" Manufacturer="Fly Guy" Version="1.0.0" UpgradeCode="7F5FD73C-3527-4E41-B841-970085F67D46" Scope="perUser" InstallerVersion="500">
    <MajorUpgrade DowngradeErrorMessage="A newer version of Fly Guy is already installed." />
    <MediaTemplate EmbedCab="yes" CompressionLevel="medium" />
    <Property Id="WINDOWSBUILD"><RegistrySearch Id="WindowsBuild" Root="HKLM" Key="SOFTWARE\Microsoft\Windows NT\CurrentVersion" Name="CurrentBuildNumber" Type="raw" Bitness="always64" /></Property>
    <Launch Condition="Installed OR (VersionNT64 AND WINDOWSBUILD &gt;= 19041)" Message="Fly Guy requires 64-bit Windows 10 version 2004 or newer, or Windows 11." />
    <StandardDirectory Id="LocalAppDataFolder"><Directory Id="INSTALLFOLDER" Name="Fly Guy" /></StandardDirectory>
    <StandardDirectory Id="ProgramMenuFolder" />
    <Feature Id="Main" Title="Fly Guy" Level="1" />
  </Package>
</Wix>
'@
$ns = $document.DocumentElement.NamespaceURI
$package = $document.DocumentElement.FirstChild
$package.SetAttribute('Version',$Version)
$feature = $package.SelectSingleNode("*[local-name()='Feature']")
$install = $package.SelectSingleNode(".//*[@Id='INSTALLFOLDER']")
function Element($parent, [string]$name, [hashtable]$attributes) {
    $node = $document.CreateElement($name,$ns)
    foreach ($entry in $attributes.GetEnumerator()) { $node.SetAttribute($entry.Key,[string]$entry.Value) }
    $null = $parent.AppendChild($node)
    return $node
}
function StableId([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return 'I' + ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($path.ToLowerInvariant())))).Replace('-','').Substring(0,30) }
    finally { $sha.Dispose() }
}
function StableGuid([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes('FlyGuy-win-x64:'+$path.ToLowerInvariant()))
        return ([Guid]::new([byte[]]$hash[0..15])).ToString('D')
    }
    finally { $sha.Dispose() }
}
$directories = @{ '' = $install }
foreach ($folder in (Get-ChildItem -LiteralPath $publish -Directory -Recurse | Sort-Object FullName)) {
    $relative = $folder.FullName.Substring($publish.Length+1)
    $parentRelative = Split-Path $relative -Parent
    $directories[$relative] = Element $directories[$parentRelative] 'Directory' @{ Id=(StableId $relative); Name=$folder.Name }
}
foreach ($file in (Get-ChildItem -LiteralPath $publish -File -Recurse | Sort-Object FullName)) {
    if ($file.Extension -eq '.pdb') { continue }
    $relative = $file.FullName.Substring($publish.Length+1)
    $id = StableId $relative
    $component = Element $directories[(Split-Path $relative -Parent)] 'Component' @{ Id=('C'+$id); Guid=(StableGuid $relative) }
    $fileId = if ($relative -eq 'FlyGuy.Desktop.exe') { 'FlyGuyExe' } else { 'F'+$id }
    $null = Element $component 'File' @{ Id=$fileId; Source=$file.FullName }
    $null = Element $component 'RegistryValue' @{ Root='HKCU'; Key='Software\FlyGuy\InstallerFiles'; Name=$id; Type='string'; Value=$Version; KeyPath='yes' }
    $null = Element $feature 'ComponentRef' @{ Id=('C'+$id) }
}
$shortcuts = Element $install 'Component' @{ Id='Shortcuts'; Guid='*' }
$null = Element $shortcuts 'RegistryValue' @{ Root='HKCU'; Key='Software\FlyGuy'; Name='Installed'; Type='integer'; Value='1'; KeyPath='yes' }
$null = Element $shortcuts 'Shortcut' @{ Id='StartMenuShortcut'; Directory='ProgramMenuFolder'; Name='Fly Guy'; Target='[#FlyGuyExe]'; WorkingDirectory='INSTALLFOLDER' }
foreach ($directory in $directories.Values) {
    $null = Element $shortcuts 'RemoveFolder' @{ Id=('Remove'+$directory.GetAttribute('Id')); Directory=$directory.GetAttribute('Id'); On='uninstall' }
}
$null = Element $feature 'ComponentRef' @{ Id='Shortcuts' }
$source = Join-Path $output 'FlyGuy.wxs'
$document.Save($source)
$msi = Join-Path $output "FlyGuy-$Version-win-x64.msi"
& $wix build $source -arch x64 -o $msi
if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }
Get-FileHash -LiteralPath $msi -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($msi))" } | Set-Content -LiteralPath "$msi.sha256" -Encoding ascii
Get-Item -LiteralPath $msi | Select-Object FullName,Length
