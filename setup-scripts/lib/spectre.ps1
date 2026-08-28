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

function Invoke-VrcSetupSpectreStringPrompt {
    param([Parameter(Mandatory)]$Prompt)

    $method = [Spectre.Console.AnsiConsole].GetMethods() | Where-Object {
        $_.Name -eq 'Prompt' -and $_.IsGenericMethodDefinition -and $_.GetParameters().Count -eq 1
    } | Select-Object -First 1
    if (-not $method) { throw 'Spectre.Console prompt method was not found.' }
    $closedMethod = $method.MakeGenericMethod([string])
    return [string]$closedMethod.Invoke($null, @($Prompt))
}

function Show-VrcSetupProjectCatalogSpectre {
    param(
        [Parameter(Mandatory)]$Catalog,
        [Parameter(Mandatory)][string]$ScriptDir
    )

    if (-not (Initialize-VrcSetupSpectre -ScriptDir $ScriptDir)) { return $false }

    [Spectre.Console.AnsiConsole]::Clear()
    $rule = [Spectre.Console.Rule]::new('[aqua]VRChat Project Setup - Project library[/]')
    $rule.Justification = [Spectre.Console.Justify]::Left
    [Spectre.Console.AnsiConsole]::Write($rule)
    [Spectre.Console.AnsiConsole]::MarkupLine("[grey]Projects found under $(ConvertTo-VrcSetupSpectreText $Catalog.RootPath)[/]")
    [Spectre.Console.AnsiConsole]::WriteLine()

    if ($Catalog.ProjectCount -eq 0) {
        [Spectre.Console.AnsiConsole]::MarkupLine('[yellow]No Unity projects found.[/]')
    } else {
        $table = [Spectre.Console.Table]::new()
        $table.Border = [Spectre.Console.TableBorder]::Simple
        [void][Spectre.Console.TableExtensions]::AddColumns($table, [string[]]@('Project', 'Type', 'Unity', 'VPM'))
        foreach ($project in @($Catalog.Projects | Select-Object -First 18)) {
            $packageText = if ($project.Status -eq 'Ready') { [string]$project.PackageCount } else { [string]$project.Status }
            [void][Spectre.Console.TableExtensions]::AddRow($table, [string[]]@(
                (ConvertTo-VrcSetupSpectreText ([string]$project.RelativePath)),
                (ConvertTo-VrcSetupSpectreText ([string]$project.Kind)),
                (ConvertTo-VrcSetupSpectreText ([string]$project.UnityVersion)),
                (ConvertTo-VrcSetupSpectreText $packageText)
            ))
        }
        [Spectre.Console.AnsiConsole]::Write($table)
        if ($Catalog.ProjectCount -gt 18) {
            [Spectre.Console.AnsiConsole]::MarkupLine("[grey]... and $($Catalog.ProjectCount - 18) more projects.[/]")
        }
    }

    $scanSummary = "Scan: $($Catalog.DurationMs) ms | cache reused: $($Catalog.CacheHits) | refreshed: $($Catalog.Refreshed)"
    [Spectre.Console.AnsiConsole]::MarkupLine("[grey]$(ConvertTo-VrcSetupSpectreText $scanSummary)[/]")
    [Spectre.Console.AnsiConsole]::WriteLine()
    return $true
}

function Select-VrcSetupProjectCatalogAction {
    param(
        [Parameter(Mandatory)]$Catalog,
        [Parameter(Mandatory)][string]$ScriptDir
    )

    $choices = [ordered]@{}
    foreach ($project in @($Catalog.Projects)) {
        $label = "$($project.RelativePath)  |  $($project.Kind)  |  $($project.PackageCount) VPM"
        $uniqueLabel = $label
        $suffix = 2
        while ($choices.Contains($uniqueLabel)) {
            $uniqueLabel = "${label} (${suffix})"
            $suffix++
        }
        $choices[$uniqueLabel] = [pscustomobject]@{ Action = 'project'; ProjectPath = [string]$project.Path }
    }
    $choices['Refresh project scan'] = [pscustomobject]@{ Action = 'refresh'; ProjectPath = $null }
    $choices['Choose a different folder'] = [pscustomobject]@{ Action = 'manual'; ProjectPath = $null }
    $choices['Back'] = [pscustomobject]@{ Action = 'back'; ProjectPath = $null }

    if (Initialize-VrcSetupSpectre -ScriptDir $ScriptDir) {
        $prompt = [Spectre.Console.SelectionPrompt[string]]::new()
        $prompt.Title = '[white]Choose a project or action[/]'
        $prompt.PageSize = 14
        $prompt.MoreChoicesText = '[grey]Move with arrow keys, then press Enter[/]'
        foreach ($label in $choices.Keys) { [void]$prompt.AddChoice([string]$label) }
        $selected = Invoke-VrcSetupSpectreStringPrompt -Prompt $prompt
        return $choices[$selected]
    }

    $labels = @($choices.Keys)
    $index = Show-Menu -Title 'Project library' -Header "Projects: $($Catalog.ProjectCount)`nRoot: $($Catalog.RootPath)" -Options $labels
    if ($index -lt 0) { return [pscustomobject]@{ Action = 'back'; ProjectPath = $null } }
    return $choices[$labels[$index]]
}
