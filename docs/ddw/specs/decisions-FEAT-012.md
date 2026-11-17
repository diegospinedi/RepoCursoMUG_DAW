# Decisiones FEAT-012: menú de Inicio moderno

Registro de las decisiones del ticket. Las que figuran como "usuario" las tomó el usuario. Las que figuran como "agente"
las tomó el agente bajo la delegación del usuario, que pidió "la recomendada y la que menos tiempo insuma".

## D-01: un solo ticket
- **Quién:** usuario, 2026-11-17.
- **Decisión:** el ticket va entero, con los 10 AC. Se acepta W-PRD-06.

## D-02: navegadores
- **Quién:** agente, siguiendo la decisión que el usuario tomó en FEAT-007 a FEAT-011.
- **Decisión:** se prueba con la versión estable vigente de Chrome y de Edge.

## D-03: el menú como datos
- **Quién:** agente.
- **Decisión:**
  - El menú se arma desde datos: `SECCIONES_MENU`, en `web/src/paginas/menuInicio.ts`, declarado `as const satisfies`.
  - El campo `icono` acepta solo los valores de un tipo cerrado, `NombreIcono`.
  - Los íconos son SVG en línea que se dibujan con `currentColor` y llevan `aria-hidden`. No llevan `<title>`, `<style>` ni atributo
    `style`.
  - No se suma ningún paquete.

## D-04: colores de la tarjeta
- **Quién:** agente.
- **Decisión:**
  - El nombre y la descripción van en `--color-texto`.
  - El primario de la marca va en el ícono, en la franja superior, en el borde del hover y en el contorno del foco.
  - El contraste del texto no depende de la marca.
- **Descarte:** se descartó elegir el color del nombre según el contraste, porque CSS no puede decidirlo.

## D-05: foco de la tarjeta
- **Quién:** agente.
- **Decisión:**
  - El foco se muestra una sola vez, en la tarjeta, con `.tarjeta-menu:has(a:focus-visible)`: un contorno de 3px del
    primario, separado 2px.
  - El enlace no muestra su propio contorno.
  - El contraste mínimo de 3:1 se mide contra `--color-fondo`.

## D-06: enlace estirado
- **Quién:** agente.
- **Decisión:**
  - La tarjeta (`<li>`) lleva `position: relative`.
  - El `<a>` contiene solo el nombre.
  - El `::after` del enlace cubre la tarjeta, y ningún elemento intermedio lleva `overflow` ni `transform`.
  - La descripción queda fuera del enlace y se le asocia con `aria-describedby`.
  - Los ids se generan con `useId()`.

## D-07: grilla y ancho
- **Quién:** agente.
- **Decisión:**
  - La grilla usa `repeat(auto-fill, minmax(min(13rem, 100%), 1fr))`.
  - La regla nueva `.marco:has(.pantalla--menu) { max-width: 48rem }` ensancha el marco solo en Inicio.
  - `.pantalla--ancha` no cambia.

## D-08: "Salir" y el orden de la pantalla
- **Quién:** agente.
- **Decisión:**
  - "Salir" sigue siendo un `<button>` con el nombre exacto "Salir". Se muestra como secundario, con la clase
    `boton-secundario`: borde del primario y fondo blanco.
  - Va en una barra al final, separado del menú.
  - El `h1` "Sesión iniciada" sigue siendo hijo directo de la sección de la pantalla.
  - Los avisos van antes del menú.

## D-09: descripciones de las tarjetas
- **Quién:** agente.
- **Decisión:** las descripciones evitan las palabras que vigilan otras pruebas:
  - "restaurar" y "restablecer" (`backup-aviso.spec.ts`);
  - "punto de venta" y "certificado" (`App.test.tsx`).

