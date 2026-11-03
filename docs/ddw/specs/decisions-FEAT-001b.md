# Decisiones fuera de la spec — FEAT-001b

Formato: decisión — quién la aprobó — fecha — bloque.

"Aceptada por el orquestador" significa: tomada por el implementador o el orquestador, aceptada por
las dos revisiones independientes en autonomía `minimal` y mostrada al usuario en la conversación;
ninguna persona la aprobó explícitamente todavía.

## PLAN

Decisiones del usuario, ya incorporadas en la spec y el modelo de amenazas:

- Un error en la contraseña actual al cambiarla no cuenta para el bloqueo (riesgo R-04, mantenido después de explicarle el efecto de una sesión robada) — aprobada por el usuario — 2026-11-02 — PLAN
- El restablecimiento es un comando de la aplicación en la PC, registra un evento `restablecimiento` y corta las sesiones renovando las claves de Data Protection — aprobada por el usuario — 2026-11-02 — PLAN
- Los eventos de acceso quedan sin retención en este ticket (riesgo aceptado R-14) — aprobada por el usuario — 2026-11-03 — PLAN

Precisiones de la tercera auditoría de arquitectura de la spec (PASS, 7 WARN), que no se
volcaron a la spec para no consumir el último loop correctivo y que el implementador de cada
bloque cumple como parte del bloque — aceptadas por el orquestador — 2026-11-03 — PLAN:

- Block 5, momento del candado en la Api: después de `builder.Build()` y de
  `FabricaConexion.PrepararDirectorio()`, y antes de `Database.Migrate()`, dentro del manejo de
  errores de `InicializarBaseDeDatos`; si no se puede adquirir, `LogCritical` más una excepción,
  nunca `Environment.Exit`. Así `dotnet ef` no lo toma y dos instancias no migran a la vez.
- Block 5, vida del candado: el `IDisposable` adquirido se registra como singleton del contenedor
  (o se ata a `IHostApplicationLifetime`) para que lo libere el `Dispose` del host y no lo recolecte
  el JIT; `Ingresar_BloqueoSobreviveAlReinicio_Bloqueado423` descarta la primera fábrica antes de
  crear la segunda. No usar `FileOptions.DeleteOnClose` ni interpretar la existencia del archivo
  como "tomado": el candado es el handle abierto.
- Block 5, orden del comando: primero verificar sin crear nada que existan el directorio de datos y
  `optica.db`; después adquirir el candado; después comprobar las migraciones pendientes.
  Clasificación: `IOException` por uso compartido y `UnauthorizedAccessException` → "en marcha";
  `DirectoryNotFoundException` → "base inexistente"; cualquier otra → falla con el tipo; todas con
  código 2.
- Block 5, datos personales: los mensajes y el log de la Api y del comando sobre el candado usan
  solo `RutaVisible` y el nombre del tipo de excepción, sin adjuntar la excepción (su texto trae la
  ruta con el usuario de Windows); una prueba de `RegistroTests` lo cubre para la segunda instancia.
  `optica.instancia` queda vacío: sin PID, usuario ni nombre de equipo.
- Block 5, plataforma: fuera de Windows `FileShare.None` es un candado advisory (`flock`); los
  directorios de prueba nunca van bajo `/mnt/c`. En VERIFY se corren a mano en Windows
  `Comando_ApiEnMarcha_ErrorSinCambios` y `Api_SegundaInstanciaSobreElMismoDirectorio_RechazadaAlArrancar`.
- Block 5, `docs/instalacion.md`: agregar que los backups contienen los eventos de acceso (datos
  personales de las operadoras) y se guardan con la misma protección que `optica.db` (R-14).
- Block 1 y Block 3, migraciones: se generan con `dotnet ef migrations add` en el orden de los
  bloques (`IndiceEventoAccesoTipoResultado` en Block 1, `TokenConcurrenciaCredencial` en Block 3),
  con fecha posterior a `20261030220627_Inicial`; una prueba comprueba que `GetMigrations()`
  devuelve `Inicial`, `IndiceEventoAccesoTipoResultado` y `TokenConcurrenciaCredencial` en ese orden.

Desvío de proceso del orquestador: renumeró referencias a bloques del modelo de amenazas con `sed`
en lugar de Edit; el documento se revalidó después.

## Block 1 — Política de bloqueo y consulta de rechazos

