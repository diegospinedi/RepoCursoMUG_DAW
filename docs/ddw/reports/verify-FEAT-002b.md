# Verification FEAT-002b

Ronda 1, sobre el commit `ca3011d` de la rama `feat/FEAT-002b-busqueda-edicion-baja`. Verificación cruzada de
ddw-module-verifier (agente que no escribió el código), que volvió a correr, en orden y sin nada en paralelo,
`dotnet test Optica.sln` (1116/1116), `npm run test:coverage` en `web/` (433/433), `npm run test:unidad` (49/49) y
`npm run test:harness` (60/60, con `chrome-articulos-edicion` y `msedge-articulos-edicion` 7/7 cada uno) en `e2e/`,
`dotnet format --verify-no-changes`, `dotnet build --no-incremental` y `npm run build`.

| Field | Value |
|---|---|
| Module | catálogo de artículos: `src/Optica.Domain/Catalogo/`, `src/Optica.Application/Catalogo/`, `src/Optica.Application/Comun/IUnidadDeTrabajo.cs`, `src/Optica.Infrastructure/Persistencia/` (`ArticuloRepository`, `FuncionesSqlite`, `FabricaConexion`, `UnidadDeTrabajoEf`, `OpticaDbContext`, migración `CatalogoActivo`), `src/Optica.Api/Controllers/ArticulosController.cs`, `src/Optica.Api/Errores/RespuestasValidacion.cs`, `src/Optica.Api/Acceso/LimitesDelCuerpo.cs`, `web/src/paginas/Articulos.tsx`, `web/src/paginas/EditarArticulo.tsx`, `web/src/api/cliente.ts`, `web/src/catalogo/reglas.ts`, `e2e/tests/articulos-edicion.spec.ts` |
| Line coverage | 94.1% |
| Branch coverage | 87.4% |
| Function coverage | 91.7% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean; `dotnet build Optica.sln --no-incremental` — 0 warnings; `npm run build` en `web/` (tsc con noUnusedLocals y noUnusedParameters) — clean |

Cobertura de la tabla: código nuevo o modificado de .NET (20 archivos de `src/` sin migraciones), según el informe de
pruebas: líneas 1154/1227, ramas 346/396, funciones 199/217. La medición propia del verificador, contando cada línea
una vez, da 94.5% / 89.1% / 92.2%. Web, por separado: líneas 99.24%, ramas 96.86%, funciones 100%. Total del
proyecto .NET: 95.0% / 91.5% / 94.6%.

## Acceptance criteria
- ✅ AC-01 — `EditarArticulo.tsx` (`CampoPrecioVenta` de solo lectura) y `ArticulosController.Obtener`; pruebas `Get_Articulo_MuestraPrecioVentaYVersion`, `EditarArticulo_PrecioVentaSoloLectura`, e2e `al abrir un articulo el precio de venta es de solo lectura`
- ✅ AC-02 — `Articulo.Modificar`, `ModificarArticulo.EjecutarAsync`, `ArticuloRepository.Actualizar`; `Modificar_MargenDe50A60_Recalcula1936`, `Actualizar_GuardaDatosYIncrementaVersion`, `Modificar_Margen50A60_Graba1936`, `Put_Margen50A60_200Con1936`, `EditarArticulo_GrabarMargen_MuestraPrecioNuevo`, e2e `cambiar el margen de 50 a 60 graba 1.936,00`
- ✅ AC-03 — los mismos componentes; `Modificar_CostoDe1210A2420_Recalcula3630`, `Modificar_Costo1210A2420_Graba3630`, `Put_Costo1210A2420_200Con3630`, `EditarArticulo_GrabarCosto_MuestraPrecioNuevo`, e2e `cambiar el costo de 1.210 a 2.420 graba 3.630,00`
- ✅ AC-04 — `PlegadoBusqueda.Plegar`, funciones `plegar` y `orden_articulo`, `ArticuloRepository.BuscarAsync`, `BuscarArticulos`; `Plegar_Armazon_IgualQueArmazonSinTilde`, `BuscarAsync_Armazon_EncuentraConYSinTilde`, `Buscar_Armazon_PliegaElTexto`, `Get_Listado_Armazon_SinCostoNiMargen`, `Articulos_Buscar_MuestraCoincidencias`, e2e `buscar armazon lista los que contienen Armazón o armazon y ningún otro`
- ✅ AC-05 — `ArticuloRepository.CambiarEstado`, `CambiarEstadoArticulo`, `PUT /api/articulos/{codigo}/estado`, `Articulos.tsx`; `ConEstado_Desactivar_ConservaLosDatos`, `CambiarEstado_Desactivar_SoloCambiaActivoYVersion`, `CambiarEstado_Desactivar_ConservaLosDatos`, `PutEstado_Desactivar_ListadoInactivo`, `Articulos_Desactivar_MuestraInactivo`, e2e `desactivar muestra inactivo y conserva los datos`
- ✅ AC-06 — los mismos componentes; `ConEstado_Reactivar_QuedaActivo`, `CambiarEstado_Reactivar_QuedaActivo`, `PutEstado_Reactivar_ListadoActivo`, `Articulos_Reactivar_MuestraActivo`, e2e `reactivar muestra activo`

