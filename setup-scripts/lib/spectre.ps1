$script:VrcSetupSpectreAvailable = $null

function New-VrcSetupActionButton {
    param([string]$Label, [string]$Color = '#67E8A5', [string]$Key = 'ENTER')
    $text = "${Key}   $(ConvertTo-VrcSetupSpectreText $Label)"
    $button = [Spectre.Console.Panel]::new([Spectre.Console.Markup]::new("[bold black on ${Color}] ${text} [/]"))
    $button.Border = [Spectre.Console.BoxBorder]::Rounded
    $button.BorderStyle = New-VrcSetupSpectreStyle -Foreground $Color
    $button.Padding = [Spectre.Console.Padding]::new(1, 0)
    return $button
}

function Initialize-VrcSetupSpectre {
    param([string]$ScriptDir)

    if ($null -ne $script:VrcSetupSpectreAvailable) { return [bool]$script:VrcSetupSpectreAvailable }
    $script:VrcSetupSpectreAvailable = $false
    if ($env:VRCSETUP_DISABLE_SPECTRE -eq '1' -or $PSVersionTable.PSEdition -ne 'Core') { return $false }
    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) { return $false }

    try {
        $libraryRoot = Join-Path $ScriptDir 'lib\spectre'
        Add-Type -LiteralPath (Join-Path $libraryRoot 'Spectre.Console.Ansi.dll') -ErrorAction Stop
        Add-Type -LiteralPath (Join-Path $libraryRoot 'Spectre.Console.dll') -ErrorAction Stop
        $script:VrcSetupSpectreAvailable = $true
    } catch {
        $script:VrcSetupSpectreAvailable = $false
    }
    return [bool]$script:VrcSetupSpectreAvailable
}

function ConvertTo-VrcSetupSpectreText {
    param([AllowEmptyString()][string]$Text)
    if ($null -eq $Text) { return '' }
    return [Spectre.Console.Markup]::Escape($Text)
}

function Get-VrcSetupSpectreScriptDir {
    # This file lives under setup-scripts\lib.  Deriving the runtime root keeps
    # the interactive UI portable when the downloaded source is moved or when
    # the installed copy is used through its Start menu shortcut.
    return [System.IO.Directory]::GetParent($PSScriptRoot).FullName
}

function New-VrcSetupSpectreStyle {
    param(
        [Parameter(Mandatory)][string]$Foreground,
        [string]$Background
    )

    $foregroundColor = [Spectre.Console.Color]::FromHex($Foreground)
    $backgroundColor = if ([string]::IsNullOrWhiteSpace($Background)) {
        $null
    } else {
        [Spectre.Console.Color]::FromHex($Background)
    }
    return [Spectre.Console.Style]::new($foregroundColor, $backgroundColor, $null)
}

function ConvertTo-VrcSetupSpectreChoice {
    param([AllowEmptyString()][string]$Text)

    $safe = ConvertTo-VrcSetupSpectreText $Text
    if ($Text -match '^Required\s+·\s+(.*)$') {
        return "[#F6C451]Required[/]  [#78909F]·[/]  [#DDEAF2]$($matches[1] | ForEach-Object { ConvertTo-VrcSetupSpectreText $_ })[/]"
    }
    if ($Text -match '^Optional\s+·\s+(.*)$') {
        return "[#A8B6C1]Optional[/]  [#78909F]·[/]  [#DDEAF2]$($matches[1] | ForEach-Object { ConvertTo-VrcSetupSpectreText $_ })[/]"
    }
    if ($Text -match '^(Back|Cancel)$') { return "[#78909F]$safe[/]" }
    if ($Text -match '^(Exit|Reset|Delete|Remove|Clean up)') { return "[#FB7185]$safe[/]" }
    if ($Text -match '^(\+\s+)?(Create|Manage|Add|Apply|Use|Include|Start|Open|Search|Refresh|Choose|Change|Set|Go|Enter)') { return "[#75D7F7]$safe[/]" }
    return "[#DDEAF2]$safe[/]"
}

function Write-VrcSetupSpectreFrame {
    param(
        [Parameter(Mandatory)][string]$Title,
        [AllowEmptyString()][string]$Header,
        [Parameter(Mandatory)][string]$ScriptDir
    )

    if (-not (Initialize-VrcSetupSpectre -ScriptDir $ScriptDir)) { return $false }

    [Spectre.Console.AnsiConsole]::Clear()
    $rule = [Spectre.Console.Rule]::new("[#6BD5FF]$(ConvertTo-VrcSetupSpectreText $Title)[/]")
    $rule.Justification = [Spectre.Console.Justify]::Left
    $rule.Style = New-VrcSetupSpectreStyle -Foreground '#4C6A7A'
    [Spectre.Console.AnsiConsole]::Write($rule)

    if (-not [string]::IsNullOrWhiteSpace($Header)) {
        [Spectre.Console.AnsiConsole]::WriteLine()
        $body = [Spectre.Console.Markup]::new("[#B7C7D3]$(ConvertTo-VrcSetupSpectreText $Header)[/]")
        $panel = [Spectre.Console.Panel]::new($body)
        $panel.Border = [Spectre.Console.BoxBorder]::Rounded
        $panel.BorderStyle = New-VrcSetupSpectreStyle -Foreground '#4C6A7A'
        $panel.Padding = [Spectre.Console.Padding]::new(1, 0, 1, 0)
        $panel.Expand = $false
        [Spectre.Console.AnsiConsole]::Write($panel)
    }

    [Spectre.Console.AnsiConsole]::WriteLine()
    return $true
}


function Get-VrcSetupAdjacentPackageVersion {
    param(
        [Parameter(Mandatory)]$AvailableVersions,
        [AllowEmptyString()][string]$CurrentVersion,
        [ValidateSet('Older', 'Newer')][string]$Direction
    )

    $versions = @($AvailableVersions | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Unique)
    if ($versions.Count -eq 0) { return $null }

    $currentIndex = [array]::IndexOf($versions, $CurrentVersion)
    if ($CurrentVersion -eq 'latest') {
        if ($Direction -eq 'Newer') {
            return [pscustomobject]@{ Version = 'latest'; AtLimit = $true }
        }
        $targetIndex = 0
    } elseif ($currentIndex -lt 0) {
        $targetIndex = 0
    } elseif ($Direction -eq 'Older') {
        $targetIndex = [Math]::Min($versions.Count - 1, $currentIndex + 1)
    } else {
        if ($currentIndex -eq 0) {
            return [pscustomobject]@{ Version = 'latest'; AtLimit = $false }
        }
        $targetIndex = [Math]::Max(0, $currentIndex - 1)
    }

    return [pscustomobject]@{
        Version = [string]$versions[$targetIndex]
        AtLimit = ($currentIndex -eq $targetIndex)
    }
}

function Set-VrcSetupWorkspaceItemVersion {
    param(
        [Parameter(Mandatory)]$Items,
        [Parameter(Mandatory)][string]$PackageName,
        [Parameter(Mandatory)][string]$Version
    )

    $sdkNames = @('com.vrchat.base', 'com.vrchat.avatars', 'com.vrchat.worlds')
    $targets = if ($sdkNames -contains $PackageName) { $sdkNames } else { @($PackageName) }
    $changed = @()
    foreach ($item in @($Items | Where-Object { $targets -contains [string]$_.Name })) {
        $item.DesiredVersion = $Version
        $item.Status = if (-not $item.CurrentVersion) {
            'Add'
        } elseif ([string]$item.CurrentVersion -ne $Version) {
            'Update'
        } elseif ($item.Required) {
            'Required'
        } else {
            'Installed'
        }
        $changed += [string]$item.Name
    }
    return @($changed)
}

function Get-VrcSetupWorkspacePendingCount {
    param([Parameter(Mandatory)]$Items)

    $sdkNames = @('com.vrchat.base', 'com.vrchat.avatars', 'com.vrchat.worlds')
    $changedItems = @($Items | Where-Object { $_.Status -in @('Add', 'Update', 'Remove') })
    $sdkChanged = @($changedItems | Where-Object { $sdkNames -contains [string]$_.Name }).Count -gt 0
    $otherCount = @($changedItems | Where-Object { $sdkNames -notcontains [string]$_.Name }).Count
    return ($otherCount + $(if ($sdkChanged) { 1 } else { 0 }))
}

