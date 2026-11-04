# SAST FEAT-001c

| Field | Value |
|-------|-------|
| Ticket | FEAT-001c |
| Tools | revisión del código por el orquestador (git grep de patrones por categoría sobre los 36 archivos de `src/`, `web/src/` y `e2e/` que cambiaron en la rama, sin los `.Designer.cs` de migraciones); `npm audit --omit=dev` en `web/` y `npm audit` en `e2e/`; `dotnet list Optica.sln package --vulnerable --include-transitive`; revisiones independientes por bloque (ddw-module-verifier y ddw-arch-auditor, 6 bloques) |
| Date | 2026-11-04 |
| Alcance | rama `feat/FEAT-001c-parametros-negocio` completa, desde `master` hasta `cd19ad8`: dominio de configuración, tabla `ConfiguracionNegocio` y su migración con valores iniciales, casos de uso de consultar y grabar, `GET` y `POST /api/configuracion`, pantalla `/configuracion` y harness e2e. Dependencia nueva: `Microsoft.Extensions.Logging.Abstractions` 10.0.12 en `Optica.Application`. |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos agregados: ninguna asignación literal de contraseña, token ni cadena de conexión en el diff; la contraseña de `e2e/tests/configuracion.spec.ts` es una constante sintética para una base temporal; `.env`, `.env.*`, `*.db`, `*.db-wal` y `*.db-shm` siguen en `.gitignore` |
| F-SAST-02 | ✅ | `ConfiguracionNegocioRepository.cs` usa solo LINQ de EF Core (`SingleOrDefaultAsync` por `Id == 1`); no hay `FromSql`/`ExecuteSql` en producción; la migración usa `InsertData` con valores constantes; el SQL de las pruebas de infraestructura interpola solo constantes propias |
| F-SAST-03 | ✅ | la Api no lanza procesos; `servidor.mjs` (harness de pruebas, no se publica) lanza `dotnet` con argumentos fijos y nombres de instancia validados por `leerInstancias` (`/^[a-z]+(?:-[a-z]+)*$/`) |
| F-SAST-04 | ✅ | sin `eval` ni `new Function`; los dos `.exec(` del diff son `RegExp.exec` (`web/src/configuracion/numeros.ts:20`, `web/src/App.test.tsx:194`); System.Text.Json sobre un record con tipos fijos, sin deserialización polimórfica |
| F-SAST-05 | ✅ | ninguna ruta de archivo deriva de la entrada; el único `Path.Combine` tocado (`ConfiguracionInfraestructura.cs:51`) ya existía y usa `AppContext.BaseDirectory`; los directorios de datos del harness salen de nombres validados |
| F-SAST-06 | ✅ | sin `dangerouslySetInnerHTML` ni `innerHTML`; React escapa valores y mensajes; sin estilos en línea (CSP de la SPA intacta) |
| F-SAST-07 | ✅ | el cliente web hace `fetch` solo a rutas fijas del mismo origen (`web/src/api/cliente.ts:144`); la Api no hace pedidos salientes |
| F-SAST-08 | ✅ | sin criptografía nueva; la sesión y el hash de contraseña de FEAT-001a/b no cambian |
| F-SAST-09 | ✅ | sin modo de depuración agregado; `appsettings.json` solo suma `Logging:EventLog:LogLevel:Optica.Application.Configuracion = Information` |
| F-SAST-10 | ✅ | el registro de `GrabarConfiguracionNegocio.cs:63` lleva solo los cuatro parámetros de negocio y la fecha UTC, con marcadores estructurados; no registra cuerpos de pedidos ni datos personales; la excepción de fila corrupta adjunta una causa de dominio cuyo mensaje nombra el campo y nunca el valor |
| F-SAST-11 | ✅ | no hay superficie de carga de archivos |
| F-SAST-12 | ✅ | `POST /api/configuracion` exige sesión (política por defecto, sin `[AllowAnonymous]`), cookie `SameSite=Strict` y `Content-Type: application/json` por `LimitesDelCuerpo` (415); sin CORS |
| F-SAST-13 | ✅ | `npm audit`: 0 vulnerabilidades en `web/` y en `e2e/`; `dotnet list package --vulnerable --include-transitive`: ninguno de los 8 proyectos tiene paquetes vulnerables |
| F-SAST-14 | ✅ | validación en el borde y en el dominio: `[RequestSizeLimit(1024)]` (413), números JSON estrictos (`ConfiguracionController.cs:15`), condición como texto con comparación exacta, `PoliticaConfiguracionNegocio` con rango, escala y máximo del tipo, CHECK en la tabla; el conversor a centésimos rechaza más de 2 decimales |
| F-SAST-15 | ✅ | cuerpo ilegible: 400 con un texto fijo, sin mensajes del lector de JSON ni valores; sin fila: 500 genérico por `ManejadorErroresNoControlados`, sin traza; los textos de error no repiten lo enviado |
| F-SAST-16 | ✅ | dependencia nueva `Microsoft.Extensions.Logging.Abstractions` 10.0.12 (Microsoft, NuGet oficial, misma versión que el resto del framework), sin CVE conocidas según `dotnet list package --vulnerable` |
| F-SAST-17 | ✅ | sin rutas de código dinámicas ni reflexión sobre la entrada |

## Hallazgos informativos (no bloquean; documentados)
- `FabricaConexion` no fuerza `PRAGMA ignore_check_constraints = OFF` al abrir una conexión. No hay código que lo active en producción; es defensa en profundidad para la segunda barrera de los CHECK (observación del implementador de Block 2).
- D-09, decidido por el usuario: la pantalla acepta coma o punto decimal, así que "350.000" en el tope se graba como 350 y "1.500" en el múltiplo como 1,5. No es una vulnerabilidad: es un riesgo de carga aceptado.

## Suppressions
None.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
