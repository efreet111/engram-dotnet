#!/bin/bash
set -eEuo pipefail

# Run tests inside Docker (uses .NET 10 SDK from the build image)
# Usage: ./scripts/run-tests.sh [filter]
#   filter: dotnet test filter (e.g., "FullyQualifiedName~DeployProfile")

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

FILTER="${1:-}"

echo "=== Building test projects in Docker ==="

docker run --rm \
  -v "$PROJECT_ROOT:/src" \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet build tests/Engram.Store.Tests/Engram.Store.Tests.csproj -c Release 2>&1

echo ""
echo "=== Running tests (excluding Postgres + Docker) ==="

TEST_CMD="dotnet test tests/Engram.Store.Tests/Engram.Store.Tests.csproj -c Release --logger 'console;verbosity=normal'"
if [[ -n "$FILTER" ]]; then
  TEST_CMD="$TEST_CMD --filter '$FILTER'"
else
  TEST_CMD="$TEST_CMD --filter 'FullyQualifiedName!~Engram.Postgres.Tests&Category!=RequiresDocker'"
fi

docker run --rm \
  -v "$PROJECT_ROOT:/src" \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c "$TEST_CMD"
