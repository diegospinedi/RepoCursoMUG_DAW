# Spec FEAT-001c: Configuración de parámetros de negocio

| Field | Value |
|-------|-------|
| Ticket | FEAT-001c |
| PRD | docs/ddw/prd/prd-FEAT-001c.md |
| Tier | FEATURE |
| Date | 2026-11-04 |
| Spec loops | 1 |
| Loops since last human decision | 1 |

## Summary
Una fila única `ConfiguracionNegocio` en SQLite guarda los cuatro parámetros, con los tres numéricos en
centésimos enteros y valores iniciales insertados por la migración (ADR-008). Una política de dominio concentra
las reglas de FR-02 a FR-05 y devuelve todas las infracciones por campo; la entidad la usa para sus invariantes y
la aplicación solo las traduce. Dos casos de uso (consultar y grabar) leen la fila en cada operación, sin caché
(FR-06). La Api expone `GET` y `POST /api/configuracion` con sesión, y la SPA suma la pantalla `/configuracion`.
Las decisiones tomadas en PLAN están en `docs/ddw/specs/decisions-FEAT-001c.md` (D-01 a D-06).

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 3, Block 4, Block 5, Block 6 |
| FR-02 | Block 1, Block 2, Block 3, Block 5 |
| FR-03 | Block 1, Block 3, Block 5 |
| FR-04 | Block 1, Block 3, Block 5 |
| FR-05 | Block 1, Block 3, Block 5 |
| FR-06 | Block 2, Block 3, Block 6 |
| FR-07 | Block 2, Block 6 |
| FR-08 | Block 2, Block 3, Block 5, Block 6 |
| NFR-01 | Strategy: Block 6 corre AC-01 a AC-07 en los proyectos `chrome` y `msedge` de Playwright (canal estable instalado); la versión anterior de cada navegador se verifica a mano, como en FEAT-001a y FEAT-001b |

## Dependencies between blocks
Block 1 → Block 2 → Block 3 → Block 4 → Block 5 → Block 6. Cada bloque usa los tipos del anterior; no hay bloques
independientes.

## Block 1 — Dominio: política y entidad de configuración

**Files**
- `src/Optica.Domain/Configuracion/CondicionFiscal.cs` (new) — enum `ResponsableInscripto`, `Monotributo`.
- `src/Optica.Domain/Configuracion/PoliticaConfiguracionNegocio.cs` (new) — reglas de FR-02 a FR-05 y D-01.
- `src/Optica.Domain/Configuracion/ConfiguracionNegocio.cs` (new) — entidad con los cuatro valores.
- `src/Optica.Domain/Configuracion/IConfiguracionNegocioRepository.cs` (new) — contrato de persistencia.
- `tests/Optica.Domain.Tests/Configuracion/PoliticaConfiguracionNegocioTests.cs` (new)
- `tests/Optica.Domain.Tests/Configuracion/ConfiguracionNegocioTests.cs` (new)

**Logic**
`PoliticaConfiguracionNegocio.Evaluar(decimal? alicuotaIva, string? condicionFiscal, decimal? multiploRedondeo,
decimal? topeIdentificacion)` devuelve la lista de infracciones, una por campo como máximo, con el patrón de
`PoliticaContrasena`: nunca lanza. Por campo, en este orden (la primera que se cumple gana):
- alícuota: ausente → `AlicuotaFaltante`; fuera de 0 a 100 → `AlicuotaFueraDeRango`; más de 2 decimales →
  `AlicuotaConMasDeDosDecimales`.
- condición fiscal: ausente o distinta de `responsable_inscripto` y `monotributo` (comparación exacta) →
  `CondicionFiscalInvalida`.
- múltiplo: ausente → `MultiploFaltante`; menor a 0,01 → `MultiploMenorAlMinimo` (D-01: antes que la escala, así
  0,001 da este); más de 2 decimales → `MultiploConMasDeDosDecimales`; mayor a `ImporteMaximo` → `MultiploFueraDeRango`.
- tope: ausente → válido (D-02, sin valor); menor o igual a 0 → `TopeNoPositivo`; más de 2 decimales →
  `TopeConMasDeDosDecimales`; mayor a `ImporteMaximo` → `TopeFueraDeRango`.
