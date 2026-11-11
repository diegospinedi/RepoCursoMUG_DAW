# Spec FEAT-003b: Cantidad, descuento y cálculos de las líneas y el total

| Field | Value |
|-------|-------|
| Ticket | FEAT-003b |
| PRD | docs/ddw/prd/prd-FEAT-003b.md |
| Tier | FEATURE |
| Date | 2026-11-11 |
| Spec loops | 5 |
| Loops since last human decision | 0 |

## Summary
La línea del presupuesto deja de grabarse con Cantidad 1 y Descuento 0 fijos (D-03 de FEAT-003a): la pantalla envía
por línea el código del artículo, la Cantidad (entero de 1 a 9.999, D-01) y el Porcentaje de Descuento (0 a 100 con
hasta 2 decimales, D-02). El dominio calcula el Precio con Descuento, el Precio Final y el total en centésimos con
redondeo mitad hacia arriba (D-04), y la Api devuelve además `total` (D-05). La web aplica las mismas fórmulas con
BigInt para recalcular en pantalla al cambiar la cantidad o el descuento (FR-07), muestra un mensaje junto a cada campo
(D-10) y el total con IVA incluido. No hay migración: las columnas y sus CHECK ya existen desde FEAT-003a.
**Rollback:** como no cambia el esquema, revertir el merge alcanza; las líneas grabadas con cantidad y descuento siguen
siendo válidas para el código de FEAT-003a, que las lee sin recalcular.

**Textos.** El PRD fija "La cantidad debe ser un número entero mayor a 0" (AC-02) y "El descuento debe estar entre 0 y
100" (AC-03), que se usan tal cual. "Usá hasta 9.999 unidades." lo eligió el usuario y lo fija AC-09 (D-01); "Ingresá hasta 2
decimales." es el texto ya existente del catálogo; "El precio final de la línea es demasiado alto." y "El total del
presupuesto es demasiado alto." son del límite técnico de D-04. **Datos personales:** esta parte no agrega datos
personales; los records que llevan el cliente conservan `PrintMembers => false` (D-17 de FEAT-003a).
Decisiones: `docs/ddw/specs/decisions-FEAT-003b.md` (D-01 a D-10). Amenazas: `docs/ddw/security/threat-FEAT-003b.md`.

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 2, Block 3, Block 4, Block 5 |
| FR-02 | Block 1, Block 2, Block 3, Block 4, Block 5 |
| FR-03 | Block 1, Block 2, Block 3, Block 4, Block 5 |
| FR-04 | Block 1, Block 3, Block 4 |
| FR-05 | Block 1, Block 3, Block 4 |
| FR-06 | Block 1, Block 2, Block 3, Block 4, Block 5 |
| FR-07 | Block 3, Block 4, Block 5 |
| FR-08 | Block 4, Block 5 |
| NFR-01 | Strategy: Block 5 corre AC-01, AC-02, AC-03, AC-07 y AC-08 con Playwright en los proyectos Chrome y Edge que ya tiene `e2e/playwright.config.ts`; la CI de FEAT-005 los corre en cada PR (versión estable vigente). La inmediata anterior queda para FEAT-003d (verificación global en los navegadores). |
| NFR-02 | Strategy: importes en centésimos enteros (ADR-008), redondeo mitad hacia arriba en cada paso (Block 1 y Block 3) y `mostrarImporte` con 2 decimales en pantalla (Block 4) |

## Dependencies between blocks
Block 1 → Block 2 → Block 3 → Block 4 → Block 5. Block 2 usa el cálculo y la firma nueva de `DesdeArticulo` de Block 1;
Block 3 usa el contrato de la Api de Block 2; Block 4 usa las reglas y el cliente de Block 3; Block 5 recorre todo.

## Block 1 — Dominio: cálculo de la línea, política de cantidad y descuento, total

