# Threat model FEAT-008: Recuperación de emisiones sin respuesta de ARCA

| Field | Value |
|-------|-------|
| Ticket | FEAT-008 |
| Spec | docs/ddw/specs/spec-FEAT-008.md |
| Tier | FEATURE |
| Date | 2026-11-14 |

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Infrastructure/Persistencia/EmisionPendienteRepository.cs` | Block 1 |
| `src/Optica.Infrastructure/Persistencia/DescarteEmisionRepository.cs` | Block 1 |
| `src/Optica.Infrastructure/Arca/ClienteWsfe.cs` | Block 2 |
| `src/Optica.Infrastructure/Arca/SimuladorArca.cs` | Block 3 |
| `src/Optica.Application/Facturacion/ProcesoEmision.cs` | Block 4 |
| `src/Optica.Application/Facturacion/VerificarEmision.cs` | Block 5 |
| `src/Optica.Application/Facturacion/DescartarEmision.cs` | Block 5 |
| `src/Optica.Api/Controllers/PresupuestosController.cs` | Block 6 |
| `src/Optica.Api/Controllers/FacturacionController.cs` | Block 6 |
| `web/src/paginas/Presupuesto.tsx` | Block 7 |
| `web/src/paginas/Inicio.tsx` | Block 7 |

## Trust boundaries
- Navegador → Api: `POST /api/presupuestos/{numero}/factura/verificacion` (sin cuerpo),
  `POST /api/presupuestos/{numero}/factura/descarte` (sin cuerpo), `GET /api/facturacion/emisiones-pendientes` y
  `GET /api/presupuestos/{numero}` (con la observación), por HTTPS en `localhost`, con la cookie de sesión.
- Api → ARCA (internet): `ClienteWsfe` envía a WSFEv1 `FECompConsultar` (tipo, punto de venta y número) y recibe el
  comprobante, que incluye el documento del receptor; además `FECompUltimoAutorizado` y `FECAESolicitar` como en
  FEAT-007. HTTPS con la validación de certificados por defecto de .NET.
- Api → sistema de archivos local: `arca-simulado.json` del simulador (contador y últimos 50 comprobantes por tipo y
  punto de venta) en el directorio de datos.
- Api → base SQLite: `EmisionPendiente` con las columnas nuevas, `ObservacionEmision` y `DescarteEmision`, protegidas
  por triggers.

## STRIDE analysis
### `src/Optica.Infrastructure/Persistencia/EmisionPendienteRepository.cs`
- **Spoofing:** no identifica a nadie; quien graba ya pasó la sesión en el controlador.
- **Tampering:** la pendiente se agrega y se quita, nunca se modifica (`TR_EmisionPendiente_Inmutable` se conserva:
  las columnas se agregan con `ALTER TABLE ADD COLUMN`, D-15); CHECK por columna nueva; lectura estricta "todo o nada".
- **Repudiation:** el momento UTC de la pendiente queda grabado; el log de la emisión registra presupuesto y número.
- **Information Disclosure:** guarda tipo, punto de venta, número, fecha, concepto, importes y si el receptor va
  identificado; nunca el DNI.
- **Denial of Service:** una pendiente por presupuesto (clave primaria); sin listas sin tope: la lista de pendientes
  devuelve solo números.
- **Elevation of Privilege:** sin métodos de modificación; la transacción que la graba repite las comprobaciones de
  Final, sin factura y sin pendiente (D-08).

### `src/Optica.Infrastructure/Persistencia/DescarteEmisionRepository.cs`
- **Spoofing:** sin usuarios que identificar (PRD-001, sin módulo de usuarios); el descarte exige sesión.
- **Tampering:** `DescarteEmision` es de solo agregar: triggers `registro_cerrado` BEFORE UPDATE y BEFORE DELETE.
- **Repudiation:** cada descarte queda con fecha y hora UTC, presupuesto, número, importes y motivo (FR-10).
- **Information Disclosure:** sin DNI ni datos del cliente.
- **Denial of Service:** a lo sumo un descarte por emisión pendiente; crecimiento acotado por las ventas.
- **Elevation of Privilege:** no hay endpoint que lo modifique ni lo borre.

### `src/Optica.Infrastructure/Arca/ClienteWsfe.cs`
- **Spoofing:** el token y la firma de WSAA autentican a la empresa como en FEAT-007; la consulta no agrega credenciales.
- **Tampering:** una respuesta alterada de FECompConsultar podría inducir una recuperación o una emisión equivocadas;
  TLS con validación por defecto, `DtdProcessing.Prohibit`, tope de 1 MB y solo los nodos esperados (R-03).
- **Repudiation:** se registran tipo, punto de venta, número y resultado de la consulta, sin el XML.
- **Information Disclosure:** la respuesta trae `DocTipo` y `DocNro` (DNI del receptor); se descartan al leer y nunca
  van al log ni al resultado (D-14, R-01).
- **Denial of Service:** la consulta corre con el plazo de 30 s (NFR-01), fuera de toda transacción.
- **Elevation of Privilege:** solo consulta; no autoriza nada por sí misma.

### `src/Optica.Infrastructure/Arca/SimuladorArca.cs`
- **Spoofing:** no sale del proceso; sin credenciales.
- **Tampering:** quien edita `arca-simulado.json` puede simular un comprobante con otro importe; solo afecta el
  entorno simulado (sin validez fiscal) y el archivo está en el directorio de datos con permisos NTFS.
- **Repudiation:** registra en el log cada autorización simulada con su número.
- **Information Disclosure:** guarda solo total, CAE ficticio, vencimiento y fecha; ningún dato del receptor (D-13).
- **Denial of Service:** el archivo crece sin tope y llega al límite de tamaño (R-05); se guardan los últimos 50 por
  tipo y punto de venta y el límite sube a 256 KB.
- **Elevation of Privilege:** el simulador nunca se activa en homologación ni producción (`Arca:Entorno`).

### `src/Optica.Application/Facturacion/ProcesoEmision.cs`
- **Spoofing:** recibe el turno del candado ya tomado; no lo toma (D-09).
- **Tampering:** los importes y el receptor salen del presupuesto y de la configuración en el servidor.
- **Repudiation:** registra presupuesto, número solicitado y resultado.
- **Information Disclosure:** el log no lleva la solicitud ni el DNI (R-07 de FEAT-007).
- **Denial of Service:** plazo de 30 s y margen antes de FECAESolicitar (D-10).
- **Elevation of Privilege:** dos pedidos simultáneos no producen dos comprobantes: comprobaciones dentro de la
  transacción de la pendiente (R-02).

### `src/Optica.Application/Facturacion/VerificarEmision.cs`
- **Spoofing:** se llama desde el controlador con sesión.
- **Tampering:** la comparación de importes es exacta en centavos; el número consultado es el registrado, no uno que
  mande el navegador.
- **Repudiation:** registra presupuesto, número y resultado de la verificación.
- **Information Disclosure:** el resultado no lleva el DNI; la factura recuperada guarda el DNI del presupuesto como en
  FEAT-007.
- **Denial of Service:** exige 1 minuto desde la pendiente (FR-16, R-04) y un plazo de 30 s; no reintenta.
- **Elevation of Privilege:** no emite si la pendiente es de otro entorno o no tiene número (FR-14, FR-15).

### `src/Optica.Application/Facturacion/DescartarEmision.cs`
- **Spoofing:** se llama desde el controlador con sesión.
- **Tampering:** vuelve a comprobar el motivo contra el estado actual antes de borrar (D-12).
- **Repudiation:** graba el registro de descarte en la misma transacción que quita la pendiente.
- **Information Disclosure:** el registro y la respuesta no llevan datos del cliente.
- **Denial of Service:** una sola operación por pedido, sin llamadas a ARCA.
- **Elevation of Privilege:** un pedido directo a la Api sin observación vigente se rechaza con 409 (FR-11, R-06).

### `src/Optica.Api/Controllers/PresupuestosController.cs`
- **Spoofing:** las rutas nuevas exigen sesión (`ProteccionTests`).
- **Tampering:** sin cuerpo: el número viene de la ruta (`long`); un cuerpo es ignorado y el tamaño está acotado.
- **Repudiation:** las respuestas 409 nombran el motivo sin datos del cliente.
- **Information Disclosure:** `Cache-Control: no-store` en las respuestas (`SinCacheTests`); la observación del GET no
  lleva el DNI.
- **Denial of Service:** la verificación toma el candado de emisión: una a la vez.
- **Elevation of Privilege:** no hay endpoint para modificar facturas, pendientes ni descartes.

### `src/Optica.Api/Controllers/FacturacionController.cs`
- **Spoofing:** exige sesión.
- **Tampering:** solo lectura.
- **Repudiation:** no aplica: solo lectura.
- **Information Disclosure:** devuelve solo números de presupuesto, con `no-store`.
- **Denial of Service:** una consulta por índice de la clave primaria.
- **Elevation of Privilege:** sin operaciones de escritura.

### `web/src/paginas/Presupuesto.tsx`
- **Spoofing:** sin sesión, la Api responde 401 y la pantalla va a `/ingreso`.
- **Tampering:** la pantalla no manda importes ni números: solo pide verificar o descartar.
- **Repudiation:** el descarte pide confirmación explícita (FR-09).
- **Information Disclosure:** los avisos muestran número de comprobante e importes, no datos del cliente.
- **Denial of Service:** un pedido a la vez; el botón queda deshabilitado mientras verifica.
- **Elevation of Privilege:** la Api rechaza igual un descarte no permitido.

### `web/src/paginas/Inicio.tsx`
- **Spoofing:** solo con sesión iniciada.
- **Tampering:** solo lectura.
- **Repudiation:** no aplica: solo lectura.
- **Information Disclosure:** muestra números de presupuesto.
- **Denial of Service:** un solo pedido al cargar, sin consultas periódicas; una falla no muestra nada.
- **Elevation of Privilege:** sin acciones.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| DNI del receptor en la respuesta de FECompConsultar | PII | no se guarda: se descarta al leer la respuesta | HTTPS (TLS 1.2+) desde WSFEv1 |
| DNI en la factura recuperada (tomado del presupuesto, sobre el tope) | PII | SQLite sin cifrar con permisos NTFS, como en FEAT-007 (R-13 de FEAT-007, aceptado) | `localhost` con `no-store` |
| Número, importes, concepto, CAE y vencimiento de la pendiente, la observación y el descarte | financial | SQLite en centavos, protegidos por triggers | HTTPS a WSFEv1; `localhost` con `no-store` |
| Comprobantes del simulador (total, CAE ficticio, vencimiento, fecha) | financial | `arca-simulado.json` en el directorio de datos con permisos NTFS, sin validez fiscal | no sale del proceso |
| Token y firma de WSAA | credentials | sin cambios respecto de FEAT-007: cifrados con Data Protection | HTTPS (TLS 1.2+) a WSFEv1 |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | el DNI de la respuesta de FECompConsultar llega al log, al resultado o a una respuesta de la Api | I | M | H | `ClienteWsfe` lee solo los nodos de importe, CAE, vencimiento, fecha, resultado y errores; sin log del XML; excepciones solo con su tipo; prueba de log con un XML que trae un DNI ficticio (D-14) |
| R-02 | dos pedidos del mismo presupuesto (emisión y verificación, o dos pestañas) producen dos comprobantes | E | M | C | candado de emisión tomado una sola vez por operación (D-09); comprobaciones de Final, factura y pendiente dentro de la transacción `BEGIN IMMEDIATE` que graba la pendiente (D-08); prueba en paralelo |
| R-03 | una respuesta de consulta alterada induce a recuperar con importe equivocado o a emitir de nuevo | T | L | C | TLS con validación por defecto; `DtdProcessing.Prohibit`; tope de 1 MB; comparación exacta en centavos; solo "no autorizado" con el código 602 permite emitir |
| R-04 | ARCA autoriza tarde el envío original y la verificación hecha enseguida emite otro comprobante | T | M | C | 1 minuto desde la pendiente antes de verificar (FR-16, D-07); prueba con reloj controlado |
| R-05 | el archivo del simulador crece hasta el límite y deja toda emisión en "no enviada" | D | M | M | últimos 50 comprobantes por tipo y punto de venta y límite de 256 KB (D-13); prueba de crecimiento |
| R-06 | un pedido directo a la Api descarta una pendiente que era de esta venta | E | L | H | el descarte exige una observación vigente y vuelve a comprobar el motivo contra el estado actual (FR-11, D-12); confirmación en la pantalla; registro del descarte |
| R-07 | se modifica o se borra un registro de descarte o una observación | T | L | M | triggers `registro_cerrado` en `DescarteEmision` (UPDATE y DELETE) y en `ObservacionEmision` (UPDATE); sin endpoints |
| R-08 | la migración reconstruye `EmisionPendiente` y pierde su trigger de inmutabilidad | T | L | H | `ALTER TABLE ADD COLUMN` por SQL; prueba de migración que verifica el trigger, las filas viejas con columnas nulas y el Down (D-15) |
| R-09 | ARCA lento o caído deja la verificación colgada o bloquea emisiones | D | M | M | plazo de 30 s para la consulta y otro para la emisión de FR-06; llamadas fuera de toda transacción; sin reintento (D-10) |
| R-10 | se verifica una pendiente de otro entorno y se emite en el entorno equivocado | E | L | H | FR-15: sin consulta ni emisión; `Factura.Recuperada` valida el entorno (D-18) |

## Supply chain
No se agregan dependencias: `FECompConsultar` usa el cliente SOAP propio de FEAT-007 (ADR-023). ARCA sigue aislado en
`Optica.Infrastructure/Arca` (AGENTS.md, RF-27). La web no suma paquetes.

## Availability
El vector es ARCA lento o caído durante la verificación: lo acotan los plazos de 30 s, la consulta fuera de toda
transacción y una operación a la vez por el candado de emisión (R-09). La espera de 1 minuto es por presupuesto y no
afecta a los demás. La Api escucha solo en `localhost`, y las rutas nuevas no tienen cuerpo y exigen sesión.