`ImporteMaximo` = `long.MaxValue / 100` truncado a 2 decimales: el mayor importe cuyo valor en centavos entra en un
entero de 64 bits, la representación del modelo (ADR-008). No es un tope de negocio sino el del tipo.
"Más de 2 decimales" se mide como `valor != decimal.Round(valor, 2)`, nunca con `decimal.Scale`: 21.500 y 0.010 llegan
del JSON con escala 3 y son valores válidos.
`ConfiguracionNegocio(decimal alicuotaIva, CondicionFiscal condicionFiscal, decimal multiploRedondeo, decimal?
topeIdentificacion)` evalúa la política y lanza `ArgumentException` (sin el valor en el mensaje) si hay alguna
infracción. Expone los cuatro valores y `FaltaTope` (verdadero si el tope es null). `CondicionFiscal` se traduce a
texto y desde texto solo en `PoliticaConfiguracionNegocio.LeerCondicion` / `TextoDe` (D-07): ese texto es a la vez
el contrato de la Api y el valor de la columna. El constructor rechaza con `ArgumentException` un valor del enum que
no está definido.
`IConfiguracionNegocioRepository`: `Task<ConfiguracionNegocio> ObtenerAsync(CancellationToken)` (lanza
`InvalidOperationException` si no hay fila, D-04) y `void Reemplazar(ConfiguracionNegocio nueva)` (solo registra el
cambio; lo confirma `IUnidadDeTrabajo`).

**Data model**
- Sin esquema propio: la tabla es de Block 2. La entidad tiene alícuota `decimal` (0 a 100, hasta 2 decimales),
  condición `CondicionFiscal` (NOT NULL), múltiplo `decimal` (>= 0,01, hasta 2 decimales), tope `decimal?`
  (NULL o > 0, hasta 2 decimales).

**Input validation**
- alícuota: decimal requerido, 0 ≤ x ≤ 100, escala ≤ 2.
- condición: texto requerido, uno de `responsable_inscripto` o `monotributo`.
- múltiplo: decimal requerido, 0,01 ≤ x ≤ `ImporteMaximo`, escala ≤ 2.
- tope: decimal opcional; si viene, 0 < x ≤ `ImporteMaximo`, escala ≤ 2.

**Error handling**
- Un valor inválido al construir la entidad — `ArgumentException` con el nombre del parámetro y sin el valor.
- Un campo inválido al evaluar — la política devuelve la infracción del campo; no lanza.

**Required tests**
- [ ] `Evaluar_ValoresIniciales_SinInfracciones` — 21, `responsable_inscripto`, 0,01, null; validates AC-06
- [ ] `Evaluar_Alicuota101_Rechazado` y `Evaluar_AlicuotaMenos1_Rechazado` — alícuota rechazada por rango; validates AC-03
- [ ] `Evaluar_AlicuotaBordes0y100y10coma5_Validos` — bordes válidos de FR-03
- [ ] `Evaluar_Alicuota21coma555_Invalido` — valor inválido por escala (`AlicuotaConMasDeDosDecimales`)
- [ ] `Evaluar_Multiplo0coma001_RechazadoPorMinimo` — múltiplo rechazado por el mínimo, no por la escala; validates AC-04
- [ ] `Evaluar_Multiplo0coma015_Invalido` — múltiplo inválido por escala (D-01)
- [ ] `Evaluar_Tope0_Rechazado` y `Evaluar_TopeNegativo_Rechazado` — tope rechazado por no ser positivo; validates AC-05
- [ ] `Evaluar_TopeAusente_Valido` — D-02
- [ ] `Evaluar_CondicionDesconocida_Invalido` — condición inválida para `"Monotributo"` con mayúscula, `""` y `"otro"`; validates AC-02
- [ ] `Evaluar_ImporteSobreElMaximo_Rechazado` — múltiplo y tope rechazados por el rango del tipo
- [ ] `Evaluar_VariosCamposInvalidos_DevuelveUnaInfraccionPorCampo` — tres campos inválidos, una infracción por campo
- [ ] `Constructor_ValorInvalido_LanzaSinIncluirElValor` — error sin el valor en el mensaje
- [ ] `FaltaTope_SinTope_Verdadero` — validates AC-07
- [ ] `Evaluar_CerosALaDerecha_Validos` — 21.500 y 0.010 no son inválidos por escala
- [ ] `Constructor_CondicionNoDefinida_Rechazado` — `(CondicionFiscal)7` rechazado con `ArgumentException`

