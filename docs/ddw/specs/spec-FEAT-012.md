# Spec FEAT-012: Menú de Inicio moderno y estilo uniforme

| Field | Value |
|-------|-------|
| Ticket | FEAT-012 |
| PRD | docs/ddw/prd/prd-FEAT-012.md |
| Tier | FEATURE |
| Date | 2026-11-17 |
| Spec loops | 2 |
| Loops since last human decision | 1 |

## Summary
Inicio deja de mostrar siete enlaces subrayados en una columna y pasa a mostrar un menú de tarjetas en tres secciones:
Ventas, Catálogo y Sistema.

- El menú se arma desde datos (`SECCIONES_MENU`).
- Cada tarjeta tiene un ícono SVG propio, el nombre como único texto del enlace y una descripción asociada con
  `aria-describedby`.
- Toda la tarjeta es clicable mediante un enlace estirado.
- Los colores salen de la marca instalada.
- Es solo web: no cambian la Api ni el dominio.

**Ampliación (vuelta de CODE, decisión del usuario del 2026-11-17).**
- El Block 2 lleva el estilo del menú a las 15 pantallas desde la hoja global. El markup se toca solo donde falta una
  clase.
- El Block 3 suma la e2e del menú y la del estilo uniforme.

Las decisiones de diseño son D-01 a D-20, en `docs/ddw/specs/decisions-FEAT-012.md`. Las amenazas son R-01 a R-03, en
`docs/ddw/security/threat-FEAT-012.md`.

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 3 |
| FR-02 | Block 1, Block 3 |
| FR-03 | Block 1, Block 3 |
| FR-04 | Block 1, Block 3 |
| FR-05 | Block 1, Block 3 |
| FR-06 | Block 1, Block 3 |
| FR-07 | Block 1 |
| FR-08 | Block 1 |
| FR-09 | Block 2, Block 3 |
| FR-10 | Block 2, Block 3 |
| FR-11 | Block 2, Block 3 |
| FR-12 | Block 2, Block 3 |
| FR-13 | Block 2, Block 3 |
| NFR-01 | Strategy: texto de las tarjetas en `--color-texto` (D-04); función `contraste` probada con las dos paletas conocidas (D-10) y medición de los colores computados en la e2e con la marca del harness; pares nuevos de mensajes, campos, botones y tablas en `contraste.test.ts` (D-20) |
| NFR-02 | Strategy: e2e `inicio-menu` y `estilo-uniforme` en Chrome y Edge estables, con ventanas de 360 px y 1280 px (Block 3) |
| NFR-03 | Strategy: íconos SVG propios en JSX; `web/package.json` y su lock sin cambios |

## Dependencies between blocks
El Block 2 depende del Block 1 (clases y estilos del menú) y el Block 3 de los dos. Orden: 1 → 2 → 3.

## Block 1 — Menú de tarjetas en Inicio

**Files**
- `web/src/paginas/menuInicio.ts` (new). Define:
  - el tipo `NombreIcono`;
  - `SECCIONES_MENU` (`as const satisfies readonly SeccionMenu[]`), con las 3 secciones y las 7 opciones.
- `web/src/componentes/IconoMenu.tsx` (new): `IconoMenu({ nombre })`. Dibuja un SVG de 24×24 en línea, con
  `stroke="currentColor"`, `aria-hidden="true"` y `focusable="false"`, sin `<title>` ni `<style>`. Exporta solo el
  componente.
- `web/src/paginas/MenuInicio.tsx` (new): una `<section aria-labelledby>` por sección, con un `h2` y un
  `<ul className="menu__lista">`. Cada tarjeta es un `<li className="tarjeta-menu">` que contiene el ícono, un `<Link>`
  con solo el nombre y un `<p>` con la descripción. Los ids salen de `useId()`.
- `web/src/marca/contraste.ts` (new): `contraste(colorA, colorB)`, que calcula la relación de contraste WCAG entre dos
  `#RRGGBB`.
- `web/src/paginas/Inicio.tsx` (modified). El orden pasa a ser:
  1. el `h1` "Sesión iniciada", hijo directo de la sección;
  2. los avisos, sin cambios;
  3. `<MenuInicio />`;
  4. una barra `.inicio__acciones` con "Salir" (`<button className="boton-secundario">`).
  
  La sección lleva la clase `pantalla pantalla--menu`.
- `web/src/estilos.css` (modified). Clases nuevas: `.marco:has(.pantalla--menu)` (48rem), `.menu`, `.menu__seccion`,
  `.menu__lista`, `.tarjeta-menu`, `.tarjeta-menu__icono`, `.tarjeta-menu__enlace`, `.tarjeta-menu__descripcion`,
  `.inicio__acciones` y `.boton-secundario`.
