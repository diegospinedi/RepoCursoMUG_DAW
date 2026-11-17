# Spec FEAT-010: Backup y restauración

| Field | Value |
|-------|-------|
| Ticket | FEAT-010 |
| PRD | docs/ddw/prd/prd-FEAT-010.md |
| Tier | FEATURE |
| Date | 2026-11-16 |
| Spec loops | 2 |
| Loops since last human decision | 2 |

## Summary
La ubicación del backup es un agregado propio (`UbicacionBackup`, fila única) con su endpoint
`GET/POST /api/configuracion/backup` y su sección en la configuración; al grabar se comprueba que la carpeta exista,
admita escritura y esté en otra unidad o dentro de la carpeta local de OneDrive, Google Drive o Dropbox (puerto
`IInspectorUbicaciones`). El caso de uso `HacerBackupDelDia` copia la base con la API de backup de SQLite a un temporal,
lo verifica y lo renombra a `optica-AAAA-MM-DD.db` (ADR-029), anota la fecha en el historial de solo agregar y aplica
la retención (7 diarias y 4 mensuales). `ProgramadorBackup` (primer servicio en segundo plano, en la Api, ADR-030) lo
ejecuta al arrancar si falta el de ayer y todos los días a las 20:00. `GET /api/backup/estado` alimenta el aviso al
ingresar. La restauración es el comando de consola `restaurar-backup` con la Api detenida. Decisiones en
`docs/ddw/specs/decisions-FEAT-010.md`; amenazas en `docs/ddw/security/threat-FEAT-010.md`.

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 2, Block 3 |
| FR-02 | Block 2, Block 3 |
| FR-03 | Block 2, Block 3 |
| FR-04 | Block 5 |
| FR-05 | Block 4 |
| FR-06 | Block 1, Block 4 |
| FR-07 | Block 4 |
| FR-08 | Block 5 |
| FR-09 | Block 6 |
| FR-10 | Block 1, Block 4 |
| FR-11 | Block 1, Block 4 |
| FR-12 | Block 4 |
| FR-13 | Block 7 |
| FR-14 | Block 7 |
| NFR-01 | Strategy: el comando `restaurar-backup` hace la restauración en minutos; una prueba de integración restaura una copia real en una instalación nueva y abre un presupuesto; la medición en la PC del local con el procedimiento de `docs/instalacion.md` queda como verificación manual del usuario |
| NFR-02 | Strategy: `PoliticaRetencion` probada con 5 meses de copias diarias simuladas que deja exactamente 7 diarias y 4 mensuales |
| NFR-03 | Strategy: e2e del Block 8 en Chrome y Edge estables (un proyecto por navegador y archivo); la versión anterior queda manual |

## Dependencies between blocks
Block 1 → Block 2 → Block 3. Block 4 depende de Block 1. Block 5 depende de Block 4. Block 6 depende de Block 1 y Block
4. Block 7 depende de Block 4 (copiador). Block 8 depende de todos. Orden: 1, 2, 3, 4, 5, 6, 7, 8.

## Block 1 — Dominio y persistencia de la ubicación, el historial y la retención

**Files**
- `src/Optica.Domain/Backup/UbicacionBackup.cs` (new) — ruta absoluta recortada, hasta 260 caracteres, sin
  caracteres de control; `ParaGrabar` devuelve infracciones por campo y `Leer` reconstruye la fila; clase sin
  `ToString` con la ruta.
- `src/Optica.Domain/Backup/CopiaBackup.cs` (new) — nombre `optica-AAAA-MM-DD.db` ↔ fecha (`Nombre(fecha)`,
  `TryLeerFecha(nombre)` rechaza fechas inexistentes como `2026-02-30`), el sufijo temporal `.parcial` y el patrón de
  sus auxiliares (`.parcial-journal`, `.parcial-wal`, `.parcial-shm`).
- `src/Optica.Domain/Backup/PoliticaRetencion.cs` (new) — función pura: dadas las fechas de las copias y la fecha de hoy,
  devuelve las que se conservan (7 diarias más recientes que no pasan de hoy + la última de cada uno de los 4 meses
  calendario terminados más recientes que tengan copias; un mes sin copias no se reemplaza por otro, D-21); las copias
  con fecha futura se conservan y no cuentan entre las 7 (D-21); una copia puede ser diaria y mensual a la vez.
