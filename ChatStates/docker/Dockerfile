# Use Debian slim for wkhtmltopdf availability
FROM debian:bookworm-slim

# Install pandoc and wkhtmltopdf
RUN apt-get update && apt-get install -y --no-install-recommends \
    pandoc \
    wkhtmltopdf \
    fontconfig \
    fonts-dejavu \
    fonts-liberation2 \
    && rm -rf /var/lib/apt/lists/*

# Create working directories
WORKDIR /app

# Copy conversion script and styles
COPY convert.sh /app/convert.sh
COPY style.css /app/style.css
# Fix Windows line endings and make executable
RUN sed -i 's/\r$//' /app/convert.sh && chmod +x /app/convert.sh

# Set default input/output directories
VOLUME ["/input", "/output"]

ENTRYPOINT ["/bin/sh", "/app/convert.sh"]
