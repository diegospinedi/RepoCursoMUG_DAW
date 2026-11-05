# SAST FEAT-002b

| Field | Value |
|-------|-------|
| Ticket | FEAT-002b |
| Tools | revisión del código por el orquestador (git grep de patrones por categoría sobre los 40 archivos de `src/`, `web/src/` y `e2e/` que cambiaron en la rama, sin los `.Designer.cs` de migraciones); `npm audit --omit=dev` en `web/` y `npm audit` en `e2e/`; `dotnet list Optica.sln package --vulnerable --include-transitive`; revisiones independientes por bloque (ddw-module-verifier y ddw-arch-auditor, 6 bloques, con mutaciones y sondeos del log en Trace) |
| Date | 2026-11-05 |
| Alcance | rama `feat/FEAT-002b-busqueda-edicion-baja` desde `master` hasta `7b08dde`: estado, versión y modificación del artículo; plegado de búsqueda y funciones SQLite registradas (ADR-011); migración `CatalogoActivo` (ADR-012); casos de uso de búsqueda, modificación y cambio de estado; `GET /api/articulos`, `PUT /api/articulos/{codigo}` y `PUT /api/articulos/{codigo}/estado`; pantallas `/articulos` y `/articulos/:codigo`; e2e y la configuración de Edge (D-30). Sin dependencias nuevas (ningún `.csproj` ni `package.json` cambió). |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos agregados; la contraseña de `e2e/tests/articulos-edicion.spec.ts` es una constante sintética para una base temporal; `.env`, `*.db`, `*.db-wal` y `*.db-shm` siguen en `.gitignore` |
| F-SAST-02 | ✅ | `ArticuloRepository.cs` usa LINQ de EF Core con parámetros; la búsqueda traduce a `instr(plegar(...), @p)` sin `LIKE` y sin concatenar el texto (prueba `BuscarAsync_Consulta_InstrConPlegarYParametro`); el único SQL crudo es el DDL fijo de la migración `CatalogoActivo` (`20261105104610_CatalogoActivo.cs:25`), sin datos de entrada |
| F-SAST-03 | ✅ | la Api no lanza procesos; el harness e2e no cambió en su manejo de procesos |
| F-SAST-04 | ✅ | sin `eval`, `new Function` ni deserialización polimórfica; System.Text.Json sobre records con tipos fijos y números estrictos |
| F-SAST-05 | ✅ | ninguna ruta de archivo deriva de la entrada |
| F-SAST-06 | ✅ | sin `dangerouslySetInnerHTML` ni `innerHTML`; React escapa descripciones y códigos; los 409 muestran textos propios y nunca el `detail` del servidor; CSP de la SPA intacta |
| F-SAST-07 | ✅ | el cliente web hace `fetch` solo a rutas fijas del mismo origen; la Api no hace pedidos salientes; Edge del e2e solo resuelve `localhost` (D-30) |
| F-SAST-08 | ✅ | sin criptografía nueva |
| F-SAST-09 | ✅ | sin modo de depuración; `Program.cs` fija en código el piso `Warning` para `Microsoft.AspNetCore.Hosting.Diagnostics` y `Microsoft.AspNetCore.Mvc.Infrastructure` (D-28) |
| F-SAST-10 | ✅ | los records de solicitud y `DatosArticulo` no imprimen sus miembros (`PrintMembers` falso), así costo, margen y descripción no llegan al log ni en Trace; el texto buscado no se registra (pruebas de log con todos los niveles); `ConflictoAlConfirmarException` sigue sin causa |
| F-SAST-11 | ✅ | no hay superficie de carga de archivos |
| F-SAST-12 | ✅ | las tres rutas nuevas exigen sesión (sin `[AllowAnonymous]`, `RutasAnonimas` con 5 rutas); cookie `SameSite=Strict`, sin CORS y JSON obligatorio también en PUT (415, `LimitesDelCuerpo.cs`), con prueba de un PUT sin cuerpo |
| F-SAST-13 | ✅ | `npm audit`: 0 vulnerabilidades en `web/` y en `e2e/`; `dotnet list package --vulnerable --include-transitive`: ninguno de los 8 proyectos tiene paquetes vulnerables |
| F-SAST-14 | ✅ | validación en el borde y en el dominio: texto de búsqueda hasta 200 caracteres y UTF-16 válido, página de 1 a 10.000 con desplazamiento en `long`, `version` de 0 a 2^53 − 1, `activo` obligatorio, `RequestSizeLimit` de 1024 y 64 bytes (413), números estrictos, CHECK de `Activo` y `Version`, tokens de concurrencia `Version` y `Activo` |
| F-SAST-15 | ✅ | 400 con textos fijos sin el valor recibido; 404 sin detalles; 409 con `title` fijo, `detail` de un conjunto cerrado y `motivo`; un error de enlace fuera de `pagina` y cualquier resultado no previsto responden 500 genérico sin traza |
| F-SAST-16 | ✅ | sin dependencias nuevas |
| F-SAST-17 | ✅ | sin rutas de código dinámicas ni reflexión sobre la entrada; las funciones `plegar` y `orden_articulo` son deterministas, totales y no forman parte del esquema (prueba con conexión cruda) |

## Hallazgos informativos (no bloquean; documentados)
- Costos, márgenes y códigos de proveedor son información comercial confidencial: viajan solo con sesión y TLS, el listado no los incluye y no se registran (R-01, D-12, D-28).
- Riesgo residual aceptado en D-28: una regla de log propia de un proveedor (`Logging:<Proveedor>:LogLevel:...`) puede vencer el piso y registrar el texto buscado; nunca costo ni margen. Documentado en `docs/instalacion.md` §8.
- D-30: el Edge del e2e iniciaba sesión con la cuenta del equipo y transmitía datos de la identidad del empleado y de la organización a servicios de Microsoft; la configuración de los proyectos `msedge-*` corta ese tráfico.

## Suppressions
None.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