- `src/Optica.Domain/Backup/IUbicacionBackupRepository.cs`, `IHistorialBackupRepository.cs` (new) — fila única y
  historial de solo agregar (`AgregarAsync(fecha, momentoUtc)`, `UltimaFechaHastaAsync(hoy)` — la mayor fecha que no
  pasa de hoy —, `HuboCopiaProgramadaAsync(fecha)` — si hay una fila de esa fecha con momento local desde las 20:00).
- `src/Optica.Infrastructure/Persistencia/UbicacionBackupRepository.cs`, `HistorialBackupRepository.cs` (new).
- `src/Optica.Infrastructure/Persistencia/OpticaDbContext.cs` (modified) — `ConfiguracionBackupFila` y
  `BackupRealizadoFila`.
- `src/Optica.Infrastructure/Persistencia/Migraciones/20261116200000_Backup.cs`, `.Designer.cs` (new) y
  `OpticaDbContextModelSnapshot.cs` (modified) — escrita a mano (ADR-012).
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — registros.
- Pruebas: `tests/Optica.Domain.Tests/Backup/UbicacionBackupTests.cs`, `CopiaBackupTests.cs`,
  `PoliticaRetencionTests.cs`; `tests/Optica.Infrastructure.Tests/Persistencia/UbicacionBackupRepositoryTests.cs`,
  `HistorialBackupRepositoryTests.cs`, `MigracionBackupTests.cs` (new).

**Logic**
Implementa FR-01, FR-06, FR-10 y FR-11 en el dominio y la persistencia.

**Data model**
- `ConfiguracionBackup`: `Id` INTEGER PK CHECK `Id = 1`; `Ruta` TEXT NOT NULL CHECK largo 1 a 260.
- `BackupRealizado`: `Id` INTEGER PK autoincremental; `Fecha` TEXT NOT NULL (`yyyy-MM-dd`, CHECK GLOB); `MomentoUtc`
  TEXT NOT NULL; índice sobre `Fecha`; triggers `TR_BackupRealizado_Inmutable` (UPDATE) y `TR_BackupRealizado_NoSeBorra`
  (DELETE) con `RAISE(ABORT, 'registro_cerrado')`.
- Rollback: el `Down` borra los triggers y las dos tablas y deja el esquema de FEAT-009; probado por
  `Migracion_Backup_Down_Revierte`.

**Input validation**
- Ruta: texto recortado, 1 a 260 caracteres, sin caracteres de control, absoluta (`Path.IsPathFullyQualified`).

**Error handling**
- Ruta vacía, relativa o demasiado larga — infracción del campo `ruta`.
- Fila corrupta — `InvalidOperationException` con texto fijo, sin la ruta.
- UPDATE o DELETE sobre el historial — rechazado por trigger.

**Required tests**
- [ ] UbicacionBackup_RutaRelativa_Rechazada — invalid (sad path)
- [ ] UbicacionBackup_RutaVacia_Rechazada — faltante (sad path)
- [ ] UbicacionBackup_ToString_SinRuta
- [ ] CopiaBackup_NombreYFecha_IdaYVuelta — validates AC-04
- [ ] CopiaBackup_NombreAjeno_Rechazado — invalid (sad path)
- [ ] Retencion_CincoMeses_SieteDiariasYCuatroMensuales — validates AC-11
- [ ] Retencion_UltimaDeOctubreEsMensual — validates AC-12
- [ ] Retencion_CopiasFuturas_NoDesplazanLasDiarias — invalid (sad path)
- [ ] Retencion_MesSinCopias_TresMensuales
- [ ] CopiaBackup_FechaInexistente_Rechazada — invalid (sad path)
- [ ] Repositorio_GrabaYLeeLaUbicacion — validates AC-01
- [ ] Repositorio_FilaCorrupta_ErrorSinRuta — error (sad path)
- [ ] Historial_UpdateOBorrado_RechazadoPorTrigger — rejected (sad path)
- [ ] Historial_UltimaFecha — validates AC-18
- [ ] Migracion_Backup_ConservaTodosLosTriggers
- [ ] Migracion_Backup_Down_Revierte

