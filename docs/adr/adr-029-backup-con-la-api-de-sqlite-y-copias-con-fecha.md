# ADR-029: Backup con la API de SQLite y copias con fecha

| Field | Value |
|-------|-------|
| Date | 2026-11-16 |
| Ticket | FEAT-010 |
| Status | Accepted |

## Context
FEAT-010 (`prd-FEAT-010.md`) pide un backup diario consistente sin frenar a la operadora (FR-05), verificado (FR-07),
identificado por fecha (FR-06) y con retención de 7 diarias y 4 mensuales (FR-10 a FR-12). La base está en modo WAL y
AGENTS.md prohíbe la copia de archivo común. El aviso al ingresar necesita la fecha del último backup aunque el
pendrive esté desconectado (FR-09, decisión del usuario).

## Options considered

### Option 1: `VACUUM INTO` hacia el destino
- **Pros:** una sola sentencia SQL; la copia queda compactada.
- **Cons:** reescribe toda la base y toma un bloqueo de lectura largo; no admite escribir primero a un temporal sin
  repetir el nombre; menos control del progreso.

### Option 2: API de backup de SQLite (`SqliteConnection.BackupDatabase`) hacia un temporal
- **Pros:** copia por páginas una foto consistente compatible con WAL sin frenar las escrituras; viene con
  `Microsoft.Data.Sqlite`; permite escribir a `.parcial`, verificar y renombrar.
- **Cons:** la copia hereda el modo WAL y hay que pasarla a `DELETE` para que no deje `-wal` y `-shm` en el destino.

### Option 3: tabla con el registro de cada copia como única fuente de verdad
- **Pros:** consultas simples.
- **Cons:** la tabla y la carpeta pueden discrepar (copias borradas a mano, carpeta cambiada).

## Decision
Se eligió la API de backup hacia `optica-AAAA-MM-DD.db.parcial`, pasada a `journal_mode=DELETE`, verificada con
`PRAGMA integrity_check` y renombrada a `optica-AAAA-MM-DD.db`. Los archivos de la carpeta son la verdad sobre qué
copias hay (retención, restauración) y una tabla de solo agregar `BackupRealizado` guarda la fecha de cada backup
verificado para el aviso con la carpeta inaccesible.

## Consequences
- Una copia con fecha existe solo si pasó la verificación; un `.parcial` nunca cuenta.
- La retención borra solo archivos con el patrón exacto en la carpeta configurada.
- Restaurar usa también la API de backup desde la copia elegida (comando `restaurar-backup`, ADR-030).
- La conexión del backup es propia y sin pool, para que Windows libere el archivo antes del renombre y del borrado.