**Completion criterion**
Las pruebas del bloque pasan con `dotnet test` y `Optica.Domain` sigue sin referencias a otros proyectos.

## Block 2 — Infraestructura: tabla, migración con valores iniciales y repositorio

**Files**
- `src/Optica.Infrastructure/Persistencia/OpticaDbContext.cs` (modified) — `DbSet<ConfiguracionNegocioFila>`, mapeo,
  CHECKs, los `ValueConverter` de `decimal` a centésimos y la clase `ConfiguracionNegocioFila` (las filas viven en
  este archivo, como `CredencialAccesoFila`).
- `src/Optica.Infrastructure/Persistencia/ConfiguracionNegocioRepository.cs` (new)
- `src/Optica.Infrastructure/Persistencia/Migraciones/<fecha>_ConfiguracionNegocio.cs` y `.Designer.cs` (new)
- `src/Optica.Infrastructure/Persistencia/Migraciones/OpticaDbContextModelSnapshot.cs` (modified)
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — registra el repositorio (scoped).
- `tests/Optica.Infrastructure.Tests/Persistencia/ConfiguracionNegocioRepositoryTests.cs` (new)
- `tests/Optica.Infrastructure.Tests/Acceso/AccesoPersistenciaTests.cs` (modified) — la lista exacta de migraciones
  de `Migracion_IndiceTipoResultado_CreadoYRevertible` suma `ConfiguracionNegocio`, y su reversión a la primera
  migración también quita la tabla nueva.

**Logic**
`ConfiguracionNegocioFila` tiene `decimal AlicuotaIva`, `string CondicionFiscal`, `decimal MultiploRedondeo` y
`decimal? TopeIdentificacion`; `OpticaDbContext` los mapea a las columnas en centésimos con un `ValueConverter`
(×100 al guardar, /100 al leer), como hace `FechaUtcIso8601` con las fechas (ADR-008). El repositorio pasa de la fila
a la entidad sin aritmética; la condición se traduce con `PoliticaConfiguracionNegocio.TextoDe`/`LeerCondicion`. La migración crea la tabla y, solo en `Up()`, ejecuta
`InsertData` con `Id = 1`, 2100, `'responsable_inscripto'`, 1, NULL (FR-07, FR-08); sin `HasData` en el modelo
(ADR-008). `Down()` borra la tabla. Así una base nueva y una creada con FEAT-001a o FEAT-001b quedan con la fila al
migrar en el arranque. `ObtenerAsync` lee con `AsNoTracking` por `Id = 1` en cada llamada (FR-06) y lanza
`InvalidOperationException("No hay configuración de negocio.")` si no hay fila (D-04). `Reemplazar` lee la fila con
seguimiento (o reutiliza la entrada local) y le asigna los valores nuevos; sin `ExecuteUpdate`, para que el UPDATE
salga en la transacción de `UnidadDeTrabajoEf`. Sin token de concurrencia: la última grabación gana (D-03).

**Data model**
- Tabla `ConfiguracionNegocio`: `Id` INTEGER PK, `CHECK ("Id" = 1)` (`CK_ConfiguracionNegocio_Id`);
  `AlicuotaIvaCentesimas` INTEGER NOT NULL, `CHECK BETWEEN 0 AND 10000` (`CK_ConfiguracionNegocio_Alicuota`);
  `CondicionFiscal` TEXT NOT NULL, `CHECK IN ('responsable_inscripto','monotributo')`
  (`CK_ConfiguracionNegocio_Condicion`); `MultiploRedondeoCentavos` INTEGER NOT NULL, `CHECK >= 1`
  (`CK_ConfiguracionNegocio_Multiplo`); `TopeIdentificacionCentavos` INTEGER NULL, `CHECK IS NULL OR > 0`
  (`CK_ConfiguracionNegocio_Tope`). Fila inicial: 1, 2100, 'responsable_inscripto', 1, NULL. Sin índices extra.

**Input validation**
- Los valores llegan como `ConfiguracionNegocio` ya validada por Block 1; la base vuelve a controlar rango y
  condición con los CHECK.

**Error handling**
- No hay fila `Id = 1` — `ObtenerAsync` y `Reemplazar` lanzan `InvalidOperationException` (estado corrupto, D-04).
- Un CHECK violado (solo posible con una fila editada a mano o un error de mapeo) — la `DbUpdateException` se
  propaga desde `UnidadDeTrabajoEf` como error, no como conflicto, y no se guarda nada.

