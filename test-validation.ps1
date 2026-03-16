# Quick VPM validation test

# Carica la funzione
. "$PSScriptRoot\setup-scripts\commands\wizard.ps1"

Write-Host "`n=== VPM Validation Tests ===" -ForegroundColor Cyan

# Test 1: latest version (always valid)
Write-Host "`n[Test 1] adjerry91.vrcft.templates @ latest" -ForegroundColor Yellow
$result = Test-VpmPackageVersion -PackageName "adjerry91.vrcft.templates" -Version "latest" -ScriptDir $PSScriptRoot
Write-Host "Valid: $($result.Valid) - $($result.Message)" -ForegroundColor $(if ($result.Valid) { "Green" } else { "Red" })

# Test 2: existing version (6.8.0)
Write-Host "`n[Test 2] adjerry91.vrcft.templates @ 6.8.0" -ForegroundColor Yellow
$result = Test-VpmPackageVersion -PackageName "adjerry91.vrcft.templates" -Version "6.8.0" -ScriptDir $PSScriptRoot
Write-Host "Valid: $($result.Valid) - $($result.Message)" -ForegroundColor $(if ($result.Valid) { "Green" } else { "Red" })

# Test 3: non-existing version
Write-Host "`n[Test 3] adjerry91.vrcft.templates @ 99.99.99" -ForegroundColor Yellow
$result = Test-VpmPackageVersion -PackageName "adjerry91.vrcft.templates" -Version "99.99.99" -ScriptDir $PSScriptRoot
Write-Host "Valid: $($result.Valid) - $($result.Message)" -ForegroundColor $(if ($result.Valid) { "Green" } else { "Red" })

# Test 4: official VRChat package
Write-Host "`n[Test 4] com.vrchat.avatars @ latest" -ForegroundColor Yellow
$result = Test-VpmPackageVersion -PackageName "com.vrchat.avatars" -Version "latest" -ScriptDir $PSScriptRoot
Write-Host "Valid: $($result.Valid) - $($result.Message)" -ForegroundColor $(if ($result.Valid) { "Green" } else { "Red" })

Write-Host "`n=== Tests Completed ===" -ForegroundColor Cyan

Write-Host "`n=== Unity Editor Path Tests ===" -ForegroundColor Cyan

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if ($Condition) {
        Write-Host "[PASS] $Message" -ForegroundColor Green
    } else {
        Write-Host "[FAIL] $Message" -ForegroundColor Red
    }
}

$foundEditors = @((Find-UnityEditorPaths))
Assert-True ($null -ne $foundEditors) "Find-UnityEditorPaths returns not null"
Assert-True ($foundEditors -is [array]) "Find-UnityEditorPaths returns array"
Write-Host ("Found editors: {0}" -f $foundEditors.Count) -ForegroundColor Gray
if ($foundEditors.Count -gt 0) {
    $firstPath = [string]$foundEditors[0].Path
    Write-Host ("First path: {0}" -f $firstPath) -ForegroundColor Gray
    $check = Test-UnityEditorPath -Path $firstPath
    Assert-True ($check.Valid) "First Unity editor path validates"
} else {
    Write-Host "No Unity editors found on this machine." -ForegroundColor Yellow
}

Write-Host "`n=== Unity Path Tests Completed ===" -ForegroundColor Cyan