Ninguna de las pruebas citadas se limita al código de estado: verifican el cuerpo, lo grabado o lo que muestra la
pantalla. NFR-02 (2 decimales) ✅. NFR-01 ⚠️: Chrome y Edge estables probados (60/60); la versión inmediata anterior
de cada navegador sigue como verificación manual pendiente, como en FEAT-001 y FEAT-002a.

## Spec blocks
- ✅ Block 1 — dominio: `Articulo.Nuevo`, `Modificar` y `ConEstado`, `ResultadoModificacion`, `PlegadoBusqueda.Plegar` y `ClaveOrden`, `PaginaArticulos`, contrato de `IArticuloRepository`; 15/15 pruebas de la spec
- ✅ Block 2 — infraestructura: columnas `Activo` y `Version` con CHECK y tokens, funciones registradas, motivo del conflicto, migración `CatalogoActivo` con `Sql` (desvío de la letra documentado en D-23 y ADR-012), `HasPendingModelChanges` falso; 19/19
- ✅ Block 3 — aplicación: búsqueda, modificación y cambio de estado con la precedencia de D-17 y las relecturas de D-08, D-16 y D-26; 19/19
- ✅ Block 4 — Api: tres acciones nuevas, JSON obligatorio en PUT, `ConflictoArticulo` con `motivo`, piso de log; `RutasAnonimas` con 5 rutas; 17/17
- ✅ Block 5 — web: rutas, enlace en Inicio, cliente y las dos pantallas; 15/15
- ✅ Block 6 — e2e en Chrome y Edge y README; 7/7

## Tests
Pruebas que la spec nombra, todas presentes y en verde (92/92, F-VER-06):

- Block 1: `Modificar_InactivoRechazado_DevuelveResultadoInactivo`, `Modificar_DatosInvalidos_DevuelveTodasLasInfracciones`, `Modificar_PrecioFueraDeRango_DevuelveInfraccionDeRango`, `Constructor_VersionNegativaInvalida_Lanza`, `Modificar_MargenDe50A60_Recalcula1936`, `Modificar_CostoDe1210A2420_Recalcula3630`, `Modificar_SoloDescripcion_ConservaPrecioVenta`, `ConEstado_Desactivar_ConservaLosDatos`, `ConEstado_Reactivar_QuedaActivo`, `Nuevo_QuedaActivoConVersionCero`, `Plegar_Armazon_IgualQueArmazonSinTilde`, `Plegar_Enie_NoEquivaleAEne`, `Plegar_Utf16Invalido_NoLanza`, `Plegar_TildeGraveYDieresis_SeIgnoran`, `ClaveOrden_Enie_EntreNyO`
- Block 2: `AEntidad_ActivoInvalido_EsEstadoCorrupto`, `Actualizar_VersionCambiada_ConflictoDeConcurrencia`, `Actualizar_ClaveDuplicada_ConflictoDeUnicidad`, `CambiarEstado_CodigoInexistente_LanzaError`, `BuscarAsync_PaginaInvalida_Lanza`, `BuscarAsync_Armazon_EncuentraConYSinTilde`, `BuscarAsync_PorcentajeYGuionBajo_NoSonComodines`, `BuscarAsync_PorCodigoYCodigoProveedor_CoincidenciaParcial`, `BuscarAsync_OrdenPorDescripcionYPaginasDe50`, `CambiarEstado_Desactivar_SoloCambiaActivoYVersion`, `CambiarEstado_Reactivar_QuedaActivo`, `Actualizar_GuardaDatosYIncrementaVersion`, `Migracion_CatalogoActivo_DejaActivasLasFilasExistentes`, `Esquema_Articulo_ColumnasNotNullYChecks`, `Plegar_RegistradaEnCadaConexion`, `Esquema_NingunObjetoUsaPlegar`, `BuscarAsync_Enie_OrdenaEntreNyO`, `UnidadDeTrabajo_ClavePrimariaYCheck_MotivoUnicidad`, `BuscarAsync_DesplazamientoMayorQueInt_LanzaError`
- Block 3: `Buscar_TextoInvalido_DevuelveErrorEnTexto`, `Buscar_PaginaFueraDeRango_DevuelveErrorEnPagina`, `Modificar_Inexistente_NoEncontrado`, `Modificar_Inactivo_Rechazado`, `Modificar_DatosInvalidos_TodosLosErrores`, `Modificar_CodigoDuplicado_ErrorEnCodigoProveedor`, `Modificar_VersionFaltante_ErrorEnVersion`, `Modificar_ConflictoDeConcurrencia_Cambiado`, `CambiarEstado_ActivoFaltante_ErrorEnActivo`, `CambiarEstado_ConflictoDeConcurrencia_Cambiado`, `Modificar_VersionDistintaDeLaLeida_CambiadoSinEscribir`, `Modificar_VersionDistintaYDesactivado_InactivoRechazado`, `Buscar_SinTexto_PrimeraPaginaDe50`, `Buscar_Armazon_PliegaElTexto`, `Modificar_Margen50A60_Graba1936`, `Modificar_Costo1210A2420_Graba3630`, `CambiarEstado_Desactivar_ConservaLosDatos`, `CambiarEstado_Reactivar_QuedaActivo`, `CambiarEstado_MismoEstado_NoEscribe`
- Block 4: `Get_Listado_PaginaIlegible_400`, `Put_CuerpoIlegible_400`, `Put_Inexistente_404`, `Put_Inactivo_409`, `Put_Cambiado_409`, `Put_VersionFaltante_400`, `PutEstado_Cambiado_409`, `Put_SinJson_415`, `Put_DemasiadoGrande_413`, `RutasNuevas_SinSesion_401`, `Get_Articulo_MuestraPrecioVentaYVersion`, `Put_Margen50A60_200Con1936`, `Put_Costo1210A2420_200Con3630`, `Get_Listado_Armazon_SinCostoNiMargen`, `PutEstado_Desactivar_ListadoInactivo`, `PutEstado_Reactivar_ListadoActivo`, `Traductores_ResultadoNoPrevisto_Lanzan`
- Block 5: `EditarArticulo_CodigoInvalido_NoEncontrado`, `EditarArticulo_ErroresPorCampo_400`, `EditarArticulo_Conflicto409_MuestraMensaje`, `Articulos_RespuestaIlegible_MensajeGeneral`, `modificarArticulo_409ConMotivoDesconocido_RespuestaIlegible`, `modificarArticulo_EnviaJsonConPut`, `Articulos_SinSesion401_VuelveAlIngreso`, `EditarArticulo_PrecioVentaSoloLectura`, `EditarArticulo_GrabarMargen_MuestraPrecioNuevo`, `EditarArticulo_GrabarCosto_MuestraPrecioNuevo`, `Articulos_Buscar_MuestraCoincidencias`, `Articulos_Desactivar_MuestraInactivo`, `Articulos_Reactivar_MuestraActivo`, `EditarArticulo_Inactivo_SoloLecturaConReactivar`, `Articulos_Paginas_AnteriorYSiguiente`
- Block 6: `edicion con codigo duplicado muestra el error rechazado junto al campo`, `al abrir un articulo el precio de venta es de solo lectura`, `cambiar el margen de 50 a 60 graba 1.936,00`, `cambiar el costo de 1.210 a 2.420 graba 3.630,00`, `buscar armazon lista los que contienen Armazón o armazon y ningún otro`, `desactivar muestra inactivo y conserva los datos`, `reactivar muestra activo`