function New-VrcSetupPackageWorkspaceRenderable {
    param(
        [Parameter(Mandatory)]$Items,
        [Parameter(Mandatory)][int]$SelectedIndex,
        [Parameter(Mandatory)][int]$PendingCount,
        [bool]$IncludeExtras = $false,
        [bool]$HasExtras = $false,
        [bool]$HasFavorites = $false,
        [AllowEmptyString()][string]$Notice = '',
        [ValidateRange(4, 20)][int]$MaxVisible = 10
    )

    $itemsArray = @($Items)
    $windowWidth = [Spectre.Console.AnsiConsole]::Profile.Width
    try { $windowWidth = [Console]::WindowWidth } catch { }
    if ($windowWidth -le 0) { $windowWidth = [Spectre.Console.AnsiConsole]::Profile.Width }
    $isNarrow = ($windowWidth -lt 100)
    $showPresence = $windowWidth -ge 68 -and $itemsArray.Count -gt 0 -and $null -ne $itemsArray[0].PSObject.Properties['Presence']
    $versionWidth = if ($windowWidth -lt 68) { 12 } elseif ($isNarrow) { 20 } else { 30 }
    $outcomeWidth = if ($windowWidth -lt 68) { 10 } elseif ($isNarrow) { 12 } else { 18 }
    $packageWidth = [Math]::Max(12,[Math]::Min(40,$windowWidth - $versionWidth - $outcomeWidth - $(if ($showPresence) { 21 } else { 10 })))
    $MaxVisible = [Math]::Max(4,[Math]::Min($MaxVisible,(Get-VrcSetupViewport).Height - 16))
    $table = [Spectre.Console.Table]::new()
    $table.Border = [Spectre.Console.TableBorder]::Rounded
    $table.BorderStyle = New-VrcSetupSpectreStyle -Foreground '#4C6A7A'
    $table.ShowHeaders = $true
    $table.ShowRowSeparators = $false
    $table.Expand = $false

    foreach ($columnInfo in @(
        @{ Label = 'Package'; Width = $packageWidth },
        @{ Label = 'Version'; Width = $versionWidth },
        @{ Label = 'After saving'; Width = $outcomeWidth }
    )) {
        $column = [Spectre.Console.TableColumn]::new("[#78909F]$($columnInfo.Label)[/]")
        $column.Width = [int]$columnInfo.Width
        $column.NoWrap = $true
        [void]$table.AddColumn($column)
    }
    if ($showPresence) { $column = [Spectre.Console.TableColumn]::new('[#78909F]Projects[/]'); $column.Width = 8; $column.NoWrap = $true; [void]$table.AddColumn($column) }

    $offset = 0
    if ($itemsArray.Count -gt $MaxVisible) {
        if ($SelectedIndex -ge $itemsArray.Count) {
            $offset = $itemsArray.Count - $MaxVisible
        } else {
            $offset = [Math]::Floor($SelectedIndex / $MaxVisible) * $MaxVisible
        }
    }
    $end = [Math]::Min($itemsArray.Count - 1, $offset + $MaxVisible - 1)
    for ($index = $offset; $index -le $end; $index++) {
        $item = $itemsArray[$index]
        $isSelected = ($SelectedIndex -eq $index)
        $installed = if ([string]::IsNullOrWhiteSpace([string]$item.CurrentVersion)) { '' } else { [string]$item.CurrentVersion }
        $selectedVersion = if ([string]::IsNullOrWhiteSpace([string]$item.DesiredVersion)) { '' } elseif ([string]$item.DesiredVersion -eq 'latest') { 'Newest' } else { [string]$item.DesiredVersion }
        $versionLabel = if ($item.Status -eq 'Update') {
            "${installed} -> ${selectedVersion}"
        } elseif ($item.Status -eq 'Add') {
            $selectedVersion
        } elseif ($item.Status -eq 'Available') {
            [string]$item.SuggestedVersion
        } else {
            $installed
        }
        $stateLabel = switch ([string]$item.Status) {
            'Add' { $(if ($isNarrow) { 'To add' } else { 'Pending add' }); break }
            'Update' { $(if ($isNarrow) { 'To update' } else { 'Pending update' }); break }
            'Remove' { $(if ($isNarrow) { 'To remove' } else { 'Pending removal' }); break }
            'Required' { $(if ($isNarrow) { 'Always' } else { 'Always included' }); break }
            'Available' { 'Saved'; break }
            'Mixed' { 'Keep varied'; break }
            default { 'Installed' }
        }
        if ($item.Scope -and $item.Status -in @('Add','Update','Remove')) { $stateLabel += ' (' + ($item.Scope -replace ' projects$','') + ')' }
        $toggle = if ($item.Required) { '[*]' } elseif (-not $item.DesiredVersion) { '[ ]' } elseif ($item.Status -eq 'Mixed') { '[-]' } else { '[x]' }
        $packageText = ('{0}{1} {2}' -f $(if ($isSelected) { '> ' } else { '  ' }), $toggle, [string]$item.FriendlyName)
        $rawCells = @(
            (Format-VrcSetupPackageCell -Text $packageText -Width $packageWidth),
            (Format-VrcSetupPackageCell -Text $versionLabel -Width $versionWidth),
            (Format-VrcSetupPackageCell -Text $stateLabel -Width $outcomeWidth)
        )
        if ($showPresence) { $rawCells += Format-VrcSetupPackageCell -Text ([string]$item.Presence) -Width 8 }
        $cells = @()
        for ($cellIndex = 0; $cellIndex -lt $rawCells.Count; $cellIndex++) {
            $safeText = ConvertTo-VrcSetupSpectreText ([string]$rawCells[$cellIndex])
            if ($isSelected) {
                $markup = "[black on #75D7F7]${safeText}[/]"
            } elseif ($cellIndex -eq 2 -and $item.Status -eq 'Add') {
                $markup = "[#67E8A5]${safeText}[/]"
            } elseif ($cellIndex -eq 2 -and $item.Status -eq 'Update') {
                $markup = "[#F6C451]${safeText}[/]"
            } elseif ($cellIndex -eq 2 -and $item.Status -eq 'Remove') {
                $markup = "[#FB7185]${safeText}[/]"
            } elseif ($cellIndex -eq 2 -and $item.Status -eq 'Required') {
                $markup = "[#78909F]${safeText}[/]"
            } else {
                $markup = "[#DDEAF2]${safeText}[/]"
            }
            $cells += [Spectre.Console.Markup]::new($markup)
        }
        [void]$table.Rows.Add([Spectre.Console.Rendering.IRenderable[]]$cells)
    }

    $renderables = @($table)
    if ($itemsArray.Count -gt $MaxVisible) {
        $rangeEnd = [Math]::Min($itemsArray.Count, $offset + $MaxVisible)
        $renderables += [Spectre.Console.Markup]::new("[#78909F]  Showing $($offset + 1)-${rangeEnd} of $($itemsArray.Count) packages[/]")
    }
    $renderables += [Spectre.Console.Text]::new(' ')

    $saveSelected = ($SelectedIndex -eq $itemsArray.Count)
    $canSave = ($PendingCount -gt 0 -or $IncludeExtras)
    $saveText = if ($canSave) {
        if ($PendingCount -eq 1) { 'Apply 1 change' } elseif ($PendingCount -gt 1) { "Apply ${PendingCount} changes" } else { 'Apply changes' }
    } else {
        'No changes to save'
    }
    $buttonLabel = "$(if ($saveSelected) { '> ' })${saveText}"
    $savePanelWidth = [Math]::Min(90, [Math]::Max(30, $windowWidth - 4))
    $buttonWidth = $savePanelWidth - 6
    $buttonLabel = $buttonLabel.PadLeft([Math]::Floor(($buttonWidth + $buttonLabel.Length) / 2)).PadRight($buttonWidth)
    if ($saveSelected -and $canSave) {
        $saveMarkup = "[black on #67E8A5]$(ConvertTo-VrcSetupSpectreText $buttonLabel)[/]"
    } elseif ($canSave) {
        $saveMarkup = "[bold #67E8A5]$(ConvertTo-VrcSetupSpectreText $buttonLabel)[/]"
    } else {
        $saveMarkup = "[#607D8B]$(ConvertTo-VrcSetupSpectreText $buttonLabel)[/]"
    }
    $savePanel = [Spectre.Console.Panel]::new([Spectre.Console.Markup]::new($saveMarkup))
    $savePanel.Border = [Spectre.Console.BoxBorder]::Rounded
    $savePanel.BorderStyle = New-VrcSetupSpectreStyle -Foreground $(if ($canSave) { '#67E8A5' } else { '#4C6A7A' })
    $savePanel.Width = $savePanelWidth
    $savePanel.Padding = [Spectre.Console.Padding]::new(1, 0)
    $renderables += $savePanel
    $buttons = [Spectre.Console.Table]::new(); $buttons.Border = [Spectre.Console.TableBorder]::None; $buttons.ShowHeaders = $false
    foreach ($label in @('Add package','Change preset','Copy project','Options')) { [void]$buttons.AddColumn($label) }
    $cells = @(); $buttonIndex = 0
    foreach ($label in @('Add package','Change preset','Copy project','Options')) {
        $focused = $SelectedIndex -eq ($itemsArray.Count + 1 + $buttonIndex)
        $color = if ($buttonIndex -eq 1) { '#C4A5FF' } else { '#75D7F7' }
        $style = if ($focused) { "bold black on $color" } else { $color }
        $panel = [Spectre.Console.Panel]::new([Spectre.Console.Markup]::new("[$style]$(if ($focused) { '> ' })$label[/]"))
        $panel.Border = [Spectre.Console.BoxBorder]::Rounded; $panel.BorderStyle = New-VrcSetupSpectreStyle -Foreground $color
        $cells += $panel; $buttonIndex++
    }
    [void]$buttons.Rows.Add([Spectre.Console.Rendering.IRenderable[]]$cells); $renderables += $buttons
    $renderables += [Spectre.Console.Markup]::new('[#78909F]Space Toggle  |  Enter Version/select  |  Tab Actions  |  Esc Back[/]')
    if (-not [string]::IsNullOrWhiteSpace($Notice)) {
        $renderables += [Spectre.Console.Text]::new(' ')
        $renderables += [Spectre.Console.Markup]::new("[#75D7F7]$(ConvertTo-VrcSetupSpectreText $Notice)[/]")
    }
    return [Spectre.Console.Rows]::new([Spectre.Console.Rendering.IRenderable[]]$renderables)
}