**Files**
- `src/Optica.Domain/Presupuestos/CalculoLinea.cs` (new) — `PrecioConDescuento(precioUnitario, porcentajeDescuento)`,
  `PrecioFinal(precioConDescuento, cantidad)`; constantes `CantidadMinima` = 1 y `CantidadMaxima` = 9.999 (D-01). Los
  límites de importe del dominio siguen siendo los del tipo (`ReglasImporte.ImporteMaximo`); el tope del contrato con la
  web vive en Block 2 (ADR-016).
- `src/Optica.Domain/Presupuestos/LineaPresupuesto.cs` (modified) — `DesdeArticulo(articulo, cantidad,
  porcentajeDescuento)` calcula los dos precios; el constructor valida cantidad 1..9.999; nuevo rechazo
  `ResultadoLinea.ImporteExcedido` cuando el Precio Final supera `ReglasImporte.ImporteMaximo`.
- `src/Optica.Domain/Presupuestos/Presupuesto.cs` (modified) — propiedad `Total` (suma de los Precios Finales); el
  constructor rechaza un total mayor que `ReglasImporte.ImporteMaximo` (invariante del dominio).
- `src/Optica.Application/Presupuestos/AltaPresupuesto.cs` (modified) — solo la llamada a `DesdeArticulo` (:199) pasa
  Cantidad 1 y Descuento 0, para que la solución compile; los valores reales llegan en Block 2.
- `tests/Optica.Api.Tests/Presupuestos/PresupuestosApiTests.cs` (modified) — solo la llamada de :566, con 1 y 0.
- `tests/Optica.Infrastructure.Tests/Persistencia/PresupuestoRepositoryTests.cs` (modified) — solo la llamada de :638,
  con 1 y 0.
- `tests/Optica.Domain.Tests/Presupuestos/CalculoLineaTests.cs` (new) — la tabla de casos común con
  `reglas.test.ts` (ADR-016).
- `tests/Optica.Domain.Tests/Presupuestos/LineaPresupuestoTests.cs` (modified) — las 12 llamadas a `DesdeArticulo` con
  la firma nueva.
- `tests/Optica.Domain.Tests/Presupuestos/PresupuestoTests.cs` (modified) — `Total`.

**Logic**
Implements FR-01 a FR-06 en el dominio. `CalculoLinea.PrecioConDescuento` pasa el Precio Unitario y el descuento a
centésimos enteros, calcula PU × (10000 − D) / 10000 en `decimal` y redondea a 2 decimales con
`MidpointRounding.AwayFromZero` (los importes no son negativos, así que es mitad hacia arriba). `PrecioFinal` multiplica
el Precio con Descuento ya redondeado por la Cantidad (FR-05). `DesdeArticulo` conserva los rechazos de FEAT-003a
(inactivo, precio negativo) y devuelve `ImporteExcedido` si el Precio Final supera `ReglasImporte.ImporteMaximo`.
`Presupuesto.Total` suma los Precios Finales ya redondeados (FR-06); el dominio hace cumplir los dos límites de tipo
(línea y total) sea cual sea la interfaz que lo use. Ejemplos de los AC: PU 1.000,00 × 3 con 10 % →
900,00 y 2.700,00 (AC-01); 100 % → 0,00 (AC-04); 0 % → 1.000,00 (AC-05); PU 6,67 × 3 con 50 % → 3,34 y 10,02 (AC-06);
10,02 + 2.700,00 = 2.710,02 (AC-07).

**Data model**
- Reuses `LineaPresupuesto` from FEAT-003a (Block 2 de esa spec) — no schema change; constraints as declared there:
  `Cantidad` NOT NULL con CHECK `>= 1`, `PorcentajeDescuento` en centésimos NOT NULL con CHECK `BETWEEN 0 AND 10000`,
  `PrecioConDescuento` y `PrecioFinal` en centésimos NOT NULL con CHECK `>= 0`, FK a `Presupuesto` `ON DELETE RESTRICT`.
  El tope de 9.999 vive solo en el dominio: un CHECK nuevo obligaría a reconstruir la tabla, y la reconstrucción
  borraría los triggers de inmutabilidad (ADR-012).

