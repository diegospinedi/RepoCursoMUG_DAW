# Verification FEAT-003c

Ronda 1 (2026-11-12), sobre `2dc98f9` (el código es el de `558d5ab`; `558d5ab..2dc98f9` solo cambia documentos de
`docs/ddw/`). La hizo un verificador independiente, `ddw-module-verifier`, sobre el módulo completo. La evidencia TDD de
cada bloque está registrada en `.ddw-work/review-block-1.md` a `review-block-5.md`. La contrasté con el disco y con
`git log 552bf59..HEAD` y coincide. Veredicto: PASS con 3 WARN.

| Field | Value |
|---|---|
| Module | Presupuestos: edición en Borrador y paso a Final (src/Optica.Domain/Presupuestos, src/Optica.Application/Presupuestos, IUnidadDeTrabajo, PresupuestosController, RespuestasValidacion, PresupuestoRepository, UnidadDeTrabajoEf, OpticaDbContext, migración CierrePresupuestos, web/src/api/cliente.ts, web/src/presupuestos/FormularioPresupuesto.tsx, usePresupuestoDeLaRuta.ts y reglas.ts, web/src/paginas/EditarPresupuesto.tsx, NuevoPresupuesto.tsx y Presupuesto.tsx, e2e/tests/presupuestos-edicion.spec.ts) |
| Line coverage | 97.5% |
| Branch coverage | 93.4% |
| Function coverage | 99.2% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes`: clean. `dotnet build Optica.sln`: 0 warnings. `npm run lint` (oxlint `--max-warnings 0`): clean, también en la corrida del verificador. `npm run build` (tsc estricto): clean. `npm run test:lint`: 8/8 |

**Alcance de la cobertura.** La tabla mide el código .NET nuevo o modificado de la rama: 17 archivos de `src/` según
`git diff --name-only 552bf59..HEAD`, sin `*.Designer.cs` ni el snapshot, con la migración `CierrePresupuestos.cs`.
Resultados: líneas 1536/1575, ramas 464/497, funciones 248/250. Sale de la corrida del cierre de CODE sobre `558d5ab`:
unión de los cuatro informes de coverlet, con la ruta de cada archivo resuelta completa. Una línea cubierta en
cualquier informe cuenta como cubierta, y las ramas se toman por línea con el máximo cubierto.
`IPresupuestoRepository.cs` es una interfaz y no tiene líneas medibles.

Lo que queda sin cubrir son guardas defensivas: `ArgumentException` y `UnreachableException`, el `PrintMembers` de
`LineaPedida` y la rama `Attach` de `PresupuestoRepository.Actualizar` (`PresupuestoRepository.cs:165-168`), además de
las líneas de arranque de `Program.cs`.

Para comparar, .NET completo sin migraciones generadas da 96.9% / 93.3% / 96.7% (4710/4861, 1582/1696, 723/748).

**Corrección del informe de pruebas.** `docs/ddw/reports/tests-FEAT-003c.md` declara para .NET 7080/7355 líneas. Ese
total supera las 4861 líneas distintas que miden los informes, así que la unión volvió a contar dos veces archivos que
cada informe nombra con una ruta relativa distinta. Es el mismo error ya señalado en FEAT-003b. Los porcentajes
correctos son los de este informe. La diferencia no cambia ningún veredicto, y el informe de CODE no se modifica en
VERIFY.

**Web.** Vitest 5 omite del listado los archivos al 100%. Los archivos del ticket quedan así:

| Archivo | Líneas | Ramas | Funciones |
|---|---|---|---|
| `cliente.ts` | 100% | 99.52% | 100% |
| `reglas.ts` | 100% | 98.82% | 100% |
| `Presupuesto.tsx` | 97.87% | 95.83% | 100% |
| `FormularioPresupuesto.tsx` | 99.33% | 96.46% | 100% |
| `usePresupuestoDeLaRuta.ts` | 97.29% | 84.61% | 100% |
| `NuevoPresupuesto.tsx` | 95% | 87.5% | 100% |
| `EditarPresupuesto.tsx` | 100% | 100% | 100% |
| `App.tsx` | 100% | 100% | 100% |

Total de la web: 99.11% / 96.85% / 100%.

**Suite del cierre de CODE: 2675/2675.** El desglose es .NET 1649, web 825, e2e unidad 71 y e2e 130 (65 en Chrome y 65
en Edge). Corridas del verificador sobre `2dc98f9`:

- Optica.Domain.Tests: 334/334.
- Optica.Application.Tests: 358/358.
- Optica.Infrastructure.Tests filtrado a `PresupuestoRepositoryTests|UnidadDeTrabajoTests`: 86/86.
- Web (`src/paginas`, `src/presupuestos`, `src/api` y `App.test.tsx`): 646/646.
- `npm run lint`: clean.

## Acceptance criteria
- ✅ AC-01: `Modificar_Borrador_ConservaNumeroFechaYVersion`, `Modificar_ConRelojPosterior_ConservaFecha` (reloj falso: alta el 10/03/2026 y modificación el 12/03/2026, en la respuesta y en el GET), `Trigger_CambiarFechaONumero_Rechazado`, e2e `AC-01: modificar no cambia la fecha` (sin campo Fecha en pantalla; un PUT con `fecha` en el cuerpo no la cambia). Código: `Presupuesto.Modificar`, `ModificarPresupuesto.EjecutarAsync`, trigger `TR_Presupuesto_FechaYNumeroFijos` en `20261111220704_CierrePresupuestos.cs`.
- ✅ AC-02: `Put_ModificarDomicilio_200YGetLoMuestra` (cuerpo completo, versión 1 y GET idéntico), `Actualizar_Borrador_MenosLineas_ReemplazaLineasEIncrementaVersion`, web `Editar_CargaDatosYLineasGrabadas` y `Editar_ModificarDomicilio_GrabaYConfirma` (mensaje de confirmación y cuerpo enviado), e2e `AC-02: modificar el domicilio graba y se ve al reabrir`. Código: `PresupuestosController.Modificar`, `ModificarPresupuesto.EjecutarAsync`, `PresupuestoRepository.Actualizar`, `EditarPresupuesto.tsx`, `FormularioPresupuesto.tsx`.
- ✅ AC-03: `Put_AgregarYQuitarLinea_200` (renumeración 1..n y total), `Actualizar_Borrador_MasLineas_ReemplazaLineasEIncrementaVersion`, `Modificar_LineasExistentesYNueva_ConservaCopiaYRenumera`, web `Editar_AgregarYQuitarLinea_EnviaOrdenSoloEnLasGrabadas`, e2e `AC-03: agregar una línea y quitar otra`. Código: `ArmadoLineas`, `PresupuestoRepository.Actualizar`.
- ✅ AC-04: `PasarAFinal_Borrador_QuedaEnFinal`, `CambiarEstado_Borrador_FinalSinTocarLineas` (100 líneas intactas), `PutEstado_Final_200`, web `Presupuesto_ConfirmarFinal_GrabaFinal`, e2e `AC-04 y AC-05: pasar a Final pide confirmación y graba Final`. Código: `Presupuesto.PasarAFinal`, `PasarPresupuestoAFinal.EjecutarAsync`, `PresupuestoRepository.CambiarEstado`, `PresupuestosController.CambiarEstado`.
- ✅ AC-05: web `Presupuesto_PasarAFinal_PideConfirmacion`; el e2e de AC-04/AC-05 comprueba el diálogo modal (`:modal`), el texto de aviso, los dos botones, el foco en "Cancelar" y que no se envió ningún pedido antes de confirmar. Código: `Presupuesto.tsx` (`<dialog>`, `TEXTO_CONFIRMAR_FINAL`).
- ✅ AC-06: web `Presupuesto_CancelarFinal_NoLlamaALaApi` (Cancelar, Escape y cancel), e2e `AC-06: cancelar deja el presupuesto en Borrador` (sin pedidos; presupuesto idéntico byte a byte). Código: `Presupuesto.tsx`.
- ✅ AC-07: `Modificar_Final_Rechazado`, `Trigger_ModificarFinal_Rechazado`, `Trigger_LineasDeFinal_Rechazado`, `Modificar_Final_FinalSinEscribir`, `Put_PresupuestoFinal_409Final`, web `Presupuesto_Final_SinAccionesDeEdicion` y `Editar_PresupuestoFinal_MuestraAvisoSinFormulario`, e2e `AC-07 y AC-08: un Final rechaza cambios por pantalla y por Api` (5 pedidos directos, todos 409 `final`, sin cambios en la base). Código: las tres capas (dominio, caso de uso con 409 y triggers).
- ✅ AC-08: `PasarAFinal_Final_Rechazado`, `Trigger_FinalABorrador_Rechazado`, `PasarAFinal_SobreFinal_Final`, `PutEstado_BorradorSobreFinal_409Final`, mismo e2e que AC-07. Código: `PasarPresupuestoAFinal.EjecutarAsync`, trigger `TR_Presupuesto_FinalInmutable`.
- ✅ AC-09: `ConCantidadYDescuento_ConservaPrecioUnitarioGrabado`, `Put_LineaExistente_ConservaPrecioUnitarioGrabado`, web `Editar_CargaDatosYLineasGrabadas`, e2e `AC-09 y AC-10: las líneas conservan precio tras recalcular y desactivar` (recálculo real desde la pantalla de Configuración: el artículo pasa a 1.850,00 y la línea conserva 1.815,00). Código: `LineaPresupuesto.ConCantidadYDescuento`.
- ✅ AC-10: `Put_LineaConArticuloInactivo_ConservaCopia`, mismo e2e que AC-09 (artículo inactivo; al reabrir, Código, Descripción y 1.815,00 sin cambios, también en la modificación). Código: `ArmadoLineas` y `ModificarPresupuesto` (la línea con `orden` no consulta el catálogo).

NFR-01: e2e en Chrome y Edge (130/130). Como dice la spec, la versión anterior de cada navegador queda para FEAT-003d.

## Spec blocks
- ✅ Block 1: 7/7 pruebas requeridas y todas las tareas implementadas. Evidencia TDD: un esqueleto de firmas sin comportamiento dio 7/7 en rojo por aserción; en la corrección, la prueba de reflexión quedó en rojo con dos mutaciones.
- ✅ Block 2: 16/16 pruebas requeridas. Seis triggers, `ADD COLUMN` con CHECK, `Down` que quita primero los triggers, motivo `RegistroCerrado` y traducción del código 1811. TDD: esqueleto → Infrastructure 26/471 en rojo.
- ✅ Block 3: 21/21 pruebas requeridas. Los dos PUT, `Program.cs:153-154`, `Conflicto409`/`ConflictoPresupuesto`, rutas en `ProteccionTests.cs:94-95` y `no-store` en `SinCacheTests.cs:90-109,201`. TDD: esqueleto → Application 39/358 y Api 31/476 en rojo.
- ✅ Block 4: 14/14 pruebas requeridas (`Presupuesto_CancelarFinal_NoLlamaALaApi` y los dos `leerPresupuesto_*` están escritos como `it.each` con sufijo). Ruta `/presupuestos/:numero/editar` en `App.tsx:49`. TDD: 76 en rojo antes; las correcciones tuvieron rojo propio, incluida `Presupuesto_VolverAlMismoNumero_SinAvisoNiDialogoViejos` con 2/2.
- ✅ Block 5: 7/7 escenarios y puertos 5550/5558 en `e2e/README.md`. TDD: 14/14 en rojo contra `d721f27`. Es coherente con el disco: cada escenario necesita la pantalla de modificación, el botón "Pasar a Final" o los PUT nuevos, y nada de eso existe en `d721f27`.

**Contraste de la evidencia TDD con git.** Cada bloque se commiteó con sus pruebas y su código juntos (`2ac54b5`,
`335df8c`, `1eb5053`, `0b59376`, `558d5ab`), así que la historia no guarda la fase en rojo. La evidencia es la de los
verificadores de cada bloque, con conteos y causas. Verifiqué que todas las pruebas nombradas existen en disco y que
cada una ejercita una API o pantalla que no existía antes de su bloque, así que ninguna podía pasar antes del código.
No encontré afirmaciones que no coincidan con el disco.

Hay desvíos de la lista de archivos, documentados en D-12, D-14 a D-17. El principal es `ArmadoLineas.cs`, que la spec
no nombraba y quedó compartido por el alta y la modificación. Todas las tareas de cada bloque están implementadas.

## Tests
- ✅ Sad-path tests:
  - `PUT /api/presupuestos/{numero}`: versión faltante, -1, 2^53 e ilegible (`Put_VersionFaltante_400`); datos del cliente, cantidad, descuento y lista vacía (`Put_DatosInvalidos_400PorCampo`); `orden` repetido, inexistente o con otro código (`Put_LineaAjena_400`); artículo nuevo inactivo (`Put_ArticuloNuevoInactivo_400`); 404 (`Put_Inexistente_404`); 401 (`Put_SinSesion_401`); 409 `cambiado` (`Put_VersionVieja_409Cambiado`, `Put_OrdenDeVersionVieja_409Cambiado`); 409 `final` (`Put_PresupuestoFinal_409Final`); 409 sin datos del pedido (`Put_Errores409_NoRepitenDatos`); 413 y 415 en `ProteccionTests`.
  - `PUT /api/presupuestos/{numero}/estado`: `PutEstado_EstadoInvalido_400`, `PutEstado_BorradorSobreBorrador_400`, `PutEstado_VersionVieja_409Cambiado`, `PutEstado_BorradorSobreFinal_409Final`, y la versión inválida, el 404 y el 401 en las mismas pruebas de los dos PUT.
  - En cada caso se verifica el campo, el texto fijo y que el presupuesto quedó idéntico.
  - Casos de uso: `Modificar_DatosInvalidos_TodosLosErroresSinLeer`, `Modificar_VersionFueraDeRango_ErrorEnVersionSinLeer`, `Modificar_LineaAjena_InvalidoSinEscribir`, `Modificar_TotalExcedido_Invalido`, `Modificar_OtroMotivoDeConflicto_SePropaga`, `PasarAFinal_DatosInvalidos_SinLeer`, `PasarAFinal_BorradorSobreBorrador_Invalido`, entre otras.
  - Dominio: `Modificar_Final_Rechazado`, `PasarAFinal_Final_Rechazado`, `Constructor_VersionInvalida_Rechazada`, `ConCantidadYDescuento_ImporteExcedido_Rechazado`.
  - Repositorio y base: `Actualizar_VersionMaxima_Rechazada`, `CambiarEstado_PresupuestoNoFinal_Rechazado`, `Obtener_OrdenConHueco_EstadoCorrupto` y los cinco `Trigger_*`.
  - Web: `Editar_DatoInvalido_MensajeJuntoAlCampo`, `Editar_409Cambiado_MuestraMensaje`, `Editar_409Final_MuestraMensaje`, `Editar_404_MensajeNoEncontrado`, `leerPresupuesto_SinVersion_ErrorDeLectura`, `leerPresupuesto_LineaSinOrden_ErrorDeLectura`.
- ✅ Todas las pruebas que lista la spec existen y pasan (65/65).

Pruebas requeridas por la spec, por bloque (todas encontradas con su nombre y en verde):
- ✅ Block 1:
  - `Modificar_Borrador_ConservaNumeroFechaYVersion`
  - `Modificar_Final_Rechazado`
  - `PasarAFinal_Borrador_QuedaEnFinal`
  - `PasarAFinal_Final_Rechazado`
  - `Constructor_VersionInvalida_Rechazada`
  - `ConCantidadYDescuento_ConservaPrecioUnitarioGrabado`
  - `ConCantidadYDescuento_ImporteExcedido_Rechazado`
- ✅ Block 2:
  - `Actualizar_Borrador_MenosLineas_ReemplazaLineasEIncrementaVersion`
  - `Actualizar_Borrador_MasLineas_ReemplazaLineasEIncrementaVersion`
  - `Actualizar_VersionVieja_ConflictoConcurrencia`
  - `CambiarEstado_Borrador_FinalSinTocarLineas`
  - `CambiarEstado_VersionVieja_ConflictoConcurrencia`
  - `Actualizar_VersionMaxima_Rechazada`
  - `CambiarEstado_PresupuestoNoFinal_Rechazado`
  - `Obtener_OrdenConHueco_EstadoCorrupto`
  - `Trigger_ModificarFinal_Rechazado`
  - `Trigger_LineasDeFinal_Rechazado`
  - `Trigger_FinalABorrador_Rechazado`
  - `Trigger_CambiarFechaONumero_Rechazado`
  - `Trigger_BorrarFinal_Rechazado`
  - `Confirmar_TriggerRegistroCerrado_ConflictoSinCausa`
  - `Migracion_CierrePresupuestos_BorradoresExistentesConVersion0`
  - `Migracion_Down_QuitaTriggersYColumna`
- ✅ Block 3:
  - `Modificar_ConRelojPosterior_ConservaFecha`
  - `Put_ModificarDomicilio_200YGetLoMuestra`
  - `Put_AgregarYQuitarLinea_200`
  - `PutEstado_Final_200`
  - `Put_PresupuestoFinal_409Final`
  - `PutEstado_BorradorSobreFinal_409Final`
  - `Put_LineaExistente_ConservaPrecioUnitarioGrabado`
  - `Put_LineaConArticuloInactivo_ConservaCopia`
  - `Put_VersionVieja_409Cambiado`
  - `PutEstado_VersionVieja_409Cambiado`
  - `Put_VersionFaltante_400`
  - `PutEstado_EstadoInvalido_400`
  - `Put_LineaAjena_400`
  - `Put_OrdenDeVersionVieja_409Cambiado`
  - `PutEstado_BorradorSobreBorrador_400`
  - `Get_LineasTraenOrden`
  - `Put_ArticuloNuevoInactivo_400`
  - `Put_DatosInvalidos_400PorCampo`
  - `Put_Inexistente_404`
  - `Put_SinSesion_401`
  - `Put_Errores409_NoRepitenDatos`
- ✅ Block 4:
  - `Editar_CargaDatosYLineasGrabadas`
  - `Editar_ModificarDomicilio_GrabaYConfirma`
  - `Editar_AgregarYQuitarLinea_EnviaOrdenSoloEnLasGrabadas`
  - `Presupuesto_PasarAFinal_PideConfirmacion`
  - `Presupuesto_ConfirmarFinal_GrabaFinal`
  - `Presupuesto_CancelarFinal_NoLlamaALaApi`
  - `Presupuesto_Final_SinAccionesDeEdicion`
  - `Editar_PresupuestoFinal_MuestraAvisoSinFormulario`
  - `Editar_409Cambiado_MuestraMensaje`
  - `Editar_409Final_MuestraMensaje`
  - `Editar_DatoInvalido_MensajeJuntoAlCampo`
  - `Editar_404_MensajeNoEncontrado`
  - `leerPresupuesto_SinVersion_ErrorDeLectura`
  - `leerPresupuesto_LineaSinOrden_ErrorDeLectura`
- ✅ Block 5:
  - `AC-01: modificar no cambia la fecha`
  - `AC-02: modificar el domicilio graba y se ve al reabrir`
  - `AC-03: agregar una línea y quitar otra`
  - `AC-04 y AC-05: pasar a Final pide confirmación y graba Final`
  - `AC-06: cancelar deja el presupuesto en Borrador`
  - `AC-07 y AC-08: un Final rechaza cambios por pantalla y por Api`
  - `AC-09 y AC-10: las líneas conservan precio tras recalcular y desactivar`

## Warnings (no bloquean)
- ⚠️ W-VER-01: `campoOrden` (`web/src/api/cliente.ts:591`) se exporta y solo lo usa la prueba (`web/src/api/cliente.test.ts:1227`); la pantalla usa `CAMPO_ORDEN`. Es el mismo patrón que `campoCantidad` y `campoDescuento` en FEAT-003b. `CAMPO_ESTADO` (`cliente.ts:593`) se exporta y solo se usa dentro de `cliente.ts`.
- ⚠️ W-VER-02: en ramas, `ModificarPresupuesto.cs` tiene 34/38 (89.5%) y `ArmadoLineas.cs` 12/14 (85.7%). Lo sin cubrir son guardas defensivas (`Invalido` sin errores en `ModificarPresupuesto.cs:74-76` y `UnreachableException` en `ArmadoLineas.cs:71,83-84`). Las líneas están al 98.2% y al 92.7%, y la capa de aplicación del ticket en conjunto suma 195/210 ramas (92.9%).
- ⚠️ W-VER-03 (latente): el e2e `AC-09 y AC-10` (`e2e/tests/presupuestos-edicion.spec.ts:613`) graba el múltiplo de redondeo en 50 y no lo restablece. Hoy no afecta porque es la última prueba del archivo, con base propia por proyecto, `workers: 1` y `fullyParallel: false`. Una prueba que se agregue después en el mismo archivo encontraría el catálogo recalculado.
- Informativos:
  - `web/src/paginas/Presupuesto.test.tsx:413` espera una sola vuelta del bucle de eventos (`setTimeout(0)`) antes de aserciones negativas: podría pasar sin probar nada si el defecto apareciera más tarde.
  - La rama `Attach` de `PresupuestoRepository.Actualizar` (`PresupuestoRepository.cs:165-168`, línea original no seguida por el contexto) no la ejercita ninguna prueba.
  - El informe de pruebas sobrestima los totales de cobertura .NET; está corregido arriba.
  - Proceso (D-17): el commit del Block 3 dejó dos e2e existentes en rojo hasta `558d5ab`.
  - El `Down` de `CierrePresupuestos` quita la protección de los Final y es solo para desarrollo (D-10, D-18).
- Alerta de datos personales:
  - Los datos nuevos de este ticket son ficticios: DNI 99999999, 99999998 y 99.999.999, y correos `@example.com`.
  - Siguen pendientes, de tickets anteriores, los DNI y correos con formato real y el `Arca__Entorno=Simulado` sin fijar en `e2e/servidor.mjs`. Están diferidos a un ticket aparte en D-31 de FEAT-003b y D-18; este diff no los introduce.
  - La `DbUpdateException` del trigger de fecha y número se registra sin `Entries` ni datos del cliente (`RegistroTests.cs`).

Result: PASSED