function Show-VrcSetupSpectrePackageWorkspace {
    param(
        [Parameter(Mandatory)]$Items,
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][int]$PendingCount,
        [int]$InitialSelectedIndex = 0,
        [string]$Notice = '',
        [bool]$IncludeExtras = $false,
        [int]$ExtrasCount = 0,
        [bool]$HasFavorites = $false,
        [scriptblock]$VersionProvider,
        [string]$ScriptDir = (Get-VrcSetupSpectreScriptDir)
    )

    if (-not (Initialize-VrcSetupSpectre -ScriptDir $ScriptDir)) { return $null }
    $itemsArray = @($Items)
    if ($itemsArray.Count -eq 0) { return $null }
    $initialIndex = [Math]::Max(0, [Math]::Min($InitialSelectedIndex, $itemsArray.Count))
    $headerText = if ($IncludeExtras -and $ExtrasCount -gt 0) { "${ProjectName}`nSaved imports included: ${ExtrasCount}" } else { $ProjectName }
    if (-not (Write-VrcSetupSpectreFrame -Title 'Project packages' -Header $headerText -ScriptDir $ScriptDir)) { return $null }

    $state = [pscustomobject]@{
        SelectedIndex = $initialIndex
        Done = $false
        Action = $null
        PackageName = $null
        VersionChanges = [ordered]@{}
        Notice = $Notice
    }
    $hasExtras = ($ExtrasCount -gt 0)
    $initial = New-VrcSetupPackageWorkspaceRenderable -Items $itemsArray -SelectedIndex $initialIndex -PendingCount $PendingCount -IncludeExtras:$IncludeExtras -HasExtras:$hasExtras -HasFavorites:$HasFavorites -Notice $state.Notice
    $live = [Spectre.Console.AnsiConsole]::Live($initial)
    $live.AutoClear = $true
    $cursorWasVisible = $true
    try {
        try { $cursorWasVisible = [Console]::CursorVisible; [Console]::CursorVisible = $false } catch { }
        $live.Start([System.Action[Spectre.Console.LiveDisplayContext]]{
            param($context)
            $context.Refresh()
            while (-not $state.Done) {
                $key = [Console]::ReadKey($true)
                $refresh = $false
                switch ($key.Key) {
                    'F2' { $state.Action = 'options'; $state.Done = $true }
                    'UpArrow' {
                        if ($state.SelectedIndex -gt 0) { $state.SelectedIndex--; $refresh = $true }
                    }
                    'DownArrow' {
                        if ($state.SelectedIndex -lt $itemsArray.Count + 4) { $state.SelectedIndex++; $refresh = $true }
                    }
                    'Tab' { $state.SelectedIndex = if ($state.SelectedIndex -lt $itemsArray.Count -or $state.SelectedIndex -ge $itemsArray.Count + 4) { $itemsArray.Count } else { $state.SelectedIndex + 1 }; $refresh = $true }
                    { $_ -in @('LeftArrow', 'RightArrow') } {
                        if ($state.SelectedIndex -lt $itemsArray.Count -and $VersionProvider) {
                            $item = $itemsArray[$state.SelectedIndex]
                            $state.Notice = "Loading versions for $($item.FriendlyName)..."
                            $loading = New-VrcSetupPackageWorkspaceRenderable -Items $itemsArray -SelectedIndex $state.SelectedIndex -PendingCount $PendingCount -IncludeExtras:$IncludeExtras -HasExtras:$hasExtras -HasFavorites:$HasFavorites -Notice $state.Notice
                            $context.UpdateTarget($loading)
                            $context.Refresh()
                            $versions = @()
                            try { $versions = @(& $VersionProvider ([string]$item.Name)) } catch { $versions = @() }
                            if ([string]$item.DesiredVersion -notmatch '-') { $versions = @($versions | Where-Object { $_ -notmatch '-' }) }
                            $direction = if ($key.Key -eq 'LeftArrow') { 'Older' } else { 'Newer' }
                            $adjacent = Get-VrcSetupAdjacentPackageVersion -AvailableVersions $versions -CurrentVersion ([string]$item.DesiredVersion) -Direction $direction
                            if ($adjacent) {
                                $changedPackages = @(Set-VrcSetupWorkspaceItemVersion -Items $itemsArray -PackageName ([string]$item.Name) -Version ([string]$adjacent.Version))
                                foreach ($changedPackage in $changedPackages) {
                                    $state.VersionChanges[$changedPackage] = [string]$adjacent.Version
                                }
                                $PendingCount = Get-VrcSetupWorkspacePendingCount -Items $itemsArray
                                $linkedLabel = if ($changedPackages.Count -gt 1) { 'VRChat SDK' } else { [string]$item.FriendlyName }
                                $state.Notice = if ($adjacent.AtLimit) { "No more $($direction.ToLowerInvariant()) versions." } else { "${linkedLabel}: $($adjacent.Version)" }
                            } else {
                                $state.Notice = 'No version list is available. Press V to enter one manually.'
                            }
                            $refresh = $true
                        }
                    }
                    'Home' { $state.SelectedIndex = 0; $refresh = $true }
                    'End' { $state.SelectedIndex = $itemsArray.Count; $refresh = $true }
                    'Escape' { $state.Action = 'back'; $state.Done = $true }
                    'Enter' {
                        if ($state.SelectedIndex -eq $itemsArray.Count) {
                            if ($PendingCount -gt 0 -or $IncludeExtras) {
                                $state.Action = 'save'
                            } else {
                                $state.Notice = 'Nothing to save yet.'
                                $refresh = $true
                            }
                        } elseif ($state.SelectedIndex -gt $itemsArray.Count) {
                            $state.Action = @('add','presets','copy','options')[$state.SelectedIndex - $itemsArray.Count - 1]
                        } else {
                            $state.Action = 'version'
                            $state.PackageName = [string]$itemsArray[$state.SelectedIndex].Name
                        }
                        $state.Done = (-not [string]::IsNullOrWhiteSpace([string]$state.Action))
                    }
                    'Spacebar' {
                        if ($state.SelectedIndex -lt $itemsArray.Count -and -not $itemsArray[$state.SelectedIndex].Required) {
                            $state.Action = 'toggle'
                            $state.PackageName = [string]$itemsArray[$state.SelectedIndex].Name
                            $state.Done = $true
                        } elseif ($state.SelectedIndex -lt $itemsArray.Count) {
                            $state.Notice = 'This package is always included in this project.'
                            $refresh = $true
                        }
                    }
                    default {
                        switch ([char]::ToUpperInvariant($key.KeyChar)) {
                            'A' { $state.Action = 'add'; $state.Done = $true }
                            'F' { $state.Action = 'favorites'; $state.Done = $true }
                            'D' { $state.Action = 'defaults'; $state.Done = $true }
                            'P' { $state.Action = 'presets'; $state.Done = $true }
                            'I' { if ($hasExtras) { $state.Action = 'extras'; $state.Done = $true } }
                            'S' {
                                if ($PendingCount -gt 0 -or $IncludeExtras) {
                                    $state.Action = 'save'; $state.Done = $true
                                } else {
                                    $state.Notice = 'Nothing to save yet.'; $refresh = $true
                                }
                            }
                            'V' {
                                if ($state.SelectedIndex -lt $itemsArray.Count) {
                                    $state.Action = 'version'
                                    $state.PackageName = [string]$itemsArray[$state.SelectedIndex].Name
                                    $state.Done = $true
                                }
                            }
                        }
                    }
                }
                if ($refresh) {
                    $next = New-VrcSetupPackageWorkspaceRenderable -Items $itemsArray -SelectedIndex $state.SelectedIndex -PendingCount $PendingCount -IncludeExtras:$IncludeExtras -HasExtras:$hasExtras -HasFavorites:$HasFavorites -Notice $state.Notice
                    $context.UpdateTarget($next)
                    $context.Refresh()
                }
            }
        })
    } catch {
        return $null
    } finally {
        try { [Console]::CursorVisible = $cursorWasVisible } catch { }
    }

    if (-not $state.Action) { return $null }
    return [pscustomobject]@{
        Action = [string]$state.Action
        PackageName = [string]$state.PackageName
        VersionChanges = [pscustomobject]$state.VersionChanges
        SelectedIndex = [int]$state.SelectedIndex
    }
}

