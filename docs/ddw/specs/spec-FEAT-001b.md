# Spec FEAT-001b: Bloqueo y gestión de la contraseña

| Field | Value |
|-------|-------|
| Ticket | FEAT-001b |
| PRD | docs/ddw/prd/prd-FEAT-001b.md |
| Tier | FEATURE |
| Date | 2026-11-03 |
| Spec loops | 2 |
| Loops since last human decision | 2 |

## Summary
Sobre la solución en capas de FEAT-001a. El bloqueo por intentos fallidos se deriva del registro
`EventoAcceso` que ya existe, sin tabla nueva: a partir del quinto rechazo de ingreso consecutivo el
ingreso queda bloqueado 5 minutos, anclados en ese quinto rechazo aunque una carrera concurrente lo
supere, y el bloqueo sobrevive a un reinicio. Una migración aditiva agrega un índice para que la
consulta no recorra la tabla. El cambio de contraseña es un caso de uso nuevo detrás de
`POST /api/acceso/cambiar`, con sesión. El restablecimiento es un comando de la aplicación
(`Optica.Api restablecer-contrasena`) que se ejecuta en la PC y se niega si la Api está en marcha:
borra la credencial, registra el evento y renueva las claves de sesión, sin endpoint. La web suma el
mensaje de bloqueo y la pantalla de cambio.

Decisiones del usuario del 2026-11-02, que esta spec implementa:
- Un error en la contraseña actual al cambiarla NO cuenta para el bloqueo (riesgo aceptado R-04 del
  modelo de amenazas).
- El restablecimiento es un comando de la aplicación con confirmación por consola.
- El restablecimiento registra un evento `restablecimiento` en el historial.
- El restablecimiento corta las sesiones abiertas renovando las claves de Data Protection.

Spec loop 1 (2026-11-03): aplica la auditoría de arquitectura de PLAN (2 FAIL: bloqueo eludible por
concurrencia y comando ejecutable con la Api en marcha; 11 WARN), sin cambiar requisitos del PRD.
Spec loop 2 (2026-11-03): aplica la segunda auditoría (1 FAIL: el mutex con nombre fijo rompía las
pruebas en paralelo y dependía del hilo, reemplazado por un candado de archivo en el directorio de
datos; 7 WARN: conexión `Mode=ReadWrite` para las migraciones, raíz del contenido fijada al crear el
builder, token de concurrencia en el modelo, política sincrónica, límite del reloj atrasado y
comparación de claves enteras en la prueba de ARCA).

Criterio de ubicación de contratos, el mismo de FEAT-001a: los contratos de persistencia
(repositorios, registro de eventos) van en Domain; los de servicios técnicos que usa un caso de uso
(`IDatosLocales`, hasher, unidad de trabajo) van en Application. Los controllers y el comando de
consola solo invocan casos de uso y contratos de Application.

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 2, Block 6 |
| FR-02 | Block 1, Block 2 |
| FR-03 | Block 3, Block 4, Block 6 |
| FR-04 | Block 5 |
| FR-05 | Block 5 |
| FR-06 | Block 7 |
| NFR-01 | Block 1 (`PoliticaBloqueo`: 5 rechazos consecutivos, 5 minutos) |
| NFR-02 | Strategy: Block 7 corre la suite e2e en los proyectos `chrome` y `msedge` de Playwright (versión estable instalada); la versión inmediata anterior de cada navegador se verifica a mano en VERIFY con la misma suite, documentando las versiones probadas |

## Dependencies between blocks
Block 1 → Block 2 → Block 3 → Block 4 → Block 5 → Block 6 → Block 7. Block 2 usa la política y la
consulta de Block 1; Block 3 usa los tipos de evento de Block 1; Block 4 usa los contratos de
Block 3; Block 5 usa los tipos de evento de Block 1 y `ValidacionContrasena` sin cambios; Block 6
consume las Api de Block 2 y Block 4; Block 7 necesita todo lo anterior.

## Block 1 — Política de bloqueo y consulta de rechazos

**Files**
- `src/Optica.Domain/Acceso/EventoAcceso.cs` (modified) — `TipoEventoAcceso` suma `CambioContrasena`
  y `Restablecimiento`; `ResultadoEventoAcceso` suma `Bloqueado`
- `src/Optica.Domain/Acceso/PoliticaBloqueo.cs` (new) — la regla del bloqueo y el conjunto
  `EventosQueReinician`
- `src/Optica.Domain/Acceso/IRegistroEventosAcceso.cs` (modified) — consulta
  `ObtenerRechazosIngresoDesdeUltimoReinicioAsync`
- `src/Optica.Infrastructure/Persistencia/RegistroEventosAccesoEf.cs` (modified) — implementa la
  consulta filtrando por los textos conocidos y los textos nuevos al registrar
- `src/Optica.Infrastructure/Persistencia/OpticaDbContext.cs` (modified) — índice
  `IX_EventoAcceso_Tipo_Resultado` y el comentario de `EventoAccesoFila` con los textos nuevos
- `src/Optica.Infrastructure/Persistencia/Migraciones/<fecha>_IndiceEventoAccesoTipoResultado.cs`
  (new) — migración aditiva del índice, con `Down` que lo elimina
- `tests/Optica.Domain.Tests/Acceso/PoliticaBloqueoTests.cs` (new)
- `tests/Optica.Application.Tests/Acceso/Dobles.cs` (modified) — `AlmacenEnMemoria` implementa la
  consulta nueva
- `tests/Optica.Api.Tests/ArranqueYErroresTests.cs` y `tests/Optica.Api.Tests/RegistroTests.cs`
  (modified) — los dobles de registro de eventos compilan con el contrato nuevo
