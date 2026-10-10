$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\setup-scripts\commands\wizard.ps1')
Add-Type -LiteralPath (Join-Path $scriptDir 'lib\spectre\Spectre.Console.Ansi.dll')
Add-Type -LiteralPath (Join-Path $scriptDir 'lib\spectre\Spectre.Console.dll')
function Measure-ProgressFrame {
    param($Renderable, [int]$Width)
    $options = [Spectre.Console.Rendering.RenderOptions]::new([Spectre.Console.AnsiConsole]::Profile.Capabilities,[Spectre.Console.Size]::new($Width,50))
    $segments = @([Spectre.Console.Rendering.IRenderable].GetMethod('Render').Invoke($Renderable,@($options,$Width)))
    return 1 + @($segments | Where-Object IsLineBreak).Count
}
$originalWidth = [Spectre.Console.AnsiConsole]::Profile.Width
try {
    foreach ($width in @(50,80,120)) {
        [Spectre.Console.AnsiConsole]::Profile.Width=$width
        $heights = @()
        foreach ($status in @('Running','Success','Failed','Cancelled')) {
            foreach ($count in @(0,1,12,30)) {
                $lines = @(for ($i=0;$i -lt $count;$i++) { "[INF] Package step ${i}" })
                $renderable = New-VrcSetupProgressRenderable -Lines $lines -MaxVisible 5 -Status $status -LogFile ('C:\logs\' + ('long-path-' * 12) + '.log')
                $heights += Measure-ProgressFrame -Renderable $renderable -Width $width
            }
        }
        if (@($heights | Select-Object -Unique).Count -ne 1) { throw "Progress frame height changes at ${width} columns: $($heights -join ',')" }
        Write-Host "PASS: ${width} columns; all progress states stay $($heights[0]) rows high."
    }
} finally { [Spectre.Console.AnsiConsole]::Profile.Width=$originalWidth }
