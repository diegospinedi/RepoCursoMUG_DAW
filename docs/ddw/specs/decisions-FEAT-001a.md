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

## Block 5 — Interfaz web: definir contraseña, ingresar, sesión y marca

Supuestos del implementador (3 rondas) — aceptados por el orquestador — 2026-11-02 — Block 5:

- Archivos fuera de la lista de la spec: `src/acceso/politicaContrasena.ts` (regla de largo, espejo de la del servidor), `src/componentes/FormularioContrasena.tsx` (formulario compartido por definir e ingresar), `src/pruebas/servidorFalso.tsx` y `src/pruebas/formulario.ts` (ayudas de prueba, excluidas de la cobertura junto con `src/main.tsx`), `src/App.test.tsx`, `src/configuracionVite.test.ts` y `src/api/cliente.test.ts`.
- El navegador valida 8 a 128 unidades UTF-16 sobre `normalize('NFC')`, sin recortar espacios (ADR-004); la spec solo pedía el mínimo. No revisa sustitutos sueltos: ese caso lo informa el 400 del servidor junto al campo.
- Mensajes que la spec no fija: 409 en definir "Ya hay una contraseña definida. Ingresá con ella." (enlace "Ir al ingreso"); 409 en ingresar "Todavía no hay una contraseña. Definí la contraseña primero." (enlace "Definir la contraseña"); 413 "La contraseña es demasiado larga. Ingresá una de 128 caracteres o menos." junto al campo; 429 "El sistema está ocupado. Intentá de nuevo en un momento."; 403, 415 y 400 sin `errors` comparten el mensaje general "No se pudo completar la operación. Recargá la página y reintentá."; 5xx y red usan el texto de la spec. El 413 se traduce siempre al campo `contrasena` (hoy es el único POST con cuerpo).
- 401 con `errors` → error del campo (contraseña incorrecta); 401 vacío o con cuerpo sin `errors` → "sin sesión": navega a `/ingreso` con `replace` y desmonta la pantalla protegida. `Arranque` también navega ante "sin sesión" porque su efecto corre antes de que `App` registre el manejador global.
- Los errores de campo del servidor se muestran junto a la contraseña aunque traigan otro campo (hoy la Api solo usa `contrasena`).
- `esEjemplo` no se muestra en pantalla (la spec no lo pide); queda disponible en `useMarca()`, sin consumidores en la aplicación.
- Después de definir (201) o ingresar (204) se navega a `/`, que vuelve a consultar estado y sesión una vez; no hay consultas periódicas (prueba con reloj falso que avanza 61 minutos).
- `fetch` con `credentials: 'same-origin'` y `cache: 'no-store'`; todo POST lleva `Content-Type: application/json`, incluido `salir` sin cuerpo; el formulario declara `method="post"` como defensa en profundidad.
- Colores de marca aplicados por CSSOM solo si cumplen `^#[0-9A-Fa-f]{6}$`; logo por `<img src="/api/marca/logo">`; build sin sourcemaps; `index.html` con `referrer` `no-referrer`. Compatible con una CSP `default-src 'self'` (Block 6).
- Dependencias con versiones exactas desde registry.npmjs.org: react y react-dom 19.3.1, react-router 8.5.1; de desarrollo vite 8.3.5, @vitejs/plugin-react 6.2.1, typescript 7.0.2, vitest y @vitest/coverage-v8 5.0.5, jsdom 30.1.3, @testing-library/react 16.3.3, @testing-library/dom 10.4.2, @types/react y @types/react-dom 19.3.0. `engines.node` `^22.22.2` (lo exigen jsdom y vite; queda dentro de "Node 22" de `AGENTS.md`).
- Proxy de desarrollo de Vite hacia `OPTICA_API_URL` (por defecto `https://localhost:5001`): `secure: false` solo para `localhost` o `127.0.0.1`; cualquier otro host exige un certificado TLS de confianza. Una `OPTICA_API_URL` inválida hace fallar la carga de la configuración.
- `vite.config.ts` queda fuera de `tsc` para no sumar `@types/node`; la prueba del proxy lo importa con una ruta no literal.

