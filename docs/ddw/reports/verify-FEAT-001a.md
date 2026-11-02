# Verification FEAT-001a

Ingreso, sesión y marca. Verificación cruzada del módulo completo por `ddw-module-verifier` (un
agente que no escribió el código), contra `docs/ddw/prd/prd-FEAT-001a.md`,
`docs/ddw/specs/spec-FEAT-001a.md`, `docs/ddw/specs/decisions-FEAT-001a.md` y los ADR-001 a
ADR-005. Rama `feat/FEAT-001a-ingreso-sesion-marca`, árbol de trabajo limpio. Ronda 1.

| Field | Value |
|---|---|
| Module | `src/Optica.Domain`, `src/Optica.Application`, `src/Optica.Infrastructure`, `src/Optica.Api`, `web/`, `e2e/` |
| Line coverage | 95.7% |
| Branch coverage | 94.0% |
| Function coverage | 92.7% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet build Optica.sln --no-incremental` — 0 warnings; `dotnet format Optica.sln --verify-no-changes` — clean; `npm run build` (`tsc` estricto + `vite build`) — clean |

Cobertura combinada .NET + web por conteos con rutas normalizadas: líneas 1576/1647, ramas 513/546,
funciones 228/246 (.NET 95,3 / 93,1 / 91,2; web 99,4 / 97,9 / 100). El informe de pruebas
`tests-FEAT-001a.md` declaraba 93,0 / 91,0 / 89,8 porque contó dos veces los archivos de Domain;
la diferencia es a favor.

Comandos de esta verificación: `dotnet test Optica.sln` 468/468; `web` `npm run test:coverage`
62/62; `e2e` `npm run test:unidad` 25/25; `e2e` `npm run test:harness` (Chrome 156.0.8078.79,
Edge 156.0.4314.58): la primera corrida 5/6 y las tres siguientes 6/6; `msedge` con traza, 1 falla
en 6 corridas; `Spa_DirectorioWebFaltante_ApiArrancaY404` 25 veces, 0 fallas.

## Acceptance criteria
- ✅ AC-01 — `Arranque_SinSesion_MuestraIngreso`, `Arranque_RutaDesconocida_VuelveAlArranque`, e2e `sin sesión una ruta protegida va a ingreso` (`web/src/rutas/Arranque.tsx`, `web/src/App.tsx`, política por defecto en `src/Optica.Api/Program.cs`)
- ✅ AC-02 — `EndpointsProtegidos_SinSesion_NoAutorizado401`, `RutasAnonimas_SonExactamenteCinco` (`Program.cs`, `src/Optica.Api/Acceso/RutasAnonimas.cs`): 401 sin `Location` y sin cuerpo en todo endpoint registrado
- ✅ AC-03 — `Estado_SinContrasena_DevuelveNoDefinida`, `Arranque_SinContrasena_MuestraDefinir`, e2e `primera vez define contraseña e ingresa` (`AccesoController`, `ConsultarEstadoAcceso`, `Arranque.tsx`)
- ✅ AC-04 — `DefinirContrasena_De7Caracteres_RechazadaPorInvalida`, `Definir_De7Caracteres_Invalido400ConCampoYAccion`, `DefinirContrasena_7Caracteres_RechazadaConMensajeJuntoAlCampo`, e2e `7 caracteres rechazada con mensaje junto al campo` (`PoliticaContrasena.Evaluar`, `ValidacionContrasena`, `RespuestasValidacion`, `politicaContrasena.ts`, `CampoContrasena.tsx`)
- ✅ AC-05 — `Sesion_60MinutosSinActividad_NoAutorizado`, `Sesion_PedidoIntermedio_RenuevaVencimiento`, `Cliente_401_NoAutorizadoNavegaAIngreso` (`src/Optica.Api/Acceso/RenovacionSesion.cs:AlValidarAsync`, `web/src/api/cliente.ts`)
- ✅ AC-06 — `DefinirContrasena_De8Caracteres_GuardaSoloHash` (revisa columnas y bytes UTF-8/UTF-16 de la base y del WAL), `Log_DefinirEIngresar_NoContieneLaContrasena` (`HasherContrasenaPbkdf2.cs`, `DefinirContrasena`)
- ✅ AC-07 — `Marca_Instalada_DevuelveColoresYLogoDeLaOptica`, `Encabezado_MuestraLogoYAplicaColoresDeMarca`, e2e `primera vez define contraseña e ingresa` (`ProveedorMarcaArchivo.cs`, `MarcaController`, `MarcaProvider.tsx`, `Encabezado.tsx`)
- ⚠️ AC-08 — `Marca_CarpetaFaltante_UsaEjemplo`, `Marca_ColoresInvalidos_UsaEjemplo`, `Marca_LogoFaltanteOGrande_UsaEjemplo`; `/Marca/` ignorada por git y sin ningún objeto en el historial (`git rev-list --all --objects`). WARN: ninguna prueba de interfaz ni e2e renderiza la pantalla de ingreso con la marca de ejemplo; queda cubierto por composición.

## Spec blocks
- ✅ Block 1 — Esqueleto de la solución y persistencia: 4/4 pruebas requeridas presentes y en verde
- ✅ Block 2 — Dominio y casos de uso de la contraseña: 11/11
- ✅ Block 3 — Api de acceso, sesión y protección: 13/13
- ✅ Block 4 — Marca instalada y marca de ejemplo: 8/8
- ✅ Block 5 — Interfaz web: 8/8
- ⚠️ Block 6 — Servir la SPA desde la Api y pruebas de punta a punta: 6/6 presentes; las 3 de la Api pasan siempre; las 3 e2e pasan siempre en `chrome` y de forma intermitente en `msedge` (ver WARN 1)

Las 47 pruebas que nombra la spec existen con su nombre exacto. Los archivos fuera de la lista de la
spec están registrados en `decisions-FEAT-001a.md`.

### Pruebas requeridas por la spec (F-VER-06), todas presentes y en verde

- Block 1 (`tests/Optica.Infrastructure.Tests/Persistencia/PersistenciaTests.cs`): ✅ `Migracion_CreaTablasConRestricciones`, ✅ `DirectorioDatos_Invalido_ImpideArrancar`, ✅ `CredencialAcceso_SegundaFila_RechazadaComoDuplicado`, ✅ `DirectorioDatos_PorDefecto_RutaAbsolutaFueraDelRepositorio`
- Block 2: ✅ `DefinirContrasena_De8Caracteres_GuardaSoloHash`, ✅ `DefinirContrasena_ErrorAlConfirmar_NoGuardaCredencialNiEvento` (`AccesoPersistenciaTests.cs`); ✅ `DefinirContrasena_De7Caracteres_RechazadaPorInvalida`, ✅ `DefinirContrasena_De129Caracteres_RechazadaPorInvalida`, ✅ `DefinirContrasena_YaDefinida_ConflictoSinModificar` (`DefinirContrasenaTests.cs`); ✅ `IngresarConContrasena_SinCredencial_RechazadoSinEvento`, ✅ `IngresarConContrasena_Incorrecta_RechazadaYEventoRegistrado`, ✅ `IngresarConContrasena_Correcta_ExitoYEventoRegistrado` (`IngresarConContrasenaTests.cs`); ✅ `Hasher_HashMalFormado_InvalidoDevuelveFalso`, ✅ `Hasher_MismaContrasena_SalesDistintas` (`HasherContrasenaPbkdf2Tests.cs`); ✅ `PoliticaContrasena_Limites8y128` (`PoliticaContrasenaTests.cs`)
- Block 3: ✅ `EndpointsProtegidos_SinSesion_NoAutorizado401`, ✅ `RutasAnonimas_SonExactamenteCinco`, ✅ `Post_ConTextoPlano_Rechazado415`, ✅ `Cookie_EsHttpOnlySecureSameSiteStrict` (`ProteccionTests.cs`); ✅ `Estado_SinContrasena_DevuelveNoDefinida`, ✅ `Definir_De7Caracteres_Invalido400ConCampoYAccion`, ✅ `Definir_YaDefinida_Conflicto409`, ✅ `Ingresar_ContrasenaIncorrecta_Rechazado401YEventoRegistrado`, ✅ `Ingresar_SinContrasenaDefinida_Conflicto409`, ✅ `Ingresar_ContrasenaFaltante_Invalido400`, ✅ `Ingresar_LimitadorSaturado_Rechazado429` (`AccesoTests.cs`); ✅ `Sesion_60MinutosSinActividad_NoAutorizado`, ✅ `Sesion_PedidoIntermedio_RenuevaVencimiento` (`SesionTests.cs`)
- Block 4 (`tests/Optica.Api.Tests/Marca/MarcaTests.cs`): ✅ `Marca_Instalada_DevuelveColoresYLogoDeLaOptica`, ✅ `Marca_CarpetaFaltante_UsaEjemplo`, ✅ `Marca_ColoresInvalidos_UsaEjemplo`, ✅ `Marca_LogoFaltanteOGrande_UsaEjemplo`, ✅ `Marca_LogoConPuntosPuntos_RechazadoFueraDeCarpeta`, ✅ `Marca_EjemploIlegible_Error500`, ✅ `MarcaLogo_Svg_ConCspSandbox`, ✅ `Marca_Anonima_SinSesionResponde200`
- Block 5 (`web/src`): ✅ `Arranque_SinContrasena_MuestraDefinir`, ✅ `Arranque_SinSesion_MuestraIngreso` (`rutas/Arranque.test.tsx`); ✅ `DefinirContrasena_7Caracteres_RechazadaConMensajeJuntoAlCampo`, ✅ `DefinirContrasena_Error400_InvalidoMuestraMensajeDelServidor` (`paginas/DefinirContrasena.test.tsx`); ✅ `Cliente_401_NoAutorizadoNavegaAIngreso` (`paginas/Inicio.test.tsx`); ✅ `Ingreso_ContrasenaIncorrecta_RechazadaConMensaje`, ✅ `Cliente_ErrorDeRed_MuestraMensajeGeneral` (`paginas/Ingreso.test.tsx`); ✅ `Encabezado_MuestraLogoYAplicaColoresDeMarca` (`componentes/Encabezado.test.tsx`)
- Block 6 (`tests/Optica.Api.Tests/Web/SpaTests.cs`): ✅ `Spa_DirectorioWebFaltante_ApiArrancaY404`, ✅ `Spa_RutaApiInexistente_NoDevuelveIndexError404`, ✅ `Spa_RespuestaConCsp`; e2e (`e2e/tests/acceso.spec.ts`): ✅ `primera vez define contraseña e ingresa`, ✅ `7 caracteres rechazada con mensaje junto al campo`, ✅ `sin sesión una ruta protegida va a ingreso` (en `chrome` siempre; en `msedge` intermitente, WARN 1)

## Tests
- ✅ Sad-path tests: `definir` (7 y 129 caracteres, ya definida, 415, 413), `ingresar` (incorrecta, faltante, sin definir, fuera de política, 429), `salir` (sin sesión, 415), `sesion` (vencida), marca (carpeta faltante, colores inválidos, logo faltante o grande, `..`, ejemplo ilegible), SPA (directorio faltante, `/api` inexistente, path traversal). Excepción menor: constructores de entidades de Domain (WARN 4).
- ✅ Lint / type checker: sin errores (ver tabla). No hay ESLint configurado; el control estático de la web es `tsc` estricto (registrado en decisions).

## Decisiones tomadas en VERIFY

- E2E intermitente en `msedge`: se acepta como WARN (W-VER-03), sin volver a CODE — aprobada por el usuario — 2026-11-02 — VERIFY. La traza muestra que la Api responde en 126 ms y que los clics de Edge en WSL tardan de 1,1 a 2,2 s frente al `expect` de 5 s: es lentitud del navegador en este ambiente, no un defecto del producto.
- NFR-03 (versión inmediata anterior de Chrome y Edge): queda como verificación manual pendiente, a hacer antes de instalar en la óptica — aprobada por el usuario — 2026-11-02 — VERIFY. Hace falta instalar Chrome 155 (Chrome for Testing) y Edge 155, correr `npm run test:harness` contra cada uno y documentar las cuatro combinaciones con su versión exacta. Las versiones estables actuales (Chrome 156, Edge 156) sí están verificadas.

## Warnings

1. W-VER-03 — e2e intermitente en `msedge` (2 de 10 corridas, en pruebas distintas), no reflejado en `tests-FEAT-001a.md`, que informa 6/6. Aceptado por el usuario. Recomendación para un ticket futuro: subir `expect.timeout` en `e2e/playwright.config.ts` o desactivar el gestor de contraseñas de Edge.
2. El informe de pruebas cuenta dos veces los archivos de Domain y declara una cobertura menor que la real.
3. La evidencia TDD no es verificable desde el disco: los informes de rojo a verde de cada bloque están en las revisiones de CODE (`.ddw-work/review-block-*.md` y los informes del implementador), y cada commit junta prueba y código.
4. Los invariantes de `CredencialAcceso` y `EventoAcceso` (enum indefinido, fecha no UTC) no tienen pruebas de entrada inválida (`src/Optica.Domain/Acceso/CredencialAcceso.cs`, `EventoAcceso.cs`; 69 % y 67 % de líneas).
5. La migración `Inicial.Down` no tiene prueba.
6. Funciones de `Optica.Api` al 76,2 % medidas aisladas: miembros de `Stream` de `LimitesDelCuerpo.FlujoLimitado` y código generado de logging. El total combinado supera el piso.
7. W-VER-01 — código muerto: `PoliticaContrasena.Evaluar(string?)` y `EsValida` solo los usan las pruebas; `useMarca()`/`esEjemplo` sin consumidores; envoltorios de la etapa TDD en `e2e/compilacion.test.mjs:12-20`; `FabricaConexion.MismaRuta` sin cobertura.
8. AC-07/AC-08: la e2e verifica los colores solo en Inicio; no hay prueba de interfaz con la marca de ejemplo.
9. W-VER-03 — `Spa_DirectorioWebFaltante_ApiArrancaY404`: 0 fallas en 25 corridas, pero frágil por diseño (`Assert.Single` sobre el log y filtro de Warning con excepción de cualquier categoría).
10. NFR-03 pendiente de verificación manual (ver decisiones de VERIFY).
11. Pendientes menores de decisions: el 404 de la carrera de `index.html` sale sin CSP ni `nosniff`, y su `catch` no tiene prueba.

## Datos personales y sensibles

Sin vulnerabilidades explotables en el código. Riesgos registrados que condicionan la operación con
datos reales (Ley 25.326):

- No hay bloqueo por intentos fallidos (R-03) hasta FEAT-001b: no se cargan datos reales de clientes antes.
- Cerrar sesión no invalida la cookie en el servidor (riesgo aceptado por el usuario).
- EF Core en `Information` registra el SQL sin valores: subirlo a `Warning` antes de los módulos con datos de clientes y nunca a `Debug`; no poner nombre, DNI ni CUIT en mensajes de excepción ni en rutas de la Api.
- Fuera de Windows las claves de Data Protection quedan sin cifrar: la Api productiva corre en Windows con DPAPI.
- Las compilaciones Debug llevan rutas y Source Link del desarrollador (dato personal de un empleado): solo se distribuye Release, controlado por `PublicacionSinRutasTests`.

Result: PASSED
