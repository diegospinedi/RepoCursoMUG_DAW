# Decisiones fuera de la spec — FEAT-001a

Formato: decisión — quién la aprobó — fecha — bloque.

"Aceptada por el orquestador" significa: tomada por el implementador, aceptada por las dos revisiones
independientes (ddw-module-verifier y ddw-arch-auditor) en autonomía `minimal` y mostrada al usuario
en la conversación; ninguna persona la aprobó explícitamente todavía.

## Block 1 — Esqueleto de la solución y persistencia

- El `OpticaDbContext` mapea clases propias de Infrastructure (`CredencialAccesoFila`, `EventoAccesoFila`, declaradas en `OpticaDbContext.cs`) porque la entidad de dominio llega en el Block 2; el Block 2 puede mapearlas o reemplazarlas sin cambiar el esquema — aceptada por el orquestador — 2026-10-30 — Block 1
- `Microsoft.EntityFrameworkCore.Design` también en `Optica.Api.csproj` (`PrivateAssets=all`), porque `dotnet ef` lo exige en el proyecto de inicio; su DLL sale en la compilación y el publish de la Api — aceptada por el orquestador — 2026-10-30 — Block 1
- Sin `CHECK` sobre los valores de `EventoAcceso.Tipo` y `EventoAcceso.Resultado`: la spec no lo pide; los valores los controla el código del Block 2 — aceptada por el orquestador — 2026-10-30 — Block 1
- Fechas guardadas como `DateTime.ToString("O")` en UTC; un `DateTime` sin tipo se toma como UTC sin convertirlo, así que el Block 2 debe usar siempre la hora UTC o un reloj inyectable — aceptada por el orquestador — 2026-10-30 — Block 1
- La prueba de directorio inválido ejercita `InicializarBaseDeDatos` (lo mismo que llama `Program.cs`) sin levantar la Api; el arranque completo se cubre con la `FabricaApi` del Block 3 — aceptada por el orquestador — 2026-10-30 — Block 1
- Protección de datos personales en el log de arranque: con el directorio por defecto, mensajes, log y excepción muestran la ruta como `%LOCALAPPDATA%\OpticaSistema` (fuera de Windows, `$XDG_DATA_HOME/…`, `~/…` o un texto genérico) y no adjuntan la excepción original, para no registrar el nombre de usuario de Windows; una ruta configurada se muestra tal cual — aceptada por el orquestador — 2026-10-30 — Block 1
- `FabricaConexion.Configurar` baja `RelationalEventId.ConnectionError` de EF Core a nivel Debug en todos los casos (también con ruta configurada), porque ese evento registra el DataSource completo — aceptada por el orquestador — 2026-10-30 — Block 1
- `InternalsVisibleTo` de Optica.Infrastructure hacia Optica.Infrastructure.Tests, usado solo por un constructor `internal` de `FabricaConexion` para probar la ruta por defecto sin tocar variables de entorno — aceptada por el orquestador — 2026-10-30 — Block 1

### Restricciones y pendientes que deja el Block 1

- Restricción de operación: no subir la categoría de log `Microsoft.EntityFrameworkCore` a Debug en la PC del local; a ese nivel EF Core registra la ruta completa de la base, con el nombre de usuario de Windows. Documentarlo en `docs/instalacion.md` (Block 6).
- Pendiente para el Block 3: como `ConnectionError` quedó en Debug, la Api debe registrar los errores de conexión en los pedidos usando `RutaVisible`.
- Pendiente para el Block 6: las trazas de excepción llevan rutas de compilación (con el usuario del desarrollador) si se publican los PDB; definir `PathMap`/`DeterministicSourcePaths` o no publicar PDB.
- Menor: el aviso de `BaseTemporal` cuando no puede borrar va a stderr y probablemente no se vea en `dotnet test`.

## Block 2 — Dominio y casos de uso de la contraseña

