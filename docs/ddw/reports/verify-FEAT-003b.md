# Verification FEAT-003b

Ronda 1 (2026-11-11), sobre `bfd10e7`. Verificador independiente `ddw-module-verifier` sobre el módulo completo:
primer veredicto FAIL solo por falta de evidencia TDD a la vista para los Blocks 2 a 5 y la corrección D-29; con
la evidencia registrada en `.ddw-work/review-block-1.md` a `review-block-5.md` (veredictos de los verificadores de
cada bloque), contrastada con el disco en los commits anteriores a cada bloque, el veredicto es PASS con 4 WARN.
Sin cambios de código entre los dos veredictos.

| Field | Value |
|---|---|
| Module | Presupuestos: cantidad, descuento y cálculos (src/Optica.Domain/Presupuestos, src/Optica.Application/Presupuestos, PresupuestosController y LectorCantidadJson, web/src/presupuestos/reglas.ts, web/src/api/cliente.ts, web/src/configuracion/numeros.ts, web/src/paginas/NuevoPresupuesto.tsx y Presupuesto.tsx, e2e/tests/presupuestos-calculos.spec.ts) |
| Line coverage | 95.7% |
| Branch coverage | 92.6% |
| Function coverage | 96.5% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean; `dotnet build Optica.sln` — 0 warnings; `npm run lint` (oxlint `--max-warnings 0`) — clean; `npm run build` (tsc estricto) — clean |

Cobertura de la tabla: código nuevo o modificado de .NET en la rama (8 archivos de `src/`, sin migraciones;
líneas 530/554, ramas 237/256, funciones 111/115), de la corrida del cierre de CODE sobre `2fff802`. .NET
completo: 97.3% / 93.4% / 96.2%. Web: líneas 99.02%, ramas 97.23%, funciones 100% (`reglas.ts` y `cliente.ts`
100% de líneas). **Corrección del informe de pruebas:** `docs/ddw/reports/tests-FEAT-003b.md` declara para .NET
95.9% / 91.6% / 95.7%; esa unión de los informes de coverlet contaba dos veces archivos que cada informe nombra con
una ruta relativa distinta. Resolviendo la ruta completa, la cobertura correcta es la de este informe (mayor). El
informe de CODE no se modifica en VERIFY; la diferencia no cambia ningún veredicto. El informe de FEAT-003a puede
tener el mismo error de cálculo.

Suite del cierre de CODE: 2483/2483 (.NET 1546, web 750, e2e unidad 71, e2e 116 en Chrome y Edge). Corridas del
verificador sobre `bfd10e7`: .NET 599/599 (filtro Presupuesto|CalculoLinea|Acceso), web 644/644, `src/paginas` y
`src/presupuestos` 374/374 en 4 corridas seguidas.