function New-VrcSetupSaveReviewRenderable {
    param(
        [string[]]$Added = @(),
        [string[]]$Updated = @(),
        [string[]]$Removed = @(),
        [int]$ExtrasCount = 0,
        [ValidateRange(0, 1)][int]$SelectedIndex = 0
    )

    $table = [Spectre.Console.Table]::new()
    $table.Border = [Spectre.Console.TableBorder]::Rounded
    $table.BorderStyle = New-VrcSetupSpectreStyle -Foreground '#4C6A7A'
    $table.ShowHeaders = $true
    $table.ShowRowSeparators = $false
    $table.Expand = $false
    $windowWidth = try { [Console]::WindowWidth } catch { [Spectre.Console.AnsiConsole]::Profile.Width }
    $isNarrow = ($windowWidth -lt 100)
    $changeColumn = [Spectre.Console.TableColumn]::new('[#78909F]Change[/]')
    $changeColumn.Width = 15
    $detailsColumn = [Spectre.Console.TableColumn]::new('[#78909F]Packages[/]')
    $detailsColumn.Width = if ($isNarrow) { 52 } else { 68 }
    [void]$table.AddColumn($changeColumn)
    [void]$table.AddColumn($detailsColumn)

    foreach ($row in @(
        @{ Label = 'Add'; Values = @($Added); Color = '#67E8A5' },
        @{ Label = 'Change version'; Values = @($Updated); Color = '#F6C451' },
        @{ Label = 'Remove'; Values = @($Removed); Color = '#FB7185' }
    )) {
        $details = if ($row.Values.Count -gt 0) { $row.Values -join ', ' } else { 'None' }
        [void]$table.Rows.Add([Spectre.Console.Rendering.IRenderable[]]@(
            [Spectre.Console.Markup]::new("[$($row.Color)]$(ConvertTo-VrcSetupSpectreText $row.Label)[/]"),
            [Spectre.Console.Markup]::new("[#DDEAF2]$(ConvertTo-VrcSetupSpectreText $details)[/]")
        ))
    }
    if ($ExtrasCount -gt 0) {
        [void]$table.Rows.Add([Spectre.Console.Rendering.IRenderable[]]@(
            [Spectre.Console.Markup]::new('[#75D7F7]Saved imports[/]'),
            [Spectre.Console.Markup]::new("[#DDEAF2]${ExtrasCount} UnityPackage$(if ($ExtrasCount -eq 1) { '' } else { 's' })[/]")
        ))
    }

    $changeCount = @($Added).Count + @($Updated).Count + @($Removed).Count
    if ($ExtrasCount -gt 0) { $changeCount++ }
    $saveText = if ($changeCount -eq 1) { 'Save 1 change' } else { "Save ${changeCount} changes" }
    $buttonWidth = if ($isNarrow) { 66 } else { 82 }
    $buttonLabel = "ENTER   ${saveText}"
    $buttonLabel = $buttonLabel.PadLeft([Math]::Floor(($buttonWidth + $buttonLabel.Length) / 2)).PadRight($buttonWidth)
    $saveMarkup = if ($SelectedIndex -eq 0) {
        "[black on #67E8A5]$(ConvertTo-VrcSetupSpectreText $buttonLabel)[/]"
    } else {
        "[bold #67E8A5]$(ConvertTo-VrcSetupSpectreText $buttonLabel)[/]"
    }
    $savePanel = [Spectre.Console.Panel]::new([Spectre.Console.Markup]::new($saveMarkup))
    $savePanel.Border = [Spectre.Console.BoxBorder]::Rounded
    $savePanel.BorderStyle = New-VrcSetupSpectreStyle -Foreground '#67E8A5'
    $savePanel.Width = if ($isNarrow) { 72 } else { 88 }
    $savePanel.Padding = [Spectre.Console.Padding]::new(1, 0)

    $backMarkup = if ($SelectedIndex -eq 1) { '[black on #75D7F7]  Back  [/]' } else { '[#78909F]  Back[/]' }
    return [Spectre.Console.Rows]::new([Spectre.Console.Rendering.IRenderable[]]@(
        $table,
        [Spectre.Console.Text]::new(' '),
        [Spectre.Console.Text]::new(' '),
        $savePanel,
        [Spectre.Console.Text]::new(' '),
        [Spectre.Console.Markup]::new($backMarkup),
        [Spectre.Console.Text]::new(' '),
        [Spectre.Console.Markup]::new('[#78909F]Up/Down Choose     Enter Confirm     Esc Back[/]')
    ))
}

function Show-VrcSetupSpectreSaveReview {
    param(
        [Parameter(Mandatory)][string]$ProjectName,
        [string[]]$Added = @(),
        [string[]]$Updated = @(),
        [string[]]$Removed = @(),
        [int]$ExtrasCount = 0,
        [string]$ScriptDir = (Get-VrcSetupSpectreScriptDir)
    )

    if (-not (Write-VrcSetupSpectreFrame -Title 'Review changes' -Header $ProjectName -ScriptDir $ScriptDir)) { return $null }
    $state = [pscustomobject]@{ SelectedIndex = 0; Done = $false; Confirmed = $false }
    $initial = New-VrcSetupSaveReviewRenderable -Added $Added -Updated $Updated -Removed $Removed -ExtrasCount $ExtrasCount -SelectedIndex 0
    $live = [Spectre.Console.AnsiConsole]::Live($initial)
    $live.AutoClear = $true
    $cursorWasVisible = $true
    try {
        try { $cursorWasVisible = [Console]::CursorVisible; [Console]::CursorVisible = $false } catch { }
        $live.Start([System.Action[Spectre.Console.LiveDisplayContext]]{
            param($context)
            $context.Refresh()
            while (-not $state.Done) {
                $key = [Console]::ReadKey($true)
                $refresh = $false
                switch ($key.Key) {
                    'UpArrow' { if ($state.SelectedIndex -ne 0) { $state.SelectedIndex = 0; $refresh = $true } }
                    'DownArrow' { if ($state.SelectedIndex -ne 1) { $state.SelectedIndex = 1; $refresh = $true } }
                    'Escape' { $state.Done = $true; $state.Confirmed = $false }
                    'Enter' { $state.Done = $true; $state.Confirmed = ($state.SelectedIndex -eq 0) }
                }
                if ($refresh) {
                    $next = New-VrcSetupSaveReviewRenderable -Added $Added -Updated $Updated -Removed $Removed -ExtrasCount $ExtrasCount -SelectedIndex $state.SelectedIndex
                    $context.UpdateTarget($next)
                    $context.Refresh()
                }
            }
        })
    } catch {
        return $null
    } finally {
        try { [Console]::CursorVisible = $cursorWasVisible } catch { }
    }
    return [bool]$state.Confirmed
}

