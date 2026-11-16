# Decisiones FEAT-010 — Backup y restauración

Formato: decisión — quién la aprobó — fecha — dónde aplica.

## DEFINE
- **D-01 (del usuario).** Un solo ticket con todos los AC (W-PRD-06 aceptado). — usuario — 2026-11-16 — PRD.
- **D-02 (del usuario).** NFR-03 y AC-16 solo en la versión estable vigente de Chrome y Edge; la anterior queda como
  verificación manual. — usuario — 2026-11-16 — PRD, Block 8.
- **D-03 (del usuario).** Modo `minimal` para este ticket. — usuario — 2026-11-16 — todo el ticket.
- **D-04 (de las dueñas, DISC-001; reiterada).** Las copias se guardan sin cifrar, con mitigación documental (pendrive
  bajo llave, nube con contraseña propia y dos pasos, permisos NTFS); riesgo R-06 del modelo de amenazas. — dueñas —
  2026-10-30, reiterada 2026-11-16 — PRD, Block 7.
- **D-05 (del usuario).** Delegación: ante una decisión no escrita, el agente toma la opción recomendada que menos
  tiempo lleve y la registra acá (memoria del 2026-11-14, vigente). — usuario — 2026-11-14 — todo el ticket.

## PLAN (lazo correctivo a DEFINE por el análisis de impacto)
- **D-06 (del usuario).** Se acepta como ubicación una carpeta de la misma unidad que la base si está dentro de la
  carpeta local de OneDrive, Google Drive o Dropbox (FR-02, AC-17); el riesgo de que la nube no sincronice queda
  aceptado (R-09). — usuario — 2026-11-16 — PRD, Block 2.
- **D-07 (del usuario).** "Otro disco" es otra unidad (letra o punto de montaje); una partición distinta del mismo
  disco físico se acepta y la guía recomienda pendrive o disco externo. — usuario — 2026-11-16 — PRD, Block 2.
- **D-08 (del usuario).** Historial de backups verificados en la base para que el aviso muestre la fecha del último
  aunque la ubicación no esté accesible (FR-09, AC-18). — usuario — 2026-11-16 — PRD, Blocks 1, 4 y 6.

## PLAN (del agente, por D-05)
- **D-09.** Copia con la API de backup de SQLite a un temporal `.parcial`, `journal_mode=DELETE`, `integrity_check` y
  renombre a `optica-AAAA-MM-DD.db`; los archivos de la carpeta son la verdad sobre qué copias hay (ADR-029). —
  agente — 2026-11-16 — Block 4.
- **D-10.** `ProgramadorBackup` como `BackgroundService` en la Api (no en Infrastructure, para no sumar paquetes) y
  comando `restaurar-backup` en un selector de comandos que generaliza el de ADR-007 (ADR-030). — agente — 2026-11-16 —
  Blocks 5 y 7.
- **D-11.** La copia hecha al arrancar por faltar la de ayer lleva la fecha de hoy (FR-06: fecha del día en que se
  hizo) y cuenta como cubierta de ayer para el aviso. — agente — 2026-11-16 — Blocks 4 a 6.
- **D-12.** La ruta del backup nunca va al log ni a mensajes de error (puede llevar el nombre de una persona); el
  estado no devuelve rutas. — agente — 2026-11-16 — Blocks 2, 4 y 6.
- **D-13.** El aviso al ingresar usa `role="status"`, sin enlaces ni la palabra "configuración" (hay e2e que buscan el
  enlace "Configuración" por subcadena); `e2e/tests/acceso.spec.ts` filtra su `getByRole('status')` por texto. La
  sección se titula "Backup" y su botón "Grabar ubicación del backup". — agente — 2026-11-16 — Blocks 3 y 6.
- **D-14.** AC-14: la restauración se prueba con una prueba de integración que restaura una copia real en una
  instalación nueva y abre un presupuesto; la medición de menos de 4 horas en la PC del local con la guía queda como
  verificación manual del usuario. — agente — 2026-11-16 — Block 7.
- **D-15.** La guía de restauración exige revisar en ARCA los comprobantes emitidos desde la fecha de la copia antes de
  volver a facturar (evita una factura duplicada), y el comando lo recuerda al terminar (R-07). — agente — 2026-11-16 —
  Block 7.