**Input validation**
- cantidad: entero, 1 a 9.999 (D-01).
- porcentajeDescuento: decimal, 0 a 100, hasta 2 decimales (D-02).
- Precio Final y total: hasta `ReglasImporte.ImporteMaximo` (límite del tipo).

**Error handling**
- Cantidad fuera de 1..9.999 en el constructor — `ArgumentException` con texto fijo que nombra el campo, sin el valor.
- Descuento fuera de 0..100 o con más de 2 decimales — `ArgumentException` con texto fijo (ya existente).
- Precio Final mayor que el límite del tipo — `DesdeArticulo` devuelve `ResultadoLinea.ImporteExcedido`, sin línea.
- Total mayor que el límite del tipo — el constructor de `Presupuesto` lanza `ArgumentException` con texto fijo.

**Required tests**
- [ ] `PrecioConDescuento_Ejemplos_AC01_AC04_AC05_AC06` — validates AC-01, AC-04, AC-05, AC-06
- [ ] `PrecioFinal_RedondeaDespuesDelDescuento` — validates AC-06 (3,34 × 3 = 10,02, no 10,01)
- [ ] `PrecioConDescuento_MitadHaciaArriba` — x,xx5 sube (NFR-02)
- [ ] `Total_SumaPreciosFinales` — validates AC-07
- [ ] `DesdeArticulo_CantidadYDescuento_CalculaPrecios` — validates AC-01
- [ ] `Constructor_CantidadInvalida_Rechazada` — 0 y 10.000 rechazados, validates AC-09 (sad path)
- [ ] `Constructor_DescuentoInvalido_Rechazado` — -0,01, 100,01 y 10,555 rechazados (sad path)
- [ ] `DesdeArticulo_ImporteExcedido_Rechazado` — Precio Final por encima del límite del tipo (sad path)
- [ ] `Constructor_TotalExcedido_Rechazado` — total por encima del límite del tipo (sad path)
- [ ] `Excepciones_NoRepitenElValor` — los mensajes no incluyen el valor recibido

**Completion criterion**
`dotnet build Optica.sln` compila y `dotnet test Optica.sln` pasa, con las pruebas nuevas, las 12 llamadas del dominio y
las tres llamadas de otros proyectos actualizadas con Cantidad 1 y Descuento 0.

## Block 2 — Aplicación y Api: cantidad y descuento en el alta, errores por campo y total

**Files**
- `src/Optica.Application/Presupuestos/AltaPresupuesto.cs` (modified) — `DatosLinea(long? CodigoArticulo, int?
  Cantidad, decimal? PorcentajeDescuento)` con `PrintMembers => false`; `DatosPresupuesto.Lineas` pasa a
  `IReadOnlyList<DatosLinea?>?`; `GrabarEnTransaccionAsync` recibe las líneas; rechazo de `ImporteExcedido` y de los
  topes del contrato; `PresupuestoGrabado.Total` es una propiedad `init`, fuera del constructor posicional, que
  `PresupuestoGrabado.Desde` copia de `presupuesto.Total` del dominio (D-05): Application no vuelve a sumar.
- `src/Optica.Application/Presupuestos/ValidacionPresupuesto.cs` (modified) — `CampoCantidad(i)` y `CampoDescuento(i)`
  hermanas de `CampoLinea`; `ImporteMaximoContrato` = 90.071.992.547.409,91 (`Number.MAX_SAFE_INTEGER / 100`, límite
  del contrato con la web, como `CodigoArticuloMaximo`; ADR-016) y `ControlarTopes(lineas)`, que evalúa en un solo
  lugar el Precio Final de cada línea y el total; textos `AccionCantidadInvalida`, `AccionCantidadMaxima`,
  `AccionDescuentoFueraDeRango`, `AccionImporteLineaExcedido`, `AccionTotalExcedido`; la escala usa
  `AccionesNumericas.HastaDosDecimales`.
