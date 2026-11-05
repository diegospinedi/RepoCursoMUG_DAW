# ADR-013: Transacción de escritura que abarca las lecturas en los casos de uso del catálogo

| Field | Value |
|-------|-------|
| Date | 2026-11-05 |
| Ticket | FEAT-002c |
| Status | Accepted |

## Context
`UnidadDeTrabajoEf.ConfirmarAsync` abría la transacción recién al grabar. El alta y la modificación de artículos leían
la configuración antes, fuera de ella (D-15 de FEAT-002a, D-14 de FEAT-002b). FEAT-002c agregó el recálculo de todo el
catálogo al grabar la configuración (FR-02): con lecturas fuera de la transacción, un alta que leyó la configuración
anterior podía grabar su precio después del recálculo y quedar con un precio de los parámetros viejos.

## Options considered

### Option 1: token de versión en la fila de configuración
- **Pros:** sin transacciones anidadas; detectaba el cambio al confirmar.
- **Cons:** EF no incluye en el UPDATE una fila sin cambios, así que había que tocar la fila de configuración en cada
  alta; el conflicto llegaba como un 409 que la operadora no causó.

### Option 2: una transacción de escritura que abarca lecturas y grabación
- **Pros:** SQLite serializa los escritores; la lectura de la configuración y la grabación del artículo, o la grabación
  de la configuración y el recálculo, quedaron en un mismo instante lógico; sin columnas nuevas.
- **Cons:** las demás escrituras esperaron durante el recálculo (hasta el timeout de 30 s de Microsoft.Data.Sqlite);
  `ConfirmarAsync` dejó de confirmar cuando corre dentro de la operación.

## Decision
Se eligió la opción 2. `IUnidadDeTrabajo` sumó `EjecutarEnTransaccionAsync`, que en EF abrió una transacción con
`BEGIN IMMEDIATE`; dentro, `ConfirmarAsync` solo llamó a `SaveChanges` (con el savepoint automático de EF) y la
operación externa confirmó o revirtió. Se aplicó a la grabación de la configuración con recálculo, al alta y a la
modificación; el cambio de estado quedó afuera (D-27 de FEAT-002b).

## Consequences
- Afectó a `src/Optica.Application/Comun/IUnidadDeTrabajo.cs`, `src/Optica.Infrastructure/Persistencia/UnidadDeTrabajoEf.cs`
  y a los casos de uso del catálogo; los registros en el log quedaron después del commit externo (D-11).
- Ninguna operación dentro de la transacción abrió otra conexión que escriba: se habría bloqueado contra sí misma. Una
  operación anidada se rechazó con un error genérico (D-19 de FEAT-002c).
- Un escritor que esperó más de 30 s recibió SQLITE_BUSY, informado como error genérico.
