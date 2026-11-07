# SAST FEAT-003a

| Field | Value |
|-------|-------|
| Ticket | FEAT-003a |
| Alcance | `git diff master...e298f0e`: 34 archivos de `src/`, `web/src` y `e2e/` (sin migraciones generadas), más las pruebas .NET |
| Tools | revisión manual del diff con búsquedas de patrones (grep), `npm audit` (web y e2e), `dotnet list Optica.sln package --vulnerable` |
| Date | 2026-11-07 |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos embebidos en el diff; la única contraseña es la de prueba del e2e (`e2e-presupuestos-alta-1`, `e2e/tests/presupuestos-alta.spec.ts:26`), válida solo en la base temporal del harness; `.env` y `.env.*` en `.gitignore` |
| F-SAST-02 | ✅ | todas las consultas por EF Core/LINQ (parametrizadas), incluido `MAX(Numero)` en `PresupuestoRepository`; sin `FromSqlRaw`/`ExecuteSqlRaw` ni SQL interpolado en `src/` |
| F-SAST-03 | ✅ | ninguna entrada llega a `Process.Start` ni a comandos |
| F-SAST-04 | ✅ | sin `eval`, `new Function` ni deserialización insegura; System.Text.Json con tipos concretos y números estrictos |
| F-SAST-05 | ✅ | ninguna entrada del usuario forma rutas de archivo; sin `File.`/`Path.Combine` nuevos en `src/` |
| F-SAST-06 | ✅ | React escapa todo el texto del cliente; sin `dangerouslySetInnerHTML` ni `innerHTML` en `web/src` |
| F-SAST-07 | ✅ | sin pedidos salientes guiados por la entrada (sin `HttpClient` nuevo) |
| F-SAST-08 | ✅ | sin criptografía nueva; la sesión sigue con la cookie cifrada de FEAT-001a |
| F-SAST-09 | ✅ | sin modo debug ni `EnableSensitiveDataLogging`; sin mapas de fuente en la web publicada |
| F-SAST-10 | ✅ | sin registro nuevo de datos del cliente: ningún `ILogger` en los casos de uso, records con `PrintMembers` falso, errores con texto fijo; pruebas `Registro_AltaPresupuesto_SinDatosPersonales` (log en Trace, incluido un cuerpo ilegible) y `Agregar_ComandosRegistrados_SinDatosDelCliente`; en `web/src` no hay `console`, `localStorage` ni `sessionStorage` fuera de las pruebas |
| F-SAST-11 | ✅ | no hay superficie de carga de archivos |
| F-SAST-12 | ✅ | cookie `SameSite=Strict`, sin CORS y JSON obligatorio en el POST (415); sesión obligatoria por la política por defecto (401) |
| F-SAST-13 | ✅ | `npm audit`: 0 vulnerabilidades en web y en e2e; `dotnet list package --vulnerable`: ningún paquete vulnerable en los 8 proyectos |
| F-SAST-14 | ✅ | validación en el borde: cuerpo hasta 16 KB (413), hasta 100 líneas, largos de D-08, DNI y email con formato, `soloActivos` booleano (400), número de ruta `long`; el dominio vuelve a validar |
| F-SAST-15 | ✅ | errores 400 con texto fijo que no repite valores, 404 sin detalles, 500 genérico sin excepción; `DbUpdateException` no se serializa ni se registra completa (D-30) |
| F-SAST-16 | ✅ | sin dependencias nuevas en .NET, web ni e2e |
| F-SAST-17 | ✅ | sin rutas de código dinámico |

## Observaciones (informativas, no bloquean)
- `src/Optica.Infrastructure/Persistencia/UnidadDeTrabajoEf.cs:131-135`: una `DbUpdateException` que no es
  conflicto conserva en `Entries` la fila con los datos del cliente; hoy no llega al log ni a la respuesta
  (D-30, prueba en `PresupuestoRepositoryTests`). Riesgo a revisar si se agrega telemetría o log estructurado.
- La base y los backups guardan los datos personales del cliente sin cifrar: riesgo R-01 aceptado por el
  usuario (D-27), advertido en `docs/instalacion.md`.

## Suppressions
None.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