- `web/src/paginas/MenuInicio.test.tsx` (new), `web/src/paginas/Inicio.test.tsx` (modified),
  `web/src/marca/contraste.test.ts` (new).

**Logic**
- **Secciones y opciones (FR-01).** El nombre, el destino y la descripción de cada opción:

  | Sección | Opción | Destino | Descripción |
  |---|---|---|---|
  | Ventas | Nuevo presupuesto | `/presupuestos/nuevo` | "Armá un presupuesto con artículos del catálogo." |
  | Ventas | Buscar presupuestos | `/presupuestos` | "Encontrá presupuestos por fecha o por cliente." |
  | Ventas | Facturas | `/facturas` | "Buscá facturas emitidas y descargá su PDF." |
  | Catálogo | Artículos | `/articulos` | "Buscá, editá y actualizá los costos del catálogo." |
  | Catálogo | Nuevo artículo | `/articulos/nuevo` | "Cargá un artículo con su costo y su margen." |
  | Sistema | Configuración | `/configuracion` | "Alícuota, redondeo, datos del emisor y backup." |
  | Sistema | Cambiar la contraseña | `/cambiar-contrasena` | "Cambiá la contraseña de acceso al sistema." |

  Nombres y destinos idénticos a los de hoy (FR-03). Las descripciones no usan las palabras vigiladas (D-09).
- **Estilo de la tarjeta (FR-02, FR-06, D-04).**
  - La tarjeta lleva `position: relative`, fondo blanco, borde de 1px `#d0d0d0`, radio de 8px, sombra leve y una franja
    superior de 4px del primario.
  - El ícono va en el primario.
  - El nombre va en `--color-texto`, en negrita y sin subrayado.
  - La descripción va en `--color-texto` con opacidad 0,8 y no se cambia por un gris de menor contraste.
- **Enlace estirado (FR-02, D-06).** El `::after` del enlace (`position:absolute; inset:0`) cubre la tarjeta.
- **Hover y foco (FR-04, D-05).**
  - Con el puntero encima, el borde pasa al primario y la sombra crece, con una transición de 150 ms que
    `prefers-reduced-motion: reduce` desactiva.
  - Con el foco, la regla `.tarjeta-menu:has(a:focus-visible)` dibuja un contorno de 3px del primario separado 2px, y
    `.tarjeta-menu__enlace:focus-visible` queda con `outline: none`.
- **Grilla (FR-05, D-07).** `display:grid; gap:1rem; grid-template-columns: repeat(auto-fill, minmax(min(13rem, 100%),
  1fr))`.
- **Avisos y salida (FR-07, FR-08, D-08).** Los avisos se dibujan antes de `MenuInicio`. "Salir" queda en la barra
  final, como botón secundario.
- **Sin estilos en línea (R-02).** No se usa ningún atributo `style`.

**Input validation**
- `nombre` de `IconoMenu`: solo valores de `NombreIcono`, verificado por `tsc` en la compilación.
- `contraste`: acepta solo colores `#RRGGBB` (7 caracteres, hexadecimal) y lanza `RangeError` con cualquier otro valor.

**Error handling**
- Un color inválido en `contraste` lanza `RangeError` sin repetir el valor recibido.
- Si fallan las consultas de emisiones pendientes o de backup, el menú se dibuja igual y el aviso no aparece, como hoy.

**Required tests**
- [ ] `MenuInicio_TresSeccionesConSusOpciones` — validates AC-01.
- [ ] `MenuInicio_UnEnlacePorNombreConSuDestino` — validates AC-03 y AC-02 (destino).
- [ ] `MenuInicio_DescripcionFueraDelNombreDelEnlace` — validates AC-03 (nombre accesible) y R-01.
- [ ] `MenuInicio_OrdenDeLosEnlacesVentasCatalogoSistema` — validates AC-04 (orden en el DOM).
- [ ] `MenuInicio_IconosDecorativosEnTodasLasOpciones` — validates FR-02 y FR-06: el SVG lleva `aria-hidden`, sin
  `<title>` ni `style`, y se dibujan los 7 íconos.
- [ ] `Inicio_AvisosAntesDelMenu` — validates AC-08.
- [ ] `Inicio_SalirSigueCerrandoLaSesion` — validates AC-09.
- [ ] `Inicio_ConsultasFallidas_MenuSinAvisos` — el menú se dibuja aunque fallen las consultas (error).
- [ ] `contraste_PaletasConocidas_TextoYFoco` — validates NFR-01:
  - `--color-texto` sobre blanco ≥ 4,5;
  - #0903A0 sobre #FFFFFF ≥ 3;
  - #5A5A5A sobre #F5F5F5 ≥ 3.
