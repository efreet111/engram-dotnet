# ─── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution/project files first for better layer caching
COPY *.slnx ./
COPY src/Engram.Store/Engram.Store.csproj src/Engram.Store/
COPY src/Engram.Server/Engram.Server.csproj src/Engram.Server/
COPY src/Engram.Mcp/Engram.Mcp.csproj src/Engram.Mcp/
COPY src/Engram.Sync/Engram.Sync.csproj src/Engram.Sync/
COPY src/Engram.Obsidian/Engram.Obsidian.csproj src/Engram.Obsidian/
COPY src/Engram.Cli/Engram.Cli.csproj src/Engram.Cli/
COPY tests/Engram.Store.Tests/Engram.Store.Tests.csproj tests/Engram.Store.Tests/
COPY tests/Engram.Postgres.Tests/Engram.Postgres.Tests.csproj tests/Engram.Postgres.Tests/
COPY tests/Engram.Server.Tests/Engram.Server.Tests.csproj tests/Engram.Server.Tests/
COPY tests/Engram.HttpStore.Tests/Engram.HttpStore.Tests.csproj tests/Engram.HttpStore.Tests/
COPY tests/Engram.Obsidian.Tests/Engram.Obsidian.Tests.csproj tests/Engram.Obsidian.Tests/

RUN dotnet restore src/Engram.Cli/Engram.Cli.csproj

# Copy source and build
COPY src/ src/
COPY tests/ tests/

# Default version is a valid NuGet SemVer 2.0 string.
# Override at build time: docker build --build-arg ENGRAM_VERSION=1.3.0 ...
# Accepts "v1.3.0" or "1.3.0" — the shell strips the leading "v" so NuGet
# receives a semver-compliant value (NuGet rejects "v1.3.0" but accepts "1.3.0").
# Bug history: a previous default of "dev" caused `error: 'dev' is not a valid
# version string` during `dotnet publish` on servers that built without
# passing --build-arg. See docs/DOCKER-VANILLA.md for details.
ARG ENGRAM_VERSION=0.0.0-dev
RUN dotnet publish src/Engram.Cli/Engram.Cli.csproj \
    -c Release \
    -o /app/publish \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -p:InvariantGlobalization=true \
    -p:Version=${ENGRAM_VERSION#v} \
    --self-contained false

# ─── Runtime stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for healthcheck
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN useradd --create-home --shell /bin/bash engram
RUN mkdir -p /data/engram && chown engram:engram /data/engram
RUN mkdir -p /app/docs && chown engram:engram /app/docs

COPY --from=build /app/publish .
RUN chmod +x ./engram

# Install gosu for dropping privileges in entrypoint
# https://github.com/tianon/gosu
RUN apt-get update && apt-get install -y --no-install-recommends gosu \
    && rm -rf /var/lib/apt/lists/* \
    && gosu nobody true

# Copy entrypoint script
COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

# NOTE: No USER directive here — entrypoint runs as root, then exec gosu engram
# This allows fixing volume permissions before dropping privileges

ENV ENGRAM_DATA_DIR=/data/engram
ENV ENGRAM_PORT=7437
ENV ENGRAM_PROFILE=local
ENV ASPNETCORE_URLS=http://+:7437

EXPOSE 7437

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:7437/health || exit 1

# PublishSingleFile with Exe output produces a native executable (no .dll)
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD ["./engram", "serve"]
