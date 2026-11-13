# Threat model FEAT-007: Emisión de factura a consumidor final (ARCA WSFEv1)

| Field | Value |
|-------|-------|
| Ticket | FEAT-007 |
| Spec | docs/ddw/specs/spec-FEAT-007.md |
| Tier | FEATURE |
| Date | 2026-11-13 |

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Domain/Configuracion/PoliticaConfiguracionNegocio.cs` | Block 1 |
| `src/Optica.Api/Controllers/ConfiguracionController.cs` | Block 1 |
| `web/src/paginas/Configuracion.tsx` | Block 2 |
| `src/Optica.Domain/Facturacion/ReglasEmision.cs` | Block 3 |
| `src/Optica.Infrastructure/Persistencia/FacturaRepository.cs` | Block 4 |
| `src/Optica.Infrastructure/Arca/SimuladorArca.cs` | Block 5 |
| `src/Optica.Infrastructure/Arca/ClienteWsaa.cs` | Block 6 |
| `src/Optica.Infrastructure/Arca/ClienteWsfe.cs` | Block 6 |
| `src/Optica.Application/Facturacion/EmitirFactura.cs` | Block 7 |
| `src/Optica.Api/Controllers/PresupuestosController.cs` | Block 7 |
| `web/src/paginas/Presupuesto.tsx` | Block 8 |

## Trust boundaries
- Navegador → Api: `POST /api/presupuestos/{numero}/factura` (concepto), `POST /api/configuracion` (alícuota y punto de
  venta) y `GET /api/presupuestos/{numero}` (factura y emisión pendiente), por HTTPS en `localhost`, con la cookie de
  sesión.
- Api → ARCA (internet): `ClienteWsaa` envía a WSAA el pedido de acceso firmado con el certificado de la empresa y
  recibe el token y la firma; `ClienteWsfe` envía a WSFEv1 el token, la firma, el CUIT, el punto de venta, los importes
  y, sobre el tope, el DNI del cliente, y recibe el CAE o los errores. HTTPS con la validación de certificados por
  defecto de .NET.
- Api → sistema de archivos local: el PFX del certificado y su clave (configuración local fuera del repositorio), el
  ticket de acceso guardado (`arca-ta-<entorno>.json`) y el contador del simulador (`arca-simulado.json`) en el
  directorio de datos.
- Api → base SQLite: facturas, líneas de factura y emisiones pendientes, protegidas por triggers `registro_cerrado`.

## STRIDE analysis
### `src/Optica.Domain/Configuracion/PoliticaConfiguracionNegocio.cs`
- **Spoofing:** no identifica a nadie; quien graba la configuración ya pasó la sesión en el controlador.
- **Tampering:** una alícuota fuera de las 6 de ARCA o un punto de venta fuera de 1 a 99999 se rechaza al grabar
  (FR-26, FR-28); la base suma `CHECK` del punto de venta como segunda barrera (R-04).
- **Repudiation:** el cambio de configuración no se registra hoy por quién (instalación de una sola operadora); no
  cambia en este ticket.
- **Information Disclosure:** los mensajes de infracción nombran el campo y nunca el valor recibido.
- **Denial of Service:** una alícuota guardada antes del cambio se sigue leyendo (D-06): no deja la configuración ni el
  recálculo en 500 (R-05).
- **Elevation of Privilege:** no hay roles; la política no concede permisos.

### `src/Optica.Api/Controllers/ConfiguracionController.cs`
- **Spoofing:** requiere sesión por la política por defecto; `ProteccionTests` incluye la ruta.
- **Tampering:** el punto de venta se lee como número JSON estricto (sin texto ni decimales) con el límite de cuerpo de
  1 KB existente (R-04).
- **Repudiation:** sin cambios respecto de FEAT-001c.
- **Information Disclosure:** el GET devuelve el punto de venta (D-05) pero nunca el certificado, el CUIT ni la clave;
  `SinDatosArcaTests` sigue prohibiendo `certificado` en toda respuesta (R-03).
- **Denial of Service:** límite de cuerpo existente; sin trabajo nuevo costoso.
- **Elevation of Privilege:** no hay endpoint para el certificado ni el CUIT: no se pueden cambiar desde la web (R-03).

### `web/src/paginas/Configuracion.tsx`
- **Spoofing:** la pantalla está detrás del ingreso; un 401 vuelve a `/ingreso`.
- **Tampering:** la alícuota es una lista cerrada y el punto de venta se valida en el navegador y otra vez en la Api.
- **Repudiation:** no aplica en el navegador.
- **Information Disclosure:** muestra solo alícuota, condición, múltiplo, tope y punto de venta; nada del certificado.
- **Denial of Service:** sin trabajo nuevo en el navegador.
- **Elevation of Privilege:** no hay controles de rol que eludir.

### `src/Optica.Domain/Facturacion/ReglasEmision.cs`
- **Spoofing:** no aplica (reglas puras).
- **Tampering:** el total facturado es el total del presupuesto en Final (FR-04) y neto + IVA suman exactamente el total
  (FR-14); el tipo sale de la condición fiscal vigente y el receptor del tope vigente, sin datos de la pantalla (R-06).
- **Repudiation:** no aplica.
- **Information Disclosure:** el DNI se incluye en la solicitud solo sobre el tope (FR-11, R-07); los tipos con DNI no
  muestran datos en `ToString`.
- **Denial of Service:** cálculos acotados por `ReglasImporte.ImporteMaximo`.
- **Elevation of Privilege:** no aplica.

### `src/Optica.Infrastructure/Persistencia/FacturaRepository.cs`
- **Spoofing:** no aplica.
- **Tampering:** la factura y sus líneas no se modifican ni se borran: triggers `RAISE(ABORT, 'registro_cerrado')` sobre
  `Factura` y `LineaFactura`, sin métodos de actualización en el repositorio y sin endpoints (FR-25, R-08); un índice
  único por presupuesto impide una segunda factura (FR-03, R-09).
- **Repudiation:** la factura guarda tipo, punto de venta, número, fecha, CAE, vencimiento y entorno (FR-18).
- **Information Disclosure:** consultas parametrizadas de EF Core; las filas no muestran datos del receptor en
  `ToString`; base sin cifrar (R-13, aceptado).
- **Denial of Service:** escrituras dentro de la transacción corta de la unidad de trabajo, nunca durante la llamada a
  ARCA (D-09).
- **Elevation of Privilege:** no aplica.

### `src/Optica.Infrastructure/Arca/SimuladorArca.cs`
- **Spoofing:** responde solo dentro del proceso; no abre puertos.
- **Tampering:** su contador vive en el directorio de datos; alterarlo solo cambia números de comprobantes simulados, que
  no tienen validez fiscal.
- **Repudiation:** cada factura simulada queda guardada con entorno `simulado` (FR-18), que el PRD 08 usa para la
  leyenda.
- **Information Disclosure:** no envía nada fuera del proceso.
- **Denial of Service:** `no_responder` espera hasta el tiempo máximo y libera la emisión (R-11).
- **Elevation of Privilege:** una instalación que factura en simulado cree emitir comprobantes reales (R-10).

### `src/Optica.Infrastructure/Arca/ClienteWsaa.cs`
- **Spoofing:** el pedido de acceso va firmado con el certificado de la empresa (CMS); un tercero no puede pedir un
  token en su nombre sin la clave privada (R-01). La URL de WSAA es una constante por entorno, nunca configurable desde
  la web.
- **Tampering:** HTTPS con validación de certificados por defecto, sin `ServerCertificateCustomValidationCallback` (R-12).
- **Repudiation:** se registra en el log la obtención o el rechazo del token, sin token, firma ni CMS.
- **Information Disclosure:** el PFX y su clave salen de la configuración local, nunca de `appsettings.json` versionado;
  el token y la firma se guardan cifrados con el `IDataProtector` de la Api en `arca-ta-<entorno>.json` del directorio de
  datos (D-30) y nunca van al log ni a una respuesta (R-02).
- **Denial of Service:** el tiempo máximo cubre WSAA; un WSAA caído termina en "no enviada" y no deja pendiente (D-16).
- **Elevation of Privilege:** el token solo habilita el servicio `wsfe` del CUIT configurado.

### `src/Optica.Infrastructure/Arca/ClienteWsfe.cs`
- **Spoofing:** autentica con token, firma y CUIT de WSAA; el entorno sale de `Arca:Entorno` y el valor por defecto es
  simulado (R-14).
- **Tampering:** el XML se arma con `System.Xml.Linq` (escapa todos los valores) y la respuesta se lee con
  `DtdProcessing.Prohibit` y un tope de 1 MB (R-12).
- **Repudiation:** el log registra tipo, punto de venta, número, resultado y códigos de error, sin el cuerpo SOAP.
- **Information Disclosure:** el cuerpo lleva el DNI sobre el tope y el token: nunca se registra; las excepciones se
  relanzan con texto fijo y sin causa (R-07).
- **Denial of Service:** tiempo máximo de 30 s (NFR-01) y una sola emisión a la vez (D-10, R-11).
- **Elevation of Privilege:** solo `FECompUltimoAutorizado` y `FECAESolicitar`; no hay operaciones de anulación.

### `src/Optica.Application/Facturacion/EmitirFactura.cs`
- **Spoofing:** lo invoca solo el controlador con sesión.
- **Tampering:** valida dentro de una transacción de escritura que el presupuesto esté en Final, sin factura y sin
  emisión pendiente, y graba la pendiente antes de llamar a ARCA: un segundo Facturar simultáneo encuentra la pendiente
  (R-09).
- **Repudiation:** la pendiente guarda el momento del intento; el PRD 07 la resuelve.
- **Information Disclosure:** el resultado al controlador lleva solo el comprobante armado, o el código y la descripción
  de ARCA.
- **Denial of Service:** la llamada a ARCA corre fuera de toda transacción (D-09); la espera por otra emisión en curso
  está acotada por un único plazo de 30 s que cubre candado y llamada (D-32, R-11).
- **Elevation of Privilege:** un Borrador o un presupuesto ya facturado se rechaza antes de llamar a ARCA (FR-02, FR-03,
  R-09).

### `src/Optica.Api/Controllers/PresupuestosController.cs`
- **Spoofing:** la ruta nueva requiere sesión (`ProteccionTests`).
- **Tampering:** el concepto es un texto de dos valores con JSON estricto y límite de cuerpo de 1 KB; no se reciben
  importes, tipo, receptor ni fecha desde el navegador (R-06).
- **Repudiation:** la respuesta 201 lleva el comprobante; los rechazos llevan el código de ARCA.
- **Information Disclosure:** `no-store` en `/api` (FEAT-003a); `PrintMembers` sin valores; ninguna respuesta lleva
  `certificado` (R-03).
- **Denial of Service:** límite de cuerpo; la emisión se serializa.
- **Elevation of Privilege:** no hay `PUT` ni `DELETE` de facturas (FR-25, R-08).

### `web/src/paginas/Presupuesto.tsx`
- **Spoofing:** detrás del ingreso; un 401 vuelve a `/ingreso`.
- **Tampering:** el botón Facturar se deshabilita en Borrador, con factura o con emisión pendiente, pero la Api valida
  igual (R-09).
- **Repudiation:** no aplica.
- **Information Disclosure:** la descripción de ARCA se muestra como texto (React escapa), recortada a 500 caracteres.
- **Denial of Service:** el botón se deshabilita mientras la emisión está en curso (sin doble envío).
- **Elevation of Privilege:** no hay controles de rol que eludir.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| Certificado PFX y su clave privada, clave del PFX | credentials | archivo PFX fuera del repositorio con permisos NTFS; la clave en variable de entorno o configuración local no versionada (D-13); nunca en el log | dentro del proceso; la clave privada nunca sale: solo viaja la firma CMS por HTTPS (TLS 1.2+) a WSAA |
| Token y firma de WSAA | credentials | `arca-ta-<entorno>.json` cifrado con Data Protection (claves protegidas con DPAPI en Windows, D-30) en el directorio de datos con permisos NTFS (vence en ~12 h) | HTTPS (TLS 1.2+) a WSFEv1 |
| CUIT de la empresa | public | configuración local | HTTPS a WSAA y WSFEv1 |
| DNI del cliente enviado a ARCA y guardado en la factura (sobre el tope) | PII | SQLite sin cifrar con permisos NTFS (R-13, aceptado) | HTTPS (TLS 1.2+) a WSFEv1; HTTP local `localhost` con `no-store` hacia el navegador |
| Importes, tipo, punto de venta, número, CAE y vencimiento | financial | SQLite en centavos (ADR-008), inmodificables por triggers | HTTPS a WSFEv1; `localhost` con `no-store` |
| Alícuota y punto de venta de la configuración | financial | SQLite, fila única (ADR-008) | `localhost` con `no-store` |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | un tercero pide un token de WSAA a nombre de la empresa | S | L | C | firma CMS con la clave privada del PFX, que nunca sale del proceso ni del archivo; el PFX fuera del repositorio (D-13) |
| R-02 | el PFX, su clave, el token o la firma terminan en el repositorio, el log, una respuesta o legibles en disco | I | M | C | ticket cifrado con Data Protection (D-30); claves solo en configuración local; `.gitignore` que ya excluye `*.pfx`, `*.p12` y `*.key` (D-26); ningún `Log*` recibe esos valores; excepciones relanzadas con texto fijo y sin causa; prueba de log sin token ni clave (Block 6) |
| R-03 | la web expone o permite cambiar el certificado o el CUIT | I | L | H | sin endpoint para ellos; `SinDatosArcaTests` prohíbe `certificado` en toda respuesta y recorre las rutas nuevas (D-08) |
| R-04 | un punto de venta o una alícuota inválidos llegan a la base o a ARCA | T | M | M | validación en la política al grabar, número JSON estricto, `CHECK` de la columna; pruebas de AC-27 y AC-31 |
| R-05 | una alícuota guardada antes del cambio deja la configuración y el recálculo en 500 | D | M | H | la regla de las 6 alícuotas se aplica solo al grabar (D-06); prueba con una base que tiene 19 |
| R-06 | el navegador manda importes, tipo, receptor o fecha alterados | T | M | H | el endpoint solo recibe el concepto; todo lo demás sale del presupuesto y de la configuración en el servidor |
| R-07 | el DNI del cliente llega al log o a un mensaje de error | I | M | H | sin log del cuerpo SOAP ni de la solicitud; `ToString` sin datos; excepciones con texto fijo; prueba de log de la emisión con DNI |
| R-08 | una factura con CAE se modifica o se borra | T | L | C | triggers `registro_cerrado` en `Factura` y `LineaFactura`; sin métodos ni endpoints; pruebas de AC-23 por la Api y directo en SQLite |
| R-09 | un presupuesto se factura dos veces (doble clic, dos pestañas o un pedido tras un timeout) | E | M | C | pendiente grabada en transacción `BEGIN IMMEDIATE` antes de llamar a ARCA; índice único de factura por presupuesto; pendiente bloquea hasta el PRD 07 (FR-24) |
| R-10 | la óptica factura en entorno simulado creyendo emitir comprobantes reales | E | M | H | `Simulado` es el valor por defecto solo mientras no hay certificado (D-02); advertencia en el log al arrancar en simulado; `docs/instalacion.md` lo explica; la factura guarda el entorno y el PRD 08 imprime la leyenda (AGENTS.md) |
| R-11 | ARCA lento o caído deja emisiones colgadas o bloquea otras pantallas | D | M | M | tiempo máximo de 30 s sobre toda la llamada; la llamada fuera de transacción; una emisión a la vez, con un único plazo para la espera y la llamada (D-32) |
| R-12 | una respuesta de ARCA (o un intermediario) inyecta XML o fuerza la aceptación de un certificado falso | T | L | H | validación TLS por defecto; `DtdProcessing.Prohibit`; tope de 1 MB; solo se leen los nodos esperados |
| R-14 | una prueba o un desarrollo emite contra producción | E | L | C | `Produccion` solo con `Arca:Entorno` explícito; pruebas y harness fijan `Simulado`; prueba de que sin la clave el entorno es simulado (AGENTS.md) |
| R-13 | quien accede a `optica.db` o a un respaldo lee el DNI y los datos de las facturas en claro | I | L | H | sin cambios en el cifrado de la base; accepted, see below |

## Accepted risks
### R-13
- **Accepted by:** el usuario, dueño del proyecto, al aprobar el modelo de amenazas de FEAT-003a (R-01 de ese ticket),
  ratificado en FEAT-006 (R-09)
- **Justification:** el sistema corre en una PC del local con la base fuera del árbol del código y permisos NTFS;
  `docs/instalacion.md` advierte que la base y los backups guardan los datos sin cifrar; la factura guarda el mismo DNI
  que ya guarda el presupuesto.
- **Review conditions:** antes de instalar el sistema con datos reales de clientes, o cuando se implemente el backup
  del PRD 09.

## Supply chain
`System.Security.Cryptography.Pkcs` 10.0.12 (Microsoft, misma versión que el resto de los paquetes .NET del proyecto)
es la única dependencia nueva; queda fijada en los `packages.lock.json` y la CI restaura en modo bloqueado (FEAT-005).
No se agregan dependencias a la web. ARCA es un servicio externo del Estado: su disponibilidad y sus cambios normativos
quedan aislados en `Optica.Infrastructure/Arca` (AGENTS.md, RF-27).

## Availability
El vector es ARCA lento o caído: lo acotan el tiempo máximo de 30 s, la llamada fuera de toda transacción de SQLite y
una emisión a la vez (R-11). La Api escucha solo en `localhost`; el límite de cuerpo de 1 KB de las rutas nuevas y la
sesión obligatoria cubren el resto.