**Required tests**
- [ ] `BaseNueva_TieneFilaConValoresIniciales` — 21, Responsable Inscripto, 0,01, sin tope; validates AC-06, AC-07
- [ ] `BaseDeFeat001b_AlMigrar_RecibeLaFila` — migrar hasta `TokenConcurrenciaCredencial`, después hasta la última
- [ ] `Reemplazar_YConfirmar_OtroContextoLeeElValorNuevo` — un segundo alcance lee lo grabado sin reiniciar; validates AC-01
- [ ] `Reemplazar_TopeNull_GuardaNull` — D-02
- [ ] `Insertar_AlicuotaFueraDeRango_RechazadoPorCheck` — SQL directo con 10001 falla por el CHECK
- [ ] `Insertar_CondicionDesconocida_RechazadoPorCheck` — SQL directo con 'otro' falla
- [ ] `Insertar_SegundaFila_RechazadoPorCheck` — `Id = 2` falla
- [ ] `Obtener_SinFila_LanzaError` — fila borrada a mano da error `InvalidOperationException` (D-04)
- [ ] `AgregarInfraestructura_RegistraRepositorioDeConfiguracion`
- [ ] `Migracion_IndiceTipoResultado_CreadoYRevertible` actualizada con las cuatro migraciones

**Completion criterion**
Las pruebas del bloque pasan contra una base temporal y `dotnet ef migrations has-pending-model-changes` no informa
cambios pendientes.

## Block 3 — Aplicación: consultar y grabar la configuración

**Files**
- `src/Optica.Application/Configuracion/ValidacionConfiguracion.cs` (new) — nombres de campo y acciones; traduce
  infracciones a `ErrorValidacion`.
- `src/Optica.Application/Configuracion/ObtenerConfiguracionNegocio.cs` (new)
- `src/Optica.Application/Configuracion/GrabarConfiguracionNegocio.cs` (new)
- `src/Optica.Application/Optica.Application.csproj` (modified) — `Microsoft.Extensions.Logging.Abstractions` 10.0.12
  para el registro de R-04.
- `tests/Optica.Application.Tests/Configuracion/Dobles.cs` (new) — repositorio y unidad de trabajo controlados, con
  el patrón de `tests/Optica.Application.Tests/Acceso/Dobles.cs`.
- `tests/Optica.Application.Tests/Configuracion/GrabarConfiguracionNegocioTests.cs` (new)
- `tests/Optica.Application.Tests/Configuracion/ObtenerConfiguracionNegocioTests.cs` (new)

**Logic**
Campos (los de la Api): `alicuotaIva`, `condicionFiscal`, `multiploRedondeo`, `topeIdentificacion`. Acciones (texto
fijo, sin el valor): alícuota faltante "Ingresá la alícuota de IVA."; fuera de rango "La alícuota de IVA debe estar
entre 0 y 100." (AC-03); escala "Ingresá hasta 2 decimales."; condición "Elegí Responsable Inscripto o Monotributo.";
múltiplo faltante "Ingresá el múltiplo de redondeo comercial."; mínimo "El valor mínimo es 0,01." (AC-04); tope "El
tope debe ser mayor a 0." (AC-05); fuera de rango del tipo "Ingresá un valor menor.".
`ObtenerConfiguracionNegocio.EjecutarAsync` devuelve `ConfiguracionVigente(decimal AlicuotaIva, string
CondicionFiscal, decimal MultiploRedondeo, decimal? TopeIdentificacion, bool FaltaTope)` leyendo el repositorio en
cada llamada. `GrabarConfiguracionNegocio.EjecutarAsync(DatosConfiguracion)` evalúa la política; si hay infracciones
devuelve `Resultado.Invalido` con TODAS, una por campo, sin tocar la base. Si no, lee la vigente (para el registro),
llama a `Reemplazar` con la entidad nueva, confirma con `IUnidadDeTrabajo.ConfirmarAsync` y registra en
`ILogger<GrabarConfiguracionNegocio>`, nivel Information, "Configuración de negocio grabada" con los cuatro valores
anteriores y nuevos y la fecha y hora UTC del `TimeProvider` (R-04, D-06). Devuelve `Resultado.Exito`.

