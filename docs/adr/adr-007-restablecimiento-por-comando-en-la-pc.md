# ADR-007: Restablecimiento de la contraseña por comando en la PC, con renovación de claves

| Field | Value |
|-------|-------|
| Date | 2026-11-03 |
| Ticket | FEAT-001b |
| Status | Accepted |

## Context
FEAT-001b (FR-04, FR-05) pidió un procedimiento documentado, ejecutable solo en la PC del local, que
eliminara la contraseña sin modificar otros datos, sin ofrecerlo desde la interfaz ni la Api. No hay
administrador ni envío de emails. El usuario lo decidió el 2026-11-02, junto con registrar el evento
y cortar las sesiones abiertas.

## Options considered

### Option 1: procedimiento con `sqlite3.exe` y un `DELETE` en `docs/instalacion.md`
- **Pros:** sin código nuevo.
- **Cons:** fácil de equivocar (otra tabla, otra base, base abierta en WAL); no registraba el evento
  ni cortaba sesiones; dependía de una herramienta que Windows no trae.

### Option 2: comando de la aplicación `Optica.Api restablecer-contrasena`
- **Pros:** usa la misma configuración y la misma base que la Api; transacción con el evento; puede
  verificar que la base exista sin crearla; renueva las claves de sesión; probado con pruebas.
- **Cons:** una rama de arranque más en `Program.cs`; con la base en WAL el comando terminaba bien
  aunque la Api siguiera en marcha, que conservaba las claves de sesión en memoria, así que hizo
  falta un candado de instancia única. Un mutex con nombre fijo se descartó: lo tomaba cada instancia
  de prueba y dependía del hilo. Quedó un archivo `optica.instancia` en el directorio de datos,
  abierto en exclusiva por la Api y adquirido por el comando hasta terminar.

## Decision
Se eligió la opción 2, decisión del usuario. El comando pidió escribir `RESTABLECER`, borró solo la
credencial y agregó `restablecimiento/exito` en una transacción, y vació `claves\` de Data
Protection para que ninguna cookie anterior siguiera valiendo. Las claves no son datos del negocio,
así que FR-04 ("sin modificar ningún otro dato") se cumplió. No se agregó endpoint ni pantalla.

## Consequences
- Afecta `Program.cs`, `ComandoRestablecerContrasena.cs`, `RestablecerContrasena.cs`,
  `IDatosLocales`/`DatosLocales` y `docs/instalacion.md`.
- Después del restablecimiento `definir` volvió a ser anónimo (R-02 de FEAT-001a): el procedimiento
  restringe la Api a `localhost` hasta definir la contraseña desde la PC.
- Quien opere la PC con la cuenta de la Api puede tomar el control; queda a cargo de la protección
  física de la PC, como dice el PRD.
