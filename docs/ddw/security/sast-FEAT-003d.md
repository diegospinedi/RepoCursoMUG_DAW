# SAST FEAT-003d

| Field | Value |
|-------|-------|
| Ticket | FEAT-003d |
| Alcance | `git diff origin/master..9ce3c3c`: 12 archivos; producción solo `src/Optica.Infrastructure/Persistencia/FabricaConexion.cs` (`DefaultTimeout = 30`); el resto son pruebas (`tests/Compartido/EncuentroDeAltas.cs`, `tests/Compartido/SiembraPresupuestos.cs`, pruebas .NET de concurrencia y volumen) y e2e (`e2e/tests/presupuestos-concurrencia.spec.ts`, `e2e/README.md`) |
| Tools | revisión manual del diff con búsquedas de patrones (grep), `npm audit` (web y e2e), `dotnet list Optica.sln package --vulnerable` |
| Date | 2026-11-12 |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos; la única contraseña es la de prueba del e2e (`e2e-presupuestos-concurrencia-1`), válida solo en la base temporal del harness; `.env`, `.env.*`, `*.db`, `*.db-wal` y `*.db-shm` siguen en `.gitignore` |
| F-SAST-02 | ✅ | sin SQL nuevo en producción; el SQL de `tests/Compartido/SiembraPresupuestos.cs` usa parámetros y `Prepare()` en una sola transacción, sin interpolación de datos; sin `FromSqlRaw`/`ExecuteSqlRaw` |
| F-SAST-03 | ✅ | ninguna entrada llega a `Process.Start` ni a comandos |
| F-SAST-04 | ✅ | sin `eval` ni deserialización insegura; el P/Invoke `[DllImport("e_sqlite3")] sqlite3_busy_handler` de `tests/Compartido/EncuentroDeAltas.cs` existe solo en proyectos de prueba (D-07), con delegado estático y handler que devuelve 0 |
| F-SAST-05 | ✅ | ninguna entrada del usuario forma rutas de archivo |
| F-SAST-06 | ✅ | sin cambios en `web/src`; sin `innerHTML` ni `dangerouslySetInnerHTML` |
| F-SAST-07 | ✅ | sin pedidos salientes nuevos; el e2e solo habla con la Api local y pasa la regla `hostsExternos` de `e2e/ci.mjs` |
| F-SAST-08 | ✅ | sin criptografía nueva |
| F-SAST-09 | ✅ | sin modo debug ni `EnableSensitiveDataLogging` |
| F-SAST-10 | ✅ | sin registro nuevo; los mensajes del sembrador y del encuentro son fijos, sin valores ni excepción como causa; la prueba del duplicado verifica que el apellido no aparezca en la excepción |
| F-SAST-11 | ✅ | no hay superficie de carga de archivos |
| F-SAST-12 | ✅ | sin endpoints nuevos; las dos sesiones del e2e usan la cookie `SameSite=Strict` existente |
| F-SAST-13 | ✅ | `npm audit`: 0 vulnerabilidades en web y en e2e; `dotnet list package --vulnerable`: ningún paquete vulnerable en los 8 proyectos |
| F-SAST-14 | ✅ | sin entrada nueva; el sembrador valida parámetros (1 a 10.000 presupuestos, 0 a n Finales, 1 a 5 líneas) antes de abrir la conexión y valida cada dato con el dominio antes de insertar |
| F-SAST-15 | ✅ | una base ocupada más allá de 30 s sigue terminando en el 500 genérico sin datos del pedido (ADR-013, `ManejadorErroresNoControlados`) |
| F-SAST-16 | ✅ | sin dependencias nuevas: solo enlaces `<Compile Include>` en los `.csproj` de prueba; SQLitePCLRaw llega en forma transitiva |
| F-SAST-17 | ✅ | sin rutas de código dinámico |

## Observaciones (informativas, no bloquean; documentadas en D-07, D-08 y D-10 de `docs/ddw/specs/decisions-FEAT-003d.md`)
- El P/Invoke a `e_sqlite3` vive solo en `tests/Compartido` y no se enlaza en `src/` (D-07).
- Dos ediciones se hicieron por shell en lugar de Edit/Write (una del implementador en un archivo de prueba, una del
  verificador que modificó y restauró `src/` con verificación md5); ninguna quedó en el diff (D-08, D-10).
- Datos de prueba: todos los datos nuevos son ficticios (DNI 99.999.9xx, `@example.com`, teléfonos de ceros). Sigue
  pendiente el reemplazo de datos con formato real de tickets anteriores (D-31 de FEAT-003b, D-18 de FEAT-003c).

## Suppressions
None.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
