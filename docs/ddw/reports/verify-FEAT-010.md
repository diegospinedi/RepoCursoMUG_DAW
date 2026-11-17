# Verification FEAT-010

Backup y restauración. Ronda 1 de VERIFY, sobre el commit del cierre de CODE `84babab` (rama
`feat/FEAT-010-backup-restauracion`, base `7330d05`). Verificación cruzada de ddw-module-verifier (agente que no
escribió el código) contra `docs/ddw/prd/prd-FEAT-010.md`, `docs/ddw/specs/spec-FEAT-010.md`,
`docs/ddw/specs/decisions-FEAT-010.md` (D-01 a D-38), `docs/ddw/security/threat-FEAT-010.md` y la corrida
`docs/ddw/reports/tests-FEAT-010.md` (4523/4523). El verificador volvió a correr .NET con cobertura (2819/2819), la web
con cobertura (1335/1335), oxlint, `tsc --noEmit`, `test:lint` 8/8 y la e2e de unidad (159/159); el harness Playwright
(210/210) queda del informe de pruebas.

| Field | Value |
|---|---|
| Module | `src/Optica.Domain/Backup` (UbicacionBackup, CopiaBackup, PoliticaRetencion), `src/Optica.Application/Backup` (ObtenerUbicacionBackup, GrabarUbicacionBackup, HacerBackup, ConsultarBackupProgramado, ObtenerEstadoBackup, RestaurarBackup, FormaRuta y puertos), `src/Optica.Infrastructure/Backup` (InspectorUbicaciones, FuenteCarpetasNube, CopiadorBackupSqlite, AlmacenCopiasArchivo, CandadoBackup, RestauradorSqlite, VerificacionSqlite, TriggersDeProteccion), `Persistencia` (repositorios y migración Backup), `src/Optica.Api` (ConfiguracionController, BackupController, ProgramadorBackup, ComandoRestaurarBackup, Program), `web/src` (SeccionBackup, Inicio, cliente), `e2e` (configuracion-backup, backup-aviso, harness), `docs/instalacion.md` |
| Line coverage | 95.2% |
| Branch coverage | 87.8% |
| Function coverage | 98.7% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean; `dotnet build` — 0 warnings; `npm run lint` (oxlint) — clean; `tsc --noEmit` — clean; `npm run build` — clean; `npm run test:lint` 8/8 |

Cobertura de la tabla: los archivos .NET de producción del ticket (nuevos o modificados, sin migraciones), medida por el
verificador con las líneas sin duplicar: líneas 95,2 %, ramas 87,8 %, funciones 98,7 %. El informe de pruebas declara
96,9 / 87,7 / 98,7 porque su denominador de líneas cuenta líneas repetidas en clases generadas por el compilador; ramas
y funciones coinciden y el piso se cumple con cualquiera de las dos mediciones. Suite completa: .NET 95,3 / 89,3 / 97,6
(sin duplicar); web 99 / 97,13 / 100 / 98,95. Por capa, en el ticket: Domain 98,0 / 95,2 / 100; Application 98,0 /
89,5 / 100; Infrastructure 96,2 / 85,1 / 98,3; Api 91,5 / 89,4 / 98,8.

