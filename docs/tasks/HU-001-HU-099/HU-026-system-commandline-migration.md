# HU-026 — Migrar System.CommandLine a versión estable

**As**: Developer
**I want**: actualizar System.CommandLine de la versión beta a la versión estable (2.0.11)
**To**: resolver el bug de middleware que causa errores de runtime y garantizar compatibilidad futura con .NET 10

---

## Acceptance Criteria

- [x] El proyecto compila exitosamente con System.CommandLine 2.0.11 ✅ (Program.cs + tests)
- [x] Todos los comandos CLI funcionan correctamente ✅ (smoke test: `engram --help` → 21 comandos)
- [x] El API de System.CommandLine 2.0.11 se usa correctamente ✅
- [x] Los tests pasan con la nueva versión ✅ (121/121 tests passed)
- [x] CHANGELOG.md actualizado ✅
- [ ] El binary pre-built en GitHub Releases usa la versión estable (pendiente release v1.3.1)

---

## Tasks (Implementation)

- [x] Investigar qué métodos/API cambiaron entre beta y 2.0.11 ✅ (2026-08-20)
- [x] Actualizar Program.cs para usar el API de 2.0.11 ✅ (2026-08-20)
  - `SetHandler` → `SetAction(ParseResult)`
  - `root.Add(cmd)` → `root.Subcommands.Add(cmd)`
  - `command.Add(opt)` → `command.Options.Add(opt)`
  - `GetValueForOption` → `GetValue`
  - `InvocationContext` → `ParseResult`
- [x] Verificar que compile: `dotnet build` ✅ (0 errores en Engram.Cli)
- [x] Migrar tests/Engram.Cli.Tests (7 archivos con 244 errores) ✅ (2026-08-20)
- [x] Verificar que los tests pasen: `dotnet test` ✅ (121/121 tests passed)
- [x] CHANGELOG actualizado ✅
- [x] Fix Dockerfile.allinone para build local con binary compilado ✅
- [x] Actualizar uninstall.sh para limpiar imagen engram-dotnet-allinone ✅
- [ ] Publicar release v1.3.1

---

## Notes

- **Versión actual**: `System.CommandLine` 2.0.11 en `src/Engram.Cli/Engram.Cli.csproj` ✅ (ya estaba actualizada)
- **Error conocido con beta**: CS1061 — "RootCommand" no contiene definición para "AddCommand" ni "InvokeAsync"
- **Causa**: El API de System.CommandLine cambió significativamente entre beta y estable
- **Migración de Program.cs**: Completada ✅ (2026-08-20)
  - 34 errores CS1061 resueltos
  - Build compila con 0 errores en Engram.Cli
- **Tests migrados**: 7 archivos en `tests/Engram.Cli.Tests/` ✅ (2026-08-20)
  - 244 errores corregidos, 121/121 tests pasan
- **Fix Dockerfile.allinone** (follow-on bug encontrado durante testing de install.sh):
  - Problema: `install_build` compilaba el binary local pero `Dockerfile.allinone` siempre descargaba de GitHub
  - Causa raíz: `[[` en RUN es bash, pero el shell de Docker es `/bin/sh` (dash); la condición nunca se cumplía
  - Solución: `[[` → `[` (POSIX), y se usa un placeholder `engram-local` que siempre existe en el build context
  - Archivos: `docker/Dockerfile.allinone`, `scripts/install.sh`, `.gitignore`
- **uninstall.sh actualizado**: ahora limpia `engram-dotnet-allinone:latest` y contenedores del desktop compose
- **Relacionado**: HU-022 (fix de librerías nativas), HU-024 (desktop profile)

---

## Investigation Notes

El error CS1061 indica que el API cambió:
```
"RootCommand" no contiene una definición para "AddCommand" ni un método de extensión accesible "AddCommand"
"RootCommand" no contiene una definición para "InvokeAsync"
```

La versión 2.0.11 requiere usar `Command` en vez de métodos de extensión en algunos casos. Investigar el namespace correcto y los métodos disponibles.
