# Changelog

Todos los cambios relevantes de este proyecto se registran en este archivo.

El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/).

## [Unreleased]

### Added
- [FEAT-001a] Ingreso, sesión y marca: definición de la contraseña única de acceso (PBKDF2-SHA256, 8 a 128 caracteres), ingreso y cierre de sesión con cookie que vence a los 60 minutos sin uso, Api protegida salvo cinco rutas anónimas, marca instalada fuera del repositorio con marca de ejemplo de respaldo, interfaz web React servida por la Api, pruebas de punta a punta en Chrome y Edge, y procedimiento de instalación (`docs/instalacion.md`).
