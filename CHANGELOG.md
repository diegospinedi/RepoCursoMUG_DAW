# Changelog

Todos los cambios relevantes de este proyecto se registran en este archivo.

El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/).

## [Unreleased]

### Added
- [FEAT-001a] Ingreso, sesión y marca: definición de la contraseña única de acceso (PBKDF2-SHA256, 8 a 128 caracteres), ingreso y cierre de sesión con cookie que vence a los 60 minutos sin uso, Api protegida salvo cinco rutas anónimas, marca instalada fuera del repositorio con marca de ejemplo de respaldo, interfaz web React servida por la Api, pruebas de punta a punta en Chrome y Edge, y procedimiento de instalación (`docs/instalacion.md`).
- [FEAT-001b] Bloqueo y gestión de la contraseña:
  - Bloqueo del ingreso por 5 minutos tras 5 intentos fallidos consecutivos (423), que sobrevive al reinicio de la Api.
  - Cambio de la contraseña con sesión iniciada (`POST /api/acceso/cambiar` y pantalla `/cambiar-contrasena`).
  - Restablecimiento solo desde la PC con el comando `restablecer-contrasena`: elimina la credencial, registra el evento y corta todas las sesiones. No hay endpoint de restablecimiento.
  - Candado de instancia única (`optica.instancia`).
  - Pruebas que fijan que ninguna respuesta ni pantalla muestre datos de certificado o de punto de venta de ARCA.
- [FEAT-001c] Configuración de parámetros de negocio:
  - Pantalla `/configuracion`, con sesión iniciada, para consultar y grabar la alícuota de IVA (0 a 100, hasta 2 decimales), la condición fiscal (Responsable Inscripto o Monotributo), el múltiplo de redondeo comercial (mínimo 0,01) y el tope de identificación del receptor (mayor a 0, puede quedar sin valor).
  - Valores iniciales al instalar o al actualizar una base existente: 21, Responsable Inscripto y 0,01; el tope queda sin valor y la pantalla avisa que falta cargarlo.
  - `GET` y `POST /api/configuracion`: todos los errores por campo, valores leídos de la base en cada operación (sin reiniciar), cada grabación registrada con los valores anteriores y nuevos (también en el Registro de eventos de Windows).
  - Pruebas de punta a punta con una Api y una base por navegador y por archivo de pruebas.
- [FEAT-002a] Alta de artículos con precio de venta calculado:
  - Pantalla `/articulos/nuevo`, con sesión iniciada, para dar de alta un artículo con descripción (hasta 200 caracteres), código de proveedor (hasta 50), costo y margen (hasta 2 decimales).
  - Código interno asignado por el sistema y precio de venta calculado como `costo × (100 + margen) / 100`, redondeado hacia arriba al múltiplo comercial configurado, sin errores de punto flotante (ADR-010).
  - Código de proveedor único sin distinguir mayúsculas, espacios en blanco de los extremos ni formas Unicode equivalentes (ADR-009); se rechaza el texto con UTF-16 inválido junto al campo.
  - `POST /api/articulos` y `GET /api/articulos/{codigo}`, con todos los errores por campo; tabla `Articulo` con restricciones e índice único.
  - Pruebas de punta a punta del alta en Chrome y Edge.
- [FEAT-002b] Búsqueda, edición y baja de artículos:
  - Pantalla `/articulos`, con sesión iniciada: búsqueda por código, código en el proveedor o descripción sin distinguir mayúsculas ni tildes (la ñ cuenta como letra; ADR-011), páginas de 50 ordenadas por descripción con la ñ entre la n y la o, sin costo ni margen, y Desactivar o Reactivar por fila.
  - Pantalla `/articulos/{codigo}`: edición con el Precio de Venta de solo lectura; el precio se recalcula solo si cambia el costo o el margen; un artículo inactivo se ve en solo lectura y hay que reactivarlo para editarlo.
  - Una edición hecha en otra pestaña no se pisa: la Api responde 409 y la pantalla pide volver a abrir el artículo.
  - `GET /api/articulos`, `PUT /api/articulos/{codigo}` y `PUT /api/articulos/{codigo}/estado`; columnas `Activo` y `Version` agregadas sin reconstruir la tabla (ADR-012).
  - Pruebas de punta a punta en Chrome y Edge.
