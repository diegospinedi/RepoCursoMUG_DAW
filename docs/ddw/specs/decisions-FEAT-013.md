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

## Decisiones del agente durante CODE (2026-11-18)

### D-19: en Artículos y en el buscador del presupuesto, el efecto de la pantalla hace el pedido
- Como pide el Block 2 ("la pausa alimenta la `consulta` que ya existe"), el hook aporta la pausa, la decisión y el
  descarte; el pedido, su número (`vigente`) y su `AbortController` siguen en el efecto de la pantalla.
- `alDescartar` cancela también el pedido real en curso, para que una respuesta tardía no cambie la tabla (D-07).
- El envío manual llama a `cancelar()` y programa la consulta sin sugerencias, sin depender del orden interno del hook.
- Es una excepción de esas dos pantallas. En el Block 3, presupuestos y facturas usan el hook completo
  (`buscar(clave, senal)` llama a la Api y `alResultado` tiene el `switch` de la pantalla), porque D-08 lo necesita.

### D-20: ruta de la búsqueda de facturas
- El spec dice `POST /api/facturas/busqueda`; la ruta real, que usa el código, es `POST /api/facturacion/busqueda`.
  No cambia nada: se deja anotado para el Block 3 y para VERIFY.

### D-21: la lógica de búsqueda en vivo queda duplicada en presupuestos y facturas
- Aprobó: el agente, por la delegación del usuario del 2026-11-14 (tomar la opción recomendada que menos tiempo
  lleve) — 2026-11-18 — Block 3.
- `decidir`, `buscar`, `alResultado`, el envío manual y sus refs quedan repetidos en `BuscarPresupuestos.tsx` y
  `BuscarFacturas.tsx` (unas 130 líneas), con las mismas correcciones aplicadas a las dos copias. Extraerlos a un hook
  compartido queda para otro ticket.

### D-22: un rango de fechas inválido vacía las sugerencias en el mismo evento
- Aprobó: el agente, por la misma delegación — 2026-11-18 — Block 3.
- D-07 se mantiene: cambiar las fechas no dispara una búsqueda. Pero si el cambio deja Desde > Hasta, las sugerencias
  se vacían en ese momento, para cumplir "un rango de fechas inválido en vivo no pide ni muestra nada" (spec, Block 3).

### D-23: supuestos del implementador en el Block 3, revisados por el module-verifier
- Aprobó: el agente, con el PASS del module-verifier — 2026-11-18 — Block 3.
- "Validar la búsqueda" en vivo es: texto UTF-16 válido, largo máximo de cada campo (100/100/20 en presupuestos, 100
  en facturas, iguales a la Api) y `validarRangoFechas`. "Buscar" no agrega validaciones nuevas.
- Las fechas no forman parte de la clave y se toman al vencer la pausa; si el rango es inválido, no se pide.
- Un campo que queda vacío vuelve a buscar con los otros filtros y no da sugerencias. Una respuesta viva que llega
  después de vaciar el campo tampoco las da (tampoco tras pasar a un rango de fechas inválido, D-22); esa respuesta
  sí actualiza la tabla: el pedido no se cancela.
- Riesgo aceptado (arch-auditor, ronda 3, WARN): si con un pedido vivo en curso se edita OTRO campo con un texto
  válido, la respuesta en curso puede mostrar sus sugerencias durante la pausa. Son un superconjunto del filtro nuevo
  y las reemplaza la respuesta siguiente. Es el diseño del hook del Block 1 (la pausa no cancela el pedido en curso),
  igual que en el Block 2.

### D-24: supuestos del implementador en el Block 4 (Configuración), revisados por las dos revisiones
- Aprobó: el agente, por la delegación del usuario del 2026-11-14, con el PASS del module-verifier y el APROBADO del
  arch-auditor — 2026-11-18 — Block 4.
- Grabar con el campo vacío muestra el mensaje de FR-14 ("Elegí una alícuota de la lista." / "Elegí una condición
  fiscal de la lista."), no el de D-06/D-35 de FEAT-007: el vacío no coincide con ninguna opción (D-13).
- Un 400 de la Api para la alícuota sigue con el mensaje fijo de D-35 (`MENSAJES_FIJOS`); FR-14 es la validación del
  navegador.
- El texto exacto se compara, plegado, solo contra la etiqueta visible: "10,5" se acepta y "10.5" no (sin prueba propia;
  WARN del module-verifier, aceptado).
- La prioridad entre el mensaje de FR-14 y el de `validarConfiguracion` queda en la pantalla, no en `reglas.ts`
  (WARN del arch-auditor, aceptado): solo la usa Configuración.
- `web/src/App.test.tsx` se actualizó (3 líneas) aunque no figuraba en el bloque, por D-18.
- `e2e/tests/medidas.ts:61` todavía nombra `select` en su selector de campos: se suma al Block 5 junto con los specs de
  D-18.

### D-25: supuestos y deuda aceptada del Block 5 (e2e y rendimiento)
- Aprobó: el agente, por la delegación del usuario del 2026-11-14, con el PASS del module-verifier y el APROBADO del
  arch-auditor — 2026-11-18 — Block 5.
- AC-13 en la e2e mide 1 pedido por pausa y el tiempo desde la última tecla (< 1000 ms y ≥ 300 ms) con pocos artículos;
  los 10.000 artículos van en `Volumen_BuscarArmazon_P95MenorA700ms` de la Api (D-17). Teclas cada 30 ms.
- Los errores de consola cuentan desde el ingreso (los 401 previos son esperables); las violaciones de CSP, en toda la
  prueba.
- `busqueda-en-vivo.spec.ts` depende del orden (la primera prueba necesita la base vacía; Borradores de relleno hasta el
  155) y lo declara con modo serie.
- AC-06 se prueba en la Modificación de un Borrador.
- `medidas.ts` deja de excluir `select` y `option`: en la web no queda ninguno.
- D-16 ya estaba cubierta por `BusquedaCancelada_NoRegistraErrorNoControlado` (Block 1); el backend no cambió.
- `estilo-uniforme.spec.ts` figura en el spec pero no necesitó cambios: usa `medidas.ts`, que sí se actualizó.
- Deuda aceptada (WARN del arch-auditor): la prueba de AC-13 puede fallar si una pausa entre teclas pasa de 300 ms en
  una máquina cargada; la prueba de volumen de 700 ms es un `[Fact]` aparte que vuelve a sembrar los 10.000 artículos;
  los specs viejos con búsqueda manual siguen expuestos a coincidir con un pedido en vivo en PC lentas (AC-22 de
  `facturas-busqueda.spec.ts` volvió a exigir exactamente 1 pedido); algunas líneas nuevas pasan de 120 columnas.
- Un 400 en vivo oculta las sugerencias sin mensaje; una falla de red o un 5xx muestran el mensaje general.
- "Buscar" y la paginación cuentan como envío manual (D-08). Un Enter durante un envío manual ahora envía otra
  búsqueda que reemplaza a la anterior; la respuesta vieja no pisa la nueva.
