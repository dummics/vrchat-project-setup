$script:VrcSetupSpectreAvailable = $null

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

function Show-VrcSetupSpectreMenu {
    param(
        [string]$Title = 'VRChat Project Setup',
        [string]$Header = '',
        [string]$PromptTitle = '',
        [Parameter(Mandatory)][string[]]$Options,
        [int]$Current = 0,
        [bool]$AllowCancel = $true,
        [bool]$EnableHorizontalNav = $false,
        [int[]]$SectionBreaks = @(),
        [ValidateRange(4, 30)][int]$MaxVisible = 14,
        [switch]$SkipFrame,
        [string]$ScriptDir = (Get-VrcSetupSpectreScriptDir)
    )

    if (-not $Options) { return -1 }
    if ($SkipFrame) {
        if (-not (Initialize-VrcSetupSpectre -ScriptDir $ScriptDir)) { return $null }
    } elseif (-not (Write-VrcSetupSpectreFrame -Title $Title -Header $Header -ScriptDir $ScriptDir)) {
        return $null
    }

    $prompt = [Spectre.Console.SelectionPrompt[string]]::new()
    $prompt.Title = if ([string]::IsNullOrWhiteSpace($PromptTitle)) { '' } else { "[#DDEAF2]$(ConvertTo-VrcSetupSpectreText $PromptTitle)[/]" }
    $prompt.PageSize = [Math]::Min($MaxVisible, [Math]::Max(4, $Options.Count))
    $prompt.WrapAround = $true
    $prompt.HighlightStyle = New-VrcSetupSpectreStyle -Foreground '#0B1520' -Background '#75D7F7'
    $prompt.MoreChoicesText = '[#78909F]More items below — use Up/Down to scroll. Type to jump.[/]'
    $keyboardHint = if ($AllowCancel) { 'Up/Down to move  ·  Enter to select  ·  Esc or Back to return' } else { 'Up/Down to move  ·  Enter to select' }
    [Spectre.Console.AnsiConsole]::MarkupLine("[#78909F]${keyboardHint}[/]")
    $choiceIndex = @{}
    $footerStart = $Options.Count
    for ($index = $Options.Count - 1; $index -ge 0; $index--) {
        if (Test-IsFooterOption -Text $Options[$index]) {
            $footerStart = $index
        } else {
            break
        }
    }
    $breakIndices = @(
        @($SectionBreaks) + $(if ($footerStart -gt 0 -and $footerStart -lt $Options.Count) { $footerStart }) |
            Where-Object { $_ -gt 0 -and $_ -lt $Options.Count } |
            Sort-Object -Unique
    )
    if ($breakIndices.Count -gt 0) {
        $prompt.PageSize = [Math]::Min($MaxVisible, [Math]::Max(4, $Options.Count + $breakIndices.Count))
    }
    $activeGroup = $null
    $breakOrdinal = 0
    for ($index = 0; $index -lt $Options.Count; $index++) {
        if ($breakIndices -contains $index) {
            $breakOrdinal++
            $groupWhitespace = ' ' * $breakOrdinal
            $activeGroup = $prompt.AddChoice("[#78909F]${groupWhitespace}[/]")
        }
        $displayChoice = ConvertTo-VrcSetupSpectreChoice ([string]$Options[$index])
        $choiceIndex[$displayChoice] = $index
        if ($null -ne $activeGroup) {
            [void]$activeGroup.AddChild($displayChoice)
        } else {
            [void]$prompt.AddChoice($displayChoice)
        }
    }

    try {
        if ($AllowCancel) {
            $prompt.CancelResult = [System.Func[string]] { return '__VRCSETUP_CANCELLED__' }
        }
        $selected = Invoke-VrcSetupSpectreStringPrompt -Prompt $prompt
        if ($selected -eq '__VRCSETUP_CANCELLED__') { return -1 }
        if ($choiceIndex.ContainsKey($selected)) { return [int]$choiceIndex[$selected] }
        return -1
    } catch {
        if ($AllowCancel) { return -1 }
        throw
    }
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

function New-VrcSetupPackageWorkspaceRenderable {
    param(
        [Parameter(Mandatory)]$Items,
        [Parameter(Mandatory)][int]$SelectedIndex,
        [Parameter(Mandatory)][int]$PendingCount,
        [bool]$IncludeExtras = $false,
        [bool]$HasExtras = $false,
        [AllowEmptyString()][string]$Notice = '',
        [ValidateRange(4, 20)][int]$MaxVisible = 10
    )

    $itemsArray = @($Items)
    $table = [Spectre.Console.Table]::new()
    $table.Border = [Spectre.Console.TableBorder]::Rounded
    $table.BorderStyle = New-VrcSetupSpectreStyle -Foreground '#4C6A7A'
    $table.ShowHeaders = $true
    $table.ShowRowSeparators = $false
    $table.Expand = $false

    foreach ($columnInfo in @(
        @{ Label = 'Package'; Width = 28 },
        @{ Label = 'Version'; Width = 22 },
        @{ Label = 'After saving'; Width = 18 }
    )) {
        $column = [Spectre.Console.TableColumn]::new("[#78909F]$($columnInfo.Label)[/]")
        $column.Width = [int]$columnInfo.Width
        $column.NoWrap = $true
        [void]$table.AddColumn($column)
    }

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
        } else {
            $installed
        }
        $stateLabel = switch ([string]$item.Status) {
            'Add' { 'Will be added'; break }
            'Update' { 'Will change'; break }
            'Remove' { 'Will be removed'; break }
            'Required' { 'Always included'; break }
            default { 'Included' }
        }
        $packageText = ('{0}{1}' -f $(if ($isSelected) { '> ' } else { '  ' }), [string]$item.FriendlyName)
        $rawCells = @(
            (Format-VrcSetupPackageCell -Text $packageText -Width 28),
            (Format-VrcSetupPackageCell -Text $versionLabel -Width 22),
            (Format-VrcSetupPackageCell -Text $stateLabel -Width 18)
        )
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
    $renderables += [Spectre.Console.Text]::new(' ')

    $saveSelected = ($SelectedIndex -eq $itemsArray.Count)
    $canSave = ($PendingCount -gt 0 -or $IncludeExtras)
    $saveText = if ($canSave) {
        if ($PendingCount -eq 1) { 'Save 1 change' } elseif ($PendingCount -gt 1) { "Save ${PendingCount} changes" } else { 'Save changes' }
    } else {
        'No changes to save'
    }
    $buttonLabel = "S   ${saveText}"
    $buttonWidth = 66
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
    $savePanel.Width = 72
    $savePanel.Padding = [Spectre.Console.Padding]::new(1, 0)
    $renderables += $savePanel
    $renderables += [Spectre.Console.Text]::new(' ')
    $renderables += [Spectre.Console.Text]::new(' ')
    $renderables += [Spectre.Console.Markup]::new('[#78909F]Up/Down Choose package     Left/Right Change version     Space Include/remove[/]')
    $secondaryHints = @('A Find package', 'D My set')
    if ($HasExtras) { $secondaryHints += 'I Imports' }
    $secondaryHints += @('V Version list', 'Esc Back')
    $renderables += [Spectre.Console.Markup]::new("[#78909F]$($secondaryHints -join '     ')[/]")
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
        [bool]$IncludeExtras = $false,
        [int]$ExtrasCount = 0,
        [scriptblock]$VersionProvider,
        [string]$ScriptDir = (Get-VrcSetupSpectreScriptDir)
    )

    if (-not (Initialize-VrcSetupSpectre -ScriptDir $ScriptDir)) { return $null }
    $itemsArray = @($Items)
    if ($itemsArray.Count -eq 0) { return $null }
    $headerText = if ($IncludeExtras -and $ExtrasCount -gt 0) { "${ProjectName}`nSaved imports included: ${ExtrasCount}" } else { $ProjectName }
    if (-not (Write-VrcSetupSpectreFrame -Title 'Project packages' -Header $headerText -ScriptDir $ScriptDir)) { return $null }

    $state = [pscustomobject]@{
        SelectedIndex = 0
        Done = $false
        Action = $null
        PackageName = $null
        VersionChanges = [ordered]@{}
        Notice = ''
    }
    $hasExtras = ($ExtrasCount -gt 0)
    $initial = New-VrcSetupPackageWorkspaceRenderable -Items $itemsArray -SelectedIndex 0 -PendingCount $PendingCount -IncludeExtras:$IncludeExtras -HasExtras:$hasExtras -Notice $state.Notice
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
                    'UpArrow' {
                        if ($state.SelectedIndex -gt 0) { $state.SelectedIndex--; $refresh = $true }
                    }
                    'DownArrow' {
                        if ($state.SelectedIndex -lt $itemsArray.Count) { $state.SelectedIndex++; $refresh = $true }
                    }
                    { $_ -in @('LeftArrow', 'RightArrow') } {
                        if ($state.SelectedIndex -lt $itemsArray.Count -and $VersionProvider) {
                            $item = $itemsArray[$state.SelectedIndex]
                            $state.Notice = "Loading versions for $($item.FriendlyName)..."
                            $loading = New-VrcSetupPackageWorkspaceRenderable -Items $itemsArray -SelectedIndex $state.SelectedIndex -PendingCount $PendingCount -IncludeExtras:$IncludeExtras -HasExtras:$hasExtras -Notice $state.Notice
                            $context.UpdateTarget($loading)
                            $context.Refresh()
                            $versions = @()
                            try { $versions = @(& $VersionProvider ([string]$item.Name)) } catch { $versions = @() }
                            $direction = if ($key.Key -eq 'LeftArrow') { 'Older' } else { 'Newer' }
                            $adjacent = Get-VrcSetupAdjacentPackageVersion -AvailableVersions $versions -CurrentVersion ([string]$item.DesiredVersion) -Direction $direction
                            if ($adjacent) {
                                $item.DesiredVersion = [string]$adjacent.Version
                                $item.Status = if (-not $item.CurrentVersion) {
                                    'Add'
                                } elseif ([string]$item.CurrentVersion -ne [string]$item.DesiredVersion) {
                                    'Update'
                                } elseif ($item.Required) {
                                    'Required'
                                } else {
                                    'Installed'
                                }
                                $state.VersionChanges[[string]$item.Name] = [string]$item.DesiredVersion
                                $PendingCount = @($itemsArray | Where-Object { $_.Status -in @('Add', 'Update', 'Remove') }).Count
                                $state.Notice = if ($adjacent.AtLimit) { "No more $($direction.ToLowerInvariant()) versions." } else { "$($item.FriendlyName): $($item.DesiredVersion)" }
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
                            $state.Action = 'save'
                        } else {
                            $state.Action = 'version'
                            $state.PackageName = [string]$itemsArray[$state.SelectedIndex].Name
                        }
                        $state.Done = $true
                    }
                    'Spacebar' {
                        if ($state.SelectedIndex -lt $itemsArray.Count -and -not $itemsArray[$state.SelectedIndex].Required) {
                            $state.Action = 'toggle'
                            $state.PackageName = [string]$itemsArray[$state.SelectedIndex].Name
                            $state.Done = $true
                        }
                    }
                    default {
                        switch ([char]::ToUpperInvariant($key.KeyChar)) {
                            'A' { $state.Action = 'add'; $state.Done = $true }
                            'D' { $state.Action = 'defaults'; $state.Done = $true }
                            'I' { if ($hasExtras) { $state.Action = 'extras'; $state.Done = $true } }
                            'S' { $state.Action = 'save'; $state.Done = $true }
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
                    $next = New-VrcSetupPackageWorkspaceRenderable -Items $itemsArray -SelectedIndex $state.SelectedIndex -PendingCount $PendingCount -IncludeExtras:$IncludeExtras -HasExtras:$hasExtras -Notice $state.Notice
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
    }
}

function Read-VrcSetupSpectreTextInput {
    param(
        [Parameter(Mandatory)][string]$Title,
        [AllowEmptyString()][string]$Header,
        [Parameter(Mandatory)][string]$Prompt,
        [string]$Hint = 'Leave blank to go back.',
        [string]$ScriptDir = (Get-VrcSetupSpectreScriptDir)
    )

    if (-not (Write-VrcSetupSpectreFrame -Title $Title -Header $Header -ScriptDir $ScriptDir)) { return $null }
    if (-not [string]::IsNullOrWhiteSpace($Hint)) {
        [Spectre.Console.AnsiConsole]::MarkupLine("[#78909F]$(ConvertTo-VrcSetupSpectreText $Hint)[/]")
    }

    $textPrompt = [Spectre.Console.TextPrompt[string]]::new("[#DDEAF2]$(ConvertTo-VrcSetupSpectreText $Prompt)[/]")
    $textPrompt.AllowEmpty = $true
    $textPrompt.PromptStyle = New-VrcSetupSpectreStyle -Foreground '#75D7F7'
    return Invoke-VrcSetupSpectreStringPrompt -Prompt $textPrompt
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

function Show-VrcSetupProjectCatalogSpectre {
    param(
        [Parameter(Mandatory)]$Catalog,
        [Parameter(Mandatory)][string]$ScriptDir
    )

    if (-not (Initialize-VrcSetupSpectre -ScriptDir $ScriptDir)) { return $null }

    $sortLabel = if ([string]$Catalog.SortOrder -eq 'name') { 'Name (A-Z)' } else { 'Recently updated' }
    $recoveredCount = @($Catalog.Projects | Where-Object {
        [string]$_.PackageMetadataSource -eq 'EmbeddedPackages' -or [string]$_.UnityVersionSource -eq 'GeneratedProject'
    }).Count
    $header = "Projects under: {0}`nFound: {1}  |  Order: {2}" -f $Catalog.RootPath, $Catalog.ProjectCount, $sortLabel
    if ($recoveredCount -gt 0) {
        $header += "`n* Recovered from embedded/generated files because canonical metadata is missing."
    }
    [void](Write-VrcSetupSpectreFrame -Title 'Project library' -Header $header -ScriptDir $ScriptDir)
    $scanSummary = "Scan: $($Catalog.DurationMs) ms | cache reused: $($Catalog.CacheHits) | refreshed: $($Catalog.Refreshed)"
    [Spectre.Console.AnsiConsole]::MarkupLine("[#78909F]$(ConvertTo-VrcSetupSpectreText $scanSummary)[/]")
    [Spectre.Console.AnsiConsole]::WriteLine()

    $prompt = [Spectre.Console.SelectionPrompt[string]]::new()
    $prompt.Title = '[#DDEAF2] #   Project                       Type         Unity          Packages         Updated[/]'
    $prompt.PageSize = [Math]::Min(16, [Math]::Max(4, $Catalog.ProjectCount + 1))
    $prompt.WrapAround = $true
    $prompt.HighlightStyle = New-VrcSetupSpectreStyle -Foreground '#0B1520' -Background '#75D7F7'
    $prompt.MoreChoicesText = '[#78909F]More projects below — use Up/Down to scroll. Type to jump.[/]'
    [Spectre.Console.AnsiConsole]::MarkupLine('[#78909F]Up/Down to select a project  ·  Enter to manage it  ·  Esc to return[/]')

    $choiceActions = @{}
    $index = 1
    foreach ($project in @($Catalog.Projects)) {
        $displayChoice = ConvertTo-VrcSetupSpectreText (Format-VrcSetupProjectCatalogRow -Project $project -Index $index)
        $choiceActions[$displayChoice] = [pscustomobject]@{ Action = 'project'; ProjectPath = [string]$project.Path }
        [void]$prompt.AddChoice($displayChoice)
        $index++
    }

    $libraryActionsChoice = '[#75D7F7]More options…[/]'
    $choiceActions[$libraryActionsChoice] = [pscustomobject]@{ Action = 'library'; ProjectPath = $null }
    [void]$prompt.AddChoice($libraryActionsChoice)

    try {
        $prompt.CancelResult = [System.Func[string]] { return '__VRCSETUP_CANCELLED__' }
        $selected = Invoke-VrcSetupSpectreStringPrompt -Prompt $prompt
        if ($selected -eq '__VRCSETUP_CANCELLED__') { return [pscustomobject]@{ Action = 'back'; ProjectPath = $null } }
        if ($choiceActions.ContainsKey($selected)) { return $choiceActions[$selected] }
        return [pscustomobject]@{ Action = 'back'; ProjectPath = $null }
    } catch {
        return [pscustomobject]@{ Action = 'back'; ProjectPath = $null }
    }
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
        "Change project order ($sortLabel)" = [pscustomobject]@{ Action = 'sort'; ProjectPath = $null }
        'Refresh project list' = [pscustomobject]@{ Action = 'refresh'; ProjectPath = $null }
        'Choose a different project folder' = [pscustomobject]@{ Action = 'manual'; ProjectPath = $null }
        'Back to project table' = [pscustomobject]@{ Action = 'back'; ProjectPath = $null }
    }
    $labels = @($choices.Keys)
    $index = Show-VrcSetupSpectreMenu -Title 'Project library options' -Header 'Refresh the list, change its order, or open another project folder.' -Options $labels -AllowCancel:$true -ScriptDir $ScriptDir
    if ($null -eq $index) {
        $index = Show-Menu -Title 'Project library options' -Header 'Refresh the list, change its order, or open another project folder.' -Options $labels
    }
    if ($index -lt 0) { return [pscustomobject]@{ Action = 'back'; ProjectPath = $null } }
    return $choices[$labels[$index]]
}
