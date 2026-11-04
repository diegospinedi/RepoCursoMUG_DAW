# Verification FEAT-002a

Ronda 1, sobre el commit `7896c7c` de la rama `feat/FEAT-002a-alta-precio-venta`. Verificación cruzada de
ddw-module-verifier (agente que no escribió el código), que volvió a correr `dotnet test Optica.sln` (955/955),
`npm run test:coverage` en `web/` (330/330), `npm run test:unidad` en `e2e/` (49/49), `dotnet format
--verify-no-changes` y `tsc -b`. El arnés e2e lo corrió el orquestador: 46/46 en Chrome y Edge (11 de artículos en
cada uno).

| Field | Value |
|---|---|
| Module | catálogo de artículos: `src/Optica.Domain/Catalogo/`, `src/Optica.Domain/Comun/ReglasImporte.cs`, `src/Optica.Application/Catalogo/`, `src/Optica.Infrastructure/Persistencia/ArticuloRepository.cs`, `src/Optica.Api/Controllers/ArticulosController.cs`, `web/src/paginas/NuevoArticulo.tsx`, `web/src/catalogo/` |
| Line coverage | 94.2% |
| Branch coverage | 90.6% |
| Function coverage | 96.5% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean; `dotnet build Optica.sln --no-incremental` — 0 warnings; `npm run build` en `web/` (tsc con noUnusedLocals y noUnusedParameters) — clean |

Cobertura de la tabla: código nuevo o modificado de .NET (17 archivos de `src/` sin migraciones): líneas 728/773,
ramas 213/235, funciones 110/114. Web, por separado: líneas 99.28%, ramas 97.26%, funciones 100%. Total del proyecto
(informe de pruebas): .NET 94.9% / 92.5% / 94.2%.

## Acceptance criteria
- ✅ AC-01 — `AltaArticulo.EjecutarAsync`, `ArticuloRepository.Agregar` (AUTOINCREMENT); pruebas `Alta_Valida_DevuelveCodigoYPrecio`, `Agregar_YConfirmar_AsignaCodigoAutonumerico`, `Post_Valido_201ConCodigoYPrecio`, `NuevoArticulo_Valido_MuestraCodigoYPrecio`, e2e `articulos: alta asigna código y precio`
- ✅ AC-02 — `NormalizacionCodigo.Clave`, índice UNIQUE, `ConflictoAlConfirmarException` como duplicado; `Clave_EspaciosYMayusculas_IgualQueABC1`, `Agregar_ClaveDuplicada_RechazadoConConflicto`, `Alta_ConflictoAlConfirmar_RechazadoComoDuplicado`, `Post_CodigoDuplicado_Rechazado400EnElCampo`, `NuevoArticulo_CodigoDuplicado_RechazadoJuntoAlCampo`, e2e `articulos: código duplicado rechazado junto al campo`
- ✅ AC-03 — `PoliticaArticulo` (faltantes); `Evaluar_CodigoFaltante_Rechazado`, `Alta_SinCodigoOSinDescripcion_RechazadoSinGrabar`, `Post_SinDescripcion_Rechazado400EnElCampo`, `NuevoArticulo_SinCodigoOSinDescripcion_RechazadoSinEnviar`, e2e `articulos: sin código o sin descripción rechazado`
- ✅ AC-04 — `CalculadoraPrecioVenta.Calcular`; `Calcular_RI_Costo1210Margen150_Venta3025`, `Alta_UsaLaConfiguracionVigente_SinCache`, e2e `articulos: costo 1210 y margen 150 da 3.025,00`
- ✅ AC-05 — margen, rango antes que escala; `Evaluar_Margen1000coma01_RechazadoPorRango`, `Alta_Margen1000coma01_RechazadoConRango`, `Post_Margen1000coma01_Rechazado400EnElCampo`, `NuevoArticulo_Margen1000coma01_RechazadoSinEnviar`, e2e `articulos: margen -1 y 1000,01 rechazado`
- ✅ AC-06 — redondeo hacia +∞ y advertencia; `Calcular_RI_CostoMenos100Margen50_VentaMenos150`, `Articulo_CostoNoPositivo_Advertencia`, `Alta_CostoNegativo_GrabaConAdvertencia`, `Post_CostoNegativo_201ConAdvertencia`, `NuevoArticulo_CostoNegativo_MuestraAdvertencia`, e2e `articulos: costo -100 graba -150,00 con advertencia`
- ✅ AC-07 — `Calcular` con desglose; `Calcular_RI_Costo1210Margen50_Desglose1000_1500_Venta1815`, `Agregar_PrecioVenta_GuardadoEnCentavos`, e2e `articulos: costo 1210 y margen 50 da 1.815,00`
- ✅ AC-08 — `Calcular_RI_Costo1000Margen50_Venta1500`, e2e `articulos: costo 1000 y margen 50 da 1.500,00`
- ✅ AC-09 — `Calcular_Monotributo_Costo1210Margen50_Venta1815`, `Alta_Monotributo_PrecioSinDescontarIva`, `Post_Monotributo_201Precio1815`, e2e `articulos: Monotributo da 1.815,00`
- ✅ AC-10 — `Calcular_Multiplo50_1815coma37_Redondea1850`, `Post_Multiplo50_201Precio1850`, e2e `articulos: múltiplo 50 redondea a 1.850,00`
- ✅ AC-11 — `Calcular_Multiplo0coma01_1666coma656_Redondea1666coma66`, e2e `articulos: 1.666,656 redondea a 1.666,66`

