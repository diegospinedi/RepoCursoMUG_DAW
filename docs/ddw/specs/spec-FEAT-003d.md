# Spec FEAT-003d: Concurrencia, volumen y navegadores

| Field | Value |
|-------|-------|
| Ticket | FEAT-003d |
| PRD | docs/ddw/prd/prd-FEAT-003d.md |
| Tier | FEATURE |
| Date | 2026-11-12 |
| Spec loops | 2 |
| Loops since last human decision | 2 |

## Summary
La numeración y la grabación ya ocurren dentro de una transacción `BEGIN IMMEDIATE` (ADR-013, ADR-015), y
Microsoft.Data.Sqlite 10.0.12 reintenta ese `BEGIN IMMEDIATE` cada 150 ms mientras la base está ocupada, hasta
`DefaultTimeout` (30 s por defecto; `SqliteTransaction` usa `CreateCommand`, que toma `CommandTimeout = DefaultTimeout`).
Esta parte deja ese tiempo de espera explícito en la cadena de conexión y lo demuestra con pruebas: dos altas
simultáneas con encuentro forzado (la primera retenida dentro de su transacción hasta que la conexión de la segunda
señala "base ocupada") dan números distintos y consecutivos sin error de bloqueo, en integración, por la Api y en el
navegador. Un sembrador de pruebas carga 10.000 presupuestos ficticios (en Borrador y después en Final, como en la app)
y las pruebas de volumen repiten por la Api el alta, la modificación, el paso a Final y dos altas simultáneas (D-01,
decisión del usuario). Sin cambios de esquema ni de contrato. Decisiones en `docs/ddw/specs/decisions-FEAT-003d.md`.

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 2, Block 3 |
| NFR-01 | Block 1, Block 2, Block 3 |
| NFR-02 | Strategy: Block 3 agrega `e2e/tests/presupuestos-concurrencia.spec.ts` a los proyectos `chrome` y `msedge` estables del harness, donde ya corren `presupuestos-alta`, `presupuestos-calculos` y `presupuestos-edicion` (FEAT-003a, b y c); la evidencia es una corrida de `npm run test:harness` con los 18 proyectos en verde. Los criterios de FEAT-003a, b y c que se prueban solo en .NET no dependen del navegador (la fecha de otro día con reloj falso, la numeración, los triggers, los límites de importes; ver `e2e/README.md:89-90`): su resultado es el mismo en las dos combinaciones porque lo produce el servidor |
| NFR-03 | Block 2 |

## Dependencies between blocks
Block 2 usa el coordinador de encuentro y el tiempo de espera de Block 1. Block 3 depende de Block 1 (el comportamiento
que prueba en el navegador). Orden: 1 → 2 → 3.

## Block 1 — Tiempo de espera explícito y dos altas simultáneas en integración

**Files**
- `src/Optica.Infrastructure/Persistencia/FabricaConexion.cs` (modified) — `DefaultTimeout = 30` en el mismo
  `SqliteConnectionStringBuilder` de `CadenaConexion` (:33-39), con comentario que cita ADR-013 y NFR-01. Se fija en el
  mismo builder para que `BaseTemporal.LiberarConexiones` (`tests/Optica.Infrastructure.Tests/BaseTemporal.cs:110`) y
  `FabricaApi.LiberarConexiones` (`tests/Optica.Api.Tests/FabricaApi.cs:160`) sigan liberando el pool correcto. La
  fábrica no recibe ningún parámetro nuevo: las pruebas que necesitan otro tiempo lo cambian en la conexión ya creada.
- `tests/Optica.Infrastructure.Tests/Persistencia/PersistenciaTests.cs` (modified) — prueba nueva
  `CadenaConexion_TiempoDeEsperaExplicito30`, junto a `CadenaConexion_ConexionDirecta_ClavesForaneasActivas` (:191-204).
- `tests/Compartido/EncuentroDeAltas.cs` (new) — coordinador singleton con `Armar(tope)` y `Desarmar()` explícitos y
  dos piezas: un decorador scoped de `IArticuloRepository` que envuelve `new ArticuloRepository(contexto)` y, solo con
  el coordinador armado y solo en la primera llamada a `ObtenerAsync` posterior a `Armar`, retiene esa alta (ya dentro
  de su transacción) hasta recibir la señal; y un `DbConnectionInterceptor` que en `ConnectionOpened` registra
  `SQLitePCL.raw.sqlite3_busy_handler` devolviendo 0 (SQLite devuelve SQLITE_BUSY igual que sin handler y
  Microsoft.Data.Sqlite reintenta igual que en producción) y que, con el coordinador armado, da la señal "base ocupada"
  la primera vez que se invoca. La señal es un `TaskCompletionSource` que queda marcado: si la segunda choca con la base
  ocupada antes de que la primera llegue a `ObtenerAsync`, el decorador no retiene a nadie y la superposición igual queda
  probada. Un `DbTransactionInterceptor` registra `TransactionStarted` y `TransactionCommitted` con una marca de orden
  (`Interlocked.Increment`) por contexto, para afirmar el orden. El tope (`TimeSpan`) es un parámetro de `Armar`; vencido,
  la alta retenida falla con un mensaje fijo. Las pruebas desarman el coordinador en un `finally` (el busy handler queda
  en el handle nativo de las conexiones del pool y es inofensivo: devuelve 0, igual que sin handler).
