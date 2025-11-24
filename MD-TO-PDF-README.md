# Markdown to PDF Converter Scripts

Two scripts to convert Markdown files to PDF format using Pandoc.

## Prerequisites

**Pandoc** must be installed on your system:

### Windows
```powershell
# Using Chocolatey
choco install pandoc

# Using Winget
winget install JohnMacFarlane.Pandoc

# Or download from: https://pandoc.org/installing.html
```

### Linux (Ubuntu/Debian)
```bash
sudo apt-get install pandoc texlive-xetex
```

### macOS
```bash
brew install pandoc
```

## Usage

### PowerShell Script (Windows)

```powershell
# Convert a single file
.\convert-md-to-pdf.ps1 README.md

# Convert all .md files in current directory
.\convert-md-to-pdf.ps1 *.md

# Convert all files in a directory
.\convert-md-to-pdf.ps1 -InputPath "docs"

# Convert with custom output directory
.\convert-md-to-pdf.ps1 -InputPath "docs" -OutputPath "output"

# Convert recursively (including subdirectories)
.\convert-md-to-pdf.ps1 -InputPath "docs" -OutputPath "output" -Recursive

# Get help
Get-Help .\convert-md-to-pdf.ps1 -Detailed
```

### Bash Script (Linux/macOS/Git Bash)

First, make the script executable:
```bash
chmod +x convert-md-to-pdf.sh
```

Then use it:
```bash
# Convert a single file
./convert-md-to-pdf.sh README.md

# Convert multiple files
./convert-md-to-pdf.sh file1.md file2.md file3.md

# Convert all .md files in current directory
./convert-md-to-pdf.sh *.md

# Convert all files in a directory
./convert-md-to-pdf.sh -d docs

# Convert with custom output directory
./convert-md-to-pdf.sh -d docs -o output

# Convert recursively (including subdirectories)
./convert-md-to-pdf.sh -d docs -r

# Get help
./convert-md-to-pdf.sh -h
```

## Features

- ✅ Converts single or multiple markdown files
- ✅ Supports directory processing
- ✅ Recursive directory traversal option
- ✅ Custom output directory
- ✅ Uses XeLaTeX for better Unicode support
- ✅ Syntax highlighting for code blocks
- ✅ Reasonable default margins (1 inch)
- ✅ Colored output for better visibility
- ✅ Error handling and summary statistics

## Output

PDFs are created with:
- **Engine**: XeLaTeX (better Unicode/font support)
- **Margins**: 1 inch on all sides
- **Syntax highlighting**: Tango theme
- **Format**: Professional-looking PDF output

## Examples

### Convert project documentation
```powershell
# Windows
.\convert-md-to-pdf.ps1 -InputPath ".\docs" -OutputPath ".\pdf-output" -Recursive
```

```bash
# Linux/macOS
./convert-md-to-pdf.sh -d ./docs -o ./pdf-output -r
```

### Convert a single README
```powershell
# Windows
.\convert-md-to-pdf.ps1 README.md
```

```bash
# Linux/macOS
./convert-md-to-pdf.sh README.md
```

## Troubleshooting

### "Pandoc is not installed" error
Install Pandoc using one of the methods listed in the Prerequisites section.

### PDF engine errors
If you get errors about the PDF engine, you might need to install LaTeX:
- **Windows**: Install MiKTeX or TeX Live
- **Linux**: `sudo apt-get install texlive-xetex`
- **macOS**: `brew install mactex`

### Permission denied (Linux/macOS)
Make sure the script is executable: `chmod +x convert-md-to-pdf.sh`

## Notes

- Output PDFs are placed in the same directory as input files unless `-OutputPath` (PowerShell) or `-o` (Bash) is specified
- Existing PDF files with the same name will be overwritten
- The script will continue processing even if individual files fail to convert