## Spec blocks
- ✅ Block 1 — dominio: 25 pruebas requeridas presentes y en verde
- ✅ Block 2 — infraestructura: 9 presentes; migración `Catalogo` con 3 CHECK e índice UNIQUE
- ✅ Block 3 — aplicación: 13 presentes; Catálogo no usa Application.Configuracion (D-10)
- ✅ Block 4 — Api: 15 presentes; protección y ARCA cubren la ruta con parámetro
- ✅ Block 5 — web: 14 presentes
- ✅ Block 6 — e2e: 11 presentes con el nombre exacto; evidencia TDD por la excepción D-23 del usuario

## Tests promised by the spec
Las pruebas que nombran los Required tests de la spec existen en el repositorio (comprobado con `git grep`) y pasan:
- ✅ Block 1: `Calcular_RI_Costo1210Margen50_Desglose1000_1500_Venta1815`, `Calcular_RI_Costo1000Margen50_Venta1500`, `Calcular_RI_Costo1210Margen150_Venta3025`, `Calcular_RI_CostoMenos100Margen50_VentaMenos150`, `Calcular_Monotributo_Costo1210Margen50_Venta1815`, `Calcular_Multiplo50_1815coma37_Redondea1850`, `Calcular_Multiplo0coma01_1666coma656_Redondea1666coma66`, `Calcular_CasosMedidosPorLaAuditoria_SinCentavoDeMas`, `Calcular_Negativo_RedondeaHaciaMasInfinito`, `Calcular_PrecioQueNoEntra_Rechazado`, `Evaluar_CodigoFaltante_Rechazado`, `Evaluar_DescripcionFaltante_Rechazado`, `Evaluar_Codigo51YDescripcion201_Rechazados`, `Evaluar_MargenMenos1_Rechazado`, `Evaluar_Margen1000coma01_RechazadoPorRango`, `Evaluar_MargenBordes0y1000_Validos`, `Evaluar_CostoConTresDecimales_Invalido`, `Evaluar_MargenConTresDecimales_Invalido`, `Evaluar_CostoCeroYNegativo_Validos`, `Clave_EspaciosYMayusculas_IgualQueABC1`, `Clave_EspacioDuroYTabulador_IgualQueABC1`, `Evaluar_SoloEspaciosDuros_RechazadoComoFaltante`, `Articulo_CostoNoPositivo_Advertencia`, `Articulo_ValorInvalido_LanzaSinIncluirElValor`, `ReglasImporte_MismasRespuestasQueAntes`
- ✅ Block 2: `Agregar_YConfirmar_AsignaCodigoAutonumerico`, `Agregar_PrecioVenta_GuardadoEnCentavos`, `Agregar_ClaveDuplicada_RechazadoConConflicto`, `ExisteClave_EncuentraSinDistinguirMayusculas`, `Insertar_MargenFueraDeRango_RechazadoPorCheck`, `Insertar_CodigoVacioODe51_RechazadoPorCheck`, `Obtener_FilaInvalida_LanzaErrorDeEstadoCorrupto`, `AgregarInfraestructura_RegistraRepositorioDeArticulos`, `Migracion_IndiceTipoResultado_CreadoYRevertible`, `Articulo`
- ✅ Block 3: `Alta_Valida_DevuelveCodigoYPrecio`, `Alta_CodigoDuplicadoConEspaciosYMinusculas_Rechazado`, `Alta_ConflictoAlConfirmar_RechazadoComoDuplicado`, `Alta_SinCodigoOSinDescripcion_RechazadoSinGrabar`, `Alta_Margen1000coma01_RechazadoConRango`, `Alta_CostoNegativo_GrabaConAdvertencia`, `Alta_UsaLaConfiguracionVigente_SinCache`, `Alta_Monotributo_PrecioSinDescontarIva`, `Alta_PrecioFueraDeRango_Rechazado`, `Alta_VariosCamposInvalidos_DevuelveTodosLosErrores`, `Alta_SinConfiguracion_PropagaError`, `Obtener_Inexistente_DevuelveNull`, `Traducir_CadaInfraccion_CampoYAccionFijos`
- ✅ Block 4: `Post_Valido_201ConCodigoYPrecio`, `Post_CodigoDuplicado_Rechazado400EnElCampo`, `Post_SinDescripcion_Rechazado400EnElCampo`, `Post_Margen1000coma01_Rechazado400EnElCampo`, `Post_CostoNegativo_201ConAdvertencia`, `Post_Monotributo_201Precio1815`, `Post_Multiplo50_201Precio1850`, `PostYGet_SinSesion_NoAutorizado401`, `Post_SinContentTypeJson_Rechazado415`, `Post_CuerpoGrande_Rechazado413`, `Post_CuerpoIlegible_Invalido400SinDetallesInternos`, `Get_Inexistente_Error404SinDetalles`, `ResponderAlta_ResultadoNoPrevisto_Error500`, `EndpointsProtegidos_SinSesion_NoAutorizado401`, `SinDatosArcaTests`
- ✅ Block 5: `mostrarImporte_MilesYDecimales`, `reglas_Margen1000coma01_Rechazado`, `reglas_CostoFueraDeRangoEnLosDosSentidos_Rechazado`, `NuevoArticulo_Valido_MuestraCodigoYPrecio`, `NuevoArticulo_PrecioDeVenta_SoloLectura`, `NuevoArticulo_CodigoDuplicado_RechazadoJuntoAlCampo`, `NuevoArticulo_SinCodigoOSinDescripcion_RechazadoSinEnviar`, `NuevoArticulo_Margen1000coma01_RechazadoSinEnviar`, `NuevoArticulo_CostoNegativo_MuestraAdvertencia`, `NuevoArticulo_NoNumero_Invalido`, `NuevoArticulo_SinSesion401_NavegaAlIngreso`, `NuevoArticulo_SinConexion_ErrorGeneralConservaValores`, `grabarArticulo_400_DevuelveTodosLosErrores`, `Inicio_EnlaceNuevoArticulo`
- ✅ Block 6: `articulos: alta asigna código y precio`, `articulos: código duplicado rechazado junto al campo`, `articulos: sin código o sin descripción rechazado`, `articulos: costo 1210 y margen 150 da 3.025,00`, `articulos: margen -1 y 1000,01 rechazado`, `articulos: costo -100 graba -150,00 con advertencia`, `articulos: costo 1210 y margen 50 da 1.815,00`, `articulos: costo 1000 y margen 50 da 1.500,00`, `articulos: Monotributo da 1.815,00`, `articulos: múltiplo 50 redondea a 1.850,00`, `articulos: 1.666,656 redondea a 1.666,66`
## Tests
- ✅ Sad-path tests: cada superficie de entrada tiene casos inválidos — `Evaluar_CodigoFaltante_Rechazado`, `Calcular_PrecioQueNoEntra_Rechazado`, `Insertar_MargenFueraDeRango_RechazadoPorCheck`, `Alta_PrecioFueraDeRango_Rechazado`, `PostYGet_SinSesion_NoAutorizado401`, `Post_SinContentTypeJson_Rechazado415`, `Post_CuerpoGrande_Rechazado413`, `Post_CuerpoIlegible_Invalido400SinDetallesInternos`, `Get_Inexistente_Error404SinDetalles`, `NuevoArticulo_NoNumero_Invalido`, `NuevoArticulo_SinSesion401_NavegaAlIngreso`
- ⚠️ Dead code (W-VER-01): `consultarArticulo` (cliente.ts) no lo usa código de producción todavía (lo pide la spec del Block 5; previsto para FEAT-002b); el `case 'conflicto'` de NuevoArticulo cubre el `switch` aunque la Api no responde 409
- ⚠️ Fragile tests (W-VER-03): `articulos.spec.ts` corre en serie y depende de lo grabado antes; `ArticulosApiTests` usa códigos esperados por AUTOINCREMENT (mismo patrón aceptado en FEAT-001)