function New-VrcSetupProgressRenderable {
    param(
        [string[]]$Lines = @(),
        [int]$ScrollIndex = 0,
        [ValidateRange(3, 20)][int]$MaxVisible = 12,
        [ValidateSet('Running', 'Success', 'Failed', 'Cancelled')][string]$Status = 'Running',
        [string]$CompletionLabel = 'Done',
        [bool]$Follow = $true,
        [TimeSpan]$Elapsed = [TimeSpan]::Zero,
        [AllowEmptyString()][string]$LogFile = '',
        [AllowEmptyString()][string]$Metrics = ''
    )

    $windowWidth = try { [Console]::WindowWidth } catch { 0 }
    if ($windowWidth -le 0) { $windowWidth = [Spectre.Console.AnsiConsole]::Profile.Width }
    $panelWidth = [Math]::Min(200, [Math]::Max(24, $windowWidth - 4))
    $innerWidth = [Math]::Max(20, $panelWidth - 4)

    $items = @($Lines)
    $maxStart = [Math]::Max(0, $items.Count - $MaxVisible)
    $start = [Math]::Max(0, [Math]::Min($ScrollIndex, $maxStart))
    $visible = @(if ($items.Count -eq 0) { 'Starting...' } else { $items | Select-Object -Skip $start -First $MaxVisible })
    while ($visible.Count -lt $MaxVisible) { $visible += '' }
    $safeLines = @($visible | ForEach-Object {
        $line = ([string]$_ -replace '[\x00-\x1F\x7F]', ' ').Trim()
        if ($line.Length -gt $innerWidth) { $line = $line.Substring(0, $innerWidth - 1) + '…' }
        ConvertTo-VrcSetupSpectreText $line
    })

    $statusColor = switch ($Status) {
        'Success' { '#67E8A5' }
        'Failed' { '#FB7185' }
        'Cancelled' { '#F6C451' }
        default { '#75D7F7' }
    }
    $rangeEnd = if ($items.Count -eq 0) { 0 } else { [Math]::Min($items.Count, $start + $MaxVisible) }
    $rangeStart = if ($items.Count -eq 0) { 0 } else { $start + 1 }
    $followLabel = if ($Follow) { 'Following latest' } else { 'Paused for review' }
    $elapsedText = '{0:mm\:ss}' -f $Elapsed
    $summaryDetails = @($elapsedText)
    if (-not $Follow) { $summaryDetails += "Viewing messages ${rangeStart}-${rangeEnd} of $($items.Count)" }
    $summaryDetails += $followLabel
    $statusLabel = switch ($Status) { 'Success' { 'Completed' } 'Failed' { 'Operation failed' } 'Cancelled' { 'Cancelled' } default { 'In progress' } }
    $summary = "[bold $statusColor]${statusLabel}[/]  [#78909F]$($summaryDetails -join '  ·  ')[/]"
    if (-not [string]::IsNullOrWhiteSpace($Metrics)) {
        # Pinned live status: CPU/RAM refresh in place on this fixed colored line and
        # never add a row to the log box below.
        $summary += "  [#67E8A5]$(ConvertTo-VrcSetupSpectreText $Metrics)[/]"
    }

    $logBody = [Spectre.Console.Markup]::new("[#DDEAF2]$($safeLines -join "`n")[/]")
    $logPanel = [Spectre.Console.Panel]::new($logBody)
    $logPanel.Header = [Spectre.Console.PanelHeader]::new(' Progress ')
    $logPanel.Border = [Spectre.Console.BoxBorder]::Rounded
    $logPanel.BorderStyle = New-VrcSetupSpectreStyle -Foreground '#4C6A7A'
    $logPanel.Width = $panelWidth
    $logPanel.Height = $MaxVisible + 2
    $logPanel.Padding = [Spectre.Console.Padding]::new(1, 0)

    $hints = if ($Status -eq 'Running') {
        'Up/Down Scroll  ·  PgUp/PgDn Page  ·  Home First  ·  End Follow'
    } else {
        'Review log   Up/Down  ·  PgUp/PgDn  ·  Home First  ·  End Last'
    }
    $renderables = @(
        [Spectre.Console.Markup]::new($summary),
        [Spectre.Console.Text]::new(' '),
        $logPanel,
        [Spectre.Console.Text]::new(' '),
        [Spectre.Console.Markup]::new("[#78909F]${hints}[/]")
    )
    $label = switch ($Status) { 'Running' { 'Please wait' } 'Failed' { 'Continue to recovery' } 'Cancelled' { 'Back' } default { $CompletionLabel } }
    $renderables += [Spectre.Console.Text]::new(' ')
    if ($Status -eq 'Running') {
        $waiting = [Spectre.Console.Panel]::new([Spectre.Console.Markup]::new('[#78909F] Please wait… [/]'))
        $waiting.Border = [Spectre.Console.BoxBorder]::Rounded
        $waiting.BorderStyle = New-VrcSetupSpectreStyle -Foreground '#4C6A7A'
        $waiting.Padding = [Spectre.Console.Padding]::new(1,0)
        $renderables += $waiting
    } else { $renderables += New-VrcSetupActionButton -Label $label -Color $statusColor }
    $logPathLine = if ($LogFile) { "Full log: ${LogFile}" } else { ' ' }
    if ($logPathLine.Length -gt ($windowWidth - 2)) { $logPathLine = $logPathLine.Substring(0,[Math]::Max(1,$windowWidth - 3)) + '…' }
    $renderables += [Spectre.Console.Markup]::new("[#607D8B]$(ConvertTo-VrcSetupSpectreText $logPathLine)[/]")
    return [Spectre.Console.Rows]::new([Spectre.Console.Rendering.IRenderable[]]$renderables)
}

function Invoke-VrcSetupSpectreOperation {
    param(
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][scriptblock]$Operation,
        [object[]]$ArgumentList = @(),
        [ValidateRange(5, 20)][int]$MaxVisible = 12,
        [string]$ScriptDir = (Get-VrcSetupSpectreScriptDir)
    )

    if (-not (Get-Command Start-ThreadJob -ErrorAction SilentlyContinue)) { return $null }
    if (-not (Write-VrcSetupSpectreFrame -Title $Title -Header $Header -ScriptDir $ScriptDir)) { return $null }

    $effectiveMaxVisible = $MaxVisible
    try {
        $availableRows = [Math]::Max(3, [Console]::WindowHeight - 21)
        $effectiveMaxVisible = [Math]::Min($MaxVisible, $availableRows)
    } catch { }

    $job = Start-ThreadJob -ScriptBlock $Operation -ArgumentList $ArgumentList
    $state = [pscustomobject]@{
        Lines = [System.Collections.Generic.List[string]]::new()
        ScrollIndex = 0
        Follow = $true
        Done = $false
        Exit = $false
        Status = 'Running'
        ResultStatus = 1
        LogFile = ''
        Metrics = ''
        OutputIndex = 0
        InformationIndex = 0
        ErrorIndex = 0
        WarningIndex = 0
    }
    $started = Get-Date
    $initial = New-VrcSetupProgressRenderable -Lines @() -ScrollIndex 0 -MaxVisible $effectiveMaxVisible -Status Running -Follow:$true -Elapsed ([TimeSpan]::Zero) -Metrics $state.Metrics
    $live = [Spectre.Console.AnsiConsole]::Live($initial)
    $live.AutoClear = $true
    $cursorWasVisible = $true
    try {
        try { $cursorWasVisible = [Console]::CursorVisible; [Console]::CursorVisible = $false } catch { }
        $live.Start([System.Action[Spectre.Console.LiveDisplayContext]]{
            param($context)
            $context.Refresh()
            while (-not $state.Exit) {
                # Re-budget the log viewport when the terminal is resized.
                try { $effectiveMaxVisible = [Math]::Min($MaxVisible, [Math]::Max(3, [Console]::WindowHeight - 21)) } catch { }
                $entries = @()
                while ($state.OutputIndex -lt $job.Output.Count) {
                    $entries += $job.Output[$state.OutputIndex]
                    $state.OutputIndex++
                }
                while ($state.InformationIndex -lt $job.Information.Count) {
                    $entries += $job.Information[$state.InformationIndex]
                    $state.InformationIndex++
                }
                while ($state.ErrorIndex -lt $job.Error.Count) {
                    $entries += $job.Error[$state.ErrorIndex]
                    $state.ErrorIndex++
                }
                while ($state.WarningIndex -lt $job.Warning.Count) {
                    $entries += $job.Warning[$state.WarningIndex]
                    $state.WarningIndex++
                }
                foreach ($entry in @($entries)) {
                    if ($entry -and $entry.PSObject.Properties.Name -contains 'VrcSetupOperationResult') {
                        $state.ResultStatus = [int]$entry.Status
                        $state.LogFile = [string]$entry.LogFile
                        continue
                    }
                    $text = if ($entry -is [System.Management.Automation.InformationRecord]) { [string]$entry.MessageData } else { [string]$entry }
                    foreach ($line in @($text -split '\r?\n')) {
                        $clean = ($line -replace '[\x00-\x1F\x7F]', ' ').Trim()
                        if ([string]::IsNullOrWhiteSpace($clean)) { continue }
                        if ($clean.StartsWith('::VRCSETUP_METRICS::')) {
                            $state.Metrics = $clean.Substring('::VRCSETUP_METRICS::'.Length).Trim()
                            continue
                        }
                        [void]$state.Lines.Add($clean)
                    }
                }
                if ($state.Lines.Count -gt 1000) { $state.Lines.RemoveRange(0, $state.Lines.Count - 1000) }

                if ($job.State -in @('Completed', 'Failed', 'Stopped') -and -not $state.Done) {
                    $state.Done = $true
                    if ($job.State -eq 'Stopped' -or $state.ResultStatus -eq 2) { $state.Status = 'Cancelled' }
                    elseif ($job.State -eq 'Completed' -and $state.ResultStatus -eq 0) { $state.Status = 'Success' }
                    else { $state.Status = 'Failed' }
                    $state.Follow = $true
                }

                $maxStart = [Math]::Max(0, $state.Lines.Count - $effectiveMaxVisible)
                if ($state.Follow) { $state.ScrollIndex = $maxStart }
                try {
                    if ([Console]::KeyAvailable) {
                        $key = [Console]::ReadKey($true)
                        switch ($key.Key) {
                            'UpArrow' { $state.Follow = $false; $state.ScrollIndex = [Math]::Max(0, $state.ScrollIndex - 1) }
                            'DownArrow' {
                                $state.ScrollIndex = [Math]::Min($maxStart, $state.ScrollIndex + 1)
                                if ($state.ScrollIndex -ge $maxStart) { $state.Follow = $true }
                            }
                            'PageUp' { $state.Follow = $false; $state.ScrollIndex = [Math]::Max(0, $state.ScrollIndex - $effectiveMaxVisible) }
                            'PageDown' {
                                $state.ScrollIndex = [Math]::Min($maxStart, $state.ScrollIndex + $effectiveMaxVisible)
                                if ($state.ScrollIndex -ge $maxStart) { $state.Follow = $true }
                            }
                            'Home' { $state.Follow = $false; $state.ScrollIndex = 0 }
                            'End' { $state.Follow = $true; $state.ScrollIndex = $maxStart }
                            'Enter' { if ($state.Done) { $state.Exit = $true } }
                            'Escape' { if ($state.Done) { $state.Exit = $true } }
                        }
                    }
                } catch { }

                $completionLabel = if ($Title -eq 'Creating project') { 'Done - Back to projects' } else { 'Done - Back to project' }
                $next = New-VrcSetupProgressRenderable -Lines @($state.Lines) -ScrollIndex $state.ScrollIndex -MaxVisible $effectiveMaxVisible -Status $state.Status -CompletionLabel $completionLabel -Follow:$state.Follow -Elapsed ((Get-Date) - $started) -LogFile $state.LogFile -Metrics $state.Metrics
                $context.UpdateTarget($next)
                $context.Refresh()
                if (-not $state.Exit) { Start-Sleep -Milliseconds 120 }
            }
        })
    } finally {
        try { [Console]::CursorVisible = $cursorWasVisible } catch { }
        if ($job.State -in @('Running', 'NotStarted')) { Stop-Job -Job $job -ErrorAction SilentlyContinue }
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
    return [pscustomobject]@{ Status = [int]$state.ResultStatus; LogFile = [string]$state.LogFile }
}

function New-VrcSetupTextInputRenderable {
    param(
        [AllowEmptyString()][string]$Value,
        [Parameter(Mandatory)][string]$Prompt,
        [AllowEmptyString()][string]$Hint,
        [AllowEmptyString()][string]$Placeholder = 'Start typing...',
        [switch]$HideAction,
        [int]$Cursor = -1
    )

    $shownValue = if ([string]::IsNullOrEmpty($Value)) {
        "[#607D8B]$(ConvertTo-VrcSetupSpectreText $Placeholder)[/]"
    } else {
        $position = if ($Cursor -lt 0) { $Value.Length } else { [Math]::Min($Cursor, $Value.Length) }
        "[#DDEAF2]$(ConvertTo-VrcSetupSpectreText $Value.Substring(0,$position))[/][#75D7F7]▌[/][#DDEAF2]$(ConvertTo-VrcSetupSpectreText $Value.Substring($position))[/]"
    }
    $field = [Spectre.Console.Panel]::new([Spectre.Console.Markup]::new($shownValue))
    $field.Header = [Spectre.Console.PanelHeader]::new(" $(ConvertTo-VrcSetupSpectreText $Prompt) ")
    $field.Border = [Spectre.Console.BoxBorder]::Rounded
    $field.BorderStyle = New-VrcSetupSpectreStyle -Foreground '#75D7F7'
    $inputWidth = try { [Console]::WindowWidth } catch { 0 }
    if ($inputWidth -le 0) { $inputWidth = [Spectre.Console.AnsiConsole]::Profile.Width }
    $field.Width = [Math]::Min(88, [Math]::Max(20, $inputWidth - 4))
    $field.Padding = [Spectre.Console.Padding]::new(1, 0)

    $rows = @($field)
    $action = if ($Hint -match 'Enter\s+(Search|Save|Confirm|Select)') { $Matches[1] } else { 'Continue' }
    $rows += [Spectre.Console.Text]::new(' ')
    if (-not $HideAction) { $rows += New-VrcSetupActionButton -Label $action -Color '#75D7F7' }
    if (-not [string]::IsNullOrWhiteSpace($Hint)) {
        $rows += [Spectre.Console.Text]::new(' ')
        $secondaryHint = ($Hint -replace 'Enter\s+(Search|Save|Confirm|Select)\s*[·]?\s*', '').Trim()
        $rows += [Spectre.Console.Markup]::new("[#78909F]$(ConvertTo-VrcSetupSpectreText $secondaryHint)[/]")
    }
    return [Spectre.Console.Rows]::new([Spectre.Console.Rendering.IRenderable[]]$rows)
}

function Update-VrcSetupTextInputKey {
    param($State, $Key)
    switch ($Key.Key) {
        'Enter' { $State.Done = $true }
        'Escape' { $State.Cancelled = $true; $State.Done = $true }
        'LeftArrow' { $State.Cursor = [Math]::Max(0,$State.Cursor-1) }
        'RightArrow' { $State.Cursor = [Math]::Min($State.Value.Length,$State.Cursor+1) }
        'Home' { $State.Cursor = 0 }
        'End' { $State.Cursor = $State.Value.Length }
        'Delete' { if ($State.Cursor -lt $State.Value.Length) { $State.Value = $State.Value.Remove($State.Cursor,1) } }
        'Backspace' { if ($State.Cursor -gt 0) { $State.Value = $State.Value.Remove($State.Cursor-1,1); $State.Cursor-- } }
        default { if ($Key.KeyChar -and -not [char]::IsControl($Key.KeyChar)) { $State.Value = $State.Value.Insert($State.Cursor,[string]$Key.KeyChar); $State.Cursor++ } }
    }
}

function Read-VrcSetupSpectreTextInput {
    param(
        [Parameter(Mandatory)][string]$Title,
        [AllowEmptyString()][string]$Header,
        [Parameter(Mandatory)][string]$Prompt,
        [string]$Hint = 'Leave blank to go back.',
        [string]$Placeholder = 'Start typing...',
        [AllowEmptyString()][string]$InitialValue = '',
        [string]$ScriptDir = (Get-VrcSetupSpectreScriptDir)
    )

    $state = [pscustomobject]@{ Value = $InitialValue; Cursor = $InitialValue.Length; Done = $false; Cancelled = $false }
    if (-not (Write-VrcSetupSpectreFrame -Title $Title -Header $Header -ScriptDir $ScriptDir)) {
        Write-Host $Title -ForegroundColor Cyan
        if ([Console]::IsInputRedirected) { $value=Read-Host $Prompt; return $(if ($value) { $value } else { $InitialValue }) }
        Write-Host $Hint
        while (-not $state.Done) {
            $width=[Math]::Max(12,[Console]::WindowWidth - $Prompt.Length - 4)
            $offset=[Math]::Max(0,$state.Cursor - $width + 1)
            $shown=$state.Value.Substring($offset,[Math]::Min($width,$state.Value.Length-$offset))
            $shown=$shown.Insert([Math]::Min($shown.Length,$state.Cursor-$offset),'|')
            Write-Host ("`r${Prompt}: " + $shown.PadRight($width+1)) -NoNewline
            Update-VrcSetupTextInputKey -State $state -Key ([Console]::ReadKey($true))
        }
        Write-Host ''
        if ($state.Cancelled) { return $null }
        return $state.Value
    }
    $initial = New-VrcSetupTextInputRenderable -Value $state.Value -Cursor $state.Cursor -Prompt $Prompt -Hint $Hint -Placeholder $Placeholder
    $live = [Spectre.Console.AnsiConsole]::Live($initial)
    $live.AutoClear = $true
    $cursorWasVisible = $true
    try {
        try { $cursorWasVisible = [Console]::CursorVisible; [Console]::CursorVisible = $false } catch { }
        $live.Start([System.Action[Spectre.Console.LiveDisplayContext]]{
            param($context)
            $context.Refresh()
            while (-not $state.Done) {
                $key = [Console]::ReadKey($true)
                Update-VrcSetupTextInputKey -State $state -Key $key
                if (-not $state.Done) {
                    $next = New-VrcSetupTextInputRenderable -Value $state.Value -Cursor $state.Cursor -Prompt $Prompt -Hint $Hint -Placeholder $Placeholder
                    $context.UpdateTarget($next)
                    $context.Refresh()
                }
            }
        })
    } finally {
        try { [Console]::CursorVisible = $cursorWasVisible } catch { }
    }
    if ($state.Cancelled) { return $null }
    return [string]$state.Value
}