- `tests/Optica.Infrastructure.Tests/Acceso/AccesoPersistenciaTests.cs` (modified) — la consulta y
  la migración

**Logic**
Implementa FR-01, FR-02 y NFR-01 en el dominio. Contrato sin reglas de negocio:
`ObtenerRechazosIngresoDesdeUltimoReinicioAsync` devuelve, en orden de `Id` ascendente, las fechas
UTC de los eventos `ingreso/rechazo` posteriores al último evento de `PoliticaBloqueo.EventosQueReinician`
(`ingreso/exito`, `definicion/exito`, `cambio_contrasena/exito`, `restablecimiento/exito`). La consulta
filtra por esos textos conocidos, así que un texto desconocido (por ejemplo de una base de otra
versión) queda afuera sin lanzar. El orden es por `Id`, no por `FechaHora`.

`PoliticaBloqueo.Evaluar(rechazos, ahora)` (puro y sincrónico; la consulta la hace
`IngresarConContrasena` en Block 2), con `k` = cantidad de rechazos y
`n = floor(k / 5)`: si `n ≥ 1`, el ingreso está bloqueado mientras `0 ≤ ahora − fecha(rechazo número
5·n) < 5 minutos`. Así el bloqueo se ancla en el rechazo que cruzó el múltiplo de 5 aunque una
carrera concurrente agregue un sexto, se libera solo a los 5 minutos (FR-02) y el siguiente bloqueo
llega en el décimo rechazo. Si `ahora` es anterior a esa fecha (la hora de la PC se atrasó), no hay
bloqueo, para no dejar a las dueñas sin acceso días enteros si la hora se corrige hacia atrás. La
regla se corrige sola: el siguiente múltiplo de 5 se ancla con la hora nueva, así que cada atraso
libera como máximo 4 intentos antes del próximo bloqueo. La hora puede moverla quien tenga acceso a la
PC o la sincronización horaria de Windows; el límite queda documentado en R-02 del modelo de
amenazas. `ingreso/bloqueado` y `cambio_contrasena/rechazo` no cuentan ni reinician.

**Data model**
- Reuses `EventoAcceso` from FEAT-001a Block 1 — no column change; constraints as declared there:
  `Id` INTEGER PK autoincremental, `Tipo` TEXT NOT NULL, `Resultado` TEXT NOT NULL, `FechaHora` TEXT
  NOT NULL (ISO 8601 UTC), index `IX_EventoAcceso_FechaHora`; sin CHECK sobre `Tipo` ni `Resultado`.
  New index `IX_EventoAcceso_Tipo_Resultado` on (`Tipo`, `Resultado`), non-unique, por migración
  aditiva. Valores de texto nuevos: `Tipo` `cambio_contrasena` y `restablecimiento`; `Resultado`
  `bloqueado`.

**Input validation**
- Texto de `Tipo` o `Resultado` leído de la base: solo se consultan los textos conocidos; un valor
  desconocido no se lee y no lanza.
- `ahora` anterior a la fecha del rechazo que ancla el bloqueo: se trata como sin bloqueo.

**Error handling**
- Cinco o más rechazos consecutivos dentro de los 5 minutos del ancla — la política devuelve
  bloqueado con la hora de fin.
- Sexto rechazo por una carrera concurrente — el bloqueo sigue anclado en el quinto; no se pierde.
- Texto de evento desconocido en la base — queda fuera de la consulta; nunca un error.
- Hora de la PC atrasada respecto del ancla — sin bloqueo, sin error.

**Required tests**
- [ ] `PoliticaBloqueo_CincoRechazosConsecutivos_Bloqueado5Minutos` — validates AC-01 (sad path:
      rechazado)
- [ ] `PoliticaBloqueo_SeisRechazosPorConcurrencia_Bloqueado` — (sad path: rechazado)
- [ ] `PoliticaBloqueo_A5MinutosDelQuintoRechazo_Desbloqueado` — validates AC-02
- [ ] `PoliticaBloqueo_ExitoReiniciaLaCuenta` — 4 rechazos, un éxito y 4 rechazos no bloquean
- [ ] `PoliticaBloqueo_DecimoRechazo_BloqueadoOtraVez` — (sad path: rechazado)
- [ ] `PoliticaBloqueo_RelojAtrasado_SinBloqueoNiError` — (sad path: reloj inválido)
- [ ] `PoliticaBloqueo_RelojAtrasado_DecimoRechazoBloqueaConLaHoraNueva` — (sad path: rechazado)
- [ ] `RegistroEventos_TextoDesconocido_IgnoradoSinError` — (sad path)
- [ ] `RegistroEventos_RechazosDesdeUltimoReinicio_OrdenadosPorId`
- [ ] `Migracion_IndiceTipoResultado_CreadoYRevertible`

**Completion criterion**
Las diez pruebas pasan, la migración aplica y revierte sobre una base temporal, y las pruebas de
FEAT-001a siguen en verde.

## Block 2 — Ingreso con bloqueo en la Api

**Files**
- `src/Optica.Application/Acceso/IngresarConContrasena.cs` (modified) — consulta el bloqueo antes del
  hash; `ResultadoIngreso.Bloqueado` e `IntentoIngreso.Bloqueado`
- `src/Optica.Api/Controllers/AccesoController.cs` (modified) — traduce `Bloqueado` a 423
- `src/Optica.Api/Errores/RespuestasValidacion.cs` (modified) — respuesta del bloqueo
- `tests/Optica.Application.Tests/Acceso/Dobles.cs` (modified) — reloj que avanza
- `tests/Optica.Application.Tests/Acceso/IngresarConContrasenaTests.cs` (modified)
- `tests/Optica.Infrastructure.Tests/Acceso/AccesoPersistenciaTests.cs` (modified) — construcción de
  `IngresarConContrasena` con la consulta