- **D-16.** e2e: la carpeta de backup de las pruebas va en otro punto de montaje (`/dev/shm` en Linux) creada y borrada
  por el harness; si no hay un segundo punto de montaje, el caso positivo de AC-01 se salta con motivo (los .NET lo
  cubren). AC-17 (nube) y AC-09/AC-18 (historial con fechas pasadas) se prueban en .NET y en la web, no en e2e. —
  agente — 2026-11-16 — Block 8.

## PLAN — auditoría de arquitectura (5 FAIL, 11 WARN, 7 INFO aplicados)
- **D-17.** Se rechazan rutas UNC, unidades de red y los prefijos `\\?\` y `\\.\`; las unidades de `subst` quedan como
  limitación documentada (FAIL-4). — agente (D-05) — 2026-11-16 — Block 2.
- **D-18.** Las carpetas de nube conocidas son las de la cuenta que corre la Api (variables `OneDrive*`, `info.json` de
  Dropbox, `Mi unidad`/`My Drive` y `Google Drive` de Google Drive para escritorio), detrás de `IFuenteCarpetasNube`
  inyectable; la guía indica que la nube tiene que ser la de esa cuenta (WARN-8). — agente — 2026-11-16 — Block 2.
- **D-19.** También se rechaza una carpeta dentro de la carpeta de datos (WARN-11). — agente — 2026-11-16 — Block 2.
- **D-20.** El backup de las 20:00 copia siempre y reemplaza la copia de hoy después de verificar la nueva; "no repetir"
  vale solo para el intento al arrancar, y la regla de "falta la de ayer" vive en Application y la comparte el estado
  (FAIL-1, WARN-2). D-11 sigue: la copia al arrancar lleva la fecha de hoy. — agente — 2026-11-16 — Blocks 4 a 6.
- **D-21.** Retención: las copias con fecha futura se conservan y no cuentan entre las 7 diarias; un mes calendario
  terminado sin copias no se reemplaza (quedan menos mensuales); una copia puede ser diaria y mensual a la vez; el
  "último backup" es la mayor fecha que no pasa de hoy (WARN-5). — agente — 2026-11-16 — Blocks 1 y 6.
- **D-22.** `ICandadoBackup` en Application con su implementación singleton en Infrastructure (WARN-1). — agente —
  2026-11-16 — Block 4.
- **D-23.** Todas las conexiones del backup y la restauración son propias, sin pool, con cierre explícito y sin pasar
  por `FabricaConexion.Configurar`; origen sin crear y copia a restaurar en solo lectura; los `.parcial*` huérfanos se
  borran antes de cada backup (WARN-3, WARN-4). — agente — 2026-11-16 — Blocks 4 y 7.
- **D-24.** El programador despierta cada 5 minutos y compara el reloj con el historial (tolera suspensión, saltos de
  reloj y candado tomado) (WARN-6). — agente — 2026-11-16 — Block 5.
- **D-25.** `FabricaApi` quita el servicio de backup por defecto; "sin ubicación" se registra en Debug (WARN-7). —
  agente — 2026-11-16 — Block 5.
- **D-26.** El aviso distingue "No hay una ubicación de backup elegida." de "La ubicación del backup no está
  accesible." (INFO-4). — agente — 2026-11-16 — Block 6.
- **D-27.** La restauración es un caso de uso (`RestaurarBackup`) con el puerto `IRestauradorBase`; el comando solo
  maneja consola, candado y confirmación (FAIL-2). — agente — 2026-11-16 — Block 7.
- **D-28.** La restauración no migra (la Api migra al arrancar, como `restablecer-contrasena` que se niega con
  migraciones pendientes); rechaza una copia sin `__EFMigrationsHistory` o con migraciones desconocidas; no deja un
  evento en la base (WARN-9). — agente — 2026-11-16 — Block 7.
- **D-29.** Restauración a un temporal verificado, intercambio y vuelta atrás automática; `anterior/` se conserva bajo
  llave mientras tenga facturas con CAE que la copia no tiene, y el comando informa cuántas antes de confirmar
  (FAIL-3, FAIL-5); la guía documenta pendrives sin permisos, papelera de la nube, copias de la ubicación anterior,
  `.parcial` huérfanos y el reinicio de la numeración del simulador (WARN-10, INFO-5). — agente — 2026-11-16 — Block 7.

## CODE
- **D-30 (del agente, Block 1).** Retención mensual con ventana fija: los 4 meses calendario anteriores al mes de hoy;
  por cada uno con copias se conserva la última y un mes vacío no se reemplaza por uno anterior (D-21). El texto del
  Block 1 de la spec ("más recientes que tengan copias") se lee así. El historial se confirma con la unidad de trabajo
  como los demás repositorios de solo agregar (FAIL de la auditoría del Block 1): la firma es `void Agregar(fecha,
  momentoUtc)` y no `AgregarAsync` como dice la spec, y `HacerBackup` (Block 4) confirma después del renombre verificado; `HuboCopiaProgramadaAsync` cuenta solo
  las filas de ese día local desde las 20:00. Rutas de prueba sobre la carpeta temporal porque `C:\…` no es absoluta en
  Linux. — agente (D-05) — 2026-11-16 — Block 1.
- **D-31 (del agente, Block 2).** `IInspectorUbicaciones` suma `EsLocal` (rechazo de UNC, red y dispositivo con su
  propio mensaje, D-17), `Superpuestas` (carpeta de datos, sin lógica de rutas en Application) y `Normalizar` (se graba
  la ruta normalizada); los métodos que no pueden evaluar responden el valor que rechaza. Google Drive espejo se acepta
  solo con el indicio de instalación `%LOCALAPPDATA%\Google\DriveFS`, y una carpeta de nube que es la raíz de una unidad
  se descarta. Limitaciones documentadas: unidades `subst`, uniones, enlaces simbólicos y nombres cortos 8.3. Las
  reglas de Windows se prueban como funciones puras desde Linux; su comportamiento en Windows queda para la verificación
  manual. — agente (D-05), señalado por el verificador y el auditor — 2026-11-16 — Block 2.
- **D-32 (del agente, Block 3).** `SeccionBackup.tsx` repite la estructura de `SeccionDatosEmisor.tsx` (consulta con
  guarda, resultado de grabar, mensaje general): se acepta en este ticket y queda como deuda junto con D-43 de FEAT-009
  (extraer un hook de sección de configuración en un ticket aparte). La regla espejo de la ruta vive en
  `configuracion/reglas.ts`; el POST tiene guarda de desmontaje; el mensaje de éxito se borra al editar. AC-17 en la web
  es equivalente a AC-01 (la nube la decide el servidor) y se cuenta en .NET (Block 2). — agente (D-05) — 2026-11-16 —
  Block 3.
- **D-33 (del agente, Block 4).** El caso de uso se llama `HacerBackup` y vive en `src/Optica.Application/Backup/` (lista
  de archivos del Block 4); el Summary de la spec, el modelo de amenazas y ADR-030 lo nombran `HacerBackupDelDia`. Si
  falla anotar en el historial después del renombre verificado, la copia queda en la carpeta (ADR-029) y no se aplica la
  retención; una vez renombrada, la anotación no se cancela. Una retención fallida deja el resultado Hecho con un
  Warning. El registro de inicio va en Debug (el modelo de amenazas habla de "log de inicio"; el Registro de eventos
  recibe resultados, fallas y temporales sin borrar en Warning). Un `.parcial` verificado sin su copia (corte de luz
  durante el reemplazo en FAT/exFAT) se recupera en lugar de borrarse. La prueba de AC-06 y las de concurrencia de
  presupuestos corren sin paralelismo. La copia recuperada no se anota en el historial (no se sabe cuándo se hizo y falsearía la regla de las 20:00); con la carpeta inaccesible o una cancelación antes de copiar no se borran temporales; después del renombre la retención corre sin cancelación y el resultado es Hecho; un candidato con `-journal` caliente no pasa la verificación y se borra. — agente (D-05), señalado por el verificador y el auditor — 2026-11-16 —
  Block 4.
