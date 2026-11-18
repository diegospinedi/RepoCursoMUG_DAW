# Verification FEAT-013

Búsqueda con sugerencias mientras se escribe. **Ronda 2 de VERIFY**, sobre el commit del re-cierre de CODE `2033e61`.

**Ronda 1.** Está versionada en el commit `411fa62`, sobre `0087924`, y quedó BLOCKED:
- AC-14 pide AC-01 a AC-12 en Chrome y Edge, y AC-09 y AC-10 solo tenían prueba en Vitest.
- El corrective loop a CODE sumó tres e2e en el commit `0fc3ec9` y rehízo el cierre: suite 4870/4870 y SAST PASSED.
- Desde la ronda 1 no cambió código de producción (`git diff 0087924..HEAD -- web/src src tests` vacío).

**Contexto de la verificación:**
- **Verificador:** ddw-module-verifier, un agente que no escribió el código.
- **Fuentes contrastadas:**
  - `docs/ddw/prd/prd-FEAT-013.md` (14 AC);
  - `docs/ddw/specs/spec-FEAT-013.md` (5 bloques);
  - `docs/ddw/specs/decisions-FEAT-013.md` (D-01 a D-25);
  - `docs/ddw/security/sast-FEAT-013.md`;
  - `docs/ddw/reports/tests-FEAT-013.md` (4870/4870).
- **Lo que volvió a correr el verificador:**
  - Playwright `chrome-busqueda-en-vivo` y `msedge-busqueda-en-vivo`: 22/22.
  - Las pruebas requeridas de Vitest: 885/885 en 10 archivos.
  - Las .NET requeridas: 2/2.

| Field | Value |
|---|---|
| Module | `web/src/componentes` (useBusquedaEnVivo, ListaSugerencias, CampoBusqueda, CampoConOpciones), `web/src/api/cliente.ts`, `web/src/catalogo` (sugerencias, reglas), `web/src/paginas` (Articulos, BuscarPresupuestos, BuscarFacturas, Configuracion), `web/src/presupuestos/BuscadorArticulos.tsx`, `e2e/tests` (busqueda-en-vivo y los 10 specs de D-18), `tests/Optica.Api.Tests/Catalogo` (BusquedaCancelada, VolumenCatalogo) |
| Line coverage | 99.17% |
| Branch coverage | 97.55% |
| Function coverage | 100% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `npm run lint` (oxlint) — clean; `tsc --noEmit` — clean; `npm run build` — clean; `npm run test:lint` 8/8; `dotnet format --verify-no-changes` — clean |

**Cobertura.** La de la tabla es la de toda la web, y no cambió desde la ronda 1. Todos los archivos del ticket superan
el 80 % en líneas, ramas y funciones. El que tiene menos ramas es `Configuracion.tsx`, con 90,74 %. Ningún archivo de
lógica queda entre 80 y 90 % (W-VER-02 ✅).

## Acceptance criteria
- ✅ AC-01 — `Articulos_EscribirArm_SugerenciasYTablaFiltrada`; e2e `sugiere y filtra artículos y abre la edición` (`web/src/paginas/Articulos.tsx`)
- ✅ AC-02 — `Articulos_UnCaracter_SinSugerencias`, `useBusquedaEnVivo_UnPedidoPorPausaYMinimoDosCaracteres`; en la e2e dentro de `Buscar sigue funcionando` (ver H-10)
- ✅ AC-03 — `Articulos_ElegirSugerencia_AbreLaEdicion`; e2e `sugiere y filtra artículos y abre la edición`
- ✅ AC-04 — `BuscarPresupuestos_EscribirApellido_SugerenciasYAbrir155`; e2e `sugiere presupuestos y facturas y los abre`
- ✅ AC-05 — `BuscarFacturas_EscribirApellido_SugerenciasYAbrirLaFactura`; e2e `sugiere presupuestos y facturas y los abre`
- ✅ AC-06 — `NuevoPresupuesto_ElegirSugerencia_AgregaLaLinea`; e2e `agrega la sugerencia al presupuesto`
- ✅ AC-07 — `ListaSugerencias_TecladoFlechasEnterEscapeTab`; e2e `teclado y anuncio de sugerencias`
- ✅ AC-08 — `Articulos_Buscar_ComoAntes`; e2e `Buscar sigue funcionando`
- ✅ AC-09 — `useBusquedaEnVivo_RespuestaViejaDescartada`, `BuscarPresupuestos_RespuestaViejaNoPisaLaNueva`; e2e `respuesta vieja no pisa la nueva` en Chrome y Edge (la búsqueda nueva cancela la vieja retenida, `ERR_ABORTED`; tabla y sugerencias quedan las de lo último escrito)
- ✅ AC-10 — `useBusquedaEnVivo_ErrorDeRed_SinSugerencias`, `BuscarFacturas_ErrorDeBusqueda_SinSugerencias`; e2e de la Api con 500 y e2e de red con `route.abort` en Chrome y Edge: mensaje de hoy, lista cerrada y foco en el campo
- ✅ AC-11 — `Configuracion_Alicuota10_SoloOpcion105`; e2e `configuración filtra la alícuota y rechaza una condición inexistente`
- ✅ AC-12 — `Configuracion_CondicionInexistente_RechazadaSinGrabar`; misma e2e
- ✅ AC-13 — e2e `un pedido por pausa y sugerencias en menos de un segundo`; `Volumen_BuscarArmazon_P95MenorA700ms`
- ✅ AC-14 — AC-01 a AC-12 corren en Chrome y Edge estables, con teclado y con el anuncio de la cantidad (22/22 en `chrome-busqueda-en-vivo` y `msedge-busqueda-en-vivo`; harness 264/264)

