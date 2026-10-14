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

function Initialize-VrcSetupProcessGuardType {
    if ($null -ne ('VrcSetupNativeJob' -as [type])) { return $true }
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return $false }

    try {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class VrcSetupNativeJob
{
    private const uint ProcessTerminate = 0x0001;
    private const uint ProcessSetQuota = 0x0100;
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public IntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    public static IntPtr CreateKillOnCloseJob()
    {
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;

        ExtendedLimitInformation limits = new ExtendedLimitInformation();
        limits.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
        int size = Marshal.SizeOf(typeof(ExtendedLimitInformation));
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(limits, buffer, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)size))
            {
                CloseHandle(job);
                return IntPtr.Zero;
            }
            return job;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static bool AssignProcess(IntPtr job, uint processId)
    {
        if (job == IntPtr.Zero) return false;
        IntPtr process = OpenProcess(ProcessTerminate | ProcessSetQuota, false, processId);
        if (process == IntPtr.Zero) return false;
        try { return AssignProcessToJobObject(job, process); }
        finally { CloseHandle(process); }
    }

    public static void Close(IntPtr job)
    {
        if (job != IntPtr.Zero) CloseHandle(job);
    }
}
'@ -ErrorAction Stop
        return $true
    } catch {
        return $false
    }
}

function New-VrcSetupProcessGuard {
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process)

    if (-not (Initialize-VrcSetupProcessGuardType)) { return [IntPtr]::Zero }
    try {
        $job = [VrcSetupNativeJob]::CreateKillOnCloseJob()
        if ($job -eq [IntPtr]::Zero) { return [IntPtr]::Zero }
        if (-not [VrcSetupNativeJob]::AssignProcess($job, [uint32]$Process.Id)) {
            [VrcSetupNativeJob]::Close($job)
            return [IntPtr]::Zero
        }
        return $job
    } catch {
        return [IntPtr]::Zero
    }
}

function Close-VrcSetupProcessGuard {
    param([IntPtr]$JobHandle)

    if ($JobHandle -eq [IntPtr]::Zero) { return }
    try { [VrcSetupNativeJob]::Close($JobHandle) } catch { }
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
    # The Spectre panel captures this stream; one live row represents the whole
    # Unity/process phase. Sentinel literals are shared with
    # New-VrcSetupProgressRenderable / Invoke-VrcSetupSpectreOperation.
    $embedded = ($env:VRCSETUP_EMBEDDED_PROGRESS -eq '1')
    if ($embedded) { $AllowCancel = ${false} }
    try {
        if (${null} -eq ${Host} -or ${null} -eq ${Host}.UI -or ${null} -eq ${Host}.UI.RawUI) { $useWriteProgress = ${false} }
    } catch { $useWriteProgress = ${false} }

    # The Windows job handle kills this Unity process tree when the owning
    # PowerShell process disappears unexpectedly (terminal close / hard Ctrl+C).
    $processGuard = New-VrcSetupProcessGuard -Process $Process
    $cancelState = [pscustomobject]@{ Requested = $false }
    $cancelHandler = $null
    try {
        try {
            $cancelHandler = [ConsoleCancelEventHandler]{
                param($sender, $eventArgs)
                $eventArgs.Cancel = $true
                $cancelState.Requested = $true
            }
            [Console]::add_CancelKeyPress($cancelHandler)
        } catch { $cancelHandler = $null }

    try {
    while (-not $Process.HasExited) {
        Start-Sleep -Milliseconds $PollMs

        if ($cancelState.Requested) {
            try { Stop-ProcessTree -Pid $Process.Id } catch { }
            try { $Process.WaitForExit(5000) | Out-Null } catch { }
            try { if ($OnCancel) { & $OnCancel } } catch { }
            try {
                if ($useWriteProgress) { Write-Progress -Id $progressId -Activity $Prefix -Completed }
            } catch { }
            if ($embedded) {
                Write-Host "::VRCSETUP_LIVE_COMMIT::${Prefix} Cancelled by Ctrl+C."
            } else {
                Write-Host "${Prefix} Cancelled by Ctrl+C." -ForegroundColor Yellow
            }
            return @{ Elapsed = "{0:mm}:{0:ss}" -f ((Get-Date) - $startTime); Cpu = $cpuPercent; MemoryMB = $memoryMB; ExitCode = $null; Cancelled = $true }
        }

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
                            if ($embedded) {
                                Write-Host "::VRCSETUP_LIVE_COMMIT::${Prefix} Cancelled."
                            } else {
                                Write-Host "${Prefix} Cancelled." -ForegroundColor Yellow
                            }
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
            # One live row represents this whole Unity/process phase. The panel
            # replaces it in place until the process commits the row on exit.
            $liveStatus = "${Prefix} ${elapsedStr} | CPU: ${cpuPercent}% | RAM: ${memoryMB}MB"
            if (-not [string]::IsNullOrWhiteSpace($cleanLog)) { $liveStatus += " | ${cleanLog}" }
            Write-Host "::VRCSETUP_LIVE_STATUS::${liveStatus}"
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
    if ($embedded) {
        Write-Host "::VRCSETUP_LIVE_COMMIT::${finalMsg}"
    } else {
        Write-Host $finalMsg -ForegroundColor Green
    }
    return @{ Elapsed = $elapsedStr; Cpu = $cpuPercent; MemoryMB = $memoryMB; ExitCode = $Process.ExitCode; Cancelled = $false }
    } finally {
        if ($cancelHandler) {
            try { [Console]::remove_CancelKeyPress($cancelHandler) } catch { }
        }
    }
    } finally {
        Close-VrcSetupProcessGuard -JobHandle $processGuard
    }
}