- [ ] `contraste_ColorInvalido_Rechazado` — un color inválido lanza `RangeError` (sad path).

**Completion criterion**
Pasan Vitest, lint, `test:lint` y build, y la cobertura web sigue en 80 % o más.

## Block 2 — Estilo uniforme en todas las pantallas

**Files**
- `web/src/estilos.css` (modified): variables de diseño en `:root` (error, éxito y sus fondos, bordes, radios, sombras y
  espacios), conservando `--color-primario`, `--color-fondo` y `--color-texto`. Reglas nuevas o cambiadas:
  - `.pantalla` y `.pantalla--ancha` como tarjeta, salvo `.pantalla--menu` (D-15);
  - `.cargando` como tarjeta (D-16);
  - `.pantalla h1`, `.pantalla h2`, `.campo`, `.campo input/select/textarea`, `.tabla input`, solo lectura,
    `input[type=file]`;
  - `button`, `.boton-secundario`, `:hover`, `:focus-visible`, `:disabled`;
  - `.tabla`, `.tabla thead th`, `.tabla tbody tr:hover`;
  - `.mensaje-general`, `.mensaje-exito`, `.aviso`, `.mensaje-estado`, `.campo__error` por separado (D-17);
  - `.paginacion`, `dl.datos`, `dialog` y `::backdrop`, `fieldset` y `legend` (D-19).
  
  Se quita `.tabla td button { padding }` (D-14).
- Clase `boton-secundario` (D-14) agregada en 8 archivos de markup:
  - `web/src/paginas/Articulos.tsx` (Desactivar/Activar y paginación);
  - `web/src/paginas/BuscarFacturas.tsx` y `web/src/paginas/BuscarPresupuestos.tsx` (paginación);
  - `web/src/presupuestos/BuscadorArticulos.tsx` (Agregar y paginación);
  - `web/src/presupuestos/FormularioPresupuesto.tsx` (Quitar);
  - `web/src/paginas/EditarArticulo.tsx` (Volver a abrir);
  - `web/src/paginas/Presupuesto.tsx` (Descargar PDF, Descartar, dos Cancelar; además, `mensaje-estado` en los dos
    mensajes de estado);
  - `web/src/paginas/DetalleFactura.tsx` (Descargar PDF).
- `web/src/marca/contraste.test.ts` (modified): pares nuevos.
- Pruebas de las pantallas modificadas (modified, solo donde una aserción cambia por la clase nueva):
  `Articulos.test.tsx`, `BuscarFacturas.test.tsx`, `BuscarPresupuestos.test.tsx`, `EditarArticulo.test.tsx`,
  `Presupuesto.test.tsx`, `DetalleFactura.test.tsx` y las de `web/src/presupuestos`.

**Logic**
- **FR-09:** el contenido de cada pantalla queda en una tarjeta con el fondo, el borde, el radio y la sombra de
  `.tarjeta-menu`. Las dos reglas comparten las variables.
- **FR-10:** campos con un borde y un radio comunes, `:focus-visible` con contorno del primario,
  `[aria-invalid=true]` con borde de error y `.campo__error` como texto de error debajo del campo.
- **FR-11:** el botón principal y el secundario tienen el mismo `min-height`, padding y radio. `:hover` oscurece con
  `color-mix`, `:focus-visible` muestra un contorno y `:disabled` baja la opacidad. Los botones de las filas tienen el
  mismo alto.
- **FR-12:** la tabla lleva encabezado con fondo y texto en negrita, separación entre filas y hover de fila. Los
  importes siguen alineados a la derecha. `.tabla-contenedor` conserva `overflow-x: auto`, y la tarjeta no lleva
  `overflow: hidden` (AC-14).
- **FR-13:** bloques con borde lateral, fondo y prefijo de texto por `::before`. El error usa "✕", la confirmación
  "✓" y el aviso y el estado "ℹ". Así se distinguen sin depender solo del color.
- **Restricciones:**
  - no cambian textos, roles, nombres, orden ni la estructura `h1` → `section.pantalla`;
  - `dl.datos > div` y `p.aviso` se conservan;
  - no hay ningún atributo `style`.

**Data model**
- Reuses `Articulo`, `Presupuesto` and `Factura` from FEAT-002a, FEAT-003a and FEAT-007 — no schema change; constraints as
  declared there: claves NOT NULL y únicas, importes decimal NOT NULL, estados NOT NULL. Solo cambia cómo se dibujan.

**Input validation**
- Sin entrada nueva: los campos existentes conservan sus validaciones. Solo cambia su aspecto, también el de
  `[aria-invalid=true]`.

**Error handling**
- Un campo con error de validación muestra el borde de error y el mensaje debajo, igual en todas las pantallas (error).