- [FEAT-002c] Recálculo del catálogo por configuración:
  - Al grabar una configuración que cambia la alícuota, la condición fiscal o el múltiplo de redondeo, el sistema recalcula el Precio de Venta de todos los artículos, activos e inactivos. Si solo cambia el tope, no recalcula.
  - La configuración y los precios recalculados se graban juntos o no se graba nada (ADR-013). Si algún precio queda fuera de rango, el cambio se rechaza junto al múltiplo con hasta 10 códigos y la marca ", inactivo".
  - El alta y la edición de artículos leen la configuración dentro de la misma transacción, así nunca graban un precio con los parámetros viejos.
  - La pantalla `/configuracion` informa cuántos precios cambiaron ("Cambió el precio de venta de 10.000 artículos."), y `POST /api/configuracion` devuelve `preciosCambiados`.
  - Probado con 10.000 artículos: la búsqueda responde en menos de 1 s en el percentil 95 y el recálculo completo tarda alrededor de 1 s.
  - Pruebas de punta a punta en Chrome y Edge.
- [FEAT-003a] Alta del presupuesto con cliente y líneas del catálogo:
  - Pantalla `/presupuestos/nuevo`, con sesión iniciada: datos del cliente (Apellido, Nombre y DNI obligatorios; Domicilio, Email y Nro. de Teléfono opcionales), buscador de artículos activos sin distinguir mayúsculas ni tildes, tabla de líneas con el Precio Unitario del catálogo de solo lectura y botón "Quitar" antes de grabar.
  - El DNI se acepta con o sin puntos y se guarda con 7 u 8 dígitos; el email exige el formato usuario@dominio.extensión con una extensión de al menos 2 letras.
  - Cada presupuesto se graba en Borrador con la fecha local del día y el número siguiente al último, desde 1 (ADR-015); mientras no exista FEAT-003b, cada línea queda con Cantidad 1 y Descuento 0.
  - Pantalla `/presupuestos/{numero}` con el presupuesto grabado en solo lectura y el mensaje "Presupuesto N.º {n} grabado. Estado: Borrador.".
  - `POST /api/presupuestos` (recibe solo los códigos de artículo, nunca precios), `GET /api/presupuestos/{numero}` y el parámetro `soloActivos` en `GET /api/articulos`; tablas `Presupuesto` y `LineaPresupuesto` con la migración `Presupuestos`.
  - Pruebas de punta a punta de los 19 criterios en Chrome y Edge.
- [FEAT-003b] Cantidad, descuento y cálculos de las líneas y el total del presupuesto:
  - Cada línea tiene Cantidad (entero de 1 a 9.999; con punto de miles, "1.000" vale 1000 y "10.000" se rechaza con "Usá hasta 9.999 unidades.") y Porcentaje de Descuento (0 a 100, hasta 2 decimales, con coma o punto), que empiezan en 1 y 0.
  - Precio con Descuento = Precio Unitario × (100 − Descuento) / 100, redondeado a 2 decimales mitad hacia arriba; Precio Final = Precio con Descuento × Cantidad; Total = suma de los Precios Finales, con IVA incluido y sin discriminarlo (ADR-016).
  - El servidor calcula y graba los importes con aritmética exacta en centésimos; la pantalla calcula en espejo con BigInt para mostrarlos al escribir, y el presupuesto grabado muestra lo que devolvió el servidor.
  - `POST /api/presupuestos` recibe por línea código, cantidad y descuento (ignora precios enviados) y responde los errores junto a cada campo (`lineas[i].cantidad`, `lineas[i].porcentajeDescuento`); el alta y `GET /api/presupuestos/{numero}` devuelven `total`. Un importe de línea o un total por encima de 90.071.992.547.409,91 se rechaza con su mensaje. Sin cambios en la base.
  - Pantalla de alta con un mensaje por campo, la fila "Total (IVA incluido)" al pie de la tabla y el foco en el primer campo con error al grabar.
  - Pruebas de punta a punta de cantidad, descuento y total en Chrome y Edge.
