# Shared navigation and searchable project library, loaded after Spectre helpers.
function Get-VrcSetupViewport {
    param([int]$Width = 0, [int]$Height = 0)
    if ($Width -le 0) { $Width = try { [Console]::WindowWidth } catch { 0 } }
    if ($Height -le 0) { $Height = try { [Console]::WindowHeight } catch { 0 } }
    if ($Width -le 0) { $Width = try { [Spectre.Console.AnsiConsole]::Profile.Width } catch { 80 } }
    if ($Height -le 0) { $Height = 30 }
    return [pscustomobject]@{ Width = [Math]::Max(30, $Width); Height = [Math]::Max(16, $Height) }
}

function Get-VrcSetupMenuActionWeight {
    param([string]$Label)
    if ($Label -match '^(Back|Cancel|Exit)$') { return 'Footer' }
    if ($Label -match '^(Start importing|Import avatar or asset|Save preset|Save \d|Save changes|Apply changes|Apply to selected projects|Review changes|Confirm)') { return 'Primary' }
    if ($Label -match '^(Change |Choose another |Reset |Delete |Clean up )') { return 'Utility' }
    return 'Secondary'
}

function New-VrcSetupNavigationMenuRenderable {
    param([string[]]$Options, [int]$Current = 0, [int[]]$SectionBreaks = @(), [int]$MaxVisible = 14, [bool]$AllowCancel = $true)
    $rows = @()
    $viewport = Get-VrcSetupViewport
    $isPackageList = $Options[0] -match '^(Required|Optional)\s'
    $pageSize = [Math]::Max(3, [Math]::Min($MaxVisible, $(if ($isPackageList) { $viewport.Height - 12 } else { [Math]::Floor(($viewport.Height - 8) / 3) })))
    if (-not $isPackageList -and $Options.Count -le $MaxVisible) {
        $estimatedHeight = 4 + @($SectionBreaks | Sort-Object -Unique).Count
        foreach ($option in $Options) {
            $estimatedHeight += switch (Get-VrcSetupMenuActionWeight $option) { 'Primary' { 5 } 'Secondary' { 3 } default { 2 } }
        }
        if ($estimatedHeight -le $viewport.Height) { $pageSize = $Options.Count }
    }
    $start = [int]([Math]::Floor($Current / $pageSize) * $pageSize)
    $end = [Math]::Min($Options.Count, $start + $pageSize)
    $footerStart = $Options.Count
    for ($i = $Options.Count - 1; $i -ge 0; $i--) {
        if (Test-IsFooterOption -Text $Options[$i]) { $footerStart = $i } else { break }
    }
    for ($i = $start; $i -lt $end; $i++) {
        $isPackage = $Options[$i] -match '^(Required|Optional)\s'
        $startsActions = $i -gt 0 -and -not $isPackage -and $Options[$i - 1] -match '^(Required|Optional)\s'
        if ($i -gt $start -and ($SectionBreaks -contains $i -or $i -eq $footerStart -or $startsActions)) {
            $rule = [Spectre.Console.Rule]::new(); $rule.Style = New-VrcSetupSpectreStyle -Foreground '#4C6A7A'; $rows += $rule
        }
        $label = ConvertTo-VrcSetupSpectreText $Options[$i]
        $weight = Get-VrcSetupMenuActionWeight -Label $Options[$i]
        $color = if ($isPackage) { $(if ($Options[$i] -match '^Required') { '#F6C451' } else { '#DDEAF2' }) } elseif ($Options[$i] -match '^(Reset|Clean|Remove|Delete)') { '#FB7185' } elseif ($Options[$i] -match '(?i)preset') { '#C4A5FF' } elseif ($Options[$i] -match '^(Import|Create|Add|Save)') { '#67E8A5' } elseif ($Options[$i] -match '(?i)settings|Unity Editor|Project names') { '#F6C451' } else { '#75D7F7' }
        if ($weight -in @('Utility','Footer')) { $color = if ($Options[$i] -match '^(Reset|Clean|Delete)') { '#FB7185' } else { '#A8B6C1' } }
        $focusColor = if ($weight -in @('Utility','Footer')) { '#75D7F7' } else { $color }
        if ($Options[$i] -eq 'Settings') { $color = '#F2A0A8'; $focusColor = $color }
        $style = if ($i -eq $Current) { "bold black on ${focusColor}" } elseif ($weight -eq 'Primary') { 'bold #67E8A5' } else { $color }
        if ($weight -eq 'Primary') { $label = $label.ToUpperInvariant() }
        $panelWidth = [Math]::Min($(if ($weight -eq 'Primary') { 88 } else { 72 }),$viewport.Width - 4)
        if ($Options[$i] -eq 'Settings') { $rows += [Spectre.Console.Text]::new(' ') }
        $content = [Spectre.Console.Markup]::new("[$style]$(if ($i -eq $Current) { '> ' } else { '  ' })${label}[/]")
        if ($isPackage) { $rows += $content }
        elseif ($weight -in @('Utility','Footer')) { $rows += $content; $rows += [Spectre.Console.Text]::new(' ') }
        else {
            $panel = [Spectre.Console.Panel]::new($content)
            $panel.Border = if ($weight -eq 'Primary') { [Spectre.Console.BoxBorder]::Double } else { [Spectre.Console.BoxBorder]::Rounded }
            $panel.BorderStyle = New-VrcSetupSpectreStyle -Foreground $(if ($weight -eq 'Primary') { '#67E8A5' } elseif ($i -eq $Current) { $color } else { '#4C6A7A' })
            $panel.Width = $panelWidth
            $panel.Padding = [Spectre.Console.Padding]::new(1,$(if ($weight -eq 'Primary') { 1 } else { 0 }))
            $rows += $panel
        }
    }
    if ($Options.Count -gt $pageSize) { $rows += [Spectre.Console.Text]::new("$($start + 1)-${end} / $($Options.Count)") }
    $rows += [Spectre.Console.Markup]::new("[#78909F]Up/Down Move  ·  Enter Select$(if ($AllowCancel) { '  ·  Esc Back' })[/]")
    return [Spectre.Console.Rows]::new([Spectre.Console.Rendering.IRenderable[]]$rows)
}