## Acceptance criteria
- ✅ AC-01 — `Grabar_OtraUnidad_Graba`, `PostBackup_Registro_SinRuta`, `Repositorio_GrabaYLeeLaUbicacion`, `Backup_GrabaUbicacion_LaMuestraAlVolver`; e2e `configuracion-backup.spec.ts:189` (`GrabarUbicacionBackup.cs:EjecutarAsync`)
- ✅ AC-02 — `Inspector_MismaUnidadEntre_LetrasYMontajes`, `Grabar_MismaUnidadFueraDeNube_Rechazado`, `Backup_MismaUnidad_ErrorJuntoAlCampo`; e2e `configuracion-backup.spec.ts:146`
- ✅ AC-03 — `Grabar_Inexistente_Rechazado`, `Grabar_SinEscritura_Rechazado`, `Backup_SinEscritura_ErrorJuntoAlCampo`; e2e `configuracion-backup.spec.ts:160` y `:171`
- ✅ AC-04 — `Backup_CopiaConFechaDelDia`, `CopiaBackup_NombreYFecha_IdaYVuelta`, `Programador_A_las_20_HaceElBackup`, `Programador_CopiaAlIniciar_YA_las_20_VuelveACopiar` (`HacerBackup.cs:EjecutarAsync`, `ProgramadorBackup.cs:ExecuteAsync`)
- ✅ AC-05 — `Backup_IncluyePresupuestoGrabadoAntes`: el presupuesto solo en el WAL llega a la copia y una copia de archivo común no lo tiene (`CopiadorBackupSqlite.cs:CopiarAsync`)
- ✅ AC-06 — `Backup_GrabarDuranteElBackup_SinError`: 0 errores con escrituras durante la copia y copia íntegra
- ✅ AC-07 — `Backup_CarpetaDesconectada_SinCopiaDelDia`, `Backup_IntegridadFallida_NoCuenta`: sin copia ni historial y el reintento funciona
- ✅ AC-08 — `Programador_AlIniciarSinElDeAyer_HaceElBackup`: copia al arrancar con la fecha de hoy (D-11)
- ✅ AC-09 — `Estado_FaltaAyerConUltimo29`, `GetEstado_SinRutas`, `Inicio_AvisoBackupConFecha` (`ObtenerEstadoBackup.cs:EjecutarAsync`, `Inicio.tsx`); en .NET y en la web (D-16)
- ✅ AC-10 — `Estado_SinNingunBackup`, `GetEstado_BaseNueva_SinBackupNiUbicacion`, `Inicio_AvisoSinNingunBackup`; e2e `backup-aviso.spec.ts:83`
- ✅ AC-11 — `Retencion_CincoMeses_SieteDiariasYCuatroMensuales`, `Backup_Retencion_SoloArchivosConPatron` (`PoliticaRetencion.cs:Conservar`)
- ✅ AC-12 — `Retencion_UltimaDeOctubreEsMensual`
- ✅ AC-13 — `Backup_Falla_NoBorraCopias` (6 puntos de corte, ninguna copia borrada)
- ✅ AC-14 — `Restaurar_CopiaValida_AbrePresupuestoDeLaCopia`: restauración real en una instalación nueva que abre el presupuesto 7; la medición de menos de 4 horas en la PC del local es verificación manual (D-14)
- ✅ AC-15 — `Api_NingunaRutaRestaura` (por reflexión); e2e `backup-aviso.spec.ts:102` (Inicio y las pantallas del menú)
- ✅ AC-16 — los archivos e2e del ticket en los proyectos `chrome-*` y `msedge-*` (210/210); la versión anterior es verificación manual (D-02)
- ✅ AC-17 — `Inspector_DentroDeNubeConocida`, `Grabar_MismaUnidadEnOneDrive_Graba` (`FuenteCarpetasNube.cs`, `InspectorUbicaciones.cs`); en .NET (D-16, D-32)
- ✅ AC-18 — `Historial_UltimaFecha`, `Estado_PendriveDesconectado_UltimoDelHistorial`, `GetEstado_CarpetaIlegible_AccesibleFalseSin500`, `Inicio_AvisoBackupConFecha` (caso no accesible)

## Spec blocks
- ✅ Block 1 — Dominio y persistencia de la ubicación, el historial y la retención: 16/16 pruebas (D-30)
- ✅ Block 2 — Casos de uso y Api de la ubicación: 17/17 (D-31)
- ✅ Block 3 — Pantalla de la ubicación: 4/4 (D-32)
- ✅ Block 4 — Copia, verificación, historial y retención: 13/13 (D-33)
- ✅ Block 5 — Programación diaria y al iniciar: 8/8 (D-34)
- ✅ Block 6 — Estado y aviso al ingresar: 9/9 (D-35)
- ✅ Block 7 — Restauración por comando: 10/10, guía §9 y §10 (D-36, D-38)
- ✅ Block 8 — e2e: 4/4 (D-37)

Las pruebas de la web con `it.each` llevan el sufijo ` (%s)` en el nombre y cuentan como equivalentes. La evidencia de
rojo de cada bloque está en `.ddw-work/review-block-1.md` a `review-block-8.md`; `backup-aviso.spec.ts` no tuvo rojo
propio y se mostró que puede fallar con mutaciones revertidas (D-37).

## Required tests (spec)

