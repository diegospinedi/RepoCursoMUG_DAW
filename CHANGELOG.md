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

### Security
- [FEAT-001b] `ConflictoAlConfirmarException` ya no admite una excepción interna, así que el hash de la contraseña que viaja en los `Entries` de EF Core no puede llegar al log por esa vía.