**Data model**
- Reuses `ConfiguracionNegocio` from Block 2 — no schema change; constraints as declared there: `CHECK (Id = 1)`,
  alícuota 0 a 10000 centésimos NOT NULL, condición NOT NULL en sus dos valores, múltiplo >= 1 NOT NULL, tope NULL o > 0.

**Input validation**
- `DatosConfiguracion(decimal? AlicuotaIva, string? CondicionFiscal, decimal? MultiploRedondeo, decimal?
  TopeIdentificacion)`: las reglas de Block 1, aplicadas por `PoliticaConfiguracionNegocio`, una infracción por campo.

**Error handling**
- Datos inválidos — `Resultado.Invalido` con un `ErrorValidacion` por campo inválido; nada se graba ni se registra.
- Sin fila — la `InvalidOperationException` del repositorio se propaga (D-04).
- `ConflictoAlConfirmarException` — no puede ocurrir (sin token de concurrencia, una fila existente); si ocurriera se
  propaga como error.

**Required tests**
- [ ] `Grabar_TopeNuevo_ObtenerDevuelveElNuevo` — validates AC-01
- [ ] `Grabar_Monotributo_ObtenerDevuelveMonotributo` — validates AC-02
- [ ] `Grabar_Alicuota101_RechazadoSinGrabar` — rechazado con la acción de AC-03 en `alicuotaIva`; validates AC-03
- [ ] `Grabar_Multiplo0coma001_RechazadoConMinimo` — rechazado con la acción del mínimo; validates AC-04
- [ ] `Grabar_Tope0_RechazadoMayorACero` — rechazado con la acción del tope; validates AC-05
- [ ] `Grabar_VariosCamposInvalidos_DevuelveTodosLosErrores` — inválido en tres campos a la vez, tres errores
- [ ] `Grabar_TopeAusente_GrabaSinTope` — D-02; `FaltaTope` verdadero; validates AC-07
- [ ] `Grabar_Exitoso_RegistraValoresAnterioresYNuevos` — R-04
- [ ] `Grabar_Invalido_NoRegistraNiConfirma` — con datos inválidos no confirma ni registra
- [ ] `Obtener_SinFila_PropagaError` — sin fila propaga el error (D-04)
- [ ] `Grabar_ConflictoAlConfirmar_SePropaga` — un conflicto de la unidad de trabajo se propaga como error

**Completion criterion**
Las pruebas del bloque pasan con dobles en memoria, sin base, y `Optica.Application` sigue sin depender de EF Core.

## Block 4 — Api: `GET` y `POST /api/configuracion`

**Files**
- `src/Optica.Api/Controllers/ConfiguracionController.cs` (new)
- `src/Optica.Api/Errores/RespuestasValidacion.cs` (modified) — sobrecarga de `CuerpoIlegible` con la acción como
  parámetro y la constante `AccionCuerpoIlegibleConfiguracion` = "No se pudieron leer los valores enviados. Revisalos
  y grabá de nuevo."; `AccionCuerpoIlegible` y la firma que usa `AccesoController` no cambian.
- `src/Optica.Api/Program.cs` (modified) — registra `ObtenerConfiguracionNegocio` y `GrabarConfiguracionNegocio`.
- `src/Optica.Api/appsettings.json` (modified) — `Logging:EventLog:LogLevel` con `Optica.Application.Configuracion`
  en `Information`, para que el registro de cada grabación (R-04) quede en el Registro de eventos de Windows y no
  solo en la consola.
- `tests/Optica.Api.Tests/Configuracion/ConfiguracionApiTests.cs` (new)

**Logic**
El controlador solo traduce HTTP: delega en los casos de uso de Block 3. Sin `[AllowAnonymous]` y fuera de
`RutasAnonimas`: lo protege la política por defecto (R-01). `POST` lleva `[RequestSizeLimit(1024)]`, así
`LimitesDelCuerpo` responde 413 y exige `Content-Type: application/json` (415, R-02). El cuerpo se lee con números
JSON a `decimal?` y la condición como `string?` (no como enum, para que un texto desconocido sea un error del campo).
Un cuerpo que no se puede leer (JSON roto, un texto donde va un número, un número que no entra en `decimal`)
responde 400 con un solo error en el campo que nombra `ModelState` (o `alicuotaIva` si ninguno) y la acción
genérica; los errores de validación responden 400 con todos los campos.

