# ADR-030: Backup programado en la Api y comando de restauración

| Field | Value |
|-------|-------|
| Date | 2026-11-16 |
| Ticket | FEAT-010 |
| Status | Accepted |

## Context
El backup tiene que iniciarse solo a las 20:00 con la PC encendida y al iniciar si falta el de ayer (FR-04, FR-08), y
la restauración tiene que hacerse en la PC, nunca desde la interfaz ni la Api (FR-13, FR-14). El repositorio no tenía
tareas en segundo plano, e Infrastructure no referencia las abstracciones de hosting. ADR-007 definió un único comando
de consola (`restablecer-contrasena`).

## Options considered

### Option 1: tarea programada de Windows que llama a un comando
- **Pros:** corre aunque la Api esté detenida.
- **Cons:** configuración aparte en cada instalación, fácil de olvidar; el backup con la Api detenida no es el caso
  pedido y el comando competiría con el candado de instancia.

### Option 2: `BackgroundService` en Infrastructure
- **Pros:** cerca del copiador.
- **Cons:** suma `Microsoft.Extensions.Hosting.Abstractions` a Infrastructure y a cuatro lockfiles.

### Option 3: `BackgroundService` en la Api y comando `restaurar-backup` en el selector de comandos
- **Pros:** la Api ya tiene el SDK web; el servicio solo orquesta y delega en el caso de uso `HacerBackupDelDia` en un
  alcance propio; el selector de comandos generaliza el de ADR-007 sin cambiar `restablecer-contrasena`.
- **Cons:** el backup solo corre con la Api en marcha (es lo que pide el PRD: "con la PC encendida" y el sistema en
  operación).

## Decision
Se eligió la opción 3: `ProgramadorBackup` en `src/Optica.Api/Backup`, registrado solo en la rama del servidor, con
`TimeProvider` para esperar hasta las 20:00 locales y un intento al arrancar; y un selector de comandos en `Program.cs`
que suma `restaurar-backup <copia>` con el mismo orden de pasos que `restablecer-contrasena` (base existente, candado,
confirmación, ejecución).

## Consequences
- Es el primer servicio en segundo plano del repositorio; las pruebas de la Api lo arrancan con el reloj falso, que no
  cruza las 20:00.
- ADR-007 sigue vigente para `restablecer-contrasena`; el selector admite un segundo comando.
- El candado singleton de backup evita dos copias simultáneas (arranque y 20:00).