Block 1:
- ✅ `UbicacionBackup_RutaRelativa_Rechazada`
- ✅ `UbicacionBackup_RutaVacia_Rechazada`
- ✅ `UbicacionBackup_ToString_SinRuta`
- ✅ `CopiaBackup_NombreYFecha_IdaYVuelta`
- ✅ `CopiaBackup_NombreAjeno_Rechazado`
- ✅ `Retencion_CincoMeses_SieteDiariasYCuatroMensuales`
- ✅ `Retencion_UltimaDeOctubreEsMensual`
- ✅ `Retencion_CopiasFuturas_NoDesplazanLasDiarias`
- ✅ `Retencion_MesSinCopias_TresMensuales`
- ✅ `CopiaBackup_FechaInexistente_Rechazada`
- ✅ `Repositorio_GrabaYLeeLaUbicacion`
- ✅ `Repositorio_FilaCorrupta_ErrorSinRuta`
- ✅ `Historial_UpdateOBorrado_RechazadoPorTrigger`
- ✅ `Historial_UltimaFecha`
- ✅ `Migracion_Backup_ConservaTodosLosTriggers`
- ✅ `Migracion_Backup_Down_Revierte`

Block 2:
- ✅ `Inspector_MismaUnidadEntre_LetrasYMontajes`
- ✅ `Inspector_DentroDeNubeConocida`
- ✅ `Grabar_OtraUnidad_Graba`
- ✅ `Grabar_MismaUnidadFueraDeNube_Rechazado`
- ✅ `Grabar_MismaUnidadEnOneDrive_Graba`
- ✅ `Grabar_Inexistente_Rechazado`
- ✅ `Grabar_SinEscritura_Rechazado`
- ✅ `Grabar_RutaUncODispositivo_Rechazada`
- ✅ `Grabar_DentroDeCarpetaDatos_Rechazado`
- ✅ `Inspector_DentroDeAlguna_PorSegmentos`
- ✅ `PostBackup_RutaRelativa_400SinRepetirla`
- ✅ `PostBackup_CuerpoIlegible_400`
- ✅ `PostBackup_CuerpoGrande_413`
- ✅ `RutasBackup_SinSesion_401`
- ✅ `GetBackup_SinUbicacion_200ConNull`
- ✅ `RutasBackup_NoStore`
- ✅ `PostBackup_Registro_SinRuta`

Block 3:
- ✅ `Backup_GrabaUbicacion_LaMuestraAlVolver`
- ✅ `Backup_MismaUnidad_ErrorJuntoAlCampo`
- ✅ `Backup_SinEscritura_ErrorJuntoAlCampo`
- ✅ `Backup_ErrorDelServidor_MensajeGeneral`

Block 4:
- ✅ `Backup_CopiaConFechaDelDia`
- ✅ `Backup_IncluyePresupuestoGrabadoAntes`
- ✅ `Backup_GrabarDuranteElBackup_SinError`
- ✅ `Backup_CarpetaDesconectada_SinCopiaDelDia`
- ✅ `Backup_IntegridadFallida_NoCuenta`
- ✅ `Backup_Falla_NoBorraCopias`
- ✅ `Backup_Retencion_SoloArchivosConPatron`
- ✅ `Backup_SinWalNiShmEnElDestino`
- ✅ `Backup_EnCurso_SegundoPedidoNoHaceNada`
- ✅ `Backup_ParcialHuerfano_SeBorraAlEmpezar`
- ✅ `Backup_Programado_ReemplazaLaCopiaDeHoyDespuesDeVerificar`
- ✅ `Backup_AlIniciarConLaDeAyer_NoRepite`
- ✅ `Backup_Registro_SinRutas`

Block 5:
- ✅ `Programador_A_las_20_HaceElBackup`
- ✅ `Programador_AlIniciarSinElDeAyer_HaceElBackup`
- ✅ `Programador_AlIniciarConElDeAyer_NoRepite`
- ✅ `Programador_CopiaAlIniciar_YA_las_20_VuelveACopiar`
- ✅ `Programador_SaltoDeReloj_CopiaUnaSolaVez`
- ✅ `Programador_FallaDelBackup_SigueFuncionando`
- ✅ `Programador_DetenerDuranteElBackup_ParcialSeLimpiaDespues`
- ✅ `Appsettings_Backup_VaAlRegistroDeEventos`

Block 6:
- ✅ `Estado_FaltaAyerConUltimo29`
- ✅ `Estado_SinNingunBackup`
- ✅ `Estado_PendriveDesconectado_UltimoDelHistorial`
- ✅ `GetEstado_SinRutas`
- ✅ `GetEstado_CarpetaIlegible_AccesibleFalseSin500`
- ✅ `RutasEstado_SinSesion_401`
- ✅ `Inicio_AvisoBackupConFecha`
- ✅ `Inicio_AvisoSinNingunBackup`
- ✅ `Inicio_EstadoFalla_SinAviso`