Supuestos del implementador (2 rondas) — aceptados por el orquestador — 2026-11-03 — Block 1:

- `PoliticaBloqueo.RechazosDesdeUltimoReinicio(eventos)` queda en Domain como oráculo de referencia
  de la consulta (no lo usa producción); lo usan el doble `AlmacenEnMemoria` y una prueba de
  equivalencia que compara sus fechas con las del SQL en cuatro escenarios.
- `PoliticaBloqueo.Evaluar` y `EstadoBloqueo.BloqueadoHasta` exigen fechas UTC (`ArgumentException`)
  y no nulas; Block 2 pasa `reloj.GetUtcNow().UtcDateTime`.
- El resultado es el record `EstadoBloqueo` (`EstaBloqueado`, `Hasta` nulo si no hay bloqueo).
- La consulta solo ve eventos confirmados: para responder 423 en el quinto rechazo, Block 2 evalúa
  la política con el rechazo recién registrado agregado a la lista.
- La comparación de textos distingue mayúsculas (colación BINARY): `INGRESO` o `Exito` son textos
  desconocidos, no cuentan ni reinician.
- Fuente única: `PoliticaBloqueo.EventoQueCuenta` (`ingreso/rechazo`) y `EventosQueReinician`; la
  consulta arma sus filtros desde ahí.
- Excepción a "parameterized queries for all data values" (`AGENTS.md`): los textos del esquema
  (`ingreso`, `rechazo`, `exito`, …) van como constantes en la expresión de EF, para que
  `ToQueryString()` sea ejecutable en `EXPLAIN QUERY PLAN`. Salen de `TextoDe` sobre enums cerrados
  del dominio, nunca de datos del usuario ni de la base; si `ArmarCoincideCon` llegara a recibir
  valores externos, se vuelve a parametrizar. Las fechas y los Id no entran como literales.
- Migración `20261103130011_IndiceEventoAccesoTipoResultado` generada con `dotnet ef`; la prueba
  exige la lista exacta de migraciones a propósito para fijar el orden, y Block 3 la extiende con
  `TokenConcurrenciaCredencial`. En tickets futuros conviene relajarla a "presentes y en orden".
- `tests/Optica.Api.Tests/ArranqueYErroresTests.cs` y `RegistroTests.cs` no se tocaron: sus dobles no
  implementan `IRegistroEventosAcceso` (la spec los listaba de más).

Pendientes menores del Block 1: `ArmarCoincideCon` usa `Aggregate` sin semilla y fallaría con una lista vacía
(hoy no puede pasar); las guardas del constructor de `EventoAcceso` siguen sin prueba (FEAT-001a);
el SAST de FEAT-001b tiene que mencionar la excepción de constantes en F-SAST-02.

## Block 2 — Ingreso con bloqueo en la Api

Supuestos del implementador (3 rondas) — aceptados por el orquestador — 2026-11-03 — Block 2:

- Desvío de la letra de la spec ("vuelve a evaluar la política con ese rechazo incluido"): después de
  confirmar el rechazo propio, el caso de uso relee los rechazos confirmados (una consulta de solo
  lectura más, solo en el camino de rechazo) y queda bloqueado si da bloqueado
  `Evaluar(confirmados, ahoraNuevo)` (con la hora tomada después de releer) o
  `Evaluar([.. rechazosLeidos, ahora], ahora)` (lo leído más el rechazo propio). La primera evita que
  dos pedidos simultáneos sobre el tercer rechazo respondan los dos 401; la segunda cubre la carrera
  inversa cuando la hora de la PC retrocede entre dos pedidos. Con eso el quinto rechazo responde 423
  en ese mismo intento en cualquier orden de confirmación; sigue habiendo una sola confirmación por
  pedido.
- Falso 423 aceptado (hacia el lado seguro): si otro pedido confirma un reinicio (`ingreso/exito`;
  desde los Blocks 3 a 5 también definición, cambio o restablecimiento) entre la lectura y la
  confirmación propia, la segunda evaluación responde 423 aunque la cuenta real volvió a empezar;
  solo afecta esa respuesta, no el registro, y el pedido siguiente entra normal.
- `ahora` se toma después de la primera consulta de rechazos; el evento conserva ese `ahora`.
- El 423 es ProblemDetails con `title` "El acceso está bloqueado." y `detail` "El acceso está
  bloqueado temporalmente. Volvé a intentar en unos minutos.", sin `errors` ni hora de fin.
