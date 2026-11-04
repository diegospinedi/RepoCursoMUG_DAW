# ADR-008: Parámetros de negocio en centésimos enteros, en una fila única con valores iniciales por migración

| Field | Value |
|-------|-------|
| Date | 2026-11-04 |
| Ticket | FEAT-001c |
| Status | Accepted |

## Context
FEAT-001c guarda cuatro parámetros (alícuota de IVA, condición fiscal, múltiplo de redondeo y tope de
identificación) que después usan los cálculos de precio y la facturación (PRDs 02, 06 y 07 de DISC-001).
Los tres numéricos admiten hasta 2 decimales (FR-03 y decisión D-01 de `docs/ddw/specs/decisions-FEAT-001c.md`).
EF Core guarda `decimal` en SQLite como TEXT, que no se compara como número, así que no admite un CHECK de
rango; la base es SQLite (AGENTS.md) y la credencial ya usa una fila única con `CHECK (Id = 1)`.

## Options considered

### Option 1: `decimal` guardado como TEXT, valores iniciales con `HasData`
- **Pros:** es el mapeo por defecto de EF Core y no necesita conversores.
- **Cons:** sin CHECK de rango en la base; los valores iniciales quedan en el snapshot del modelo, y cambiarlos
  genera un `UpdateData` que pisa lo que cargó la operadora.

### Option 2: INTEGER en centésimos detrás de un `ValueConverter`, `InsertData` en la migración
- **Pros:** el valor es exacto, la base tiene CHECK de rango como segunda barrera, el dominio sigue exponiendo
  `decimal`, y la fila inicial se inserta una sola vez, también en las bases creadas con FEAT-001a y FEAT-001b.
- **Cons:** hace falta un conversor por columna, y un valor que no entra en 64 bits tiene que rechazarse antes
  de convertirlo.

## Decision
Se eligió la opción 2. La tabla `ConfiguracionNegocio` tiene una sola fila (`CHECK (Id = 1)`) con
`AlicuotaIvaCentesimas`, `MultiploRedondeoCentavos`, `TopeIdentificacionCentavos` (NULL mientras no se cargue) y
`CondicionFiscal` como TEXT con CHECK de sus dos valores. La migración inserta 2100, 'responsable_inscripto',
1 y NULL. Ninguna capa guarda los valores en memoria: cada operación lee la fila (FR-06).

## Consequences
- El dominio (`ConfiguracionNegocio`) expone `decimal` y su política de reglas; la conversión a centésimos vive
  solo en `OpticaDbContext`.
- Archivos: `OpticaDbContext.cs`, la migración `ConfiguracionNegocio` y su snapshot, `ConfiguracionNegocioRepository.cs`.
- Un valor de más de 2 decimales o fuera del rango del tipo se rechaza como error del campo, nunca como desborde.
- Cambiar los valores iniciales en el futuro implica una migración nueva que decida qué hacer con las bases ya
  instaladas.