function Show-VrcSetupSpectreNotice {
    param(
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string]$Message,
        [AllowEmptyString()][string]$Details = '',
        [ValidateSet('Info', 'Warning', 'Error')][string]$Kind = 'Info',
        [string]$ScriptDir = (Get-VrcSetupSpectreScriptDir)
    )

    if (-not (Write-VrcSetupSpectreFrame -Title $Title -ScriptDir $ScriptDir)) { return $false }
    $color = switch ($Kind) { 'Error' { '#FB7185' } 'Warning' { '#F6C451' } default { '#75D7F7' } }
    $lines = @("[bold $color]$(ConvertTo-VrcSetupSpectreText $Message)[/]")
    if (-not [string]::IsNullOrWhiteSpace($Details)) {
        $safeDetails = @($Details -split '\r?\n' | Where-Object { $_ } | Select-Object -First 12 | ForEach-Object { ConvertTo-VrcSetupSpectreText ([string]$_) })
        if ($safeDetails.Count -gt 0) { $lines += ''; $lines += @($safeDetails | ForEach-Object { "[#78909F]$_[/]" }) }
    }
    $panel = [Spectre.Console.Panel]::new([Spectre.Console.Markup]::new($lines -join "`n"))
    $panel.Border = [Spectre.Console.BoxBorder]::Rounded
    $panel.BorderStyle = New-VrcSetupSpectreStyle -Foreground $color
    $panel.Width = [Math]::Min(88, [Math]::Max(48, $(try { [Console]::WindowWidth - 4 } catch { 72 })))
    $panel.Padding = [Spectre.Console.Padding]::new(1, 0)
    [Spectre.Console.AnsiConsole]::Write($panel)
    [Spectre.Console.AnsiConsole]::WriteLine()
    [Spectre.Console.AnsiConsole]::Write((New-VrcSetupActionButton -Label 'Continue' -Color $color))
    [Spectre.Console.AnsiConsole]::MarkupLine('[#78909F]Esc Back[/]')
    while ($true) {
        $key = [Console]::ReadKey($true)
        if ($key.Key -in @('Enter', 'Escape')) { break }
    }
    return $true
}

function Show-VrcSetupSpectreFilterMenu {
    param(
        [string]$Title = 'VRChat Project Setup',
        [string]$Header = '',
        [Parameter(Mandatory)][string[]]$Options,
        [string[]]$PinnedOptions = @(),
        [string]$Placeholder = 'Type to filter...',
        [bool]$AllowCancel = $true,
        [int]$MaxVisible = 15,
        [bool]$EnterReturnsFilterWhenNoMatch = $true,
        [bool]$ShowListMarkers = $false,
        [bool]$ReturnSelectionWithFilter = $false,
        [string]$ScriptDir = (Get-VrcSetupSpectreScriptDir)
    )

    if (-not $Options) { return $null }
    if (-not (Initialize-VrcSetupSpectre -ScriptDir $ScriptDir)) { return $null }

    $filter = Read-VrcSetupSpectreTextInput -Title $Title -Header $Header -Prompt $Placeholder -Hint 'Type a few words, then press Enter. Leave blank to browse.' -ScriptDir $ScriptDir
    if ($null -eq $filter) { return $null }
    $filter = [string]$filter
    $tokens = @($filter.Split(@(' ', "`t"), [System.StringSplitOptions]::RemoveEmptyEntries))
    $candidates = @($Options | Where-Object { $PinnedOptions -notcontains $_ })
    $matches = if ($tokens.Count -eq 0) {
        $candidates
    } else {
        @($candidates | Where-Object {
            $candidate = [string]$_
            (@($tokens | Where-Object { $candidate -notlike "*$_*" }).Count -eq 0)
        })
    }

    $selectionOptions = @($PinnedOptions) + @($matches)
    if ($selectionOptions.Count -eq 0 -and $EnterReturnsFilterWhenNoMatch -and -not [string]::IsNullOrWhiteSpace($filter)) {
        $selectionOptions = @($filter)
    }
    if ($selectionOptions.Count -eq 0) { return $null }

    $resultHeader = if ([string]::IsNullOrWhiteSpace($filter)) {
        'Select a result.'
    } else {
        "Matches for: ${filter}"
    }
    $selectedIndex = Show-VrcSetupSpectreMenu -Title $Title -Header $resultHeader -Options $selectionOptions -AllowCancel:$AllowCancel -ScriptDir $ScriptDir
    if ($selectedIndex -lt 0) { return $null }
    $selected = [string]$selectionOptions[$selectedIndex]
    if ($ReturnSelectionWithFilter) {
        return [pscustomobject]@{ Selection = $selected; Filter = $filter }
    }
    return $selected
}

function Invoke-VrcSetupSpectreStringPrompt {
    param([Parameter(Mandatory)]$Prompt)

    $method = [Spectre.Console.AnsiConsole].GetMethods() | Where-Object {
        $_.Name -eq 'Prompt' -and $_.IsGenericMethodDefinition -and $_.GetParameters().Count -eq 1
    } | Select-Object -First 1
    if (-not $method) { throw 'Spectre.Console prompt method was not found.' }
    $closedMethod = $method.MakeGenericMethod([string])
    return [string]$closedMethod.Invoke($null, @($Prompt))
}

