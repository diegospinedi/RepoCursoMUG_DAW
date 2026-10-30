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
