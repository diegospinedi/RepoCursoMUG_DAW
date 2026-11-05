# ADR-011: Búsqueda de artículos sin mayúsculas ni acentos con una función SQLite registrada en la conexión

| Field | Value |
|-------|-------|
| Date | 2026-11-04 |
| Ticket | FEAT-002b |
| Status | Accepted |

## Context
FR-04 de FEAT-002b pide buscar artículos por coincidencia parcial sin distinguir mayúsculas ni acentos ("armazon"
encuentra "Armazón"), y D-04 fijó que la ñ cuenta como letra. Las funciones `lower()` y `LIKE` de SQLite solo
pliegan ASCII, y el catálogo llega a 10.000 artículos (FEAT-002c). Una coincidencia parcial en el medio del texto no
aprovecha ningún índice, así que la consulta recorre la tabla con cualquier diseño.

## Options considered

### Option 1: columnas plegadas persistidas
- **Pros:** consultas en SQL puro; la base se puede consultar con el plegado desde cualquier herramienta.
- **Cons:** las filas existentes necesitaban un relleno inicial calculado en .NET; las columnas quedaban
  desactualizadas si cambiaba la regla de plegado; no ganaban rendimiento, porque el recorrido era el mismo.

### Option 2: función escalar `plegar` registrada en cada conexión
- **Pros:** una sola regla de plegado, la del dominio (`PlegadoBusqueda`), para la Api y para la base; sin relleno
  ni columnas nuevas; el texto buscado viaja como parámetro.
- **Cons:** la función existe solo en las conexiones que abre la aplicación; `sqlite3` y otras herramientas no la
  conocen.

## Decision
Se eligió la opción 2. La función se registró en el interceptor `PragmasAlAbrir` de `FabricaConexion`, marcada como
determinista, y EF Core la traduce desde un método propio de Infraestructura que llama a `PlegadoBusqueda.Plegar`.
La consulta usa `instr(plegar(columna), @texto) > 0` en lugar de `LIKE`, así `%` y `_` se buscan tal cual. La
función se escribió total: con un valor nulo devuelve nulo, y si el texto no se puede normalizar devuelve el
original en minúsculas, sin lanzar.

## Consequences
- `plegar` quedó fuera de todo objeto del esquema (índices, CHECK, triggers, vistas): si un objeto la usara, la base
  dejaría de abrirse desde `sqlite3` o desde el backup con `VACUUM INTO` (RNF-02).
- Afectó a `src/Optica.Domain/Catalogo/PlegadoBusqueda.cs`, a `src/Optica.Infrastructure/Persistencia/FabricaConexion.cs`
  y a `ArticuloRepository.cs`; las conexiones crudas de las pruebas no tienen la función.
- Se aceptó el recorrido completo de la tabla en cada búsqueda; con 10.000 filas, FEAT-002c lo mide.
- Como en D-18 de FEAT-002a, un cambio de las tablas Unicode de .NET podía cambiar qué textos coinciden.
