# Helper: interactive menu with arrow keys and number keys support

function Test-VtSupported {
    # IMPORTANT: VT is opt-in.
    # Some hosts (es. PowerShell Extension / vecchia console) mostrano le escape come testo ("e[H...").
    # Set `VRCSETUP_TUI_VT=1` solo se sai che il tuo terminale le supporta.
    if ($env:VRCSETUP_TUI_VT -ne '1') { return $false }

    try {
        $ui = $Host.UI
        if ($ui -and ($ui | Get-Member -Name SupportsVirtualTerminal -MemberType Property -ErrorAction SilentlyContinue)) {
            return [bool]$ui.SupportsVirtualTerminal
        }
    } catch { }

    # Heuristic fallback for older hosts
    if (-not [string]::IsNullOrWhiteSpace($env:WT_SESSION)) { return $true }
    if (-not [string]::IsNullOrWhiteSpace($env:TERM) -and $env:TERM -ne 'dumb') { return $true }
    return $false
}

function Get-TuiTheme {
    return @{
        TitleFg     = 'Cyan'
        HeaderFg    = 'Gray'
        OptionFg    = 'White'
        SelectedFg  = 'White'
        SelectedBg  = 'DarkBlue'
        ActionFg    = 'Cyan'
        ActionSelectedFg = 'White'
        ActionSelectedBg = 'DarkBlue'
        BackFg      = 'Red'
        BackSelectedFg = 'White'
        BackSelectedBg = 'DarkRed'
        MutedFg     = 'DarkGray'
        InputFg     = 'Gray'
    }
}

function Test-IsBackOption {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    $t = $Text.Trim()
    return ($t -eq 'Back' -or $t -eq 'Cancel' -or $t -eq 'Exit')
}

function Test-IsActionOption {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    $t = $Text.Trim()
    if (Test-IsBackOption -Text $t) { return $true }
    if ($t -eq 'Add package') { return $true }
    if ($t -eq 'Enter manually' -or $t -eq 'Enter exact version...') { return $true }
    if ($t -eq 'Enter package name manually') { return $true }
    if ($t -eq 'Search packages' -or $t -eq 'SEARCH PACKAGES') { return $true }
    if ($t -eq 'Set filter' -or $t -eq 'Clear filter' -or $t -eq 'Search versions...' -or $t -eq 'Clear search') { return $true }
    if ($t -eq 'Jump to range' -or $t -eq 'Go to version range...') { return $true }
    if ($t -eq '< Prev page' -or $t -eq 'Next page >' -or $t -eq '< Previous page') { return $true }
    if ($t -like '(*)') { return $true }
    return $false
}

function Test-IsFooterOption {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    return (Test-IsBackOption -Text $Text) -or (Test-IsActionOption -Text $Text)
}

function Get-ConsoleWidthSafe {
    try {
        $w = [Console]::WindowWidth
        if ($w -gt 0) { return $w }
    } catch { }
    return 120
}

function Write-ConsoleAt {
    param(
        [int]$Left,
        [int]$Top,
        [string]$Text,
        [ConsoleColor]$ForegroundColor,
        [ConsoleColor]$BackgroundColor,
        [switch]$ClearToEnd
    )

    $oldFg = [Console]::ForegroundColor
    $oldBg = [Console]::BackgroundColor
    try {
        [Console]::SetCursorPosition([Math]::Max(0, $Left), [Math]::Max(0, $Top))
    } catch {
        return
    }

    try {
        if ($PSBoundParameters.ContainsKey('ForegroundColor')) { [Console]::ForegroundColor = $ForegroundColor }
        if ($PSBoundParameters.ContainsKey('BackgroundColor')) { [Console]::BackgroundColor = $BackgroundColor }

        $out = if ($null -eq $Text) { "" } else { [string]$Text }
        if ($ClearToEnd) {
            $w = Get-ConsoleWidthSafe
            if ($out.Length -lt ($w - 1)) {
                $out = $out + (' ' * (($w - 1) - $out.Length))
            } else {
                $out = $out.Substring(0, [Math]::Max(0, $w - 1))
            }
        }
        [Console]::Write($out)
    } catch {
    } finally {
        try {
            [Console]::ForegroundColor = $oldFg
            [Console]::BackgroundColor = $oldBg
        } catch { }
    }
}