- `tests/Optica.Api.Tests/Acceso/AccesoTests.cs` (modified) — `Ingresar_LimitadorSaturado_Rechazado429`
  deja de esperar 12 respuestas 401
- `tests/Optica.Api.Tests/Acceso/CierrePorDefectoTests.cs` (modified) — `Bloqueado` no emite cookie

**Logic**
Implementa FR-01 y FR-02 en el caso de uso y la Api. `IngresarConContrasena`, en este orden: valida
la política (400, ADR-005, antes que todo, también durante un bloqueo); sin credencial,
`SinCredencial` (409); con bloqueo vigente, registra `ingreso/bloqueado`, no calcula el hash y
devuelve `Bloqueado`; si no, verifica el hash. Si no coincide, registra `ingreso/rechazo` y vuelve a
evaluar la política con ese rechazo incluido: si con él queda bloqueado (el quinto), devuelve
`Bloqueado` en lugar de `Rechazo`, así la persona se entera en ese mismo intento. Todo en una sola
unidad de trabajo por pedido. El controller emite la cookie solo con `Exito` (cerrado por defecto) y
traduce `Bloqueado` a 423 con ProblemDetails y el mensaje "El acceso está bloqueado temporalmente.
Volvé a intentar en unos minutos."; el 429 del limitador sigue respondiendo antes del controller.

**API contract**
- Method + path: `POST /api/acceso/ingresar` (existente, de FEAT-001a)
- Request: `{ "contrasena": string }` (sin cambios)
- Response: 204 con cookie de sesión (sin cambios)
- Error codes: 400, 401, 409, 413, 415, 429 (sin cambios) y 423 nuevo (bloqueado, ProblemDetails
  con `detail` y sin `errors`), también en el quinto rechazo
- Auth: anónimo (sin cambios; `RutasAnonimas` no cambia)

**Data model**
- Reuses `EventoAcceso` from Block 1 — no schema change; constraints as declared there: `Tipo` TEXT
  NOT NULL `ingreso`, `Resultado` TEXT NOT NULL `exito`, `rechazo` o `bloqueado`, index
  `IX_EventoAcceso_Tipo_Resultado`.

**Input validation**
- `contrasena`: la de FEAT-001a (string JSON, requerida, 8 a 128 unidades UTF-16 en NFC).

**Error handling**
- Ingreso durante un bloqueo — 423 con mensaje de bloqueo temporal; evento `ingreso/bloqueado`; el
  hash no se calcula, ni siquiera con la contraseña correcta.
- Quinto rechazo consecutivo — 423 en ese mismo intento; evento `ingreso/rechazo`.
- Contraseña fuera de política durante un bloqueo — 400 como en ADR-005, sin evento.
- Resultado de ingreso no previsto en el controller — falla cerrado (500 genérico, sin cookie).

**Required tests**
- [ ] `IngresarConContrasena_Bloqueado_RechazadoSinCalcularHashYEventoRegistrado` — validates AC-01
      (sad path)
- [ ] `IngresarConContrasena_QuintoRechazo_DevuelveBloqueado` — (sad path: rechazado)
- [ ] `IngresarConContrasena_FueraDePoliticaDuranteBloqueo_Invalido400SinEvento` — (sad path)
- [ ] `Ingresar_CincoRechazos_Bloqueado423ConLaCorrecta` — validates AC-01 (sad path)
- [ ] `Ingresar_Pasados5Minutos_AceptaLaCorrecta` — con el reloj adelantado 5 minutos tras el
      bloqueo, 204 — validates AC-02
- [ ] `Ingresar_DosPedidosSimultaneosEnElCuarto_Bloqueado423` — (sad path: rechazado por
      concurrencia)
- [ ] `Ingresar_BloqueoSobreviveAlReinicio_Bloqueado423` — una Api nueva sobre la misma base sigue
      bloqueada (sad path)
- [ ] `Ingresar_ResultadoBloqueado_NoEmiteCookie` — (sad path)

**Completion criterion**
Las ocho pruebas pasan y `POST /api/acceso/ingresar` responde 423 desde el quinto rechazo consecutivo
durante 5 minutos, también con dos pedidos simultáneos.

## Block 3 — Contratos y persistencia del cambio de contraseña

**Files**
- `src/Optica.Domain/Acceso/CredencialAcceso.cs` (modified) — `ReemplazarHash(nuevo, ahora)` que
  actualiza `Hash` y `DefinidaEn`
- `src/Optica.Domain/Acceso/ICredencialAccesoRepository.cs` (modified) — `Reemplazar(anterior,
  nueva)`
- `src/Optica.Infrastructure/Persistencia/CredencialAccesoRepository.cs` (modified) — implementa
  `Reemplazar` dentro de la transacción de `UnidadDeTrabajoEf`, con el `Hash` anterior como valor
  original (filtro `Id = 1 AND Hash = @anterior`), sin `ExecuteUpdate`
- `src/Optica.Infrastructure/Persistencia/OpticaDbContext.cs` (modified) — `Hash` de
  `CredencialAccesoFila` con `IsConcurrencyToken()`
- `src/Optica.Infrastructure/Persistencia/Migraciones/<fecha>_TokenConcurrenciaCredencial.cs` (new)
  — migración sin SQL que actualiza `OpticaDbContextModelSnapshot` con el token, para que
  `Migrate()` no encuentre cambios de modelo pendientes
- `src/Optica.Infrastructure/Persistencia/UnidadDeTrabajoEf.cs` (modified) — traduce
  `DbUpdateConcurrencyException` a `ConflictoAlConfirmarException`
