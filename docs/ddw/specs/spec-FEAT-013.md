# Spec FEAT-013: Búsqueda con sugerencias mientras se escribe

| Field | Value |
|-------|-------|
| Ticket | FEAT-013 |
| PRD | docs/ddw/prd/prd-FEAT-013.md |
| Tier | FEATURE |
| Date | 2026-11-17 |
| Spec loops | 1 |
| Loops since last human decision | 1 |

## Summary
- **Base compartida:** `ListaSugerencias` resuelve la lista navegable, el teclado y ARIA 1.2.
- **Dos usos de esa base:** `CampoBusqueda` (sugerencias de la Api) y `CampoConOpciones` (filtrado local).
- **Hook `useBusquedaEnVivo`:** pausa de 300 ms, mínimo de 2 caracteres, número de pedido y `AbortController`.
- **Cliente de la Api:** `cliente.ts` acepta una señal de cancelación.
- **Pantallas de búsqueda:** en las cuatro, una sola búsqueda llena la tabla y las 8 sugerencias.
- **Configuración:** los dos `select` pasan a ser campos con filtro.
- **Sin dependencias.** Los endpoints no cambian; solo se confirma que un pedido cancelado no deja error en el log.
- **Referencias:** decisiones D-01 a D-18 en `docs/ddw/specs/decisions-FEAT-013.md`, ADR-032, amenazas R-01 a R-05 en
  `docs/ddw/security/threat-FEAT-013.md`.

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 2 |
| FR-02 | Block 2 |
| FR-03 | Block 2 |
| FR-04 | Block 3 |
| FR-05 | Block 3 |
| FR-06 | Block 3 |
| FR-07 | Block 3 |
| FR-08 | Block 2 |
| FR-09 | Block 1, Block 5 |
| FR-10 | Block 2, Block 3 |
| FR-11 | Block 1, Block 3 |
| FR-12 | Block 1, Block 2, Block 3 |
| FR-13 | Block 1, Block 4 |
| FR-14 | Block 4 |
| NFR-01 | Strategy: escenario "armazon" con 10.000 artículos y p95 < 700 ms en la Api, más 300 ms de pausa; la e2e mide el tiempo hasta las sugerencias (D-17, Block 5) |
| NFR-02 | Strategy: pausa y número de pedido en el hook; pruebas de Vitest con temporizadores falsos y una e2e que cuenta los pedidos (Block 1, Block 5) |
| NFR-03 | Strategy: ARIA 1.2 en `ListaSugerencias` (D-10, D-11), probado en Vitest y en la e2e con teclado |
| NFR-04 | Strategy: e2e `busqueda-en-vivo` en Chrome y Edge estables (Block 5) |
| NFR-05 | Strategy: componentes propios (ADR-032); `package.json` sin cambios |

## Dependencies between blocks
Los Blocks 2, 3 y 4 dependen del 1. El 5 depende de todos. Orden: 1 → 2 → 3 → 4 → 5.

## Block 1 — Base: cancelación, hook y componentes

**Files**
- `web/src/api/cliente.ts` (modified): `señal?: AbortSignal` en `Opciones` y en `buscarArticulos`,
  `buscarPresupuestos` y `buscarFacturas`; `AbortError` → `{tipo:'cancelado'}` (D-05). Pruebas en `cliente.test.ts`.
- `web/src/catalogo/reglas.ts` (modified): `plegar(texto)` (D-14), con prueba.
- `web/src/componentes/useBusquedaEnVivo.ts` (new), `ListaSugerencias.tsx` (new), `CampoBusqueda.tsx` (new),
  `CampoConOpciones.tsx` (new), con sus pruebas.
- `web/src/pruebas/servidorFalso.tsx` (modified): respeta `init.signal`. `web/src/pruebas/diferida.ts` (new): helper
  movido (D-18).