function Clear-TuiScreen {
    param(
        [bool]$UseVt
    )
    try {
        if ($UseVt) {
            $esc = [char]27
            [Console]::Write("${esc}[H${esc}[2J")
        } else {
            [Console]::Clear()
        }
    } catch {
        try { Clear-Host } catch { }
    }
}

function Start-TuiFrame {
    param(
        [bool]$UseVt
    )
    $state = [pscustomobject]@{
        UseVt = $UseVt
        CursorWasVisible = $true
        AltBuffer = $false
        RenderTop = 0
    }

    try {
        $state.CursorWasVisible = [Console]::CursorVisible
        [Console]::CursorVisible = $false
    } catch { }

    # NOTE: alternate screen buffer disabled (caused literal escape text in some hosts)

    try {
        [Console]::SetCursorPosition(0, 0)
        $state.RenderTop = 0
    } catch {
        $state.RenderTop = 0
    }

    return $state
}

function Stop-TuiFrame {
    param(
        $State
    )

    if ($null -eq $State) { return }
    try {
        [Console]::CursorVisible = [bool]$State.CursorWasVisible
    } catch { }

    # After a TUI interaction, the cursor may still be parked on an option line.
    # Move it to a safe spot so subsequent Write-Host/Read-Host doesn't overwrite the menu.
    try {
        $h = [Console]::WindowHeight
        if ($h -gt 0) {
            [Console]::SetCursorPosition(0, [Math]::Max(0, $h - 1))
            [Console]::WriteLine('')
        }
    } catch { }
}

function Write-TuiFrame {
    param(
        [string]$Frame,
        $State
    )

    try {
        [Console]::SetCursorPosition(0, $State.RenderTop)
        if ($State.UseVt) {
            # Home + clear-to-end to prevent leftovers
            $esc = [char]27
            Write-Host ("${esc}[H") -NoNewline
            Write-Host $Frame -NoNewline
            Write-Host ("${esc}[J") -NoNewline
        } else {
            Write-Host $Frame -NoNewline
        }
    } catch {
        Clear-Host
        Write-Host $Frame
    }
}

