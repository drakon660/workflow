#!/bin/bash
#
# Convert Markdown files to PDF using Pandoc
#
# Usage:
#   ./convert-md-to-pdf.sh file.md               # Convert single file
#   ./convert-md-to-pdf.sh *.md                  # Convert multiple files
#   ./convert-md-to-pdf.sh -d docs -o output     # Convert directory with output path
#   ./convert-md-to-pdf.sh -d docs -r            # Convert directory recursively
#

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Default values
OUTPUT_DIR=""
RECURSIVE=false
INPUT_DIR=""

# Function to print colored messages
print_error() {
    echo -e "${RED}Error: $1${NC}" >&2
}

print_success() {
    echo -e "${GREEN}$1${NC}"
}

print_info() {
    echo -e "${CYAN}$1${NC}"
}

print_warning() {
    echo -e "${YELLOW}$1${NC}"
}

# Check if pandoc is installed
check_pandoc() {
    if ! command -v pandoc &> /dev/null; then
        print_error "Pandoc is not installed!"
        echo ""
        print_warning "Please install Pandoc first:"
        echo "  - Ubuntu/Debian: sudo apt-get install pandoc texlive-xetex"
        echo "  - macOS: brew install pandoc"
        echo "  - Or download from: https://pandoc.org/installing.html"
        exit 1
    fi
}

# Convert a single markdown file to PDF
convert_file() {
    local md_file="$1"
    local output_dir="$2"

    if [ ! -f "$md_file" ]; then
        print_warning "File not found: $md_file"
        return 1
    fi

    local filename=$(basename "$md_file" .md)
    local pdf_file="$output_dir/${filename}.pdf"

    print_info "Converting: $md_file -> $pdf_file"

    if pandoc "$md_file" -o "$pdf_file" \
        --pdf-engine=xelatex \
        -V geometry:margin=1in \
        --highlight-style=tango \
        2>/dev/null; then
        print_success "  ✓ Created: $pdf_file"
        return 0
    else
        print_error "  ✗ Failed to convert: $md_file"
        return 1
    fi
}

# Show usage
show_usage() {
    cat << EOF
Usage: $0 [OPTIONS] [FILES...]

Convert Markdown files to PDF using Pandoc.

Options:
    -d DIR      Input directory containing .md files
    -o DIR      Output directory for PDF files (default: same as input)
    -r          Process subdirectories recursively
    -h          Show this help message

Examples:
    $0 README.md                           # Convert single file
    $0 *.md                                # Convert all .md files
    $0 -d docs                             # Convert all .md in docs/
    $0 -d docs -o output                   # Convert with output directory
    $0 -d docs -r                          # Convert recursively
EOF
}

# Parse command line arguments
while getopts "d:o:rh" opt; do
    case $opt in
        d) INPUT_DIR="$OPTARG" ;;
        o) OUTPUT_DIR="$OPTARG" ;;
        r) RECURSIVE=true ;;
        h) show_usage; exit 0 ;;
        ?) show_usage; exit 1 ;;
    esac
done

shift $((OPTIND-1))

# Check if pandoc is installed
check_pandoc

# Collect files to convert
declare -a FILES_TO_CONVERT

if [ -n "$INPUT_DIR" ]; then
    # Directory mode
    if [ ! -d "$INPUT_DIR" ]; then
        print_error "Directory not found: $INPUT_DIR"
        exit 1
    fi

    if [ "$RECURSIVE" = true ]; then
        while IFS= read -r -d '' file; do
            FILES_TO_CONVERT+=("$file")
        done < <(find "$INPUT_DIR" -type f -name "*.md" -print0)
    else
        while IFS= read -r -d '' file; do
            FILES_TO_CONVERT+=("$file")
        done < <(find "$INPUT_DIR" -maxdepth 1 -type f -name "*.md" -print0)
    fi

    # Set default output dir if not specified
    [ -z "$OUTPUT_DIR" ] && OUTPUT_DIR="$INPUT_DIR"
else
    # File mode - use remaining arguments as files
    if [ $# -eq 0 ]; then
        print_error "No input files specified"
        show_usage
        exit 1
    fi

    FILES_TO_CONVERT=("$@")
fi

# Check if we have files to convert
if [ ${#FILES_TO_CONVERT[@]} -eq 0 ]; then
    print_warning "No markdown files found"
    exit 0
fi

print_info "Found ${#FILES_TO_CONVERT[@]} markdown file(s) to convert"
echo ""

# Create output directory if specified and doesn't exist
if [ -n "$OUTPUT_DIR" ] && [ ! -d "$OUTPUT_DIR" ]; then
    mkdir -p "$OUTPUT_DIR"
fi

# Convert each file
success_count=0
fail_count=0

for file in "${FILES_TO_CONVERT[@]}"; do
    # Determine output directory
    if [ -n "$OUTPUT_DIR" ]; then
        out_dir="$OUTPUT_DIR"
    else
        out_dir=$(dirname "$file")
    fi

    if convert_file "$file" "$out_dir"; then
        ((success_count++))
    else
        ((fail_count++))
    fi
done

# Summary
echo ""
print_info "Conversion complete!"
print_success "  Successful: $success_count"
if [ $fail_count -gt 0 ]; then
    print_error "  Failed: $fail_count"
fi
