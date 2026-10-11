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
    foreach ($height in @(24, 40, 60, 100)) {
        $rows = Get-VrcSetupProgressVisibleRows -Header 'Avatar project' -Height $height -Width 80
        if ($rows -ne [Math]::Max(3, $height - 21)) { throw "Unexpected log budget at height ${height}: $rows" }
        if ((Get-VrcSetupProgressVisibleRows -Header 'Avatar project' -Height $height -Width 80 -MaxVisible 8) -gt 8) { throw 'Explicit log limit was ignored.' }
        foreach ($status in @('Running','Success','Failed','Cancelled')) {
            $frame = New-VrcSetupProgressRenderable -MaxVisible $rows -Status $status
            if ((Measure-ProgressFrame $frame 80) + 6 -gt $height -and $height -ge 24) { throw "Progress controls overflow at height $height." }
        }
    }
    if ((Get-VrcSetupProgressVisibleRows -Header ('Long project name ' * 8) -Height 60 -Width 50) -ge (Get-VrcSetupProgressVisibleRows -Header 'Avatar' -Height 60 -Width 50)) { throw 'Wrapped headers did not reserve extra rows.' }
    Write-Host 'PASS: progress expands vertically, reserves wrapped headers and fits completion controls.'
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