**API contract**
- Method + path: `GET /api/configuracion`
- Request: sin cuerpo; cookie de sesión.
- Response: 200 `{ alicuotaIva: number, condicionFiscal: "responsable_inscripto" | "monotributo", multiploRedondeo:
  number, topeIdentificacion: number | null, faltaTope: boolean }`.
- Error codes: 401 sin sesión; 500 genérico sin fila (D-04).
- Auth: sesión de FEAT-001a (política por defecto).
- Method + path: `POST /api/configuracion`
- Request: `{ alicuotaIva: number, condicionFiscal: string, multiploRedondeo: number, topeIdentificacion: number |
  null }`, `Content-Type: application/json`, hasta 1024 bytes.
- Response: 200 con el mismo cuerpo que el `GET`, leído después de grabar (D-03).
- Error codes: 400 ValidationProblemDetails con `errors.{campo}`; 401 sin sesión; 413; 415; 500 genérico.
- Auth: sesión de FEAT-001a (política por defecto).

**Input validation**
- Tamaño del cuerpo hasta 1024 bytes; `Content-Type` JSON; tipos: tres números o null y un texto o null; el resto
  lo valida el caso de uso (Block 3).

**Error handling**
- Sin sesión — 401 antes de leer el cuerpo.
- Content-Type que no es JSON — 415.
- Cuerpo mayor a 1024 bytes — 413.
- Cuerpo ilegible — 400 con un error y la acción genérica, sin los mensajes del lector de JSON ni el valor.
- Valores inválidos — 400 con todos los campos inválidos.
- Sin fila — 500 genérico por `ManejadorErroresNoControlados`, sin traza.

**Required tests**
- [ ] `Get_ConSesion_DevuelveValoresIniciales` — validates AC-06, AC-07
- [ ] `Post_TopeNuevo_GetPosteriorLoDevuelve` — validates AC-01
- [ ] `Post_Monotributo_GetPosteriorLoDevuelve` — validates AC-02
- [ ] `Post_Alicuota101_Rechazado400EnElCampo` — rechazado con 400 en `alicuotaIva`; validates AC-03
- [ ] `Post_Multiplo0coma001_Rechazado400EnElCampo` — rechazado con 400 en `multiploRedondeo`; validates AC-04
- [ ] `Post_Tope0_Rechazado400EnElCampo` — rechazado con 400 en `topeIdentificacion`; validates AC-05
- [ ] `Post_VariosInvalidos_400ConTodosLosCampos` — valores inválidos en tres campos, 400 con los tres
- [ ] `GetYPost_SinSesion_NoAutorizado401` — sin sesión, no autorizado con 401
- [ ] `Post_SinContentTypeJson_Rechazado415` — Content-Type de texto rechazado con 415
- [ ] `Post_CuerpoGrande_Rechazado413` — cuerpo de 2 KB rechazado con 413
- [ ] `Post_CuerpoIlegible_Invalido400SinDetallesInternos` — JSON roto y texto en un número: inválido, 400 sin detalles internos
- [ ] `Get_SinFila_Error500Generico` — sin fila, error 500 genérico (D-04)
- [ ] `Appsettings_GrabacionDeConfiguracion_VaAlRegistroDeEventos` — la categoría queda en Information para EventLog

**Completion criterion**
Las pruebas del bloque pasan, y siguen pasando sin cambios `EndpointsProtegidos_SinSesion_NoAutorizado401`,
`RutasAnonimas_SonExactamenteCinco` y las de `SinDatosArcaTests`, que recorren los endpoints nuevos.

## Block 5 — Web: pantalla de configuración

**Files**
- `web/src/configuracion/numeros.ts` (new) — `leerDecimal(texto)` y `mostrarDecimal(valor)`.
- `web/src/configuracion/numeros.test.ts` (new)
- `web/src/configuracion/reglas.ts` (new) — las reglas de Block 1 y los mensajes de Block 3, para validar en el navegador.
- `web/src/configuracion/reglas.test.ts` (new)
- `web/src/api/cliente.ts` (modified) — `consultarConfiguracion`, `grabarConfiguracion` con `campo413`
  `alicuotaIva` y mensaje propio, y un lector de TODOS los errores de `errors` usado solo por `grabarConfiguracion`
  (`traducirError`, `primerError` y el tipo `Resultado` que usan las pantallas de acceso no cambian).