## Spec blocks
- ✅ Block 1 — 7/7 tareas; D-16 cubierta sin cambio de backend
- ✅ Block 2 — 2/2 tareas (D-19)
- ✅ Block 3 — 3/3 tareas (D-12, D-20 a D-23)
- ✅ Block 4 — 3/3 tareas (D-24)
- ✅ Block 5 — 4/4 tareas, más las 3 e2e del corrective loop (D-25)
- ✅ Final verification — suite completa en verde, los 14 AC con prueba, ningún `package.json` ni `.csproj` cambiado, sin cambios de esquema ni de migraciones

## Tests
Las 36 pruebas requeridas por la spec existen con su nombre exacto y pasan:
- ✅ `cliente_BusquedaCancelada_DevuelveCanceladoNoError`
- ✅ `useBusquedaEnVivo_UnPedidoPorPausaYMinimoDosCaracteres`
- ✅ `useBusquedaEnVivo_RespuestaViejaDescartada`
- ✅ `useBusquedaEnVivo_EnviarAhoraCancelaLaPausa`
- ✅ `useBusquedaEnVivo_ErrorDeRed_SinSugerencias`
- ✅ `ListaSugerencias_TecladoFlechasEnterEscapeTab`
- ✅ `ListaSugerencias_AriaActivedescendantYAnuncioSoloCantidad`
- ✅ `ListaSugerencias_HtmlEnSugerenciaSeVeComoTexto`
- ✅ `CampoConOpciones_FiltraPlegadoYFlechaAbreLista`
- ✅ `plegar_AcentosMayusculasYEnie`
- ✅ `BusquedaCancelada_NoRegistraErrorNoControlado`
- ✅ `Articulos_EscribirArm_SugerenciasYTablaFiltrada`
- ✅ `Articulos_UnCaracter_SinSugerencias`
- ✅ `Articulos_ElegirSugerencia_AbreLaEdicion`
- ✅ `Articulos_Buscar_ComoAntes`
- ✅ `Articulos_ErrorDeBusqueda_SinSugerencias`
- ✅ `NuevoPresupuesto_ElegirSugerencia_AgregaLaLinea`
- ✅ `NuevoPresupuesto_SugerenciaConPrecioNegativo_Rechazada`
- ✅ `BuscarPresupuestos_EscribirApellido_SugerenciasYAbrir155`
- ✅ `BuscarPresupuestos_RespuestaViejaNoPisaLaNueva`
- ✅ `BuscarPresupuestos_SugerenciaSinDni`
- ✅ `BuscarPresupuestos_RangoInvalidoEnVivo_SinPedido`
- ✅ `BuscarFacturas_EscribirApellido_SugerenciasYAbrirLaFactura`
- ✅ `BuscarFacturas_ErrorDeBusqueda_SinSugerencias`
- ✅ `Configuracion_Alicuota10_SoloOpcion105`
- ✅ `Configuracion_CondicionInexistente_RechazadaSinGrabar`
- ✅ `Configuracion_AlicuotaFueraDeLista_CampoVacioYAviso`
- ✅ `sugiere y filtra artículos y abre la edición`
- ✅ `sugiere presupuestos y facturas y los abre`
- ✅ `agrega la sugerencia al presupuesto`
- ✅ `teclado y anuncio de sugerencias`
- ✅ `un pedido por pausa y sugerencias en menos de un segundo`
- ✅ `configuración filtra la alícuota y rechaza una condición inexistente`
- ✅ `Buscar sigue funcionando`
- ✅ `Volumen_BuscarArmazon_P95MenorA700ms`
- ✅ `sin violaciones de CSP ni error en la consola`

