# ADR-012: Bloqueo de conexiones localhost en perfil `remote-server`

**Status:** Accepted
**Date:** 2026-08-06
**Deciders:** victor
**Related:** HU-010, HU-012, ADR-008

## Context

El perfil `remote-server` está diseñado para deployments compartidos en equipos (2-5 personas). El servidor de sync expone endpoints HTTP que reciben conexiones de clientes remotos.

### Problema identificado

En un entorno de servidor compartido, permitir conexiones desde `localhost`, `127.0.0.1` o `::1` representa un vector de ataque:

1. **Confusión de roles**: Un cliente corriendo en el mismo host que el servidor podría accidentally conectars a sí mismo (self-loop) en lugar de al servidor real
2. **Security boundary**: En un server deployment, `localhost` representa una fuente de confianza que no debería existir — el servidor no debe confiar en ningún cliente por el hecho de que corra localmente
3. **ADR-008 self-loop**: El self-loop detection ya maneja el caso de SyncManager apuntando a sí mismo, pero es mejor prevenir que curar

### Casos de uso a considerar

| Profile | Caso de uso | ¿Allow localhost? |
|---------|-------------|-------------------|
| `local` | Desarrollo solo | ✅ Sí (por defecto) |
| `remote-server` | Servidor compartido en red | ❌ No |
| `offline-first` | Cliente SQLite en cada máquina | ✅ Sí (caso normal) |
| `desktop` | Workstation personal | ✅ Sí (uso personal) |

## Decision

Bloquear conexiones entrantes desde `localhost`, `127.0.0.1` y `::1` cuando el servicio corre con perfil `remote-server`.

### Implementación

```csharp
// RemoteServerHostValidator.cs — nuevo componente
public static class RemoteServerHostValidator
{
    private static readonly string[] BlockedHosts =
    {
        "localhost",
        "127.0.0.1",
        "::1"
    };

    public static bool IsBlocked(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        return BlockedHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
    }
}

// En el middleware o endpoint de sync:
if (RemoteServerHostValidator.IsBlocked(request.Host.Host))
{
    return BadRequest(new { error = "localhost_not_allowed", message = "remote-server profile does not accept localhost connections" });
}
```

### Profiles affected

- **`remote-server`**: ❌ Bloquea localhost/127.0.0.1/::1 — implementación requerida
- **`offline-first`**: ✅ Permite localhost (el cliente local es el caso normal)
- **`desktop`**: ✅ Permite localhost (uso personal — no hay threat model)
- **`local`**: ✅ Permite localhost (desarrollo solo — no hay exposición)

### Rationale

1. **Defense in depth**: El self-loop detection (ADR-008) es una segunda línea de defensa; esta validación es la primera
2. **Principle of least privilege**: El servidor `remote-server` no debería asumir que clientes locales son trustworthy
3. **Consistency con production mindset**: En producción real, el servidor y los clientes raramente corren en la misma máquina
4. **No hay false positives legítimos**: Un cliente que quiere conectar a un servidor `remote-server` debería usar el hostname real o IP, no localhost

## Consequences

### Positive

1. **Seguridad**: Previene connections desde sources no confiables en server deployments
2. **Claridad**: El error `localhost_not_allowed` es claro sobre por qué fue bloqueado
3. **Consistencia**: Alinea el comportamiento con el threat model de un servidor compartido

### Negative

1. **Testing local**: Los tests que corren en la misma máquina que el servidor necesitan usar IP o hostname
2. **Docker-in-Docker**: Escenarios donde el contenedor Docker corre tests contra el servidor en el mismo host necesitan configuración especial

### Mitigations

1. **Tests**: Usar `host.docker.internal` o la IP real del host en lugar de `localhost`
2. **Docker**: El compose file ya usa `extra_hosts` para resolver `host.docker.internal`
3. **Documentación**: Este ADR sirve como referencia para configurar tests y entornos de desarrollo

## Compliance

- [ ] Implementación del validator en `RemoteServerHostValidator.cs`
- [ ] Middleware o endpoint intercepta requests con Host bloqueado
- [ ] Tests verifican que localhost es bloqueado para `remote-server`
- [ ] Tests verifican que localhost es permitido para `offline-first` y `desktop`
- [ ] Documentación actualizada en `docs/ARCHITECTURE.md` y `docs/SYNC-SETUP.md`
