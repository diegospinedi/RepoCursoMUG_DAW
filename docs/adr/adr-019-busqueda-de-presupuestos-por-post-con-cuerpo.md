# ADR-019: Búsqueda de presupuestos por POST con los filtros en el cuerpo

| Field | Value |
|-------|-------|
| Date | 2026-11-12 |
| Ticket | FEAT-006 |
| Status | Accepted |

## Context
La búsqueda de presupuestos filtra por Apellido, Nombre y DNI del cliente (`docs/ddw/prd/prd-FEAT-006.md`, FR-01), datos
personales que el PRD pide no llevar en la URL ni en el log. La búsqueda de artículos (FEAT-002b) es un
`GET /api/articulos?texto=...&pagina=...`, y ese era el patrón del proyecto para listados paginados.

## Options considered

### Option 1: GET con query string, como Artículos
- **Pros:** sigue el patrón existente; es una lectura idempotente y se puede repetir desde la barra de direcciones.
- **Cons:** el DNI y el apellido quedan en la URL: en el historial del navegador, en los registros de acceso de
  ASP.NET Core y de cualquier proxy, y en la pestaña Red al compartir la pantalla.

### Option 2: POST /api/presupuestos/busqueda con un cuerpo JSON
- **Pros:** los filtros viajan solo en el cuerpo, que la Api no registra; reusa `LimitesDelCuerpo` y el cuerpo estricto
  de las demás solicitudes de presupuestos.
- **Cons:** un POST que no escribe se aparta de la semántica HTTP; no se puede abrir la búsqueda desde un enlace.

## Decision
Se eligió la opción 2: la protección de los datos personales (Ley 25.326) pesa más que la semántica de lectura, y la
pantalla tampoco guarda los filtros en la URL. Artículos sigue con GET porque su texto de búsqueda no es un dato
personal.

## Consequences
- Archivos: `src/Optica.Api/Controllers/PresupuestosController.cs` (acción `Buscar`, con `[RequestSizeLimit]` y una
  solicitud con `PrintMembers => false`), `web/src/api/cliente.ts`.
- `ProteccionTests` y `SinCacheTests` cubren la ruta como cualquier otro POST con sesión.
- Volver a la lista desde un presupuesto no conserva los filtros: el estado vive solo en la pantalla.
