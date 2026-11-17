# PRD FEAT-012: Menú de Inicio moderno y estilo uniforme

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-012 |
| Tracker | ninguno |
| Fecha | 2026-11-17 |
| PRD loops | 2 |
| Loops since last human decision | 0 |

> **Origen.** El 2026-11-17 el usuario pidió "hacer este menú un poco más moderno y atractivo y no tan feo y aburrido"
> sobre la pantalla de Inicio. Acompañó el pedido con una captura que muestra siete enlaces subrayados, uno debajo del
> otro, y el botón "Salir". Las decisiones de diseño las tomó el agente, por la delegación del usuario ("la recomendada
> y la que menos tiempo insuma"), y quedan registradas en `decisions-FEAT-012.md`.
>
> **Alcance decidido.** El 2026-11-17 el usuario decidió hacerlo en un solo ticket, con sus 10 AC (W-PRD-06
> aceptado): es una sola pantalla y las partes no se pueden entregar por separado.
>
> **Ampliación (vuelta de CODE, decisión del usuario del 2026-11-17).** Con el menú ya implementado, el usuario
> señaló que "las pantallas están horribles, salvo el menú que cambiaste" y pidió que el sitio sea uniforme. Ante la
> pregunta, eligió ampliar este ticket en lugar de abrir otro. Se suman FR-09 a FR-13 y AC-11 a AC-16. Las demás
> pantallas dejan de estar fuera de alcance, solo en su aspecto: textos, flujos y datos no cambian. Sigue siendo un
> solo ticket (W-PRD-06 aceptado por la misma decisión).

## Context and Problem
Después de ingresar, la pantalla de Inicio muestra las siete opciones del sistema como enlaces de texto subrayados en
una sola columna:
- Cambiar la contraseña;
- Configuración;
- Artículos;
- Nuevo artículo;
- Nuevo presupuesto;
- Buscar presupuestos;
- Facturas.

Las opciones no están agrupadas ni tienen jerarquía visual. Las tareas diarias de la óptica (presupuestos y facturas)
tienen el mismo peso que las de mantenimiento (contraseña y configuración). El usuario la percibe fea y aburrida, y le
cuesta encontrar de un vistazo lo que usa todos los días.

## Goals
- Que la operadora reconozca en menos de un vistazo dónde está cada tarea.
- Que el menú se vea moderno y use los colores de la marca instalada.
- Que los avisos del ingreso sigan siendo lo primero que se ve: backup faltante y emisiones pendientes.

## Functional Requirements
- FR-01: El sistema debe mostrar las siete opciones de Inicio agrupadas en tres secciones con título:
  - "Ventas": Nuevo presupuesto, Buscar presupuestos y Facturas.
  - "Catálogo": Artículos y Nuevo artículo.
  - "Sistema": Configuración y Cambiar la contraseña.
- FR-02: El sistema debe mostrar cada opción como una tarjeta. La tarjeta tiene un ícono, el nombre de la opción y una
  línea breve que describe para qué sirve. Toda la superficie de la tarjeta lleva a la pantalla de esa opción.
- FR-03: El sistema debe conservar el nombre visible y el destino de cada una de las siete opciones actuales.
- FR-04: El sistema debe resaltar la tarjeta al pasar el puntero y al recibir el foco del teclado, con un indicador de
  foco visible.
- FR-05: El sistema debe acomodar las tarjetas en una sola columna en pantallas angostas y en varias columnas en
  pantallas anchas, sin desplazamiento horizontal.
- FR-06: El sistema debe usar el color primario de la marca instalada en los íconos y en el resaltado de las
  tarjetas.
- FR-07: El sistema debe mostrar los avisos del ingreso (backup faltante, emisiones pendientes y mensajes de la
  sesión) antes de las secciones del menú.
- FR-08: El sistema debe ofrecer "Salir" como una acción separada de las tarjetas del menú.

### Estilo uniforme en todas las pantallas
- FR-09: El sistema debe mostrar el contenido principal de cada pantalla dentro de una tarjeta con el mismo fondo,
  borde, radio y sombra que las tarjetas del menú. Esto vale para las 15 pantallas: ingreso, definir y cambiar la
  contraseña, Inicio, configuración, artículos, alta, edición y actualización de costos, búsqueda, alta, detalle y
  edición de presupuestos, y búsqueda y detalle de facturas.
- FR-10: El sistema debe mostrar los campos y desplegables con un mismo estilo en todas las pantallas:
  - borde y radio iguales;
  - indicador de foco visible del color primario;
  - en un campo con error, el borde en el color de error y el mensaje junto al campo.
- FR-11: El sistema debe mostrar los botones con dos estilos únicos en todas las pantallas: principal (relleno del
  primario) y secundario (borde del primario). Los dos tienen el mismo alto y radio, hover, foco visible y estado
  deshabilitado.
- FR-12: El sistema debe mostrar las tablas con un mismo estilo en todas las pantallas:
  - encabezado destacado;
  - separación entre filas;
  - resaltado de la fila al pasar el puntero;
  - importes alineados a la derecha;
  - desplazamiento horizontal dentro de su contenedor, sin que se desplace la página.
- FR-13: El sistema debe mostrar los avisos, los mensajes de error y los mensajes de confirmación con un mismo estilo
  de bloque en todas las pantallas, distinguibles entre sí por color y sin depender solo del color.

## Non-Functional Requirements
- NFR-01: El texto de las tarjetas, de los campos, de los botones, de las tablas y de los mensajes debe tener un contraste de al menos 4,5:1 contra su fondo, y el indicador de foco
  de al menos 3:1 (WCAG 2.1 AA), con la marca de ejemplo y con la marca real (primario #0903A0 sobre fondo #FFFFFF).
- NFR-02: Las pantallas deben verse y funcionar igual en la versión estable vigente de Chrome y de Edge, con anchos de
  ventana de 360 px y de 1280 px.
- NFR-03: El cambio no debe agregar ninguna dependencia nueva a la interfaz web (0 paquetes nuevos).

## Acceptance Criteria
- AC-01 (FR-01): WHEN la operadora ingresa al sistema, THE sistema SHALL mostrar en Inicio las secciones "Ventas",
  "Catálogo" y "Sistema", cada una con sus opciones según FR-01.
- AC-02 (FR-02, FR-03): WHEN la operadora hace clic en cualquier lugar de la tarjeta "Facturas", THE sistema SHALL
  abrir la pantalla de búsqueda de facturas, igual que el enlace "Facturas" anterior.
- AC-03 (FR-03): WHEN se busca en Inicio un enlace por cada uno de los siete nombres actuales, THE sistema SHALL
  encontrar exactamente un enlace con ese nombre, que lleva al mismo destino que antes.
- AC-04 (FR-04): WHEN la operadora recorre el menú con la tecla Tab, THE sistema SHALL mostrar un indicador de foco
  visible en cada tarjeta, en el orden Ventas, Catálogo y Sistema.
- AC-05 (FR-05): WHEN la ventana mide 360 px de ancho, THE sistema SHALL mostrar las tarjetas en una sola columna y sin
  barra de desplazamiento horizontal.
- AC-06 (FR-05): WHEN la ventana mide 1280 px de ancho, THE sistema SHALL mostrar al menos dos tarjetas en la misma
  fila.
- AC-07 (FR-06, NFR-01): WHILE la marca instalada es la real (primario #0903A0), THE sistema SHALL pintar los íconos
  con ese color y mantener el contraste de NFR-01.
- AC-08 (FR-07): IF falta el backup de ayer o hay emisiones pendientes, THEN THE sistema SHALL mostrar esos avisos
  antes de la primera sección del menú.
- AC-09 (FR-08): WHEN la operadora activa "Salir", THE sistema SHALL cerrar la sesión y volver al ingreso, como hoy.
- AC-11 (FR-09): WHEN se abre cualquiera de las 15 pantallas de FR-09, THE sistema SHALL mostrar su contenido principal
  dentro de una tarjeta con el fondo, el borde, el radio y la sombra de las tarjetas del menú.
- AC-12 (FR-10): IF un campo tiene un error de validación, THEN THE sistema SHALL mostrar su borde en el color de error y
  el mensaje junto al campo, y al recibir el foco un contorno del color primario, igual en todas las pantallas.
- AC-13 (FR-11): WHEN una pantalla muestra un botón principal o secundario, THE sistema SHALL dibujarlo con el mismo
  alto, radio y colores que el mismo tipo de botón en las demás pantallas.
- AC-14 (FR-12): WHILE la ventana mide 360 px de ancho, THE sistema SHALL mostrar las tablas con su desplazamiento
  horizontal dentro del contenedor, sin barra de desplazamiento horizontal en la página.
- AC-15 (FR-13): IF una pantalla muestra un mensaje de error, THEN THE sistema SHALL dibujarlo con el mismo bloque, un
  borde lateral y un fondo del color de error, que en las demás pantallas.
- AC-16 (FR-09, NFR-01): WHEN se recorren las 15 pantallas con la marca real, THE sistema SHALL cumplir el contraste de
  NFR-01 en textos, campos, botones y mensajes, sin violaciones de la política de seguridad de contenido.
- AC-10 (NFR-02): WHEN se ejecutan AC-01 a AC-09 y AC-11 a AC-16 en Chrome y en Edge, en su versión estable vigente, THE sistema SHALL
  cumplirlos en los dos navegadores.

## Out of Scope
- Cambiar textos, flujos, datos o el orden de los campos de las demás pantallas: solo cambia su aspecto.
- Cambiar el PDF de presupuestos y facturas.
- Agregar opciones nuevas al menú o quitar alguna, por ejemplo "Actualizar costos con planilla", que se sigue
  abriendo desde Artículos.
- Cambiar el título "Sesión iniciada", el encabezado con el logo o los textos de los avisos.
- Un modo oscuro o temas elegibles.
- Contadores o datos en las tarjetas, como la cantidad de presupuestos del día.

## Risks and Mitigations
- Riesgo: el rediseño cambia el nombre accesible de los enlaces y rompe la navegación por nombre que usan las pruebas y
  los lectores de pantalla. Mitigación: FR-03 y AC-03. La descripción de cada tarjeta no forma parte del nombre del
  enlace.
- Riesgo: una marca instalada con un color primario claro deja los íconos o el foco con poco contraste. Mitigación:
  NFR-01 se verifica con las dos marcas conocidas. Con otro color, el texto de las tarjetas no usa el primario.
- Riesgo: los avisos del ingreso quedan perdidos entre tarjetas llamativas. Mitigación: FR-07 y AC-08.

## Dependencies
- PRD 01 (DISC-001), FEAT-001a: la marca instalada y sus colores, y el ingreso y la salida de la sesión.
- FEAT-008 y FEAT-010: los avisos de emisiones pendientes y de backup faltante en Inicio.
- Las pantallas de destino de las siete opciones (FEAT-001b, FEAT-001c, FEAT-002a, FEAT-002b, FEAT-003a, FEAT-006 y
  FEAT-009).