- `web/src/api/cliente.test.ts` (modified)
- `web/src/paginas/Configuracion.tsx` (new)
- `web/src/paginas/Configuracion.test.tsx` (new)
- `web/src/App.tsx` (modified) — ruta `/configuracion`.
- `web/src/App.test.tsx` (modified) — `PANTALLAS` suma `/configuracion` con sus pedidos falsos y su título.
- `web/src/paginas/Inicio.tsx` (modified) — enlace "Configuración".
- `web/src/paginas/Inicio.test.tsx` (modified) — prueba del enlace.
- `web/src/estilos.css` (modified) — solo si el aviso o el selector necesitan estilo; sin estilos en línea (CSP).

**Logic**
Al montarse consulta `GET /api/configuracion`; un 401 lleva a `/ingreso` como las demás pantallas. Muestra cuatro
campos con etiqueta: "Alícuota de IVA (%)", "Condición fiscal" (selector con Responsable Inscripto y Monotributo),
"Múltiplo de redondeo comercial ($)" y "Tope de identificación del receptor ($)". Mientras el tope guardado no tenga
valor muestra el aviso "Falta cargar el tope de identificación del receptor." con `role="status"` (FR-08).
`leerDecimal` acepta dígitos con una coma o un punto decimal, sin separador de miles ni signo más; un texto vacío es
"sin valor"; cualquier otra cosa es "no es un número" con el mensaje "Ingresá un número.". Al grabar valida con
`reglas.ts`, muestra cada error junto a su campo (`aria-invalid` y `aria-describedby`) y no envía si hay alguno. Los
errores del servidor van junto al campo que indica `errors`. Con 200 reemplaza los valores por los que devolvió la Api
y muestra "La configuración se grabó." con `role="status"`. Los valores viven solo en el estado del componente; sin
consultas periódicas.

**API contract**
- Method + path: consume `GET /api/configuracion` y `POST /api/configuracion` de Block 4, sin cambiarlos.
- Request: `POST` con `{ alicuotaIva: number, condicionFiscal: string, multiploRedondeo: number, topeIdentificacion: number | null }`.
- Response: 200 `{ alicuotaIva, condicionFiscal, multiploRedondeo, topeIdentificacion, faltaTope }`.
- Error codes: 400 con `errors.{campo}`, 401, 413, 429, 5xx.
- Auth: la cookie de sesión que maneja el navegador (`credentials: 'same-origin'`).

**Input validation**
- Cada campo numérico: texto hasta 32 caracteres (`maxLength`), formato de `leerDecimal`, y las reglas de Block 1.
- Importes: hasta `Number.MAX_SAFE_INTEGER / 100`, el mayor que un `number` de JavaScript guarda con los centavos
  exactos; por encima, "Ingresá un valor menor." y no envía.
- Condición: solo las dos opciones del selector.

**Error handling**
- Texto que no es un número — "Ingresá un número." junto al campo; no envía.
- Valor fuera de las reglas — el mensaje de Block 3 junto al campo; no envía.
- 400 del servidor — cada mensaje de `errors` junto a su campo.
- 401 — navega a `/ingreso`.
- Sin conexión, 413, 429 o 5xx — mensaje general con `role="alert"`; los valores escritos se conservan.

**Required tests**
- [ ] `leerDecimal_ComaYPunto_Validos` — "10,5" y "10.5" dan 10,5
- [ ] `leerDecimal_Texto_Invalido` — "abc", "1.000,5" y "+3" son inválidos
- [ ] `reglas_ImporteSobreElMaximoSeguro_Rechazado` — un importe sobre `Number.MAX_SAFE_INTEGER / 100` rechazado
- [ ] `Configuracion_PrimeraVez_MuestraValoresIniciales` — validates AC-06
- [ ] `Configuracion_SinTope_MuestraAviso` — validates AC-07
- [ ] `Configuracion_GrabaTope_MuestraValorGuardado` — validates AC-01
- [ ] `Configuracion_GrabaMonotributo_MuestraMonotributo` — validates AC-02
- [ ] `Configuracion_Alicuota101_RechazadoJuntoAlCampo` — rechazado sin enviar; validates AC-03
- [ ] `Configuracion_Multiplo0coma001_RechazadoJuntoAlCampo` — rechazado sin enviar; validates AC-04
- [ ] `Configuracion_Tope0_RechazadoJuntoAlCampo` — rechazado sin enviar; validates AC-05
- [ ] `Configuracion_NoNumero_Invalido` — "abc" en la alícuota es inválido
- [ ] `Configuracion_Error400DelServidor_MuestraCadaCampo` — 400 inválido en dos campos
- [ ] `Configuracion_SinSesion401_NavegaAlIngreso` — no autorizado (401) navega al ingreso
- [ ] `Configuracion_SinConexion_ErrorGeneralConservaValores` — error de conexión y 500: mensaje general, conserva los valores
- [ ] `grabarConfiguracion_400_DevuelveTodosLosErrores` — 400 con dos errores devuelve los dos, en `cliente.test.ts`
- [ ] `Inicio_EnlaceConfiguracion` — en `Inicio.test.tsx`

