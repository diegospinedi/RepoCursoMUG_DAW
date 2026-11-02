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

## Block 3 — Api de acceso, sesión y protección

- `POST /api/acceso/ingresar` con una contraseña fuera de política (menos de 8, más de 128 o caracteres no válidos) responde 400 y NO registra evento en `EventoAcceso` — aprobada por el usuario — 2026-10-31 — Block 3
- Cerrar sesión no invalida la cookie en el servidor: una cookie copiada antes de salir sigue sirviendo hasta 60 minutos sin uso y se renueva si se usa; el cambio de contraseña de FEAT-001b tampoco cerrará sesiones abiertas. Riesgo aceptado y documentado, sin sello de sesión en la base — aprobada por el usuario — 2026-10-31 — Block 3
- Corrección del orquestador: el pendiente del Block 2 que decía "la Api valida la contraseña con `PoliticaContrasena` antes de delegar" contradecía la spec ("los controllers solo invocan casos de uso") y `AGENTS.md`. La validación se mueve a Application y la Api solo la invoca — 2026-10-31 — Block 3

### Estado al pausar (2026-10-31, histórico)

- Implementación del Block 3 en el working tree, sin commitear; 71 pruebas en verde.
- Revisión de arquitectura, ronda 1: BLOQUEADO por la validación de la contraseña en la capa de presentación (se corrige con lo de arriba). Advertencias a resolver en la ronda 2: datos de la base y de EF en la Api, la sonda `/api/sesion` renueva la sesión (restricción para el Block 5), falta prueba de cuerpo > 4 KB sin Content-Length, errores no de la base pueden registrar rutas con el nombre de usuario de Windows, falta prueba de que la contraseña no llega al log, el log guarda `Request.Path` (no poner datos personales en rutas), Kestrel solo en localhost y `AllowedHosts` para el Block 6, archivos y paquetes fuera de la lista sin registrar.
- Revisión contra la spec, ronda 1: FAIL por un solo motivo, el 400 sin evento de `ingresar`, que contradecía ADR-004 ("al ingresar, rechazo con evento") y R-06. Queda resuelto por la decisión del usuario de arriba (400 sin registrar): al retomar, registrar un ADR nuevo que reemplace ese punto de ADR-004 (sin editar ADR-004 más que su Status) y una prueba que fije "inválida en ingresar → 400 sin evento". Advertencias a resolver en la ronda 2: prueba de la baja a Debug de EF y de `SuppressDiagnosticsCallback`; prueba de la ubicación de las claves de Data Protection; POST sin `Content-Type` aceptados (`salir` 204) y restricción a `application/json` sin prueba; cobertura de ramas de Optica.Api 77 % (< 80 %); `using` sin uso en `Program.cs:7` y `:11`; mensaje "caracteres no válidos" para JSON roto o número; registrar los 8 supuestos del implementador; edición hecha con un script en vez de Edit (desvío de proceso, contenido revisado y correcto).
- Pendientes nuevos para el Block 5: la sesión se renueva con cualquier pedido con cookie (incluido `GET /api/acceso/estado` y `/api/sesion`), así que la interfaz no consulta en forma periódica; distinguir el 401 con cuerpo (contraseña incorrecta) del 401 vacío (sin sesión).
- Pendientes nuevos para el Block 6: configurar `Kestrel:Certificates:Default:Store/Subject` y el endpoint de la red local al instalar (hoy `https://localhost:5001`; el CN `OpticaSistema` no coincide con `localhost`); restringir `AllowedHosts`; no activar `AddHttpLogging` con cuerpos.
- Restricción: no se cargan datos reales antes de FEAT-001b (todavía no hay bloqueo por intentos fallidos, R-03).

### Cierre del Block 3 (rondas 2 y 3, 2026-11-02)