- ✅ Sad-path tests: `GET /api/articulos` (página ilegible o fuera de rango, texto de 201 caracteres, error de enlace fuera de `pagina`, 401), `PUT /api/articulos/{codigo}` (cuerpo ilegible, 404, 409 inactivo y cambiado, versión faltante, duplicado, 415, 413, 401), `PUT /api/articulos/{codigo}/estado` (sin `activo`, cuerpo ilegible, 404, 409, 415, 413, 401); en dominio, aplicación e infraestructura, las pruebas de error de la spec; en la web, errores por campo, 409 por motivo, motivo desconocido, respuesta ilegible, 401 y código de ruta inválido; en e2e, el duplicado.
- ✅ TDD: rojo previo registrado en los bloques 1 (15/229), 2 (33/377), 3 (20 pruebas) y 5 (92/417); en los bloques 4 y 6, evidencia por mutación (M01 y 23/23 mutantes muertos; 3/4 en el e2e y el cuarto muerto por la prueba de dominio).

## Warnings
- ⚠️ W-VER-01 — `Articulo.ConEstado` sin uso en producción (aceptado en D-27); los avisos IDE0060 de `PrintMembers` son falsos positivos (la firma del record exige el parámetro).
- ⚠️ W-VER-02 — ramas de la lógica de negocio entre 80% y 90% (aplicación 89.1%, infraestructura 88.2%); lo que falta son guardas defensivas (`BuscarArticulos.cs:43,53`, `ResultadoModificacion.cs:48`, `ModificarArticulo.cs:75,142,177`) y los métodos de cliente de `FuncionesSqlite` que lanzan fuera de una consulta.
- ⚠️ W-VER-03 — la prueba e2e de AC-04 ("ningún otro") depende de que ninguna otra prueba del proyecto grabe "armazón" (documentado en el archivo); los códigos `ED-…` son fijos y chocarían si se habilitaran reintentos sobre la misma base.
- ⚠️ NFR-01 — falta la verificación manual en la versión inmediata anterior de Chrome y de Edge.
- Observaciones: el resumen de la spec cita "D-01 a D-21" y las decisiones llegan a D-30 (no se corrige en VERIFY); la e2e de AC-01 no comprueba que la descripción sea editable.
- Datos sensibles: el listado no expone costo ni margen; los records de solicitud no imprimen sus datos y el piso de log está fijado en código (D-28); D-30 cortó el envío a Microsoft de datos de la identidad del empleado y de la organización desde el Edge del e2e. Riesgo residual aceptado: el texto buscado viaja en la query string (historial del navegador).

Result: PASSED