- `tests/Optica.Infrastructure.Tests/Optica.Infrastructure.Tests.csproj` y `tests/Optica.Api.Tests/Optica.Api.Tests.csproj`
  (modified) — `<Compile Include="..\Compartido\EncuentroDeAltas.cs" Link=...>` como `SiembraCatalogo.cs` (:27 y :25).
- `tests/Optica.Infrastructure.Tests/Persistencia/ConcurrenciaPresupuestosTests.cs` (new) — dos `AltaPresupuesto` con
  contextos armados con el patrón `ContextoCon` (`tests/Optica.Infrastructure.Tests/Persistencia/UnidadDeTrabajoTests.cs:407-413`),
  cada uno con su conexión, el decorador y los dos interceptores; incluye como clase anidada privada el decorador de
  `IPresupuestoRepository` de la prueba del número duplicado.

`DatosLocales.CadenaSinCrear` (`src/Optica.Infrastructure/Persistencia/DatosLocales.cs:37-44`) no lleva el cambio: solo
lee para `MigracionesPendientes` y, en WAL, un lector no espera al escritor. El candado de instancia bloquea el archivo
`optica.instancia`, no la base, y no cambia.

**Logic**
Implements FR-01 y NFR-01: con `BEGIN IMMEDIATE`, la segunda alta espera (hasta 30 s) a que la primera confirme y lee el
máximo ya grabado, así toma el número siguiente. La segunda nunca llega al repositorio de artículos mientras la primera
tiene la escritura (su `BEGIN IMMEDIATE` va antes de cualquier lectura, `UnidadDeTrabajoEf.cs:60`), por eso la señal
de espera sale del busy handler de su conexión y no del decorador. Secuencia de la prueba: armar el coordinador; lanzar
la primera alta, que queda retenida en `ObtenerAsync`; lanzar la segunda con `Task.Run` (el `BEGIN IMMEDIATE` de SQLite
bloquea el hilo que lo llama); el busy handler de la segunda señala "base ocupada"; el decorador libera a la primera,
que confirma; la segunda obtiene la escritura y confirma; `Task.WhenAll` con tope de 30 s. La prueba afirma el orden:
la señal de base ocupada ocurrió antes de que la primera confirmara, y la transacción de la segunda empezó después de
esa confirmación. Si la prueba en rojo muestra que hace falta código de producción además del tiempo de espera, se
registra como decisión y se agrega aquí antes de implementarlo.

**Data model**
- Reuses `Presupuesto` y `LineaPresupuesto` de la migración `Presupuestos` y de `CierrePresupuestos` — no schema change;
  constraints as declared there: `Numero` PK sin AUTOINCREMENT (`ValueGeneratedNever`), `Version` NOT NULL con CHECK de
  0 a 2^53−1, `Estado` CHECK `IN ('borrador','final')`, PK de línea `(PresupuestoNumero, Orden)`, FK a `Articulo`,
  triggers de cierre.

**Input validation**
- Sin entrada nueva: las altas usan `DatosPresupuesto` con la validación de FEAT-003a y FEAT-003b
  (`ValidacionPresupuesto`), sin cambios. `Armar` rechaza un tope menor o igual a cero.

**Error handling**
- La base sigue ocupada más allá del tiempo de espera — la `SqliteException` (código 5, SQLITE_BUSY) se propaga sin
  traducir, la transacción revierte y no se graba nada; la Api la convierte en el 500 genérico existente
  (`ManejadorErroresNoControlados.cs:49`). Riesgo aceptado en el PRD (W-PRD-04).
- Número repetido por una lectura vieja del máximo — `ConflictoAlConfirmarException` (motivo `Unicidad`) como fija
  ADR-015, sin escritura.
- El encuentro forzado no se produce dentro del tope — la alta retenida falla con un mensaje fijo, sin quedar colgada.

