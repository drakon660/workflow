# Clean all .NET build artifacts (bin and obj folders)

$rootPath = $PSScriptRoot
if (-not $rootPath) {
    $rootPath = Get-Location
}

Write-Host "Cleaning build artifacts in: $rootPath" -ForegroundColor Cyan

$folders = Get-ChildItem -Path $rootPath -Include bin,obj -Recurse -Directory -Force

if ($folders.Count -eq 0) {
    Write-Host "No bin/obj folders found." -ForegroundColor Green
    exit 0
}

Write-Host "Found $($folders.Count) folders to delete:" -ForegroundColor Yellow

foreach ($folder in $folders) {
    Write-Host "  Deleting: $($folder.FullName)" -ForegroundColor Gray
    Remove-Item -Path $folder.FullName -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Clean complete!" -ForegroundColor Green