- `src/Optica.Application/Presupuestos/ObtenerPresupuesto.cs` (modified) — devuelve `PresupuestoGrabado` con `Total`.
- `src/Optica.Api/Controllers/PresupuestosController.cs` (modified) — `SolicitudLinea(long? CodigoArticulo, int?
  Cantidad, decimal? PorcentajeDescuento)`; se borra el comentario "si llegan se ignoran"; `CampoDeLaClave` (D-09);
  `CuerpoIlegiblePresupuesto` responde con el texto del campo de la línea (AC-02 para la cantidad, AC-03 para el
  descuento) en lugar de `AccionCuerpoIlegibleConfiguracion` cuando la clave es de esos campos (D-07).
- `src/Optica.Infrastructure/Persistencia/PresupuestoRepository.cs` (modified solo si la prueba de lectura lo pide) —
  `ALinea` reconstruye cantidades y descuentos distintos de 1 y 0.
- `tests/Optica.Application.Tests/Presupuestos/AltaPresupuestoTests.cs` (modified) — líneas como `DatosLinea` (:66,
  :138, :139, :164, :174) y casos nuevos.
- `tests/Optica.Application.Tests/Presupuestos/ValidacionPresupuestoTests.cs` (modified)
- `tests/Optica.Api.Tests/Presupuestos/PresupuestosApiTests.cs` (modified) — `CamposRespuesta` con `total` (:26); se
  invierte el test de :408-419; `DesdeArticulo` (:566); valores 1 y 0 (:621-622).
- `tests/Optica.Api.Tests/Presupuestos/CierrePorDefectoPresupuestosTests.cs` (modified) — `SolicitudLinea` con tres
  argumentos (:89-90) y casos nuevos de `CampoDeLaClave` (:52-70).
- `tests/Optica.Infrastructure.Tests/Persistencia/PresupuestoRepositoryTests.cs` (modified) — `DesdeArticulo` (:638) y
  lectura de cantidad 3 con 10 %.

**Logic**
Implements FR-01 a FR-03 y FR-06 en el servidor. `ValidacionPresupuesto.Validar` suma, por línea, la cantidad (ausente,
menor que 1 → `AccionCantidadInvalida`; mayor que 9.999 → `AccionCantidadMaxima`) y el descuento (ausente o fuera de
0..100 → `AccionDescuentoFueraDeRango`; más de 2 decimales → `HastaDosDecimales`), sin leer la base. El alta llama
`DesdeArticulo(articulo, cantidad, descuento)` dentro de la transacción; `ControlarTopes` traduce un Precio Final por
encima de `ImporteMaximoContrato` (o un `ImporteExcedido` del dominio) en `AccionImporteLineaExcedido` sobre
`lineas[i].cantidad`, y un total por encima en `AccionTotalExcedido` sobre `lineas`; en los dos casos no graba nada. El pedido nunca fija precios: `SolicitudLinea` no tiene esos campos (R-01).

**API contract**
- Method + path: `POST /api/presupuestos` (alta) y `GET /api/presupuestos/{numero}` (consulta).
- Request: `{ cliente: {…seis campos de FEAT-003a…}, lineas: [{ codigoArticulo: number, cantidad: integer,
  porcentajeDescuento: number }] }`.
- Response: 201 / 200 con `{ numero, fecha, estado, cliente, lineas: [{ codigoArticulo, descripcion, precioUnitario,
  cantidad, porcentajeDescuento, precioConDescuento, precioFinal }], total }`.
- Error codes: 400 (`errors` por campo: `lineas[i].cantidad`, `lineas[i].porcentajeDescuento`, `lineas[i]`,
  `lineas`), 401, 404 (consulta), 413, 415.
- Auth: cookie de sesión de FEAT-001a, por la política por defecto de la Api.

