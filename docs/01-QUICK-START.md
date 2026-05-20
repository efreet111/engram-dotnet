# Guía Rápida — engram-dotnet por Persona

---

## 🧑 Solo Developer

**Objetivo**: Usar Engram localmente, solo SQLite, sin servidor compartido. Ideal para un developer trabajando solo con su agente de IA.

### Requisitos

- **Linux x64** (para el binario publicado)
- **.NET 10 SDK** ([descargar](https://dotnet.microsoft.com/download/dotnet/10.0)) — solo para compilar desde fuente
- Opcional: **Docker** si querés PostgreSQL en vez de SQLite
- Sin runtime externo requerido en producción — el binario es **self-contained**

> 💡 **¿Windows o macOS?** Cambiá `-r linux-x64` por `-r win-x64` o `-r osx-x64` al compilar.

### Instalación

```bash
# 1. Compilar
git clone https://github.com/efreet111/engram-dotnet
cd engram-dotnet
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o dist/

# 2. Iniciar servidor
./dist/engram serve
```

### Verificar

```bash
curl http://localhost:7437/health
# → {"status":"ok","service":"engram","version":"1.1.0","backend":"sqlite"}
```

### Configurar MCP (OpenCode)

```json
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_DATA_DIR": "~/.engram"
      }
    }
  }
}
```

### Ya está 🎉

El agente de IA ya puede usar `mem_save`, `mem_search`, `mem_context`, `mem_session_summary`, etc.

---

## 👥 Team Leader (2-5 personas)

**Objetivo**: Servidor compartido con PostgreSQL, multi-user isolation, SIN sync offline-first. Cada desarrollador se conecta al servidor central y sus memorias se aíslan por identidad.

### Arquitectura

```
Dev 1 (ENGRAM_USER=victor)  ─┐
Dev 2 (ENGRAM_USER=juan)    ─┤── HTTP ──► Servidor PostgreSQL ──► BD compartida
Dev 3 (ENGRAM_USER=ana)     ─┘           (aislamiento por user)
```

### Requisitos (Servidor)

- Servidor Linux x64
- PostgreSQL instalado y accesible
- .NET 10 SDK (solo para compilar)

### 1. Preparar PostgreSQL

```sql
CREATE DATABASE engram;
CREATE USER engram WITH PASSWORD 'supersecret';
GRANT ALL PRIVILEGES ON DATABASE engram TO engram;
```

### 2. Compilar e Iniciar Servidor

```bash
# En el servidor
git clone https://github.com/efreet111/engram-dotnet
cd engram-dotnet
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o dist/

# Iniciar con PostgreSQL
ENGRAM_DB_TYPE=postgres \
ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=engram;Password=supersecret" \
./dist/engram serve
```

### 3. Configurar Cada Developer

Cada dev agrega a su `opencode.json`:

```json
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_URL": "http://192.168.1.100:7437",
        "ENGRAM_USER": "victor.silgado"  // ← identidad ÚNICA de cada dev
      }
    }
  }
}
```

### Verificar Aislamiento

```bash
# Dev 1 guarda una memoria personal
curl -X POST http://servidor:7437/observations \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: victor" \
  -d '{"session_id":"s1","title":"Mi nota","content":"privada","type":"manual","project":"team/mi-api"}'

# Dev 2 NO ve la memoria de Dev 1
curl -H "X-Engram-User: juan" http://servidor:7437/search?q=nota
# → [] (vacío)
```

---

## 🏢 IT Admin (5-15 personas)

**Objetivo**: Servidor PostgreSQL + offline-first sync con enrollment, pause/resume, SyncManager automático. Los developers trabajan offline y sincronizan cuando hay conexión.

### Arquitectura

```
Cada Developer:                    Servidor:
┌──────────────┐     push/pull    ┌──────────────────┐
│ SQLite Local  │ ◄───HTTP────►  │ PostgreSQL Server │
│ SyncManager   │     cada 30s   │ cloud_mutations   │
│ pending_queue │                │ enrolled_projects │
└──────────────┘                 └──────────────────┘
     │ offline-first                    │
     └── Sin conexión = escribe local   │
     └── Con conexión = sync automático │
```

### Requisitos

- Igual que Team Leader +
- SyncManager activo (requiere ENGRAM_SYNC_ENABLED=true)

### 1. PostgreSQL

```sql
CREATE DATABASE engram;
-- Las tablas se crean automáticamente al iniciar el servidor
```

### 2. Servidor

```bash
ENGRAM_DB_TYPE=postgres \
ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=postgres;Password=NoAdmin.210725" \
ENGRAM_SYNC_ENABLED=true \
ENGRAM_SYNC_TARGET=cloud \
ENGRAM_SYNC_POLL_SECONDS=30s \
./engram serve
```

### 3. Cada Developer (configuración completa)

```json
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_SERVER_URL": "http://192.168.0.178:7437",
        "ENGRAM_USER": "victor.silgado",
        "ENGRAM_SYNC_ENABLED": "true",
        "ENGRAM_SYNC_TARGET": "cloud",
        "ENGRAM_SYNC_POLL_SECONDS": "30s",
        "ENGRAM_DATA_DIR": "~/.engram"
      }
    }
  }
}
```

### 4. Enrollar Proyectos

Cada developer inscribe los proyectos que quiere sincronizar:

```bash
curl -X POST http://192.168.0.178:7437/sync/enroll \
  -H "X-Engram-User: victor.silgado" \
  -d '{"project":"team/mi-api"}'
```

### 5. Verificar Sync

```bash
# Ver estado del sync
engram sync status

# Ver enrolled projects
curl -H "X-Engram-User: victor" http://192.168.0.178:7437/sync/enroll

# Ver health general
curl http://192.168.0.178:7437/sync/status
```

### Pausar Sync (Admin)

```bash
# Pausar (mantenimiento)
curl -X POST http://192.168.0.178:7437/sync/pause \
  -H "X-Engram-User: admin" \
  -d '{"project":"team/mi-api","reason":"Migración DB"}'

# Reanudar
curl -X DELETE "http://192.168.0.178:7437/sync/pause?project=team/mi-api" \
  -H "X-Engram-User: admin"
```

---

## ⚙️ Comparativa de Modos

| Aspecto | Solo Developer | Team Leader | IT Admin |
|---------|---------------|-------------|----------|
| **Backend** | SQLite local | PostgreSQL | PostgreSQL |
| **Sync** | ❌ No | ❌ No | ✅ Offline-First |
| **Multi-User** | ❌ No | ✅ RFC-002 | ✅ RFC-002 |
| **Enrollment** | ❌ No | ❌ No | ✅ Requerido |
| **Pause/Resume** | ❌ No | ❌ No | ✅ Admin |
| **Offline tolerance** | N/A | ❌ (necesita conexión) | ✅ Ilimitado |
| **Complejidad** | Baja | Media | Alta |

---

## 🔧 Troubleshooting por Persona

### Solo Developer

```bash
# Error: Unable to load shared library 'e_sqlite3'
# Solución: El binario self-contained ya incluye las librerías nativas.
# Asegurate de correr ./dist/engram (no dotnet run)

# Error: Address already in use
# Solución: Otro proceso ocupa el puerto.
fuser -k 7437/tcp
```

### Team Leader

```bash
# Error: 28P01 (password authentication failed)
# Solución: La contraseña de PostgreSQL es incorrecta.
# Verificá ENGRAM_PG_CONNECTION

# Error: 42P01 (relation does not exist)
# Solución: Las tablas se crean automáticamente al iniciar.
# Verificá que el usuario de PostgreSQL tenga permisos CREATE.
```

### IT Admin

```bash
# Error: 42P10 (no unique constraint matching ON CONFLICT)
# Solución: Falta UNIQUE constraint en sync_enrolled_projects.
# El servidor lo crea automáticamente en la última versión.

# Error: Sync disabled en /sync/status
# Solución: Seteá ENGRAM_SYNC_ENABLED=true

# Error: proyecto not found en pull
# Solución: Enrollá el proyecto primero con POST /sync/enroll
```

---

➜ **Siguiente**: [📖 API Reference](API-REFERENCE.md) para todos los endpoints  
➜ **Siguiente**: [🤖 Agent Protocol](AGENT-PROTOCOL.md) para cómo lo usan los agentes IA  
➜ **Siguiente**: [📖 Full Sync Setup](SYNC-SETUP.md) para configuración avanzada