- `src/Optica.Application/Comun/IUnidadDeTrabajo.cs` (modified) — la documentación del contrato suma
  el conflicto de concurrencia
- `src/Optica.Application/Acceso/ValidacionContrasena.cs` (modified) — el campo del error como
  parámetro (`contrasena`, `contrasenaActual`, `contrasenaNueva`); `contrasena` por defecto
- `tests/Optica.Application.Tests/Acceso/Dobles.cs`, `tests/Optica.Api.Tests/ArranqueYErroresTests.cs`,
  `tests/Optica.Api.Tests/RegistroTests.cs` (modified) — dobles de repositorio con `Reemplazar`
- `tests/Optica.Infrastructure.Tests/Acceso/AccesoPersistenciaTests.cs` (modified)

**Logic**
Prepara FR-03. `Reemplazar` usa concurrencia optimista sobre el hash: si otra operación cambió o
borró la credencial entre la lectura y la confirmación, la actualización afecta 0 filas, EF lanza
`DbUpdateConcurrencyException` y la unidad de trabajo la traduce a conflicto, sin dejar nada a
medias. `ValidacionContrasena` conserva su comportamiento de FEAT-001a con el campo por defecto.

**Data model**
- Reuses `CredencialAcceso` from FEAT-001a Block 1 — no schema change; constraints as declared
  there: una sola fila (`Id` = 1, `CK_CredencialAcceso_Id`), `Hash` TEXT NOT NULL, `DefinidaEn` TEXT
  NOT NULL (ISO 8601 UTC); `Hash` se usa como token de concurrencia en la actualización.

**Input validation**
- `nuevo` hash: el que produce el hasher de FEAT-001a (no nulo, formato PBKDF2 propio).
- `ahora`: UTC; un `DateTime` sin tipo UTC se rechaza como en `CredencialAcceso` de FEAT-001a.

**Error handling**
- Credencial cambiada o borrada entre la lectura y la confirmación — `ConflictoAlConfirmarException`;
  nada se confirma.
- Fecha no UTC al reemplazar — `ArgumentException` sin datos de la contraseña.

**Required tests**
- [ ] `CredencialAcceso_ReemplazarHash_ActualizaHashYFecha`
- [ ] `CredencialAcceso_ReemplazarConFechaNoUtc_Rechazado` — (sad path)
- [ ] `Reemplazar_CredencialCambiadaEnElMedio_Conflicto` — (sad path: conflicto)
- [ ] `ValidacionContrasena_CampoComoParametro_ErrorEnEseCampo`
- [ ] `Modelo_SinCambiosPendientes_TrasLasMigraciones` — `HasPendingModelChanges()` es falso y la Api
      arranca

**Completion criterion**
Las cinco pruebas pasan y las pruebas de FEAT-001a de definir e ingresar siguen en verde.

## Block 4 — Cambio de contraseña: caso de uso y Api

**Files**
- `src/Optica.Application/Acceso/CambiarContrasena.cs` (new) — el caso de uso
- `src/Optica.Api/Controllers/AccesoController.cs` (modified) — acción `cambiar` y el record
  `SolicitudCambioContrasena` con las dos propiedades marcadas con `LectorContrasenaJson` y un
  `ToString()` que oculta las dos contraseñas
- `src/Optica.Api/Errores/RespuestasValidacion.cs` (modified) — cuerpo ilegible en el campo que
  corresponde
- `src/Optica.Api/Program.cs` (modified) — registra `CambiarContrasena`; comentarios del limitador
- `tests/Optica.Application.Tests/Acceso/CambiarContrasenaTests.cs` (new)
- `tests/Optica.Api.Tests/Http.cs` (modified) — ayudas para `contrasenaActual` y `contrasenaNueva`
- `tests/Optica.Api.Tests/Acceso/AccesoTests.cs`, `tests/Optica.Api.Tests/Acceso/ProteccionTests.cs`,
  `tests/Optica.Api.Tests/Acceso/CierrePorDefectoTests.cs` (modified) — las teorías de 413 y 415
  suman `cambiar` con cookie; el constructor del controller con el caso de uso nuevo
- `tests/Optica.Api.Tests/RegistroTests.cs` (modified) — las pruebas de log cubren `cambiar`

**Logic**
Implementa FR-03. `CambiarContrasena(actual, nueva)`: valida la forma de las dos con la política de
FEAT-001a (NFC, 8 a 128) y su campo; sin credencial devuelve `SinCredencial`; verifica la actual con
el hasher; si no coincide registra `cambio_contrasena/rechazo` (no cuenta para el bloqueo, decisión
del usuario) y devuelve el error en `contrasenaActual`; si coincide reemplaza el hash (PBKDF2 nuevo,
sal nueva, `DefinidaEn` = ahora) y registra `cambio_contrasena/exito`, en una sola transacción. El
bloqueo de Block 2 no impide cambiar la contraseña. Las sesiones abiertas siguen válidas (riesgo
aceptado en FEAT-001a). La acción comparte el limitador de concurrencia de `definir` e `ingresar` y
la exigencia de `application/json` (R-04 de FEAT-001a). Cuerpo ilegible: el error va en el campo de
la clave que informa ModelState y, si no hay ninguna, en `contrasenaActual`.

**API contract**
- Method + path: `POST /api/acceso/cambiar`
- Request: `{ "contrasenaActual": string, "contrasenaNueva": string }`
- Response: 204 sin cuerpo
- Error codes: 400 (ValidationProblemDetails con `errors.contrasenaActual` o
  `errors.contrasenaNueva`), 401 (sin sesión), 409 (sin credencial o conflicto al confirmar), 413,
  415, 429
