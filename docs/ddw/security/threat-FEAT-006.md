# Threat model FEAT-006: Búsqueda y PDF de presupuestos

| Field | Value |
|-------|-------|
| Ticket | FEAT-006 |
| Spec | docs/ddw/specs/spec-FEAT-006.md |
| Tier | FEATURE |
| Date | 2026-11-12 |

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Domain/Presupuestos/FiltroPresupuestos.cs` (con `ResumenPresupuesto.cs` y `PaginaPresupuestos.cs`) | Block 1 |
| `src/Optica.Infrastructure/Persistencia/PresupuestoRepository.cs` | Block 1 |
| `src/Optica.Application/Presupuestos/BuscarPresupuestos.cs` (con `PresupuestosController.cs`, `POST /api/presupuestos/busqueda`) | Block 2 |
| `web/src/paginas/BuscarPresupuestos.tsx` (con `api/cliente.ts` y `presupuestos/reglas.ts`) | Block 3 |
| `src/Optica.Infrastructure/Pdf/GeneradorPdfPresupuesto.cs` (QuestPDF) | Block 4 |
| `src/Optica.Application/Presupuestos/GenerarPdfPresupuesto.cs` (con `PresupuestosController.cs`, `GET /api/presupuestos/{numero}/pdf`) | Block 5 |
| `web/src/paginas/Presupuesto.tsx` (botón Descargar PDF, con `api/cliente.ts`) | Block 6 |

## Trust boundaries
- Navegador de la operadora → API ASP.NET Core: `POST /api/presupuestos/busqueda` lleva Apellido, Nombre y DNI
  (datos personales) y las fechas Desde/Hasta en el cuerpo JSON, nunca en la URL ni en la query string; solo con sesión.
- API → navegador: la respuesta de la búsqueda (hasta 50 resultados con Apellido, Nombre, DNI) y el PDF
  (`application/pdf`, `Content-Disposition: attachment` con solo el número) cruzan con `Cache-Control: no-store`.
- API → archivo SQLite `optica.db`: la búsqueda es una lectura parametrizada con la función `plegar` registrada en la
  conexión (ADR-011); no escribe ni cambia el esquema.
- API → carpeta de la marca (`Optica:Marca:Directorio` o `marca-ejemplo/`): el generador del PDF dibuja el logo (PNG,
  JPEG o SVG) que leyó y validó `ProveedorMarcaArchivo` (FEAT-001a, R-09 de ese ticket).
- Navegador → disco de la PC: el PDF descargado queda en la carpeta de descargas, fuera del control del sistema.

## STRIDE analysis
### `src/Optica.Domain/Presupuestos/FiltroPresupuestos.cs` (con `ResumenPresupuesto.cs` y `PaginaPresupuestos.cs`)
- **Spoofing:** sin identidades; lo construye el caso de uso detrás de la sesión.
- **Tampering:** el filtro solo guarda textos plegados con `PlegadoBusqueda.Plegar`, un DNI sin puntos ni guiones y
  fechas `DateOnly`; rechaza Desde posterior a Hasta (FR-06) con una excepción de texto fijo.
- **Repudiation:** no aplica: es un valor inmutable sin efectos.
- **Information Disclosure:** sus excepciones y su `ToString` no muestran Apellido, Nombre ni DNI (R-02).
- **Denial of Service:** los textos llegan acotados por el tamaño del cuerpo (4 KB) y la página tiene tamaño fijo 50.
- **Elevation of Privilege:** no concede nada: solo restringe lo que devuelve la consulta.

### `src/Optica.Infrastructure/Persistencia/PresupuestoRepository.cs`
- **Spoofing:** sin identidades; la base es local y solo la abre la Api.
- **Tampering:** `BuscarAsync` es de solo lectura (`AsNoTracking`), con parámetros de EF Core; la coincidencia parcial usa
  `instr` y no `LIKE`, así `%` o `_` en el texto buscado no actúan como comodines (R-01).
- **Repudiation:** las lecturas no se registran, como las demás de presupuestos.
- **Information Disclosure:** no registra filtros ni filas en el log; mensajes de error fijos (D-17 de FEAT-003a).
- **Denial of Service:** una página de 50 ordenada por la clave primaria `Numero` y un conteo; con 10.000 presupuestos
  se mide en AC-16 (R-04).
- **Elevation of Privilege:** no hay camino de escritura nuevo; los triggers de cierre siguen intactos (sin migración).

### `src/Optica.Application/Presupuestos/BuscarPresupuestos.cs` (con `PresupuestosController.cs`, `POST /api/presupuestos/busqueda`)
- **Spoofing:** exige sesión por la política por defecto (no está en `RutasAnonimas`); sin sesión, 401 (R-03).
- **Tampering:** cuerpo JSON estricto con `[RequestSizeLimit]` de 4 KB y Content-Type JSON (`LimitesDelCuerpo`); fecha
  inválida, Desde posterior a Hasta o página menor que 1 responden 400 por campo sin repetir el valor.
- **Repudiation:** no registra la búsqueda: el filtro lleva datos personales; la operadora es la única usuaria.
- **Information Disclosure:** POST con los filtros en el cuerpo, no en la URL ni en el log del servidor; la solicitud tiene
  `PrintMembers => false`; la respuesta lleva `no-store` (R-02).
- **Denial of Service:** página fija de 50 y cuerpo acotado; una página fuera de rango devuelve una lista vacía.
- **Elevation of Privilege:** devuelve un record propio de Application, sin tipos del dominio ni de la base.

### `web/src/paginas/BuscarPresupuestos.tsx` (con `api/cliente.ts` y `presupuestos/reglas.ts`)
- **Spoofing:** usa la cookie de sesión del navegador; ante un 401 vuelve a `/ingreso` como las demás pantallas.
- **Tampering:** valida Desde posterior a Hasta antes de llamar a la Api (AC-07); el servidor valida de nuevo.
- **Repudiation:** no aplica en el navegador.
- **Information Disclosure:** los filtros quedan en el estado del componente, no en la URL, `localStorage` ni
  `sessionStorage`; React escapa los textos de los resultados (R-02).
- **Denial of Service:** una búsqueda por pulsación; el botón no envía otra mientras hay una en curso.
- **Elevation of Privilege:** abrir un resultado navega a `/presupuestos/:numero`, que vuelve a pedir el presupuesto con sesión.

### `src/Optica.Infrastructure/Pdf/GeneradorPdfPresupuesto.cs` (QuestPDF)
- **Spoofing:** sin identidades; recibe el presupuesto y la marca ya leídos.
- **Tampering:** los textos del cliente se escriben con la API de texto de QuestPDF, que los codifica como contenido y no
  como sintaxis PDF; los importes son los grabados, formateados con una cultura fija, sin recalcular (R-05).
- **Repudiation:** el PDF lleva el número y la fecha del presupuesto, que en Final no cambian (FEAT-003c).
- **Information Disclosure:** el título del documento PDF lleva solo el número y Author, Subject y Keywords quedan
  vacíos; los campos vacíos del cliente no se imprimen (FR-13); toda excepción de QuestPDF, cuyo mensaje copia el texto
  que no pudo dibujar, se relanza con texto fijo y sin causa (R-10); no se registra el contenido.
- **Denial of Service:** hasta 100 líneas por presupuesto y un logo de 1 MB como máximo (`LogoMarca`); el SVG del logo
  fue validado sin DTD ni entidades externas (R-06).
- **Elevation of Privilege:** el logo viene de la carpeta de instalación que controla la dueña, no de la operadora.

### `src/Optica.Application/Presupuestos/GenerarPdfPresupuesto.cs` (con `PresupuestosController.cs`, `GET /api/presupuestos/{numero}/pdf`)
- **Spoofing:** exige sesión; sin sesión, 401 (R-03).
- **Tampering:** solo genera para un presupuesto en Final; uno en Borrador responde 409 `borrador` sin generar (FR-12,
  AC-12), también ante un pedido directo a la Api (R-07).
- **Repudiation:** no registra la descarga: la respuesta lleva datos personales.
- **Information Disclosure:** `Content-Disposition` con `presupuesto-{numero}.pdf`, sin apellido ni DNI; `no-store`
  (R-02); 404 y 409 con textos fijos.
- **Denial of Service:** generación en memoria acotada por las 100 líneas; una por pedido.
- **Elevation of Privilege:** el número de la ruta es `long`; no hay acceso a archivos por nombre.

### `web/src/paginas/Presupuesto.tsx` (botón Descargar PDF, con `api/cliente.ts`)
- **Spoofing:** la descarga usa `fetch` con la cookie de sesión; un 401 navega a `/ingreso`.
- **Tampering:** en Borrador el botón está deshabilitado; aunque se fuerce, la Api responde 409 (R-07).
- **Repudiation:** no aplica en el navegador.
- **Information Disclosure:** el PDF se entrega como descarga con nombre fijo por número; la URL de objeto se revoca
  después de usarla; el archivo en la PC queda a cargo de la operadora (R-08).
- **Denial of Service:** el botón no lanza otra descarga mientras hay una en curso.
- **Elevation of Privilege:** no ejecuta el contenido del PDF; el navegador lo guarda como archivo.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| Filtros de búsqueda (Apellido, Nombre, DNI) | PII | no se guardan: ni en la base, ni en el log, ni en el almacenamiento del navegador | HTTP local `localhost`, en el cuerpo del POST, `no-store` en la respuesta |
| Datos del cliente en los resultados y en el PDF (Apellido, Nombre, DNI, Domicilio, Email, Teléfono) | PII | SQLite sin cifrar, permisos NTFS del directorio de datos (R-09, aceptado en FEAT-003a); el PDF descargado, en la PC (R-08) | HTTP local `localhost`, `Cache-Control: no-store` |
| Importes de las líneas y total | financial | SQLite en centésimos (ADR-008), inmodificables en Final | HTTP local `localhost` |
| Logo y colores de la marca | public | carpeta de la marca fuera del repositorio (ADR-003) | dentro del proceso de la Api |
| Contraseña de prueba del e2e | credentials | PBKDF2-SHA256 en la base temporal del harness | `localhost` del harness |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | un texto de búsqueda con `%`, `_` o comillas altera la consulta o actúa como comodín | T | M | M | consulta de EF Core parametrizada con `instr(plegar(columna), @texto)`; pruebas con `%` y `_` que no devuelven todo (Block 1) |
| R-02 | los datos personales de la búsqueda o del PDF quedan en la URL, el log, la cache o el almacenamiento del navegador | I | M | H | POST con cuerpo; `PrintMembers => false`; `no-store` probado en `SinCacheTests`; filtros solo en el estado del componente; nombre del PDF solo con el número |
| R-03 | la búsqueda o el PDF responden sin sesión | S | L | H | política por defecto con sesión; `ProteccionTests.RutasNuevas_SinSesion_401` suma las dos rutas |
| R-04 | la búsqueda sobre 10.000 presupuestos supera 2 s o bloquea a otra sesión | D | M | M | página de 50 por la clave primaria, total con `SUM` en SQL; prueba de volumen de AC-16 con `SiembraPresupuestos`; lectura sin transacción de escritura (WAL) |
| R-05 | el PDF muestra importes distintos de los grabados | T | L | H | el generador recibe los importes del presupuesto en Final y solo los formatea; pruebas con PdfPig sobre los textos de AC-14 |
| R-06 | un SVG del logo con referencias externas hace que el servidor pida recursos o consuma memoria | I | L | M | `LogoMarca` rechaza DTD y entidades; el logo lo instala la dueña en su carpeta (ADR-003); tope de 1 MB |
| R-07 | se obtiene el PDF de un presupuesto en Borrador pidiéndolo directo a la Api | E | M | M | el caso de uso responde 409 `borrador` antes de generar; prueba de Api y e2e con pedido directo (AC-12) |
| R-10 | una excepción de QuestPDF lleva el Apellido o el Domicilio al log del manejador de errores | I | M | H | glifos faltantes reemplazados (D-15); try/catch que relanza con texto fijo y sin causa; prueba de Api de que el log del 500 no tiene el texto del cliente |
| R-08 | el PDF descargado con datos personales queda en la PC sin protección | I | M | M | accepted, see below |
| R-09 | quien accede a `optica.db` o a un respaldo lee los datos del cliente en claro | I | L | H | sin cambios en este ticket; accepted, see below |

## Accepted risks
### R-08
- **Accepted by:** las dueñas de la óptica en el PRD 05 de DISC-001 (riesgo "el PDF contiene datos personales del
  cliente"), ratificado por el usuario al elegir implementar ese PRD en FEAT-006 el 2026-11-12
- **Justification:** el PDF existe para entregarlo al cliente; se genera solo con sesión e incluye solo los datos que el
  cliente dio, y el resguardo del archivo en la PC queda a cargo de la operadora, como dice el PRD.
- **Review conditions:** si se agrega el envío automático por email o WhatsApp (fuera de alcance hoy) o si el sistema
  pasa a usarse desde más de una PC.

### R-09
- **Accepted by:** el usuario, dueño del proyecto, al aprobar el modelo de amenazas de FEAT-003a (R-01 de ese ticket)
- **Justification:** el sistema corre en una PC del local con la base fuera del árbol del código y permisos NTFS;
  `docs/instalacion.md` advierte que la base y los backups guardan los datos sin cifrar; este ticket no cambia cómo se
  guardan.
- **Review conditions:** antes de instalar el sistema con datos reales de clientes, o cuando se implemente el backup
  del PRD 09.

## Supply chain
- QuestPDF 2026.9.1 (licencia Community, decisión del usuario del 2026-11-12, ADR-018) en `Optica.Infrastructure`, sin
  dependencias NuGet: trae sus propios binarios nativos (`libQuestPdfSkia`, `libqpdf`) para 8 plataformas en `runtimes/`; versión fija en `packages.lock.json` (modo bloqueado en la CI). La
  licencia exige que la empresa facture menos de USD 1 millón por año; si se supera, hace falta licencia paga.
- PdfPig 0.1.16 (Apache-2.0), solo en `tests/Optica.Infrastructure.Tests` y `tests/Optica.Api.Tests`.
- pdfjs-dist 6.4.299 (Apache-2.0), solo como dependencia de desarrollo de `e2e/`, sin cargar recursos de hosts
  externos (`Pruebas_SinHostsExternos`).
- Las tres versiones tienen más de dos semanas publicadas; el SAST del cierre de CODE corre `dotnet list package
  --vulnerable` y `npm audit`.

## Availability
Los vectores son la búsqueda sin filtros sobre 10.000 presupuestos (R-04), acotada a una página de 50 y medida en
AC-16, y la generación del PDF, acotada a 100 líneas y un logo de 1 MB. Son lecturas: en modo WAL no esperan a las
escrituras de la otra sesión.
