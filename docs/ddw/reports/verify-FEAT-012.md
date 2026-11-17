# Verification FEAT-012

Menú de Inicio moderno y estilo uniforme. Ronda 1 de VERIFY, sobre el commit del cierre de CODE `8e72f3c`.

**Contexto de la verificación:**
- **Rama:** `feat/FEAT-012-menu-inicio`, base `cf9dc7f`.
- **Verificador:** ddw-module-verifier, un agente que no escribió el código.
- **Fuentes contrastadas:**
  - `docs/ddw/prd/prd-FEAT-012.md`;
  - `docs/ddw/specs/spec-FEAT-012.md`;
  - `docs/ddw/specs/decisions-FEAT-012.md` (D-01 a D-22);
  - `docs/ddw/security/sast-FEAT-012.md`;
  - la corrida de `docs/ddw/reports/tests-FEAT-012.md` (4762/4762).
- **Lo que el verificador volvió a correr:** la web con cobertura (1428/1428), oxlint, `tsc`, `test:lint` (8/8) y la e2e de
  unidad (164/164).
- **Lo que no volvió a correr:** el harness Playwright (242/242); ese número sale del informe de pruebas.

| Field | Value |
|---|---|
| Module | `web/src/paginas` (Inicio, MenuInicio, seccionesMenu y 10 pantallas con clases), `web/src/componentes/IconoMenu.tsx`, `web/src/marca/contraste.ts`, `web/src/estilos.css`, `e2e/tests` (inicio-menu, estilo-uniforme, medidas, backup-aviso) |
| Line coverage | 99.0% |
| Branch coverage | 97.3% |
| Function coverage | 100% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `npm run lint` (oxlint) — clean; `tsc` — clean; `npm run build` — clean; `npm run test:lint` 8/8 |

**Cobertura.** La de la tabla es la de la web, que es donde está el código del ticket: 99,06 % de sentencias, 97,28 % de
ramas, 100 % de funciones y 99,02 % de líneas. Los archivos nuevos y `Inicio.tsx` quedan al 100 %. El ticket no toca
.NET.

## Acceptance criteria
- ✅ AC-01 — `MenuInicio_TresSeccionesConSusOpciones`; e2e `muestra Ventas, Catálogo y Sistema con sus opciones` (`web/src/paginas/seccionesMenu.ts`, `MenuInicio.tsx`)
- ✅ AC-02 — `MenuInicio_UnEnlacePorNombreConSuDestino`; e2e `clic en la tarjeta fuera del texto abre Facturas` (clic en la esquina, fuera del texto y del ícono)
- ✅ AC-03 — `MenuInicio_UnEnlacePorNombreConSuDestino`, `MenuInicio_DescripcionFueraDelNombreDelEnlace`; destinos iguales a los de `cf9dc7f`
- ✅ AC-04 — `MenuInicio_OrdenDeLosEnlacesVentasCatalogoSistema`; e2e `Tab recorre las tarjetas en orden con foco visible` (contorno computado de 3px del primario)
- ✅ AC-05 — e2e `360 px una columna sin desplazamiento horizontal`
- ✅ AC-06 — e2e `1280 px dos tarjetas o más en la misma fila`
- ✅ AC-07 — e2e `íconos y foco con el primario de la marca`; `contraste_PaletasConocidas_TextoYFoco`
- ✅ AC-08 — `Inicio_AvisosAntesDelMenu` (orden en el DOM)
- ✅ AC-09 — `Inicio_SalirSigueCerrandoLaSesion`, `Inicio_SalirConError_AlertaJuntoAlBoton`
- ✅ AC-10 — proyectos `chrome-*` y `msedge-*`: 242 pruebas que pasan en la corrida del cierre
- ✅ AC-11 — e2e `las 15 pantallas como tarjeta con 360 y 1280 px`. Las 14 pantallas se comparan con la tarjeta del menú. Inicio sigue D-15, aprobada por el usuario en VERIFY: en Inicio, las tarjetas son las opciones del menú.
- ✅ AC-12 — e2e del campo inválido de `estilo-uniforme.spec.ts` (Block 3). Verifica el borde rojo sin foco y con foco, el contorno del primario y el mensaje debajo, en 5 formularios.
- ✅ AC-13 — e2e `botones principales y secundarios con el mismo alto`, más 9 pruebas de `boton-secundario` por pantalla. Sigue D-21, aprobada por el usuario en VERIFY: el mismo alto mínimo, relleno y radio; un botón de texto largo puede ocupar dos renglones a 360 px. A 1280 px todos miden igual.
- ✅ AC-14 — e2e `las 15 pantallas como tarjeta con 360 y 1280 px`: la página no se desplaza a lo ancho y las tablas tienen `overflow-x: auto`.
- ✅ AC-15 — e2e del bloque de mensaje de `estilo-uniforme.spec.ts` (Block 3): borde de 4px, fondo, color y prefijo ✕ iguales en 5 pantallas.
- ✅ AC-16 — e2e `contraste y CSP en las 15 pantallas con la marca real`; `contraste_MensajesCamposBotonesYTablas_DosMarcas`