## Threat model mitigations
- ✅ R-01 a R-07 presentes y probados (sesión también en la ruta con parámetro, 415, cálculo exacto de ADR-010 con regresiones, clave normalizada + UNIQUE + carrera, PrecioVentaFueraDeRango y borde de 1024 bytes, textos fijos sin datos de otros artículos, sin `dangerouslySetInnerHTML` y control de CSP en e2e)

## Warnings (no bloquean)
- W1 — W-VER-01, descrito arriba.
- W2 — W-VER-02: ramas de `CalculadoraPrecioVenta` al 88 % (los `catch` defensivos de OverflowException y UnreachableException); `ResultadoAlta.Invalido` con lista vacía y `ArticuloGrabado.Desde` sin código sin prueba directa.
- W3 — W-VER-03, descrito arriba.
- W4 — la spec quedó desactualizada en tres textos sin cambio de comportamiento: la línea Files del Block 5 dice "trim() de JavaScript" (D-17 la reemplaza), la Logic del Block 1 no menciona U+FEFF (D-17) y el Summary cita D-01 a D-12 (hay D-01 a D-23).
- W5 — por la identidad de ADR-010 el precio es el mismo en Responsable Inscripto y en Monotributo; AC-09 se distingue en el dominio por el desglose (D-09).
- W6 — el UTF-16 inválido (D-16) está probado en dominio, aplicación y web; en la Api System.Text.Json lo rechaza antes como cuerpo ilegible (400 sin detalles).
- W7 — D-22: limitación conocida de exactitud de centavos entre 2^46 e IMPORTE_MAXIMO_SEGURO en la web.
- NFR-01: la versión estable inmediata anterior de Chrome y de Edge se verifica a mano, como en FEAT-001.

Datos: el módulo no maneja datos personales; costos, márgenes y códigos de proveedor (información comercial confidencial) quedan detrás de la sesión, sin registro en el log ni en el navegador. Con códigos consecutivos, cualquiera con sesión puede recorrer el catálogo; se acepta con una sola operadora y sin roles (PRD) y conviene revisarlo si se agregan usuarios.

Result: PASSED
