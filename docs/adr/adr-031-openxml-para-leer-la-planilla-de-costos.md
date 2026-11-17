# ADR-031: DocumentFormat.OpenXml para leer la planilla de costos, enviada como JSON

| Field | Value |
|-------|-------|
| Date | 2026-11-17 |
| Ticket | FEAT-011 |
| Status | Accepted |

## Context
FEAT-011 (`docs/ddw/prd/prd-FEAT-011.md`) actualiza los costos del catálogo desde una planilla .xlsx de hasta
10.000 filas que arma un proveedor. Es el primer archivo de terceros que recibe la Api. Había que elegir con qué leer
el formato (un ZIP con XML) y cómo hacerlo llegar al servidor, que hasta ahora solo aceptaba `application/json`
(`LimitesDelCuerpo`, mitigación de CSRF R-04 de FEAT-001a).

## Options considered

### Option 1: DocumentFormat.OpenXml 3.1.1 (Open XML SDK de Microsoft)
- **Pros:** licencia MIT y mantenido por Microsoft; lectura SAX con `OpenXmlReader` sin cargar la hoja entera;
  `OpenSettings.MaxCharactersInPart` acota la descompresión; no resuelve DTD.
- **Cons:** dependencia nueva de varios MB con `System.IO.Packaging`; API de bajo nivel (cadenas compartidas, tipos de
  celda) que hay que interpretar a mano.

### Option 2: ClosedXML
- **Pros:** API cómoda por celda y fila.
- **Cons:** carga el libro entero en memoria (DOM) y arrastra el SDK de la opción 1 más otras dependencias; peor control
  de memoria ante una planilla armada.

### Option 3: ExcelDataReader
- **Pros:** lectura por filas, liviano, lee también .xls.
- **Cons:** el PRD excluye .xls; menos control de límites de descompresión y de tipos de celda.

### Option 4: Lector propio con `ZipArchive` y `XmlReader`
- **Pros:** sin dependencias.
- **Cons:** reimplementa casos del formato (cadenas en línea, texto enriquecido, orden de hojas, relaciones) con más
  riesgo de leer mal una planilla real.

### Transporte: multipart/form-data contra JSON con el archivo en base64
- **Multipart:** convencional, pero exige una excepción a JSON-only, y un formulario de otro sitio lo envía sin
  preflight; `IFormFile` además escribe a disco lo que pasa de 64 KB.
- **JSON base64:** un 33 % más de bytes (14 MB por 10 MB de archivo), sin excepciones al middleware ni temporales.

## Decision
Se eligió `DocumentFormat.OpenXml` 3.1.1 con la versión exacta, referenciado solo desde `Optica.Infrastructure`, con
lectura SAX de la primera hoja según el orden de `workbook.xml`. Se eligió por el control de memoria y por la
procedencia. El archivo viaja como `{ "planilla": "<base64>" }` en un JSON, porque así la regla JSON-only queda
intacta y el archivo no toca el disco.

## Consequences
- Paquete nuevo en `src/Optica.Infrastructure/Optica.Infrastructure.csproj` y cuatro `packages.lock.json`.
- `ProteccionTests` prueba que la Api no referencia el SDK, como con QuestPDF y QRCoder.
- El cuerpo máximo del endpoint es 14 MB, mayor que el del resto de las rutas.
- El harness e2e arma el .xlsx con `node:zlib` y no suma paquetes npm.
- Actualizar la versión del SDK es un cambio explícito, con su propio lock file.