- Auth: con sesión (política por defecto; no se agrega a `RutasAnonimas`)

**Data model**
- Reuses `CredencialAcceso` from Block 3 — no schema change; constraints as declared there: una
  sola fila (`Id` = 1), `Hash` TEXT NOT NULL como token de concurrencia, `DefinidaEn` TEXT NOT NULL.
  Reuses `EventoAcceso` from Block 1 — no schema change; `Tipo` TEXT NOT NULL `cambio_contrasena`,
  `Resultado` TEXT NOT NULL `exito` o `rechazo`.

**Input validation**
- `contrasenaActual`: string JSON leído con `LectorContrasenaJson`, requerida, 8 a 128 unidades
  UTF-16 en NFC, sin caracteres no válidos.
- `contrasenaNueva`: string JSON leído con `LectorContrasenaJson`, requerida, 8 a 128 unidades UTF-16
  en NFC, sin caracteres no válidos.
- Cuerpo: `application/json`, máximo 4 KB.

**Error handling**
- Contraseña actual incorrecta — 400 con `errors.contrasenaActual` "La contraseña actual no es
  válida."; evento `cambio_contrasena/rechazo`; no se cambia nada.
- Contraseña nueva fuera de política — 400 con `errors.contrasenaNueva` y la acción; sin evento.
- Contraseña actual faltante o ilegible — 400 con `errors.contrasenaActual`; sin evento.
- Sin credencial (restablecida con una sesión abierta) — 409; no se cambia nada.
- Conflicto al confirmar (dos cambios, o cambio y restablecimiento, a la vez) — 409; nada a medias.
- Sin sesión — 401 sin cuerpo.

**Required tests**
- [ ] `CambiarContrasena_ActualCorrecta_AceptaLaNuevaYRechazaLaAnterior` — validates AC-04
- [ ] `CambiarContrasena_ActualIncorrecta_RechazadaSinCambiarYEventoRegistrado` — validates AC-05
      (sad path)
- [ ] `CambiarContrasena_ActualIncorrecta_NoCuentaParaElBloqueo` — 5 rechazos de cambio no bloquean
      el ingreso (sad path)
- [ ] `CambiarContrasena_NuevaDe7Caracteres_InvalidoSinEvento` — (sad path)
- [ ] `CambiarContrasena_SinCredencial_Conflicto` — (sad path)
- [ ] `Cambiar_ActualIncorrecta_Invalido400ConCampoActual` — validates AC-05 (sad path)
- [ ] `Cambiar_ActualFaltante_Invalido400` — (sad path)
- [ ] `Cambiar_NuevaInvalida_Invalido400ConCampoNueva` — (sad path)
- [ ] `Cambiar_SinSesion_NoAutorizado401` — (sad path)
- [ ] `Cambiar_DosCambiosSimultaneos_Conflicto409` — (sad path)
- [ ] `Log_Cambiar_NoContieneNingunaContrasena` — con captura en Trace, ninguna de las dos aparece,
      tampoco con un sustituto suelto

**Completion criterion**
Las once pruebas pasan y, después de `POST /api/acceso/cambiar` con 204, `ingresar` acepta la nueva
y rechaza la anterior.

## Block 5 — Restablecimiento por comando en la PC

**Files**
- `src/Optica.Domain/Acceso/ICredencialAccesoRepository.cs` (modified) — `Eliminar()`
- `src/Optica.Infrastructure/Persistencia/CredencialAccesoRepository.cs` (modified) — implementa
  `Eliminar` dentro de la transacción de `UnidadDeTrabajoEf`, sin `ExecuteDelete`
- `src/Optica.Application/Acceso/RestablecerContrasena.cs` (new) — el caso de uso
- `src/Optica.Application/Comun/IDatosLocales.cs` (modified) — `BaseExiste`, `MigracionesPendientes`,
  `DirectorioClaves`, `RenovarClaves()` y `RutaVisible`
- `src/Optica.Application/Comun/ICandadoInstancia.cs` (new) — contrato del candado de instancia
  única: `IntentarAdquirir()` devuelve un `IDisposable` o nulo si otra instancia lo tiene
- `src/Optica.Infrastructure/Persistencia/DatosLocales.cs` (modified) — implementa esas operaciones;
  `MigracionesPendientes` abre su propia conexión `Mode=ReadWrite` (nunca crea la base), lee
  `__EFMigrationsHistory` (si no existe, hay pendientes) y la compara con las migraciones del
  ensamblado, obtenidas sin conexión mediante un `IServiceScopeFactory`
- `src/Optica.Infrastructure/Persistencia/CandadoInstanciaArchivo.cs` (new) — implementa el candado
  con el archivo `optica.instancia` en `IDatosLocales.Directorio`, abierto con `FileShare.None`
  mientras dura el proceso; no depende del hilo y se libera solo si el proceso se cae
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — registra
  `ICandadoInstancia` y las operaciones nuevas de `DatosLocales`
- `src/Optica.Api/Acceso/ComandoRestablecerContrasena.cs` (new) — el comando; recibe solo
  `RestablecerContrasena`, `IDatosLocales`, `ICandadoInstancia`, un `TextReader` y un `TextWriter`
- `src/Optica.Api/Program.cs` (modified) — mira `args[0]` antes de `CreateBuilder`: con
  `restablecer-contrasena` crea el builder con `WebApplicationOptions { ContentRootPath =
  AppContext.BaseDirectory }`; en cualquier otro caso, como hoy. La rama del comando va en una
  función de `Program` invocable desde las pruebas, después de `builder.Build()` y antes de
  `InicializarBaseDeDatos()`. La Api adquiere el candado al arrancar y termina con un mensaje si no
  puede. Data Protection usa `IDatosLocales.DirectorioClaves`