Block 7:
- ✅ `Restaurar_CopiaValida_AbrePresupuestoDeLaCopia`
- ✅ `Restaurar_CopiaCorrupta_Rechazada`
- ✅ `Restaurar_ApiEnMarcha_Rechazado`
- ✅ `Restaurar_SinConfirmacion_Rechazado`
- ✅ `Restaurar_FallaAMitad_BaseActualIntacta`
- ✅ `Restaurar_CopiaDeVersionMasNueva_Rechazada`
- ✅ `Restaurar_InformaFacturasPosteriores`
- ✅ `Restaurar_CopiaVieja_LaApiMigraAlArrancar`
- ✅ `Api_NingunaRutaRestaura`
- ✅ `Program_SelectorDeComandos_RestablecerSigueIgual`

Block 8:
- ✅ `los`

## Tests
- ✅ Sad-path tests: `POST /api/configuracion/backup` (ruta relativa o en blanco, cuerpo ilegible, 413, 415, 401, UNC, misma unidad, inexistente, sin escritura, carpeta de datos, demasiado larga); `GET /api/configuracion/backup` y `GET /api/backup/estado` (401, carpeta ilegible sin 500); comando `restaurar-backup` (`Comando_ArgumentosIncorrectos_FallaSinTocarLaBase`, `Comando_RutaUncODeDispositivo_FallaSinRepetirLaRutaNiTocarLaBase`, `Restaurar_SinConfirmacion_Rechazado`, `Restaurar_ApiEnMarcha_Rechazado`, `Restaurar_CopiaCorrupta_Rechazada`, `Restaurar_CopiaDeVersionMasNueva_Rechazada`); harness (`revisarCarpetaDeBackup` con 10 rechazos)
- ✅ Lint / type checker: oxlint, `tsc --noEmit`, `test:lint`, `dotnet build` y `dotnet format` limpios
- ✅ Sin código muerto ni `using` sin uso en el código de producción del ticket (W-VER-01)
- ✅ Mitigaciones R-01 a R-10 del modelo de amenazas implementadas o aceptadas (R-06 por las dueñas, D-04; R-09 por el usuario, D-06)

## Warnings (no bloquean)
- ⚠️ W-VER-02 — ramas de Application del ticket en 89,5 %, apenas debajo del 90 % recomendado para lógica de negocio.
- ⚠️ W-VER-03 — `tests/Optica.Api.Tests/Backup/EstadoBackupApiTests.cs:118`: el caso "sin permiso" sale temprano en Windows y como root y figura como superado; lo compensa `GetEstado_SinPermisoDeLectura_AccesibleFalseSinRuta` (D-35).
- ⚠️ W-VER-03 — `web/src/paginas/SeccionBackup.test.tsx:301` y `:331`: esperas fijas de 20 ms antes de afirmar que algo no pasó.
- ⚠️ TDD — `e2e/tests/backup-aviso.spec.ts` sin rojo propio, mostrado con mutaciones revertidas (D-37).

## Notas
- La spec y el modelo de amenazas quedaron desfasados en nombres (`HacerBackupDelDia`, `AgregarAsync`, la regla de las
  20:00 en la Api); las decisiones D-30, D-33 y D-34 registran lo que vale.
- Verificación manual del usuario: restauración en menos de 4 horas en la PC del local (D-14), versión anterior de Chrome
  y Edge (D-02) y reglas de unidades en Windows real (D-31).
- Alerta de datos personales: la base completa (DNI, domicilio, teléfono y email de clientes, Ley 25.326) queda sin
  cifrar en las copias, en `anterior/` después de restaurar y en un `.parcial` que no se pudo borrar; riesgo R-06
  aceptado por las dueñas con mitigación documental, a revisar si se pierde un pendrive o se compromete la nube.
- Riesgos residuales para un ticket aparte (D-36, D-38): `trusted_schema = OFF` en `FabricaConexion`, prefijo `\??\UNC\`
  y letras de unidades de red en el comando, triggers o vistas de más en una copia armada a mano, corte entre apartar
  `-wal`/`-shm` y mover `optica.db`, campos JSON desconocidos en el POST.

Totales: 0 bloqueantes, 4 warnings, 18/18 criterios de aceptación y 8/8 bloques verificados.

Result: PASSED
