# Threat model FEAT-009: PDF, listado y búsqueda de facturas

| Field | Value |
|-------|-------|
| Ticket | FEAT-009 |
| Spec | docs/ddw/specs/spec-FEAT-009.md |
| Tier | FEATURE |
| Date | 2026-11-16 |

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Domain/Configuracion/DatosEmisor.cs` y `Cuit.cs` | Block 1 |
| `src/Optica.Infrastructure/Persistencia/DatosEmisorRepository.cs` y la migración `DatosEmisor` | Block 1 |
| `GET` y `POST /api/configuracion/emisor` (`ConfiguracionController.cs`, `GrabarDatosEmisor.cs`) | Block 2 |
| `src/Optica.Application/Facturacion/QrArca.cs` | Block 4 |
| `src/Optica.Infrastructure/Pdf/GeneradorPdfFactura.cs` (QuestPDF + QRCoder) | Block 4 |
| `GET /api/presupuestos/{numero}/factura` y `GET /api/presupuestos/{numero}/factura/pdf` (`PresupuestosController.cs`, `ObtenerFactura.cs`, `GenerarPdfFactura.cs`) | Block 5 |
| `POST /api/facturacion/busqueda` (`FacturacionController.cs`, `BuscarFacturas.cs`, `FiltroFacturas.cs`, `FacturaRepository.BuscarAsync`) | Block 6 |
| `web/src/paginas/BuscarFacturas.tsx`, `DetalleFactura.tsx` y la sección de datos del emisor de `Configuracion.tsx` | Block 3, Block 7 |

## Trust boundaries
- Navegador → Api (`localhost`): los datos del emisor en `POST /api/configuracion/emisor`, los filtros de la búsqueda
  (Apellido, Nombre, DNI, número, fechas) en `POST /api/facturacion/busqueda`, y el número de presupuesto en la ruta del
  detalle y del PDF. Todo exige la cookie de sesión de FEAT-001a.
- Api → SQLite (`optica.db`): lectura de `Factura`, `LineaFactura` y `Presupuesto` (Apellido, Nombre, DNI del cliente) y
  lectura/escritura de la fila única `DatosEmisor`.
- Api → disco de la marca: el logo y los colores de `branding.json` entran al PDF (validados por `ProveedorMarcaArchivo`
  desde FEAT-001a).
- Api → PC de la operadora: el PDF descargado sale de la protección del sistema con el DNI y el nombre del receptor y
  el QR de ARCA.

## STRIDE analysis
### `src/Optica.Domain/Configuracion/DatosEmisor.cs` y `Cuit.cs`
- **Spoofing:** no autentica; solo la usa el caso de uso detrás de la sesión.
- **Tampering:** las invariantes (los cinco datos presentes, CUIT con dígito verificador, longitudes máximas, fecha de
  inicio no futura) se validan al construir, así que ninguna capa puede grabar un emisor inválido (R-03).
- **Repudiation:** no registra; lo hace el caso de uso.
- **Information Disclosure:** `DatosEmisor` es clase sin `ToString` con valores, como `ConfiguracionNegocio`.
- **Denial of Service:** longitudes acotadas (razón social 120, domicilio 200, Ingresos Brutos 30) evitan textos
  gigantes en la base y en el PDF.
- **Elevation of Privilege:** no aplica: no hay roles (módulo de usuarios fuera de alcance).

### `src/Optica.Infrastructure/Persistencia/DatosEmisorRepository.cs` y la migración `DatosEmisor`
- **Spoofing:** no aplica: acceso local por EF Core.
- **Tampering:** EF Core parametrizado; CHECK de longitud y de CUIT de 11 dígitos en la tabla como segunda barrera; la
  migración es `CREATE TABLE` nueva y no toca `Factura`, `Presupuesto` ni sus triggers (R-07).
- **Repudiation:** la grabación queda en el log del caso de uso.
- **Information Disclosure:** la base no se versiona; el CUIT y la razón social son datos públicos del comprobante.
- **Denial of Service:** una sola fila.
- **Elevation of Privilege:** no aplica.

### `GET` y `POST /api/configuracion/emisor` (`ConfiguracionController.cs`, `GrabarDatosEmisor.cs`)
- **Spoofing:** exige sesión (fuera de `RutasAnonimas`, que sigue con 5 rutas); sin sesión, 401.
- **Tampering:** JSON estricto, tope de 2 KB (413), `Content-Type` exigido (415), todos los errores por campo (400).
- **Repudiation:** cada grabación queda en el log con fecha y hora UTC y qué campos cambiaron, sin sus valores (R-06).
- **Information Disclosure:** la respuesta lleva solo los cinco datos del emisor; `no-store`; la solicitud con
  `PrintMembers => false`.
- **Denial of Service:** tope de cuerpo de 2 KB.
- **Elevation of Privilege:** no hay roles; misma sesión que el resto de la configuración.

### `src/Optica.Application/Facturacion/QrArca.cs`
- **Spoofing:** el QR apunta siempre a `https://www.afip.gob.ar/fe/qr/` (constante); ningún dato del usuario arma el
  host ni la ruta, solo el JSON en base64 del parámetro `p` (R-04).
