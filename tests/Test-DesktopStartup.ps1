[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WinUi,
    [Parameter(Mandatory)][string]$React,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$executables = @{
    WinUI = (Resolve-Path -LiteralPath $WinUi).Path
    React = (Resolve-Path -LiteralPath $React).Path
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh directory for the startup measurements.' }
$samples = foreach ($round in 1..4) {
    $order = if ($round % 2) { 'WinUI', 'React' } else { 'React', 'WinUI' }
    foreach ($hostName in $order) {
        $fixture = Join-Path $output "$round-$hostName"
        $homePath = Join-Path $fixture 'home'
        $projects = Join-Path $homePath 'candidate-projects'
        $project = Join-Path $projects 'Startup fixture'
        foreach ($directory in 'Assets', 'Packages', 'ProjectSettings') {
            New-Item -ItemType Directory -Path (Join-Path $project $directory) -Force | Out-Null
        }
        [IO.File]::WriteAllText((Join-Path $project 'Packages/manifest.json'), '{"dependencies":{}}')
        [IO.File]::WriteAllText((Join-Path $project 'Packages/vpm-manifest.json'), '{"dependencies":{},"locked":{}}')
        [IO.File]::WriteAllText((Join-Path $project 'ProjectSettings/ProjectVersion.txt'), "m_EditorVersion: 2022.3.22f1`n")
        # A configured editor value skips setup. Discovery is disabled and the
        # benchmark only displays Projects, so no Unity executable is launched.
        $settings = @{
            Language = 'it'
            UnityProjectsRoot = $projects
            UnityEditorPath = Join-Path $fixture 'Unity.exe'
            CheckUpdatesOnStartup = $false
            AutomaticUpdates = $false
        }
        [IO.File]::WriteAllText((Join-Path $homePath 'config.json'), ($settings | ConvertTo-Json))
        [IO.File]::WriteAllText((Join-Path $fixture '.startup-fixture'), 'Isolated Projects startup measurement')
        $start = [Diagnostics.ProcessStartInfo]::new($executables[$hostName])
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.ArgumentList.Add('--verify-projects-startup')
        $start.ArgumentList.Add($fixture)
        $process = [Diagnostics.Process]::Start($start)
        try {
            if (-not $process.WaitForExit(30000)) {
                $process.Kill($true)
                throw "$hostName startup fixture timed out."
            }
            $report = Join-Path $fixture 'startup.json'
            if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) {
                $failure = Join-Path $fixture 'failure.txt'
                if (Test-Path -LiteralPath $failure) { throw (Get-Content -LiteralPath $failure -Raw) }
                throw "$hostName startup fixture did not produce a report."
            }
            $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
            [pscustomobject]@{
                Host = $hostName
                Round = $round
                Warmup = $round -eq 1
                ReadyMilliseconds = [math]::Round($result.readyMs, 1)
                PrivateMiB = [math]::Round($result.privateBytes / 1MB, 1)
                Processes = $result.processes
            }
        }
        finally { $process.Dispose() }
    }
}
[IO.File]::WriteAllText((Join-Path $output 'startup-comparison.json'), ($samples | ConvertTo-Json))
$samples