- `web/src/estilos.css` (modified): `.campo-busqueda`, `.sugerencias`, `.sugerencia`,
  `.sugerencia[aria-selected='true']`. Las transiciones nuevas entran en el bloque de movimiento reducido.
- `tests/Optica.Api.Tests/Catalogo/BusquedaCanceladaTests.cs` (new) y, si hace falta,
  `src/Optica.Api/Errores/ManejadorErroresNoControlados.cs` (modified) (D-16).

**Logic**
- **Hook (D-06):**
  - Recibe una función `buscar(texto, señal)` y el texto. Con una pausa de 300 ms y 2 caracteres o más, pide.
  - Incrementa el número de pedido y cancela el anterior. Acepta solo la respuesta del último.
  - `enviarAhora()` cancela la pausa y el pedido en curso y busca enseguida.
  - En el cleanup limpia el temporizador y cancela el pedido.
- **`ListaSugerencias` (D-10, D-11, D-12):** combobox ARIA 1.2.
  - Atributos del campo: `aria-autocomplete="list"`, `aria-expanded`, `aria-controls`; `aria-activedescendant` solo
    con una opción marcada.
  - Lista: `listbox` con `aria-labelledby` y opciones con id estable.
  - Teclado y mouse:
    - flechas recorren la lista;
    - Enter con una opción marcada la elige, con `preventDefault`;
    - Escape con la lista abierta la cierra, con `preventDefault`;
    - Tab y la pérdida del foco cierran la lista;
    - con el mouse, `onMouseDown` + `preventDefault` y se elige en `onClick`.
  - Anuncio: una región `aria-live` siempre montada dice solo "N sugerencias".
  - Muestra como mucho 8.
- **Los dos componentes:** se usan dentro de `Campo` y suman los atributos de su render-prop.
  - `CampoBusqueda` muestra sugerencias externas.
  - `CampoConOpciones` filtra opciones fijas con `plegar`. No tiene mínimo de caracteres y flecha abajo con el campo
    vacío abre la lista completa.

**API contract**
- Method + path: sin endpoints nuevos. Se reutilizan `GET /api/articulos`, `POST /api/presupuestos/busqueda` y
  `POST /api/facturas/busqueda`, que ahora reciben pedidos cancelables.
- Request: los mismos parámetros y cuerpos de hoy.
- Response: las mismas páginas de 50.
- Error codes: 400 (validación), 401 (sin sesión), 500 (error del servidor). Un pedido cancelado no tiene código.
- Auth: sesión de la operadora, sin cambios.

**Data model**
- Reuses `Articulo`, `Presupuesto` y `Factura` de FEAT-002a, FEAT-003a y FEAT-007. Sin cambios de esquema; restricciones
  como allí se declararon: claves NOT NULL y únicas, importes decimal NOT NULL, estados NOT NULL.

**Input validation**
- Texto del buscador: el mismo largo máximo de hoy (200 en artículos, 100 en apellido y nombre, 20 en DNI). En vivo
  se pide solo con 2 caracteres o más.
- `plegar`: acepta cualquier texto. Un valor que no es texto se trata como vacío.

**Error handling**
- Un pedido cancelado devuelve `cancelado`: no se muestra error.
- Una falla de red o un 500 oculta las sugerencias y devuelve el error, que la pantalla muestra (FR-12).
- Un pedido cancelado en la Api no deja el registro "Error no controlado" (D-16).