**Data model**
- Reuses `LineaPresupuesto` from FEAT-003a — no schema change; constraints as declared there: `Cantidad` NOT NULL con
  CHECK `>= 1`, `PorcentajeDescuento` en centésimos NOT NULL con CHECK `BETWEEN 0 AND 10000`, importes en centésimos
  NOT NULL con CHECK `>= 0`, FK a `Presupuesto` `ON DELETE RESTRICT`. `total` no se guarda (D-05).

**Input validation**
- cantidad: entero JSON obligatorio, 1 a 9.999; `JsonNumberHandling.Strict` (un texto o 2.5 no se enlazan).
- porcentajeDescuento: número JSON obligatorio, 0 a 100, hasta 2 decimales.
- codigoArticulo, cliente, 100 líneas y 16 KB: sin cambios respecto de FEAT-003a.

**Error handling**
- Cantidad inválida o faltante — 400 en `lineas[i].cantidad` con "La cantidad debe ser un número entero mayor a 0".
- Cantidad mayor que 9.999 — 400 en `lineas[i].cantidad` con "Usá hasta 9.999 unidades.".
- Descuento fuera de rango o faltante — 400 en `lineas[i].porcentajeDescuento` con "El descuento debe estar entre 0 y
  100".
- Descuento con más de 2 decimales — 400 en `lineas[i].porcentajeDescuento` con "Ingresá hasta 2 decimales.".
- Precio Final o total por encima del tope — 400 en `lineas[i].cantidad` o `lineas` con los textos de D-04; nada grabado.
- Tipo JSON inválido en cantidad o descuento — 400 con el campo de la línea (D-09) y el texto de AC-02 o AC-03 (D-07),
  sin el valor ni el mensaje del serializador.

**Required tests**
- [ ] `Alta_CantidadYDescuento_GrabaPreciosCalculados` — validates AC-01
- [ ] `Alta_DevuelveTotal` — validates AC-07
- [ ] `Post_CantidadInvalida_400` — 0, -1 y ausente rechazados en `lineas[i].cantidad` (AC-02, sad path)
- [ ] `Post_CantidadDecimal_400` — 2.5 inválido por tipo JSON en `lineas[i].cantidad` con el texto de AC-02 (sad path)
- [ ] `Post_DescuentoTexto_400` — `"10"` inválido por tipo JSON en `lineas[i].porcentajeDescuento` con el texto de
  AC-03 (sad path)
- [ ] `Post_CantidadMayorAlMaximo_400` — 10000 rechazado con "Usá hasta 9.999 unidades.", validates AC-09 (sad path)
- [ ] `Post_DescuentoFueraDeRango_400` — -1, 100.01 y ausente rechazados (AC-03, sad path)
- [ ] `Post_DescuentoConTresDecimales_400` — 10.555 inválido (sad path)
- [ ] `Alta_ImporteExcedido_Rechazado` — Precio Final y total por encima de `ImporteMaximoContrato`, nada grabado
  (sad path)
- [ ] `PresupuestoGrabado_TotalCopiadoDelDominio` — `Total` es el del dominio, no una suma propia
- [ ] `Post_PreciosEnElPedido_SeIgnoran` — el pedido envía `precioFinal` y el servidor graba el calculado (R-01)
- [ ] `Post_Errores400_NoRepitenElValor` — el cuerpo del 400 no contiene el valor enviado (error, R-04)
- [ ] `CampoDeLaClave_CamposDeLinea` — `$.lineas[1].cantidad` → `lineas[1].cantidad`; índice 100 → `lineas`
- [ ] `Get_DevuelveTotal` — la consulta trae `total`
- [ ] `Repositorio_LeeCantidadYDescuento` — cantidad 3 y 10 % ida y vuelta

**Completion criterion**
`dotnet test Optica.sln` pasa, con las pruebas nuevas y las existentes actualizadas.

## Block 3 — Web: reglas de cantidad y descuento, cálculo y cliente de la Api

