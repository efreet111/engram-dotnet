# HU-058 — Sync automático después de levantar servidor

**ENG:** ENG-488  
**Tipo:** Bug / Feature  
**Prioridad:** P1  
**Esfuerzo:** M (4-8 horas)  
**Estado:** Idea  
**Origen:** ← Descubierto durante FF-003 (FlowForge onboarding flow) el 2026-08-23

---

## Problema que resuelve

Después de levantar el servidor de engram (docker start engram), el sync **no funciona automáticamente**. Las observaciones guardadas localmente no se sincronizan con el servidor remoto, incluso cuando:

1. El servidor está corriendo y accesible
2. Los proyectos están enrolled
3. El config.json tiene `sync.remote_url` configurado
4. El sync status muestra `Enabled: True, Health: healthy`

**Síntomas observados:**
- `Total pushed: 0` (aunque hay observaciones locales sin sincronizar)
- Servidor remoto no tiene las observaciones (búsquedas retornan 0 resultados)
- El usuario tiene que configurar manualmente `ENGRAM_SERVER_URL` como variable de entorno
- El campo `sync.remote_url` en config.json **NO es leído por SyncManager**

**Impacto:**
- Las memorias quedan atrapadas en el cliente local
- Otros desarrolladores no pueden acceder a las memorias del equipo
- El usuario piensa que el sync funciona pero en realidad no está pushando datos
- Riesgo de pérdida de datos si el cliente local se daña

---

## Root cause analysis

### Problema 1: SyncManager no lee remote_url de config.json

**Evidencia:** Memoria #44 (2026-07-16)
```
config.json tiene campo sync.remote_url pero SyncManager NO lo lee

Engañoso para usuarios. Ven el campo en config.json y piensan que está configurado, 
pero en realidad no lo está.
```

**Causa:** SyncManager requiere la variable de entorno `ENGRAM_SERVER_URL` en lugar de leer el campo `sync.remote_url` del config.json.

