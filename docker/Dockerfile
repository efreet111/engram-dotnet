# engram-dotnet — compilación desde código fuente (Opción A)
# Build context: raíz del proyecto (un nivel arriba de docker/)
# Para CI/CD futuro ver: docs/CICD-SPEC.md

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copiar todo el repositorio
COPY . .

# Restaurar dependencias desde la solución
RUN dotnet restore engram-dotnet.slnx

# Publicar como self-contained linux-x64
RUN dotnet publish src/Engram.Server/Engram.Server.csproj \
    -c Release \
    -o /app/publish \
    --runtime linux-x64 \
    --self-contained true \
    -p:UseAppHost=true

# ── Runtime stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-preview AS runtime
WORKDIR /app

# wget para healthcheck (runtime-deps no incluye curl ni wget por defecto)
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Usuario no-root con UID/GID 950 (coincide con truenas_admin en TrueNAS SCALE)
RUN addgroup --gid 950 appgroup \
    && adduser --uid 950 --gid 950 --disabled-password --gecos "" appuser

# Directorio de datos para SQLite (punto de mount del volumen)
RUN mkdir -p /app/database \
    && chown -R appuser:appgroup /app/database

# Copiar artefactos del stage de build
COPY --from=build /app/publish .

# Asignar ownership al usuario no-root y asegurar ejecutable
RUN chown -R appuser:appgroup /app \
    && chmod +x /app/Engram.Server

# Variables de entorno
ENV ENGRAM_DATA_DIR=/app/database
ENV ENGRAM_PORT=7437

EXPOSE 7437

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -qO- http://localhost:7437/health || exit 1

USER appuser

ENTRYPOINT ["./Engram.Server"]
CMD ["--urls", "http://+:7437"]