## Spec blocks
- ✅ Block 1 — 10/10 pruebas requeridas; D-12 aplicado (`seccionesMenu.ts`).
- ✅ Block 2 — 4/4: las clases `boton-secundario` y `mensaje-estado` están en los 8 archivos que nombra la spec.
- ✅ Block 3 — 13/13; `backup-aviso` con el orden de D-11; README con 54 proyectos.

## Required tests (spec)

**Block 1**
- `MenuInicio_TresSeccionesConSusOpciones`
- `MenuInicio_UnEnlacePorNombreConSuDestino`
- `MenuInicio_DescripcionFueraDelNombreDelEnlace`
- `MenuInicio_OrdenDeLosEnlacesVentasCatalogoSistema`
- `MenuInicio_IconosDecorativosEnTodasLasOpciones`
- `Inicio_AvisosAntesDelMenu`
- `Inicio_SalirSigueCerrandoLaSesion`
- `Inicio_ConsultasFallidas_MenuSinAvisos`
- `contraste_PaletasConocidas_TextoYFoco`
- `contraste_ColorInvalido_Rechazado`

**Block 2**
- `contraste_MensajesCamposBotonesYTablas_DosMarcas`
- `Botones secundarios por función llevan boton-secundario`: son las 9 pruebas por pantalla.
- `Presupuesto_MensajesDeEstado_ConClaseMensajeEstado`
- `Campo con error inválido muestra aria-invalid y el mensaje junto al campo`: lo cubren las pruebas existentes con `aria-invalid`.

**Block 3**
- `muestra Ventas, Catálogo y Sistema con sus opciones`
- `clic en la tarjeta fuera del texto abre Facturas`
- `Tab recorre las tarjetas en orden con foco visible`
- `360 px una columna sin desplazamiento horizontal`
- `1280 px dos tarjetas o más en la misma fila`
- `íconos y foco con el primario de la marca`
- `sin violaciones de CSP ni error en la consola`
- `backup-aviso` sigue en verde con el orden nuevo
- `las 15 pantallas como tarjeta con 360 y 1280 px`
- `botones principales y secundarios con el mismo alto`
- `campo inválido con borde de error y foco del primario`
- `mensaje de error con el mismo bloque`
- `contraste y CSP en las 15 pantallas con la marca real`

## Tests
- ✅ Sad-path tests: pruebas con entrada inválida.
  - `contraste` con un color inválido lanza `RangeError`.
  - Cuando fallan las consultas de backup y de emisiones, el menú se muestra sin avisos.
  - Si "Salir" falla, se muestra una alerta junto al botón.
  - Formularios enviados vacíos en 5 pantallas.
  - Números inexistentes en 4 pantallas de detalle.
  - Contraseña ya definida (409).
  - Una violación de CSP hace fallar la prueba.
- ✅ F-VER-05: lint, tipos y build limpios. No hay dependencias nuevas (NFR-03).

## Decisiones aprobadas por el usuario en VERIFY (2026-11-17)
- **D-21:** AC-13 se lee como el mismo alto mínimo, relleno y radio. Un botón de texto largo puede ocupar dos renglones en
  360 px.
- **D-15:** en Inicio no hay tarjeta exterior; las tarjetas son las opciones del menú (AC-11).

## Warnings (no bloquean)
- W-3: la e2e de AC-14 comprueba `overflow-x: auto`, pero no fuerza una tabla que desborde a 360 px, así que el
  desplazamiento interno no llega a ejercitarse.
- W-4: el orden "avisos antes del menú" (AC-08) se prueba solo en Vitest, sin navegador. Es el alcance que fijó la spec.
- W-5: `web/src/estilos.css:493` conserva `.tabla td button { padding-inline }`. D-14 decía quitar el padding, y se ajustó
  en la ronda 2 del Block 2 para que la tabla de Artículos entre a 1280 px. El alto no cambia.
- W-6: la spec dice "D-01 a D-20" y "los 10 AC". Hoy son D-01 a D-22 y 16 AC. La spec no se edita en VERIFY.
- W-7 (W-VER-01): `web/src/marca/contraste.ts` solo lo usan las pruebas, como está documentado. La fórmula WCAG también
  está en `e2e/tests/medidas.ts`.
- W-8 (W-VER-03): `e2e/tests/estilo-uniforme.spec.ts:31` repite los colores de error como constantes, y el recorrido con
  Tab supone que no hay nada enfocable antes del menú.

## Notas
- Las correcciones de W-3 a W-8 son menores y quedan para un ticket aparte. En VERIFY no se corrige código.
- La versión anterior de Chrome y Edge queda para la verificación manual del usuario (D-02).
- SAST PASSED, sin paquetes nuevos. El aviso de picomatch sigue aceptado en D-28 de FEAT-011.
- No se encontró exposición de datos personales, de clientes ni de la empresa.

Result: PASSED
