# SAST FEAT-011

| Field | Value |
|-------|-------|
| Ticket | FEAT-011 |
| Tools | Revisión estática del diff `690e7f1..HEAD` por ddw-sec-auditor: lectura del código contra §4 del catálogo, el modelo de amenazas R-01 a R-10 y las decisiones D-01 a D-29. Auditoría de dependencias con `npm audit` y `npm audit --omit=dev` en `web/` y `e2e/`, y con `dotnet list Optica.sln package --vulnerable --include-transitive` |
| Date | 2026-11-17 |

## Alcance

El análisis cubre el código de producción del ticket en `src/`, `web/src` y la lista permitida de `e2e/ci.mjs`:
- **Dominio:** `PlanillaCostos`, `Articulo.ConCostoNuevo` y `PoliticaArticulo`.
- **Application:** `ActualizarCostosPorPlanilla`, `CandadoCargaPlanilla` y `FilasPlanillaCostos`.
- **Infrastructure:** `LectorPlanillaCostosXlsx` y `ArticuloRepository.RegistrarCostoActualizado`.
- **Api:** `PlanillaCostosController`, el limitador "planilla" en `Program.cs` y `appsettings.json`.
- **Web:** `ActualizarCostos.tsx` y `cliente.ts`.

Dependencia nueva: `DocumentFormat.OpenXml` 3.1.1 (ADR-031), que trae `DocumentFormat.OpenXml.Framework` 3.1.1 y
`System.IO.Packaging` 8.0.1. Va solo en Infrastructure.

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | Sin credenciales, claves ni CUIT en el diff; `src/Optica.Api/appsettings.json:12` solo suma el nivel de log de la categoría de la planilla |
| F-SAST-02 | ✅ | `RegistrarCostoActualizado` usa EF Core con el change tracker y tokens de versión y estado (`src/Optica.Infrastructure/Persistencia/ArticuloRepository.cs:283`); ningún texto de la planilla se arma como SQL |
| F-SAST-03 | ✅ | El código de producción no lanza procesos; el harness arma el .xlsx con `node:zlib` |
| F-SAST-04 | ✅ | `SpreadsheetDocument.Open` en solo lectura y lectura SAX con `OpenXmlReader` (`src/Optica.Infrastructure/Planillas/LectorPlanillaCostosXlsx.cs:362`); sin `XmlDocument` ni `XmlReader` propios, DTD prohibido por el SDK, con prueba de entidad externa contra un listener local que no recibe ningún pedido; JSON con `JsonNumberHandling.Strict` y sin polimorfismo (`src/Optica.Api/Controllers/PlanillaCostosController.cs:22`) |
| F-SAST-05 | ✅ | Todo en memoria (`MemoryStream` de solo lectura, `LectorPlanillaCostosXlsx.cs:309`); no se extrae nada a disco y los nombres de las entradas no se usan como rutas |
| F-SAST-06 | ✅ | `web/src/paginas/ActualizarCostos.tsx:186` muestra códigos, razones e indicaciones por JSX, sin `dangerouslySetInnerHTML`; el cliente acepta solo razones e indicaciones de la lista cerrada y códigos de hasta 60 caracteres, y hay una prueba con `<img onerror>` y `<script>` |
| F-SAST-07 | ✅ | Sin pedidos salientes; las relaciones externas del paquete no se resuelven |
| F-SAST-08 | ✅ | Sin criptografía nueva |
| F-SAST-09 | ✅ | Sin cambios en el modo de error: `UseExceptionHandler` y HSTS fuera de Development; sin `EnableSensitiveDataLogging` |
| F-SAST-10 | ✅ | El log de la carga lleva solo cantidades y fecha UTC (`src/Optica.Application/Catalogo/ActualizarCostosPorPlanilla.cs:234`); los registros y DTO anulan `PrintMembers` y la clave repetida del catálogo lanza sin valor (`TryAdd`). Riesgo residual Bajo en H-02 |
| F-SAST-11 | ✅ | Cuerpo de hasta 14 MiB con 413 (`PlanillaCostosController.cs:135`); largo decodificado medido antes de decodificar, hasta 10 MiB. El tipo se decide por el contenido: firma ZIP, EOCD/ZIP64 y paquete OpenXml con libro. Topes de descompresión y de marcado (D-20); sin disco y sin ejecutar macros. Limitador de 1 carga en curso y 1 en fila antes de leer el cuerpo (D-25) |
| F-SAST-12 | ✅ | JSON-only intacto: `src/Optica.Api/Acceso/LimitesDelCuerpo.cs:21` responde 415 a multipart, urlencoded y text/plain, sin excepciones por endpoint; cookie `SameSite=Strict` y sin CORS; pruebas de 415 y de 401 sin sesión |
| F-SAST-13 | ✅ | Dependencias que se publican sin CVE: `dotnet list … --vulnerable --include-transitive` sin paquetes vulnerables (incluidos OpenXml 3.1.1 y System.IO.Packaging 8.0.1); `npm audit --omit=dev` en `web/` 0; `npm audit` en `e2e/` 0. Herramientas de desarrollo de `web/` (`web/package-lock.json`, picomatch 4.0.7 vía Vite y Vitest): aviso Alto GHSA-fc5q-39xc-vjpf sin versión corregida, que no viaja en la web publicada; riesgo aceptado por el usuario en D-28 |
| F-SAST-14 | ✅ | Planilla faltante, base64 inválido, más de 10 MB, formato y límites validados en el borde (`PlanillaCostosController.cs:200`); topes del lector (`LectorPlanillaCostosXlsx.cs:57`); código y costo validados por el dominio (`PoliticaArticulo.EsCodigoAdmisible`, `Articulo.ConCostoNuevo`) |
| F-SAST-15 | ✅ | ProblemDetails con textos fijos que no repiten lo recibido (`PlanillaCostosController.cs:152`); excepciones del ZIP, del XML y del SDK traducidas sin mensaje ni causa (`LectorPlanillaCostosXlsx.cs:184`); 409 sin nombrar el artículo. Riesgo residual Bajo en H-02 |
| F-SAST-16 | ✅ | Sin CVE Medium en las auditorías |
| F-SAST-17 | ⚠️ | Low, residual: no hay `eval` ni ejecución de macros, y la conversión a base64 va por trozos. Queda H-02 (`LectorPlanillaCostosXlsx.cs:215`): un tipo de excepción del SDK que no esté en la lista traducida llegaría al manejador global con su mensaje. Documentado en D-29 |