**Ubicación del código:**
- `src/Engram.Server/EngramServer.cs:847` (referenciado en memoria #44)
- SyncManager initialization

### Problema 2: Auto-enroll no funciona correctamente

**Evidencia:** Memoria #31 (2026-07-10)
```
EN for the missing "auto-enroll" behavior in the offline-first sync design. 
Currently, every new project must be manually enrolled via `engram sync enroll --project X` 
or `POST /sync/enroll/X` before its local observations are pushed to the team server.
```

**Estado:** Aunque se documentó como feature faltante, el auto-enroll debería estar implementado ahora, pero el sync sigue sin funcionar automáticamente.

### Problema 3: No hay trigger automático de push

**Observación:** Después de levantar el servidor, no hay ningún mecanismo que:
1. Detecte que el servidor está disponible
2. Inicie el proceso de push de observaciones pendientes
3. Sincronice automáticamente las memorias locales con el servidor remoto

---

## Propuesta de solución

### Opción A: Leer remote_url de config.json (Recomendado)

**Cambios:**
1. Modificar SyncManager para leer `sync.remote_url` de config.json
2. Si `ENGRAM_SERVER_URL` está configurado, usarlo como override
3. Si no hay remote_url configurado, mostrar warning claro

**Ventajas:**
- Soluciona el problema de configuración engañosa
- No requiere variables de entorno adicionales
- Más intuitivo para usuarios

**Esfuerzo:** S-M (2-4 horas)

### Opción B: Trigger automático de push después de levantar servidor

**Cambios:**
1. Agregar health check periódico al servidor remoto
2. Cuando el servidor esté disponible, iniciar push automático
3. Loggear el progreso del sync

**Ventajas:**
- Sync completamente automático
- No requiere intervención del usuario

**Esfuerzo:** M (4-8 horas)

### Opción C: Combinar A + B (Mejor solución)

**Cambios:**
1. Leer remote_url de config.json (Opción A)
2. Agregar trigger automático de push (Opción B)
3. Agregar comando `engram sync push --force` para push manual

**Ventajas:**
- Solución completa
- Config intuitiva + sync automático + escape hatch manual

**Esfuerzo:** M-L (6-10 horas)

---

## Criterios de aceptación

### Must have
- [ ] SyncManager lee `sync.remote_url` de config.json
- [ ] Variable de entorno `ENGRAM_SERVER_URL` funciona como override
- [ ] Push automático después de levantar servidor (cuando remote_url está configurado)
- [ ] Comando `engram sync push --force` para push manual
- [ ] Logs claros del progreso del sync

### Should have
- [ ] Health check periódico al servidor remoto
- [ ] Retry logic con backoff exponencial
- [ ] Warning claro si remote_url no está configurado
- [ ] Métricas de sync (total pushed, pending, failed)

### Nice to have
- [ ] UI para ver status del sync en tiempo real
- [ ] Notificación cuando el sync se completa
- [ ] Opción para deshabilitar sync automático

---

## Testing

### Test 1: Sync automático después de levantar servidor
```bash
# 1. Detener servidor
docker stop engram

# 2. Guardar observación local
engram save "test" "test content" --type decision --project flowforge

# 3. Levantar servidor
docker start engram

# 4. Esperar 10 segundos

# 5. Verificar que la observación está en el servidor remoto
curl -s http://192.168.0.178:7437/search?q=test&project=flowforge | jq '.results | length'
# Expected: 1
```

### Test 2: Push manual
```bash
# 1. Guardar observación local
engram save "test2" "test content 2" --type decision --project flowforge

# 2. Push manual
engram sync push --force

# 3. Verificar que la observación está en el servidor remoto
curl -s http://192.168.0.178:7437/search?q=test2&project=flowforge | jq '.results | length'
# Expected: 1
```

### Test 3: Config.json remote_url
```bash
# 1. Configurar remote_url en config.json
jq '.sync.remote_url = "http://192.168.0.178:7437"' ~/.engram/config.json > /tmp/config.json && mv /tmp/config.json ~/.engram/config.json

# 2. Reiniciar engram CLI (o esperar a que recargue config)

# 3. Verificar que SyncManager usa remote_url
engram sync status
# Expected: Enabled: True, Health: healthy

# 4. Guardar observación
engram save "test3" "test content 3" --type decision --project flowforge

# 5. Verificar sync automático
curl -s http://192.168.0.178:7437/search?q=test3&project=flowforge | jq '.results | length'
# Expected: 1
```

---

## Dependencies

- **ENG-435**: Sync recovery (ya completado)
- **ENG-436**: Sync pull e2e test (ya completado)
- **ENG-452**: Self-loop detection (ya completado)
- **ENG-453**: Installer missing ENGRAM_SERVER_URL prompt (ya completado, pero relacionado)

---

## Risks

### Risk 1: Breaking change en SyncManager
**Probabilidad:** Media  
**Impacto:** Alto  
**Mitigación:** Mantener compatibilidad con variable de entorno `ENGRAM_SERVER_URL` como override

### Risk 2: Push automático causa conflictos
**Probabilidad:** Baja  
**Impacto:** Medio  
**Mitigación:** Agregar flag para deshabilitar push automático, implementar merge strategy

### Risk 3: Performance impact
**Probabilidad:** Baja  
**Impacto:** Bajo  
**Mitigación:** Health check con intervalos configurables, batch push en lugar de push por observación

---

## References

- **Memoria #44**: config.json remote_url no se usa para sync
- **Memoria #31**: EN: Auto-enroll project on first save
- **Memoria #25**: ENG-453: installer missing ENGRAM_SERVER_URL prompt
- **FF-003**: FlowForge onboarding flow (donde se descubrió el problema)
- **ENG-485**: Onboarding flow para teams (relacionado, HU-055)

---

## Notes

Este issue fue descubierto durante la implementación de FF-003 (FlowForge onboarding flow). Las memorias de FF-003 están guardadas localmente pero no se sincronizaron automáticamente con el servidor remoto, lo que reveló los problemas documentados arriba.

**Workaround actual:**
```bash
export ENGRAM_SERVER_URL="http://192.168.0.178:7437"
engram sync enroll --project flowforge
# Luego las observaciones se sincronizan (pero requiere variable de entorno)
```

**Solución ideal:**
El sync debería funcionar automáticamente después de levantar el servidor, sin requerir variables de entorno adicionales, leyendo la configuración de config.json.
