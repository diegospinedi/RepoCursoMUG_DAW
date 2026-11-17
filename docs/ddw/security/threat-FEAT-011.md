# Threat model FEAT-011: Actualización de costos por planilla Excel

| Field | Value |
|-------|-------|
| Ticket | FEAT-011 |
| Spec | docs/ddw/specs/spec-FEAT-011.md |
| Tier | FEATURE |
| Date | 2026-11-17 |

Alcance: la carga de una planilla .xlsx desde la pantalla "Actualizar costos" hasta la actualización de los precios
de costo y de venta del catálogo. Es la primera superficie del sistema que recibe un archivo armado por un tercero, el
proveedor. Los modelos de FEAT-001a (sesión, CSRF R-04), FEAT-001c (JSON-only R-02) y FEAT-002c (recálculo en una
transacción) siguen vigentes y no se repiten.

## Components
| Component | Source in the spec |
|---|---|
| `web/src/paginas/ActualizarCostos.tsx` | Block 5 |
| `src/Optica.Api/Controllers/PlanillaCostosController.cs` | Block 4 |
| `src/Optica.Application/Catalogo/ActualizarCostosPorPlanilla.cs` | Block 3 |
| `src/Optica.Infrastructure/Planillas/LectorPlanillaCostosXlsx.cs` | Block 2 |
| `src/Optica.Domain/Catalogo/PlanillaCostos.cs` | Block 1 |
| `src/Optica.Infrastructure/Persistencia/ArticuloRepository.cs` | Block 3 |

## Trust boundaries
- Proveedor → operadora: la planilla .xlsx llega por correo o descarga; su contenido (XML comprimido, cadenas,
  números, partes extra) es controlado por un tercero y no es confiable.
- Navegador → Api: `POST /api/articulos/planilla-costos` lleva el archivo en base64 dentro de un JSON, con la cookie de
  sesión `HttpOnly`, `Secure`, `SameSite=Strict`, por HTTPS en localhost o la red del local.
- Api → lector: los bytes decodificados cruzan al parser de OpenXml (paquete ZIP + XML) en
  `LectorPlanillaCostosXlsx`, que solo devuelve celdas tipadas.
- Application → base SQLite: la transacción `BEGIN IMMEDIATE` escribe costo, venta y versión de los artículos.
- Api → log y Registro de eventos de Windows: solo cantidades y fecha.

## STRIDE analysis
### `web/src/paginas/ActualizarCostos.tsx`
- **Spoofing:** la pantalla está detrás de la sesión; sin sesión la ruta redirige al ingreso como las demás.
- **Tampering:** el navegador solo lee el archivo elegido y lo envía; la validación real es del servidor.
- **Repudiation:** no aplica en el cliente; la carga se registra en el servidor (R-07).
- **Information Disclosure:** los códigos de las filas no aplicadas se muestran por JSX, sin
  `dangerouslySetInnerHTML`; un código con `<script>` se ve como texto (R-08). El archivo no va a la URL ni al storage.
- **Denial of Service:** una tabla de muchas filas no aplicadas puede ser lenta de pintar; el servidor ya limita a
  50.000 filas.
- **Elevation of Privilege:** no hay roles; cualquier usuario con sesión puede actualizar costos, igual que editar un
  artículo.

### `src/Optica.Api/Controllers/PlanillaCostosController.cs`
- **Spoofing:** exige sesión; sin cookie válida responde 401 (prueba en `ProteccionTests`).
- **Tampering:** CSRF: el endpoint solo acepta `application/json` (`LimitesDelCuerpo`, 415 a cualquier otro tipo), que
  un formulario de otro sitio no puede enviar sin preflight, más `SameSite=Strict` y sin CORS (R-01).
- **Repudiation:** el caso de uso registra cada carga confirmada con cantidades y fecha UTC (R-07).
- **Information Disclosure:** los errores son ProblemDetails con textos fijos; nunca el mensaje de una excepción del
  lector ni el contenido de una celda (R-05). Respuesta con `Cache-Control: no-store`.
- **Denial of Service:** `RequestSizeLimit` de 14 MB (10 MB de archivo en base64); más grande → 413 sin leer el cuerpo
  (R-02).
- **Elevation of Privilege:** el endpoint no expone otra operación que la actualización de costos; no crea artículos ni
  cambia márgenes (fuera de alcance del PRD).