## D-10: verificación del contraste (NFR-01)
- **Quién:** agente.
- **Decisión:**
  - Una función pura, `contraste(a, b)`, en `web/src/marca/contraste.ts`, con pruebas sobre las dos paletas conocidas.
  - La e2e mide los colores computados con la marca del harness (#0903A0 sobre #FFFFFF).

## D-11: prueba de FEAT-010 afectada
- **Quién:** agente.
- **Decisión:** en `e2e/tests/backup-aviso.spec.ts`, `PANTALLAS_DEL_MENU` pasa al orden nuevo: Ventas, Catálogo y
  Sistema.

## D-12: nombre del archivo de datos del menú
- **Quién:** agente, 2026-11-17, Block 1.
- **Decisión:** los datos del menú quedan en `web/src/paginas/seccionesMenu.ts`, no en `menuInicio.ts`.
- **Motivo:**
  - En NTFS, que no distingue mayúsculas, `import './MenuInicio'` resolvía a `menuInicio.ts`, porque Vite prueba `.ts`
    antes que `.tsx`.
  - Ese import rompía Inicio.
  - Las referencias a `menuInicio.ts` en la spec, en D-03 y en el modelo de amenazas se leen como `seccionesMenu.ts`.

## Ampliación al estilo uniforme (vuelta de CODE, 2026-11-17)

Decisiones del agente bajo la delegación del usuario, salvo que se indique otra cosa.

### D-13: el estilo de todas las pantallas sale de la hoja global
- El estilo uniforme sale de `web/src/estilos.css`: variables de diseño sobre `:root`, con colores de error, éxito,
  bordes, radios, sombras y espaciados, y reglas para las clases que las pantallas ya usan.
- Se mantienen los nombres `--color-primario`, `--color-fondo` y `--color-texto`, porque los lee
  `acceso.spec.ts`.
- El markup se toca solo donde falta una clase.
- Ningún estilo va en línea: lo impide la CSP.

### D-14: botones secundarios marcados en el código
- Los botones secundarios por su función llevan la clase `boton-secundario` en el markup. Son 18 en 8 archivos:
  - paginación;
  - Desactivar y Activar;
  - Agregar y Quitar;
  - Volver a abrir;
  - Descargar PDF;
  - Descartar;
  - Cancelar.
- Se descartaron reglas que adivinan por el contexto, porque fallan con botones nuevos.
- Se quita `.tabla td button { padding }`, que achicaba los botones de las filas (AC-13).

### D-15: Inicio no lleva tarjeta exterior
- En Inicio, la tarjeta de FR-09 es cada opción del menú: `.pantalla--menu` no lleva la tarjeta exterior, para no
  quedar con tarjetas dentro de otra tarjeta.
- Las demás 14 pantallas se dibujan como tarjeta (`.pantalla`, `.pantalla--ancha`).

### D-16: "Cargando" también como tarjeta
- `.cargando` se dibuja como tarjeta con el mismo estilo de `.pantalla`.
- Así las 6 pantallas que muestran "Cargando…" fuera de la sección no necesitan cambios de markup.

### D-17: mensajes separados por tipo
- `.campo__error` deja de compartir regla con `.mensaje-general`: es texto de error junto al campo, sin bloque, y así
  no alcanza a los mensajes dentro de celdas.
- `.mensaje-general` es un bloque de error: borde lateral y fondo de error, con ícono de texto "✕" por CSS `::before`
  para no depender solo del color.
- `.mensaje-exito` es un bloque de éxito con "✓".
- `.aviso` es un bloque neutro con el primario.
- Los dos mensajes de estado sin clase de `Presupuesto.tsx` pasan a `.mensaje-estado`, un bloque neutro.
- Los "sin resultados" quedan como texto común.
- `div.mensaje-general` con un `p` adentro anula el margen interno.

### D-18: inputs de las tablas
- `.tabla input` recibe borde, radio, foco y el borde de error con `aria-invalid`, igual que `.campo input`.
- Los campos de solo lectura usan fondo gris y borde punteado, para que no parezcan editables.
- `input[type=file]` se dibuja con el botón del selector estilizado vía `::file-selector-button`.

### D-19: elementos que hoy no tienen estilo
- `<dialog>`: tarjeta centrada con sombra y fondo oscurecido (`::backdrop`).
- `fieldset` y `legend`: borde y radio, con la leyenda en negrita.
- `.pantalla h2`: el mismo estilo que los títulos de sección del menú.
- `dl.datos`: grilla de dos columnas, conservando `dl.datos > div > dt + dd`.

### D-20: verificación del estilo
- Una e2e nueva, `estilo-uniforme`, recorre las 15 pantallas en Chrome y Edge con 360 y 1280 px.
- Comprueba:
  - que la página no tenga desplazamiento horizontal;
  - que la tarjeta tenga el fondo, el radio y la sombra computados;
  - que los botones principales y secundarios tengan el mismo alto;
  - que no haya violaciones de CSP.
- `contraste.test.ts` suma los pares nuevos con las dos marcas.

## D-21: alto de los botones con texto largo
- **Quién:** agente, 2026-11-17, Block 3.
- **Decisión:** AC-13 ("mismo alto") se lee como el mismo alto mínimo, relleno y radio.
  - Un botón cuyo texto se parte en dos renglones en una pantalla angosta, como "Grabar ubicación del backup" con
    360 px, puede ser más alto.
  - A 1280 px todos quedan en un renglón y con el mismo alto.
- **Descarte:** se descartó cortar el texto o achicar la letra, porque empeora la lectura.

## D-22: datos que siembra la e2e
- **Quién:** agente, 2026-11-17, Block 3.
- **Decisión:** la e2e `estilo-uniforme` siembra también un presupuesto en Borrador, porque "Modificar presupuesto" de
  uno Final muestra solo un aviso.