## Acceptance criteria
- ✅ AC-01 — `PrecioConDescuento_Ejemplos_AC01_AC04_AC05_AC06`, `DesdeArticulo_CantidadYDescuento_CalculaPrecios`, `Alta_CantidadYDescuento_GrabaPreciosCalculados`, `Get_DevuelveTotal`, `Repositorio_LeeCantidadYDescuento`, web `calcularLinea_ejemplos de AC-01, AC-04, AC-05 y AC-06`, `cambiar cantidad y descuento recalcula la fila`, e2e `AC-01: cantidad 3 y 10 % dan 900,00 y 2.700,00` (`CalculoLinea.CalcularPrecioConDescuento`, `LineaPresupuesto.DesdeArticulo`, `reglas.ts:calcularLinea`)
- ✅ AC-02 — `Constructor_CantidadInvalida_Rechazada`, `Post_CantidadInvalida_400`, `Post_CantidadDecimal_400`, web `validarCantidad_0_-1_2,5_vacía: inválida`, `cantidad inválida muestra el mensaje junto al campo`, e2e `AC-02: cantidad 0, -1 y 2,5 rechazada junto al campo` (`ValidacionPresupuesto`, `LectorCantidadJson`, `reglas.ts:validarCantidad`)
- ✅ AC-03 — `Constructor_DescuentoInvalido_Rechazado`, `Post_DescuentoFueraDeRango_400`, `Post_DescuentoTexto_400`, `Post_DescuentoConTresDecimales_400`, web `validarDescuento_-1_100,01_vacío: inválido`, `descuento inválido muestra el mensaje junto al campo`, e2e `AC-03: descuento -1 y 100,01 rechazado junto al campo`
- ✅ AC-04 — fila de la tabla común en `CalculoLineaTests` y `reglas.test.ts`, web `descuento 100 da 0,00`, final del e2e de AC-03
- ✅ AC-05 — tabla común, web `descuento 0 muestra el precio unitario`, estado inicial del e2e de AC-01
- ✅ AC-06 — `PrecioFinal_RedondeaDespuesDelDescuento`, `PrecioConDescuento_MitadHaciaArriba`, `Alta_DevuelveTotal`, web `calcularLinea_RedondeaDespuesDelDescuento`, `total con dos líneas`, e2e `AC-07: total de dos líneas 2.710,02`
- ✅ AC-07 — `Total_SumaPreciosFinales`, `TablaComun_AC07_SumaDePreciosFinales`, `PresupuestoGrabado_TotalCopiadoDelDominio`, `Alta_DevuelveTotal`, web `calcularTotal_suma de AC-07`, `leerPresupuesto_AC07_TotalDeDosLineas`, e2e `AC-07: total de dos líneas 2.710,02` (`Presupuesto.Total`, `PresupuestoGrabado.Desde`, `reglas.ts:calcularTotal`)
- ✅ AC-08 — web `presupuesto grabado muestra el total sin discriminar IVA`, e2e `AC-08: el presupuesto grabado muestra el total con IVA incluido` (`Presupuesto.tsx`, `NuevoPresupuesto.tsx`, `tfoot` "Total (IVA incluido)")
- ✅ AC-09 — `Constructor_CantidadInvalida_Rechazada`, `Post_CantidadMayorAlMaximo_400`, `Post_CantidadFueraDeInt_400`, web `validarCantidad_10000: rechazada`, `validarCantidad_PuntoDeMiles`, `cantidad 10.000 rechazada con "Usá hasta 9.999 unidades."`, e2e `AC-09: cantidad 10.000 rechazada junto al campo` (escribe "10.000", D-29)

NFR-01: e2e en Chrome y Edge (116/116); la versión anterior de cada navegador queda para FEAT-003d, como dice la
spec. NFR-02: `MidpointRounding.AwayFromZero` en .NET y BigInt en la web, filas 0,025 y 0,505 de la tabla común.

## Spec blocks
- ✅ Block 1 — 10/10 pruebas requeridas; evidencia TDD reconstruida (D-16), aceptada
- ✅ Block 2 — 15/15 pruebas requeridas; TDD: rojo de compilación y 32/81 + 26/85 por aserción, corrección con rojo propio
- ✅ Block 3 — 12/12 pruebas requeridas (incluida `validarCantidad_PuntoDeMiles` de la corrección D-29); TDD: 90 en rojo, corrección 6 en rojo, D-29 10 en rojo y 4/143 del cero inicial
- ✅ Block 4 — 9/9 pruebas requeridas; TDD: 20/20 en rojo, correcciones 13/78 y 4/5
- ✅ Block 5 — 6/6 pruebas requeridas; TDD: 12/12 en rojo contra `7d0a3c6` y 14/36 en `presupuestos-alta`

Desvíos de la lista de archivos documentados en D-21, D-24 y D-28; todas las tareas de cada bloque implementadas.

## Tests
- ✅ Sad-path tests: `POST /api/presupuestos` con cantidad 0, -1, null, ausente, 2.5, 1e3, texto, fuera de `int` y 10.000; descuento -1, 100.01, null, ausente, 3 decimales, texto y objeto; importe de línea y total excedidos (`Post_ImporteExcedido_400SinGrabar`); en cada 400 se verifica campo, texto y que no se grabó nada, sin repetir el valor. Dominio, `ControlarTopes`, `validarCantidad`, `validarDescuento`, `calcularLinea`, `calcularTotal`, `leerPresupuesto` y la pantalla con sus casos inválidos; e2e con foco, sin POST y sin número siguiente.
- ✅ Todas las pruebas que lista la spec existen y pasan (52/52).

Pruebas requeridas por la spec, por bloque (todas encontradas con su nombre y en verde):
- ✅ Block 1:
  - `PrecioConDescuento_Ejemplos_AC01_AC04_AC05_AC06`
  - `PrecioFinal_RedondeaDespuesDelDescuento`
  - `PrecioConDescuento_MitadHaciaArriba`
  - `Total_SumaPreciosFinales`
  - `DesdeArticulo_CantidadYDescuento_CalculaPrecios`
  - `Constructor_CantidadInvalida_Rechazada`
  - `Constructor_DescuentoInvalido_Rechazado`
  - `DesdeArticulo_ImporteExcedido_Rechazado`
  - `Constructor_TotalExcedido_Rechazado`
  - `Excepciones_NoRepitenElValor`
