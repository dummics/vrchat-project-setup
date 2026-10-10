# Show a progress line for a background process, with CPU, RAM and log tail
function Get-VrcSetupChangedLogTail {
    param([string]$LogFile, [hashtable]$State)
    if ([string]::IsNullOrWhiteSpace($LogFile)) { return $null }
    try {
        $info = [IO.FileInfo]::new($LogFile)
        if (-not $info.Exists) { $State.Remove('Stamp'); return $null }
        $stamp = "$($info.FullName):$($info.Length):$($info.LastWriteTimeUtc.Ticks)"
        if ($State['Stamp'] -eq $stamp) { return $null }
        $tail = Get-Content -LiteralPath $LogFile -Tail 1 -ErrorAction Stop
        $State['Stamp'] = $stamp
        return $tail
    } catch { return $null }
}

function Show-ProcessProgress {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$LogFile,
        [string]$Prefix = "[Process]",
        [int]$PollMs = 250,
        [switch]$AllowCancel,
        [scriptblock]$OnCancel,
        [string]$CancelPrompt = "Cancel this step only? (y/N)",
        [int]$ProgressId = 2,
        [int]$ParentProgressId = 0
    )

    function Stop-ProcessTree {
        param([int]${Pid})
        if (${Pid} -le 0) { return }
        try {
            # Most reliable on Windows for Unity (kills child processes too)
            & taskkill /PID ${Pid} /T /F | Out-Null
            return
        } catch { }
        try { Stop-Process -Id ${Pid} -Force -ErrorAction SilentlyContinue } catch { }
    }

    $startTime = Get-Date
    $lastLog = ""
    $logReadState = @{}
    $lastStatsUpdate = Get-Date
    $cpuPercent = 0
    $memoryMB = 0

    # Use a fixed Progress Id so terminals (especially VSCode) don't stack/reposition.
    # Default to 2 so callers can keep Id=1 as an overall sticky progress.
    $progressId = $ProgressId

    $useWriteProgress = $true
    if ($env:VRCSETUP_PROGRESS_PLAIN -eq '1') { $useWriteProgress = ${false} }
    # The Spectre panel captures this stream; CPU/RAM/times go on one pinned status
    # line and only real Unity log changes become rows. Sentinel literal is shared
    # with New-VrcSetupProgressRenderable / Invoke-VrcSetupSpectreOperation.
    $embedded = ($env:VRCSETUP_EMBEDDED_PROGRESS -eq '1')
    if ($embedded) { $AllowCancel = ${false} }
    try {
        if (${null} -eq ${Host} -or ${null} -eq ${Host}.UI -or ${null} -eq ${Host}.UI.RawUI) { $useWriteProgress = ${false} }
    } catch { $useWriteProgress = ${false} }

    while (-not $Process.HasExited) {
        Start-Sleep -Milliseconds $PollMs

        if ($AllowCancel) {
            try {
                if ([Console]::KeyAvailable) {
                    $key = [Console]::ReadKey($true)
                    if ($key.Key -eq [ConsoleKey]::Q -or $key.Key -eq [ConsoleKey]::Escape) {
                        try {
                            if ($useWriteProgress) {
                                if ($ParentProgressId -gt 0) {
                                    Write-Progress -Id $progressId -ParentId $ParentProgressId -Activity $Prefix -Status "Cancel requested (waiting confirmation...)"
                                } else {
                                    Write-Progress -Id $progressId -Activity $Prefix -Status "Cancel requested (waiting confirmation...)"
                                }
                            }
                        } catch { }

                        $answer = Read-Host $CancelPrompt
                        if ($answer -match '^(y|yes|s|si)$') {
                            try {
                                if ($useWriteProgress) {
                                    if ($ParentProgressId -gt 0) {
                                        Write-Progress -Id $progressId -ParentId $ParentProgressId -Activity $Prefix -Status "Cancelling..."
                                    } else {
                                        Write-Progress -Id $progressId -Activity $Prefix -Status "Cancelling..."
                                    }
                                }
                            } catch { }

                            try { Stop-ProcessTree -Pid $Process.Id } catch { }
                            try { $Process.WaitForExit(5000) | Out-Null } catch { }

                            try {
                                if ($OnCancel) { & $OnCancel }
                            } catch { }

                            try {
                                if ($useWriteProgress) {
                                    Write-Progress -Id $progressId -Activity $Prefix -Completed
                                }
                            } catch { }
                            Write-Host "${Prefix} Cancelled." -ForegroundColor Yellow
                            return @{ Elapsed = "{0:mm}:{0:ss}" -f ((Get-Date) - $startTime); Cpu = $cpuPercent; MemoryMB = $memoryMB; ExitCode = $null; Cancelled = $true }
                        }
                    }
                }
            } catch {
                # Not an interactive console (ignore cancel)
            }
        }

        $elapsed = (Get-Date) - $startTime
        $elapsedStr = "{0:mm}:{0:ss}" -f $elapsed

        if ((Get-Date) - $lastStatsUpdate -gt [TimeSpan]::FromSeconds(1)) {
            try {
                $unityProc = Get-Process -Id $Process.Id -ErrorAction SilentlyContinue
                if ($unityProc) {
                    $cpuPercent = [math]::Round($unityProc.CPU / $elapsed.TotalSeconds, 1)
                    $memoryMB = [math]::Round($unityProc.WorkingSet64 / 1MB, 0)
                }
            } catch { }
            $lastStatsUpdate = Get-Date
        }

        $lastLogChanged = $false
        if ($LogFile) {
            $newLog = Get-VrcSetupChangedLogTail -LogFile $LogFile -State $logReadState
            if ($newLog -and $newLog -ne $lastLog) { $lastLog = $newLog; $lastLogChanged = $true }
        }

        $maxLogLen = 60
        if ($useWriteProgress) {
            # Best-effort width calculation for truncation (avoid multi-line progress blocks)
            $w = 120
            try { $w = [int]$Host.UI.RawUI.WindowSize.Width } catch { $w = 120 }
            $maxLogLen = [Math]::Max(25, $w - 75)
        } else {
            $winWidth = 120
            try { $winWidth = [Console]::WindowWidth } catch { $winWidth = 120 }
            $maxLogLen = [Math]::Max(40, $winWidth - 50)
        }

        # Sanitize control chars/newlines from Unity log tail (prevents cursor jumps / corruption)
        $cleanLog = [string]$lastLog
        if (-not [string]::IsNullOrEmpty($cleanLog)) {
            $cleanLog = $cleanLog -replace "`r|`n|`t", ' '
            $cleanLog = $cleanLog -replace "[\x00-\x1F\x7F]", ''
        }

        $displayLog = if ($cleanLog.Length -gt $maxLogLen) { $cleanLog.Substring(0, $maxLogLen) + "..." } else { $cleanLog }

        if ($embedded) {
            # Pinned status line: the panel renders this on one fixed colored row and
            # never as a log row, so CPU/RAM/timer updates do not advance the view.
            Write-Host "::VRCSETUP_METRICS::CPU ${cpuPercent}%  RAM ${memoryMB}MB"
            # A new log row appears only when Unity actually produced a new tail line.
            if ($lastLogChanged -and -not [string]::IsNullOrWhiteSpace($cleanLog)) {
                Write-Host $cleanLog
            }
        }
        elseif ($useWriteProgress) {
            $tail = $displayLog
            if (-not [string]::IsNullOrWhiteSpace($tail)) {
                $status = "Time ${elapsedStr} | CPU: ${cpuPercent}% | RAM: ${memoryMB}MB | ${tail}"
            } else {
                $status = "Time ${elapsedStr} | CPU: ${cpuPercent}% | RAM: ${memoryMB}MB"
            }
            try {
                # Avoid -CurrentOperation: it becomes a separate line and some hosts place it oddly.
                # Keep output stable by showing only Activity + Status.
                if ($ParentProgressId -gt 0) {
                    Write-Progress -Id $progressId -ParentId $ParentProgressId -Activity $Prefix -Status $status
                } else {
                    Write-Progress -Id $progressId -Activity $Prefix -Status $status
                }
            } catch {
                # If the host can't render progress, fall back to plain output.
                $useWriteProgress = $false
            }
        }

        if (-not $embedded -and -not $useWriteProgress) {
            $statusLine = "${Prefix} ${elapsedStr} | CPU: ${cpuPercent}% | RAM: ${memoryMB}MB | ${displayLog}"
            Write-Host $statusLine
        }
    }

    # Final line
    $elapsed = (Get-Date) - $startTime
    $elapsedStr = "{0:mm}:{0:ss}" -f $elapsed
    try {
        if ($useWriteProgress) { Write-Progress -Id $progressId -Activity $Prefix -Completed }
    } catch { }

    $finalMsg = ("${Prefix} Completed! Time: {0}" -f $elapsedStr)
    Write-Host $finalMsg -ForegroundColor Green
    return @{ Elapsed = $elapsedStr; Cpu = $cpuPercent; MemoryMB = $memoryMB; ExitCode = $Process.ExitCode; Cancelled = $false }
}

