# SAST FEAT-002a

| Field | Value |
|-------|-------|
| Ticket | FEAT-002a |
| Tools | revisión del código por el orquestador (git grep de patrones por categoría sobre los 34 archivos de `src/`, `web/src/` y `e2e/` que cambiaron en la rama, sin los `.Designer.cs` de migraciones); `npm audit --omit=dev` en `web/` y `npm audit` en `e2e/`; `dotnet list Optica.sln package --vulnerable --include-transitive`; revisiones independientes por bloque (ddw-module-verifier y ddw-arch-auditor, 6 bloques, con mutaciones y mediciones) |
| Date | 2026-11-04 |
| Alcance | rama `feat/FEAT-002a-alta-precio-venta` desde `master` hasta `2fcbdba`: dominio del catálogo (calculadora del precio de venta, política del artículo, clave normalizada), tabla `Articulo` y migración `Catalogo`, casos de uso de alta y consulta, `POST /api/articulos` y `GET /api/articulos/{codigo}`, pantalla `/articulos/nuevo` y e2e. Sin dependencias nuevas (ningún `.csproj` ni `package.json` cambió). |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos agregados; la contraseña de `e2e/tests/articulos.spec.ts` es una constante sintética para una base temporal; `.env`, `*.db`, `*.db-wal` y `*.db-shm` siguen en `.gitignore` |
| F-SAST-02 | ✅ | `ArticuloRepository.cs` usa solo LINQ de EF Core (`AnyAsync`, `SingleOrDefaultAsync`) con parámetros; sin `FromSql`/`ExecuteSql` en producción; el SQL de las pruebas usa parámetros o constantes del propio test |
| F-SAST-03 | ✅ | la Api no lanza procesos; el harness e2e no cambió en su manejo de procesos |
| F-SAST-04 | ✅ | sin `eval` ni `new Function`; System.Text.Json sobre un record con tipos fijos y números estrictos (`ArticulosController.cs`), sin deserialización polimórfica |
| F-SAST-05 | ✅ | ninguna ruta de archivo deriva de la entrada |
| F-SAST-06 | ✅ | sin `dangerouslySetInnerHTML`, `innerHTML` ni estilos en línea; React escapa descripción, códigos y advertencia; CSP de la SPA intacta |
| F-SAST-07 | ✅ | el cliente web hace `fetch` solo a rutas fijas del mismo origen; la Api no hace pedidos salientes |
| F-SAST-08 | ✅ | sin criptografía nueva |
| F-SAST-09 | ✅ | sin modo de depuración ni cambios de configuración de la Api |
| F-SAST-10 | ✅ | el alta y la consulta no registran nada en el log; los errores de fila corrupta no incluyen valores (probado con aserciones sobre `error.ToString()`); `ConflictoAlConfirmarException` sigue sin causa adjunta |
| F-SAST-11 | ✅ | no hay superficie de carga de archivos |
| F-SAST-12 | ✅ | `POST /api/articulos` exige sesión (política por defecto, sin `[AllowAnonymous]`), cookie `SameSite=Strict` y `Content-Type: application/json` (415); sin CORS; `ProteccionTests` pide también la ruta con parámetro |
| F-SAST-13 | ✅ | `npm audit`: 0 vulnerabilidades en `web/` y en `e2e/`; `dotnet list package --vulnerable --include-transitive`: ninguno de los 8 proyectos tiene paquetes vulnerables |
| F-SAST-14 | ✅ | validación en el borde y en el dominio: `[RequestSizeLimit(1024)]` (413, borde exacto probado), números estrictos, `PoliticaArticulo` (largos tras recortar, UTF-16 inválido, rango y escala), `PrecioVentaFueraDeRango` en lugar de desborde, CHECK de largos y margen e índice UNIQUE en la tabla |
| F-SAST-15 | ✅ | cuerpo ilegible: 400 con texto fijo; artículo inexistente: 404 sin detalles; duplicado: texto fijo que no revela el otro artículo; errores no controlados como 500 genérico sin traza |
| F-SAST-16 | ✅ | sin dependencias nuevas |
| F-SAST-17 | ✅ | sin rutas de código dinámicas ni reflexión sobre la entrada |

## Hallazgos informativos (no bloquean; documentados)
- Costos, márgenes y códigos de proveedor son información comercial confidencial: viajan solo con sesión y TLS (R-01 del modelo de amenazas) y no se registran.
- D-22: entre 2^46 e `IMPORTE_MAXIMO_SEGURO` un `number` de JavaScript no distingue todos los centavos; es una limitación de exactitud, no de seguridad.

## Suppressions
None.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
