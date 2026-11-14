# ADR-021: Emisión de la factura en dos transacciones con una emisión pendiente

| Field | Value |
|-------|-------|
| Date | 2026-11-13 |
| Ticket | FEAT-007 |
| Status | Accepted |

## Context
Emitir una factura (PRD de FEAT-007) es pedir el CAE a ARCA por la red, con hasta 30 s de espera (NFR-01). Un timeout
no dice si ARCA autorizó el comprobante, y volver a pedirlo podría duplicarlo (FR-23, FR-24). ADR-013 abre las
escrituras con `BEGIN IMMEDIATE` en SQLite: mientras una transacción está abierta, ninguna otra sesión escribe, y la
unidad de trabajo prohíbe abrir otra conexión que escriba dentro de una operación (D-19 de esa interfaz).

## Options considered

### Option 1: una sola transacción alrededor de la llamada a ARCA
- **Pros:** la factura se graba o no se graba, sin estados intermedios.
- **Cons:** bloquea todas las escrituras de la aplicación hasta 30 s; un corte del proceso durante la espera no deja
  rastro de que se pidió un CAE, y el presupuesto se podría facturar de nuevo.

### Option 2: llamada sin transacción y grabado al final
- **Pros:** no bloquea la base durante la espera.
- **Cons:** dos pedidos simultáneos del mismo presupuesto llegan los dos a ARCA; un corte después de autorizar pierde la
  factura sin dejar rastro.

### Option 3: emisión pendiente grabada antes de llamar y resuelta después
- **Pros:** la primera transacción, corta, valida y graba la pendiente (que impide un segundo intento); la llamada corre
  fuera de toda transacción; la segunda graba la factura o resuelve la pendiente; un corte deja la pendiente para la
  verificación del PRD 07.
- **Cons:** una tabla y un estado más; un presupuesto con pendiente no se puede facturar hasta que exista el PRD 07.

## Decision
Se eligió la opción 3 (D-09 de FEAT-007), con las emisiones serializadas en el proceso (D-10), porque es la única que
no bloquea la base durante la espera y que deja registro de cada pedido de CAE cuya respuesta no se conoce.

## Consequences
- `EmitirFactura` usa `EjecutarEnTransaccionAsync` dos veces y llama a `IAutorizadorComprobantes` entre ellas.
- Tabla `EmisionPendiente` (migración `Facturas`), con PK por presupuesto.
- Solo "sin respuesta" deja la pendiente; "rechazada" y "no enviada" la quitan (D-16).
- Hasta el PRD 07, una pendiente bloquea el presupuesto sin salida desde la aplicación (riesgo del PRD).
- Un único plazo de 30 s cubre la espera de otra emisión y la llamada; la segunda transacción corre sin cancelación,
  así un pedido HTTP cortado no pierde un CAE ya autorizado (D-32).
- Los desenlaces de ARCA (rechazo, sin respuesta, no enviada) se informan como 409 con `motivo`, igual que los
  conflictos del presupuesto: es una convención del proyecto para que la pantalla los trate a todos igual, no la
  semántica HTTP estricta (502/504).
