# ADR-025: Número registrado antes del envío y consulta por número

| Field | Value |
|-------|-------|
| Date | 2026-11-14 |
| Ticket | FEAT-008 |
| Status | Accepted |

## Context
En FEAT-007 la emisión pendiente se grababa antes de llamar a ARCA y el número lo pedía el autorizador adentro
(ADR-021, ADR-022), así que la pendiente no sabía qué número se había solicitado. El PRD 07 exigía registrarlo antes
del envío y verificar por ese número (FR-01, FR-04), y la auditoría encontró dos riesgos de duplicado: un segundo
pedido que llegaba después de la factura del primero (F-1) y el candado tomado dos veces desde la verificación (F-2).

## Options considered

### Option 1: pendiente sin número y número en una segunda escritura
- **Pros:** conservaba el orden de ADR-021.
- **Cons:** la pendiente era inmutable (trigger); actualizarla exigía quitar y volver a agregar, con un intervalo sin
  número registrado.

### Option 2: la aplicación pide el próximo número y graba la pendiente con él
- **Pros:** el número quedó registrado antes de FECAESolicitar; la consulta del próximo número es de solo lectura en
  ARCA, así que una falla ahí no deja nada grabado.
- **Cons:** el puerto pasó de una operación a tres y la emisión de dos transacciones a tres.

## Decision
Option 2. El puerto quedó con `ProximoNumeroAsync`, `AutorizarAsync(solicitud, numero)` y `ConsultarAsync`
(FECompConsultar). `ProcesoEmision` recibe el turno del candado ya tomado (una sola toma por operación) y graba la
pendiente en una transacción `BEGIN IMMEDIATE` que repite las comprobaciones de Final, factura y pendiente.

## Consequences
- Reemplaza en parte a ADR-021 (orden y cantidad de transacciones) y a ADR-022 (forma del puerto).
- Archivos: `IAutorizadorComprobantes`, `AutorizadorWsfe`, `SimuladorArca`, `AutorizadorNoConfigurado`,
  `ClienteWsfe`, `EmitirFactura`, `ProcesoEmision`, `VerificarEmision` y todos los dobles del puerto.
- Una pendiente grabada sin envío (plazo agotado) quedó como caso seguro: la consulta dice "no autorizado".