**Required tests**
- [ ] `cliente_BusquedaCancelada_DevuelveCanceladoNoError` — un pedido cancelado no es un error de red (sad path).
- [ ] `useBusquedaEnVivo_UnPedidoPorPausaYMinimoDosCaracteres` — validates NFR-02 y AC-02.
- [ ] `useBusquedaEnVivo_RespuestaViejaDescartada` — validates AC-09.
- [ ] `useBusquedaEnVivo_EnviarAhoraCancelaLaPausa` — validates FR-10 y NFR-02.
- [ ] `useBusquedaEnVivo_ErrorDeRed_SinSugerencias` — validates AC-10 (error).
- [ ] `ListaSugerencias_TecladoFlechasEnterEscapeTab` — validates AC-07.
- [ ] `ListaSugerencias_AriaActivedescendantYAnuncioSoloCantidad` — validates NFR-03 y R-02.
- [ ] `ListaSugerencias_HtmlEnSugerenciaSeVeComoTexto` — R-04 (inválido).
- [ ] `CampoConOpciones_FiltraPlegadoYFlechaAbreLista` — validates AC-11.
- [ ] `plegar_AcentosMayusculasYEnie` — D-14.
- [ ] `BusquedaCancelada_NoRegistraErrorNoControlado` — R-05 (error).

**Completion criterion**
- Vitest, lint, `test:lint` y build pasan, con la cobertura web en 80 % o más.
- La prueba de .NET pasa.

## Block 2 — Artículos y buscador del presupuesto

**Files**
- `web/src/paginas/Articulos.tsx` y su prueba (modified).
- `web/src/presupuestos/BuscadorArticulos.tsx` (modified), con pruebas en `NuevoPresupuesto.test.tsx` y
  `EditarPresupuesto.test.tsx` (modified).

**Logic**
- **Campo:** "Texto a buscar" pasa a `CampoBusqueda` (`type="text"`, D-10).
- **Búsqueda:** la pausa alimenta la `consulta` que ya existe (con `vigente`). La misma página 1 llena la tabla y las
  sugerencias: código, código en el proveedor y descripción.
- **Campo vacío y 1 carácter:** con el campo vacío vuelve a la lista inicial; con 1 carácter no pide (D-07).
- **Elegir una sugerencia:**
  - en Artículos navega a `/articulos/{codigo}` (FR-03);
  - en el presupuesto llama a `agregar(articulo)` (D-09, FR-08), que rechaza el precio negativo.
- **Botón y Enter:** "Buscar" y Enter sin una opción marcada usan `enviarAhora()` y cierran la lista.

**Data model**
- Reuses `Articulo` de FEAT-002a. Sin cambios de esquema; restricciones como allí se declararon: clave NOT NULL y única,
  costo y venta NOT NULL.

**Input validation**
- Texto: hasta 200 caracteres, como hoy. Se recorta con `recortar`.

**Error handling**
- Una sugerencia con precio de venta negativo no se agrega y muestra `MENSAJE_PRECIO_NEGATIVO` (error).
- Una falla de la búsqueda muestra el mensaje de hoy y oculta las sugerencias (error).

**Required tests**
- [ ] `Articulos_EscribirArm_SugerenciasYTablaFiltrada` — validates AC-01.
- [ ] `Articulos_UnCaracter_SinSugerencias` — validates AC-02.
- [ ] `Articulos_ElegirSugerencia_AbreLaEdicion` — validates AC-03.
- [ ] `Articulos_Buscar_ComoAntes` — validates AC-08.
- [ ] `Articulos_ErrorDeBusqueda_SinSugerencias` — validates AC-10 (error).
- [ ] `NuevoPresupuesto_ElegirSugerencia_AgregaLaLinea` — validates AC-06.
- [ ] `NuevoPresupuesto_SugerenciaConPrecioNegativo_Rechazada` — FR-12 de FEAT-003a (error).

**Completion criterion**
- Pasan las pruebas, incluidas las actualizadas de "no busca en cada tecla".

## Block 3 — Buscar presupuestos y Facturas

**Files**
- `web/src/paginas/BuscarPresupuestos.tsx` y su prueba (modified). La función local `CampoBusqueda` pasa a llamarse
  `CampoFiltro` (D-12).
- `web/src/paginas/BuscarFacturas.tsx` y su prueba (modified).

**Logic**
- **Campos:** Apellido, Nombre y DNI (en presupuestos) y los campos de texto de facturas pasan a `CampoBusqueda`.
- **Búsqueda combinada:** combina todos los filtros, fechas incluidas. Dispara cuando el campo editado tiene 2
  caracteres o más o queda vacío.