### `src/Optica.Application/Catalogo/ActualizarCostosPorPlanilla.cs`
- **Spoofing:** recibe solo lo que el controlador autenticado le pasa.
- **Tampering:** la carga es todo o nada en una transacción; una falla a mitad revierte todo (R-06). Los tokens de
  versión y estado detectan una edición concurrente y devuelven 409.
- **Repudiation:** log Information después del commit con filas leídas, aplicadas, no aplicadas por razón, en cero e
  inactivas, categoría `Optica.Application.Catalogo.Planilla`, enviada al Registro de eventos (R-07).
- **Information Disclosure:** el log no lleva códigos, precios ni nombre del archivo: son datos comerciales de la
  empresa y de sus proveedores (R-05).
- **Denial of Service:** la transacción toma el bloqueo de escritura; con 10.000 filas debe durar menos de 10 s para no
  agotar el timeout de 30 s de las demás escrituras (R-04).
- **Elevation of Privilege:** no aplica: no hay privilegios distintos dentro de la Api.

### `src/Optica.Infrastructure/Planillas/LectorPlanillaCostosXlsx.cs`
- **Spoofing:** un .xls, .csv o PDF renombrado a .xlsx no abre como paquete y se rechaza como "no es .xlsx" por el
  contenido, no por la extensión.
- **Tampering:** un .xlsx armado puede traer XML con DTD o entidades externas (XXE); el SDK de OpenXml no resuelve DTD
  y el lector no usa `XmlDocument` propio (R-03). Partes con macros (`vbaProject.bin`) nunca se ejecutan: solo se leen
  `workbook.xml`, la primera hoja y `sharedStrings.xml`.
- **Repudiation:** no aplica en el lector; la carga la registra el caso de uso.
- **Information Disclosure:** las excepciones del ZIP, del XML y del SDK se atrapan y se traducen a un resultado sin
  mensaje ni causa, para que el contenido de una celda no llegue al log por `ManejadorErroresNoControlados` (R-05).
  Los números se leen con `TryParse`, nunca con `Parse`, cuyo mensaje trae el texto.
- **Denial of Service:** bomba ZIP y planillas enormes: tope de caracteres por parte con
  `OpenSettings.MaxCharactersInPart` (incluye `sharedStrings.xml`), lectura SAX de la hoja, 50.000 filas con datos e
  índice de fila hasta 50.001, 64 KB por celda; pasar un límite rechaza la planilla completa (R-02).
- **Elevation of Privilege:** el lector no escribe en disco ni ejecuta nada del archivo.

### `src/Optica.Domain/Catalogo/PlanillaCostos.cs`
- **Spoofing:** no aplica: es código puro sin identidad.
- **Tampering:** un código con UTF-16 inválido o de más de 50 caracteres termina como "código inexistente" sin
  excepción; códigos repetidos (comparados por la clave de ADR-009) se excluyen todos, así una fila duplicada no puede
  pisar a otra (FR-10).
- **Repudiation:** no aplica: el dominio no registra.
- **Information Disclosure:** no aplica: devuelve el resultado al caso de uso, que decide qué se registra.
- **Denial of Service:** recorre las filas una vez con un diccionario por clave; lineal en la cantidad de filas.
- **Elevation of Privilege:** solo cambia costo y venta; el margen, la descripción y el estado quedan igual (FR-11 es la
  excepción documentada a D-02 para los inactivos).

### `src/Optica.Infrastructure/Persistencia/ArticuloRepository.cs`
- **Spoofing:** no aplica: recibe solo artículos del caso de uso.
- **Tampering:** `RegistrarCostoActualizado` escribe solo costo, venta y versión, con guarda propia que rechaza otras
  columnas modificadas y con activo y versión como tokens de concurrencia.
- **Repudiation:** la versión del artículo sube en cada cambio; queda la huella de que el artículo cambió.
- **Information Disclosure:** los mensajes de error del repositorio no llevan valores (convención existente).
- **Denial of Service:** `AutoDetectChanges` apagado durante el registro, como en el molde, para no volverse cuadrático
  con 10.000 artículos.