function Invoke-VrcSetupSpectreListPrompt {
    param([Parameter(Mandatory)]$Prompt)

    $method = [Spectre.Console.AnsiConsole].GetMethods() | Where-Object {
        $_.Name -eq 'Prompt' -and $_.IsGenericMethodDefinition -and $_.GetParameters().Count -eq 1
    } | Select-Object -First 1
    if (-not $method) { throw 'Spectre.Console prompt method was not found.' }
    $closedMethod = $method.MakeGenericMethod([System.Collections.Generic.List[string]])
    return $closedMethod.Invoke($null, @($Prompt))
}

function Format-VrcSetupProjectCatalogCell {
    param(
        [AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)][int]$Width
    )

    $value = ([string]$Text -replace '[\r\n]+', ' ').Trim()
    if ($value.Length -gt $Width) {
        $value = $value.Substring(0, [Math]::Max(0, $Width - 1)) + '…'
    }
    return $value.PadRight($Width)
}

function Format-VrcSetupProjectCatalogRow {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][int]$Index
    )

    $packages = if ([string]$Project.PackageMetadataSource -eq 'EmbeddedPackages') {
        '{0} packages*' -f $Project.PackageCount
    } elseif ([string]$Project.Status -eq 'Ready') {
        '{0} packages' -f $Project.PackageCount
    } else {
        [string]$Project.Status
    }
    $unityVersion = [string]$Project.UnityVersion
    if ([string]$Project.UnityVersionSource -eq 'GeneratedProject') { $unityVersion += '*' }
    $updated = 'Unknown'
    try {
        $lastModified = if ($Project.LastModifiedUtc -is [datetime]) {
            [datetime]$Project.LastModifiedUtc
        } else {
            [datetime]::Parse([string]$Project.LastModifiedUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
        }
        $updated = $lastModified.ToLocalTime().ToString('dd MMM yyyy HH:mm', [Globalization.CultureInfo]::InvariantCulture)
    } catch { }

    return ('{0,2}  {1}  {2}  {3}  {4}  {5}' -f
        $Index,
        (Format-VrcSetupProjectCatalogCell -Text ([string]$Project.RelativePath) -Width 28),
        (Format-VrcSetupProjectCatalogCell -Text ([string]$Project.Kind) -Width 11),
        (Format-VrcSetupProjectCatalogCell -Text $unityVersion -Width 13),
        (Format-VrcSetupProjectCatalogCell -Text $packages -Width 15),
        $updated)
}


function New-VrcSetupPackagePickerRenderable {
    param(
        [Parameter(Mandatory)]$Items,
        [Parameter(Mandatory)][int]$SelectedIndex,
        [Parameter(Mandatory)]$Selected,
        [AllowEmptyString()][string]$Header = '',
        [bool]$SingleSelection = $false,
        [ValidateRange(4, 20)][int]$MaxVisible = 14
    )

    $itemsArray = @($Items)
    $windowWidth = try { [Console]::WindowWidth } catch { [Spectre.Console.AnsiConsole]::Profile.Width }
    $isNarrow = ($windowWidth -lt 105)
    $table = [Spectre.Console.Table]::new()
    $table.Border = [Spectre.Console.TableBorder]::Rounded
    $table.BorderStyle = New-VrcSetupSpectreStyle -Foreground '#4C6A7A'
    $table.ShowHeaders = $true
    $table.ShowRowSeparators = $false
    $table.Expand = $false
    foreach ($columnInfo in @(
        @{ Label = 'Pick'; Width = 5 },
        @{ Label = 'Package'; Width = $(if ($isNarrow) { 22 } else { 28 }) },
        @{ Label = 'Package ID'; Width = $(if ($isNarrow) { 28 } else { 38 }) },
        @{ Label = 'Version'; Width = $(if ($isNarrow) { 10 } else { 18 }) }
    )) {
        $column = [Spectre.Console.TableColumn]::new("[#78909F]$($columnInfo.Label)[/]")
        $column.Width = [int]$columnInfo.Width
        $column.NoWrap = $true
        [void]$table.AddColumn($column)
    }

    $offset = if ($itemsArray.Count -gt $MaxVisible) { [Math]::Floor($SelectedIndex / $MaxVisible) * $MaxVisible } else { 0 }
    $end = [Math]::Min($itemsArray.Count - 1, $offset + $MaxVisible - 1)
    for ($index = $offset; $index -le $end; $index++) {
        $item = $itemsArray[$index]
        $name = if (-not [string]::IsNullOrWhiteSpace([string]$item.DisplayName)) { [string]$item.DisplayName } else { Get-VrcSetupFriendlyPackageName -PackageName ([string]$item.Id) }
        $cells = @(
            $(if ($SingleSelection) { $(if ($index -eq $SelectedIndex) { '>' } else { '' }) } elseif ([bool]$Selected[$index]) { '[x]' } else { '[ ]' }),
            $name,
            [string]$item.Id,
            $(if ($item.SelectedVersion) { [string]$item.SelectedVersion } elseif ($item.VersionEditable) { 'latest' } elseif ([string]::IsNullOrWhiteSpace([string]$item.LatestVersion)) { 'Newest' } else { [string]$item.LatestVersion })
        )
        $rendered = @()
        for ($cellIndex = 0; $cellIndex -lt $cells.Count; $cellIndex++) {
            $width = @(5, $(if ($isNarrow) { 22 } else { 28 }), $(if ($isNarrow) { 28 } else { 38 }), $(if ($isNarrow) { 10 } else { 18 }))[$cellIndex]
            $safe = ConvertTo-VrcSetupSpectreText (Format-VrcSetupPackageCell -Text ([string]$cells[$cellIndex]) -Width $width)
            $markup = if ($index -eq $SelectedIndex) { "[black on #75D7F7]${safe}[/]" } elseif ($cellIndex -eq 0 -and [bool]$Selected[$index]) { "[bold #67E8A5]${safe}[/]" } else { "[#DDEAF2]${safe}[/]" }
            $rendered += [Spectre.Console.Markup]::new($markup)
        }
        [void]$table.Rows.Add([Spectre.Console.Rendering.IRenderable[]]$rendered)
    }

    $rows = @()
    if (-not [string]::IsNullOrWhiteSpace($Header)) { $rows += [Spectre.Console.Markup]::new("[#DDEAF2]$(ConvertTo-VrcSetupSpectreText $Header)[/]"); $rows += [Spectre.Console.Text]::new(' ') }
    $rows += $table
    if ($itemsArray.Count -gt $MaxVisible) {
        $rangeEnd = [Math]::Min($itemsArray.Count, $offset + $MaxVisible)
        $rows += [Spectre.Console.Markup]::new("[#78909F]Showing $($offset + 1)-${rangeEnd} of $($itemsArray.Count) packages[/]")
    }
    $rows += [Spectre.Console.Text]::new(' ')
    $selectedCount = @($Selected.Values | Where-Object { $_ }).Count
    if (-not $SingleSelection) {
        $verb = if ($itemsArray[0].SelectionVerb) { [string]$itemsArray[0].SelectionVerb } else { 'Add' }
        $actionLabel = if ($selectedCount -gt 0) { "$verb ${selectedCount} selected package(s)" } else {
            $focused = $itemsArray[$SelectedIndex]
            "$verb $(if ($focused.DisplayName) { $focused.DisplayName } else { Get-VrcSetupFriendlyPackageName -PackageName $focused.Id })"
        }
        $rows += New-VrcSetupActionButton -Label $actionLabel
    } else {
        $rows += New-VrcSetupActionButton -Label 'Select package' -Color '#75D7F7'
    }
    $rows += [Spectre.Console.Rule]::new()
    if ($itemsArray[$SelectedIndex].VersionEditable) { $rows += New-VrcSetupActionButton -Label 'Choose version' -Key 'V' -Color '#C7A8FF' }
    $pickerHint = if ($SingleSelection) { 'Up/Down Move  ·  Esc Back' } else { 'Space Select  ·  Up/Down Move  ·  Esc Back' }
    $rows += [Spectre.Console.Markup]::new("[#78909F]${pickerHint}[/]")
    return [Spectre.Console.Rows]::new([Spectre.Console.Rendering.IRenderable[]]$rows)
}

