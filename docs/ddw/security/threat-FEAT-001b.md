# Threat model FEAT-001b: Bloqueo y gestión de la contraseña

| Field | Value |
|-------|-------|
| Ticket | FEAT-001b |
| Spec | docs/ddw/specs/spec-FEAT-001b.md |
| Tier | FEATURE |
| Date | 2026-11-03 |

Parte del modelo de FEAT-001a (`docs/ddw/security/threat-FEAT-001a.md`), cuyas mitigaciones R-01 a
R-11 siguen vigentes: cookie HttpOnly/Secure/SameSite=Strict, política con sesión por defecto,
limitador de concurrencia 2+10, `application/json` obligatorio, contraseña y hash fuera del log.

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Domain/Acceso/PoliticaBloqueo.cs` y `src/Optica.Application/Acceso/IngresarConContrasena.cs` | Block 1, Block 2 |
| `src/Optica.Application/Acceso/CambiarContrasena.cs` y `POST /api/acceso/cambiar` en `src/Optica.Api/Controllers/AccesoController.cs` | Block 3, Block 4 |
| `src/Optica.Api/Acceso/ComandoRestablecerContrasena.cs` y `src/Optica.Application/Acceso/RestablecerContrasena.cs` | Block 5 |
| `web/src/paginas/CambiarContrasena.tsx` y `web/src/api/cliente.ts` | Block 6 |

## Trust boundaries
- Navegador de la red local → Api: `POST /api/acceso/ingresar` (anónimo) y `POST /api/acceso/cambiar` (con sesión) llevan contraseñas en claro dentro de TLS.
- Api → base SQLite: los eventos `EventoAcceso` de los que se deriva el bloqueo y la fila `CredencialAcceso` cruzan al archivo `optica.db` en el perfil de la cuenta de la Api.
- Consola de la PC del local → comando `restablecer-contrasena`: quien opera la PC con la cuenta de la Api cruza a la base y a la carpeta `claves\` sin pasar por la red ni por la Api.

## STRIDE analysis
### `src/Optica.Domain/Acceso/PoliticaBloqueo.cs` y `src/Optica.Application/Acceso/IngresarConContrasena.cs`
- **Spoofing:** adivinar la contraseña compartida probando sin límite era el riesgo R-03 de FEAT-001a; a partir del quinto rechazo consecutivo el ingreso queda bloqueado 5 minutos, anclados en ese quinto rechazo aunque una carrera concurrente agregue un sexto, y durante el bloqueo ni la correcta entra (R-01, R-07).
- **Tampering:** la cuenta se deriva de eventos en la base; alterarla exige escribir `optica.db`, que está en el perfil de la cuenta de la Api (R-07 de FEAT-001a). El orden es por `Id`, así que adelantar o atrasar la hora no reinicia la cuenta; atrasarla antes del ancla anula el bloqueo vigente; la hora la mueve quien tenga acceso a la PC o la sincronización horaria de Windows, y cada atraso libera como máximo 4 intentos antes del próximo bloqueo (R-02).
- **Repudiation:** cada intento queda como `ingreso/rechazo`, `ingreso/bloqueado` o `ingreso/exito` con fecha y hora, sin contraseña ni IP (R-06 de FEAT-001a).
- **Information Disclosure:** la respuesta 423 dice solo que el acceso está bloqueado temporalmente; no confirma si la contraseña probada era la correcta, porque durante el bloqueo el hash no se calcula.
- **Denial of Service:** cualquiera en la red local puede provocar el bloqueo con 5 intentos y dejar a las dueñas 5 minutos sin entrar, y repetirlo (R-03). Durante el bloqueo no se calcula PBKDF2; cada intento agrega una fila `ingreso/bloqueado`, y el índice `IX_EventoAcceso_Tipo_Resultado` evita que la consulta recorra la tabla a medida que crece (R-12).
- **Elevation of Privilege:** el resultado `Bloqueado` no emite la cookie: el controller emite sesión solo con `Exito` (cerrado por defecto, prueba `Ingresar_ResultadoBloqueado_NoEmiteCookie`).

### `src/Optica.Application/Acceso/CambiarContrasena.cs` y `POST /api/acceso/cambiar` en `src/Optica.Api/Controllers/AccesoController.cs`
- **Spoofing:** exige sesión y la contraseña actual; quien tenga una sesión robada puede probar contraseñas actuales sin bloqueo (R-04, aceptado por el usuario).
- **Tampering:** reemplazo del hash y evento en una sola transacción; un conflicto con un restablecimiento simultáneo responde 409 sin dejar nada a medias (R-05).
- **Repudiation:** `cambio_contrasena/exito` y `cambio_contrasena/rechazo` con fecha y hora, sin contraseñas.
- **Information Disclosure:** el cuerpo trae dos contraseñas en claro; `SolicitudCambioContrasena.ToString()` las oculta para el log de MVC en Trace, la prueba `Log_Cambiar_NoContieneNingunaContrasena` lo fija y no se activa HttpLogging (R-06).
- **Denial of Service:** cada pedido calcula hasta dos PBKDF2; comparte el limitador 2+10 de `definir` e `ingresar` y el tope de 4 KB del cuerpo.
- **Elevation of Privilege:** no hay roles; cambiar la contraseña da el mismo acceso que ya tenía la sesión. Las sesiones abiertas siguen válidas tras el cambio (riesgo aceptado en FEAT-001a).

### `src/Optica.Api/Acceso/ComandoRestablecerContrasena.cs` y `src/Optica.Application/Acceso/RestablecerContrasena.cs`
- **Spoofing:** quien tenga acceso a la PC y a la cuenta de la Api puede restablecer y definir una contraseña propia; está en el PRD y queda a cargo de la protección física de la PC (R-08). Sin endpoint ni pantalla (FR-05).
- **Tampering:** borra solo la fila de credencial y agrega un evento, en una transacción; verifica que `optica.db` exista sin crearla, así no "restablece" una base vacía de otra cuenta (R-09).
- **Repudiation:** el evento `restablecimiento/exito` deja rastro de cuándo se hizo (decisión del usuario).
- **Information Disclosure:** la salida muestra la ruta con `IDatosLocales.RutaVisible` y el código de SQLite, nunca la ruta expandida con el usuario de Windows ni la excepción (R-10).
- **Denial of Service:** el comando se niega si la Api está en marcha (candado `optica.instancia` en el directorio de datos, que la Api adquiere al arrancar y el comando adquiere y mantiene hasta terminar): con la base en WAL el restablecimiento terminaría bien igual, pero la Api seguiría con las claves de sesión en memoria (R-13).
- **Elevation of Privilege:** después del restablecimiento `definir` vuelve a ser anónimo; si la Api escucha en la red, otro equipo podría definir la contraseña primero (R-11). Las sesiones anteriores se cortan renovando las claves de `claves\` (decisión del usuario), lo que solo vale con la Api detenida (R-13).

### `web/src/paginas/CambiarContrasena.tsx` y `web/src/api/cliente.ts`
- **Spoofing:** la pantalla está detrás de la sesión; un 401 vacío lleva a `/ingreso` con `replace`.
- **Tampering:** las dos contraseñas viajan solo en el cuerpo JSON del POST, con `credentials: 'same-origin'`; SameSite=Strict y `application/json` impiden un envío desde otro sitio.
- **Repudiation:** lo registra la Api (Block 4); la web no guarda registros.
- **Information Disclosure:** ninguna contraseña queda en `localStorage`, `sessionStorage`, la URL ni la consola; campos `type="password"` con `autocomplete` `current-password` y `new-password` (prueba `CambiarContrasena_NingunaContrasenaQuedaEnElNavegador`).
- **Denial of Service:** sin consultas periódicas; un 429 muestra "intentá de nuevo en un momento".
- **Elevation of Privilege:** los mensajes del servidor se muestran como texto de React, sin `dangerouslySetInnerHTML`; la CSP de la SPA de FEAT-001a sigue aplicando.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| contraseña actual y nueva | credentials | nunca se guardan; solo el hash PBKDF2-SHA256 con sal nueva en `CredencialAcceso` | TLS (HTTPS de Kestrel con el certificado del almacén de Windows) |
| hash de la contraseña | credentials | `optica.db` en el perfil de la cuenta de la Api; fuera del log y de las respuestas | no sale de la Api |
| claves de Data Protection (`claves\`) | credentials | protegidas con DPAPI en Windows; el restablecimiento las renueva | no salen de la PC |
| eventos de acceso (tipo, resultado, fecha) | PII (muestran cuándo entran y salen las operadoras) | `optica.db` en el perfil de la cuenta de la Api, sin contraseña ni IP; no van al log | no se exponen por la Api; TLS no aplica |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | adivinar la contraseña compartida probando por la red | S | M | H | cada quinto rechazo consecutivo bloquea 5 minutos; durante el bloqueo no entra ni la correcta (Block 1, Block 2) |
| R-02 | alterar el bloqueo cambiando la hora de la PC | T | L | M | la cuenta ordena por `Id`; la hora solo mide los 5 minutos; un atraso libera como máximo 4 intentos y el siguiente múltiplo se ancla con la hora nueva (Block 1) |
| R-03 | bloquear a las dueñas repitiendo 5 intentos | D | M | M | el bloqueo dura 5 minutos y se libera solo (FR-02); sin PBKDF2 durante el bloqueo |
| R-04 | sesión robada usada para probar la contraseña actual sin bloqueo | S | L | H | accepted, see below |
| R-05 | cambio y restablecimiento simultáneos dejan la credencial a medias | T | L | M | una transacción por operación con el hash como token de concurrencia; el conflicto responde 409 (Block 3, Block 4) |
| R-06 | las dos contraseñas de `cambiar` llegan al log | I | L | H | `ToString()` oculto, prueba de log en Trace, sin HttpLogging (Block 4) |
| R-07 | eludir el bloqueo con pedidos concurrentes que saltan el múltiplo de 5 | S | M | H | el bloqueo se activa con 5 o más rechazos y se ancla en el quinto; con el limitador de 2 a la vez el máximo por ventana es 6 (Block 1, Block 2, prueba con dos pedidos simultáneos) |
| R-08 | acceso físico a la PC permite tomar el control con el restablecimiento | E | L | H | sin endpoint ni pantalla (FR-05); evento registrado; protección física a cargo del negocio (PRD) |
| R-09 | el comando corre con otra cuenta y crea una base vacía | T | M | M | verifica que `optica.db` exista sin crearla y termina con error (Block 5) |
| R-10 | la salida del comando muestra el usuario de Windows | I | M | L | `IDatosLocales.RutaVisible` y el código de SQLite, sin la excepción (Block 5) |
| R-11 | tras el restablecimiento otro equipo define la contraseña primero | E | L | H | el comando se niega con la Api en marcha; el procedimiento restringe la Api a `localhost` y define la contraseña desde la PC antes de abrir la red (Block 5, `docs/instalacion.md`) |
| R-12 | la consulta del bloqueo se vuelve lenta con muchos eventos `bloqueado` | D | M | M | índice `IX_EventoAcceso_Tipo_Resultado` por migración aditiva (Block 1) |
| R-14 | los eventos de acceso (PII de las operadoras) no tienen retención y crecen sin tope durante un bloqueo, también en los backups | I | M | M | accepted, see below |
| R-13 | restablecer con la Api en marcha deja vigentes las sesiones y expone `definir` en la red | E | M | H | candado de archivo `optica.instancia` en el directorio de datos, abierto en exclusiva por la Api al arrancar; el comando lo adquiere y lo mantiene, y se niega con código 2 si está tomado o con acceso denegado (Block 5) |

## Accepted risks
### R-04
- **Accepted by:** el usuario (dueño del producto), 2026-11-02, al decidir que un error en la contraseña actual al cambiarla no cuenta para el bloqueo, con el riesgo explicado.
- **Justification:** cambiar la contraseña ya exige una sesión válida; el freno que queda es el limitador de concurrencia de 2 pedidos a la vez y 10 en cola, y cada intento calcula PBKDF2 con 600.000 iteraciones.
- **Review conditions:** se revisa si aparece un uso indebido de sesiones (por ejemplo, PC compartidas sin cerrar sesión), antes de habilitar el acceso desde fuera de la red local, o si se agregan módulos con datos de clientes de mayor sensibilidad.

### R-14
- **Accepted by:** el usuario (dueño del producto), 2026-11-03, al elegir no definir retención en este ticket.
- **Justification:** hoy los eventos solo tienen tipo, resultado y fecha, sin contraseña ni IP; el limitador de concurrencia acota cuántas filas se agregan por segundo y el índice `IX_EventoAcceso_Tipo_Resultado` mantiene la consulta rápida. `docs/instalacion.md` advierte que los backups contienen estos eventos y se guardan con la misma protección que `optica.db`.
- **Review conditions:** antes de cargar datos reales de clientes, si la tabla `EventoAcceso` supera las 100.000 filas, o cuando se defina la política de retención de datos personales del sistema.

## Supply chain
Sin dependencias nuevas: el bloqueo, el cambio y el restablecimiento usan EF Core, ASP.NET Core Data
Protection y `Rfc2898DeriveBytes`, ya presentes desde FEAT-001a, y la web no suma paquetes.

## Availability
El vector de disponibilidad nuevo es el bloqueo provocado (R-03), acotado a 5 minutos por vez y sin
costo de CPU. El resto lo cubre el limitador de concurrencia de FEAT-001a y el tope de 4 KB.