- `tests/Optica.Api.Tests/FabricaApi.cs` (modified) — cada instancia usa su directorio de datos
  temporal, así su candado no choca con otras pruebas en paralelo
- `docs/instalacion.md` (modified) — el procedimiento, y la intro, el aviso de R-03 y §8 al día
- `tests/Optica.Application.Tests/Acceso/RestablecerContrasenaTests.cs` (new)
- `tests/Optica.Api.Tests/Acceso/ComandoRestablecerContrasenaTests.cs` (new)
- `tests/Optica.Infrastructure.Tests/Persistencia/DatosLocalesTests.cs` (modified)

**Logic**
Implementa FR-04 y FR-05. `RestablecerContrasena`: si hay credencial la elimina; siempre registra
`restablecimiento/exito`; confirma en una sola transacción, sin tocar ningún otro dato; después
renueva las claves de sesión con `IDatosLocales.RenovarClaves()` (vacía `DirectorioClaves`), así
toda sesión abierta deja de valer (decisión del usuario). El evento reinicia la cuenta del bloqueo
de Block 1. El siguiente ingreso ve la pantalla de definición (FEAT-001a, AC-03). Repetir el comando
no hace daño: registra otro evento y vuelve a renovar.

El comando: `Optica.Api restablecer-contrasena`. Lee la misma configuración que el host con la raíz
del contenido fija en `AppContext.BaseDirectory`. Antes de cualquier conexión que pueda crear la
base: adquiere el candado de instancia (si no puede, la Api está en marcha: sus claves en memoria
seguirían aceptando cookies y `definir` quedaría expuesto en la red) y lo mantiene hasta terminar,
así la Api no puede arrancar a mitad del comando; verifica que `optica.db` exista; y se niega si hay
migraciones pendientes (no migra; esa comprobación usa una conexión `Mode=ReadWrite`, que no crea la
base). Un acceso denegado al archivo del candado (por ejemplo, otra cuenta) se trata como "tomado".
Después muestra qué va a hacer y pide escribir `RESTABLECER`. Códigos de salida: 0 restableció, 1 canceló, 2 falló. La salida usa
`IDatosLocales.RutaVisible`, nunca la ruta expandida con el usuario de Windows, y describe las fallas
con `IDatosLocales.DescribirFalla` (tipo y código de SQLite), sin la excepción. No hay endpoint, ruta
ni pantalla de restablecimiento (FR-05).

`docs/instalacion.md` suma el procedimiento: hacer un backup con `VACUUM INTO`, detener la Api,
ejecutar el comando con la cuenta de la Api desde `C:\OpticaSistema\App`, volver a restringir la Api
a `localhost` y definir la contraseña desde la PC antes de abrir la red (R-02 vuelve a aplicar
después del restablecimiento).

**API contract**
- Method + path: ninguno nuevo; el restablecimiento no tiene endpoint (FR-05). Se invoca por consola
  como `Optica.Api restablecer-contrasena`
- Request: la confirmación `RESTABLECER` por la entrada estándar
- Response: mensajes por la salida estándar y código de salida 0 (restablecida), 1 (cancelada) o 2
  (falló)
- Error codes: ningún endpoint responde a un pedido de restablecimiento: cualquier ruta
  `/api/acceso/restablecer*` responde 401 sin sesión y 404 con sesión, como toda ruta inexistente
- Auth: solo quien opera la PC del local con la cuenta de la Api, con la Api detenida; sin acceso por
  red

**Data model**
- Reuses `CredencialAcceso` from Block 3 — no schema change; constraints as declared there: una sola
  fila (`Id` = 1, `CK_CredencialAcceso_Id`), `Hash` TEXT NOT NULL; el restablecimiento elimina esa
  fila. Reuses `EventoAcceso` from Block 1 — no schema change; `Tipo` TEXT NOT NULL
  `restablecimiento`, `Resultado` TEXT NOT NULL `exito`.

**Input validation**
- Argumentos: solo `restablecer-contrasena` sin otros argumentos; cualquier otro primer argumento
  levanta la Api como siempre.
- Confirmación: exactamente `RESTABLECER` (sin distinguir espacios alrededor); cualquier otra
  entrada, o el fin de la entrada, cancela.

**Error handling**
- Api en marcha (candado tomado o acceso denegado al candado) — mensaje "La aplicación está en
  marcha. Detenela antes de restablecer la contraseña."; código 2; nada cambia.
- Base inexistente — mensaje "No se encontró la base de datos en <ruta visible>. Ejecutá el comando
  con la cuenta de la Api."; código 2; no se crea nada (ni la base, ni el directorio, ni `-wal` ni
  `-shm`).
- Segunda instancia de la Api sobre el mismo directorio de datos — termina al arrancar con un
  mensaje; la primera sigue.
- Migraciones pendientes — mensaje "Iniciá la aplicación una vez antes de restablecer."; código 2;
  no se migra.
- Confirmación distinta de `RESTABLECER` — mensaje de cancelación; código 1; nada cambia.
- Error de SQLite al confirmar — mensaje con el código de SQLite, sin la excepción ni la ruta
  expandida; código 2; la transacción se revierte.
- Falla al renovar las claves después de confirmar — mensaje que pide repetir el comando; código 2.
- Sin credencial definida — se registra el evento y se renuevan las claves; código 0.

**Required tests**
- [ ] `RestablecerContrasena_EliminaSoloLaCredencialYRegistraEvento` — los demás datos y eventos
      siguen iguales — validates AC-06
