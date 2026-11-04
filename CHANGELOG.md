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

### Security
- [FEAT-001b] `ConflictoAlConfirmarException` ya no admite una excepción interna, así que el hash de la contraseña que viaja en los `Entries` de EF Core no puede llegar al log por esa vía.
