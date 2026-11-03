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

Pendientes menores: `ArmarCoincideCon` usa `Aggregate` sin semilla y fallaría con una lista vacía
(hoy no puede pasar); las guardas del constructor de `EventoAcceso` siguen sin prueba (FEAT-001a);
el SAST de FEAT-001b tiene que mencionar la excepción de constantes en F-SAST-02.