function Show-Menu {
    param(
        [string]$Title = "",
        [string]$Header = "",
        [string[]]$Options,
        [int]$Current = 0,
        [bool]$AllowCancel = $true,
        [bool]$EnableHorizontalNav = $false,
        [ValidateRange(4, 30)][int]$MaxVisible = 14
    )

    if (-not $Options) { return -1 }

    # The default interactive experience is Spectre.Console.  The legacy
    # renderer remains a narrow fallback for Windows PowerShell and redirected
    # output, where the bundled Spectre runtime is intentionally unavailable.
    $spectreMenu = Get-Command -Name 'Show-VrcSetupSpectreMenu' -ErrorAction SilentlyContinue
    if ($spectreMenu -and -not $EnableHorizontalNav) {
        $spectreResult = Show-VrcSetupSpectreMenu -Title $Title -Header $Header -Options $Options -Current $Current -AllowCancel:$AllowCancel -EnableHorizontalNav:$EnableHorizontalNav -MaxVisible $MaxVisible
        if ($null -ne $spectreResult) { return [int]$spectreResult }
    }

    $theme = Get-TuiTheme
    $tui = Start-TuiFrame -UseVt (Test-VtSupported)
    try {
        Clear-TuiScreen -UseVt $tui.UseVt
        $row = 0

        # Title
        if ($Title) {
            foreach ($line in ($Title -split "`r?`n")) {
                $row++
                Write-ConsoleAt -Left 0 -Top $row -Text $line -ForegroundColor $theme.TitleFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
            }
            $row += 2
        } else {
            $row++
        }

        # Header
        if ($Header) {
            foreach ($line in ($Header -split "`r?`n")) {
                Write-ConsoleAt -Left 0 -Top $row -Text $line -ForegroundColor $theme.HeaderFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
                $row++
            }
            $row += 1
        }

        $optionsTop = $row

        $spacerLines = 2
        $footerStart = $Options.Count
        for ($i = $Options.Count - 1; $i -ge 0; $i--) {
            if (Test-IsFooterOption -Text $Options[$i]) {
                $footerStart = $i
            } else {
                break
            }
        }
        $hasFooter = ($footerStart -lt $Options.Count)

        $rowForIndex = @()
        for ($i = 0; $i -lt $Options.Count; $i++) {
            $extra = if ($hasFooter -and $i -ge $footerStart) { $spacerLines } else { 0 }
            $rowForIndex += ($optionsTop + $i + $extra)
        }

        if ($hasFooter) {
            Write-ConsoleAt -Left 0 -Top ($optionsTop + $footerStart) -Text "" -ForegroundColor $theme.MutedFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
            Write-ConsoleAt -Left 0 -Top ($optionsTop + $footerStart + 1) -Text "" -ForegroundColor $theme.MutedFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
        }

        function Render-MenuLine {
            param(
                [int]$Index,
                [bool]$Selected
            )
            $prefix = if ($Selected) { " > " } else { "   " }
            $text = $prefix + [string]$Options[$Index]

            $top = $rowForIndex[$Index]
            $isBack = Test-IsBackOption -Text $Options[$Index]
            $isAction = Test-IsActionOption -Text $Options[$Index]

            if ($Selected) {
                if ($isBack) {
                    Write-ConsoleAt -Left 0 -Top $top -Text $text -ForegroundColor $theme.BackSelectedFg -BackgroundColor $theme.BackSelectedBg -ClearToEnd
                } elseif ($isAction) {
                    Write-ConsoleAt -Left 0 -Top $top -Text $text -ForegroundColor $theme.ActionSelectedFg -BackgroundColor $theme.ActionSelectedBg -ClearToEnd
                } else {
                    Write-ConsoleAt -Left 0 -Top $top -Text $text -ForegroundColor $theme.SelectedFg -BackgroundColor $theme.SelectedBg -ClearToEnd
                }
            } else {
                if ($isBack) {
                    Write-ConsoleAt -Left 0 -Top $top -Text $text -ForegroundColor $theme.BackFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
                } elseif ($isAction) {
                    Write-ConsoleAt -Left 0 -Top $top -Text $text -ForegroundColor $theme.ActionFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
                } else {
                    Write-ConsoleAt -Left 0 -Top $top -Text $text -ForegroundColor $theme.OptionFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
                }
            }
        }

        for ($i = 0; $i -lt $Options.Count; $i++) {
            Render-MenuLine -Index $i -Selected ($i -eq $Current)
        }

        while ($true) {
            $keyInfo = [Console]::ReadKey($true)

            $prev = $Current
            $moved = $false

            switch ($keyInfo.Key) {
                'UpArrow'    { if ($Current -gt 0) { $Current--; $moved = $true } }
                'DownArrow'  { if ($Current -lt ($Options.Count - 1)) { $Current++; $moved = $true } }
                'LeftArrow'  { if ($EnableHorizontalNav) { return -2 } }
                'RightArrow' { if ($EnableHorizontalNav) { return -3 } }
                'Enter'      { return $Current }
                'Escape'     { if ($AllowCancel) { return -1 } }
                default {
                    $char = $keyInfo.KeyChar
                    if ([char]::IsDigit($char)) {
                        try {
                            $digit = [int]$char
                            if ($digit -gt 0 -and $digit -le $Options.Count) {
                                return ($digit - 1)
                            }
                        } catch { }
                    }
                }
            }

            if ($moved -and $prev -ne $Current) {
                Render-MenuLine -Index $prev -Selected $false
                Render-MenuLine -Index $Current -Selected $true
            }
        }
    } finally {
        Stop-TuiFrame -State $tui
    }
}