- ✅ Block 2:
  - `Alta_CantidadYDescuento_GrabaPreciosCalculados`
  - `Alta_DevuelveTotal`
  - `Post_CantidadInvalida_400`
  - `Post_CantidadDecimal_400`
  - `Post_DescuentoTexto_400`
  - `Post_CantidadMayorAlMaximo_400`
  - `Post_DescuentoFueraDeRango_400`
  - `Post_DescuentoConTresDecimales_400`
  - `Alta_ImporteExcedido_Rechazado`
  - `PresupuestoGrabado_TotalCopiadoDelDominio`
  - `Post_PreciosEnElPedido_SeIgnoran`
  - `Post_Errores400_NoRepitenElValor`
  - `CampoDeLaClave_CamposDeLinea`
  - `Get_DevuelveTotal`
  - `Repositorio_LeeCantidadYDescuento`
- ✅ Block 3:
  - `calcularLinea_ejemplos de AC-01, AC-04, AC-05 y AC-06`
  - `calcularTotal_suma de AC-07`
  - `validarCantidad_0_-1_2,5_vacía: inválida`
  - `validarCantidad_10000: rechazada`
  - `validarCantidad_PuntoDeMiles`
  - `validarDescuento_-1_100,01_vacío: inválido`
  - `validarDescuento_10,555: inválido por la escala`
  - `calcularLinea_importe excedido: rechazado`
  - `leerPresupuesto_total faltante o distinto: error de lectura`
  - `leerPresupuesto_0,1 + 0,2 = 0,3 se lee bien`
  - `sumarCentesimos_y_centavosDe`
  - `grabarPresupuesto_envía cantidad y descuento`
- ✅ Block 4:
  - `cambiar cantidad y descuento recalcula la fila`
  - `cantidad inválida muestra el mensaje junto al campo`
  - `cantidad 10.000 rechazada con "Usá hasta 9.999 unidades."`
  - `descuento inválido muestra el mensaje junto al campo`
  - `descuento 100 da 0,00`
  - `descuento 0 muestra el precio unitario`
  - `total con dos líneas`
  - `error 400 del servidor en la cantidad va junto al campo`
  - `presupuesto grabado muestra el total sin discriminar IVA`
- ✅ Block 5:
  - `AC-01: cantidad 3 y 10 % dan 900,00 y 2.700,00`
  - `AC-02: cantidad 0, -1 y 2,5 rechazada junto al campo`
  - `AC-09: cantidad 10.000 rechazada junto al campo`
  - `AC-03: descuento -1 y 100,01 rechazado junto al campo`
  - `AC-07: total de dos líneas 2.710,02`
  - `AC-08: el presupuesto grabado muestra el total con IVA incluido`

## Warnings (no bloquean)
- ⚠️ W-VER-03: `web/src/presupuestos/reglas.test.ts:391` (`un texto largo mal agrupado se resuelve sin demora`) depende del reloj (`performance.now() < 1000 ms`); puede fallar con la CPU muy cargada sin defecto.
- ⚠️ W-VER-03: una falla aislada de 1 prueba de 226 en `src/paginas` (1 de 6 corridas en la revisión del Block 4) sigue sin nombre ni causa; no se reprodujo en 13 corridas posteriores.
- ⚠️ W-VER-03 (latente): `nadaGrabado` de `e2e/tests/presupuestos-calculos.spec.ts:228-233` supone que el número siguiente al testigo no existe; vale con `workers: 1` y `fullyParallel: false`.
- ⚠️ W-VER-01: `campoCantidad` y `campoDescuento` (`web/src/api/cliente.ts:576-578`) se exportan y solo los usan las pruebas (la pantalla usa `CAMPO_DE_LINEA`, D-28).
- Informativos: `CalculoLinea.PrecioConDescuento` y `PrecioFinal` públicos sin uso en producción (la spec los pide así); la "Final verification" de la spec dice "8 AC" y el PRD tiene 9 (los 9 cubiertos); D-30 (contraste del punto entre Cantidad y Descuento) pendiente de confirmación del usuario; D-23 (limitación por encima de 2^46 pesos) aceptada por el usuario.
- Alerta de datos personales: pruebas anteriores a este ticket con DNI y email de formato real ("30111222", "23.456.789", "20333444", "ana@mail.com") que podrían coincidir con personas reales, y `Arca__Entorno=Simulado` sin fijar en `e2e/servidor.mjs`; diferidos a un ticket aparte en D-31, no introducidos por este diff.

Result: PASSED