function Read-VrcSetupNavigationKey {
    param($Context, [scriptblock]$Render)
    $viewport = Get-VrcSetupViewport
    while (-not [Console]::KeyAvailable) {
        $next = Get-VrcSetupViewport
        if ($next.Width -ne $viewport.Width -or $next.Height -ne $viewport.Height) {
            $Context.UpdateTarget((& $Render)); $Context.Refresh(); $viewport = $next
        }
        Start-Sleep -Milliseconds 80
    }
    return [Console]::ReadKey($true)
}

function Update-VrcSetupMenuKey {
    param($State, $Key, [string[]]$Options, [bool]$AllowCancel = $true, [bool]$EnableHorizontalNav = $false)
    switch ($Key.Key) {
        'UpArrow' { $State.Index = ($State.Index + $Options.Count - 1) % $Options.Count }
        'DownArrow' { $State.Index = ($State.Index + 1) % $Options.Count }
        'LeftArrow' { if ($EnableHorizontalNav) { $State.Index = ($State.Index + $Options.Count - 1) % $Options.Count } }
        'RightArrow' { if ($EnableHorizontalNav) { $State.Index = ($State.Index + 1) % $Options.Count } }
        'Home' { $State.Index = 0 }
        'End' { $State.Index = $Options.Count - 1 }
        'Enter' { $State.Result = $State.Index; $State.Done = $true }
        'Escape' { if ($AllowCancel) { $State.Done = $true } }
        default {
            if ($Key.KeyChar -and -not [char]::IsControl($Key.KeyChar)) {
                for ($step = 1; $step -le $Options.Count; $step++) {
                    $next = ($State.Index + $step) % $Options.Count
                    if ($Options[$next].StartsWith([string]$Key.KeyChar,[StringComparison]::OrdinalIgnoreCase)) { $State.Index = $next; break }
                }
            }
        }
    }
}

function Update-VrcSetupLibraryKey {
    param($State, $Key, $Catalog, [int]$PageSize = 10)
    if (-not $State.PSObject.Properties['Selected']) { $State | Add-Member -NotePropertyName Selected -NotePropertyValue @{} }
    $projects = @(Get-VrcSetupFilteredProjects -Projects $Catalog.Projects -Filter $State.Filter)
    $last = [Math]::Max(0,$projects.Count - 1)
    $State.Index = [Math]::Min($last,$State.Index)
    switch ($Key.Key) {
        'UpArrow' { $State.Index=[Math]::Max(0,$State.Index-1) }
        'DownArrow' { $State.Index=[Math]::Min($last,$State.Index+1) }
        'PageUp' { $State.Index=[Math]::Max(0,$State.Index-$PageSize) }
        'PageDown' { $State.Index=[Math]::Min($last,$State.Index+$PageSize) }
        'Home' { $State.Index=0 }
        'End' { $State.Index=$last }
        'F2' { $State.Result=[pscustomobject]@{Action='library';ProjectPath=$null}; $State.Done=$true }
        'Escape' { $State.Done=$true }
        'Spacebar' { if ($projects.Count -gt 0) { $path=[string]$projects[$State.Index].Path; if ($State.Selected.ContainsKey($path)) { $State.Selected.Remove($path) } else { $State.Selected[$path]=$true } } }
        'Enter' {
            $paths = @($Catalog.Projects | Where-Object { $State.Selected.ContainsKey([string]$_.Path) } | ForEach-Object { [string]$_.Path })
            if ($paths.Count -gt 0) { $State.Result=[pscustomobject]@{Action='bulk';ProjectPaths=$paths}; $State.Done=$true }
            elseif ($projects.Count -gt 0) { $State.Result=[pscustomobject]@{Action='project';ProjectPath=[string]$projects[$State.Index].Path}; $State.Done=$true }
        }
        'Backspace' { if ($State.Filter.Length -gt 0) { $State.Filter=$State.Filter.Substring(0,$State.Filter.Length-1); $State.Index=0 } }
        default { if ($Key.KeyChar -and -not [char]::IsControl($Key.KeyChar)) { $State.Filter += $Key.KeyChar; $State.Index=0 } }
    }
}