### Restricciones y pendientes que deja el Block 5

- No usar `npm run dev` contra una Api con datos reales en una red no confiable; ante un error de TLS con otro host, instalar un certificado de confianza en lugar de volver a `secure: false`.
- El `<img>` del logo se pide con la cookie y renueva la sesión una vez por carga de pantalla (no es periódico).
- Menor: `vite.config.ts` sin verificación de tipos (un `tsconfig.node.json` con `@types/node` lo resolvería); no hay ESLint configurado, el control estático es `tsc` estricto.
- Menor: `Politica_SieteCaracteresConEspaciosALosLados_SeRechaza` (`' abcde '`) no distingue un `trim()`; la regla "los espacios cuentan" la protege `Politica_SieteEspaciosYUnaLetra_SeAcepta`. Se puede reemplazar por `' abcdef '` (8 con espacios, se acepta).

## Block 6 — Servir la SPA desde la Api y pruebas de punta a punta

- Chrome y Edge para el criterio de cierre: se instalaron en WSL (`npx playwright install --with-deps chrome msedge`, Chrome 156.0.8078.79 y Edge 156.0.4314.58) y `npm run test:harness` corre en los proyectos `chrome` y `msedge` — aprobada por el usuario (opción 3, instalación hecha por el usuario con sudo) — 2026-11-02 — Block 6

Supuestos del implementador (3 rondas) — aceptados por el orquestador — 2026-11-02 — Block 6:

