param(
    [string]$DesktopPath = (Join-Path $PSScriptRoot '../artifacts/messaging-preview/FlyGuy.Desktop.exe'),
    [switch]$KeepOpen
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$flyApp = Start-Process -FilePath $DesktopPath -WindowStyle Hidden -PassThru
$success = $false
try {
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        Start-Sleep -Milliseconds 250
        $flyApp.Refresh()
        if ($flyApp.HasExited -or $flyApp.MainWindowHandle -ne [IntPtr]::Zero) { break }
    }
    if ($flyApp.HasExited) { throw 'Desktop exited during startup.' }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($flyApp.MainWindowHandle)
    function Find-Control($scope, [string]$name) {
        $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $element = $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -eq $element) { throw "Control not found: $name" }
        return $element
    }
    function Invoke-Control($scope, [string]$name) {
        (Find-Control $scope $name).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }
    function Set-Control($scope, [string]$name, [string]$value) {
        (Find-Control $scope $name).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
    }
    $processCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $flyApp.Id)
    function Find-Window([string]$title) {
        $titleCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $title)
        $owned = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $titleCondition)
        if ($null -ne $owned) { return $owned }
        foreach ($window in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $processCondition)) {
            if ($window.Current.Name -eq $title) { return $window }
        }
        return $null
    }
    Invoke-Control $root 'Fly messages'
    $messages = $null
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 250
        $messages = Find-Window 'Fly Guy - messages'
        if ($null -ne $messages) { break }
    }
    if ($null -eq $messages) { throw 'Messaging panel did not open.' }
    $relayField = (Find-Control $messages 'Relay address').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if (-not $relayField.Current.IsReadOnly -or $relayField.Current.Value -ne 'https://fly-guy-relay.vercel.app/') { throw 'Hosted relay must be fixed.' }
    $nameField = (Find-Control $messages 'Display name').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $parsedAddress = $null
    if (-not $nameField.Current.IsReadOnly -or -not [System.Net.IPAddress]::TryParse($nameField.Current.Value, [ref]$parsedAddress)) { throw 'Recipient name must be an IP address.' }
    $connected = $false
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, "Connected as $($nameField.Current.Value). Waiting for fly messages.")
        if ($null -ne $messages.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)) { $connected = $true; break }
        Start-Sleep -Milliseconds 250
    }
    if (-not $connected) { throw 'Recipient did not connect automatically. Check saved Disconnect preference and internet access.' }
    Invoke-Control $messages 'Disconnect'
    Invoke-Control $messages 'Connect / receive messages'
    Write-Output 'PASS: fixed hosted relay, IP display name, automatic startup connection, disconnect and reconnect controls.'
    $success = $true

}
finally {
    if (-not $KeepOpen -or -not $success) {
        $flyApp.Refresh()
        if (-not $flyApp.HasExited) { $null = $flyApp.CloseMainWindow(); $null = $flyApp.WaitForExit(5000) }
    }
}