**Required tests**
- [ ] `contraste_MensajesCamposBotonesYTablas_DosMarcas` — validates NFR-01 y AC-16: error, éxito y aviso contra su
  fondo, texto del botón principal, encabezado y hover de tabla, foco de campo, con #0903A0 y #5A5A5A.
- [ ] `Botones secundarios por función llevan boton-secundario` — validates AC-13. Pruebas en las pantallas
  modificadas.
- [ ] `Presupuesto_MensajesDeEstado_ConClaseMensajeEstado` — validates FR-13.
- [ ] `Campo con error inválido muestra aria-invalid y el mensaje junto al campo` — validates AC-12. Lo cubren las
  pruebas existentes de las pantallas, que siguen en verde.

**Completion criterion**
- Vitest, lint, `test:lint` y build pasan.
- La cobertura web no baja.
- Ninguna prueba existente cambia, salvo las aserciones de clase que fija este bloque.

## Block 3 — e2e en Chrome y Edge


**Files**
- `e2e/tests/inicio-menu.spec.ts` (new): proyectos `chrome-inicio-menu` y `msedge-inicio-menu`.
- `e2e/tests/estilo-uniforme.spec.ts` (new): proyectos `chrome-estilo-uniforme` y `msedge-estilo-uniforme`. Recorre las
  15 pantallas sembrando un artículo, un presupuesto Final y una factura simulada por la Api.
- `e2e/tests/backup-aviso.spec.ts` (modified): `PANTALLAS_DEL_MENU` en el orden nuevo (D-11): Nuevo presupuesto,
  Buscar presupuestos, Facturas, Artículos, Nuevo artículo, Configuración, Cambiar la contraseña.
- `e2e/README.md` (modified): pasa a 54 proyectos, con la tabla de puertos y el párrafo de corrimientos actualizados.

**Logic**
- La prueba ingresa como en las demás e2e. Con la marca del harness (#0903A0 sobre #FFFFFF), mide en Inicio:
  - las secciones y su orden;
  - el clic en la tarjeta, fuera del texto, en una esquina;
  - el recorrido con Tab y el contorno computado;
  - el color computado del ícono;
  - el ancho de la página con ventanas de 360 px y 1280 px;
  - las posiciones de las tarjetas.
- Falla ante cualquier violación de CSP, como `backup-aviso`.

**Data model**
- Reuses `Articulo`, `Presupuesto` and `Factura` from FEAT-002a, FEAT-003a and FEAT-007 — no schema change; constraints as
  declared there: claves NOT NULL y únicas, importes decimal NOT NULL, estados NOT NULL. Los datos se siembran por la Api.

**Input validation**
- Sin entrada de usuario propia: solo cambia el ancho de la ventana (360 y 1280 px) y se recorre con Tab.

**Error handling**
- Una violación de CSP durante la prueba la hace fallar con error (R-02).

**Required tests**
- [ ] `muestra Ventas, Catálogo y Sistema con sus opciones` — validates AC-01 y AC-10.
- [ ] `clic en la tarjeta fuera del texto abre Facturas` — validates AC-02 y AC-10.
- [ ] `Tab recorre las tarjetas en orden con foco visible` — validates AC-04 y AC-10.
- [ ] `360 px una columna sin desplazamiento horizontal` — validates AC-05 y AC-10.
- [ ] `1280 px dos tarjetas o más en la misma fila` — validates AC-06 y AC-10.
- [ ] `íconos y foco con el primario de la marca` — validates AC-07 y AC-10.
- [ ] `sin violaciones de CSP ni error en la consola` — R-02.
- [ ] `backup-aviso` sigue en verde con el orden nuevo — AC-15 de FEAT-010 (regresión).
- [ ] `las 15 pantallas como tarjeta con 360 y 1280 px` — validates AC-11, AC-14 y AC-10: sin desplazamiento
  horizontal de la página y con el fondo, el radio y la sombra computados.
- [ ] `botones principales y secundarios con el mismo alto` — validates AC-13 y AC-10.
- [ ] `campo inválido con borde de error y foco del primario` — validates AC-12 y AC-10.
- [ ] `mensaje de error con el mismo bloque` — validates AC-15 y AC-10.
- [ ] `contraste y CSP en las 15 pantallas con la marca real` — validates AC-16 y AC-10; una violación de CSP es un
  error.

**Completion criterion**
`npm run test:unidad` y `npm run test:harness` pasan en Chrome y Edge, con los dos proyectos nuevos.

## Final verification
- La suite completa queda en verde: .NET sin cambios, web, e2e de unidad y harness.
- Los 10 AC tienen su prueba.
- `web/package.json` sigue sin cambios (NFR-03).
- Sin cambios de esquema: el rollback es revertir los commits del ticket.
