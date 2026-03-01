#!/bin/sh
set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
NC='\033[0m'

INPUT_DIR="${INPUT_DIR:-/input}"
OUTPUT_DIR="${OUTPUT_DIR:-/output}"
STYLE_FILE="/app/style.css"

print_info() { printf "${CYAN}%s${NC}\n" "$1"; }
print_success() { printf "${GREEN}%s${NC}\n" "$1"; }
print_error() { printf "${RED}%s${NC}\n" "$1" >&2; }

convert_file() {
    local md_file="$1"
    local filename=$(basename "$md_file" .md)
    local html_file="/tmp/${filename}.html"
    local pdf_file="${OUTPUT_DIR}/${filename}.pdf"

    print_info "Converting: $md_file"

    # Step 1: MD -> HTML with embedded CSS
    pandoc "$md_file" -o "$html_file" \
        --css="$STYLE_FILE" \
        --standalone \
        --embed-resources \
        --metadata title="$filename" 2>/dev/null

    # Step 2: HTML -> PDF with page numbers
    wkhtmltopdf \
        --enable-local-file-access \
        --footer-center "Page [page] of [topage]" \
        --footer-font-size 9 \
        --margin-bottom 25mm \
        --margin-top 20mm \
        --margin-left 25mm \
        --margin-right 25mm \
        --quiet \
        "$html_file" \
        "$pdf_file" 2>/dev/null

    # Cleanup
    rm -f "$html_file"

    if [ -f "$pdf_file" ]; then
        print_success "  -> ${filename}.pdf"
        return 0
    else
        print_error "  Failed: $md_file"
        return 1
    fi
}

# Main
print_info "MD to PDF Converter"
print_info "==================="
echo ""

# Find all markdown files
files=$(find "$INPUT_DIR" -maxdepth 1 -name "*.md" -type f 2>/dev/null)

if [ -z "$files" ]; then
    print_error "No .md files found in $INPUT_DIR"
    exit 1
fi

count=$(echo "$files" | wc -l)
print_info "Found $count markdown file(s)"
echo ""

success=0
failed=0

for file in $files; do
    if convert_file "$file"; then
        success=$((success + 1))
    else
        failed=$((failed + 1))
    fi
done

echo ""
print_info "Complete: $success succeeded, $failed failed"