- **Validaciones (D-07):**
  - cambiar las fechas no dispara: aplica con "Buscar";
  - si fallan `validarBusqueda` o `validarRangoFechas`, no se pide;
  - un 400 en vivo oculta las sugerencias.
- **Pedidos (D-08):**
  - la guarda `enCurso` se reemplaza por el número de pedido;
  - `aplicado` es el filtro de la última respuesta aceptada;
  - "Buscar" se deshabilita solo durante un envío manual.
- **Sugerencias:**
  - presupuestos: número, apellido y nombre, y fecha; elegir una abre `/presupuestos/{numero}` (FR-04, FR-05);
  - facturas: comprobante, cliente y fecha; elegir una abre `/facturas/{presupuesto}` (FR-06, FR-07);
  - sin DNI (D-15).
- **Foco:** solo el campo con el foco abre la lista y anuncia.

**API contract**
- Method + path: `POST /api/presupuestos/busqueda` y `POST /api/facturas/busqueda`, sin cambios (ADR-019: filtros en el
  cuerpo).
- Request: el filtro de hoy (fechas, apellido, nombre, DNI, página).
- Response: la página de hoy.
- Error codes: 400, 401 y 500, como hoy.
- Auth: sesión.

**Data model**
- Reuses `Presupuesto` y `Factura` de FEAT-003a y FEAT-007. Sin cambios de esquema; restricciones como allí se
  declararon: número NOT NULL y único, estado NOT NULL, CAE NOT NULL en facturas.

**Input validation**
- Largos máximos de hoy (100, 100 y 20), fechas válidas y Desde ≤ Hasta. En vivo, una entrada inválida no se envía.

**Error handling**
- Un rango de fechas inválido en vivo no pide ni muestra nada; al apretar "Buscar" muestra el error de hoy (error).
- Una falla de la búsqueda muestra el mensaje de hoy y oculta las sugerencias (error).

**Required tests**
- [ ] `BuscarPresupuestos_EscribirApellido_SugerenciasYAbrir155` — validates AC-04.
- [ ] `BuscarPresupuestos_RespuestaViejaNoPisaLaNueva` — validates AC-09.
- [ ] `BuscarPresupuestos_SugerenciaSinDni` — R-02 (datos personales).
- [ ] `BuscarPresupuestos_RangoInvalidoEnVivo_SinPedido` — D-07 (inválido).
- [ ] `BuscarFacturas_EscribirApellido_SugerenciasYAbrirLaFactura` — validates AC-05.
- [ ] `BuscarFacturas_ErrorDeBusqueda_SinSugerencias` — validates AC-10 (error).

**Completion criterion**
- Pasan las pruebas, incluidas las actualizadas de "no envía otra en curso".

## Block 4 — Configuración con campos de filtro

**Files**
- `web/src/paginas/Configuracion.tsx` y su prueba (modified).
- `web/src/configuracion/reglas.ts` (modified), solo si hace falta para el mensaje de FR-14.
- `web/src/estilos.css` (modified): quitar las reglas de `.campo select` que queden sin uso.

**Logic**
- **Reemplazo:** los `select` de alícuota y condición fiscal pasan a `CampoConOpciones` (D-13).
- **Etiqueta y código:** se muestra la etiqueta y se graba el código (`ETIQUETAS_CONDICION`; alícuota con coma).
- **Texto exacto:** un texto que coincide exacto, comparado plegado, se acepta.
- **Alícuota fuera de la lista:** el campo empieza vacío y con el aviso de hoy.

**Data model**
- Reuses `ConfiguracionNegocio` de FEAT-001c. Sin cambios de esquema; restricciones como allí se declararon: alícuota y
  condición NOT NULL, fila única.