- **Tampering:** el JSON se arma con los datos de la factura guardada (inmutable por triggers) y con `System.Text.Json`,
  nunca concatenando texto.
- **Repudiation:** no aplica: cálculo puro.
- **Information Disclosure:** el JSON lleva el DNI del receptor (`nroDocRec`) cuando la factura se emitió con DNI en
  Produccion, como exige la especificación de ARCA; con Consumidor Final, o fuera de Produccion, lleva 0, así escanear
  un comprobante simulado no manda el DNI a `www.afip.gob.ar` (R-02, R-11). El QR no va al log.
- **Denial of Service:** tamaño fijo y acotado del JSON.
- **Elevation of Privilege:** no aplica.

### `src/Optica.Infrastructure/Pdf/GeneradorPdfFactura.cs` (QuestPDF + QRCoder)
- **Spoofing:** la leyenda "COMPROBANTE SIMULADO — SIN VALIDEZ FISCAL" en cada página cuando el entorno de la factura es
  Simulado u Homologacion impide presentar un PDF de prueba como válido (R-05).
- **Tampering:** los importes salen de la factura guardada; el generador no calcula ni redondea nada que no venga de
  ella.
- **Repudiation:** no aplica: genera bytes en memoria.
- **Information Disclosure:** el PDF lleva DNI, Apellido y Nombre solo si la factura se emitió con DNI (FR-08); el
  nombre del archivo es `factura-B-0003-00000125.pdf`, sin datos del cliente; sin metadatos de autor ni título con
  datos personales.
- **Denial of Service:** el logo viene acotado por `ProveedorMarcaArchivo`; las líneas de la factura son las de un
  presupuesto (acotadas en FEAT-003); sin fuentes del sistema.
- **Elevation of Privilege:** QRCoder solo codifica un texto en PNG en memoria; no lee archivos ni abre la red (R-08).

### `GET /api/presupuestos/{numero}/factura` y `GET /api/presupuestos/{numero}/factura/pdf`
- **Spoofing:** exigen sesión; sin sesión, 401.
- **Tampering:** solo lectura; no hay rutas para modificar ni borrar facturas (FR-24, RF-32); los triggers de `Factura`
  siguen sin cambios.
- **Repudiation:** el PDF se genera sin registro de cada descarga, como el del presupuesto (FEAT-006).
- **Information Disclosure:** `{numero:long}` en la ruta; inexistente o sin factura, 404 sin detalle; faltan datos del
  emisor, 409 `sin_datos_emisor` con texto fijo; un 500 no muestra el texto de la excepción; `no-store` en todas las
  respuestas; el comprobante viaja como texto ("Factura B 0003-00000125"), sin claves de ARCA (`SinDatosArcaTests`).
- **Denial of Service:** el PDF se genera en memoria por pedido; una sola PC usa el sistema.
- **Elevation of Privilege:** no hay roles.

### `POST /api/facturacion/busqueda`
- **Spoofing:** exige sesión.
- **Tampering:** los filtros van en el cuerpo, JSON estricto, tope de 4 KB, fechas `yyyy-MM-dd` estrictas, Desde ≤
  Hasta validado en el dominio; Apellido y Nombre plegados y buscados con `instr(plegar(...))` parametrizado; sin SQL
  armado (R-09).
- **Repudiation:** no se registra cada búsqueda (datos personales en los filtros), como en FEAT-006.
- **Information Disclosure:** los filtros no van a la URL ni al log (prueba con valores testigo y prueba de URL sin
  consulta); la respuesta lleva Apellido, Nombre y DNI del cliente del presupuesto también en facturas a Consumidor
  Final (FR-25, decisión del usuario); `no-store`; `ResumenFactura` es clase sin `ToString` con datos (R-01).
- **Denial of Service:** 50 resultados por página; tope de cuerpo; una sola PC.
- **Elevation of Privilege:** no hay roles.

### `web/src/paginas/BuscarFacturas.tsx`, `DetalleFactura.tsx` y la sección de datos del emisor de `Configuracion.tsx`
- **Spoofing:** las pantallas exigen sesión como el resto (redirección a `/ingreso` ante 401).
- **Tampering:** lectores estrictos de las respuestas en `cliente.ts`; el detalle no tiene acciones de edición ni de
  eliminación.
- **Repudiation:** no aplica.
- **Information Disclosure:** el estado de la búsqueda vive en memoria (`useState`), no en la URL ni en
  `localStorage`; los textos se muestran como texto de React, sin `innerHTML`; el PDF se descarga con un `blob` y su
  URL se revoca después.