# Helper: interactive filterable menu (type-to-filter + arrows)
function Show-MenuFilter {
    param(
        [string]$Title = "",
        [string]$Header = "",
        [string[]]$Options,
        [string[]]$PinnedOptions = @(),
        [string]$Placeholder = "Type to filter...",
        [bool]$AllowCancel = $true,
        [int]$MaxVisible = 15,
        [bool]$EnterReturnsFilterWhenNoMatch = $true,
        [bool]$ShowListMarkers = $false,
        [bool]$ReturnSelectionWithFilter = $false
    )

    if (-not $Options) { return $null }

    $spectreFilterMenu = Get-Command -Name 'Show-VrcSetupSpectreFilterMenu' -ErrorAction SilentlyContinue
    if ($spectreFilterMenu) {
        $spectreResult = Show-VrcSetupSpectreFilterMenu -Title $Title -Header $Header -Options $Options -PinnedOptions $PinnedOptions -Placeholder $Placeholder -AllowCancel:$AllowCancel -MaxVisible $MaxVisible -EnterReturnsFilterWhenNoMatch:$EnterReturnsFilterWhenNoMatch -ShowListMarkers:$ShowListMarkers -ReturnSelectionWithFilter:$ReturnSelectionWithFilter
        if ($null -ne $spectreResult) { return $spectreResult }
        if ($AllowCancel) { return $null }
    }

    $filter = ""
    $current = 0
    $offset = 0

    $cachedFilter = $null
    $cachedMatches = @()
    $cachedUnfiltered = @()
    $lastOptionsHash = 0
    $lastPinnedHash = 0

    $theme = Get-TuiTheme
    $tui = Start-TuiFrame -UseVt (Test-VtSupported)
    $needsFullRender = $true
    $cachedToShow = @()
    $optionsTop = 0
    $lastRenderedCount = 0

    $spacerLines = 2

    function New-MenuFilterResult {
        param([object]$Selection)

        if ($ReturnSelectionWithFilter) {
            return [pscustomobject]@{
                Selection = $Selection
                Filter    = $filter
            }
        }

        return $Selection
    }

    function Render-FilterLine {
        param(
            [int]$Index,
            [bool]$Selected
        )
        if ($Index -lt 0 -or $Index -ge $cachedToShow.Count) { return }
        $textVal = [string]$cachedToShow[$Index]

        $pinnedCount = @($PinnedOptions).Count
        $rowOffset = if ($pinnedCount -gt 0 -and $Index -ge $pinnedCount) { $spacerLines } else { 0 }
        $top = $optionsTop + $Index + $rowOffset

        $isBack = Test-IsBackOption -Text $textVal
        $isAction = Test-IsActionOption -Text $textVal
        $displayText = $textVal
        if ($ShowListMarkers -and (-not $isBack) -and (-not $isAction)) {
            $displayText = "- ${textVal}"
        }

        $lineText = " > ${displayText}"
        if (-not $Selected) { $lineText = "   ${displayText}" }

        if ($Selected) {
            if ($isBack) {
                Write-ConsoleAt -Left 0 -Top $top -Text $lineText -ForegroundColor $theme.BackSelectedFg -BackgroundColor $theme.BackSelectedBg -ClearToEnd
            } elseif ($isAction) {
                Write-ConsoleAt -Left 0 -Top $top -Text $lineText -ForegroundColor $theme.ActionSelectedFg -BackgroundColor $theme.ActionSelectedBg -ClearToEnd
            } else {
                Write-ConsoleAt -Left 0 -Top $top -Text $lineText -ForegroundColor $theme.SelectedFg -BackgroundColor $theme.SelectedBg -ClearToEnd
            }
        } else {
            if ($isBack) {
                Write-ConsoleAt -Left 0 -Top $top -Text $lineText -ForegroundColor $theme.BackFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
            } elseif ($isAction) {
                Write-ConsoleAt -Left 0 -Top $top -Text $lineText -ForegroundColor $theme.ActionFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
            } else {
                Write-ConsoleAt -Left 0 -Top $top -Text $lineText -ForegroundColor $theme.OptionFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
            }
        }
    }

    try {
    while ($true) {
        # Cache recomputation only when filter/options/pinned change
        $optionsHash = 0
        $pinnedHash = 0
        try { $optionsHash = ($Options -join "`n").GetHashCode() } catch { $optionsHash = 0 }
        try { $pinnedHash = ($PinnedOptions -join "`n").GetHashCode() } catch { $pinnedHash = 0 }

        $mustRecompute = $needsFullRender -or ($cachedFilter -ne $filter) -or ($optionsHash -ne $lastOptionsHash) -or ($pinnedHash -ne $lastPinnedHash)
        if ($mustRecompute) {
            $filterTokens = @()
            if (-not [string]::IsNullOrWhiteSpace($filter)) {
                $filterTokens = $filter.Split(@(' ',"\t"), [System.StringSplitOptions]::RemoveEmptyEntries)
            }

            $cachedUnfiltered = @($Options | Where-Object { $PinnedOptions -notcontains $_ })

            $baseMatches = if ($filterTokens.Count -eq 0) {
                $cachedUnfiltered
            } else {
                $cachedUnfiltered | Where-Object {
                    $candidate = $_
                    $ok = $true
                    foreach ($t in $filterTokens) {
                        if ($candidate -notlike "*${t}*") { $ok = $false; break }
                    }
                    $ok
                }
            }

            $cachedMatches = @($baseMatches)
            $cachedFilter = $filter
            $lastOptionsHash = $optionsHash
            $lastPinnedHash = $pinnedHash
            $offset = 0
            $current = 0
        }

        # Paginazione: pinned sempre visibili; il resto è paginato.
        $pinned = @($PinnedOptions)
        $rest = @($cachedMatches)

        $pageCapacity = [Math]::Max(1, ($MaxVisible - $pinned.Count))
        $restTotal = $rest.Count
        $maxOffset = if ($restTotal -le $pageCapacity) { 0 } else { [Math]::Max(0, $restTotal - $pageCapacity) }
        if ($offset -gt $maxOffset) { $offset = $maxOffset }
        if ($offset -lt 0) { $offset = 0 }

        $restPage = @()
        if ($restTotal -gt 0) {
            $restPage = @($rest | Select-Object -Skip $offset -First $pageCapacity)
        }

        $matches = @($pinned) + @($restPage)

        if ($matches.Count -eq 0) {
            $current = 0
        } else {
            if ($current -lt 0) { $current = 0 }
            if ($current -ge $matches.Count) { $current = [Math]::Max(0, $matches.Count - 1) }
        }

        if ($needsFullRender) {
            Clear-TuiScreen -UseVt $tui.UseVt

            $row = 0
            $row++
            if ($Title) {
                Write-ConsoleAt -Left 0 -Top $row -Text $Title -ForegroundColor $theme.TitleFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
                $row += 2
            }
            if ($Header) {
                Write-ConsoleAt -Left 0 -Top $row -Text $Header -ForegroundColor $theme.HeaderFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
                $row += 2
            }

            $filterLine = if ([string]::IsNullOrWhiteSpace($filter)) { $Placeholder } else { $filter }
            Write-ConsoleAt -Left 0 -Top $row -Text ("Filter: {0}" -f $filterLine) -ForegroundColor $theme.InputFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
            $row++
            $totalLabel = ($pinned.Count + $restTotal)
            $pageLabel = ""
            if ($restTotal -gt $pageCapacity) {
                $pageIdx = [Math]::Floor($offset / $pageCapacity) + 1
                $pageCount = [Math]::Ceiling($restTotal / [double]$pageCapacity)
                $pageLabel = "  |  Page ${pageIdx}/${pageCount} (Left/Right)"
            }
            Write-ConsoleAt -Left 0 -Top $row -Text ("Matches: {0}{1}" -f $totalLabel, $pageLabel) -ForegroundColor $theme.MutedFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
            $row += 2

            $cachedToShow = @($matches | Select-Object -First $MaxVisible)
            $optionsTop = $row
            $lastRenderedCount = $cachedToShow.Count

            $pinnedCount = @($PinnedOptions).Count
            if ($pinnedCount -gt 0 -and $cachedToShow.Count -gt $pinnedCount) {
                Write-ConsoleAt -Left 0 -Top ($optionsTop + $pinnedCount) -Text "" -ForegroundColor $theme.MutedFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
                Write-ConsoleAt -Left 0 -Top ($optionsTop + $pinnedCount + 1) -Text "" -ForegroundColor $theme.MutedFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
            }

            for ($i = 0; $i -lt $cachedToShow.Count; $i++) {
                Render-FilterLine -Index $i -Selected ($i -eq $current)
            }

            if ($matches.Count -gt $MaxVisible) {
                Write-ConsoleAt -Left 0 -Top ($optionsTop + $MaxVisible + 1) -Text ("... showing first {0} results" -f $MaxVisible) -ForegroundColor $theme.MutedFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
            }

            $needsFullRender = $false
        }

        $keyInfo = [Console]::ReadKey($true)
        switch ($keyInfo.Key) {
            'UpArrow'   {
                if ($current -gt 0) {
                    $prev = $current
                    $current--
                    Render-FilterLine -Index $prev -Selected $false
                    Render-FilterLine -Index $current -Selected $true
                }
            }
            'DownArrow' {
                $maxIdx = [Math]::Min($MaxVisible, $matches.Count) - 1
                if ($current -lt $maxIdx) {
                    $prev = $current
                    $current++
                    Render-FilterLine -Index $prev -Selected $false
                    Render-FilterLine -Index $current -Selected $true
                }
            }
            'LeftArrow' {
                if ($restTotal -gt $pageCapacity -and $offset -gt 0) {
                    $offset = [Math]::Max(0, $offset - $pageCapacity)
                    $needsFullRender = $true
                }
            }
            'RightArrow' {
                if ($restTotal -gt $pageCapacity -and ($offset + $pageCapacity) -lt $restTotal) {
                    $offset = [Math]::Min($maxOffset, ($offset + $pageCapacity))
                    $needsFullRender = $true
                }
            }
            'Enter' {
                if ($matches.Count -eq 0) {
                    if ($EnterReturnsFilterWhenNoMatch -and (-not [string]::IsNullOrWhiteSpace($filter))) {
                        return (New-MenuFilterResult -Selection $filter)
                    }
                    continue
                }

                return (New-MenuFilterResult -Selection $cachedToShow[$current])
            }
            'Escape' { if ($AllowCancel) { return $null } }
            'Backspace' {
                if ($filter.Length -gt 0) {
                    $filter = $filter.Substring(0, $filter.Length - 1)
                    $current = 0
                    $offset = 0
                    $needsFullRender = $true
                }
            }
            default {
                $c = $keyInfo.KeyChar
                if ($c -and (-not [char]::IsControl($c))) {
                    $filter += $c
                    $current = 0
                    $offset = 0
                    $needsFullRender = $true
                }
            }
        }
    }
    } finally {
        Stop-TuiFrame -State $tui
    }
}

