# Proposal: Backend Configuration Switch

## Intent

Clarificar en la documentación qué backend está activo (PostgreSQL, SQLite, o HTTP remoto) y proporcionar un mecanismo de configuración simple para cambiar entre backends modificando un solo valor visible.

## Problema actual

### 1. Documentación ambigua

Actualmente hay 3 backends posibles y la ruta que toma cada request no es obvia:

| Backend | Se activa cuando | Variables involucradas |
|---------|-----------------|----------------------|
| **HttpStore** (remoto) | `ENGRAM_URL` está definido | `ENGRAM_URL`, `ENGRAM_USER` |
| **PostgresStore** | `ENGRAM_DB_TYPE=postgres` + `ENGRAM_PG_CONNECTION` | `ENGRAM_DB_TYPE`, `ENGRAM_PG_CONNECTION` |
| **SqliteStore** | Ninguna de las anteriores (default) | `ENGRAM_DATA_DIR` |

El usuario no puede responder rápidamente: **"¿A dónde van mis datos ahora?"**

### 2. Configuración dispersa

Para cambiar de SQLite a PostgreSQL hay que:
1. Setear `ENGRAM_DB_TYPE=postgres`
2. Setear `ENGRAM_PG_CONNECTION=Host=...;Database=...;...`
3. Des-setear `ENGRAM_URL` (si estaba)
4. Verificar que el servidor PostgreSQL esté accesible

Son 4 pasos con variables de entorno que no son intuitivas para un desarrollador nuevo.

## Propuesta

### Nivel 1: Documentación clara (inmediato, sin código)

Agregar una sección en `DEPLOYMENT.md` y `README.md` con:

```
## ¿Qué backend estoy usando?

Ejecutá: curl http://localhost:7437/stats

Si la respuesta incluye "backend": "postgres" → PostgreSQL
Si no hay campo backend → SQLite local
Si el response viene de otra IP → HTTP remoto (ENGRAM_URL)
```

Y un diagrama de decisión:

```
¿ENGRAM_URL definido?
  ├─ SÍ → HttpStore (servidor remoto)
  └─ NO → ¿ENGRAM_DB_TYPE=postgres?
            ├─ SÍ → PostgresStore (base local del servidor)
            └─ NO → SqliteStore (archivo ~/.engram/engram.db)
```

### Nivel 2: Config file opcional (mediano, requiere código)

Permitir un archivo `~/.engram/config.json` que simplifique la configuración:

```json
{
  "backend": "postgres",
  "postgres": {
    "host": "192.168.0.178",
    "port": 5432,
    "database": "postgres",
    "user": "postgres",
    "password": "..."
  }
}
```

O para modo remoto:

```json
{
  "backend": "http",
  "remote": {
    "url": "http://192.168.0.178:7437",
    "user": "victor.silgado"
  }
}
```

O para SQLite (default):

```json
{
  "backend": "sqlite",
  "sqlite": {
    "data_dir": "~/.engram"
  }
}
```

**Prioridad de resolución**: config file > variables de entorno > defaults.

### Nivel 3: Indicator en responses (bajo costo, alto valor)

Agregar campo `"backend"` en todas las responses del servidor:

```json
{
  "status": "ok",
  "service": "engram",
  "version": "1.1.0",
  "backend": "postgres"
}
```

Y en `/stats`:

```json
{
  "total_sessions": 217,
  "total_observations": 563,
  "backend": "postgres",
  "postgres_host": "192.168.0.178:5432"
}
```

## Alcance

### In scope
- Documentación de decisión de backend (Nivel 1)
- Campo `backend` en responses del servidor (Nivel 3)
- Diseño del config file (Nivel 2 — solo diseño, no implementación)

### Out scope (para esta fase)
- Implementación del parser de config file
- Migración automática entre backends
- UI para cambiar backend

## Criterios de aceptación

1. [ ] Un desarrollador nuevo puede responder "¿qué backend estoy usando?" en < 30 segundos
2. [ ] La documentación incluye diagrama de decisión con los 3 backends
3. [ ] `/health` y `/stats` incluyen campo `"backend"` identificable
4. [ ] El diseño del config file está documentado (aunque no implementado)

## Tradeoffs

| Opción | Pros | Contras |
|--------|------|---------|
| Solo docs (Nivel 1) | Cero código, inmediato | No mejora la DX real |
| Config file (Nivel 2) | DX excelente, un solo archivo | Nuevo código, parsing, precedence rules |
| Backend indicator (Nivel 3) | Bajo costo, alto valor inmediato | Solo informativo, no cambia config |

## Recomendación

Implementar **Nivel 1 + Nivel 3** ahora (docs + backend indicator en responses). Son cambios mínimos con alto impacto en claridad.

Dejar **Nivel 2** (config file) para cuando haya más demanda — las variables de entorno funcionan bien para equipos técnicos.

## Impacto estimado

| Nivel | Esfuerzo | Riesgo |
|-------|----------|--------|
| 1 (docs) | 1-2 horas | Ninguno |
| 2 (config file) | 4-6 horas | Medio (precedence con env vars) |
| 3 (backend indicator) | 1-2 horas | Bajo (campo nuevo, no breaking) |