**Required tests**
- [ ] `CadenaConexion_TiempoDeEsperaExplicito30` — validates NFR-01 (la cadena declara `Default Timeout=30`)
- [ ] `DosAltasSimultaneas_NumerosDistintosYConsecutivos` — validates AC-01: con encuentro forzado, números N y N+1,
  ninguna `SqliteException`; repetida 5 veces sobre la misma base, armando y desarmando el coordinador en cada vuelta
  (2, 4, …, 10 presupuestos)
- [ ] `DosAltasSimultaneas_LaSegundaEsperaLaEscritura` — validates AC-01: mientras la primera está retenida, la base no
  tiene ningún presupuesto nuevo y la segunda señaló base ocupada sin confirmar; después, la señal precede a la
  confirmación de la primera y la transacción de la segunda empieza después de ella
- [ ] `Alta_BaseOcupadaMasQueElTiempo_ErrorDeBloqueoSinGrabar` — sad path: una conexión cruda con `Pooling = false`
  (patrón de `PresupuestoRepositoryTests.cs:87`) retiene la escritura con `BEGIN IMMEDIATE`; el contexto del alta,
  armado con `ContextoCon`, recibe `((SqliteConnection)contexto.Database.GetDbConnection()).DefaultTimeout = 1`; el alta
  falla con SQLITE_BUSY (error, rechazado) y no queda ningún presupuesto nuevo
- [ ] `Alta_NumeroDuplicado_ConflictoSinEscritura` — sad path: un decorador de `IPresupuestoRepository` cuyo
  `ObtenerUltimoNumeroAsync` devuelve N−1 con la fila N ya grabada simula la lectura vieja; la confirmación falla con
  `ConflictoAlConfirmarException` motivo `Unicidad` (duplicado, conflict) y el alta no deja líneas ni encabezado
- [ ] `Encuentro_SinSegundaAlta_FallaPorTiempo` — sad path: con el coordinador armado con tope de 1 s y sin segunda
  alta, la alta retenida falla por timeout con mensaje fijo (error)

**Completion criterion**
Las seis pruebas pasan, `dotnet test tests/Optica.Infrastructure.Tests` sigue en verde y la cadena de conexión declara
`Default Timeout=30`.

## Block 2 — Dos sesiones por la Api y volumen de 10.000 presupuestos

**Files**
- `tests/Compartido/SiembraPresupuestos.cs` (new) — sembrador determinista, en este orden: (1) valida los parámetros
  sin abrir la conexión; (2) comprueba que la tabla `Presupuesto` esté vacía; (3) siembra 200 artículos activos con
  `SiembraCatalogo.Sembrar` sobre `SiembraCatalogo.Generar(200)` con cada artículo `with { Activo = true }` (`Generar`
  deja inactivo uno de cada diez, `tests/Compartido/SiembraCatalogo.cs:59`); (4) lee sus códigos y entidades (no se
  suponen); (5) arma y valida con el dominio (`Presupuesto`, `LineaPresupuesto.DesdeArticulo`) los presupuestos
  numerados 1..n, cada uno con 1 a 5 líneas de esos artículos; (6) recién entonces inserta los presupuestos en una
  transacción con `Prepare()` y parámetros, como `tests/Compartido/SiembraCatalogo.cs`. Un fallo en (5) no inserta
  presupuestos; los artículos de (3) quedan en la base temporal, que la prueba descarta. Todos en `'borrador'` con sus
  líneas y, para los que
  corresponden, `UPDATE Presupuesto SET Estado = 'final', Version = Version + 1 WHERE Numero = $n AND Estado = 'borrador'`
  verificando que cambie 1 fila, igual que la app (`PresupuestoRepository.cs:174-176, 247-250`); los triggers de
  `CierrePresupuestos` rechazan líneas sobre un Final y AGENTS.md exige Borrador y después Final. Datos de cliente
  ficticios (DNI de la serie 99.999.9xx, email `@example.com`, teléfono `00 0000-0000`). Un dato rechazado por el
  dominio falla con mensaje fijo sin adjuntar la excepción como causa (a diferencia de `SiembraCatalogo.cs:147`): el
  dato del cliente podría viajar en ella.
- `tests/Optica.Api.Tests/Optica.Api.Tests.csproj` y `tests/Optica.Infrastructure.Tests/Optica.Infrastructure.Tests.csproj`
  (modified) — enlace `<Compile Include="..\Compartido\SiembraPresupuestos.cs" Link=...>`.
