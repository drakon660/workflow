#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Converts Markdown (.md) files to PDF using markdown-pdf npm package.

.DESCRIPTION
    This script converts Markdown files to PDF using markdown-pdf (npm package).
    If Node.js and markdown-pdf are not installed, it will provide instructions.

.PARAMETER InputPath
    Path to the markdown file to convert.

.PARAMETER OutputPath
    Optional output path for the PDF file.

.EXAMPLE
    .\convert-md-to-pdf-simple.ps1 -InputPath "README.md"
    Converts README.md to README.pdf
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$InputPath,

    [Parameter(Mandatory=$false)]
    [string]$OutputPath
)

# Check if Node.js is installed
function Test-NodeInstalled {
    try {
        $null = Get-Command node -ErrorAction Stop
        return $true
    }
    catch {
        return $false
    }
}

# Check if npx is available
function Test-NpxInstalled {
    try {
        $null = Get-Command npx -ErrorAction Stop
        return $true
    }
    catch {
        return $false
    }
}

try {
    # Check if input file exists
    if (-not (Test-Path $InputPath)) {
        Write-Host "Error: File not found: $InputPath" -ForegroundColor Red
        exit 1
    }

    # Resolve paths
    $inputFile = Resolve-Path $InputPath
    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($inputFile)

    if ($OutputPath) {
        $outputFile = $OutputPath
    }
    else {
        $outputDir = Split-Path $inputFile -Parent
        $outputFile = Join-Path $outputDir "$fileName.pdf"
    }

    # Check if Node.js is installed
    if (-not (Test-NodeInstalled)) {
        Write-Host "Error: Node.js is not installed!" -ForegroundColor Red
        Write-Host ""
        Write-Host "Please install Node.js first:" -ForegroundColor Yellow
        Write-Host "  Download from: https://nodejs.org/" -ForegroundColor Yellow
        Write-Host "  Or via Chocolatey: choco install nodejs" -ForegroundColor Yellow
        Write-Host "  Or via Winget: winget install OpenJS.NodeJS" -ForegroundColor Yellow
        exit 1
    }

    # Check if npx is available
    if (-not (Test-NpxInstalled)) {
        Write-Host "Error: npx is not available!" -ForegroundColor Red
        Write-Host "Please update Node.js to a version that includes npx." -ForegroundColor Yellow
        exit 1
    }

    Write-Host "Converting: $inputFile" -ForegroundColor Cyan
    Write-Host "Output: $outputFile" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Note: First run may take a moment to download markdown-pdf..." -ForegroundColor Yellow
    Write-Host ""

    # Use npx to run markdown-pdf without global installation
    # This will auto-download markdown-pdf if not already cached
    $result = npx -y markdown-pdf "$inputFile" -o "$outputFile" 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Success! PDF created: $outputFile" -ForegroundColor Green

        # Open the PDF
        $openPdf = Read-Host "Would you like to open the PDF? (y/n)"
        if ($openPdf -eq 'y' -or $openPdf -eq 'Y') {
            Start-Process $outputFile
        }
    }
    else {
        Write-Host "Error: Conversion failed" -ForegroundColor Red
        Write-Host $result -ForegroundColor Red
        exit 1
    }
}
catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