**Files**
- `web/src/presupuestos/reglas.ts` (modified) — `MENSAJE_CANTIDAD_INVALIDA`, `MENSAJE_CANTIDAD_MAXIMA`,
  `MENSAJE_DESCUENTO_FUERA_DE_RANGO`, `MENSAJE_IMPORTE_LINEA_EXCEDIDO`, `MENSAJE_TOTAL_EXCEDIDO`, `CANTIDAD_MAXIMA`;
  `validarCantidad(texto)`, `validarDescuento(texto)` (con `leerDecimal` de `configuracion/numeros.ts` y
  `MENSAJE_HASTA_DOS_DECIMALES` de `configuracion/reglas.ts`), `calcularLinea(precioUnitario, cantidad, descuento)` y
  `calcularTotal(preciosFinales)` en BigInt de centésimos.
- `web/src/configuracion/numeros.ts` (modified) — exporta `centavosDe` (hoy privada, :52-66) y suma
  `sumarCentesimos(importes)` en BigInt, módulo neutro que usan `presupuestos/reglas.ts` y `api/cliente.ts` sin ciclo
  de importaciones (ADR-016).
- `web/src/configuracion/numeros.test.ts` (modified)
- `web/src/presupuestos/reglas.test.ts` (modified) — la tabla de casos común con `CalculoLineaTests` (ADR-016).
- `web/src/api/cliente.ts` (modified) — `grabarPresupuesto(cliente, lineas)` envía `{ codigoArticulo, cantidad,
  porcentajeDescuento }`; `PresupuestoGrabado.total`; `leerPresupuesto` exige `total` igual a la suma calculada con
  `sumarCentesimos`, nunca con suma de flotantes (D-08); `CAMPO_LINEAS` y los campos `lineas[i].cantidad` /
  `lineas[i].porcentajeDescuento`.
- `web/src/api/cliente.test.ts` (modified)
- `web/src/App.test.tsx` (modified) — el fixture de :295-315 con `total`.

**Logic**
Implements FR-02 a FR-07 en el navegador. `validarCantidad` acepta un entero escrito con dígitos ASCII, sin separador
o con punto de miles bien formado (grupos de 3 dígitos después del primero: "1.000" es 1000, "10.000" es diez mil;
D-29, decisión del usuario), y lo valida de 1 a 9.999; un entero de cualquier largo mayor a 9.999 da
`MENSAJE_CANTIDAD_MAXIMA`. Un punto mal agrupado ("1.00", "10.0000"), la coma, el signo o el exponente dan
`MENSAJE_CANTIDAD_INVALIDA`. El servidor no cambia: recibe la cantidad como número JSON. `validarDescuento` lee con `leerDecimal` (coma o punto), primero el rango y después la escala (D-02).
`calcularLinea` convierte el precio a centésimos con `centavosDe` (no `Math.round` de flotantes), aplica las fórmulas
de D-04 con BigInt y devuelve `null` si el Precio Final supera `IMPORTE_MAXIMO_SEGURO`; `calcularTotal` suma con
`sumarCentesimos` y devuelve `null` si el total lo supera. Por encima de 2^46 pesos el recálculo en pantalla puede
diferir un centavo del servidor (D-22 de FEAT-002a, aceptado en ADR-016; umbral corregido y limitación aceptada por el
usuario en D-23).

**API contract**
- Method + path: consume `POST /api/presupuestos` y `GET /api/presupuestos/{numero}` de Block 2.
- Request: `lineas[i] = { codigoArticulo, cantidad, porcentajeDescuento }`.
- Response: el presupuesto con `total`.
- Error codes: 400 con los campos de D-06; 401; 404.
- Auth: la cookie de sesión, como el resto del cliente.

**Data model**
- Reuses `LineaPresupuesto` from FEAT-003a — no schema change; constraints as declared there: `Cantidad` NOT NULL con
  CHECK `>= 1`, `PorcentajeDescuento` en centésimos NOT NULL con CHECK `BETWEEN 0 AND 10000`, importes en centésimos
  NOT NULL con CHECK `>= 0`. La web solo lee y envía; `total` no se guarda (D-05).