**Completion criterion**
Las 16 pruebas pasan y la migración sobre una base de FEAT-009 conserva todos los triggers existentes.

## Block 2 — Casos de uso y Api de la ubicación

**Files**
- `src/Optica.Application/Backup/IInspectorUbicaciones.cs` (new) — `MismaUnidad(rutaA, rutaB)`, `EnNubeConocida(ruta)`,
  `Existe(ruta)`, `PuedeEscribir(ruta)`.
- `src/Optica.Infrastructure/Backup/InspectorUbicaciones.cs` (new) — normaliza con `Path.GetFullPath`; rechaza rutas
  UNC, unidades de red (`DriveType.Network`) y los prefijos `\\?\` y `\\.\` (D-17); unidad por `Path.GetPathRoot`
  sin distinguir mayúsculas en Windows o, sin letras de unidad (pruebas en Linux y e2e), por el punto de montaje de
  prefijo más largo de `DriveInfo.GetDrives()`; escritura con un archivo sonda de un byte que se borra. Las reglas puras
  (`MismaUnidadEntre`, `DentroDeAlguna`, que compara por segmentos sobre rutas normalizadas) son funciones estáticas.
  Las unidades de `subst` quedan como limitación documentada (D-17).
- `src/Optica.Application/Backup/IFuenteCarpetasNube.cs` (new) e `src/Optica.Infrastructure/Backup/FuenteCarpetasNube.cs`
  (new) — carpetas de nube de la cuenta que corre la Api: variables `OneDrive`, `OneDriveConsumer`, `OneDriveCommercial`;
  la ruta de `Dropbox\info.json` en `%LOCALAPPDATA%`/`%APPDATA%` leída de forma tolerante; `Mi unidad`/`My Drive` y
  `Google Drive` de Google Drive para escritorio; inyectable para las pruebas (D-18).
- `src/Optica.Application/Backup/ObtenerUbicacionBackup.cs`, `GrabarUbicacionBackup.cs` (new) — en una transacción;
  rechaza también la carpeta de datos, una que la contenga o una que esté dentro de ella (D-19); log con fecha UTC sin
  la ruta.
- `src/Optica.Api/Controllers/ConfiguracionController.cs` (modified) — `GET` y `POST /api/configuracion/backup`.
- `src/Optica.Api/Errores/RespuestasValidacion.cs` (modified) — acción de cuerpo ilegible.
- `src/Optica.Api/Program.cs` y `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — registros.
- Pruebas: `tests/Optica.Application.Tests/Backup/GrabarUbicacionBackupTests.cs`,
  `tests/Optica.Infrastructure.Tests/Backup/InspectorUbicacionesTests.cs`,
  `tests/Optica.Api.Tests/Backup/UbicacionBackupApiTests.cs` (new); `ProteccionTests.cs`, `SinCacheTests.cs`,
  `SinDatosArcaTests.cs` (modified).

**Logic**
Implementa FR-01, FR-02 y FR-03 por la Api.

**API contract**
- Method + path: `GET /api/configuracion/backup` y `POST /api/configuracion/backup`.
- Request (POST): `ruta` (string); JSON estricto, `Content-Type: application/json`, cuerpo hasta 2 KB.
- Response: `GET` 200 `{ ruta: string | null }`; `POST` 200 `{ ruta }` grabada.
- Error codes: 400 con `errors.ruta` (relativa, UNC o de red, inexistente, sin escritura, misma unidad fuera de nube
  conocida, carpeta de datos o dentro de ella); 401 sin sesión; 413; 415; 500 genérico.
- Auth: sesión con cookie de FEAT-001a (fuera de `RutasAnonimas`).

**Data model**
- Reuses `ConfiguracionBackup` from Block 1 — no schema change; constraints as declared there: `Id = 1`, `Ruta` NOT
  NULL con CHECK de largo 1 a 260.

**Input validation**
- `ruta`: las reglas del Block 1 más: no es UNC, de red ni de dispositivo; existe, admite escritura, otra unidad o
  dentro de nube conocida; no es, no contiene ni está dentro de la carpeta de datos.