function Show-VrcSetupSpectreMenu {
    param([string]$Title = 'VRChat Project Setup', [string]$Header = '', [string]$PromptTitle = '',
        [Parameter(Mandatory)][string[]]$Options, [int]$Current = 0, [bool]$AllowCancel = $true,
        [bool]$EnableHorizontalNav = $false, [int[]]$SectionBreaks = @(), [int]$MaxVisible = 14,
        [switch]$SkipFrame, [string]$ScriptDir = (Get-VrcSetupSpectreScriptDir))
    if (-not $Options) { return -1 }
    if ($SkipFrame) { if (-not (Initialize-VrcSetupSpectre -ScriptDir $ScriptDir)) { return $null } }
    elseif (-not (Write-VrcSetupSpectreFrame -Title $Title -Header $Header -ScriptDir $ScriptDir)) { return $null }
    if ($PromptTitle) { [Spectre.Console.AnsiConsole]::MarkupLine((ConvertTo-VrcSetupSpectreText $PromptTitle)) }
    $state = [pscustomobject]@{ Index = [Math]::Min([Math]::Max(0,$Current),$Options.Count - 1); Done = $false; Result = -1 }
    $live = [Spectre.Console.AnsiConsole]::Live((New-VrcSetupNavigationMenuRenderable -Options $Options -Current $state.Index -SectionBreaks $SectionBreaks -MaxVisible $MaxVisible -AllowCancel $AllowCancel))
    $live.AutoClear = $true
    $visible = $true
    try {
        try { $visible = [Console]::CursorVisible; [Console]::CursorVisible = $false } catch { }
        $live.Start([System.Action[Spectre.Console.LiveDisplayContext]]{
            param($context)
            $context.Refresh()
            while (-not $state.Done) {
                $key = Read-VrcSetupNavigationKey -Context $context -Render { New-VrcSetupNavigationMenuRenderable -Options $Options -Current $state.Index -SectionBreaks $SectionBreaks -MaxVisible $MaxVisible -AllowCancel $AllowCancel }
                Update-VrcSetupMenuKey -State $state -Key $key -Options $Options -AllowCancel $AllowCancel -EnableHorizontalNav $EnableHorizontalNav
                if (-not $state.Done) {
                    $context.UpdateTarget((New-VrcSetupNavigationMenuRenderable -Options $Options -Current $state.Index -SectionBreaks $SectionBreaks -MaxVisible $MaxVisible -AllowCancel $AllowCancel)); $context.Refresh()
                }
            }
        })
    } finally { try { [Console]::CursorVisible = $visible } catch { } }
    return $state.Result
}

function Get-VrcSetupFilteredProjects {
    param($Projects, [string]$Filter = '')
    $tokens = @($Filter.Split(@(' ', "`t"),[StringSplitOptions]::RemoveEmptyEntries))
    return @($Projects | Where-Object {
        $text = "$($_.RelativePath) $($_.Kind) $($_.UnityVersion)"
        @($tokens | Where-Object { $text.IndexOf($_,[StringComparison]::OrdinalIgnoreCase) -lt 0 }).Count -eq 0
    })
}