**Input validation**
- Cantidad: entero de dígitos, sin separador o con punto de miles bien formado (D-29), de 1 a 9.999.
- Descuento: decimal con coma o punto, 0 a 100, hasta 2 decimales.

**Error handling**
- Cantidad inválida o vacía — `MENSAJE_CANTIDAD_INVALIDA`.
- Cantidad mayor que 9.999 — `MENSAJE_CANTIDAD_MAXIMA`.
- Descuento fuera de rango o vacío — `MENSAJE_DESCUENTO_FUERA_DE_RANGO`.
- Descuento con más de 2 decimales — `MENSAJE_HASTA_DOS_DECIMALES`.
- Respuesta sin `total` o con un total que no es la suma — ilegible, mensaje general.

**Required tests**
- [ ] `calcularLinea_ejemplos de AC-01, AC-04, AC-05 y AC-06` — validates AC-01, AC-04, AC-05, AC-06
- [ ] `calcularTotal_suma de AC-07` — validates AC-07
- [ ] `validarCantidad_0_-1_2,5_vacía: inválida` — validates AC-02 (sad path)
- [ ] `validarCantidad_10000: rechazada` — validates AC-09 (sad path)
- [ ] `validarCantidad_PuntoDeMiles` — "1.000" vale 1000, "9.999" vale 9999, "10.000" y "1.000.000" dan
  `MENSAJE_CANTIDAD_MAXIMA`, "1.00" y "10.0000" dan `MENSAJE_CANTIDAD_INVALIDA`; validates AC-09 y D-29
- [ ] `validarDescuento_-1_100,01_vacío: inválido` — validates AC-03 (sad path)
- [ ] `validarDescuento_10,555: inválido por la escala` (sad path)
- [ ] `calcularLinea_importe excedido: rechazado` (sad path)
- [ ] `leerPresupuesto_total faltante o distinto: error de lectura` (sad path)
- [ ] `leerPresupuesto_0,1 + 0,2 = 0,3 se lee bien` — la suma en centésimos no tiene error de coma flotante
- [ ] `sumarCentesimos_y_centavosDe` — exportadas y exactas
- [ ] `grabarPresupuesto_envía cantidad y descuento`

**Completion criterion**
`npm run test:coverage` y `npm run lint` de `web/` pasan con las pruebas nuevas.

## Block 4 — Web: pantallas de alta y de presupuesto grabado

**Files**
- `web/src/paginas/NuevoPresupuesto.tsx` (modified) — por fila, campos Cantidad y Porcentaje de Descuento (%) con su
  error junto al campo (`aria-describedby`), Precio con Descuento y Precio Final recalculados al escribir, fila
  "Total (IVA incluido)" en `tfoot`; `indiceDeLinea` reconoce `lineas[i].campo` y guarda un mensaje por campo (D-10).
- `web/src/paginas/NuevoPresupuesto.test.tsx` (modified)
- `web/src/paginas/Presupuesto.tsx` (modified) — fila "Total (IVA incluido)" en `tfoot` con `total`.
- `web/src/paginas/Presupuesto.test.tsx` (modified)

**Logic**
Implements FR-01, FR-07 y FR-08 en pantalla. Una línea nueva empieza con Cantidad 1 y Descuento 0; al cambiar
cualquiera de los dos, la fila y el total se recalculan con `calcularLinea` / `calcularTotal` (FR-07); un valor inválido
muestra su mensaje y la fila muestra "—" en los precios hasta que se corrija. Al grabar, valida todos los campos antes
de enviar; los errores del servidor se muestran junto al campo que nombran. Los importes se muestran con
`mostrarImporte`, sin columna de IVA (FR-08).

**Data model**
- Reuses `LineaPresupuesto` from FEAT-003a — no schema change; constraints as declared there: `Cantidad` NOT NULL con
  CHECK `>= 1`, `PorcentajeDescuento` en centésimos NOT NULL con CHECK `BETWEEN 0 AND 10000`, importes en centésimos
  NOT NULL con CHECK `>= 0`. La pantalla solo lee `total`, que no se guarda (D-05).