- [FEAT-004] Análisis estático de la web con oxlint 1.92.0 (en lugar de ESLint, que con typescript-eslint no soporta TypeScript 7):
  - `npm run lint` en `web/` revisa la aplicación y sus pruebas con las reglas equivalentes a la plantilla de Vite (corrección, hooks de React, exportaciones para recarga en caliente y reglas de TypeScript), sin admitir avisos (`--max-warnings 0`) e ignorando `dist/` y `coverage/`.
  - `npm run test:lint` con 8 pruebas que fijan la configuración; el comando quedó declarado en la sección "Stack" de `AGENTS.md`.
  - Los 7 problemas que marcaba en el código actual se corrigieron sin desactivar reglas y sin cambiar el comportamiento (las 602 pruebas de la web siguen igual).
- [FEAT-005] Integración continua para los pull requests (`.github/workflows/ci.yml`):
  - Cada pull request corre en GitHub Actions, en un solo job sobre `ubuntu-24.04`, los comandos de `AGENTS.md`: `npm ci` en `web/` y `e2e/`, compilación y pruebas de .NET, lint y prueba del lint de la web, pruebas de la web con cobertura, su compilación, pruebas de unidad de e2e y la suite de punta a punta en Chrome y Edge. Un paso que falla deja el pull request en rojo con su nombre.
  - Sin secretos, con permiso de solo lectura, checkout sin credenciales guardadas, acciones oficiales fijadas por SHA, tope de 45 minutos y cancelación de la corrida anterior del mismo pull request.
  - .NET pasa a tener archivos de bloqueo de NuGet (`packages.lock.json`), exigidos en modo bloqueado en la CI.
  - `e2e/ci.mjs` fija las reglas del workflow, del bloqueo de NuGet y de los hosts de las pruebas con pruebas dentro de `npm run test:unidad`.

### Fixed
- [FEAT-002b] Las pruebas de punta a punta en Edge ya no fallan de forma intermitente en "Cargando…": Edge iniciaba sesión solo con la cuenta del equipo y dejaba de responder mientras consultaba servicios de Microsoft.

### Security
- [FEAT-005] Ninguna prueba habla con servicios externos: Chrome, Edge y el Chromium de diagnóstico solo resuelven `localhost`, y una prueba rechaza URL a hosts externos en los archivos de prueba. Riesgo conocido, pendiente de un FIX: si la aplicación se instala dentro del perfil del usuario de Windows, la línea "Content root path" del log de arranque lleva su nombre de usuario.
- [FEAT-003a] Toda respuesta de `/api`, incluidas las de error, lleva `Cache-Control: no-store`, así el navegador de una PC compartida no guarda los datos personales de los clientes (DNI, domicilio, email y teléfono). Los datos del cliente no llegan al log, a la URL ni al almacenamiento del navegador, y `docs/instalacion.md` advierte que la base y los backups los guardan sin cifrar (riesgo aceptado mientras el sistema sea de prueba; Ley 25.326).
- [FIX-001] El bloqueo del ingreso por 5 intentos fallidos ya no se levanta cuando el reloj de la PC retrocede (por ejemplo, al sincronizarse sola): un retroceso de hasta 5 minutos mantiene el bloqueo, que puede extenderse hasta 10 minutos desde ese momento (ADR-014).
- [FEAT-001b] `ConflictoAlConfirmarException` ya no admite una excepción interna, así que el hash de la contraseña que viaja en los `Entries` de EF Core no puede llegar al log por esa vía.
- [FEAT-002b] El costo, el margen, la descripción y el texto buscado no llegan al log aunque se suba el nivel de registro: los datos de los pedidos no se imprimen y la Api fija en código el piso `Warning` para el registro de pedidos y de MVC.
- [FEAT-002c] La alícuota, la condición fiscal y el tope de los pedidos y de la configuración vigente no se imprimen en el log, y las pruebas ya no escriben la ruta temporal, que puede incluir el nombre de usuario del sistema operativo.
- [FEAT-002b] El Edge de las pruebas de punta a punta solo resuelve `localhost`, así ya no transmite a Microsoft datos de la identidad del empleado ni de la organización.