# Helper: checklist paginata (checkbox) con Space toggle, A/N/I, Left/Right paging.
function Show-ChecklistPaged {
    param(
        [string]$Title = "",
        [string]$Header = "",
        $Items,
        [scriptblock]$ToLabel,
        [string]$PromptTitle = 'Choose items',
        [bool]$DefaultSelected = $true,
        [int]$MaxVisible = 15,
        [bool]$AllowCancel = $true
    )

    if ($null -eq $Items) { return @() }
    $itemsArr = @($Items)
    if ($itemsArr.Count -eq 0) { return @() }

    $spectreChecklist = Get-Command -Name 'Show-VrcSetupSpectreChecklist' -ErrorAction SilentlyContinue
    if ($spectreChecklist) {
        $spectreResult = Show-VrcSetupSpectreChecklist -Items $itemsArr -ToLabel $ToLabel -Title $Title -PromptTitle $PromptTitle -Header $Header -DefaultSelected:$DefaultSelected -MaxVisible $MaxVisible -AllowCancel:$AllowCancel
        if ($null -ne $spectreResult -or $AllowCancel) { return $spectreResult }
    }

    $theme = Get-TuiTheme
    $tui = Start-TuiFrame -UseVt (Test-VtSupported)
    $needsFullRender = $true

    $current = 0
    $offset = 0
    $selected = @{}
    for ($i = 0; $i -lt $itemsArr.Count; $i++) { $selected[$i] = [bool]$DefaultSelected }

    function Get-Label {
        param($Item, [int]$Index)
        if ($ToLabel) {
            try { return [string](& $ToLabel $Item $Index) } catch { }
        }
        return [string]$Item
    }

    function Render-Line {
        param(
            [int]$Index,
            [int]$Top,
            [bool]$IsCursor
        )
        $item = $itemsArr[$Index]
        $isChecked = $false
        try { $isChecked = [bool]$selected[$Index] } catch { $isChecked = $false }
        $box = if ($isChecked) { "[x]" } else { "[ ]" }
        $label = Get-Label -Item $item -Index $Index
        $prefix = if ($IsCursor) { " > " } else { "   " }
        $text = "${prefix}${box} ${label}"

        if ($IsCursor) {
            Write-ConsoleAt -Left 0 -Top $Top -Text $text -ForegroundColor $theme.SelectedFg -BackgroundColor $theme.SelectedBg -ClearToEnd
        } else {
            Write-ConsoleAt -Left 0 -Top $Top -Text $text -ForegroundColor $theme.OptionFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
        }
    }

    try {
        while ($true) {
            $pageCapacity = [Math]::Max(1, $MaxVisible)
            $total = $itemsArr.Count
            $maxOffset = if ($total -le $pageCapacity) { 0 } else { [Math]::Max(0, $total - $pageCapacity) }
            if ($offset -gt $maxOffset) { $offset = $maxOffset }
            if ($offset -lt 0) { $offset = 0 }

            $pageItems = @($itemsArr | Select-Object -Skip $offset -First $pageCapacity)
            $visibleCount = $pageItems.Count
            if ($visibleCount -eq 0) { return @() }

            if ($current -lt 0) { $current = 0 }
            if ($current -ge $visibleCount) { $current = [Math]::Max(0, $visibleCount - 1) }

            if ($needsFullRender) {
                Clear-TuiScreen -UseVt $tui.UseVt

                $row = 0
                $row++
                if ($Title) {
                    Write-ConsoleAt -Left 0 -Top $row -Text $Title -ForegroundColor $theme.TitleFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
                    $row += 2
                }

                $help = "Space=toggle  |  A=all  N=none  I=invert  |  Left/Right=page  |  Enter=confirm  Esc=back"
                $hdr = if ($Header) { "${Header}`n${help}" } else { $help }
                Write-ConsoleAt -Left 0 -Top $row -Text $hdr -ForegroundColor $theme.HeaderFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
                $row += 3

                $pageIdx = [Math]::Floor($offset / $pageCapacity) + 1
                $pageCount = [Math]::Ceiling($total / [double]$pageCapacity)
                Write-ConsoleAt -Left 0 -Top $row -Text ("Projects: {0}  |  Page {1}/{2} (Left/Right)" -f $total, $pageIdx, $pageCount) -ForegroundColor $theme.MutedFg -BackgroundColor ([Console]::BackgroundColor) -ClearToEnd
                $row += 2

                $optionsTop = $row
                for ($i = 0; $i -lt $visibleCount; $i++) {
                    $globalIndex = $offset + $i
                    Render-Line -Index $globalIndex -Top ($optionsTop + $i) -IsCursor ($i -eq $current)
                }

                $needsFullRender = $false
            }

            $keyInfo = [Console]::ReadKey($true)
            switch ($keyInfo.Key) {
                'UpArrow' {
                    if ($current -gt 0) {
                        $prev = $current
                        $current--
                        Render-Line -Index ($offset + $prev) -Top ($optionsTop + $prev) -IsCursor $false
                        Render-Line -Index ($offset + $current) -Top ($optionsTop + $current) -IsCursor $true
                    }
                }
                'DownArrow' {
                    if ($current -lt ($visibleCount - 1)) {
                        $prev = $current
                        $current++
                        Render-Line -Index ($offset + $prev) -Top ($optionsTop + $prev) -IsCursor $false
                        Render-Line -Index ($offset + $current) -Top ($optionsTop + $current) -IsCursor $true
                    }
                }
                'LeftArrow' {
                    if ($total -gt $pageCapacity -and $offset -gt 0) {
                        $offset = [Math]::Max(0, $offset - $pageCapacity)
                        $current = 0
                        $needsFullRender = $true
                    }
                }
                'RightArrow' {
                    if ($total -gt $pageCapacity -and ($offset + $pageCapacity) -lt $total) {
                        $offset = [Math]::Min($maxOffset, ($offset + $pageCapacity))
                        $current = 0
                        $needsFullRender = $true
                    }
                }
                'Spacebar' {
                    $idx = $offset + $current
                    $selected[$idx] = -not [bool]$selected[$idx]
                    Render-Line -Index $idx -Top ($optionsTop + $current) -IsCursor $true
                }
                'A' {
                    for ($i = 0; $i -lt $itemsArr.Count; $i++) { $selected[$i] = $true }
                    $needsFullRender = $true
                }
                'N' {
                    for ($i = 0; $i -lt $itemsArr.Count; $i++) { $selected[$i] = $false }
                    $needsFullRender = $true
                }
                'I' {
                    for ($i = 0; $i -lt $itemsArr.Count; $i++) { $selected[$i] = -not [bool]$selected[$i] }
                    $needsFullRender = $true
                }
                'Enter' {
                    $picked = @()
                    for ($i = 0; $i -lt $itemsArr.Count; $i++) {
                        if ([bool]$selected[$i]) { $picked += $itemsArr[$i] }
                    }
                    return $picked
                }
                'Escape' {
                    if ($AllowCancel) { return $null }
                }
            }
        }
    } finally {
        Stop-TuiFrame -State $tui
    }
}