## Hallazgos

- **H-01: dependencia de desarrollo, aceptada (D-28).**
  - **Dónde:** `web/package-lock.json:1`, `picomatch` 4.0.7, que traen Vite, Vitest, tinyglobby y fdir.
  - **Qué es:** el aviso GHSA-fc5q-39xc-vjpf, un ReDoS de severidad Alta según el aviso.
  - **Por qué no se corrige:** no hay versión corregida, y la única salida de npm es bajar Vitest a 3.1.4, un cambio
    incompatible.
  - **Por qué no afecta lo publicado:** solo está en dependencias de desarrollo (`--omit=dev` da 0), no viaja en el
    build y los patrones los escribe el proyecto.
  - **Origen:** el ticket no cambió los paquetes de `web/`; el aviso apareció después de FEAT-010.
- **H-02: Low, residual (D-29).**
  - **Dónde:** `src/Optica.Infrastructure/Planillas/LectorPlanillaCostosXlsx.cs:215` y
    `src/Optica.Api/Errores/ManejadorErroresNoControlados.cs:63`.
  - **Qué pasa:** `EsFallaDelArchivo` traduce una lista cerrada de excepciones. Si el SDK lanzara otro tipo con datos de
    una celda en el mensaje, el manejador global lo registraría.
  - **Probabilidad:** no se encontró ningún camino concreto.
  - **Corrección propuesta:** sumar NotSupportedException y OverflowException a la lista, o registrar solo el tipo en
    esa ruta.
  - **Alerta de datos de proveedores:** un código o un precio de proveedor podría quedar en el Registro de eventos.
- **H-03: Info (D-29).** `src/Optica.Application/Catalogo/ILectorPlanillaCostos.cs:18`: la lectura no recibe el token
  de cancelación. Está acotada por el limitador, el candado y los topes.
- **H-04: Info (D-29).** `src/Optica.Application/Catalogo/ActualizarCostosPorPlanilla.cs:234`: el log dice cuándo y
  cuánto, pero no quién, porque el sistema no tiene usuarios (fuera de alcance según AGENTS.md).

### Datos comerciales

Los códigos y los costos de los proveedores son datos comerciales de la empresa:
- Se devuelven solo a la sesión autenticada, con `no-store`.
- No van al log ni al almacenamiento del navegador.
- Quedan sin cifrar en la base SQLite local, igual que el resto del catálogo.

## Suppressions
None.

Total: 16 clean, 1 warning (Low, residual), 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