**Error handling**
- Ruta inválida, relativa, UNC o de red — 400 junto a `ruta`, sin repetirla.
- Carpeta inexistente o sin permiso de escritura — 400 junto a `ruta`.
- Misma unidad fuera de nube conocida — 400 junto a `ruta`.
- Cuerpo ilegible — 400; cuerpo grande — 413; sin sesión — 401.

**Required tests**
- [ ] Inspector_MismaUnidadEntre_LetrasYMontajes — validates AC-02
- [ ] Inspector_DentroDeNubeConocida — validates AC-17
- [ ] Grabar_OtraUnidad_Graba — validates AC-01
- [ ] Grabar_MismaUnidadFueraDeNube_Rechazado — rejected, validates AC-02 (sad path)
- [ ] Grabar_MismaUnidadEnOneDrive_Graba — validates AC-17
- [ ] Grabar_Inexistente_Rechazado — invalid, validates AC-03 (sad path)
- [ ] Grabar_SinEscritura_Rechazado — invalid, validates AC-03 (sad path)
- [ ] Grabar_RutaUncODispositivo_Rechazada — rejected (sad path)
- [ ] Grabar_DentroDeCarpetaDatos_Rechazado — rejected (sad path)
- [ ] Inspector_DentroDeAlguna_PorSegmentos
- [ ] PostBackup_RutaRelativa_400SinRepetirla — invalid (sad path)
- [ ] PostBackup_CuerpoIlegible_400 — invalid (sad path)
- [ ] PostBackup_CuerpoGrande_413 — rejected (sad path)
- [ ] RutasBackup_SinSesion_401 — unauthorized (sad path)
- [ ] GetBackup_SinUbicacion_200ConNull
- [ ] RutasBackup_NoStore
- [ ] PostBackup_Registro_SinRuta — valores testigo fuera del log

**Completion criterion**
Las 17 pruebas pasan y `SinDatosArcaTests` y `ProteccionTests` siguen en verde con las rutas nuevas.

## Block 3 — Pantalla de la ubicación

**Files**
- `web/src/api/cliente.ts` (modified) — `consultarUbicacionBackup`, `grabarUbicacionBackup`, lectores estrictos.
- `web/src/paginas/SeccionBackup.tsx` (new) — sección "Backup" con el campo de la carpeta, su botón "Grabar ubicación
  del backup" y errores junto al campo; sin mensajes de estado en reposo.
- `web/src/paginas/Configuracion.tsx` (modified) — monta la sección.
- Pruebas: `web/src/paginas/SeccionBackup.test.tsx` (new); `Configuracion.test.tsx`, `App.test.tsx`,
  `cliente.test.ts` (modified, ruta del backup en los servidores falsos).

**Logic**
Implementa FR-01 a FR-03 en la pantalla.

**Input validation**
- Ruta obligatoria y de hasta 260 caracteres (espejo); el resto lo decide el servidor.

**Error handling**
- Error del servidor — junto al campo; el valor escrito queda.
- Fallo de red o 500 — mensaje general sin la ruta.

**Required tests**
- [ ] Backup_GrabaUbicacion_LaMuestraAlVolver — validates AC-01
- [ ] Backup_MismaUnidad_ErrorJuntoAlCampo — rejected, validates AC-02 (sad path)
- [ ] Backup_SinEscritura_ErrorJuntoAlCampo — invalid, validates AC-03 (sad path)
- [ ] Backup_ErrorDelServidor_MensajeGeneral — error (sad path)

**Completion criterion**
Las 4 pruebas pasan y `npm run lint`, `npm run build` y `npm run test:coverage` siguen en verde.

## Block 4 — Copia, verificación, historial y retención

**Files**
- `src/Optica.Application/Backup/ICopiadorBackup.cs`, `IAlmacenCopias.cs` (new) — copiar la base a un temporal y
  verificar; listar copias, renombrar el temporal, borrar copias y temporales viejos.
- `src/Optica.Application/Backup/HacerBackup.cs` (new) — dos motivos (D-20): `AlIniciar` copia solo si falta la de ayer
  (ni ayer ni hoy en el historial, regla única compartida con el estado); `Programado` (20:00) copia siempre y reemplaza
  la copia de hoy después de verificar la nueva. Sin ubicación no hace nada (log en Debug). Antes de copiar borra los
  `.parcial*` huérfanos; copia, verifica, renombra (con reemplazo solo después de verificar), anota en el historial y
  aplica la retención solo si la copia quedó verificada; log con fecha UTC, resultado y tipo de falla, sin rutas.