- La SPA se sirve con un middleware propio (`src/Optica.Api/Web/SpaEstatica.cs`, no listado en la spec) con `UseWhen` antes del ruteo, en lugar del fallback en `Program.cs` con `MapFallbackToFile`: un endpoint de fallback anónimo esquivaría R-01 y romperían `EndpointsProtegidos_SinSesion_NoAutorizado401` y `RutasAnonimas_SonExactamenteCinco`. Las rutas `/api` (por segmento, sin distinguir mayúsculas) siguen al ruteo y a la política por defecto; `/apiario` va a la SPA.
- CSP de la SPA `default-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'`, más estricta que el `default-src 'self'` de la spec, contra el clickjacking en las pantallas de contraseña; `X-Content-Type-Options: nosniff` en toda respuesta fuera de `/api`.
- Solo `GET`/`HEAD` sin extensión reciben `index.html`; rutas con extensión inexistente y otros métodos, 404; `index.html` con `Cache-Control: no-cache` (por el fallback y por `/index.html` directo); `HEAD` sin cuerpo. La existencia de `index.html` se comprueba en cada pedido: si se borra con la Api en marcha, 404; si desaparece entre la comprobación y el envío (`FileNotFoundException`/`DirectoryNotFoundException` antes de empezar la respuesta), también 404.
- Guarda de `Optica:Web:Directorio`: si es igual o contiene la raíz del contenido, la carpeta de la aplicación o la carpeta de datos (que guarda las claves de Data Protection), no se sirve nada (advertencia sin ruta y 404). Un directorio relativo se resuelve contra la raíz del contenido; la advertencia de "no se encontró `index.html`" lleva la ruta pasada por `RutasPrivadas`. Riesgo aceptado: la guarda compara rutas sin resolver enlaces simbólicos ni junctions (armarlos exige escribir la carpeta de la aplicación, que es solo de Administradores).
- `AllowedHosts` por defecto `"localhost"` (el acceso por `127.0.0.1` recibe 400); se amplía al instalar con el nombre de la PC.
- Datos personales de empleados: `Directory.Build.props` activa `ContinuousIntegrationBuild` y desactiva Source Link solo en Release, así los PDB publicados no llevan la ruta del repositorio, el perfil del desarrollador ni la URL de GitHub con su cuenta. `PublicacionSinRutasTests` (Trait `Publicacion`) hace un `dotnet publish -c Release` y falla si encuentra la ruta del repositorio, el perfil, `github.com`, `raw.githubusercontent.com` o el nombre de usuario (4 caracteres o más). Las compilaciones Debug siguen con rutas y Source Link: no salen de la PC de desarrollo. Corridas rápidas: `--filter "Categoria!=Publicacion"`.
- El `LogCritical` de `DirectorioDatosInvalidoException` ya no adjunta la excepción y los errores de `ResolverDirectorio` pasan la ruta por `RutasPrivadas` (cierra pendientes de los Blocks 1 y 3).
- e2e: `e2e/servidor.mjs` y `e2e/compilacion.mjs` (no listados) compilan la web y la Api en Release en temporales y levantan una Api por navegador (puertos desde 5543, configurables con `OPTICA_E2E_PUERTO`), con base y marca de prueba; `dotnet` nunca pasa por el intérprete de comandos (la ruta del repositorio tiene un espacio); solo `npm.cmd` en Windows, con argumentos fijos. La limpieza borra solo `optica-e2e-*` de procesos muertos o sin `pid` con más de 10 minutos. El e2e usa el certificado de desarrollo de .NET con `ignoreHTTPSErrors`, define una contraseña de exactamente 8 caracteres y baja `Microsoft.EntityFrameworkCore` a Warning solo en la corrida.
- Pruebas de path traversal con `Server.SendAsync` y la ruta cruda; no cubren la normalización propia de Kestrel (que la hace antes del middleware).
- `@playwright/test` 1.64.1 exacto; `engines` node `^22.22.2` y npm `>=10.9.8` como `web/`. `web/.npmrc` y `e2e/.npmrc` con `engine-strict=true`: `npm ci` falla con otra línea de Node (por ejemplo Node 24) en lugar de solo avisar.
- Los errores de lanzamiento del e2e (`mensajeDeFallo`) solo informan `error.code`, nunca el texto del error; un archivo `pid` vacío, 0 o negativo cuenta como sin `pid`.
- `docs/instalacion.md`: certificado autofirmado con SAN `NOMBRE-PC` y `localhost`, `ca=false`, en `CurrentUser\My`, importado en Root de cada cuenta y PC que use el navegador (o `LocalMachine\Root`); contraseña definida escuchando solo en `localhost` antes de abrir la red (R-02); firewall solo en red privada; permisos NTFS de marca y backups por SID; backup con `VACUUM INTO` y restauración; restricciones de log y de PDB.
- Archivos fuera de la lista de la spec: `SpaEstatica.cs`, `Directory.Build.props`, `e2e/servidor.mjs`, `e2e/compilacion.mjs`, `e2e/compilacion.test.mjs`, `e2e/README.md`, `InstalacionTests.cs`, `PublicacionSinRutasTests.cs`, pruebas nuevas de Infrastructure y `*.cer` en `.gitignore`.

### Restricciones y pendientes que deja el Block 6

- Falla intermitente vista una vez en `Spa_DirectorioWebFaltante_ApiArrancaY404`, no reproducida en 35 corridas; si se repite, guardar el mensaje (candidatos: el `Assert.Single` de la advertencia y el filtro de Warning con excepción de cualquier categoría).
- NFR-03: la versión inmediata anterior de Chrome y Edge se verifica a mano en VERIFY.
- `PublicacionSinRutasTests` escribe `obj/Release` en el árbol de fuentes; dos corridas a la vez pueden chocar.
- La raíz del contenido depende del directorio de trabajo (no hay hospedaje como servicio de Windows); la instalación usa rutas absolutas.
- La Api corre en Windows en la PC del local: solo ahí las claves de Data Protection quedan protegidas con DPAPI (en WSL/Linux se guardan sin cifrar, como en las pruebas).
- Menor, sin ronda disponible: el 404 de la carrera de `index.html` usa `Response.Clear()` y sale sin CSP ni `nosniff` (cuerpo vacío); la rama del `catch` no tiene prueba propia (haría falta un `IFileInfo` falso que diga `Exists=true` y falle al abrirse); `e2e/compilacion.test.mjs:12-20` conserva envoltorios defensivos y un comentario de la etapa TDD que ya sobran.
