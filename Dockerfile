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

ARG ENGRAM_VERSION=dev
# Strip leading 'v' from version tag (NuGet requires semver: "1.3.0" not "v1.3.0")
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

COPY --from=build /app/publish .
RUN chmod +x ./engram

USER engram

ENV ENGRAM_DATA_DIR=/data/engram
ENV ENGRAM_PORT=7437
ENV ASPNETCORE_URLS=http://+:7437

EXPOSE 7437

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:7437/health || exit 1

# PublishSingleFile with Exe output produces a native executable (no .dll)
ENTRYPOINT ["./engram"]