- **Denial of Service:** paginado de 50.
- **Elevation of Privilege:** no aplica.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| DNI, Apellido y Nombre del cliente en el listado, el detalle y el PDF | PII | SQLite sin cifrar con permisos NTFS (R-13 de FEAT-003a y FEAT-007, aceptado por el usuario); el PDF descargado queda en la PC de la operadora (advertido en `docs/instalacion.md`, FEAT-006) | `localhost` con sesión y `no-store`; filtros en el cuerpo, no en la URL |
| DNI del receptor dentro del QR de ARCA | PII | solo dentro del PDF descargado; no se guarda aparte | `localhost` dentro del PDF |
| Razón social, domicilio comercial, CUIT, Ingresos Brutos y fecha de inicio de la óptica | PII (si la óptica es persona física, la razón social es un nombre y el CUIT contiene un DNI; figuran en cada comprobante) | fila única `DatosEmisor` en SQLite sin cifrar con permisos NTFS, como el resto de la base (R-13 de FEAT-003a, aceptado); nunca en el log | `localhost` con sesión y `no-store` |
| Importes, IVA contenido, CAE y vencimiento de la factura | financial | SQLite en centavos, protegidos por los triggers de FEAT-007 | `localhost` con sesión y `no-store` |
| Cookie de sesión | credentials | sin cambios respecto de FEAT-001a | `localhost`, `HttpOnly` y `SameSite=Strict` |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | los filtros de la búsqueda (DNI, Apellido) llegan a la URL, al log o a la caché del navegador | I | M | H | `POST /api/facturacion/busqueda` con filtros en el cuerpo y `PrintMembers => false`; sin log de la búsqueda; prueba con valores testigo en el log y prueba e2e de URL sin consulta; `no-store` |
| R-02 | el PDF o el QR exponen el DNI de una factura a Consumidor Final | I | L | H | FR-08: "Consumidor Final" sin DNI en el PDF; el QR lleva `tipoDocRec` 99 y `nroDocRec` 0 sin DNI; pruebas con PdfPig y del JSON del QR |
| R-03 | se graba un emisor con CUIT inválido o datos vacíos y el PDF sale con datos que no corresponden | T | M | M | `DatosEmisor` y `Cuit` validan al construir (módulo 11); CHECK en la tabla; FR-04: sin los cinco datos no hay PDF (409 `sin_datos_emisor`) |
| R-04 | el QR lleva a un sitio que no es ARCA | S | L | H | host y ruta constantes en `QrArca`; solo el parámetro `p` sale de datos; prueba que decodifica la URL y el JSON |
| R-05 | un PDF de simulado u homologación se entrega como comprobante válido | S | M | H | leyenda en cada página según el entorno guardado en la factura (FR-14), prueba con dos páginas; sin leyenda solo en Produccion (FR-15) |
| R-06 | los datos del emisor cambian sin rastro, o su valor (nombre o CUIT de una persona física) llega al log | R | L | M | log de cada grabación con fecha y hora UTC y los nombres de los campos cambiados, nunca sus valores; grabación en una transacción |
| R-07 | la migración o un endpoint nuevo permite modificar una factura con CAE | T | L | C | migración `CREATE TABLE DatosEmisor` sin tocar `Factura`; sin rutas de escritura de facturas; prueba de migración que verifica que los triggers de `Factura` y `LineaFactura` siguen |
| R-08 | QRCoder o lo que arrastra trae una vulnerabilidad o `System.Drawing` | E | L | M | QRCoder 1.6.0 (sin dependencias para net6.0+), restauración en modo bloqueado, `dotnet list package --vulnerable --include-transitive` en el SAST; prueba de que la Api no referencia QRCoder (ADR-027) |
| R-09 | inyección SQL o búsqueda costosa con los filtros | T | L | H | EF Core parametrizado con `instr(plegar(...))` registrado (ADR-011); tope de cuerpo y paginado de 50 |
| R-10 | el CUIT del emisor cargado no es el de `Arca:Cuit` y el QR apunta a un comprobante que ARCA no tiene | T | M | M | `docs/instalacion.md` pide que el CUIT del emisor sea el mismo que `Arca:Cuit`; hoy solo se emite en simulado y homologación (PDF con leyenda); condición previa para facturar en producción (ADR-028, D-17) |
| R-11 | escanear el QR de un PDF simulado o de homologación manda el DNI del receptor a `www.afip.gob.ar` por un comprobante que no existe | I | L | M | fuera de Produccion el QR lleva `tipoDocRec` 99 y `nroDocRec` 0 (D-23); prueba `QrArca_FueraDeProduccion_SinDni` |

## Supply chain
Dependencia nueva: QRCoder 1.6.0 (MIT) en `Optica.Infrastructure`, solo `PngByteQRCode` (código administrado, sin
`System.Drawing`); entra a los lockfiles de Infrastructure, Api, Api.Tests e Infrastructure.Tests en modo bloqueado y
se revisa con `dotnet list package --vulnerable` (ADR-027). QuestPDF y PdfPig ya estaban (ADR-018). La web no suma
paquetes.

## Availability
El PDF se genera en memoria por pedido y la búsqueda está paginada de a 50 con tope de cuerpo; la Api escucha solo en
`localhost` y la usa una PC. Ninguna ruta nueva llama a ARCA, así que un ARCA caído no afecta el listado ni el PDF.