**Input validation**
- Cantidad: `inputMode="numeric"`, validación de Block 3.
- Descuento: `inputMode="decimal"`, validación de Block 3.

**Error handling**
- Cantidad inválida — mensaje de AC-02 junto al campo; no se envía.
- Descuento inválido — mensaje de AC-03 junto al campo; no se envía.
- Error 400 del servidor en `lineas[i].cantidad` o `lineas[i].porcentajeDescuento` — junto al campo de esa fila.

**Required tests**
- [ ] `cambiar cantidad y descuento recalcula la fila` — validates AC-01
- [ ] `cantidad inválida muestra el mensaje junto al campo` — validates AC-02 (sad path)
- [ ] `cantidad 10.000 rechazada con "Usá hasta 9.999 unidades."` — validates AC-09 (sad path)
- [ ] `descuento inválido muestra el mensaje junto al campo` — validates AC-03 (sad path)
- [ ] `descuento 100 da 0,00` — validates AC-04
- [ ] `descuento 0 muestra el precio unitario` — validates AC-05
- [ ] `total con dos líneas` — validates AC-07
- [ ] `error 400 del servidor en la cantidad va junto al campo` (error, sad path)
- [ ] `presupuesto grabado muestra el total sin discriminar IVA` — validates AC-08

**Completion criterion**
`npm run test:coverage`, `npm run lint` y `npm run build` de `web/` pasan.

## Block 5 — Punta a punta en Chrome y Edge

**Files**
- `e2e/tests/presupuestos-alta.spec.ts` (modified) — `grabarPorApi` (:236-239) envía cantidad y descuento; tipo
  `PresupuestoGrabado` (:115-129) con `total`; `celdasLinea1850` (:376-384) lee los inputs en la pantalla de alta; las
  cuentas de filas siguen en `tbody` (el total va en `tfoot`).
- `e2e/tests/presupuestos-calculos.spec.ts` (new) — AC-01, AC-02, AC-03, AC-07 y AC-08.

**Logic**
Implements NFR-01 para esta parte: el recorrido en Chrome y Edge carga cantidad y descuento, ve los precios y el total
recalculados, ve los mensajes de AC-02 y AC-03, graba y ve el total en el presupuesto grabado.

**Input validation**
- Los mismos campos de Block 4, cargados desde el navegador.

**Error handling**
- Cantidad inválida — el e2e verifica el mensaje y que no se grabó nada.
- Descuento inválido — el e2e verifica el mensaje y que no se grabó nada.

**Required tests**
- [ ] `AC-01: cantidad 3 y 10 % dan 900,00 y 2.700,00` — validates AC-01
- [ ] `AC-02: cantidad 0, -1 y 2,5 rechazada junto al campo` — validates AC-02 (sad path)
- [ ] `AC-09: cantidad 10.000 rechazada junto al campo` — validates AC-09 (sad path); la operadora escribe "10.000"
  con punto de miles, como dice el AC (D-29)
- [ ] `AC-03: descuento -1 y 100,01 rechazado junto al campo` — validates AC-03 (sad path)
- [ ] `AC-07: total de dos líneas 2.710,02` — validates AC-07
- [ ] `AC-08: el presupuesto grabado muestra el total con IVA incluido` — validates AC-08

**Completion criterion**
`npm run test:harness` de `e2e/` pasa en Chrome y Edge, incluidos los tests existentes de `presupuestos-alta.spec.ts`.

## Final verification
`validate_spec.py --tier FEATURE` pasa; `dotnet test Optica.sln`, `npm run test:coverage`, `npm run lint`,
`npm run build` y `npm run test:unidad && npm run test:harness` pasan; los 8 AC del PRD tienen al menos una prueba que
los valida, y la CI del PR queda en verde.
