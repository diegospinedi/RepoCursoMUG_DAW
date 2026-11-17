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