function New-VrcSetupProjectLibraryRenderable {
    param($Catalog, $State, [int]$Width = 0, [int]$Height = 0)
    $viewport = Get-VrcSetupViewport -Width $Width -Height $Height
    $projects = @(Get-VrcSetupFilteredProjects -Projects $Catalog.Projects -Filter $State.Filter)
    $rows = @([Spectre.Console.Markup]::new("[#DDEAF2]$($projects.Count) / $($Catalog.ProjectCount) projects  ·  $(if ($Catalog.SortOrder -eq 'name') { 'Name A-Z' } else { 'Recent first' })[/]"))
    $rows += New-VrcSetupTextInputRenderable -Value $State.Filter -Prompt 'Find a project' -Hint '' -Placeholder 'Type a name, avatar, PC or Quest...' -HideAction
    $table = [Spectre.Console.Table]::new(); $table.Border = [Spectre.Console.TableBorder]::Rounded
    $table.BorderStyle = New-VrcSetupSpectreStyle -Foreground '#4C6A7A'; $table.Expand = $true
    $wide = $viewport.Width -ge 100; $medium = $viewport.Width -ge 65
    $labels = @('Project'); $widths = @([Math]::Max(16, $viewport.Width - $(if ($wide) { 64 } elseif ($medium) { 24 } else { 8 })))
    if ($wide) { $labels += @('Type','Unity','Packages','Updated'); $widths += @(8,13,8,16) }
    elseif ($medium) { $labels += 'Updated'; $widths += 16 }
    for ($i=0; $i -lt $labels.Count; $i++) { $col = [Spectre.Console.TableColumn]::new($labels[$i]); $col.Width = $widths[$i]; $col.NoWrap = $true; [void]$table.AddColumn($col) }
    $pageSize = [Math]::Max(1,$viewport.Height - 20)
    $selected = [Math]::Min([Math]::Max(0,$State.Index),[Math]::Max(0,$projects.Count - 1))
    $offset = [int]([Math]::Floor($selected / $pageSize) * $pageSize)
    for ($i=$offset; $i -lt [Math]::Min($projects.Count,$offset + $pageSize); $i++) {
        $project = $projects[$i]
        $date = try { ([datetime]$project.LastModifiedUtc).ToLocalTime().ToString('dd MMM yy HH:mm',[Globalization.CultureInfo]::InvariantCulture) } catch { 'Unknown' }
        $kind = if ($project.Kind -eq 'Unity') { 'Project' } else { [string]$project.Kind }
        $checked = $State.PSObject.Properties['Selected'] -and $State.Selected.ContainsKey([string]$project.Path)
        $cells = @("$(if ($checked) { '[x] ' } elseif ($i -eq $selected) { '>   ' } else { '    ' })$($project.RelativePath)")
        if ($wide) { $cells += @($kind,[string]$project.UnityVersion,[string]$project.PackageCount,$date) } elseif ($medium) { $cells += $date }
        $rendered = @()
        for ($j=0;$j -lt $cells.Count;$j++) {
            $safe = ConvertTo-VrcSetupSpectreText (Format-VrcSetupProjectCatalogCell -Text $cells[$j] -Width $widths[$j])
            $style = if ($i -eq $selected) { 'bold black on #75D7F7' } else { '#DDEAF2' }
            $rendered += [Spectre.Console.Markup]::new("[$style]${safe}[/]")
        }
        [void]$table.Rows.Add([Spectre.Console.Rendering.IRenderable[]]$rendered)
    }
    if ($projects.Count -eq 0) { $rows += [Spectre.Console.Text]::new('No matching projects. Backspace clears the search.') }
    else {
        $rows += $table
        $count = if ($State.PSObject.Properties['Selected']) { $State.Selected.Count } else { 0 }
        $rows += New-VrcSetupActionButton -Label $(if ($count) { "Edit packages in $count projects" } else { 'Open project' }) -Color '#75D7F7'
    }
    $rows += [Spectre.Console.Markup]::new('[#78909F]Space Select projects  ·  Up/Down Move  ·  F2 Options  ·  Esc Back[/]')
    return [Spectre.Console.Rows]::new([Spectre.Console.Rendering.IRenderable[]]$rows)
}

function Show-VrcSetupProjectCatalogSpectre {
    param([Parameter(Mandatory)]$Catalog, [Parameter(Mandatory)][string]$ScriptDir, $State)
    if (-not (Write-VrcSetupSpectreFrame -Title 'Project library' -ScriptDir $ScriptDir)) { return $null }
    if (-not $State) { $State = [pscustomobject]@{ Filter=''; Index=0; Done=$false; Result=$null } }
    $State.Done = $false
    $State.Result = [pscustomobject]@{Action='back';ProjectPath=$null}
    $live = [Spectre.Console.AnsiConsole]::Live((New-VrcSetupProjectLibraryRenderable -Catalog $Catalog -State $state)); $live.AutoClear = $true
    $visible = $true
    try {
        try { $visible = [Console]::CursorVisible; [Console]::CursorVisible=$false } catch { }
        $live.Start([System.Action[Spectre.Console.LiveDisplayContext]]{
            param($context)
            $context.Refresh()
            while (-not $state.Done) {
                $key = Read-VrcSetupNavigationKey -Context $context -Render { New-VrcSetupProjectLibraryRenderable -Catalog $Catalog -State $state }
                Update-VrcSetupLibraryKey -State $state -Key $key -Catalog $Catalog -PageSize ([Math]::Max(1,(Get-VrcSetupViewport).Height - 20))
                if (-not $state.Done) { $context.UpdateTarget((New-VrcSetupProjectLibraryRenderable -Catalog $Catalog -State $state)); $context.Refresh() }
            }
        })
    } finally { try { [Console]::CursorVisible=$visible } catch { } }
    return $state.Result
}