- `src/Optica.Application/Backup/ICandadoBackup.cs` (new) — puerto del candado (D-22).
- `src/Optica.Infrastructure/Backup/CopiadorBackupSqlite.cs` (new) — conexiones propias sin pool (`Pooling=False`) y
  con cierre explícito para el origen (`Mode=ReadWrite`, sin crear), el destino `optica-AAAA-MM-DD.db.parcial` y la
  verificación; ninguna pasa por `FabricaConexion.Configurar` (D-23); `BackupDatabase` en un paso, `PRAGMA
  journal_mode=DELETE` en la copia y `PRAGMA integrity_check` (`ok`).
- `src/Optica.Infrastructure/Backup/AlmacenCopiasArchivo.cs` (new) — solo archivos con el patrón exacto en la carpeta
  configurada; nunca subcarpetas ni otros archivos.
- `src/Optica.Infrastructure/Backup/CandadoBackup.cs` (new) — implementa `ICandadoBackup`, singleton: un backup a la
  vez.
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` y `src/Optica.Api/Program.cs` (modified) — registros.
- Pruebas: `tests/Optica.Application.Tests/Backup/HacerBackupTests.cs`,
  `tests/Optica.Infrastructure.Tests/Backup/CopiadorBackupSqliteTests.cs`, `AlmacenCopiasArchivoTests.cs` (new).

**Logic**
Implementa FR-05, FR-06, FR-07, FR-10, FR-11 y FR-12.

**Data model**
- Reuses `BackupRealizado` and `ConfiguracionBackup` from Block 1 — no schema change; constraints as declared there:
  `Fecha` NOT NULL con CHECK GLOB, triggers de solo agregar, `ConfiguracionBackup.Id = 1`.

**Input validation**
- La fecha de la copia sale de `TimeProvider` (local); los nombres se arman con `CopiaBackup.Nombre`, nunca con texto
  del usuario.

**Error handling**
- Carpeta inaccesible o llena (IO) — falla del día, se borra el `.parcial`, no se borra ninguna copia.
- `integrity_check` distinto de `ok` — falla del día, no se renombra, no se anota en el historial.
- Backup en curso — el segundo pedido no hace nada (candado).

**Required tests**
- [ ] Backup_CopiaConFechaDelDia — validates AC-04
- [ ] Backup_IncluyePresupuestoGrabadoAntes — validates AC-05
- [ ] Backup_GrabarDuranteElBackup_SinError — validates AC-06
- [ ] Backup_CarpetaDesconectada_SinCopiaDelDia — error, validates AC-07 (sad path)
- [ ] Backup_IntegridadFallida_NoCuenta — invalid (sad path)
- [ ] Backup_Falla_NoBorraCopias — error, validates AC-13 (sad path)
- [ ] Backup_Retencion_SoloArchivosConPatron — validates AC-11
- [ ] Backup_SinWalNiShmEnElDestino
- [ ] Backup_EnCurso_SegundoPedidoNoHaceNada — conflict (sad path)
- [ ] Backup_ParcialHuerfano_SeBorraAlEmpezar
- [ ] Backup_Programado_ReemplazaLaCopiaDeHoyDespuesDeVerificar
- [ ] Backup_AlIniciarConLaDeAyer_NoRepite
- [ ] Backup_Registro_SinRutas

**Completion criterion**
Las 13 pruebas pasan con SQLite real en carpetas temporales y ninguna copia queda con `-wal` ni `-shm`.

## Block 5 — Programación diaria y al iniciar

**Files**
- `src/Optica.Api/Backup/ProgramadorBackup.cs` (new) — `BackgroundService` que solo agenda: al arrancar ejecuta
  `HacerBackup(AlIniciar)`; después despierta cada 5 minutos con `TimeProvider` y, si son las 20:00 o más y
  `HuboCopiaProgramadaAsync(hoy)` es falso, ejecuta `HacerBackup(Programado)` (D-24: tolera suspensión, saltos de reloj y
  reintenta si el candado estaba tomado); cada ejecución en un alcance propio (`IServiceScopeFactory`); una falla queda
  en el log y no detiene el servicio.
- `src/Optica.Api/Program.cs` (modified) — registro solo en la rama del servidor.
- `src/Optica.Api/appsettings.json` (modified) — categoría del backup al Registro de eventos para el éxito diario (las
  fallas ya llegan por Warning y Error).
- `tests/Optica.Api.Tests/FabricaApi.cs` (modified) — quita el servicio de backup por defecto (D-25).
- Pruebas: `tests/Optica.Api.Tests/Backup/ProgramadorBackupTests.cs` (new, FakeTimeProvider);
  `tests/Optica.Api.Tests/Configuracion/ConfiguracionApiTests.cs` (modified, prueba hermana del filtro).

**Logic**
Implementa FR-04 y FR-08.

**Input validation**
- No recibe entrada del usuario; la hora sale de `TimeProvider`.

**Error handling**
- Falla del caso de uso — se registra el tipo de falla y el servicio sigue con la próxima ejecución.
- Cancelación al detener la Api — se respeta antes y después de la copia (la API de backup corre en un paso); un
  `.parcial` interrumpido se borra en la próxima ejecución.

**Required tests**
- [ ] Programador_A_las_20_HaceElBackup — validates AC-04
- [ ] Programador_AlIniciarSinElDeAyer_HaceElBackup — validates AC-08
- [ ] Programador_AlIniciarConElDeAyer_NoRepite
- [ ] Programador_CopiaAlIniciar_YA_las_20_VuelveACopiar — validates AC-04
- [ ] Programador_SaltoDeReloj_CopiaUnaSolaVez
- [ ] Programador_FallaDelBackup_SigueFuncionando — error (sad path)
- [ ] Programador_DetenerDuranteElBackup_ParcialSeLimpiaDespues — error (sad path)
- [ ] Appsettings_Backup_VaAlRegistroDeEventos

**Completion criterion**
Las 8 pruebas pasan con reloj falso y la suite de la Api no arranca el servicio de backup (`FabricaApi` lo quita).

## Block 6 — Estado y aviso al ingresar

**Files**
- `src/Optica.Application/Backup/ObtenerEstadoBackup.cs` (new) — `faltaAyer` (la misma regla que `HacerBackup`),
  `ultimo` (mayor fecha del historial que no pasa de hoy, o null), `ubicacion` (`elegida`, `sin_elegir`) y `accesible`
  (la ubicación existe y se puede leer); sin rutas.
- `src/Optica.Api/Controllers/BackupController.cs` (new) — `GET /api/backup/estado`.
- `src/Optica.Api/Program.cs` (modified) — registro.
- `web/src/api/cliente.ts` (modified) — `consultarEstadoBackup` con lector estricto.
- `web/src/paginas/Inicio.tsx` (modified) — aviso con `role="status"`: "Falta el backup de ayer. El último backup es
  del 29/10/2026." o "Falta el backup de ayer. No hay ningún backup disponible.", más "La ubicación del backup no está
  accesible." cuando corresponde, o "No hay una ubicación de backup elegida." si no hay ubicación (D-26); sin enlaces ni la palabra "configuración" en el texto; si el estado falla, no muestra
  nada.
- `e2e/tests/acceso.spec.ts` (modified) — `getByRole('status')` filtrado por su texto (el aviso de backup aparece en
  bases nuevas).
- Pruebas: `tests/Optica.Application.Tests/Backup/ObtenerEstadoBackupTests.cs`,
  `tests/Optica.Api.Tests/Backup/EstadoBackupApiTests.cs` (new); `web/src/paginas/Inicio.test.tsx`, `App.test.tsx`,
  `cliente.test.ts`, `ProteccionTests.cs`, `SinCacheTests.cs`, `SinDatosArcaTests.cs` (modified).

**Logic**
Implementa FR-09.

**API contract**
- Method + path: `GET /api/backup/estado`.
- Request: sin cuerpo.
- Response: 200 `{ faltaAyer: bool, ultimo: "2026-10-29" | null, ubicacion: "elegida" | "sin_elegir", accesible: bool }`.
- Error codes: 401 sin sesión; 500 genérico (una carpeta ilegible responde `accesible: false`, no 500).
- Auth: sesión con cookie de FEAT-001a.

**Data model**
- Reuses `BackupRealizado` and `ConfiguracionBackup` from Block 1 — no schema change; constraints as declared there:
  `Fecha` NOT NULL con CHECK GLOB, solo agregar, `Id = 1`.

**Input validation**
- No recibe entrada.

**Error handling**
- Carpeta ilegible o pendrive desconectado — `accesible: false`, sin 500.
- Sin sesión — 401.

**Required tests**
- [ ] Estado_FaltaAyerConUltimo29 — validates AC-09
- [ ] Estado_SinNingunBackup — validates AC-10
- [ ] Estado_PendriveDesconectado_UltimoDelHistorial — validates AC-18
- [ ] GetEstado_SinRutas
- [ ] GetEstado_CarpetaIlegible_AccesibleFalseSin500 — error (sad path)
- [ ] RutasEstado_SinSesion_401 — unauthorized (sad path)
- [ ] Inicio_AvisoBackupConFecha — validates AC-09
- [ ] Inicio_AvisoSinNingunBackup — validates AC-10
- [ ] Inicio_EstadoFalla_SinAviso — error (sad path)

**Completion criterion**
Las 9 pruebas pasan y las pruebas existentes de Inicio y del e2e de acceso siguen en verde.

## Block 7 — Restauración por comando

**Files**
- `src/Optica.Api/Program.cs` (modified) — selector de comandos que reconoce `restablecer-contrasena` (sin cambios de
  comportamiento) y `restaurar-backup <copia>`; código de falla compartido.
- `src/Optica.Api/Backup/ComandoRestaurarBackup.cs` (new) — solo consola, candado y confirmación, como
  `ComandoRestablecerContrasena` (D-27): toma el candado de instancia (Api detenida), muestra el resultado de la
  verificación previa y cuántas facturas con CAE tiene la base actual posteriores a la última de la copia, pide escribir
  `RESTAURAR`, ejecuta el caso de uso y recuerda revisar en ARCA los comprobantes emitidos desde la fecha de la copia y
  conservar `anterior/`; salida sin rutas con el usuario.
- `src/Optica.Application/Backup/RestaurarBackup.cs` (new) — caso de uso: verifica la copia (patrón de nombre,
  `integrity_check`, `__EFMigrationsHistory` presente y con un prefijo de las migraciones conocidas: una copia de una
  versión más nueva o que no es de la óptica se rechaza, D-28); cuenta las facturas posteriores; restaura con el puerto.
- `src/Optica.Application/Backup/IRestauradorBase.cs` (new) — puerto: verificar copia, restaurar a un temporal,
  apartar la base actual y poner la restaurada, volver atrás.
- `src/Optica.Infrastructure/Backup/RestauradorSqlite.cs` (new) — restaura con la API de backup a
  `optica.db.restaurando` en la carpeta de datos (copia abierta `Mode=ReadOnly`, conexiones sin pool), verifica, mueve
  `optica.db`, `-wal` y `-shm` a `anterior/AAAAMMDD-HHMMSS/` y renombra el temporal a `optica.db`; si un paso posterior
  al apartado falla, devuelve la base desde `anterior/` (D-29). No migra: la Api migra al arrancar.
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — registro.
- `docs/instalacion.md` (modified) — §4 permisos de la carpeta de backup; §9 reescrita: backup automático y ubicación
  en la configuración, nube conocida de la cuenta de la Api, pendrive FAT/exFAT sin permisos (bajo llave), nube con dos
  pasos y papelera de 30 a 93 días, copias de la ubicación anterior al cambiarla, `.parcial` huérfanos, resguardo aparte
  del certificado de ARCA, de la marca y de `arca-simulado.json` (D-29); §10 restauración con el comando, revisión en
  ARCA y conservación de `anterior/` bajo llave mientras tenga facturas que la copia no tiene.
- Pruebas: `tests/Optica.Api.Tests/Backup/ComandoRestaurarBackupTests.cs`,
  `tests/Optica.Application.Tests/Backup/RestaurarBackupTests.cs`,
  `tests/Optica.Infrastructure.Tests/Backup/RestauradorSqliteTests.cs` (new);
  `tests/Optica.Api.Tests/Acceso/ComandoRestablecerContrasenaTests.cs` (modified, selector);
  `tests/Optica.Api.Tests/Acceso/ProteccionTests.cs` (modified, ninguna ruta restaura).

**Logic**
Implementa FR-13, FR-14 y NFR-01.

**Input validation**
- Un único argumento: la ruta de una copia existente con el patrón `optica-AAAA-MM-DD.db`; confirmación exacta
  `RESTAURAR`.

**Error handling**
- Copia inexistente, corrupta, de una versión más nueva o ajena — sale con código de falla sin tocar la base.
- Api en marcha (candado tomado) — sale sin tocar la base.
- Confirmación distinta — sale sin tocar la base.
- Falla a mitad de la restauración — la base actual vuelve sola desde `anterior/`; nunca queda la carpeta sin
  `optica.db`.

**Required tests**
- [ ] Restaurar_CopiaValida_AbrePresupuestoDeLaCopia — validates AC-14
- [ ] Restaurar_CopiaCorrupta_Rechazada — invalid (sad path)
- [ ] Restaurar_ApiEnMarcha_Rechazado — conflict (sad path)
- [ ] Restaurar_SinConfirmacion_Rechazado — rejected (sad path)
- [ ] Restaurar_FallaAMitad_BaseActualIntacta — error (sad path)
- [ ] Restaurar_CopiaDeVersionMasNueva_Rechazada — rejected (sad path)
- [ ] Restaurar_InformaFacturasPosteriores
- [ ] Restaurar_CopiaVieja_LaApiMigraAlArrancar
- [ ] Api_NingunaRutaRestaura — validates AC-15
- [ ] Program_SelectorDeComandos_RestablecerSigueIgual

**Completion criterion**
Las 10 pruebas pasan, `ComandoRestablecerContrasenaTests` sigue en verde y la guía describe la restauración completa.

## Block 8 — e2e

**Files**
- `e2e/tests/configuracion-backup.spec.ts` (new) — AC-01 a AC-03 con una carpeta de backup en otro punto de montaje
  (`/dev/shm` en Linux) creada y borrada por el harness.
- `e2e/tests/backup-aviso.spec.ts` (new) — AC-10 (base nueva sin backups) y AC-15 en pantalla (ningún texto ni botón
  de restaurar).
- `e2e/servidor.mjs`, `e2e/compilacion.mjs`, `e2e/compilacion.test.mjs` (modified) — carpeta de backup temporal por
  archivo y su borrado.
- `e2e/README.md` (modified) — tabla de proyectos y puertos.

**Logic**
Implementa NFR-03: los criterios con interfaz (FR-01 a FR-03, FR-09, FR-14) de punta a punta en Chrome y Edge
estables.

**Data model**
- Reuses `ConfiguracionBackup` and `BackupRealizado` from Block 1 — no schema change; constraints as declared there:
  `Id = 1`, `Ruta` NOT NULL con CHECK de largo, `Fecha` NOT NULL con CHECK GLOB y triggers de solo agregar.

**Input validation**
- El harness rechaza una carpeta de backup que no sea temporal propia.

**Error handling**
- Sin un segundo punto de montaje disponible — el caso positivo de AC-01 se salta con motivo explícito (los .NET lo
  cubren); nunca se desactiva la regla con configuración.

**Required tests**
- [ ] configuracion-backup: graba, misma unidad rechazada, sin escritura rechazada — validates AC-01, AC-02, AC-03
- [ ] backup-aviso: aviso sin ningún backup y sin opción de restaurar — validates AC-10, AC-15
- [ ] compilacion: carpeta de backup no temporal rechazada — invalid (sad path)
- [ ] los dos archivos en los proyectos `chrome-*` y `msedge-*` — validates AC-16

**Completion criterion**
Los dos archivos pasan en `chrome-*` y `msedge-*` y `npm run test:unidad` sigue en verde.

## Final verification
`dotnet build` y `dotnet test Optica.sln`, `npm run lint`, `npm run test:lint`, `npm run test:coverage` y
`npm run build` en `web/`, `npm run test:unidad` y `npm run test:harness` en `e2e/` en verde; ninguna copia queda con
`-wal` ni `-shm`; ningún log contiene la ruta del backup; la restauración medida en la PC del local y la versión anterior
de los navegadores quedan como verificación manual del usuario.