- `tests/Optica.Api.Tests/Presupuestos/ConcurrenciaPresupuestosApiTests.cs` (new) — dos clientes con sesión propia
  (dos ingresos, dos cookies; ADR-001 permite varias sesiones); `EncuentroDeAltas` registrado con el parámetro
  `reemplazos` de `FabricaApi` (`FabricaApi.cs:35`): `RemoveAll<IArticuloRepository>()` + `AddScoped` del decorador, el
  coordinador como singleton y `ConfigureDbContext<OpticaDbContext>(o => o.AddInterceptors(...))` para el busy
  handler. La preparación (artículos por la Api) se hace con el coordinador desarmado.
- `tests/Optica.Api.Tests/Presupuestos/VolumenPresupuestosTests.cs` (new) — en la colección `ColeccionVolumen`
  (`tests/Optica.Api.Tests/Catalogo/VolumenCatalogoTests.cs:19-23`, `DisableParallelization = true`), con el mismo
  registro de `EncuentroDeAltas` para la prueba de dos altas.
- `tests/Optica.Infrastructure.Tests/Persistencia/SiembraPresupuestosTests.cs` (new) — el sembrador respeta numeración,
  estados, versión y triggers.
- `.github/workflows/ci.yml` (modified, solo si la medición lo pide) — el comentario de la duración estimada; el tope de
  45 minutos no cambia (regla de `e2e/ci.mjs:505-514`).

**Logic**
Implements FR-01, NFR-01 y NFR-03: por la Api, dos sesiones graban a la vez con encuentro forzado y reciben números
consecutivos. Con 10.000 presupuestos sembrados (9.000 en Borrador y 1.000 en Final), un alta recibe el número 10.001
con el mismo cuerpo de respuesta que en FEAT-003a y FEAT-003b; la modificación de un Borrador y el paso a Final dan las
mismas respuestas que en FEAT-003c; un Final sigue rechazando cambios (409 `final`); y dos altas simultáneas con
encuentro forzado reciben 10.001 y 10.002.

**Data model**
- Reuses `Presupuesto`, `LineaPresupuesto` y `Articulo` from Block 1 y del catálogo — no schema change; constraints as
  declared there: `Numero` PK, `Version` NOT NULL con CHECK de 0 a 2^53−1, `Estado` CHECK `IN ('borrador','final')`, PK
  de línea `(PresupuestoNumero, Orden)`, FK de línea a `Articulo`, triggers de cierre que impiden insertar líneas en un
  Final.

**Input validation**
- Sembrador: cantidad de presupuestos de 1 a 10.000, cantidad de Finales entre 0 y la cantidad total, 1 a 5 líneas por
  presupuesto, base de presupuestos vacía al empezar; cada presupuesto y línea se valida con el dominio antes de
  insertar.
- Pedidos de la Api: los mismos de FEAT-003a, b y c (`ValidacionPresupuesto`), sin cambios.

**Error handling**
- Siembra inválida — un parámetro fuera de rango falla antes de abrir la conexión; una tabla `Presupuesto` no vacía
  falla antes de sembrar artículos; un dato que el dominio rechaza falla antes de insertar presupuestos. En los tres
  casos el mensaje es fijo, no repite valores ni adjunta la excepción, y no se inserta ningún presupuesto.
- Pedido rechazado de una sesión mientras la otra graba — por un artículo inactivo (D-14 de FEAT-003a, `ArmadoLineas.cs:39`: el rechazo del
  catálogo ocurre dentro de la transacción, así que también espera la escritura), 400 en `lineas[i]` sin escritura: la
  otra sesión recibe el número siguiente sin hueco.
- Cambio sobre un Final con 10.000 presupuestos — 409 `final`, como en FEAT-003c, sin escritura.
- Base ocupada más allá del tiempo de espera — 500 genérico sin datos del pedido (Block 1).

**Required tests**
- [ ] `DosSesiones_PostSimultaneos_NumerosConsecutivos` — validates AC-01 por la Api, con encuentro forzado: los dos 201,
  números N y N+1, el GET de cada uno igual a lo enviado
- [ ] `DosSesiones_UnaRechazada_NumeroSinHueco` — sad path, con encuentro forzado: el pedido con artículo inactivo
  responde 400 (inválido, rechazado) después de esperar la escritura, y el número de la otra sesión no deja hueco
- [ ] `Volumen_Alta_Numero10001MismoResultado` — validates AC-03 (alta con 10.000)
- [ ] `Volumen_ModificarBorrador_MismoResultado` — validates AC-03 (modificación con 10.000)
- [ ] `Volumen_PasarAFinal_MismoResultado` — validates AC-03 (paso a Final con 10.000)
- [ ] `Volumen_ModificarFinal_409Final` — sad path: con 10.000, un Final rechaza la modificación (rechazado, conflict)
  sin escritura
