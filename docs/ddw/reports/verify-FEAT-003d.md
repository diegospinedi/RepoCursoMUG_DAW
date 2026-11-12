# Verification FEAT-003d

Ronda 1 (2026-11-12), sobre `23dfa8d` (el código es el de `9ce3c3c`; `9ce3c3c..23dfa8d` solo agrega el informe de
pruebas y el SAST). La hizo un verificador independiente, `ddw-module-verifier`, sobre el módulo completo. La evidencia
TDD de cada bloque está registrada en `.ddw-work/review-block-1.md` a `review-block-3.md`. La contrasté con el disco y
con `git log origin/master..HEAD` y coincide. Veredicto: PASS con 4 WARN.

| Field | Value |
|---|---|
| Module | Presupuestos: concurrencia, volumen y navegadores (src/Optica.Infrastructure/Persistencia/FabricaConexion.cs; tests/Compartido/EncuentroDeAltas.cs y SiembraPresupuestos.cs; tests/Optica.Infrastructure.Tests/Persistencia/ConcurrenciaPresupuestosTests.cs, SiembraPresupuestosTests.cs y PersistenciaTests.cs; tests/Optica.Api.Tests/Presupuestos/ConcurrenciaPresupuestosApiTests.cs y VolumenPresupuestosTests.cs; e2e/tests/presupuestos-concurrencia.spec.ts; e2e/README.md) |
| Line coverage | 96.8% |
| Branch coverage | 93.3% |
| Function coverage | 96.3% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes`: clean. `dotnet build Optica.sln`: 0 warnings (también 0 en las compilaciones de las corridas del verificador). `npm run lint` (oxlint `--max-warnings 0`): clean. `npm run build` (tsc estricto): clean. `npm run test:lint`: 8/8. Este ticket no toca `web/` |

**Alcance de la cobertura.** El único cambio de producción del ticket es `DefaultTimeout = 30` en
`FabricaConexion.cs:42` (con su comentario en :39-41). La tabla mide .NET completo sin la carpeta
`Persistencia/Migraciones`, a partir de la corrida del cierre de CODE sobre `9ce3c3c`. Uní los cuatro informes de
coverlet con la ruta de cada archivo resuelta completa: una línea cubierta en cualquier informe cuenta como cubierta y
las ramas se toman por línea con el máximo cubierto. Resultados: líneas 4615/4766, ramas 1617/1733, funciones 726/754.
Coinciden exactamente con `docs/ddw/reports/tests-FEAT-003d.md`, que esta vez no cuenta archivos dos veces. Si se
conservan las migraciones escritas a mano y solo se quitan `*.Designer.cs` y el snapshot (el criterio de FEAT-003c), da
96.9% / 93.3% / 96.2% (4853/5010, 1617/1733, 741/770).

`FabricaConexion.cs` por separado: líneas 103/113 (91.2%), ramas 23/30 (76.7%), funciones 17/18 (94.4%). La línea
modificada está cubierta (100% del diff de producción). Las 7 ramas sin cubrir están en `ResolverDirectorio` y
`MostrarRutaPorDefecto` (:69, :94-127): Windows, `XDG_DATA_HOME`, perfil del usuario y ruta relativa. Son de FEAT-001
(`882bc30`, `ed04df3`), dependen del sistema operativo y del entorno, y este ticket no las toca. Ver W-COV en Warnings.

**Suite del cierre de CODE: 2701/2701.** El desglose es .NET 1671 (Domain 334, Application 358, Infrastructure 489,
Api 490), web 825, e2e unidad 71 y e2e 134 (67 en Chrome y 67 en Edge, 18 proyectos). Corridas del verificador sobre
`23dfa8d`:

- `dotnet test tests/Optica.Infrastructure.Tests --filter "FullyQualifiedName~Concurrencia|FullyQualifiedName~Siembra|FullyQualifiedName~TiempoDeEspera"`: 22/22. Incluye las 6 del Block 1, las 2 de siembra del Block 2 (la teoría con sus 8 casos) y 7 pruebas existentes cuyo nombre contiene "Concurrencia".
- `dotnet test tests/Optica.Api.Tests --filter "FullyQualifiedName~ConcurrenciaPresupuestosApi|FullyQualifiedName~VolumenPresupuestos"`: 7/7, en 14 s. Cada prueba de volumen tarda de 0,8 a 1 s.
- Mutación propia en una copia exportada con `git archive` (el repo no se tocó): `EjecutarEnTransaccionAsync` pasa a abrir una transacción diferida en lugar de `BEGIN IMMEDIATE` (`UnidadDeTrabajoEf.cs:60`).
  - `DosAltasSimultaneas_NumerosDistintosYConsecutivos` falla con `TimeoutException`: la segunda alta no espera la escritura y no hay encuentro.
  - `DosAltasSimultaneas_LaSegundaEsperaLaEscritura` también falla, pero en la aserción del inicio de transacción. Esa falla viene de la forma de la mutación (`UseTransaction` no dispara `TransactionStarted`), no del comportamiento.
  - Control sin mutación en la misma copia: 2/2 en verde.

No volví a correr el harness e2e. La evidencia de los 134/134 en Chrome y Edge es la corrida del cierre de CODE y las del
Block 3 (10/10 por navegador con `--repeat-each=5`).

## Acceptance criteria
- ✅ AC-01:
  - Integración: `DosAltasSimultaneas_NumerosDistintosYConsecutivos` (5 vueltas sobre la misma base, números 2k−1 y 2k, ninguna `SqliteException`, encuentro comprobado en cada vuelta) y `DosAltasSimultaneas_LaSegundaEsperaLaEscritura` (con la primera retenida la base está vacía; orden inicio de la primera < señal de base ocupada de la conexión "segunda" < confirmación de la primera < inicio de la segunda; filas finales `1 Ficticiez` y `2 Quimerez`).
  - Api: `DosSesiones_PostSimultaneos_NumerosConsecutivos` (dos cookies distintas, 201 y 201, N+1 y N+2 respecto de un alta previa, GET idéntico al cuerpo de cada alta, exactamente 2 transacciones en el orden esperado).
  - Navegador, en Chrome y Edge: `AC-01: dos sesiones graban a la vez con números consecutivos` (dos contextos, clics con `Promise.all`, números `testigo+1` y `testigo+2`, mensaje "Presupuesto N.º {n} grabado. Estado: Borrador." en cada pantalla, sin alertas, datos al reabrir, `testigo+3` responde 404).
  - Tiempo de espera: `CadenaConexion_TiempoDeEsperaExplicito30` lee la cadena tal cual.
  - Código: `UnidadDeTrabajoEf.EjecutarEnTransaccionAsync` (`BEGIN IMMEDIATE`, :60, ADR-013), la numeración de `AltaPresupuesto.EjecutarAsync` (FEAT-003a) y `FabricaConexion` (`DefaultTimeout = 30`, :42).
- ✅ AC-02:
  - Los proyectos del harness usan `channel: 'chrome'` y `channel: 'msedge'` estables (`e2e/playwright.config.ts:44-45`). `presupuestos-concurrencia.spec.ts` corre en los dos (`chrome-presupuestos-concurrencia` en 5550 y `msedge-presupuestos-concurrencia` en 5559, `e2e/README.md:24,33`) junto a `presupuestos-alta` (18+18), `presupuestos-calculos` (6+6) y `presupuestos-edicion` (7+7) de FEAT-003a, b y c.
  - Harness 134/134 en el cierre de CODE.
  - Los criterios de FEAT-003a, b y c que se prueban solo en .NET los resuelve el servidor y no dependen del navegador, como fija la spec.
  - La versión anterior de cada navegador queda fuera de alcance por D-02.
- ✅ AC-03:
  - `Volumen_Alta_Numero10001MismoResultado`: 201, `Location` `/api/presupuestos/10001`, cuerpo completo con los campos de FEAT-003a y b, Borrador, versión 0, fecha del reloj, línea 1.815 × 5 al 50 % = 907,50 y 4.537,50, GET idéntico, 10.001 filas.
  - `Volumen_ModificarBorrador_MismoResultado`: 200, mismo número y fecha sembrada, versión 1, cliente y línea nuevos, GET idéntico, sin filas nuevas.
  - `Volumen_PasarAFinal_MismoResultado`: 200, Final, versión 1, fecha, cliente, líneas y total sin cambios, 1.001 Finales.
  - `Volumen_ModificarFinal_409Final`: PUT y vuelta a Borrador con 409 `final` y texto fijo; GET, líneas y Finales sin cambios.
  - `Volumen_DosAltasSimultaneas_10001Y10002`: con encuentro forzado.
  - Las cinco pruebas siembran 10.000 presupuestos (9.000 en Borrador y 1.000 en Final) y comprueban los conteos.
  - Código: el mismo de FEAT-003a, b y c, sin cambios. El sembrador `tests/Compartido/SiembraPresupuestos.cs` graba en Borrador y después pasa a Final con `Version + 1`, como la app.

FR-01, NFR-01, NFR-02 y NFR-03 quedan cubiertos por estos tres AC, según la tabla de la spec.

## Spec blocks
- ✅ Block 1: 6/6 pruebas requeridas y todas las tareas implementadas.
  - `DefaultTimeout = 30` en el mismo builder de `CadenaConexion` (:33-43), con el comentario que cita ADR-013 y NFR-01.
  - `EncuentroDeAltas` con `Armar`/`Desarmar` explícitos (`Armar` rechaza un tope ≤ 0), el decorador `ArticulosConEncuentro`, el interceptor `BaseOcupadaAlAbrir` (busy handler que devuelve 0) y `MarcasDeTransaccion`.
  - Enlaces `<Compile Include>` en los dos `.csproj` y el decorador `UltimoNumeroViejo` como clase anidada privada.
  - Desvío documentado en D-07: P/Invoke a `e_sqlite3`, porque SQLitePCLRaw 2.1.12 no expone `sqlite3_busy_handler`. Solo vive en código de prueba.
  - Evidencia TDD: `CadenaConexion_TiempoDeEsperaExplicito30` en rojo ("La cadena no declara 'Default Timeout'") antes del cambio en `FabricaConexion`. Es coherente con el disco: el builder omite las claves con valor por defecto y la prueba lee la cadena cruda. Las otras 5 caracterizan un comportamiento que ya existía (ADR-013 y ADR-015, D-03), así que pasaron sin código nuevo. Se vieron en rojo con sabotajes (sin espera → `database is locked`; `ultimoLeido` 2; `SELECT 1`; sin `Armar`) y el verificador reprodujo uno de forma independiente (ver arriba).
- ✅ Block 2: 9/9 pruebas requeridas. Sembrador en el orden de la spec:
  1. Valida parámetros sin abrir la conexión.
  2. Comprueba que la tabla `Presupuesto` esté vacía.
  3. Siembra 200 artículos forzados a activos.
  4. Lee los artículos sembrados.
  5. Valida cada presupuesto y línea con el dominio.
  6. Inserta en una transacción con `Prepare()` y parámetros, y pasa a Final con `UPDATE … WHERE Estado = 'borrador'`, comprobando 1 fila.

  Mensajes fijos, sin causa adjunta y sin dígitos. `ColeccionVolumen` y `SesionesConEncuentro` registrados por los `reemplazos` de `FabricaApi`. `.github/workflows/ci.yml` sin cambios: la medición no lo pidió y la duración quedó anotada en el informe de pruebas. Desvíos documentados en D-09 (fecha opcional del sembrador, Finales = múltiplos de 10, origen "api" de la señal). Evidencia TDD: sin el archivo nuevo el proyecto no compila; con un esqueleto, 7/9 en rojo por aserción; las 2 de dos sesiones pasaron sin producción (caracterización) y se vieron en rojo con `BEGIN` diferido y `DefaultTimeout=1`.
- ✅ Block 3: 2/2 pruebas requeridas, en `chrome` y `msedge`. Dos contextos (`browser.newContext`); el segundo ingresa después del primero, con cookies propias. Números relativos a un testigo grabado por la Api, independientes del orden y de `--repeat-each`. `e2e/README.md`: 18 proyectos de 5543 a 5560, con `chrome-presupuestos-concurrencia` en 5550. Evidencia TDD (D-11): el bloque no agrega producción. El rojo se obtuvo saboteando las aserciones (4/4 en rojo: Received [2,3] y Received 5) y restaurándolas. Prueba que las aserciones pueden fallar, no que el comportamiento faltara.

**Contraste de la evidencia TDD con git.** El Block 1 se commiteó con sus pruebas y `FabricaConexion` juntos (`1878f6d`).
Los Blocks 2 y 3 solo traen pruebas (`c31b064`, `9ce3c3c`), de acuerdo con los informes, que dicen que no hubo
producción nueva. La historia no guarda la fase en rojo, así que la evidencia es la de los verificadores de cada bloque.
Todas las pruebas nombradas existen en disco. No encontré afirmaciones que no coincidan con él. `git diff
e4338e9..HEAD -- src/ web/` es exactamente 1 archivo con 4 líneas agregadas.

## Tests
- ✅ Sad-path tests:
  - Integración: `Alta_BaseOcupadaMasQueElTiempo_ErrorDeBloqueoSinGrabar` (conexión cruda sin pool con `BEGIN IMMEDIATE`, `DefaultTimeout = 1` en la conexión del contexto, `SqliteException` código 5 sin traducir, sin transacción abierta, sin encabezado ni líneas), `Alta_NumeroDuplicado_ConflictoSinEscritura` (`ConflictoAlConfirmarException` con motivo `Unicidad`, sin `InnerException`, sin el apellido en el texto, encabezados y líneas idénticos) y `Encuentro_SinSegundaAlta_FallaPorTiempo` (`TimeoutException` con `MensajeTopeVencido` en 1 s, sin señal, sin filas; `Armar` rechaza 0 y −1).
  - Api: `DosSesiones_UnaRechazada_NumeroSinHueco` (400 `application/problem+json` solo en `lineas[0]` con el texto fijo, sin el DNI en la respuesta, 404 en N+2 y la siguiente alta toma N+2) y `Volumen_ModificarFinal_409Final`.
  - Sembrador: `Siembra_ParametroInvalido_FallaSinInsertar` (8 casos: cantidad 0 y 10.001, Finales −1 y más que la cantidad, líneas 0 y 6 sobre una cadena que no se puede abrir; base con presupuestos sin artículos nuevos; dato rechazado por el dominio sin presupuestos ni líneas; siempre con mensaje fijo, sin dígitos y sin causa).
  - Navegador: `AC-01: un alta inválida en una sesión no saltea el número de la otra` (DNI de 6 dígitos con `aria-invalid`, mensaje junto al campo, 0 POST, la otra sesión recibe `testigo+1` y la corrección `testigo+2`).
- ✅ Todas las pruebas que lista la spec existen y pasan (17/17).

Pruebas requeridas por la spec, por bloque (todas encontradas con su nombre exacto y en verde):
- ✅ Block 1:
  - `CadenaConexion_TiempoDeEsperaExplicito30`
  - `DosAltasSimultaneas_NumerosDistintosYConsecutivos`
  - `DosAltasSimultaneas_LaSegundaEsperaLaEscritura`
  - `Alta_BaseOcupadaMasQueElTiempo_ErrorDeBloqueoSinGrabar`
  - `Alta_NumeroDuplicado_ConflictoSinEscritura`
  - `Encuentro_SinSegundaAlta_FallaPorTiempo`
- ✅ Block 2:
  - `DosSesiones_PostSimultaneos_NumerosConsecutivos`
  - `DosSesiones_UnaRechazada_NumeroSinHueco`
  - `Volumen_Alta_Numero10001MismoResultado`
  - `Volumen_ModificarBorrador_MismoResultado`
  - `Volumen_PasarAFinal_MismoResultado`
  - `Volumen_ModificarFinal_409Final`
  - `Volumen_DosAltasSimultaneas_10001Y10002`
  - `Siembra_ParametroInvalido_FallaSinInsertar`
  - `Siembra_FinalesComoEnLaApp_TriggersIntactos`
- ✅ Block 3:
  - `AC-01: dos sesiones graban a la vez con números consecutivos`
  - `AC-01: un alta inválida en una sesión no saltea el número de la otra`

## Warnings (no bloquean)
- ⚠️ W-COV (cobertura por archivo): `src/Optica.Infrastructure/Persistencia/FabricaConexion.cs` tiene 23/30 ramas (76.7%), debajo del piso del 80% si se mide por archivo modificado, como en FEAT-003c. No lo cuento como FAIL de F-VER-03 por tres razones: el diff de producción del ticket es una sola asignación sin ramas, cubierta al 100% (:42); las 7 ramas sin cubrir son de FEAT-001 (`882bc30`, `ed04df3`) y dependen del sistema operativo (`OperatingSystem.IsWindows()` en :94, que en Linux nunca es verdadero) o del entorno (`XDG_DATA_HOME` y perfil del usuario en :99-127, `ResolverDirectorio` en :69); y .NET completo está en 96.8/93.3/96.3. Es una decisión de criterio del verificador y queda registrada para que el usuario la confirme. Si se exige el piso por archivo, la deuda es de FEAT-001, no de este ticket.
- ⚠️ Evidencia TDD por caracterización: los AC-01 y AC-03 ya se cumplían con el código existente (ADR-013, ADR-015, D-03), así que sus pruebas de comportamiento nunca estuvieron en rojo contra producción. El rojo previo real es `CadenaConexion_TiempoDeEsperaExplicito30` y 7/9 del Block 2 contra el esqueleto del sembrador. El poder de detección de las pruebas de concurrencia se demostró con mutaciones de producción (`BEGIN` diferido, `DefaultTimeout=1`), una reproducida por este verificador. El Block 3 solo tiene rojo por aserciones saboteadas (D-11). Es coherente con un ticket que prueba un comportamiento existente bajo concurrencia y volumen, y está documentado.
- ⚠️ W-VER-03: `VolumenPresupuestosTests.cs:73` espera la descripción "Armazón acetato 00001" y el precio 1.815 fijos, y `:111` la descripción "Cristal orgánico 00002". Dependen del generador determinista `SiembraCatalogo.Generar`: un cambio en el generador rompe estas pruebas sin que cambie el comportamiento. (`:111` lee el precio de la base, pero no la descripción.)
- ⚠️ W-VER-03 (latente, aceptado en D-09): en la Api, la señal de base ocupada tiene origen "api" para las dos sesiones (`ConcurrenciaPresupuestosApiTests.cs:141`). Lo compensa exigir exactamente 2 transacciones y el orden inicioA < señal < confirmaciónA < inicioB (:181-195).
- W-VER-01: sin código muerto ni `using` sin uso en los archivos nuevos (build con 0 warnings). `MensajeArticulos` y `MensajeFinalNoAplicado` (`tests/Compartido/SiembraPresupuestos.cs:46,52`) son guardas defensivas sin prueba propia, referenciadas solo por sus `throw` (:132, :281).
- W-VER-02: no aplica. El ticket no modifica lógica de negocio (dominio ni aplicación).
- Informativos:
  - `BaseOcupadaAlAbrir.PorHandle` (`tests/Compartido/EncuentroDeAltas.cs:178`) es un diccionario estático compartido por las pruebas del proceso, indexado por el handle nativo. La entrada se quita y el handler se desregistra al cerrar la conexión (:226-239), y cada prueba usa su base y su coordinador, así que no se observan interferencias.
  - El P/Invoke a `e_sqlite3` vive solo en `tests/Compartido` y no se enlaza en `src/` (D-07).
  - `Alta_BaseOcupadaMasQueElTiempo_ErrorDeBloqueoSinGrabar` y `Encuentro_SinSegundaAlta_FallaPorTiempo` esperan 1 s cada una a propósito. El resultado no depende del reloj, solo la duración.
- Proceso:
  - D-08 y D-10: dos ediciones por shell durante CODE (una en un archivo de prueba, otra en `src/`, restaurada con md5). Ninguna quedó en el diff.
  - En esta verificación, una corrida con `-r` (que en `dotnet test` es `--runtime`, no el directorio de resultados) agregó un RuntimeIdentifier falso a cuatro `packages.lock.json` (`src/Optica.Application`, `src/Optica.Domain`, `src/Optica.Infrastructure`, `tests/Optica.Infrastructure.Tests`). Los restauré con `git checkout --` a su contenido de `HEAD`: el árbol estaba limpio antes y quedó limpio después. Nada se commiteó.
- Alerta de datos personales:
  - Todos los datos nuevos son ficticios: DNI de la serie 99.999.9xx (`99999901` a `99999932`, `99.999.900` a `99.999.902`, `999999xx` en el sembrador), correos `@example.com`, teléfonos `00 0000-000x`, apellidos inventados ("Ficticiez", "Quimerez", "Ficticio Uno").
  - Los mensajes del sembrador y del encuentro son fijos, sin valores ni excepción como causa. `Alta_NumeroDuplicado_ConflictoSinEscritura` verifica que el apellido no aparezca en la excepción, y `DosSesiones_UnaRechazada_NumeroSinHueco` que el DNI no aparezca en el 400.
  - La contraseña del e2e (`e2e-presupuestos-concurrencia-1`) solo vale en la base temporal del harness.
  - Siguen pendientes, de tickets anteriores, los DNI y correos con formato real y `Arca__Entorno=Simulado` sin fijar en `e2e/servidor.mjs`. Están diferidos a un ticket aparte en D-31 de FEAT-003b y D-18 de FEAT-003c. Este diff no los introduce.

Result: PASSED