- `Ingresar_LimitadorSaturado_Rechazado429` ya no espera 12 respuestas 401: con el limitador de 2
  pedidos, entre 3 y 4 responden 401 y el resto 423 (estable en 20 corridas).
- `tests/Optica.Infrastructure.Tests/Acceso/AccesoPersistenciaTests.cs` no se tocó: el constructor de
  `IngresarConContrasena` no cambió (la spec lo listaba de más).

Pendientes del Block 2: el resumen XML de `IngresarConContrasena` (líneas 40-42) no menciona la
segunda evaluación, y el comentario de las líneas 84-86 y de la prueba de carrera inversa atribuye la
segunda evaluación a "si el reloj no avanzó" en vez de a que la hora de la PC retroceda; se corrigen en
el cierre de CODE. La web todavía no traduce el 423 (Block 6). Tomar `ahoraNuevo` después de releer
no tiene una prueba propia (sobreviven las mutaciones que lo toman antes o lo igualan a `ahora`): lo
que exige la spec se cumple igual por las otras dos evaluaciones, y una prueba con un gancho entre
la confirmación y la relectura lo fijaría. El piso de tres 401 de `LimitadorSaturado` no está
garantizado en teoría (estable en 30 corridas).

## Block 3 — Contratos y persistencia del cambio de contraseña

Supuestos del implementador (3 rondas) — aceptados por el orquestador — 2026-11-03 — Block 3:

- `CredencialAcceso.ReemplazarHash(nuevo, ahora)` usa las mismas validaciones que el constructor
  (hash no vacío, fecha UTC) y valida antes de asignar; `Hash` y `DefinidaEn` pasan a `private set`.
- `ICredencialAccesoRepository.Reemplazar(anterior, nueva)`: `anterior` es la credencial tal como se
  leyó, sin modificar, y `nueva` otra instancia con el hash nuevo; la misma instancia en los dos se
  rechaza con `ArgumentException` (`paramName` `nueva`). El `UPDATE` filtra por `Hash = @anterior`
  (`IsConcurrencyToken()`), dentro de la transacción de `UnidadDeTrabajoEf`, sin `ExecuteUpdate`; si
  la fila ya está en el contexto, reutiliza esa entrada.
- Datos sensibles: `ConflictoAlConfirmarException` nunca lleva la excepción de EF como causa
  (concurrencia, unicidad ni CHECK), porque sus `Entries` guardan el hash de la credencial. El
  mensaje nuevo de concurrencia es "Los datos a guardar cambiaron después de leerlos."
- `ValidacionContrasena.Validar(contrasena, out normalizada, campo = Campo)` con las constantes
  `CampoActual` (`contrasenaActual`) y `CampoNueva` (`contrasenaNueva`); un campo vacío lanza
  `ArgumentException`. Los llamadores de FEAT-001a no cambian.
- El doble `AlmacenEnMemoria` imita el repositorio real: `ObtenerAsync` devuelve una copia,
  `Reemplazar` rechaza la misma instancia y el conflicto compara contra el hash leído.
- Migración `20261103143513_TokenConcurrenciaCredencial` sin SQL, generada con `dotnet ef`. La
  justificación de la spec ("para que `Migrate()` no encuentre cambios de modelo pendientes") no se
  sostiene en SQLite: un token de concurrencia no cambia el esquema y `HasPendingModelChanges()` daba
  falso sin ella. Se mantiene porque alinea el snapshot con el modelo y no tiene efectos.
- Pruebas nuevas fuera de la lista de la spec: `CredencialAccesoTests.cs`,
  `ValidacionContrasenaTests.cs` y `AlmacenEnMemoriaTests.cs`; `Modelo_SinCambiosPendientes_LaApiArranca`
  en la Api.
- xUnit serializa los datos de `MemberData` al descubrir las pruebas y un sustituto suelto llega como
  U+FFFD: las pruebas con sustitutos sueltos usan `DisableDiscoveryEnumeration = true`.

Pendiente del Block 3 para el cierre de CODE: quitar el parámetro opcional `Exception? causa` del
constructor de `ConflictoAlConfirmarException` (nadie lo usa), para que "nunca lleva la causa" se
cumpla por compilación.