- [ ] `RestablecerContrasena_SinCredencial_RegistraEventoYRenuevaClaves`
- [ ] `RestablecerContrasena_ErrorAlConfirmar_NoEliminaNada` — (sad path)
- [ ] `RestablecerContrasena_FallaAlRenovarClaves_Error` — (sad path)
- [ ] `Comando_ConfirmacionCorrecta_SiguienteIngresoPideDefinir` — validates AC-06
- [ ] `Comando_ConfirmacionInvalida_CanceladoSinCambios` — (sad path)
- [ ] `Comando_ApiEnMarcha_ErrorSinCambios` — (sad path)
- [ ] `Comando_BaseInexistente_ErrorSinCrearla` — (sad path)
- [ ] `Comando_MigracionesPendientes_ErrorSinMigrar` — (sad path)
- [ ] `Comando_ErrorDeSqlite_ErrorSinRutaNiExcepcion` — (sad path)
- [ ] `Comando_RenuevaLasClaves_SesionAnteriorNoAutorizada` — una cookie anterior recibe 401
- [ ] `Restablecimiento_NoExisteEnLaApi_NingunEndpointLoOfrece` — validates AC-07 (sad path)
- [ ] `CandadoInstancia_SegundaInstancia_RechazadaHastaQueLaPrimeraLoLibere` — (sad path)
- [ ] `Api_SegundaInstanciaSobreElMismoDirectorio_RechazadaAlArrancar` — (sad path)
- [ ] `FabricaApi_PruebasEnParalelo_ArrancanCadaUnaConSuCandado`

**Completion criterion**
Las quince pruebas pasan, la prueba de arquitectura de FEAT-001a sigue en verde y, después del comando
con `RESTABLECER`, `GET /api/acceso/estado` responde `contrasenaDefinida: false` y los eventos
anteriores siguen en la base.

## Block 6 — Interfaz web: mensaje de bloqueo y cambio de contraseña

**Files**
- `web/src/api/cliente.ts` (modified) — 423 → `bloqueado` con su mensaje; función
  `cambiarContrasena`; el 413 en el campo del formulario que lo envió
- `web/src/componentes/CampoContrasena.tsx` (modified) — etiqueta, nombre y `autocomplete` como
  props
- `web/src/componentes/FormularioContrasena.tsx` (modified) — muestra el mensaje de bloqueo
- `web/src/paginas/CambiarContrasena.tsx` (new) — campos "Contraseña actual" y "Contraseña nueva" y
  errores junto a cada uno; consulta la sesión una vez al montarse
- `web/src/paginas/Inicio.tsx` (modified) — enlace "Cambiar la contraseña" y el mensaje de éxito que
  llega por el estado de la navegación
- `web/src/App.tsx` (modified) — ruta `/cambiar-contrasena`
- `web/src/pruebas/formulario.ts` (modified) — ayudas para varios campos; la comprobación de que
  ninguna contraseña quedó en el navegador
- `web/src/api/cliente.test.ts`, `web/src/App.test.tsx`, `web/src/paginas/Ingreso.test.tsx`,
  `web/src/paginas/Inicio.test.tsx` (modified)
- `web/src/paginas/CambiarContrasena.test.tsx` (new)
- `tests/Optica.Api.Tests/Web/SpaTests.cs` (modified) — el fallback sirve `/cambiar-contrasena`

**Logic**
Implementa la parte visible de FR-01 y FR-03. En Ingreso, un 423 muestra "El acceso está bloqueado
temporalmente. Volvé a intentar en unos minutos." sin vaciar la pantalla. Desde Inicio, el enlace
lleva a `/cambiar-contrasena`; al montarse, la pantalla consulta `GET /api/sesion` una sola vez y, sin
sesión, navega a `/ingreso` con `replace`. Valida en el navegador la nueva (8 a 128 en NFC, igual que
FEAT-001a), envía las dos a `POST /api/acceso/cambiar`, muestra el error del servidor junto al campo
que indica (`contrasenaActual` o `contrasenaNueva`) y, con 204, navega a Inicio con el mensaje "La
contraseña se cambió." en el estado de la navegación. Los campos usan `autocomplete`
`current-password` y `new-password`. Ninguna contraseña queda en `localStorage`, `sessionStorage`, la
URL ni la consola. Sin consultas periódicas.

**API contract**
- Method + path: consume `POST /api/acceso/ingresar` (Block 2), `POST /api/acceso/cambiar` (Block 4)
  y `GET /api/sesion` (FEAT-001a)
- Request: `{ "contrasena": string }`; `{ "contrasenaActual": string, "contrasenaNueva": string }`
- Response: 204
- Error codes: 400, 401, 409, 413, 415, 423, 429, 5xx y error de red, cada uno con su mensaje
- Auth: la cookie de sesión de FEAT-001a (`credentials: 'same-origin'`)

**Input validation**
- Contraseña nueva: 8 a 128 unidades UTF-16 sobre `normalize('NFC')`, sin recortar espacios, antes de
  enviar.
- Contraseña actual: requerida antes de enviar.

**Error handling**
- 423 en ingreso — mensaje de bloqueo temporal junto al formulario; no se navega.
- 400 en cambio — mensaje del servidor junto al campo indicado.
- Contraseña nueva de 7 caracteres — mensaje junto al campo; no se envía.
- Sin sesión al abrir la pantalla, o 401 vacío al enviar — navega a `/ingreso` con `replace`.
- 409 en cambio — mensaje "No hay una contraseña definida. Volvé a ingresar."; enlace al ingreso.
- Error de red o 5xx — mensaje general "No se pudo conectar con el sistema. Reintentá."

