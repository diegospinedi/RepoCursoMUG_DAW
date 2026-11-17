# Threat model FEAT-010: Backup y restauración

| Field | Value |
|-------|-------|
| Ticket | FEAT-010 |
| Spec | docs/ddw/specs/spec-FEAT-010.md |
| Tier | FEATURE |
| Date | 2026-11-16 |

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Domain/Backup/UbicacionBackup.cs`, `PoliticaRetencion.cs` y la migración `Backup` | Block 1 |
| `GET` y `POST /api/configuracion/backup` (`ConfiguracionController.cs`, `GrabarUbicacionBackup.cs`, `InspectorUbicaciones.cs`) | Block 2 |
| `src/Optica.Infrastructure/Backup/CopiadorBackupSqlite.cs`, `AlmacenCopiasArchivo.cs`, `HacerBackupDelDia.cs` | Block 4 |
| `src/Optica.Api/Backup/ProgramadorBackup.cs` | Block 5 |
| `GET /api/backup/estado` (`BackupController.cs`, `ObtenerEstadoBackup.cs`) y el aviso de `Inicio.tsx` | Block 6 |
| Comando `restaurar-backup` (`ComandoRestaurarBackup.cs`, `RestauradorSqlite.cs`) | Block 7 |
| `web/src/paginas/SeccionBackup.tsx` | Block 3 |

## Trust boundaries
- Navegador → Api (`localhost`): la ruta de la carpeta de backup en `POST /api/configuracion/backup` y la lectura del
  estado; todo con la cookie de sesión de FEAT-001a.
- Api → sistema de archivos: escritura de copias, temporales y borrados de retención en una carpeta elegida por la
  operadora (pendrive, otra unidad o carpeta local de OneDrive, Google Drive o Dropbox).
- Copias → fuera de la PC: el pendrive o la nube sacan la base completa (datos personales de clientes) de la
  protección del sistema.
- Consola de la PC → base: el comando `restaurar-backup` reemplaza `optica.db` con la Api detenida (candado de
  instancia), ejecutado por quien tiene acceso a la PC.

## STRIDE analysis
### `src/Optica.Domain/Backup/UbicacionBackup.cs`, `PoliticaRetencion.cs` y la migración `Backup`
- **Spoofing:** no autentica; lo usan casos de uso detrás de la sesión o del servicio interno.
- **Tampering:** la ubicación exige ruta absoluta y se valida al construir; la retención es una función pura que nunca
  propone borrar la copia del día ni actuar si la copia del día no está verificada (R-04); el historial `BackupRealizado`
  es de solo agregar (triggers que rechazan UPDATE y DELETE).
- **Repudiation:** el historial registra fecha y momento UTC de cada backup verificado.
- **Information Disclosure:** `UbicacionBackup` es clase sin `ToString` con la ruta (puede llevar el nombre de una
  persona).
- **Denial of Service:** la ruta se acota a 260 caracteres.
- **Elevation of Privilege:** no aplica: no hay roles.

### `GET` y `POST /api/configuracion/backup`
- **Spoofing:** exigen sesión (fuera de `RutasAnonimas`, que sigue con 5).
- **Tampering:** la ruta tiene que ser absoluta, existir, admitir escritura (archivo sonda que se borra) y estar en otra
  unidad o dentro de la carpeta local de OneDrive, Google Drive o Dropbox (R-02); JSON estricto, tope de 2 KB, 415 sin
  JSON; se rechazan rutas UNC, unidades de red y los prefijos `\\?\` y `\\.\`, y la carpeta de datos, una que la
  contenga o una dentro de ella.
- **Repudiation:** cada cambio de ubicación queda en el log con fecha UTC, sin la ruta.
- **Information Disclosure:** el GET devuelve la ruta configurada solo con sesión y `no-store`; los mensajes de error no
  repiten la ruta; la solicitud con `PrintMembers => false`; el log nunca lleva la ruta (R-05).
- **Denial of Service:** tope de cuerpo; la prueba de escritura es un archivo de un byte.
- **Elevation of Privilege:** la Api escribe solo en la carpeta configurada y con nombres fijos (`optica-AAAA-MM-DD.db`),
  nunca con nombres que vengan del usuario (R-03).

### `src/Optica.Infrastructure/Backup/CopiadorBackupSqlite.cs`, `AlmacenCopiasArchivo.cs`, `HacerBackupDelDia.cs`
- **Spoofing:** no aplica: proceso interno.
- **Tampering:** la copia usa la API de backup de SQLite sobre una conexión propia sin pool hacia un temporal
  `.parcial`, la pasa a `journal_mode=DELETE`, corre `PRAGMA integrity_check` y recién entonces la renombra; nunca una
  copia de archivo común (AGENTS.md, R-01); un `.parcial` nunca cuenta como copia.
- **Repudiation:** log de inicio, resultado y tipo de falla con fecha UTC.
- **Information Disclosure:** la copia contiene toda la base sin cifrar (R-06, decisión de las dueñas); el log nunca
  lleva rutas ni textos de excepciones (pueden traer el usuario de Windows o la ruta).
- **Denial of Service:** un backup a la vez (candado singleton); el pendrive lleno o desconectado da falla del día sin
  borrar copias (FR-12); la retención limita el espacio a 7 + 4 copias.
- **Elevation of Privilege:** la retención solo borra archivos que respetan el patrón exacto `optica-AAAA-MM-DD.db` (o
  su `.parcial`) en la carpeta configurada, nunca otros archivos ni subcarpetas (R-03).

### `src/Optica.Api/Backup/ProgramadorBackup.cs`
- **Spoofing:** no aplica.
- **Tampering:** usa `TimeProvider`; corre el caso de uso en un alcance propio; no expone superficie.
- **Repudiation:** cada ejecución deja su línea en el log.
- **Information Disclosure:** sin rutas en el log.
- **Denial of Service:** una falla no detiene el servicio ni la Api; la espera hasta las 20:00 no bloquea pedidos.
- **Elevation of Privilege:** no aplica.

### `GET /api/backup/estado` (`BackupController.cs`, `ObtenerEstadoBackup.cs`) y el aviso de `Inicio.tsx`
- **Spoofing:** exige sesión.
- **Tampering:** solo lectura.
- **Repudiation:** no aplica.
- **Information Disclosure:** devuelve solo `faltaAyer`, la fecha del último backup verificado y si la ubicación está
  accesible; nunca la ruta; `no-store`.
- **Denial of Service:** lectura de un directorio y de una fila; si la carpeta no se puede leer responde igual (sin 500).
- **Elevation of Privilege:** no aplica.

### Comando `restaurar-backup`
- **Spoofing:** solo en la consola de la PC; no hay ruta de Api ni pantalla (FR-14, AC-15).
- **Tampering:** verifica la copia antes de tocar nada (integridad, historial de migraciones conocido), exige el candado
  de instancia (Api detenida) y la palabra `RESTAURAR`; restaura primero a un temporal verificado y recién entonces
  aparta la base actual a `anterior/<fecha-hora>/` y pone la restaurada; si algo falla después de apartarla, la devuelve
  sola; no migra (la Api migra al arrancar).
- **Repudiation:** escribe en la consola el resultado y cuántas facturas con CAE posteriores a la copia quedan solo en
  `anterior/`.
- **Information Disclosure:** la carpeta `anterior/` tiene los mismos datos que una copia y puede tener las únicas
  copias locales de facturas con CAE posteriores a la copia: la guía pide conservarla bajo llave como un backup; la
  salida de consola no muestra la ruta con el usuario.
- **Denial of Service:** si la copia está corrupta, es de una versión más nueva o es ajena no cambia nada; si falla a
  mitad, la base actual vuelve sola y nunca queda la carpeta de datos sin `optica.db`.
- **Elevation of Privilege:** quien restaura ya tiene acceso a la PC; restaurar trae la contraseña vigente a la fecha
  de la copia (documentado).

### `web/src/paginas/SeccionBackup.tsx`
- **Spoofing:** la pantalla exige sesión.
- **Tampering:** validación espejo (ruta absoluta, largo); el servidor decide.
- **Repudiation:** no aplica.
- **Information Disclosure:** la ruta vive solo en el estado del componente, no en la URL, el storage ni la consola.
- **Denial of Service:** no aplica.
- **Elevation of Privilege:** no aplica.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| Copias de backup (toda la base: DNI, domicilio, teléfono y email de clientes, hash de la contraseña, eventos de acceso, facturas) | PII | sin cifrar por decisión de las dueñas (DISC-001): carpeta con permisos NTFS restringidos, pendrive bajo llave, nube con contraseña propia y verificación en dos pasos (`docs/instalacion.md`) | no viajan por la red desde el sistema; la sincronización de la nube la hace su cliente con TLS |
| Ruta de la carpeta de backup | PII (puede llevar el nombre de una persona o del usuario de Windows) | fila única `ConfiguracionBackup` en SQLite con permisos NTFS; nunca en el log | `localhost` con sesión y `no-store` |
| Historial de backups (fecha y momento) | public | tabla de solo agregar `BackupRealizado` | `localhost` con sesión y `no-store` |
| Carpeta `anterior/` después de restaurar | PII | igual que una copia; la guía pide conservarla bajo llave mientras tenga facturas que la copia no tiene | no sale de la PC |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | la copia sale inconsistente o con cambios a medio escribir del WAL | T | M | C | API de backup de SQLite sobre conexión propia, `journal_mode=DELETE` en la copia e `integrity_check` antes de renombrar; nunca copia de archivo común |
| R-02 | se configura una carpeta de la misma unidad que no es nube y el backup se pierde con el disco | T | M | H | validación de unidad al grabar, con excepción solo para la carpeta local de OneDrive, Google Drive o Dropbox; pruebas con unidades y nubes falsas |
| R-03 | la retención o la escritura tocan archivos ajenos en la carpeta elegida | E | L | H | nombres fijos generados por el sistema; la retención solo borra archivos con el patrón exacto; prueba con archivos ajenos que deben quedar |
| R-04 | un backup fallido borra copias buenas | T | M | H | la retención se aplica solo después de una copia del día verificada (FR-12); prueba con la copia que falla |
| R-05 | la ruta de la carpeta (con un nombre de persona) llega al log o a una respuesta de error | I | M | M | sin ruta en el log ni en los mensajes; `PrintMembers => false`; prueba con valores testigo |
| R-06 | se pierde el pendrive o se compromete la nube y quedan expuestos los datos de los clientes | I | M | H | aceptado por las dueñas (copias sin cifrar, DISC-001) con mitigación documental: pendrive bajo llave, nube con dos pasos, permisos NTFS |
| R-07 | se restaura una copia vieja y se vuelve a facturar un presupuesto ya facturado en ARCA (factura duplicada) | T | M | H | la guía exige revisar en ARCA los comprobantes emitidos desde la fecha de la copia antes de volver a facturar, y el comando lo recuerda al terminar |
| R-08 | se restaura con la Api en marcha, o falla a mitad y la Api arranca con una base vacía | T | L | C | candado de instancia obligatorio; restauración a un temporal verificado, intercambio y vuelta atrás automática; base anterior conservada en `anterior/` |
| R-10 | se pierden las facturas con CAE posteriores a la copia al restaurar | T | M | H | el comando informa cuántas quedan solo en `anterior/` antes de confirmar; la guía exige conservar `anterior/` y revisar ARCA |
| R-09 | la carpeta de nube en la misma unidad deja de sincronizar y las copias quedan solo en el disco principal | D | M | H | aceptado por el usuario al elegir la nube conocida (decisión del 2026-11-16); la guía pide revisar en la nube que las copias lleguen |

## Accepted risks
### R-06
- **Accepted by:** las dueñas de la óptica, en DISC-001 (PRD 09: "las copias se guardan sin cifrar"); reiterado por el
  usuario al confirmar el PRD de FEAT-010 el 2026-11-16.
- **Justification:** el cifrado de las copias complica la restauración en una PC sin conocimiento técnico; se prefiere
  la protección física y de la cuenta de la nube.
- **Review conditions:** si se pierde un pendrive o la cuenta de la nube, si cambia la normativa de datos personales o
  antes de que un tercero administre las copias.

### R-09
- **Accepted by:** el usuario, el 2026-11-16, al elegir "Aceptar nube conocida" para FR-02.
- **Justification:** la nube es el único destino fuera del local que tiene la óptica sin hardware adicional.
- **Review conditions:** si la óptica compra un disco externo o si una revisión encuentra copias sin sincronizar.

## Supply chain
Sin dependencias nuevas: la API de backup viene con `Microsoft.Data.Sqlite` (EF Core SQLite 10.0.12) y el servicio en
segundo plano usa el SDK web de la Api (ADR-030). La web no suma paquetes.

## Availability
El backup corre en segundo plano y no bloquea pedidos; la API de backup no impide que la operadora grabe (FR-05). Un
pendrive lleno o desconectado deja la falla del día y el aviso al ingresar, sin afectar la operación.