Las 3 e2e del corrective loop se suman a esta lista: `respuesta vieja no pisa la nueva`,
`una falla de la búsqueda oculta las sugerencias` y `una falla de red oculta las sugerencias`.

- ✅ Sad-path tests (F-VER-04): cada función o pantalla que recibe entrada tiene prueba con entrada inválida o con
  falla. Ejemplos: pedido cancelado, texto de 1 carácter, texto inválido, rango de fechas inválido, condición y
  alícuota inexistentes, precio negativo, HTML en una sugerencia, 400 en vivo, 500 y red caída, estos dos últimos
  también en la e2e.
- ✅ Lint y tipos (F-VER-05): limpios.
- ✅ Código muerto (W-VER-01): sin imports sin uso ni código comentado.
- ⚠️ Pruebas frágiles (W-VER-03): ver H-1 y H-2.

## Hallazgos

Ninguno bloquea. De la ronda 1 se cerraron dos: H-3, porque AC-09 y AC-10 ya corren en Chrome y Edge, y H-5, porque
el SAST ya ubica bien `CampoBusqueda.tsx`.

- **H-1 (W-VER-03, aceptado en D-25):** `busqueda-en-vivo.spec.ts` depende del orden y lo declara con modo serie. Las
  pruebas nuevas esperan 2 sugerencias exactas para "visor", "visi" y "vitr", y hoy no chocan con otros datos.
- **H-2 (W-VER-03, aceptado en D-25):** la e2e de AC-13 y la prueba de volumen dependen de los tiempos de la máquina.
  Las pruebas nuevas no suman tiempos fijos: esperan eventos del pedido y cuadros del navegador.
- **H-4 (evidencia TDD):** la evidencia está en las revisiones por bloque de CODE (`.ddw-work/review-block-1.md` a
  `-5.md`), fuera de git. Para las e2e del corrective loop, el rojo se obtuvo con las mutaciones D y E, revertidas.
  - Mutación D: aparece de más "3 · SG-VE1 · Visera de respuesta 1".
  - Mutación E: el listbox queda visible.
- **H-6 (cobertura menor):** no se ejerce `BuscarFacturas.tsx:281`, la rama de un 400 con un solo campo en el envío
  manual. Es lógica anterior al ticket.
- **H-7 (aceptado en D-21):** la lógica en vivo está duplicada entre `BuscarPresupuestos.tsx` y `BuscarFacturas.tsx`.
- **H-8 (datos personales, informativo, ya informado al usuario):**
  - Vaciar el último campo de texto busca sola la primera página.
  - Las listas ocultas de los campos sin foco conservan apellido y nombre en el DOM, nunca el DNI.
  - Las pruebas nuevas usan solo artículos ficticios y no crean clientes.
- **H-9 (cosmético):** la spec dice `señal` y el código usa `senal`.
- **H-10 (AC-02 en la e2e, aceptado):** en el navegador, AC-02 se ejerce dentro de `Buscar sigue funcionando`, que
  escribe 1 carácter y comprueba que la lista sigue cerrada. No espera la pausa para confirmar que no sale un pedido
  en vivo. Ese comportamiento lo fijan `Articulos_UnCaracter_SinSugerencias` y
  `useBusquedaEnVivo_UnPedidoPorPausaYMinimoDosCaracteres`, y la e2e de AC-13 cuenta 1 pedido por pausa. Se acepta
  como cobertura suficiente de AC-14 para AC-02 (decisión del agente por la delegación del usuario del 2026-11-14).
  Endurecerla queda para otro ticket.
- **H-11 (observación):** en Chrome y Edge, la respuesta vieja no llega a aplicarse porque la búsqueda nueva cancela su
  pedido, y la e2e verifica esa cancelación. El descarte por número de pedido de una respuesta que llega tarde, y las
  pantallas de presupuestos y facturas, están cubiertos en Vitest. El README y el comentario de la prueba lo dicen.

Result: PASSED
