# SAST FEAT-003c

| Field | Value |
|-------|-------|
| Ticket | FEAT-003c |
| Alcance | `git diff 552bf59..558d5ab`: 54 archivos de `src/`, `web/src`, `e2e/` y pruebas .NET, incluida la migración `CierrePresupuestos` (triggers y columna `Version`) |
| Tools | revisión manual del diff con búsquedas de patrones (grep), `npm audit` (web y e2e), `dotnet list Optica.sln package --vulnerable` |
| Date | 2026-11-12 |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos embebidos en el diff; la única contraseña es la de prueba del e2e (`e2e-presupuestos-edicion-1`, `e2e/tests/presupuestos-edicion.spec.ts:24`), válida solo en la base temporal del harness; `.env`, `.env.*`, `*.db`, `*.db-wal` y `*.db-shm` en `.gitignore` |
| F-SAST-02 | ✅ | la modificación y el paso a Final van por EF Core (parametrizadas, `Actualizar` y `CambiarEstado` en `PresupuestoRepository.cs`); sin `FromSqlRaw`/`ExecuteSqlRaw`; los `migrationBuilder.Sql` de `20261111220704_CierrePresupuestos.cs` son DDL constante (triggers y `DROP`), sin datos de entrada |
| F-SAST-03 | ✅ | ninguna entrada llega a `Process.Start` ni a comandos |
| F-SAST-04 | ✅ | sin `eval`, `new Function` ni deserialización insegura; System.Text.Json con tipos concretos para los dos PUT |
| F-SAST-05 | ✅ | ninguna entrada del usuario forma rutas de archivo; sin `File.`/`Path.Combine` en el diff |
| F-SAST-06 | ✅ | React escapa todo el texto; sin `dangerouslySetInnerHTML` ni `innerHTML` en `web/src`; la pantalla nunca muestra el texto del servidor (D-07) |
| F-SAST-07 | ✅ | sin pedidos salientes guiados por la entrada; el `fetch` del e2e (AC-07/AC-08) va solo a la Api local del harness |
| F-SAST-08 | ✅ | sin criptografía nueva |
| F-SAST-09 | ✅ | sin modo debug ni `EnableSensitiveDataLogging` nuevos |
| F-SAST-10 | ✅ | sin `ILogger` ni `console` nuevos; la `DbUpdateException` del trigger se registra sin `Entries` ni datos del cliente (D-12, D-15, prueba en `tests/Optica.Api.Tests/RegistroTests.cs`); datos personales solo en el estado del formulario, nada en la URL, `localStorage` ni `sessionStorage` (pruebas de `EditarPresupuesto.test.tsx`) |
| F-SAST-11 | ✅ | no hay superficie de carga de archivos |
| F-SAST-12 | ✅ | los dos PUT nuevos (`PresupuestosController.cs:162`, `:183`) exigen sesión (rutas en `ProteccionTests.cs:95`), cookie `SameSite=Strict`, sin CORS y JSON obligatorio (415, pruebas de `ProteccionTests.cs:268`) |
| F-SAST-13 | ✅ | `npm audit`: 0 vulnerabilidades en web y en e2e; `dotnet list package --vulnerable`: ningún paquete vulnerable en los 8 proyectos |
| F-SAST-14 | ✅ | validación en el borde: cuerpo hasta 16 KB en los dos PUT (`RequestSizeLimit`, 413), `version` de 0 a 2^53−1, `orden` sintáctico antes de la transacción y semántico después (D-03, D-10), `estado` solo "final"; el dominio vuelve a validar y la base rechaza con triggers cualquier cambio a un Final, a la fecha o al número (D-04, D-06, D-08); concurrencia optimista con 409 `cambiado` (D-01) |
| F-SAST-15 | ✅ | 409 con `title` y `detail` fijos (`final`, `cambiado`), 400 por campo con texto fijo que no repite valores; la falla del trigger de fecha y número termina en 500 genérico sin la excepción (D-15) |
| F-SAST-16 | ✅ | sin dependencias nuevas en .NET, web ni e2e (sin cambios en `*.csproj` ni `package*.json`) |
| F-SAST-17 | ✅ | sin rutas de código dinámico |

## Observaciones (informativas, no bloquean; documentadas en D-18 de `docs/ddw/specs/decisions-FEAT-003c.md`)
- Datos de prueba: las pruebas nuevas usan valores ficticios (DNI 99.999.999, `cliente.edicion@example.com`,
  teléfono `00 0000-0000`). Sigue pendiente el reemplazo de los datos con formato real de pruebas anteriores
  y el `Arca__Entorno=Simulado` explícito en `e2e/servidor.mjs` (D-31 de FEAT-003b, ticket aparte).
- `Down` de `CierrePresupuestos` quita los triggers de inmutabilidad; es solo para desarrollo (D-10) y no debe
  correrse sobre una base con datos reales.

## Suppressions
None.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
