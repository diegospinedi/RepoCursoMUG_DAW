# SAST FEAT-006

| Field | Value |
|-------|-------|
| Ticket | FEAT-006 |
| Tools | revisión del diff contra `origin/master` (31 archivos de `src/`, `web/src/` y `e2e/tests/`) con grep de patrones; `dotnet list Optica.sln package --vulnerable --include-transitive`; `npm audit` en `web/` y en `e2e/` |
| Date | 2026-11-13 |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos embebidos en el diff; `.env` está en `.gitignore`; la marca real (`/Marca/`) sigue ignorada y sin archivos versionados; la licencia Community de QuestPDF no es una clave |
| F-SAST-02 | ✅ | la búsqueda es LINQ de EF Core parametrizado: `instr(plegar(...), @p)` y `instr(Dni, @p)`, sin `LIKE`, sin `FromSql` ni SQL concatenado (`PresupuestoRepository.BuscarAsync`); prueba de que `%` y `_` viajan como parámetros |
| F-SAST-03 | ✅ | ningún dato llega a `Process.Start` ni a un shell |
| F-SAST-04 | ✅ | sin `eval`, `new Function` ni deserialización insegura; el cuerpo JSON se lee con `JsonNumberHandling.Strict` y tipos cerrados |
| F-SAST-05 | ✅ | ninguna ruta de archivo se arma con datos del pedido; el generador recibe el logo ya leído por `ProveedorMarcaArchivo` (FEAT-001a, R-09); `Content-Disposition` usa solo el número (`long`) |
| F-SAST-06 | ✅ | la web muestra los resultados con React (texto escapado), sin `innerHTML` ni `dangerouslySetInnerHTML`; los textos del cliente en el PDF van por la API de texto de QuestPDF, no como sintaxis PDF |
| F-SAST-07 | ✅ | no hay pedidos salientes nuevos; el SVG del logo con `href` a `file:` o `http:` no se sigue (QuestPDF 2026.9.1, `Pdf_LogoSvgConReferenciaExterna_NoLaSigue`, D-35) |
| F-SAST-08 | ✅ | el ticket no agrega criptografía |
| F-SAST-09 | ✅ | sin cambios de configuración de depuración; la publicación sigue en Release (`docs/instalacion.md`) |
| F-SAST-10 | ✅ | sin logging nuevo de datos personales: solicitudes y resultados con `PrintMembers => false`, caso de uso y generador sin registrador; pruebas en nivel Trace confirman que Apellido, Nombre y DNI no llegan al log en la búsqueda ni en el 500 del PDF (R-10); la web no usa `console` ni almacenamiento del navegador |
| F-SAST-11 | ✅ | no hay superficie de carga de archivos |
| F-SAST-12 | ✅ | la búsqueda (POST) usa la misma sesión por cookie y el mismo control de Content-Type JSON (`LimitesDelCuerpo`) que el resto de la Api (ADR-001); el PDF es un GET de solo lectura |
| F-SAST-13 | ✅ | `dotnet list package --vulnerable --include-transitive`: ningún proyecto con paquetes vulnerables; `npm audit` en `web/` y `e2e/`: 0 vulnerabilidades |
| F-SAST-14 | ✅ | validación en el borde: fechas `yyyy-MM-dd` reales, Desde ≤ Hasta, página 1..1.000.000, Apellido y Nombre ≤ 100, DNI ≤ 20, UTF-16 válido, cuerpo ≤ 4 KB (`ValidacionPresupuesto.ValidarBusqueda`, `[RequestSizeLimit]`); el número del PDF es `long` por la ruta |
| F-SAST-15 | ✅ | errores con textos fijos sin valores; toda falla de QuestPDF se relanza sin la excepción original (su mensaje copia el texto del cliente) y la Api responde el 500 genérico; 404 y 409 con títulos fijos |
| F-SAST-16 | ✅ | dependencias nuevas sin CVE conocidos: QuestPDF 2026.9.1 (servidor), PdfPig 0.1.16 (solo pruebas), pdfjs-dist 6.4.299 (solo e2e, con la opcional @napi-rs/canvas); versiones fijas en los lock files y publicadas hace más de dos semanas |
| F-SAST-17 | ✅ | sin rutas de código dinámicas; el generador es un singleton sin estado con configuración global fijada una vez |

## Suppressions
None.

## Observaciones
- Datos personales (Ley 25.326): la respuesta de la búsqueda y el PDF llevan datos del cliente por diseño (FR-09,
  FR-13), siempre con sesión y `no-store`. El PDF descargado queda en la carpeta Descargas de la PC (R-08, aceptado
  por las dueñas; aviso en `docs/instalacion.md`, D-38), y la base sigue sin cifrar (R-09, aceptado en FEAT-003a).
- `autoComplete="off"` en los filtros de la búsqueda es una mitigación de máximo esfuerzo (D-31).

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
