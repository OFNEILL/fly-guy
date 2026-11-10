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
    Start-Sleep -Seconds 2
    $flyApp.Refresh()
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
    Set-Control $messages 'Display name' 'Your fly'
    Invoke-Control $messages 'Use local invitation'
    Invoke-Control $messages 'Connect / receive messages'
    Start-Sleep -Seconds 1
    (Find-Control $messages 'Admin').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Invoke-Control $messages 'Use local owner key'
    Invoke-Control $messages 'Sign in as admin'
    Start-Sleep -Seconds 1
    $recipients = Find-Control $messages 'Recipients'
    $itemCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $items = $recipients.FindAll([System.Windows.Automation.TreeScope]::Children, $itemCondition)
    if ($items.Count -eq 0) { throw 'Connected recipient did not appear in admin roster.' }
    $selected = $null
    foreach ($item in $items) { if ($item.Current.Name -like 'Your fly*online*') { $selected = $item; break } }
    if ($null -eq $selected) { throw 'Local recipient did not become online.' }
    $selected.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $greeting = "Hello! It's me, your fly."
    Set-Control $messages 'Fly speech' $greeting
    Invoke-Control $messages 'Send as the fly'
    $bubble = $null
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        Start-Sleep -Milliseconds 250
        $bubble = Find-Window 'Fly Guy speech bubble'
        if ($null -ne $bubble -and -not $bubble.Current.IsOffscreen) { break }
    }
    if ($null -eq $bubble -or $bubble.Current.IsOffscreen) { throw 'Recipient did not show a speech bubble.' }
    $null = Find-Control $bubble $greeting
    Invoke-Control $messages 'Sign out'
    Write-Output 'PASS: local invitation, opt-in connection, owner login, online recipient list, admin send, real speech bubble and sign-out.'
    if ($KeepOpen) {
        Invoke-Control $messages 'Use local owner key'
        Invoke-Control $messages 'Sign in as admin'
        Write-Output "Updated Fly Guy is running with the admin panel open (PID $($flyApp.Id))."
    }
    $success = $true
}
finally {
    if (-not $KeepOpen -or -not $success) {
        $flyApp.Refresh()
        if (-not $flyApp.HasExited) { $null = $flyApp.CloseMainWindow(); $null = $flyApp.WaitForExit(5000) }
    }
}
