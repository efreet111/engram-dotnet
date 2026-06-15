# Contributing to engram-dotnet

Thanks for your interest! This project is in active development and welcomes contributions of all sizes — docs, bugfixes, features, or just feedback.

**Antes de arrancar**: si pensás hacer un cambio grande, abrí un [issue](https://github.com/efreet111/engram-dotnet/issues/new) primero para discutirlo. No queremos que labures al pedo.

---

## Quick start

```bash
# 1. Fork + clone
git clone https://github.com/tu-usuario/engram-dotnet.git
cd engram-dotnet

# 2. Compilar
dotnet build -c Release

# 3. Correr tests (sin Docker)
dotnet test -c Release --filter "FullyQualifiedName!~Postgres.Tests"

# 4. Tests de PostgreSQL (opcional, requiere servidor)
dotnet test tests/Engram.Postgres.Tests -c Release
```

---

## Flujo de trabajo

Seguí el **[GIT-WORKFLOW.md](docs/GIT-WORKFLOW.md)**. En resumen:

| Rama | Para |
|------|------|
| `main` | Siempre deployable. No commits directos. |
| `feat/...` | Features nuevas |
| `fix/...` | Bugfixes |
| `docs/...` | Solo documentación |
| `chore/...` | Tooling, CI, dependencias |

```bash
git checkout -b fix/eng-206-postgres-tests
# hacé tus cambios
dotnet test -c Release
git push -u origin fix/eng-206-postgres-tests
# abrí el PR desde GitHub
```

### Antes del PR

- [ ] `dotnet test -c Release` pasa (sin flags de filtro si tocás Postgres)
- [ ] Agregaste tests si corresponde
- [ ] Actualizaste `CHANGELOG.md` en `[Unreleased]`
- [ ] Actualizaste `docs/BACKLOG.md` si aplica
- [ ] Revisaste que no se cuelen archivos de IDE (`.cursor/`, `.vscode/`, `.idea/`)

---

## Reportar bugs

Abrí un [issue](https://github.com/efreet111/engram-dotnet/issues/new) con:

- Qué esperabas que pase
- Qué pasó en realidad
- Cómo reproducirlo (comandos, payloads)
- Entorno (PostgreSQL / SQLite, versión, OS)

Si es una vulnerabilidad de seguridad, **no abras issue público**. Seguí [SECURITY.md](SECURITY.md).

---

---

## Project Identity (`.engram-id`)

This repo uses a project identity fingerprint for stable memory tracking across clones and renames. The file `.engram-id` at the repository root contains a UUID v5 deterministically computed from the git remote URL and first commit SHA.

- **Commit `.engram-id`** — it must be in version control so all team members share the same identity.
- **Never add `.engram-id` to `.gitignore`** — doing so causes silent identity divergence (each member gets a different project ID, memories become isolated).
- **Don't edit `.engram-id` manually** — the UUID is deterministic. Editing breaks the guarantee.
- If you lose the file, run `engram project id --regenerate` to recreate it (same UUID, deterministic).

---

## Code of Conduct

Este proyecto sigue el [Contributor Covenant](CODE_OF_CONDUCT.md). Sé respetuoso, el feedback constructivo es bienvenido, el maltrato no.

---

## Preguntas?

Abrí un [Discussion](https://github.com/efreet111/engram-dotnet/discussions) o mandá un issue con tag `question`.