**Input validation**
- Solo se aceptan opciones de la lista: las 6 alícuotas de ARCA y las 2 condiciones.

**Error handling**
- Un texto que no coincide al grabar no se graba y muestra "Elegí una alícuota de la lista." o "Elegí una condición
  fiscal de la lista." junto al campo (error).

**Required tests**
- [ ] `Configuracion_Alicuota10_SoloOpcion105` — validates AC-11.
- [ ] `Configuracion_CondicionInexistente_RechazadaSinGrabar` — validates AC-12 (error).
- [ ] `Configuracion_AlicuotaFueraDeLista_CampoVacioYAviso` — D-13.

**Completion criterion**
- Pasan las pruebas, incluidas las actualizadas que usaban el `select`.

## Block 5 — e2e y rendimiento

**Files**
- `e2e/tests/busqueda-en-vivo.spec.ts` (new): proyectos `chrome-busqueda-en-vivo` y `msedge-busqueda-en-vivo`.
- Specs actualizados (D-18): `configuracion.spec.ts`, `configuracion-recalculo.spec.ts`, `articulos.spec.ts`,
  `articulos-edicion.spec.ts`, `presupuestos-busqueda.spec.ts`, `facturas-busqueda.spec.ts`, `presupuestos-alta.spec.ts`,
  `presupuestos-calculos.spec.ts`, `presupuestos-concurrencia.spec.ts`, `presupuestos-edicion.spec.ts`,
  `estilo-uniforme.spec.ts` (los que usan `selectOption`, cuentan un pedido o buscan antes de Agregar).
- `e2e/README.md` (modified).
- `tests/Optica.Api.Tests/Catalogo/VolumenCatalogoTests.cs` (modified): escenario "armazon" (D-17).

**Logic**
- La e2e siembra artículos, presupuestos y una factura simulada por la Api.
- Escribe en los cuatro buscadores y recorre la lista con el teclado. Verifica que la tabla se filtra, que se abre o se
  agrega lo elegido, el anuncio y que haya 1 pedido por pausa.
- En Configuración, escribe y elige.
- La prueba de volumen de la Api mide p95 < 700 ms con 10.000 artículos.

**Data model**
- Reuses `Articulo`, `Presupuesto` y `Factura` de FEAT-002a, FEAT-003a y FEAT-007. Sin cambios de esquema; restricciones
  como allí se declararon: claves NOT NULL y únicas, importes decimal NOT NULL, estados NOT NULL. Los datos ficticios se
  siembran por la Api.

**Input validation**
- Sin entrada propia: textos de búsqueda y teclado.

**Error handling**
- Una violación de la CSP o un error en la consola durante la e2e la hace fallar (error).

**Required tests**
- [ ] `sugiere y filtra artículos y abre la edición` — validates AC-01, AC-03 y AC-14.
- [ ] `sugiere presupuestos y facturas y los abre` — validates AC-04, AC-05 y AC-14.
- [ ] `agrega la sugerencia al presupuesto` — validates AC-06 y AC-14.
- [ ] `teclado y anuncio de sugerencias` — validates AC-07 y AC-14.
- [ ] `un pedido por pausa y sugerencias en menos de un segundo` — validates AC-13 y AC-14.
- [ ] `configuración filtra la alícuota y rechaza una condición inexistente` — validates AC-11, AC-12 y AC-14.
- [ ] `Buscar sigue funcionando` — validates AC-08 y AC-14.
- [ ] `Volumen_BuscarArmazon_P95MenorA700ms` — validates AC-13 (Api).
- [ ] `sin violaciones de CSP ni error en la consola` — R-04 (error).

**Completion criterion**
- `npm run test:unidad` y `npm run test:harness` pasan en Chrome y Edge, y la prueba de volumen pasa.

## Final verification
- La suite completa queda en verde.
- Los 14 AC tienen su prueba.
- No cambia ningún `package.json`.
- Sin cambios de esquema: el rollback es revertir los commits.
