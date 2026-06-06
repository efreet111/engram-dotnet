# PRD — Memoria Semántica v1.1

**Status:** Discovery / Análisis  
**Date:** 2026-06-05  
**Source:** Análisis de Victor — sesión post-logging-infrastructure  
**Scope:** Post v1.0.0 (julio 2026+)

---

Este documento captura 10 problemas identificados en la arquitectura actual del sistema de memoria de Engram, con soluciones propuestas. Sirve como fuente para RFCs y backlog items futuros.

## 1. Deriva de Identidad del Proyecto (Project Drift)

**Problema raíz:** Usar el nombre de carpeta o ruta como identificador único es extremadamente frágil.

**Solución recomendada:** Crear una huella de identidad determinista y estable (`.engram-id`) que no dependa de la ubicación física. UUID v5 con `origin URL + first commit SHA`.

**RFC:** [RFC-001 — Project Identity Fingerprint](rfc/RFC-001-project-identity.md)  
**Backlog:** ENG-410

---

## 2. Contradicciones Temporales e Infección de Memoria Falsa

**Problema:** Apilar hechos sin invalidación explícita convierte la base de conocimiento en ruido (ej: "DB host: localhost" → "DB host: 10.0.0.5").

**Soluciones propuestas:**
- Modelo de supersedencia explícita (`SupersedesEngramId`)
- Decaimiento temporal exponencial por importancia
- Detección automática de contradicciones (similitud > 0.9 + diferencias factuales)
- Compactación periódica de obsoletos

**Backlog:** ENG-414

---

## 3. Degradación del Contexto por Ruido Incidental

**Problema:** Guardar todo sin filtro (errores de sintaxis, debug logs, ruido conversacional) contamina las búsquedas.

**Solución:** Taxonomía de tipos de memoria:
- `Decision` — arquitectónica, elección de librería
- `Insight` — aprendizaje, patrón descubierto
- `ErrorSolution` — error resuelto con causa raíz
- `Fact` — configuración, dato verificable
- `Transient` — debug, sintaxis, ruido (TTL corto, excluido de briefing)

**Consolidación:** Job nocturno (o al cerrar sesión) que resuma `Transient` → `SessionSummary`, degrade importancia de memorias no referenciadas.

**Backlog:** ENG-412

---

## 4. Presupuesto de Tokens en Retorno de Consultas

**Problema:** Devolver todo lo similar es inaceptable para viabilidad económica y técnica.

**Solución:** Empaquetador inteligente con `tokenBudget`:
1. Obtener top-N candidatos (similitud × decaimiento)
2. Ordenar por importancia, recencia, tipo
3. Empaquetar incrementalmente hasta agotar presupuesto
4. Incluir `truncated_count` + resumen final

**Backlog:** ENG-413

---

## 5. Aislamiento Multi-usuario y Concurrencia en SQLite

**Problema:** SQLITE_BUSY bajo concurrencia sin WAL.

**Solución:**
- `Journal Mode=WAL` en connection string
- Polly retry con backoff exponencial (3 intentos)
- Channel<WriteRequest> como cola de escritura serializada (opcional, para alta concurrencia)

**Backlog:** ENG-411

---

## 6. Conflictos de Sincronización Local ↔ Nube

**Problema:** Offline en dos máquinas genera memorias contradictorias al sincronizar.

**Solución:**
- Vector de reloj (máquina + timestamp) — gana el más reciente
- Flag de conflicto para resolución humana en contradicciones semánticas
- Merge automático para hechos no conflictivos
- Sincronización selectiva (no subir `Transient` a la nube)

**Backlog:** ENG-415

---

## 7. Evolución del Esquema de la Memoria (Migraciones)

**Problema:** El modelo de engrama cambia con el tiempo; clientes con versiones antiguas pueden romper sync.

**Solución:** Versionar el esquema. Durante sync, transformar engramas al esquema canónico de la nube (siempre actualizado).

**Backlog:** ENG-416

---

## 8. Privacidad y Encriptación Local

**Problema:** SQLite local contiene información sensible del proyecto.

**Solución:** Soporte para SQLCipher con clave derivada de secreto del equipo (open-source friendly).

**Backlog:** ENG-417

---

## 9. Búsqueda Híbrida y Multimodal

**Problema:** Búsqueda puramente vectorial falla con consultas exactas ("puerto 5432").

**Solución:** Combinar vectorial + FTS5 (SQLite) / tsvector (PostgreSQL) + filtros de metadata (tipo, fecha, rama).

**Backlog:** ENG-418

---

## 10. Ciclo de Vida de un Engrama

**Problema:** Solo existen `active` y `deleted` hoy. No hay estados intermedios ni auditoría.

**Solución:** Definir estados: `Active`, `Deprecated`, `Archived`, `Deleted`. No borrar físicamente; excluir de búsquedas normales los no-activos.

**Backlog:** ENG-412 (combinado con taxonomía)

---

## References

- [RFC-001 — Project Identity Fingerprint](rfc/RFC-001-project-identity.md)
- [ENG-410..418](../BACKLOG.md) — backlog items derivados
