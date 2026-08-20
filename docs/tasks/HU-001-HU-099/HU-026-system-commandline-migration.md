# HU-026 — Migrar System.CommandLine a versión estable

**As**: Developer
**I want**: actualizar System.CommandLine de la versión beta a la versión estable (2.0.11)
**To**: resolver el bug de middleware que causa errores de runtime y garantizar compatibilidad futura con .NET 10

---

## Acceptance Criteria

- [ ] El proyecto compila exitosamente con System.CommandLine 2.0.11
- [ ] Todos los comandos CLI funcionan correctamente (`engram serve`, `engram doctor`, `engram mcp`, etc.)
- [ ] El API de System.CommandLine 2.0.11 se usa correctamente (RootCommand.InvokeAsync, AddCommand, etc.)
- [ ] Los tests pasan con la nueva versión
- [ ] El binary pre-built en GitHub Releases usa la versión estable

---

## Tasks (Implementation)

- [ ] Investigar qué métodos/API cambiaron entre beta y 2.0.11 (RootCommand.InvokeAsync, AddCommand, etc.)
- [ ] Actualizar el código en Program.cs para usar el API de 2.0.11
- [ ] Verificar que compile: `dotnet build`
- [ ] Verificar que los tests pasen: `dotnet test`
- [ ] Publicar nuevo release v1.3.1 con el fix
- [ ] Actualizar CHANGELOG con el fix

---

## Notes

- **Versión actual**: `System.CommandLine` 2.0.0-beta4.22272.1 en `src/Engram.Cli/Engram.Cli.csproj`
- **Versión objetivo**: 2.0.11 (última estable en NuGet)
- **Error conocido con beta**: CS1061 — "RootCommand" no contiene definición para "AddCommand" ni "InvokeAsync"
- **Causa**: El API de System.CommandLine cambió significativamente entre beta y estable
- **El error de runtime** (`UseTypoCorrections`, `UseSuggestDirective`, etc.) ocurría al ejecutar `engram doctor` en el contenedor Docker pre-built
- **Relacionado**: HU-022 (fix de librerías nativas), HU-024 (desktop profile)

---

## Investigation Notes

El error CS1061 indica que el API cambió:
```
"RootCommand" no contiene una definición para "AddCommand" ni un método de extensión accesible "AddCommand"
"RootCommand" no contiene una definición para "InvokeAsync"
```

La versión 2.0.11 requiere usar `Command` en vez de métodos de extensión en algunos casos. Investigar el namespace correcto y los métodos disponibles.
