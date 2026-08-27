#!/bin/bash
# allinone-postgres.sh — arranque de PostgreSQL dentro del contenedor all-in-one.
#
# 1. Inicializa $PGDATA con initdb si está vacío.
# 2. Arranca postgres temporalmente para crear el role + la base `engram`.
# 3. Corre postgres en foreground (supervisor lo mantiene vivo).
set -euo pipefail

# PostgreSQL 16 binaries are not in the default PATH for non-login shells.
# Use absolute paths for all su calls so postgres user can find them.
PG_BIN="/usr/lib/postgresql/16/bin"
PGDATA="${PGDATA:-/data/postgres}"
PG_SUPERUSER="${ENGRAM_PG_USER:-engram}"
PG_DATABASE="${ENGRAM_PG_DATABASE:-engram}"
PG_PORT="${ENGRAM_PG_PORT:-5432}"

mkdir -p "$PGDATA"
chown postgres:postgres "$PGDATA"

# 1. initdb si el directorio no está inicializado
if [[ ! -s "$PGDATA/PG_VERSION" ]]; then
  echo "[allinone] Inicializando PostgreSQL en $PGDATA"
  su postgres -c "PATH=\"$PG_BIN:$PATH\" initdb -D '$PGDATA' --auth=trust" >/dev/null
fi

# 2. Arranque temporal para el setup inicial
su postgres -c "PATH=\"$PG_BIN:$PATH\" pg_ctl -D '$PGDATA' -o '-c listen_addresses=127.0.0.1 -p $PG_PORT' -l /tmp/pg.log start" >/dev/null

# Esperar a que esté listo
for _ in $(seq 1 30); do
  if su postgres -c "PATH=\"$PG_BIN:$PATH\" pg_isready -h 127.0.0.1 -p $PG_PORT" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

# 3. Crear role + base de datos si no existen
if ! su postgres -c "PATH=\"$PG_BIN:$PATH\" psql -h 127.0.0.1 -p $PG_PORT -tAc \"SELECT 1 FROM pg_roles WHERE rolname='$PG_SUPERUSER'\"" | grep -q 1; then
  su postgres -c "PATH=\"$PG_BIN:$PATH\" psql -h 127.0.0.1 -p $PG_PORT -c \"CREATE ROLE $PG_SUPERUSER LOGIN SUPERUSER\"" >/dev/null
fi
if ! su postgres -c "PATH=\"$PG_BIN:$PATH\" psql -h 127.0.0.1 -p $PG_PORT -tAc \"SELECT 1 FROM pg_database WHERE datname='$PG_DATABASE'\"" | grep -q 1; then
  su postgres -c "PATH=\"$PG_BIN:$PATH\" createdb -h 127.0.0.1 -p $PG_PORT -O $PG_SUPERUSER $PG_DATABASE" >/dev/null
fi

# 4. Detener la instancia temporal y correr postgres en foreground
su postgres -c "PATH=\"$PG_BIN:$PATH\" pg_ctl -D '$PGDATA' stop -m fast" >/dev/null

echo "[allinone] PostgreSQL listo en 127.0.0.1:$PG_PORT (db=$PG_DATABASE)"
exec su postgres -c "PATH=\"$PG_BIN:$PATH\" postgres -D '$PGDATA'"