- Normalización Unicode NFC de la contraseña antes de evaluar la política y antes de calcular o verificar el hash, al definir y al ingresar; el largo se cuenta en unidades UTF-16 de la forma NFC (un emoji cuenta como 2) — aprobada por el usuario — 2026-10-31 — Block 2 (ADR-004)
- Al verificar solo se aceptan hashes de 600.000 a 10.000.000 iteraciones; el piso es un literal independiente del costo actual — aprobada por el usuario (piso); tope elegido por el implementador y aceptado por las revisiones — 2026-10-31 — Block 2 (ADR-004)
- Texto UTF-16 mal formado (sustituto suelto) se rechaza en la política como `CaracteresInvalidos`: al definir, `ErrorValidacion` campo `contrasena` con la acción "La contraseña tiene caracteres no válidos. Escribila de nuevo."; al ingresar, `Rechazo` con evento y sin calcular el hash. El hasher, como segunda defensa, nunca lanza con datos de la contraseña — aprobada por el usuario — 2026-10-31 — Block 2 (ADR-004)
- Pretope crudo de 512 unidades (4 × 128): un texto más largo se rechaza como `MuyLarga` sin normalizarlo, para acotar el costo de pedidos sin sesión — aceptada por el orquestador — 2026-10-31 — Block 2 (ADR-004)
- Orden de evaluación de la política: nula → pretope → caracteres inválidos → largo NFC; una contraseña nula cuenta como `MuyCorta` — aceptada por el orquestador — 2026-10-31 — Block 2
- `IngresarConContrasena` con una contraseña fuera de política devuelve `Rechazo`, registra el evento `ingreso`/`rechazo` y no calcula el hash; el 400 por campo faltante de la Api queda para el Block 3 — aceptada por el orquestador — 2026-10-31 — Block 2
- `EventoAcceso.cs` (entidad y enums de tipo y resultado) en Domain, no listado en la spec, porque lo necesita `IRegistroEventosAcceso` — aceptada por el orquestador — 2026-10-31 — Block 2
- `ConflictoAlConfirmarException` en Application: la unidad de trabajo, y no el repositorio como decía la spec del Block 1, traduce a conflicto la clave primaria o única duplicada (1555/2067) y el CHECK `CK_CredencialAcceso_Id` (275, reconocido por el texto del mensaje de SQLite); cualquier otro CHECK se propaga como error — aceptada por el orquestador — 2026-10-31 — Block 2
- Registro en DI dentro de `AgregarInfraestructura` (repositorio, registro de eventos y unidad de trabajo scoped; hasher singleton; `TryAddSingleton(TimeProvider.System)`); los casos de uso no se registran todavía — aceptada por el orquestador — 2026-10-31 — Block 2
- No se prueba la aceptación de exactamente 10.000.000 iteraciones porque tarda más de 1 s; el límite queda cubierto por el rechazo de 10.000.001 sin derivar — aceptada por el orquestador — 2026-10-31 — Block 2

### Restricciones y pendientes que deja el Block 2

- Restricción de operación: no activar nunca `EnableSensitiveDataLogging` ni `EnableDetailedErrors` de EF Core; con eso, el hash de la credencial aparecería en logs y excepciones.
- Pendiente para el Block 3: la Api valida la contraseña con `PoliticaContrasena` antes de delegar, para responder 400 con campo y acción (incluido `CaracteresInvalidos`); no cuenta sobre el texto crudo, porque NFC puede cambiar el largo.
- Pendiente para el Block 3: no registrar el cuerpo de los pedidos de `/api/acceso/definir` ni `/api/acceso/ingresar` (traen la contraseña en claro), ni las excepciones de EF/SQLite tal cual (pueden traer la ruta con el nombre de usuario de Windows).
- Pendiente para el Block 3: `UnidadDeTrabajoEf` limpia todo el `ChangeTracker` del alcance ante una falla; si un pedido rastrea otras entidades en el mismo alcance, una falla las descarta.
- Pendiente para el Block 5: el navegador cuenta sobre `value.normalize('NFC')` y muestra el mensaje de caracteres no válidos si llega en un 400.
