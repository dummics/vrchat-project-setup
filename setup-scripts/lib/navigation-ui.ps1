# Shared navigation and searchable project library, loaded after Spectre helpers.
function Get-VrcSetupViewport {
    param([int]$Width = 0, [int]$Height = 0)
    if ($Width -le 0) { $Width = try { [Console]::WindowWidth } catch { 0 } }
    if ($Height -le 0) { $Height = try { [Console]::WindowHeight } catch { 0 } }
    if ($Width -le 0) { $Width = [Spectre.Console.AnsiConsole]::Profile.Width }
    if ($Height -le 0) { $Height = 30 }
    return [pscustomobject]@{ Width = [Math]::Max(30, $Width); Height = [Math]::Max(16, $Height) }
}

function New-VrcSetupNavigationMenuRenderable {
    param([string[]]$Options, [int]$Current = 0, [int[]]$SectionBreaks = @(), [int]$MaxVisible = 14, [bool]$AllowCancel = $true)
    $rows = @()
    $viewport = Get-VrcSetupViewport
    $pageSize = [Math]::Max(3, [Math]::Min($MaxVisible, [Math]::Floor(($viewport.Height - 10) / 2)))
    $start = [int]([Math]::Floor($Current / $pageSize) * $pageSize)
    $end = [Math]::Min($Options.Count, $start + $pageSize)
    $footerStart = $Options.Count
    for ($i = $Options.Count - 1; $i -ge 0; $i--) {
        if (Test-IsFooterOption -Text $Options[$i]) { $footerStart = $i } else { break }
    }
    for ($i = $start; $i -lt $end; $i++) {
        if ($i -gt $start -and ($SectionBreaks -contains $i -or $i -eq $footerStart)) {
            $rule = [Spectre.Console.Rule]::new(); $rule.Style = New-VrcSetupSpectreStyle -Foreground '#4C6A7A'; $rows += $rule
        }
        $label = ConvertTo-VrcSetupSpectreText $Options[$i]
        $style = if ($i -eq $Current) { 'bold black on #75D7F7' } elseif ($Options[$i] -match '^(Reset|Clean|Remove|Delete)') { '#FB7185' } else { '#DDEAF2' }
        $rows += [Spectre.Console.Markup]::new("[$style]$(if ($i -eq $Current) { '> ' } else { '  ' })${label}[/]")
        $rows += [Spectre.Console.Text]::new(' ')
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
        'Enter' { if ($projects.Count -gt 0) { $State.Result=[pscustomobject]@{Action='project';ProjectPath=[string]$projects[$State.Index].Path}; $State.Done=$true } }
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
        $cells = @("$(if ($i -eq $selected) { '> ' } else { '  ' })$($project.RelativePath)")
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
        $project = $projects[$selected]
        $rows += [Spectre.Console.Markup]::new("[#78909F]$(ConvertTo-VrcSetupSpectreText ([string]$project.Path))[/]")
        $rows += [Spectre.Console.Markup]::new("[#DDEAF2]Unity $(ConvertTo-VrcSetupSpectreText ([string]$project.UnityVersion))  ·  $($project.PackageCount) packages  ·  $($selected + 1) / $($projects.Count)[/]")
        if ($project.UnityVersionSource -eq 'GeneratedProject' -or $project.PackageMetadataSource -eq 'EmbeddedPackages') { $rows += [Spectre.Console.Text]::new('Some project details were recovered from local files.') }
        $rows += New-VrcSetupActionButton -Label 'Manage selected project' -Color '#75D7F7'
    }
    $rows += [Spectre.Console.Markup]::new('[#78909F]Up/Down Move  ·  PgUp/PgDn Page  ·  F2 Options  ·  Esc Back[/]')
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
            while (-not $state.Done) {
                $key = Read-VrcSetupNavigationKey -Context $context -Render { New-VrcSetupProjectLibraryRenderable -Catalog $Catalog -State $state }
                Update-VrcSetupLibraryKey -State $state -Key $key -Catalog $Catalog -PageSize ([Math]::Max(1,(Get-VrcSetupViewport).Height - 20))
                if (-not $state.Done) { $context.UpdateTarget((New-VrcSetupProjectLibraryRenderable -Catalog $Catalog -State $state)); $context.Refresh() }
            }
        })
    } finally { try { [Console]::CursorVisible=$visible } catch { } }
    return $state.Result
}