**Required tests**
- [ ] `Ingreso_Bloqueado423_MuestraMensajeDeBloqueo` — validates AC-01 (sad path)
- [ ] `CambiarContrasena_Correcta_MuestraExitoEnInicio` — validates AC-04
- [ ] `CambiarContrasena_ActualIncorrecta_RechazadaConMensajeJuntoAlCampo` — validates AC-05 (sad
      path)
- [ ] `CambiarContrasena_Nueva7Caracteres_InvalidaSinEnviar` — (sad path)
- [ ] `CambiarContrasena_SinSesionAlAbrir_NoAutorizadoNavegaAIngreso` — (sad path)
- [ ] `CambiarContrasena_409_ConflictoMuestraMensaje` — (sad path)
- [ ] `CambiarContrasena_ErrorDeRed_MuestraMensajeGeneral` — (sad path)
- [ ] `CambiarContrasena_NingunaContrasenaQuedaEnElNavegador`

**Completion criterion**
Las ocho pruebas pasan, la cobertura de `web/` sigue sobre el 80 % y el fallback de la Api sirve
`/cambiar-contrasena`.

## Block 7 — Sin datos de ARCA en pantalla y pruebas de punta a punta

**Files**
- `tests/Optica.Api.Tests/Acceso/SinDatosArcaTests.cs` (new) — recorre todos los GET registrados que
  devuelven JSON, con sesión, y verifica que ninguna respuesta tiene campos de certificado ni de
  punto de venta
- `web/src/App.test.tsx` (modified) — ninguna pantalla muestra textos de certificado ni de punto de
  venta
- `e2e/tests/acceso.spec.ts` (modified) — cambio de contraseña y mensaje de bloqueo, en orden: el
  cambio antes que el bloqueo, y el bloqueo última porque deja la base bloqueada 5 minutos

**Logic**
Implementa FR-06 (AC-03) y la estrategia de NFR-02. Hoy ninguna pantalla ni respuesta lleva datos de
ARCA; las pruebas lo fijan para que un cambio futuro no los agregue sin que falle algo. La prueba de
Api recorre el `EndpointDataSource` real, se limita a los GET que devuelven JSON (los POST modifican
datos y `/api/marca/logo` es binario) y compara, sin distinguir mayúsculas, los nombres de clave
JSON enteros contra `certificado`, `puntoDeVenta`, `puntoVenta` y `ptoVta`, sin revisar valores libres
como el nombre de la marca (una búsqueda por subcadena daría falsos positivos con "concepto" o
"receptor"); la prueba de la web busca las palabras enteras "certificado" y "punto de venta". La e2e del cambio define una
contraseña, entra, la cambia, sale e ingresa con la nueva; la de bloqueo prueba 5 incorrectas y
comprueba el mensaje de bloqueo con la correcta. El desbloqueo a los 5 minutos se prueba en la Api
con `TimeProvider` (Block 2), no en la e2e.

**API contract**
- Method + path: ningún endpoint nuevo; la prueba consume todos los `GET` registrados que devuelven
  JSON (hoy `GET /api/acceso/estado`, `GET /api/sesion` y `GET /api/marca`)
- Request: sin cuerpo, con la cookie de sesión
- Response: el JSON de cada endpoint, sin claves ni valores de certificado ni de punto de venta
- Error codes: los de cada endpoint, sin cambios
- Auth: con sesión para recorrer también los endpoints protegidos

**Input validation**
- Sin entrada propia: las entradas se validan en Block 2, Block 4 y Block 6.

**Error handling**
- Un GET con sesión que devuelve un campo de certificado o de punto de venta — la prueba falla
  nombrando el endpoint y la clave.
- Una pantalla que muestra un texto de certificado o de punto de venta — la prueba falla nombrando
  la ruta.

**Required tests**
- [ ] `ApiConSesion_NingunGet_DevuelveDatosArca` — validates AC-03 (sad path: falla si aparece el
      dato, rechazado)
- [ ] `Pantallas_NingunaMuestraDatosArca` — validates AC-03 (sad path: falla si aparece el dato,
      rechazado)
- [ ] `acceso.spec.ts: cambia la contraseña e ingresa con la nueva` — validates AC-04
- [ ] `acceso.spec.ts: cinco intentos incorrectos bloquean el ingreso` — validates AC-01 (sad path)

**Completion criterion**
`npm run test:unidad && npm run test:harness` pasa en los proyectos `chrome` y `msedge`, y las dos
pruebas de AC-03 pasan.

## Final verification
- `dotnet build Optica.sln` sin advertencias, `dotnet test Optica.sln` en verde, `web` con
  `npm run test:coverage` sobre el 80 %, `e2e` con `npm run test:unidad && npm run test:harness` en
  `chrome` y `msedge`.
- Las pruebas de FEAT-001a siguen en verde, incluidas `EndpointsProtegidos_SinSesion_NoAutorizado401`,
  `RutasAnonimas_SonExactamenteCinco` y la prueba de arquitectura de la Api.
- `validate_spec.py --tier FEATURE` pasa sobre este documento.
- NFR-02: la versión inmediata anterior de Chrome y Edge se verifica a mano en VERIFY.

## Rollback
Las dos migraciones son aditivas: `IndiceEventoAccesoTipoResultado` (su `Down` elimina el índice) y
`TokenConcurrenciaCredencial` (sin SQL; solo actualiza el snapshot). Volver atrás es revertir las
migraciones y los commits del ticket; el archivo `optica.instancia` que deja el candado no afecta a
FEAT-001a. Los eventos con los textos nuevos
(`cambio_contrasena`, `restablecimiento`, `bloqueado`) quedan en `EventoAcceso`; la versión de
FEAT-001a solo agrega eventos y nunca los lee, así que no la afectan. Una contraseña cambiada sigue
siendo un hash PBKDF2 válido para FEAT-001a.