Revisiones de la ronda 3: ddw-module-verifier PASS (0 FAIL, 3 WARN) y ddw-arch-auditor PASS (0 FAIL, 4 WARN). La decisión del usuario sobre el 400 sin evento quedó en ADR-005, que reemplaza el punto de `ingresar` de ADR-004; la línea del Block 2 "`IngresarConContrasena` con una contraseña fuera de política devuelve `Rechazo` y registra el evento" quedó reemplazada por ADR-005.

Supuestos del implementador — aceptados por el orquestador — 2026-11-02 — Block 3:

- `ingresar` valida antes de mirar la credencial: una contraseña fuera de política sin contraseña definida da 400, no 409, y no consulta la base.
- Un cuerpo `application/json` ilegible (JSON roto, número, arreglo, objeto o UTF-8 mal formado) da 400 `errors.contrasena` "No se pudo leer la contraseña enviada. Escribila de nuevo."; la spec decía "Cuerpo que no es JSON — 415", que queda para el `Content-Type` distinto de JSON. Un sustituto suelto escapado (`\ud800`) llega a Application y da "caracteres no válidos" (ADR-004).
- 415 solo en POST, con `application/json` exacto (se admite `charset`; `application/*+json` se rechaza), mediante un único middleware `LimitesDelCuerpo` en lugar de `[Consumes]`. `salir` exige `Content-Type: application/json` aunque no lleve cuerpo (sin `Content-Type` → 415 y no cierra la sesión); el cliente del Block 5 lo tiene que mandar.
- Sin sesión, un POST protegido sin `Content-Type` da 401: la autorización va antes del 415 y del 413.
- 413 para cuerpos de más de 4 KB, con y sin `Content-Length` (la spec no lista el 413; el Block 5 lo tiene que contemplar).
- Un 415 con cookie puede traer el `Set-Cookie` de renovación.
- 401 de contraseña incorrecta con forma ValidationProblemDetails (`errors.contrasena` y `detail`); 409 de `definir`: "Ya hay una contraseña definida. Ingresá con ella."; 403 sin redirección aunque hoy ninguna política lo produce.
- Cookie `OpticaSesion` de sesión del navegador (sin `Expires`); el vencimiento de 60 minutos lo controla el servidor, se renueva en cada pedido autenticado y rechaza en el instante exacto del vencimiento.
- Política de reserva igual a la política por defecto; `RutasAnonimas` compara sin distinguir mayúsculas e incluye las dos rutas de marca del Block 4.
- Data Protection con nombre de aplicación `OpticaSistema`, claves sin cifrar fuera de Windows (solo pruebas) y endpoint Kestrel `https://localhost:5001` en `appsettings.json`.
- El controller falla cerrado: la cookie se emite solo con el resultado `Exito`; cualquier otro valor lanza `InvalidOperationException` y termina en 500 genérico. La traducción del resultado es `internal`, con `InternalsVisibleTo Optica.Api.Tests` para probarla.
- `IDatosLocales` y `FallaDatos` (con el código de error de SQLite, sin texto de la excepción) en Application, aunque los consume la Api y no un caso de uso: es la única ubicación que respeta las dependencias. Lo mismo para `RutasPrivadas` (`Optica.Application/Comun`), usada por la Api y por `FabricaConexion`.
- `RutasPrivadas` reemplaza la carpeta del usuario actual por `%USERPROFILE%` (Windows, `/` y `\` equivalentes, sin distinguir mayúsculas) o `~`, respetando el límite de segmento; no cubre nombres 8.3, barras escapadas ni `DOMINIO\usuario`.
- Errores que no son de la base: se registran tipo, mensaje y traza como texto, con la carpeta del usuario oculta y sin adjuntar la excepción; la ruta del pedido se registra escapada (`ToUriComponent`).
- La baja a Debug de los eventos de error de EF (`ConnectionError`, `CommandError`, `TransactionError`, `SaveChangesFailed`, `QueryIterationFailed`) vive en `FabricaConexion.Configurar` y vale también para `dotnet ef` y las pruebas. `PrepararDirectorio` nunca adjunta la causa original; el arranque registra el código de SQLite sin la `SqliteException`.
- `Microsoft.Hosting.Lifetime` en `Warning` en `appsettings.json`, para no registrar la carpeta raíz del contenido.
- `SolicitudContrasena.ToString()` sin la contraseña: con log en Trace, MVC registraba los argumentos de la acción con la contraseña en claro. Cualquier DTO futuro con datos personales o sensibles necesita lo mismo.
- `PoliticaContrasena.EsValida` y `Normalizar` pasan a `internal` (`InternalsVisibleTo Optica.Domain.Tests`); se quitaron las constantes alias de `DefinirContrasena`.
- Archivos fuera de la lista de la spec: `LectorContrasenaJson`, `LimitesDelCuerpo`, `ManejadorErroresNoControlados`, `ValidacionContrasena`, `IDatosLocales`, `DatosLocales`, `RutasPrivadas`, `Http.cs` y las pruebas nuevas; paquetes de prueba `Microsoft.AspNetCore.Mvc.Testing` y `Microsoft.Extensions.TimeProvider.Testing`.

### Restricciones y pendientes que deja el Block 3

- Pendiente para el Block 6 (datos personales de empleados): las trazas de excepción llevan rutas de compilación con el usuario del desarrollador si se publican los PDB; salen por el manejador de errores, por el `LogCritical` de `DirectorioDatosInvalidoException` (que adjunta la excepción) y por el `throw` del arranque hacia stderr y el Visor de eventos. Definir `PathMap`/`ContinuousIntegrationBuild` o no publicar PDB, y quitar la excepción de ese `LogCritical`.
- Pendiente: en los dos errores de `ResolverDirectorio` (carpeta local no calificada, ruta configurada relativa) la ruta no pasa por `RutasPrivadas`.
- Restricción para los módulos con datos de clientes (presupuestos, facturas): no poner datos personales (nombre, DNI, CUIT) en mensajes de excepción ni en las rutas de la Api; el manejador de errores registra el mensaje y la ruta.
- Menor: el comentario de `RutasPrivadas` justifica mal su ubicación y sus pruebas están en `Optica.Api.Tests`; `PoliticaContrasena.Evaluar(string?)` solo la usan las pruebas; `Microsoft.EntityFrameworkCore.Design` sigue en la Api (Block 1); la prueba de arquitectura revisa referencias de ensamblado y constructores, no todo uso de Infrastructure; `LimitesDelCuerpo` aplica a todo POST (una subida multipart futura necesitará excepción).
- Desvíos de proceso: el implementador borró por shell `src/Optica.Api/Errores/RutasPrivadas.cs` al moverla a Application, y el orquestador cambió un encabezado de este archivo con un script; contenido revisado y correcto.
- Pendiente antes de los módulos con datos de clientes: subir `Microsoft.EntityFrameworkCore` a `Warning` en `appsettings.json`; hoy, en Information, EF registra el SQL de cada comando (sin valores de parámetros).

## Block 4 — Marca instalada y marca de ejemplo

Supuestos del implementador (3 rondas) — aceptados por el orquestador — 2026-11-02 — Block 4:

- Logo: solo `.png`, `.jpg` y `.svg`, exactos y en minúsculas (`.jpeg` y `logo.PNG` usan la marca de ejemplo); el contenido tiene que corresponder al formato (firma PNG o JPEG; SVG como XML bien formado, sin DTD ni DOCTYPE, con raíz `svg` del espacio de nombres SVG). Tope de 1 MB inclusive, como dice la spec ("supera 1 MB"); el modelo de amenazas (R-09) dice "menos de 1 MB", y prevalece la spec.
- `LogoMarca.cs` en Domain (no listado en la spec): la validación de formato y contenido es un invariante del logo para cualquier llamador; usa `System.Xml` en memoria, sin E/S, con `DtdProcessing.Prohibit` y `XmlResolver = null`.
- `branding.json`: hasta 64 KB, JSON estricto (sin comentarios, comas finales ni propiedades repetidas; nombres con distinción de mayúsculas), BOM UTF-8 aceptado, campos extra ignorados.
- `Optica:Marca:Directorio` tiene que ser una ruta absoluta; vacía, relativa, inexistente o anómala → marca de ejemplo con advertencia. Cualquier falla al leer la marca instalada (salvo `OutOfMemoryException` e `InsufficientExecutionStackException`) usa la de ejemplo; el 500 queda solo para la marca de ejemplo ilegible.
- Contra path traversal: del campo `logo` se toma lo que sigue al último `/` o `\`; se rechazan vacío, `.`, `..`, caracteres de control, `<>:"|?*` y los nombres reservados de Windows (CON, PRN, AUX, NUL, CONIN$, CONOUT$, COM/LPT con 0–9 o ¹²³, sin distinguir mayúsculas, tomando lo anterior al primer punto y sin espacios finales); después de `GetFullPath` la carpeta del archivo tiene que ser exactamente la carpeta de marca; se rechazan enlaces simbólicos y puntos de reanálisis en el logo y en `branding.json`.
- La marca se lee una vez y queda en memoria hasta reiniciar la Api; una falla no se guarda y se reintenta en el pedido siguiente. Cambiar la marca exige reiniciar la Api (documentarlo en la instalación, Block 6).
- Log sin rutas: las advertencias llevan un motivo fijo, el tipo de excepción y la clave `Optica:Marca:Directorio`; nunca el valor de la carpeta, valores leídos de los archivos, el mensaje de la excepción ni la excepción adjunta.
- Encabezados del logo: `X-Content-Type-Options: nosniff` siempre; en el SVG, `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; sandbox`, más estricto que el `sandbox` de la spec y de R-10 (defensa en profundidad para navegadores sin soporte de `sandbox`).
- Marca de ejemplo en `AppContext.BaseDirectory/marca-ejemplo` (no configurable), copiada a la salida y a la publicación por `Optica.Api.csproj`; colores #5A5A5A / #F5F5F5 y un logo SVG genérico, sin la marca real.
- `IProveedorMarca` sincrónico, registrado como singleton; `ObtenerMarca` expone `Ejecutar()` → `MarcaVisible` y `ObtenerLogo()` → `LogoVisible`, para que la Api no dependa de Domain.
- Archivos fuera de la lista de la spec: `LogoMarca.cs`, `ConfiguracionInfraestructura.cs`, `Program.cs`, `appsettings.json` (`Optica:Marca:Directorio` vacío) y `tests/Optica.Infrastructure.Tests/Marca/AtributosCondicionales.cs` (omite de forma visible las pruebas de enlaces simbólicos y de archivos con nombres no válidos donde el sistema no los permite). `NombreDeArchivo`, `EsNombreReservadoDeWindows` y `EstaEnLaCarpeta` son `internal` y se prueban directamente en todos los sistemas.

### Restricciones y pendientes que deja el Block 4

- Riesgo aceptado: TOCTOU y enlaces duros en la carpeta de marca (los atributos se revisan sobre la ruta antes de abrir el archivo). Pendiente para el Block 6: permisos NTFS de la carpeta de marca, de solo lectura para la cuenta de la Api y con escritura solo para quien administra.
- `GET /api/marca/logo` es anónimo por diseño: cualquier archivo válido que se deje como logo se sirve sin sesión a la red local; en la carpeta de marca va solo la marca.
- Pendiente para el Block 5 o 6: sin `Cache-Control`/`ETag` en `/api/marca` y `/api/marca/logo`; `nosniff` global en lugar de por endpoint. Si el `<img>` del logo se pide con la cookie, renueva la sesión (pendiente del Block 3).
- El filtro de excepciones críticas de `Cargar` no tiene prueba: la lectura es estática sobre `File`/`FileInfo` y no hay costura para inyectar la falla.
