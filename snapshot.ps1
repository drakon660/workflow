# Create a git snapshot of the current branch as a zip file

$rootPath = $PSScriptRoot
if (-not $rootPath) {
    $rootPath = Get-Location
}

Set-Location $rootPath

# Get current branch name
$branch = git rev-parse --abbrev-ref HEAD
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Not a git repository or git not found" -ForegroundColor Red
    exit 1
}

# Get short commit hash
$commitHash = git rev-parse --short HEAD

# Create timestamp
$timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"

# Sanitize branch name for filename (replace invalid chars)
$safeBranch = $branch -replace '[\\/:*?"<>|]', '-'

# Create output filename
$outputFile = "$rootPath\${safeBranch}_${timestamp}_${commitHash}.zip"

Write-Host "Creating snapshot..." -ForegroundColor Cyan
Write-Host "  Branch: $branch" -ForegroundColor Gray
Write-Host "  Commit: $commitHash" -ForegroundColor Gray
Write-Host "  Output: $outputFile" -ForegroundColor Gray

# Create archive using git archive
git archive --format=zip --output="$outputFile" HEAD

if ($LASTEXITCODE -eq 0) {
    Write-Host "Snapshot created successfully!" -ForegroundColor Green
    Write-Host "  File: $outputFile" -ForegroundColor White
} else {
    Write-Host "Error creating snapshot" -ForegroundColor Red
    exit 1
}