- [ ] `Volumen_DosAltasSimultaneas_10001Y10002` — validates AC-03 con AC-01, con encuentro forzado
- [ ] `Siembra_ParametroInvalido_FallaSinInsertar` — sad path: siembra inválida rechazada con mensaje sin valores
- [ ] `Siembra_FinalesComoEnLaApp_TriggersIntactos` — los Finales sembrados tienen `Version = 1`, los 6 triggers `TR_%`
  de `CierrePresupuestos` siguen en `sqlite_master` y un INSERT de línea sobre un Final sembrado es rechazado

**Completion criterion**
Las nueve pruebas pasan, `dotnet test Optica.sln` sigue en verde y la duración agregada de las pruebas de volumen queda
anotada en el informe de pruebas.

## Block 3 — Dos sesiones en el navegador, Chrome y Edge estables

**Files**
- `e2e/tests/presupuestos-concurrencia.spec.ts` (new) — dos contextos de navegador (`browser.newContext`): el primero
  ingresa (en la base nueva define la contraseña, como `ingresar` en el resto de la suite) y recién después ingresa el
  segundo con la misma contraseña (dos cookies); ayudantes `ingresar` y `crearArticulo` copiados como en el resto de la
  suite (`e2e/tests/presupuestos-edicion.spec.ts:156-213`); datos ficticios. Las dos pruebas del archivo no dependen
  del orden: cada una crea sus artículos y compara números relativos al último grabado.
- `e2e/README.md` (modified) — tabla de proyectos y puertos (:14-33: 18 proyectos de 5543 a 5560;
  `chrome-presupuestos-concurrencia` en 5550, `chrome-presupuestos-edicion` pasa a 5551, Edge de 5552 a 5560), el
  párrafo de puertos (:34-44), la lista `chromium-*` (:184-186), un párrafo del archivo nuevo y la lista de archivos que
  no dependen del orden (:93-97).

**Logic**
Implements FR-01, NFR-01 y NFR-02 en el navegador: las dos sesiones pulsan "Grabar" a la vez (`Promise.all` sobre los
dos clics) y cada pantalla muestra "Presupuesto N.º {n} grabado. Estado: Borrador." con números distintos y
consecutivos; ninguna muestra el mensaje de error general. Al reabrir, cada presupuesto tiene sus datos. NFR-02 se
cumple con la corrida del harness en los proyectos `chrome` y `msedge`, que incluyen los archivos de FEAT-003a, b y c.
El encuentro forzado no existe en el navegador: la superposición real la prueban Block 1 y Block 2; aquí se prueba que la
pantalla de cada sesión responde bien cuando graban juntas.

**Data model**
- Reuses `Presupuesto` y `LineaPresupuesto` from Block 1 — no schema change; constraints as declared there: `Numero` PK,
  `Version` NOT NULL, `Estado` CHECK `IN ('borrador','final')`, triggers de cierre. Cada proyecto del harness usa su base
  temporal.

**Input validation**
- Los campos del alta de FEAT-003a y FEAT-003b, cargados desde el navegador, sin cambios.

**Error handling**
- Una sesión carga un dato inválido mientras la otra graba — la pantalla inválida muestra el mensaje junto al campo y
  no graba; la otra recibe el número siguiente.

**Required tests**
- [ ] `AC-01: dos sesiones graban a la vez con números consecutivos` — validates AC-01 y, por correr en `chrome` y
  `msedge`, AC-02
- [ ] `AC-01: un alta inválida en una sesión no saltea el número de la otra` — sad path: el dato inválido es rechazado
  junto al campo y la numeración queda sin hueco

**Completion criterion**
`npm run test:harness` de `e2e/` pasa con los 18 proyectos (Chrome y Edge estables), incluidos los archivos existentes
de presupuestos, y `npm run test:unidad` sigue en verde.

## Final verification
Los 3 AC del PRD tienen pruebas en verde: AC-01 en integración, Api y navegador; AC-02 con la corrida del harness en
Chrome y Edge estables; AC-03 con 10.000 presupuestos por la Api. `dotnet test Optica.sln`, las pruebas de la web y el
harness pasan; la cadena de conexión declara `Default Timeout=30`; no hay cambios de esquema ni de contrato, y todo dato
sembrado es ficticio.

## Rollback
Sin migraciones ni cambios de esquema: no hay reversión de base. Revertir es revertir los commits; lo único de
producción es `DefaultTimeout = 30` en `FabricaConexion`, que repite el valor por defecto de Microsoft.Data.Sqlite, así
que quitarlo no cambia el comportamiento.
