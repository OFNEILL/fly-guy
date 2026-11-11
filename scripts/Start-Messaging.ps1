param([switch]$CopyOwnerKey)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$relayDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FlyGuy'
$ownerPath = Join-Path $relayDirectory 'relay-owner.json'
[IO.Directory]::CreateDirectory($relayDirectory) | Out-Null
function New-ProtectedKey {
    $bytes = New-Object byte[] 32
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($bytes) } finally { $random.Dispose() }
    $key = [BitConverter]::ToString($bytes).Replace('-', '')
    return [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($key), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser))
}
if (-not (Test-Path -LiteralPath $ownerPath)) {
    $keys = @{ ProtectedAdminKey = New-ProtectedKey }
    [IO.File]::WriteAllText($ownerPath, ($keys | ConvertTo-Json))
}
$keys = Get-Content -LiteralPath $ownerPath -Raw | ConvertFrom-Json
function Open-Key([string]$value) {
    return [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($value), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser))
}
$oldAdmin = $env:FLYGUY_ADMIN_KEY

$oldData = $env:FLYGUY_DATA_PATH
try {
    $env:FLYGUY_ADMIN_KEY = Open-Key $keys.ProtectedAdminKey

    $env:FLYGUY_DATA_PATH = Join-Path $relayDirectory 'relay-state.json'
    if ($CopyOwnerKey) {
        Set-Clipboard -Value $env:FLYGUY_ADMIN_KEY
        Write-Output 'Owner key copied to clipboard. Paste it into the web desk, then clear your clipboard (and clipboard history if enabled).'
    }
    Write-Output 'Web message desk: http://127.0.0.1:5187. Open it in your browser.'
    Write-Output 'Local relay: http://127.0.0.1:5187. In Fly messages, connect without an invitation; use the owner key only for admin access. Keys are protected by this Windows account.'
    dotnet run --project (Join-Path $PSScriptRoot '../FlyGuy/FlyGuy.Relay') -c Release --no-launch-profile -- --urls http://127.0.0.1:5187
    if ($LASTEXITCODE -ne 0) { throw "Relay exited with code $LASTEXITCODE." }
}
finally {
    $env:FLYGUY_ADMIN_KEY = $oldAdmin

    $env:FLYGUY_DATA_PATH = $oldData
}
