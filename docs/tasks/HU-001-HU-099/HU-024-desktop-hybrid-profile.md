# HU-024 — Perfil Desktop Híbrido: SQLite local + PostgreSQL Docker

**As**: Power user con múltiples equipos
**I want**: Tener SQLite local Y PostgreSQL Docker corriendo simultáneamente en mi desktop, sirviendo a otros equipos
**To**: Máxima resiliencia (si Docker cae, SQLite local funciona), máximo performance (lectura local), y sync para múltiples dispositivos

---

## Context

El perfil `desktop` actual (HU-010/HU-012) usa solo PostgreSQL como backend. Si Docker no funciona, el sistema queda inaccessible.

**Caso de uso real:**
- Desktop (PC potente): SQLite local + PostgreSQL Docker → sirve a sí mismo y a otros
- Laptop/PC nuevo: `offline-first` → SQLite local + sync al desktop
- Sync bidireccional entre equipos

---

## Acceptance Criteria

### Perfil Desktop híbrido

- [ ] `DeployProfile.Desktop` setea: `ENGRAM_DB_TYPE=sqlite` (no postgres)
- [ ] `DeployProfile.Desktop` setea: `ENGRAM_SYNC_ENABLED=true`
- [ ] `DeployProfile.Desktop` setea: `ENGRAM_SERVER_URL=http://localhost:7437` (self-serving)
- [ ] PostgreSQL Docker corre en paralelo como servidor de sync (no como backend primario)
- [ ] Datos se guardan en SQLite local (lectura/escritura inmediata)
- [ ] SyncManager empuja y recibe mutaciones al PostgreSQL Docker

### Experiencia multi-dispositivo

- [ ] Desktop sirve como "remote server" para laptop/PC nuevos
- [ ] Laptop usa `offline-first` apuntando a `http://<desktop-ip>:7437`
- [ ] Sync bidireccional: cambios en desktop aparecen en laptop y viceversa

### Resiliencia

- [ ] Si PostgreSQL Docker no está disponible: engram sigue funcionando con SQLite local
- [ ] Sync se reanuda automáticamente cuando Docker vuelve
- [ ] Sin data loss: SQLite local es source of truth

### Installer

- [ ] `install.sh` para perfil `desktop` ofrece modo **híbrido** (SQLite + PG Docker)
- [ ] Docker Compose genera configuración para PostgreSQL Docker (como servidor de sync)
- [ ] `ENGRAM_SERVER_URL` default = `http://localhost:7437` (self-serving)

---

## Tasks (Implementation)

- [ ] `src/Engram.Store/DeployProfile.cs` — cambiar Desktop: `DB_TYPE=sqlite`, `SYNC_ENABLED=true`, `SERVER_URL=http://localhost:7437`
- [ ] `src/Engram.Cli/Program.cs` — verificar que `OpenStore` con SQLite + sync funciona correctamente
- [ ] `scripts/install.sh` — modo `desktop` ahora genera Docker Compose para PG Docker como sync server (no como backend)
- [ ] `scripts/install.sh` — generar config para que engram serve use SQLite pero SyncManager apunte a localhost:7437
- [ ] `docker/docker-compose.yml` — perfil desktop usa el compose existente (PG Docker)
- [ ] `docs/DEPLOYMENT.md` — actualizar sección desktop: explicar modo híbrido
- [ ] `docs/01-QUICK-START.md` — tabla de perfiles actualizada
- [ ] Tests: `DeployProfileTests` — Desktop defaults verifican SQLite + sync
- [ ] Tests de integración: desktop híbrido + offline-first client

---

## Notes

- **Dependencia**: HU-010 (Deployment Profile System) y HU-012 (profile rename) — el concepto de perfil ya existe
- **Decisión técnica**: PostgreSQL Docker en desktop actúa como "server" para sync, no como storage backend. SQLite local es siempre el storage primario.
- **Docker Compose**: el compose generado para desktop levanta PostgreSQL + engram serve. Ambos corren en el mismo host.
- **Relación offline-first**: laptop/PC nuevos usan offline-first porque sus datos van a SQLite local y sync al desktop. El desktop usa su propia SQLite como storage y serve a través de Docker.
- **Posible ADR**: Si la decisión de "dual backend" (SQLite + sync server) es significativa, documentar en ADR.