**Completion criterion**
`npm run test:coverage` pasa con el umbral de 80 % y `npm run build` compila sin errores de tipos.

## Block 6 — E2E: AC-01 a AC-07 en Chrome y Edge

**Files**
- `e2e/tests/configuracion.spec.ts` (new)
- `e2e/playwright.config.ts` (modified) — un proyecto por navegador y por archivo de pruebas (`testMatch`), cada uno
  con su Api y su base (D-05); los nombres de navegador `chrome` y `msedge` siguen identificando el canal.
- `e2e/servidor.mjs` (modified) — acepta los nombres de instancia nuevos y levanta una Api por instancia.
- `e2e/compilacion.mjs` y `e2e/compilacion.test.mjs` (modified, si la lectura de instancias se mueve ahí para probarla)
- `e2e/README.md` (modified) — describe una Api y una base por navegador y por archivo.

**Logic**
Con su base nueva, el archivo corre en serie: define la contraseña e ingresa; AC-06 (valores iniciales) y AC-07
(aviso) primero; después AC-03, AC-04 y AC-05 (rechazos junto al campo, sin grabar); AC-02 (Monotributo, volver a
abrir); y AC-01 (grabar un tope, el aviso desaparece, volver a abrir y ver el valor; un segundo `GET` desde la
página muestra el mismo valor sin reiniciar la Api). `acceso.spec.ts` no cambia y sigue con su propia base.

**Input validation**
- Las mismas entradas de los AC, escritas en los campos como lo haría la operadora ("10,5", "0,001", "0").

**Error handling**
- Una Api que no arranca — `servidor.mjs` termina con error y Playwright no corre las pruebas.
- Un nombre de instancia mal formado — `servidor.mjs` lanza "OPTICA_E2E_INSTANCIAS mal formada" como hoy.

**Required tests**
- [ ] `configuracion: primera vez muestra 21, Responsable Inscripto y 0,01` — validates AC-06
- [ ] `configuracion: sin tope muestra el aviso` — validates AC-07
- [ ] `configuracion: alícuota 101 y -1 rechazado junto al campo` — validates AC-03
- [ ] `configuracion: múltiplo 0,001 rechazado con el mínimo` — validates AC-04
- [ ] `configuracion: tope 0 rechazado junto al campo` — validates AC-05
- [ ] `configuracion: Monotributo queda vigente al volver a abrir` — validates AC-02
- [ ] `configuracion: el tope grabado se ve al volver a abrir y sin reiniciar` — validates AC-01
- [ ] `instancias: nombre mal formado rechazado` — en `compilacion.test.mjs` si la lectura se mueve ahí
- [ ] `servidor: una Api que no arranca da error` — `servidor.mjs` termina con código distinto de 0 y Playwright no corre

**Completion criterion**
`npm run test:unidad && npm run test:harness` pasa en los proyectos de Chrome y Edge, con `acceso.spec.ts` y
`configuracion.spec.ts` cada uno contra su base.

## Final verification
`dotnet build Optica.sln` y `dotnet test Optica.sln` pasan; en `web/`, `npm run build` y `npm run test:coverage`
pasan; en `e2e/`, `npm run test:unidad && npm run test:harness` pasa en Chrome y Edge. Una base de FEAT-001b migrada
muestra los valores iniciales en `/configuracion`, y `validate_spec.py --tier FEATURE` pasa sobre este documento.
