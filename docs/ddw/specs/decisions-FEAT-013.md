# Decisiones de FEAT-013: búsqueda con sugerencias mientras se escribe

Las que dicen "usuario" las tomó el usuario. Las que dicen "agente" se tomaron por la delegación del usuario: "la
recomendada y la que menos tiempo insuma".

## Decisiones del usuario (2026-11-17)
- **D-01:** se hace en un solo ticket, con los 14 AC; se acepta la advertencia W-PRD-06. Los buscadores comparten el
  componente y los desplegables, la lista.
- **D-02:** los buscadores sugieren y además filtran la tabla; los dos desplegables de Configuración pasan a ser un
  campo con filtro.
- **D-03:** se trabaja sobre la rama de FEAT-012. Después del merge del PR #22 se rebasó sobre `master`.
- **D-04:** se prueba solo en la versión estable vigente de Chrome y de Edge, igual que en los tickets anteriores.

## Decisiones del agente

### D-05: cancelación en el cliente de la Api
- `web/src/api/cliente.ts` acepta `señal?: AbortSignal` en `Opciones` y en `buscarArticulos`, `buscarPresupuestos`
  y `buscarFacturas`, y la pasa a `fetch`.
- Un `AbortError` devuelve `{tipo:'cancelado'}`, nunca la falla de red.
- Las pantallas ignoran `cancelado`.

### D-06: hook `useBusquedaEnVivo` (`web/src/componentes/useBusquedaEnVivo.ts`)
- Espera una pausa de 300 ms y un mínimo de 2 caracteres en el campo editado.
- Lleva un número de pedido creciente y un `AbortController`: la respuesta de un pedido que no es el último se
  descarta.
- Un envío manual ("Buscar" o Enter) cancela la pausa pendiente y el pedido en vivo, y usa el mismo número de pedido.
- Al desmontar, limpia el temporizador y cancela el pedido, también con StrictMode.
- Devuelve el `Resultado` del cliente sin traducir: cada pantalla conserva su `switch`, con `sinSesion` y `campos`
  incluidos.
- Un texto nuevo vuelve a la página 1.

### D-07: cuándo dispara la búsqueda en vivo
- **Artículos y buscador del presupuesto:**
  - con 2 o más caracteres, busca en vivo;
  - con el campo vacío, vuelve a la lista inicial, la misma de abrir la pantalla, sin sugerencias;
  - con 1 carácter, no pide nada y deja la tabla como estaba.
- **Presupuestos y facturas:**
  - busca en vivo cuando el campo de texto editado tiene 2 o más caracteres o queda vacío;
  - cambiar Desde o Hasta no dispara la búsqueda: aplica con "Buscar";
  - si `validarBusqueda` o `validarRangoFechas` fallan, no pide ni muestra nada en vivo, y el error aparece al apretar
    "Buscar";
  - un 400 en vivo oculta las sugerencias sin mostrar el error del campo.

### D-08: la guarda `enCurso` se reemplaza
- En presupuestos y facturas, la guarda `enCurso` se reemplaza por el número de pedido.
- `aplicado` es el filtro de la última respuesta aceptada.
- La búsqueda en vivo no deshabilita el botón "Buscar". El botón se deshabilita solo durante un envío manual, para
  evitar el doble clic.

### D-09: sugerencia elegida en el presupuesto
- Elegir una sugerencia en el buscador del presupuesto pasa por `agregar(articulo)`, que rechaza el precio de venta
  negativo con `MENSAJE_PRECIO_NEGATIVO` (FR-12 de FEAT-003a).
- Nunca llama directo a `alAgregar`.

### D-10: teclado
- Los campos son `type="text"` dentro del `form role="search"`, para que Escape no los vacíe solo.
- **Escape:** con la lista abierta, la cierra con `preventDefault`; con la lista cerrada, no hace nada.
- **Enter:** con una opción marcada, la elige con `preventDefault`; sin opción marcada, busca como hoy.
- **Tab** cierra la lista sin elegir.
- **Mouse:** `onMouseDown` + `preventDefault` solo para no perder el foco; se elige en `onClick`.
- La lista se cierra al perder el foco.

### D-11: ARIA
- `aria-activedescendant` aparece solo con la lista abierta y una opción marcada.
- Los ids de las opciones son estables: el prefijo de `useId()` más el código o el número.
- Con sugerencias nuevas, la marca vuelve a "ninguna".
- El `listbox` lleva `aria-labelledby` con la etiqueta.
- Con varios campos que comparten resultado, solo el que tiene el foco lleva `aria-expanded=true` y anuncia.
- La región `aria-live` está siempre montada y dice solo la cantidad ("8 sugerencias").

### D-12: estructura de los componentes
- Una base compartida, `ListaSugerencias` (`web/src/componentes/ListaSugerencias.tsx`), maneja la lista navegable,
  el teclado y ARIA.
- La usan `CampoBusqueda`, con sugerencias de la Api, y `CampoConOpciones`, con filtrado local.
- Los dos se usan dentro de `Campo`, con su render-prop (id, `aria-invalid`, `aria-describedby`).
- La función local `CampoBusqueda` de `BuscarPresupuestos.tsx` pasa a llamarse `CampoFiltro`.

### D-13: `CampoConOpciones` en Configuración
- No tiene mínimo de caracteres. Con el campo vacío, flecha abajo abre la lista completa.
- Muestra la etiqueta y graba el código. En condición fiscal usa `ETIQUETAS_CONDICION`; en alícuota, el decimal con
  coma ("10,5").
- Un texto que coincide exacto con una opción, comparado plegado, se acepta sin elegirlo de la lista.
- Una alícuota fuera de la lista empieza con el texto vacío y el aviso de hoy.
- Con un texto que no coincide al grabar, muestra "Elegí una alícuota de la lista." o "Elegí una condición fiscal de
  la lista.".

### D-14: plegado en la web
- `plegar(texto)` vive en `web/src/catalogo/reglas.ts`, alineada con `PlegadoBusqueda.cs`:
  - recorta los espacios;
  - normaliza con NFD;
  - quita las marcas, salvo la de la ñ;
  - pasa a minúsculas.

### D-15: datos personales
- Las sugerencias de presupuestos y de facturas no muestran el DNI.
- El anuncio dice solo la cantidad.
- No se escribe nada en la consola.
- Los campos conservan `autoComplete="off"`.
- Los filtros siguen yendo en el cuerpo y no en la URL (ADR-019).

### D-16: pedidos cancelados en la Api
- Una prueba de integración confirma que un pedido cancelado no deja el registro "Error no controlado".
- Si lo deja, `ManejadorErroresNoControlados` trata `OperationCanceledException` con el pedido abortado como fin
  silencioso. Es el único cambio de backend posible.

### D-17: rendimiento (NFR-01, AC-13)
- `VolumenCatalogoTests` suma el escenario "armazon" con 10.000 artículos y p95 < 700 ms en la Api.
- La e2e mide que haya 1 pedido por pausa y el tiempo hasta las sugerencias.
- La pausa de 300 ms más los 700 ms dan el segundo de NFR-01.

### D-18: pruebas existentes
- El helper `diferida` se mueve a `web/src/pruebas`.
- `servidorFalso` respeta `init.signal` y rechaza con `AbortError`.
- Las pruebas que fijaban "no busca en cada tecla", "no envía otra en curso", el `select` o un único pedido se
  actualizan al comportamiento nuevo, sin borrar lo que cubrían.
