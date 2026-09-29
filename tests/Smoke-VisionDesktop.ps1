$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$desktopPath = Join-Path $PSScriptRoot '../FlyGuy/FlyGuy.Desktop/bin/Release/net10.0-windows/FlyGuy.Desktop.exe'
$visionProcess = Start-Process -FilePath $desktopPath -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds 3
    $visionProcess.Refresh()
    if ($visionProcess.HasExited) { throw 'Desktop exited during startup.' }
    $main = [System.Windows.Automation.AutomationElement]::FromHandle($visionProcess.MainWindowHandle)
    function Find-Control($root, [string]$name) {
        $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $element = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -eq $element) { throw "Control not found: $name" }
        return $element
    }
    function Invoke-Control($root, [string]$name) {
        $element = Find-Control $root $name
        $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }
    function Read-Text($root) {
        $items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        return (($items | ForEach-Object { $_.Current.Name }) -join "`n")
    }
    Invoke-Control $main 'Fly Vision'
    Start-Sleep -Seconds 2
    $windowName = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Fly Vision - two eyes')
    $processIdCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $visionProcess.Id)
    $condition = [System.Windows.Automation.AndCondition]::new($windowName, $processIdCondition)
    $viewer = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
    if ($null -eq $viewer) { throw 'Fly Vision window did not open.' }
    $text = Read-Text $viewer
    if ($text -notmatch 'Live desktop pixels' -or $text -notmatch 'Capture frames: [1-9]') { throw "Capture not live: $text" }
    $comboType = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ComboBox)
    $combo = $viewer.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$comboType)
    foreach ($mode in @('Raw','Receptors','Contrast / edges','Motion','Neural')) {
        $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        $expand.Expand()
        $name = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$mode)
        $selection = $null
        foreach ($item in $viewer.FindAll([System.Windows.Automation.TreeScope]::Descendants,$name)) {
            if ($item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$selection)) { break }
        }
        if ($null -eq $selection) { throw "Mode cannot be selected: $mode" }
        $selection.Select()
        $expand.Collapse()
        Start-Sleep -Milliseconds 150
    }
    foreach ($setting in @(@('Capture Hz','12'),@('Receptors Hz','24'),@('Processing Hz','18'),@('Brain Hz','120'),@('Render Hz','8'),@('Columns','16'),@('Rows','8'))) {
        $fieldId = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$setting[0].Replace(' ',''))
        $field = $viewer.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$fieldId)
        if ($null -eq $field) { throw "Editable setting not found: $($setting[0])" }
        $valuePattern = $field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        $valuePattern.SetValue($setting[1])
    }
    Invoke-Control $viewer 'Apply vision settings'
    Start-Sleep -Seconds 4
    $text = Read-Text $viewer
    if ($text -notmatch 'Vision settings applied' -or $text -notmatch 'Brain ([0-9.]+)/120 Hz') { throw "Settings/rates not visible: $text" }
    $brainRate = [double]::Parse($Matches[1],[Globalization.CultureInfo]::InvariantCulture)
    if ($brainRate -lt 90) { throw "Brain slowed with 8 Hz rendering: $brainRate" }
    if ($text -match 'VISION UNAVAILABLE') { throw "Vision failed: $text" }
    Write-Output ($text -split "`n" | Where-Object { $_ -match 'Capture frames:|^Capture |^Brain ' })
    Invoke-Control $main 'Pause / resume'
    Start-Sleep -Seconds 1
    Invoke-Control $main 'Pause / resume'
    Start-Sleep -Seconds 1
    $viewer.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Invoke-Control $main 'Fly Vision'
    Start-Sleep -Milliseconds 300
    Invoke-Control $main 'Exit creature'
    if (-not $visionProcess.WaitForExit(5000) -or $visionProcess.ExitCode -ne 0) { throw 'Desktop did not shut down cleanly.' }
    Write-Output 'PASS: live capture, two-eye viewer, all five modes, geometry/rate configuration, 120 Hz brain with 8 Hz rendering, pause/resume, reopen and shutdown.'
}
finally {
    $visionProcess.Refresh()
    if (-not $visionProcess.HasExited) {
        $null = $visionProcess.CloseMainWindow()
        if (-not $visionProcess.WaitForExit(5000)) { Stop-Process -Id $visionProcess.Id }
    }
}