- **Elevation of Privilege:** no aplica: no hay SQL armado con texto de la planilla; todo pasa por EF Core
  parametrizado.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| Precio de costo de cada artículo (de la planilla) | financial (dato comercial de la empresa y del proveedor) | en la base SQLite local, sin cifrar, como el resto del catálogo; nunca en log ni en archivos temporales | TLS (HTTPS de Kestrel), en el cuerpo JSON |
| Código en el proveedor | financial (dato comercial) | en la base, como hoy; nunca en log | TLS |
| Archivo .xlsx completo | financial | solo en memoria durante el pedido; no se escribe a disco | TLS |
| Cookie de sesión | credentials | sin cambios respecto de FEAT-001a: clave aleatoria protegida con Data Protection | TLS, `HttpOnly`, `Secure`, `SameSite=Strict` |
| Cantidades de la carga en el log | public | Registro de eventos de Windows | no viaja |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | Un sitio ajeno hace que el navegador de la operadora suba una planilla y cambie todos los costos (CSRF) | T | L | H | JSON-only (`LimitesDelCuerpo`, sin excepción por endpoint), `SameSite=Strict`, sin CORS; prueba de 415 con multipart y de 401 sin sesión |
| R-02 | Bomba ZIP, `sharedStrings.xml` gigante o planilla de un millón de filas agota memoria o CPU de la Api | D | M | H | 14 MB de cuerpo, `MaxCharactersInPart`, lectura SAX, 50.000 filas e índice de fila, 64 KB por celda; rechazo completo; pruebas con cada caso |
| R-03 | XML con entidades externas (XXE) lee archivos locales o hace pedidos | I | L | H | El SDK no procesa DTD; prueba con un .xlsx con `<!DOCTYPE` y entidad externa que termina rechazado sin leer el archivo referido |
| R-04 | La transacción de una carga grande bloquea facturación, edición y backup más de 30 s | D | M | M | Una lectura del catálogo, diccionario en memoria, `AutoDetectChanges` apagado; prueba AC-13 mide < 10 s dentro de la transacción |
| R-05 | Códigos o precios de proveedores llegan al log o al Registro de eventos por el mensaje de una excepción | I | M | M | Lector atrapa y traduce excepciones sin mensaje ni causa; `TryParse`; log solo con cantidades; prueba de que el log no contiene el contenido de una celda |
| R-06 | Una carga interrumpida deja parte de la planilla aplicada sin resultado visible | T | L | M | Una sola transacción y una sola confirmación: todo o nada; prueba de falla al confirmar |
| R-07 | No queda constancia de quién y cuándo cambió los costos de todo el catálogo | R | M | L | Log Information después del commit con cantidades y fecha UTC en el Registro de eventos; la versión de cada artículo sube |
| R-08 | Un código malicioso en la planilla (`<img onerror>`) se ejecuta al mostrar el resultado | I | L | M | La web muestra todo por JSX; prueba de Vitest con un código con HTML que se ve como texto |
| R-09 | Un .xls o un archivo renombrado se interpreta como .xlsx con datos basura | T | M | L | Detección por contenido (paquete OpenXml válido con libro); si no, rechazo "guardá la planilla como .xlsx" |
| R-10 | Dependencia nueva `DocumentFormat.OpenXml` con una vulnerabilidad conocida o una versión flotante | T | L | M | Versión exacta fijada (ADR-031), lock files en modo bloqueado, `dotnet list package --vulnerable` en el SAST, prueba de que la Api no la referencia |

## Supply chain
Una dependencia nueva: `DocumentFormat.OpenXml` (con `DocumentFormat.OpenXml.Framework` y `System.IO.Packaging`
transitivos), publicada por Microsoft bajo licencia MIT, solo en `Optica.Infrastructure`. Versión exacta fijada en
ADR-031, restaurada con lock files (`RestoreLockedMode` en CI) y auditada con `dotnet list package --vulnerable
--include-transitive` en el SAST. El harness e2e no suma paquetes npm: arma el .xlsx con `node:zlib`.

## Availability
Los vectores de DoS son R-02 (archivo armado) y R-04 (bloqueo de escritura). Ambos se mitigan con límites que rechazan
la planilla antes de abrir la transacción y con una transacción corta. Dos cargas simultáneas se serializan en el
bloqueo de escritura de SQLite; cada una tiene su propio tope de memoria (14 MB de cuerpo más el XML acotado). La Api
corre en la PC del local para pocas operadoras, sin exposición a Internet.
