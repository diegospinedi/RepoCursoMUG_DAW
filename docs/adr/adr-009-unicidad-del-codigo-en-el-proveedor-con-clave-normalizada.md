# ADR-009: Unicidad del código en el proveedor con una columna de clave normalizada

| Field | Value |
|-------|-------|
| Date | 2026-11-04 |
| Ticket | FEAT-002a |
| Status | Accepted |

## Context
FR-02 de FEAT-002a pide que no haya dos artículos con el mismo Código en el proveedor, comparando sin
distinguir mayúsculas e ignorando los espacios del principio y del final ("ABC-1" y " abc-1 " son el mismo).
La base es SQLite (AGENTS.md); su intercalación `NOCASE` y la función `upper()` solo pliegan letras ASCII y
no recortan espacios, y la planilla del PRD 03 va a buscar artículos por ese código.

## Options considered

### Option 1: índice UNIQUE sobre la columna con `COLLATE NOCASE` o sobre `upper(trim(...))`
- **Pros:** una sola columna, sin dato derivado que mantener.
- **Cons:** la regla de comparación queda repartida entre el dominio y SQLite, y SQLite no pliega letras con
  tilde ni normaliza Unicode, así que la base y el dominio pueden dar respuestas distintas.

### Option 2: columna `CodigoProveedorClave` calculada por el dominio, con índice UNIQUE
- **Pros:** la regla vive en un solo lugar (el dominio: recorte de espacios, normalización NFC y mayúsculas
  invariantes); la base solo compara bytes iguales; el mismo valor sirve para buscar desde la planilla.
- **Cons:** un dato derivado más por fila, que solo escribe el repositorio.

## Decision
Se eligió la opción 2. El dominio guarda el código tal como se escribió (recortado) y su clave normalizada;
la tabla `Articulo` tiene un índice UNIQUE sobre la clave, que actúa como segunda barrera detrás del chequeo
previo del caso de uso. Una violación de ese índice al confirmar se informa como código duplicado junto al
campo (decisión D-07 de `docs/ddw/specs/decisions-FEAT-002a.md`).

## Consequences
- `Articulo` expone la clave normalizada; `IArticuloRepository` busca por ella.
- Archivos: `src/Optica.Domain/Catalogo/Articulo.cs`, `OpticaDbContext.cs`, la migración `Catalogo`.
- Si en el futuro la tabla suma otro índice UNIQUE, la traducción de `ConflictoAlConfirmarException` a
  "código duplicado" deja de ser exacta: la decisión asumió una sola restricción única en la tabla.
