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
    if ($Text -match '^(Exit|Reset|Delete|Remove)') { return "[#FB7185]$safe[/]" }
    if ($Text -match '^(Create|Manage|Add|Apply|Start|Open|Search|Refresh|Choose|Change|Set)') { return "[#75D7F7]$safe[/]" }
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
        [Parameter(Mandatory)][string[]]$Options,
        [int]$Current = 0,
        [bool]$AllowCancel = $true,
        [bool]$EnableHorizontalNav = $false,
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
    $prompt.Title = '[#DDEAF2]Choose an action[/]'
    $prompt.PageSize = [Math]::Min(14, [Math]::Max(4, $Options.Count))
    $prompt.WrapAround = $true
    $prompt.HighlightStyle = New-VrcSetupSpectreStyle -Foreground '#0B1520' -Background '#75D7F7'
    $prompt.MoreChoicesText = '[#78909F]Use Up/Down then Enter. Type to jump through a long list.[/]'
    $keyboardHint = if ($AllowCancel) { 'Up/Down to move  ·  Enter to select  ·  Esc or Back to return' } else { 'Up/Down to move  ·  Enter to select' }
    [Spectre.Console.AnsiConsole]::MarkupLine("[#78909F]${keyboardHint}[/]")
    $choiceIndex = @{}
    for ($index = 0; $index -lt $Options.Count; $index++) {
        $displayChoice = ConvertTo-VrcSetupSpectreChoice ([string]$Options[$index])
        $choiceIndex[$displayChoice] = $index
        [void]$prompt.AddChoice($displayChoice)
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
        'Choose an action or package.'
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

    $packages = if ([string]$Project.Status -eq 'Ready') {
        '{0} packages' -f $Project.PackageCount
    } else {
        [string]$Project.Status
    }
    $updated = 'Unknown'
    try {
        $updated = ([datetime]::Parse([string]$Project.LastModifiedUtc)).ToLocalTime().ToString('dd MMM yyyy HH:mm', [Globalization.CultureInfo]::InvariantCulture)
    } catch { }

    return ('{0,2}  {1}  {2}  {3}  {4}  {5}' -f
        $Index,
        (Format-VrcSetupProjectCatalogCell -Text ([string]$Project.RelativePath) -Width 28),
        (Format-VrcSetupProjectCatalogCell -Text ([string]$Project.Kind) -Width 11),
        (Format-VrcSetupProjectCatalogCell -Text ([string]$Project.UnityVersion) -Width 12),
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
    [void](Write-VrcSetupSpectreFrame -Title 'Project library' -Header ("Projects under: {0}`nFound: {1}  |  Order: {2}" -f $Catalog.RootPath, $Catalog.ProjectCount, $sortLabel) -ScriptDir $ScriptDir)
    $scanSummary = "Scan: $($Catalog.DurationMs) ms | cache reused: $($Catalog.CacheHits) | refreshed: $($Catalog.Refreshed)"
    [Spectre.Console.AnsiConsole]::MarkupLine("[#78909F]$(ConvertTo-VrcSetupSpectreText $scanSummary)[/]")
    [Spectre.Console.AnsiConsole]::WriteLine()

    $prompt = [Spectre.Console.SelectionPrompt[string]]::new()
    $prompt.Title = '[#DDEAF2] #   Project                       Type         Unity         Packages         Updated[/]'
    $prompt.PageSize = [Math]::Min(16, [Math]::Max(4, $Catalog.ProjectCount + 1))
    $prompt.WrapAround = $true
    $prompt.HighlightStyle = New-VrcSetupSpectreStyle -Foreground '#0B1520' -Background '#75D7F7'
    $prompt.MoreChoicesText = '[#78909F]Use Up/Down to browse the project table. Type to jump to a project.[/]'
    $prompt.InstructionsText = '[#78909F]Up/Down to select a project  ·  Enter to manage it  ·  Esc to return[/]'

    $choiceActions = @{}
    $index = 1
    foreach ($project in @($Catalog.Projects)) {
        $displayChoice = ConvertTo-VrcSetupSpectreText (Format-VrcSetupProjectCatalogRow -Project $project -Index $index)
        $choiceActions[$displayChoice] = [pscustomobject]@{ Action = 'project'; ProjectPath = [string]$project.Path }
        [void]$prompt.AddChoice($displayChoice)
        $index++
    }

    $libraryActionsChoice = '[#75D7F7]Library actions…[/]'
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
    $prompt.InstructionsText = '[#78909F]Up/Down to move  ·  Space to toggle  ·  Enter to confirm  ·  Esc to return[/]'
    $prompt.MoreChoicesText = '[#78909F]Use Up/Down to browse the remaining projects.[/]'

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
    $index = Show-VrcSetupSpectreMenu -Title 'Project library actions' -Header 'Choose an action for the current project library.' -Options $labels -AllowCancel:$true -ScriptDir $ScriptDir
    if ($null -eq $index) {
        $index = Show-Menu -Title 'Project library actions' -Header 'Choose an action for the current project library.' -Options $labels
    }
    if ($index -lt 0) { return [pscustomobject]@{ Action = 'back'; ProjectPath = $null } }
    return $choices[$labels[$index]]
}
