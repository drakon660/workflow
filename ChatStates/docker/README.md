# MD to PDF Docker Converter

Converts Markdown files to styled PDFs using Pandoc + wkhtmltopdf.

## Quick Start

```bash
# Build and run (converts all .md in ../md folder)
docker compose up --build

# Or run once
docker compose run --rm md-to-pdf
```

## Custom Paths

```bash
# Convert specific folder
docker run --rm \
  -v /path/to/your/docs:/input:ro \
  -v /path/to/output:/output \
  md-to-pdf
```

## Image Size

Based on Alpine 3.19 (~150MB total with dependencies):
- Alpine base: ~7MB
- Pandoc: ~90MB
- wkhtmltopdf + deps: ~50MB

## What It Does

1. Finds all `*.md` files in input directory
2. Converts MD → HTML (with embedded CSS styling)
3. Converts HTML → PDF (with page numbers, margins)
4. Outputs PDFs to output directory

## Styling

Edit `style.css` to customize PDF appearance. Rebuild after changes:
```bash
docker compose build --no-cache
```
