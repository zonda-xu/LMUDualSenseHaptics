param([Parameter(Mandatory=$true)][string]$Archive)
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('LMU 解压 测试 ' + [guid]::NewGuid().ToString('N'))
$launcher = $null
$child = $null
$started = Get-Date
$languagePath = Join-Path $env:LOCALAPPDATA 'LMUDualSenseHaptics/ui-language.txt'
$hadLanguage = Test-Path $languagePath
$oldLanguage = if ($hadLanguage) { [IO.File]::ReadAllBytes($languagePath) } else { $null }
New-Item -ItemType Directory -Force (Split-Path $languagePath) | Out-Null
[IO.File]::WriteAllText($languagePath, 'zh-CN', [Text.Encoding]::ASCII)
try {
    Expand-Archive -LiteralPath $Archive -DestinationPath $testRoot
    $rootFiles = @(Get-ChildItem $testRoot -File)
    if ($rootFiles.Count -ne 1 -or $rootFiles[0].Name -ne 'LMUDualSenseHaptics.exe') { throw 'Only the launch EXE may be a root file.' }
    foreach ($file in @('bin/LMUDualSenseHaptics.exe','bin/LMUDualSenseHaptics.dll','bin/hostfxr.dll','bin/coreclr.dll','docs/LICENSE','docs/THIRD_PARTY_NOTICES.md')) {
        if (-not (Test-Path (Join-Path $testRoot $file))) { throw "Missing package file: $file" }
    }
    function Start-PackagedApp {
        $script:child = $null
        $script:launcher = Start-Process -FilePath $rootFiles[0].FullName -WorkingDirectory $env:TEMP -PassThru
        $deadline = [DateTime]::UtcNow.AddSeconds(40)
        while ([DateTime]::UtcNow -lt $deadline) {
            if ($launcher.HasExited) { throw "Launcher exited before the window opened: $($launcher.ExitCode)" }
            if (-not $child) {
                $info = Get-CimInstance Win32_Process -Filter "ParentProcessId = $($launcher.Id)" | Where-Object { $_.Name -eq 'LMUDualSenseHaptics.exe' } | Select-Object -First 1
                if ($info) { $script:child = Get-Process -Id $info.ProcessId }
            }
            if ($child) { $child.Refresh(); if ($child.MainWindowHandle -ne 0) { break } }
            Start-Sleep -Milliseconds 250
        }
        if (-not $child -or $child.MainWindowHandle -eq 0) { throw 'Extracted application did not open a WPF window.' }
        if ($child.Path -ne (Join-Path $testRoot 'bin/LMUDualSenseHaptics.exe')) { throw 'Launcher started the wrong application.' }
    }
    Start-PackagedApp
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($child.MainWindowHandle)
    $scope = [System.Windows.Automation.TreeScope]::Descendants
    function Find-Id([string]$id) {
        $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
        $element = $window.FindFirst($scope, $condition)
        if (-not $element) { throw "Control not found: $id" }
        return $element
    }
    function Find-Name([string]$name) {
        $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $element = $window.FindFirst($scope, $condition)
        if (-not $element) { throw "Localized control not found: $name" }
        return $element
    }
    function Select-Language([string]$name) {
        $combo = Find-Id 'LanguageChoice'
        $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Start-Sleep -Milliseconds 150
        $condition = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        do {
            $item = $combo.FindFirst($scope, $condition)
            if (-not $item) { $item = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst($scope, $condition) }
            if ($item) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        if (-not $item) {
            $combo.FindAll($scope, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { Write-Host "LANGUAGE ITEM: $($_.Current.ControlType.ProgrammaticName) / $($_.Current.Name)" }
            throw "Language item not found: $name"
        }
        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
        Start-Sleep -Milliseconds 250
    }
    function Wait-Dialog([string]$title) {
        $condition = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $title),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window))
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        do {
            $dialog = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst($scope, $condition)
            if ($dialog) { return $dialog }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        throw "Localized dialog did not open: $title"
    }
    $codes = @('en','fr','es','ru','ar','zh-CN')
    $names = @('English','Français','Español','Русский','العربية','中文')
    for ($i=0; $i -lt $codes.Count; $i++) {
        $code = $codes[$i]
        $catalog = Get-Content (Join-Path $PSScriptRoot "../src/LMUDualSenseHaptics/Localization/$code.json") -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
        Select-Language $names[$i]
        if ((Get-Content $languagePath -Raw) -ne $code) { throw 'Language preference was not persisted.' }
        $import = Find-Id 'ImportVehicleButton'
        if ($import.Current.Name -ne $catalog['Text_340152EE2E']) { throw 'Static button language did not update.' }
        $null = Find-Name $catalog['Text_E9DFBBC15A']
        $null = Find-Name $catalog['Text_3E0454700A']
        # Physical left/right and L2/R2 meters must not exchange places in Arabic.
        $left = Find-Id 'BrakeMeter'; $right = Find-Id 'ThrottleMeter'
        if ($left.Current.BoundingRectangle.X -ge $right.Current.BoundingRectangle.X) { throw 'Physical trigger order reversed.' }
        # WPF buttons may return UIA Culture=0. Check visible text and the actual RTL layout.
        $languageX = (Find-Id 'LanguageChoice').Current.BoundingRectangle.X
        $themeX = (Find-Id 'ThemeButton').Current.BoundingRectangle.X
        if (($code -eq 'ar') -ne ($languageX -gt $themeX)) { throw "Incorrect reading direction: $code" }
        $import.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $dialog = Wait-Dialog $catalog['Text_96618B11E9']
        $dialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        Start-Sleep -Milliseconds 150
        # Visit every tab and open the app-owned confirmation without changing settings.
        $tab = Find-Name $catalog['Text_3F33F2BC25']
        $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        $tabCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
        $tabs = (Find-Id 'SettingsTabs').FindAll($scope, $tabCondition)
        foreach ($t in $tabs) {
            $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Start-Sleep -Milliseconds 100
            if ($t.Current.BoundingRectangle.Width -le 0) { throw 'Invisible translated tab.' }
        }
        (Find-Id 'ResetDefaultsButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $dialog = Wait-Dialog $catalog['Text_F242C94797']
        foreach ($pair in @(@('ConfirmAccept','Text_FAC2A67AD8'),@('ConfirmCancel','Text_2CD0F3BE87'))) {
            $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $pair[0])
            $button = $dialog.FindFirst($scope, $condition)
            if (-not $button -or $button.Current.Name -ne $catalog[$pair[1]]) { throw 'Confirmation action was not translated.' }
            if ($pair[0] -eq 'ConfirmCancel') { $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
        }
        $tabs[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Write-Host "LANGUAGE UI PASS: $code, dynamic controls, tabs, file dialog, confirmation, physical trigger order, persistence."
    }
    foreach ($theme in @('浅色','深色')) {
        if ($theme -eq '深色') {
            $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, '深色外观')
            $button = $window.FindFirst($scope, $condition)
            if (-not $button) { throw 'Theme button not found.' }
            $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 150
        }
        foreach ($name in @('编辑车辆扳机配置','刹车反馈模式')) {
            $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
            $combo = $window.FindFirst($scope, $condition)
            if (-not $combo) { throw "UI control not found: $name" }
            $pattern = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            $pattern.Expand()
            Start-Sleep -Milliseconds 150
            $pattern.Collapse()
        }
    }
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, '分享此车配置')
    $exportButton = $window.FindFirst($scope, $condition)
    if (-not $exportButton -or $exportButton.Current.IsEnabled) { throw 'Sharing must require a selected vehicle profile.' }
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, '导入车辆配置')
    $importButton = $window.FindFirst($scope, $condition)
    if (-not $importButton -or -not $importButton.Current.IsEnabled) { throw 'Vehicle import button is unavailable.' }
    $importButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, '导入车辆扳机配置')
    $dialog = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not $dialog -and [DateTime]::UtcNow -lt $deadline) {
        $dialog = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst($scope, $condition)
        if (-not $dialog) { Start-Sleep -Milliseconds 100 }
    }
    if (-not $dialog) { throw 'Vehicle import file dialog did not open.' }
    $dialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Milliseconds 200
    Select-Language 'العربية'
    if (-not $child.CloseMainWindow()) { throw 'Could not close application window.' }
    if (-not $launcher.WaitForExit(15000) -or $launcher.ExitCode -ne 0) { throw 'Launcher did not propagate clean application shutdown.' }
    Start-PackagedApp
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($child.MainWindowHandle)
    Start-Sleep -Milliseconds 250
    $arabic = Get-Content (Join-Path $PSScriptRoot '../src/LMUDualSenseHaptics/Localization/ar.json') -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
    if ((Find-Id 'ImportVehicleButton').Current.Name -ne $arabic['Text_340152EE2E']) { throw 'Saved Arabic language did not survive restart.' }
    if (-not $child.CloseMainWindow()) { throw 'Could not close restarted application.' }
    if (-not $launcher.WaitForExit(15000) -or $launcher.ExitCode -ne 0) { throw 'Restarted application did not exit cleanly.' }
    Write-Host 'LANGUAGE RESTART PASS: saved Arabic is restored on a fresh process.'
    Write-Host 'PACKAGE PASS: root-only EXE, bin dependencies, Unicode/spaces path, unrelated cwd, WPF startup, Material combos in light/dark themes, vehicle share/import controls and import cancellation, clean exit.'
} catch {
    Write-Host "Package startup diagnostics: $($_.Exception.Message)"
    Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=$started} -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -match 'LMUDualSenseHaptics' } |
        Select-Object -First 5 | ForEach-Object { Write-Host $_.Message }
    throw
} finally {
    if ($child -and -not $child.HasExited) { Stop-Process -Id $child.Id -Force }
    if ($launcher -and -not $launcher.HasExited) { Stop-Process -Id $launcher.Id -Force }
    if ($hadLanguage) { [IO.File]::WriteAllBytes($languagePath, $oldLanguage) } elseif (Test-Path $languagePath) { Remove-Item $languagePath }
    if (Test-Path $testRoot) { Remove-Item $testRoot -Recurse -Force }
}