function Show-VrcSetupSpectrePackageChecklist {
    param(
        [Parameter(Mandatory)]$Items,
        [string]$Title = 'Choose packages',
        [string]$Header = '',
        [bool]$DefaultSelected = $false,
        [bool]$SingleSelection = $false,
        [ValidateRange(4, 20)][int]$MaxVisible = 14,
        [string]$ScriptDir = (Get-VrcSetupSpectreScriptDir),
        [scriptblock]$VersionSelector
    )

    $itemsArray = @($Items)
    if ($itemsArray.Count -eq 0) { return @() }
    if (-not (Write-VrcSetupSpectreFrame -Title $Title -ScriptDir $ScriptDir)) { return $null }
    try { $MaxVisible = [Math]::Max(4, [Math]::Min($MaxVisible, [Console]::WindowHeight - 17)) } catch { }
    $selected = @{}
    for ($index = 0; $index -lt $itemsArray.Count; $index++) { $selected[$index] = $DefaultSelected }
    if ($VersionSelector) { foreach ($item in $itemsArray) { $item | Add-Member -NotePropertyName VersionEditable -NotePropertyValue $true -Force } }
    $state = [pscustomobject]@{ Index = 0; Done = $false; Cancelled = $false; ChooseVersion=$false }
    do {
    $state.Done = $false; $state.ChooseVersion = $false
    [void](Write-VrcSetupSpectreFrame -Title $Title -ScriptDir $ScriptDir)
    $initial = New-VrcSetupPackagePickerRenderable -Items $itemsArray -SelectedIndex $state.Index -Selected $selected -Header $Header -SingleSelection:$SingleSelection -MaxVisible $MaxVisible
    $live = [Spectre.Console.AnsiConsole]::Live($initial)
    $live.AutoClear = $true
    $cursorWasVisible = $true
    try {
        try { $cursorWasVisible = [Console]::CursorVisible; [Console]::CursorVisible = $false } catch { }
        $live.Start([System.Action[Spectre.Console.LiveDisplayContext]]{
            param($context)
            $context.Refresh()
            while (-not $state.Done) {
                $key = [Console]::ReadKey($true)
                switch ($key.Key) {
                    'UpArrow' { $state.Index = [Math]::Max(0, $state.Index - 1) }
                    'DownArrow' { $state.Index = [Math]::Min($itemsArray.Count - 1, $state.Index + 1) }
                    'PageUp' { $state.Index = [Math]::Max(0, $state.Index - $MaxVisible) }
                    'PageDown' { $state.Index = [Math]::Min($itemsArray.Count - 1, $state.Index + $MaxVisible) }
                    'Home' { $state.Index = 0 }
                    'End' { $state.Index = $itemsArray.Count - 1 }
                    'Spacebar' { if (-not $SingleSelection) { $selected[$state.Index] = -not [bool]$selected[$state.Index] } }
                    'Enter' {
                        if (-not $SingleSelection -and @($selected.Values | Where-Object { $_ }).Count -eq 0) {
                            $selected[$state.Index] = $true
                        }
                        $state.Done = $true
                    }
                    'Escape' { $state.Cancelled = $true; $state.Done = $true }
                    'V' { if ($VersionSelector) { $state.ChooseVersion=$true; $state.Done=$true } }
                    default {
                        if (-not $SingleSelection -and [char]::ToUpperInvariant($key.KeyChar) -in @('A','N')) {
                            $include = ([char]::ToUpperInvariant($key.KeyChar) -eq 'A')
                            for ($i = 0; $i -lt $itemsArray.Count; $i++) { $selected[$i] = $include }
                        }
                    }
                }
                if (-not $state.Done) {
                    $next = New-VrcSetupPackagePickerRenderable -Items $itemsArray -SelectedIndex $state.Index -Selected $selected -Header $Header -SingleSelection:$SingleSelection -MaxVisible $MaxVisible
                    $context.UpdateTarget($next)
                    $context.Refresh()
                }
            }
        })
    } finally {
        try { [Console]::CursorVisible = $cursorWasVisible } catch { }
    }
    if ($state.ChooseVersion) {
        $version = & $VersionSelector $itemsArray[$state.Index]
        if ($version) {
            $itemsArray[$state.Index] | Add-Member -NotePropertyName SelectedVersion -NotePropertyValue ([string]$version) -Force
            $selected[$state.Index]=$true
        }
    }
    } while ($state.ChooseVersion)
    if ($state.Cancelled) { return $null }
    return @(Get-VrcSetupConfirmedPackageSelection -Items $itemsArray -Selected $selected -FocusedIndex $state.Index -SingleSelection:$SingleSelection)
}

function Show-VrcSetupSpectreChecklist {
    param(
        [Parameter(Mandatory)]$Items,
        [scriptblock]$ToLabel,
        [string]$Title = 'Select items',
        [string]$PromptTitle = 'Choose items',
        [string]$Header = '',
        [bool]$DefaultSelected = $true,
        [int]$MaxVisible = 15,
        [bool]$AllowCancel = $true,
        [string]$ScriptDir = (Get-VrcSetupSpectreScriptDir)
    )

    $itemsArr = @($Items)
    if ($itemsArr.Count -eq 0) { return @() }
    if (-not (Write-VrcSetupSpectreFrame -Title $Title -Header $Header -ScriptDir $ScriptDir)) { return $null }

    $prompt = [Spectre.Console.MultiSelectionPrompt[string]]::new()
    $prompt.Title = "[#DDEAF2]$(ConvertTo-VrcSetupSpectreText $PromptTitle)[/]"
    $prompt.PageSize = [Math]::Min(14, [Math]::Max(4, $MaxVisible))
    $prompt.WrapAround = $true
    $prompt.Required = $false
    $prompt.HighlightStyle = New-VrcSetupSpectreStyle -Foreground '#0B1520' -Background '#75D7F7'
    [Spectre.Console.AnsiConsole]::MarkupLine('[#78909F]Up/Down to move  ·  Space to toggle  ·  Enter to confirm  ·  Esc to return[/]')
    $prompt.MoreChoicesText = '[#78909F]More items below — use Up/Down to scroll.[/]'

    $itemByChoice = @{}
    for ($index = 0; $index -lt $itemsArr.Count; $index++) {
        $label = if ($ToLabel) { [string](& $ToLabel $itemsArr[$index] $index) } else { [string]$itemsArr[$index] }
        $choice = $label
        $suffix = 2
        while ($itemByChoice.ContainsKey($choice)) {
            $choice = "${label} (${suffix})"
            $suffix++
        }
        $displayChoice = ConvertTo-VrcSetupSpectreText $choice
        $itemByChoice[$displayChoice] = $itemsArr[$index]
        $choiceItem = $prompt.AddChoice($displayChoice)
        if ($DefaultSelected) { [void]$choiceItem.Select() }
    }

    try {
        if ($AllowCancel) {
            $prompt.CancelResult = [System.Func[System.Collections.Generic.List[string]]] { return [System.Collections.Generic.List[string]]::new() }
        }
        $selectedDisplayChoices = Invoke-VrcSetupSpectreListPrompt -Prompt $prompt
        if ($null -eq $selectedDisplayChoices) { return $null }
        $selectedItems = @()
        foreach ($choice in @($selectedDisplayChoices)) {
            if ($itemByChoice.ContainsKey([string]$choice)) { $selectedItems += $itemByChoice[[string]$choice] }
        }
        return $selectedItems
    } catch {
        if ($AllowCancel) { return $null }
        throw
    }
}

function Select-VrcSetupProjectCatalogAction {
    param(
        [Parameter(Mandatory)]$Catalog,
        [Parameter(Mandatory)][string]$ScriptDir
    )

    $choices = [ordered]@{}
    foreach ($project in @($Catalog.Projects)) {
        $label = "$($project.RelativePath)  ·  $($project.Kind)  ·  $($project.PackageCount) packages"
        $uniqueLabel = $label
        $suffix = 2
        while ($choices.Contains($uniqueLabel)) {
            $uniqueLabel = "${label} (${suffix})"
            $suffix++
        }
        $choices[$uniqueLabel] = [pscustomobject]@{ Action = 'project'; ProjectPath = [string]$project.Path }
    }
    $sortLabel = if ([string]$Catalog.SortOrder -eq 'name') { 'Name (A-Z)' } else { 'Recently updated' }
    $choices["Change sort order ($sortLabel)"] = [pscustomobject]@{ Action = 'sort'; ProjectPath = $null }
    $choices['Edit packages in multiple projects'] = [pscustomobject]@{ Action = 'choosebulk'; ProjectPath = $null }
    $choices['Refresh project list'] = [pscustomobject]@{ Action = 'refresh'; ProjectPath = $null }
    $choices['Choose a project folder'] = [pscustomobject]@{ Action = 'manual'; ProjectPath = $null }
    $choices['Back'] = [pscustomobject]@{ Action = 'back'; ProjectPath = $null }

    $labels = @($choices.Keys)
    $index = Show-VrcSetupSpectreMenu -Title 'Project library' -Options $labels -AllowCancel:$true -ScriptDir $ScriptDir -SkipFrame
    if ($null -eq $index) {
        $index = Show-Menu -Title 'Project library' -Header "Select a project to manage, refresh the list, or open a different folder." -Options $labels
    }
    if ($index -lt 0) { return [pscustomobject]@{ Action = 'back'; ProjectPath = $null } }
    return $choices[$labels[$index]]
}

function Select-VrcSetupProjectCatalogLibraryAction {
    param(
        [Parameter(Mandatory)]$Catalog,
        [Parameter(Mandatory)][string]$ScriptDir
    )

    $sortLabel = if ([string]$Catalog.SortOrder -eq 'name') { 'Name (A-Z)' } else { 'Recently updated' }
    $choices = [ordered]@{
        'Change selected projects' = [pscustomobject]@{ Action = 'choosebulk'; ProjectPath = $null }
        'Clear project selection' = [pscustomobject]@{ Action = 'clearselection'; ProjectPath = $null }
        "Change project order ($sortLabel)" = [pscustomobject]@{ Action = 'sort'; ProjectPath = $null }
        'Refresh project list' = [pscustomobject]@{ Action = 'refresh'; ProjectPath = $null }
        'Choose a different project folder' = [pscustomobject]@{ Action = 'manual'; ProjectPath = $null }
        'Clean up incomplete projects' = [pscustomobject]@{ Action = 'cleanup'; ProjectPath = $null }
        'Back to project table' = [pscustomobject]@{ Action = 'back'; ProjectPath = $null }
    }
    $labels = @($choices.Keys)
    $index = Show-VrcSetupSpectreMenu -Title 'Library options' -Options $labels -SectionBreaks @(2, 4, 5, 6) -AllowCancel:$true -ScriptDir $ScriptDir
    if ($null -eq $index) {
        $index = Show-Menu -Title 'Project library options' -Header 'Refresh the list, change its order, or open another project folder.' -Options $labels
    }
    if ($index -lt 0) { return [pscustomobject]@{ Action = 'back'; ProjectPath = $null } }
    return $choices[$labels[$index]]
}

. (Join-Path $PSScriptRoot 'navigation-ui.ps1')
