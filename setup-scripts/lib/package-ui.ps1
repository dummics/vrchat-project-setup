function Get-VrcSetupVersionChoices {
    param([string[]]$Versions, [string]$Filter = '', [bool]$IncludePrerelease = $false)
    return @($Versions | Where-Object {
        ($IncludePrerelease -or $_ -notmatch '-') -and
        ([string]::IsNullOrWhiteSpace($Filter) -or $_.IndexOf($Filter, [StringComparison]::OrdinalIgnoreCase) -ge 0)
    } | Select-Object -Unique)
}

function Get-VrcSetupConfirmedPackageSelection {
    param($Items, $Selected, [int]$FocusedIndex, [bool]$SingleSelection = $false)
    $itemsArray = @($Items)
    if ($itemsArray.Count -eq 0) { return @() }
    if ($SingleSelection -or @($Selected.Values | Where-Object { $_ }).Count -eq 0) {
        return @($itemsArray[$FocusedIndex])
    }
    return @(for ($i = 0; $i -lt $itemsArray.Count; $i++) {
        if ([bool]$Selected[$i]) { $itemsArray[$i] }
    })
}

function New-VrcSetupVersionPickerRenderable {
    param($State, [string[]]$Versions, [string]$CurrentVersion)
    $matches = @(Get-VrcSetupVersionChoices -Versions $Versions -Filter $State.Filter -IncludePrerelease:$State.Prerelease)
    $rows = @([Spectre.Console.Markup]::new("[#78909F]Filter: [#DDEAF2]$(ConvertTo-VrcSetupSpectreText $(if ($State.Filter) { $State.Filter } else { '(type to search)' }))[/]   Prereleases: $(if ($State.Prerelease) { 'shown' } else { 'hidden' })[/]"))
    $rows += [Spectre.Console.Text]::new(' ')
    $table = [Spectre.Console.Table]::new()
    $table.Border = [Spectre.Console.TableBorder]::Rounded
    foreach ($label in @('Version', 'Channel', 'Selection')) { [void]$table.AddColumn($label) }
    $choices = @('latest') + $matches
    $offset = [Math]::Floor($State.Index / 12) * 12
    $end = [Math]::Min($choices.Count - 1, $offset + 11)
    for ($i = $offset; $i -le $end; $i++) {
        $version = $choices[$i]
        $channel = if ($version -eq 'latest') { 'Newest compatible at apply time' } elseif ($version -match '-') { 'Prerelease' } else { 'Stable' }
        $selection = if ($version -eq $CurrentVersion) { 'Current choice' } elseif ($version -ne 'latest') { 'Pin this version' } else { 'Follow updates' }
        $style = if ($i -eq $State.Index) { 'black on #75D7F7' } else { '#DDEAF2' }
        $cells = @($version, $channel, $selection) | ForEach-Object { [Spectre.Console.Markup]::new("[${style}]$(ConvertTo-VrcSetupSpectreText $_)[/]") }
        [void]$table.Rows.Add([Spectre.Console.Rendering.IRenderable[]]$cells)
    }
    $rows += $table
    if ($matches.Count -eq 0) { $rows += [Spectre.Console.Markup]::new('[#F6C451]No matching versions. Backspace clears the filter; M enters a version.[/]') }
    $rows += [Spectre.Console.Text]::new(' ')
    $rows += New-VrcSetupActionButton -Label $(if ($choices[$State.Index] -eq 'latest') { 'Use latest compatible' } else { "Pin $($choices[$State.Index])" }) -Color '#75D7F7'
    $rows += [Spectre.Console.Markup]::new("[#78909F]$($matches.Count) versions  ·  Esc Back[/]")
    $rows += [Spectre.Console.Rule]::new()
    $rows += [Spectre.Console.Markup]::new('[#78909F]Navigate   Up/Down  ·  PgUp/PgDn     Search   Type  ·  Backspace[/]')
    $rows += [Spectre.Console.Markup]::new('[#78909F]Options    P Prereleases  ·  M Enter version[/]')
    return [Spectre.Console.Rows]::new([Spectre.Console.Rendering.IRenderable[]]$rows)
}

function Show-VrcSetupVersionPicker {
    param([string]$PackageName, [string]$CurrentVersion, [string[]]$Versions, [string]$ScriptDir)
    if (-not (Write-VrcSetupSpectreFrame -Title 'Choose package version' -Header "$(Get-VrcSetupFriendlyPackageName -PackageName $PackageName)  ·  ${PackageName}`nCurrent choice: ${CurrentVersion}" -ScriptDir $ScriptDir)) { return $null }
    $state = [pscustomobject]@{ Filter = ''; Prerelease = ($CurrentVersion -match '-'); Index = 0; Done = $false; Result = $null }
    $initialChoices = @('latest') + @(Get-VrcSetupVersionChoices -Versions $Versions -IncludePrerelease:$state.Prerelease)
    $currentIndex = [array]::IndexOf($initialChoices, $CurrentVersion)
    if ($currentIndex -ge 0) { $state.Index = $currentIndex }
    $live = [Spectre.Console.AnsiConsole]::Live((New-VrcSetupVersionPickerRenderable -State $state -Versions $Versions -CurrentVersion $CurrentVersion))
    $live.AutoClear = $true
    $cursorVisible = $true
    try {
        try { $cursorVisible = [Console]::CursorVisible; [Console]::CursorVisible = $false } catch { }
        $live.Start([System.Action[Spectre.Console.LiveDisplayContext]]{
            param($context)
            while (-not $state.Done) {
                $choices = @('latest') + @(Get-VrcSetupVersionChoices -Versions $Versions -Filter $state.Filter -IncludePrerelease:$state.Prerelease)
                $key = [Console]::ReadKey($true)
                switch ($key.Key) {
                    'UpArrow' { $state.Index = [Math]::Max(0, $state.Index - 1) }
                    'DownArrow' { $state.Index = [Math]::Min($choices.Count - 1, $state.Index + 1) }
                    'PageUp' { $state.Index = [Math]::Max(0, $state.Index - 12) }
                    'PageDown' { $state.Index = [Math]::Min($choices.Count - 1, $state.Index + 12) }
                    'Home' { $state.Index = 0 }
                    'End' { $state.Index = $choices.Count - 1 }
                    'Escape' { $state.Done = $true }
                    'Enter' { $state.Result = $choices[$state.Index]; $state.Done = $true }
                    'Backspace' { if ($state.Filter.Length -gt 0) { $state.Filter = $state.Filter.Substring(0, $state.Filter.Length - 1) }; $state.Index = 0 }
                    default {
                        if ([char]::ToUpperInvariant($key.KeyChar) -eq 'P') { $state.Prerelease = -not $state.Prerelease; $state.Index = 0 }
                        elseif ([char]::ToUpperInvariant($key.KeyChar) -eq 'M') { $state.Result = '__manual__'; $state.Done = $true }
                        elseif ($key.KeyChar -and -not [char]::IsControl($key.KeyChar)) { $state.Filter += $key.KeyChar; $state.Index = 0 }
                    }
                }
                if (-not $state.Done) {
                    $context.UpdateTarget((New-VrcSetupVersionPickerRenderable -State $state -Versions $Versions -CurrentVersion $CurrentVersion))
                    $context.Refresh()
                }
            }
        })
    } finally { try { [Console]::CursorVisible = $cursorVisible } catch { } }
    return [pscustomobject]@{ Version = $state.Result }
}
