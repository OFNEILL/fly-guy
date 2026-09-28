$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$desktopPath = Join-Path $PSScriptRoot '../FlyGuy/FlyGuy.Desktop/bin/Release/net10.0-windows/FlyGuy.Desktop.exe'
$sprayProcess = Start-Process -FilePath $desktopPath -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds 2
    $sprayProcess.Refresh()
    if ($sprayProcess.HasExited) { throw 'Desktop exited during startup.' }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($sprayProcess.MainWindowHandle)
    function Find-Control([string]$name) {
        $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -eq $element) { throw "Control not found: $name" }
        return $element
    }
    function Invoke-Control([string]$name) {
        $element = Find-Control $name
        $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
    }
    $comboCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)
    $combo = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $comboCondition)
    $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    $selection = $null
    $sprayName = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Fly Spray')
    foreach ($sprayItem in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $sprayName)) {
        if ($sprayItem.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selection)) { break }
    }
    if ($null -eq $selection) { throw 'Spray option does not expose selection.' }
    $selection.Select()
    $expand.Collapse()
    Invoke-Control 'Apply rates'
    $null = Find-Control 'Rates applied.'
    Invoke-Control 'Apply at cursor in 3s'
    Start-Sleep -Seconds 4
    $descendants = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $sprayVisible = $false
    foreach ($element in $descendants) {
        if ($element.Current.Name -match 'Spray clouds: 1') { $sprayVisible = $true }
    }
    if (-not $sprayVisible) { throw 'Timed spray did not create one cloud.' }
    Invoke-Control 'Pause / resume'
    Invoke-Control 'Reset brain'
    Invoke-Control 'Pause / resume'
    $sprayProcess.Refresh()
    if ($sprayProcess.HasExited -or -not $sprayProcess.Responding) { throw 'Desktop failed after spray interaction.' }
    Invoke-Control 'Exit creature'
    if (-not $sprayProcess.WaitForExit(5000)) { throw 'Desktop did not shut down.' }
    if ($sprayProcess.ExitCode -ne 0) { throw "Desktop exit code: $($sprayProcess.ExitCode)" }
    Write-Output 'PASS: desktop startup, spray tool selection, rate controls, timed cloud creation, pause/reset/resume and clean shutdown.'
}
finally {
    $sprayProcess.Refresh()
    if (-not $sprayProcess.HasExited) {
        $null = $sprayProcess.CloseMainWindow()
        if (-not $sprayProcess.WaitForExit(5000)) { Stop-Process -Id $sprayProcess.Id }
    }
}
