# engram-dotnet — imagen desde binario publicado
# No requiere .NET SDK — descarga el binario self-contained del release

FROM debian:12-slim

ARG ENGRAM_VERSION=v1.2.0

# Dependencias mínimas para correr el binario .NET self-contained en Linux
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    curl \
    libicu72 \
    && rm -rf /var/lib/apt/lists/*

# Descargar el binario del release
RUN curl -L \
    "https://github.com/efreet111/engram-dotnet/releases/download/${ENGRAM_VERSION}/engram-linux-x64" \
    -o /usr/local/bin/engram \
    && chmod +x /usr/local/bin/engram

# Directorio de datos (se monta como volumen)
RUN mkdir -p /data/engram
VOLUME ["/data/engram"]

# Puerto HTTP
EXPOSE 7437

# Variables de entorno con defaults seguros
ENV ENGRAM_DATA_DIR=/data/engram
ENV ENGRAM_PORT=7437

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:7437/health || exit 1

ENTRYPOINT ["/usr/local/bin/engram"]
CMD ["serve"]
