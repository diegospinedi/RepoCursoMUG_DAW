# Decisiones FEAT-011 — Actualización de costos por planilla Excel

Registro de las decisiones tomadas durante el ticket. Las del usuario dicen "usuario"; las técnicas tomadas por el
agente bajo la delegación del usuario ("la recomendada y la que menos tiempo insuma") dicen "agente".

- **D-01 (usuario, 2026-11-17):** un solo ticket con los 13 AC; W-PRD-06 aceptado.
- **D-02 (usuario, 2026-11-17):** NFR-02 en la versión estable vigente de Chrome y Edge; la anterior, verificación
  manual.
- **D-03 (usuario, 2026-11-17):** un precio con más de dos decimales se redondea al centavo con la mitad hacia arriba
  (`MidpointRounding.AwayFromZero`) y la fila se aplica: 1234,565 → 1234,57.
- **D-04 (usuario, 2026-11-17):** una celda de precio con texto es "precio inválido" aunque parezca un número.
- **D-05 (agente):** lectura con `DocumentFormat.OpenXml` 3.1.1, primera hoja por orden de `workbook.xml`, SAX
  (ADR-031).
- **D-06 (agente):** el archivo viaja como JSON `{ "planilla": "<base64>" }` y no como multipart: conserva JSON-only
  (R-04 de FEAT-001a, R-02 de FEAT-001c), sin excepción en `LimitesDelCuerpo` ni temporales en disco (ADR-031).
- **D-07 (agente):** el formato se decide por el contenido. El lector decide solo "no es .xlsx" y los límites técnicos;
  `PlanillaCostos` (dominio) decide encabezados, columnas de más, precio inválido, repetidos e inexistentes.
- **D-08 (agente):** una celda sin `<v>` ni `<is>` (solo formato) es vacía; una fila con A y B vacías se ignora.
- **D-09 (agente):** precio: celda numérica leída del texto del XML con `decimal.TryParse(NumberStyles.Float,
  CultureInfo.InvariantCulture)`, redondeada según D-03. Texto, booleano, error, fecha o vacía → "precio inválido". Fuera
  de `ReglasImporte.ImporteMaximo` o con precio de venta recalculado fuera de rango → "precio inválido".
- **D-10 (agente):** código: texto de la celda (cadena compartida, en línea o número con cultura invariante), recortado
  con `NormalizacionCodigo`. Vacío, UTF-16 inválido o más de 50 caracteres → "código inexistente" sin excepción. En la
  respuesta el código se muestra hasta 60 caracteres (con "…" si es más largo).
- **D-11 (agente):** prioridad de razones por fila: repetido > código inexistente > precio inválido. Todas las filas de
  una clave repetida se listan como repetidas, aunque alguna tenga el precio mal.
- **D-12 (agente):** FR-11 es la excepción documentada a D-02 de FEAT-002b ("un inactivo no se modifica"):
  `Articulo.ConCostoNuevo` cambia costo y venta de un inactivo y lo deja inactivo.
- **D-13 (agente):** costo y venta iguales a los guardados → la fila cuenta como aplicada y no se escribe (molde D-04
  de FEAT-002c).
- **D-14 (agente):** límites: cuerpo de 14 MB (10 MB de archivo), 50.000 filas con datos, índice de fila hasta 50.001,
  64 KB por celda, `MaxCharactersInPart` de 100 millones de caracteres. Pasar uno rechaza la planilla completa.
- **D-15 (agente):** todo o nada: una transacción y una confirmación. Una edición concurrente → 409 con el motivo
  "cambiado" de `ConflictoArticulo`.
- **D-16 (agente):** log Information después del commit, categoría `Optica.Application.Catalogo.Planilla`, enviada al
  Registro de eventos; solo cantidades y fecha UTC, nunca códigos, precios ni nombre del archivo.
- **D-17 (agente):** las excepciones del ZIP, el XML y el SDK se traducen a "no es .xlsx" sin mensaje ni causa.
- **D-18 (agente):** el harness e2e arma el .xlsx con `node:zlib` en un auxiliar propio, sin paquetes npm nuevos.
- **D-19 (agente):** controlador propio `PlanillaCostosController` en la ruta `api/articulos/planilla-costos`, sin
  cambiar el constructor de `ArticulosController`.
- **D-20 — agente, bajo la delegación del usuario — 2026-11-17 — Block 2:** las revisiones midieron que
  `MaxCharactersInPart` no acota `[Content_Types].xml` ni los `.rels` (un ZIP de 205 KB llegó a 3,36 GB), ni la
  cantidad de cadenas compartidas, ni `workbook.xml` cargado como DOM, ni millones de `<c/>` (32 KB → 2,8 s). Guardas
  adicionales de R-02, que el Error handling del Block 2 pide ("sin agotar memoria"):
  - antes de abrir: EOCD presente y válido (ZIP64 aceptado si declara hasta 100 entradas y registros coherentes);
    recorrido del ZIP contando los bytes realmente descomprimidos: hasta 100 entradas, 16 MiB por entrada y 32 MiB en
    total. Una planilla legítima de 50.000 filas mide unos 10,5 MB en su parte más grande;
  - lectura SAX sin `LoadCurrentElement` de `workbook.xml`, las cadenas (hasta 200.000) y la hoja (hasta 1.000.000 de
    `<c>` y 16.384 por fila); `r` de fila entre 1 y 1.048.576;
  - pasar un tope de tamaño o cantidad → "excede límites"; firma, EOCD o ZIP corruptos → "no es .xlsx".
  El tope de 100 millones de caracteres por parte de D-14 sigue configurado en el SDK como segunda barrera, inerte en
  producción por el tope de 16 MiB. Pico transitorio aceptado antes de un rechazo: unos 185 MB y 380 ms por carga; las
  cargas se serializan en el Block 4 (WARN-D).
  Rondas 5 y 6: recorrido de bytes del marcado en cada entrada (hasta 8 KB por etiqueta y 64 niveles de anidamiento,
  en UTF-8, UTF-16 y UTF-32; una entrada cuyo comienzo tiene 0x00 junto a `<` o a una marca FE FF / FF FE fuera de UTF-16/UTF-32 —UCS-4 2143 y 3412— → "no es .xlsx"; los binarios de Excel que empiezan con 0x00, como `printerSettings1.bin` o imágenes EMF, no se recorren y se aceptan), hasta
  1.048.576 `<row>` recorridas. Residuo aceptado: una hoja armada con un millón de filas vacías asigna unos 520 MB y
  tarda unos 0,7 s antes del corte; una planilla con formato en muchas columnas vacías puede pasar los topes y recibir
  "excede límites" (el mensaje del Block 4/5 lo explica). El supuesto de que no hay `CodePagesEncodingProvider`
  registrado queda protegido por prueba.
- **D-22 — usuario — 2026-11-17 — Block 2:** el Block 2 agotó los 3 intentos de corrección; el usuario autorizó un
  4.º intento limitado a rechazar codificaciones no reconocidas, sin otra ronda de búsqueda de ataques.
- **D-21 — agente, bajo la delegación del usuario — 2026-11-17 — Block 2:** el código numérico se convierte a texto sin escala (100.0 → "100") con una única
  función del dominio, `PlanillaCostos.TextoDeCodigoNumerico`, que usan el lector y el dominio. La spec decía
  `ToString(CultureInfo.InvariantCulture)`, que conservaría "100.0" y no coincidiría con el artículo "100"; prevalece
  D-10 (comparar el código como lo ve la operadora).
- **D-23 — agente, bajo la delegación del usuario — 2026-11-17 — Block 4:** las cargas de planilla se serializan con un
  único `SemaphoreSlim(1)` estático en `ActualizarCostosPorPlanilla` (esperan en fila, sin mensaje nuevo): acota la
  memoria transitoria de D-20 a una carga por vez y coincide con el único escritor de SQLite (R-02, R-04).
- **D-24 — agente, bajo la delegación del usuario — 2026-11-17 — Block 4:** el mensaje de "excede límites" agrega la
  causa más probable en una planilla legítima (formato en muchas celdas vacías): "La planilla supera las 50.000 filas o
  tiene celdas demasiado largas. Dividila y cargala por partes. Si tiene formato en celdas vacías, quitáselo.".
- **D-25 — agente, bajo la delegación del usuario — 2026-11-17 — Block 4:**
  - limitador de concurrencia de ASP.NET para el endpoint (1 en curso, 1 en fila), que corre antes de leer el cuerpo:
    el pedido que excede responde 429 sin leerlo, y la pantalla muestra "Hay otra planilla en proceso. Esperá unos
    segundos y volvé a cargarla.". Cada pedido retiene hasta unos 50 MB (cuerpo UTF-8, texto base64 y archivo
    decodificado), así que el pico queda acotado a dos pedidos. El semáforo de D-23 queda como segunda barrera,
    inyectado como singleton `CandadoCargaPlanilla`;
  - 409 con el texto "Un artículo cambió mientras se cargaba la planilla. No se actualizó ningún costo. Volvé a
    cargarla.";
  - un archivo decodificado de más de 10 MB responde 400 "La planilla supera los 10 MB." (el mismo texto que el 413 en
    la pantalla);
  - un JSON ilegible o una planilla que no es texto responde 400 con el mensaje "no es .xlsx".
- **D-26 — agente, bajo la delegación del usuario — 2026-11-17 — Block 5:**
  - 409 con el texto de D-25 (constante propia del cliente, no el `detail`);
  - un archivo que el navegador no puede leer muestra "No se pudo leer el archivo. Elegilo de nuevo.";
  - una tabla sin filas no se muestra;
  - cada tabla muestra las primeras 500 filas con el aviso "Se muestran 500 de N filas. Corregí la planilla y volvé a
    cargarla." (una planilla de 50.000 filas congelaría la pantalla);
  - un 200 que no respeta el contrato muestra "La planilla se procesó, pero no se pudo mostrar el resultado. Revisá los
    costos en el listado." (los costos ya se aplicaron);
  - el resumen usa el texto literal de la spec, sin singular.
  Deuda fuera de alcance: las regiones `role="status"` del proyecto se montan junto con su texto y algunos lectores de
  pantalla no las anuncian (mismo patrón que SeccionBackup y NuevoArticulo).
- **D-27 — agente, bajo la delegación del usuario — 2026-11-17 — Block 6:**
  - **Hosts de las pruebas de .NET.** Las pruebas del lector (Block 2) escribían URLs de espacios de nombres OOXML
    (`schemas.openxmlformats.org`, `purl.org`, `schemas.microsoft.com`) y `example.invalid`, y
    `Pruebas_SinHostsExternos` de `npm run test:unidad` quedó en rojo desde ese bloque. No se detectó antes porque el
    `/ddw-test` por bloque no corría `test:unidad` de e2e cuando el bloque tocaba solo `tests/**/*.cs`. Como en W3
    (`RUTAS_W3`) y ARCA (`RUTAS_ARCA`, ADR-027), los identificadores que no se piden por red van a la lista permitida
    de `e2e/ci.mjs` (`RUTAS_OOXML`, con ruta exacta) y el TLD reservado `.invalid` (RFC 6761) se acepta; las URLs se
    escriben enteras. El cierre de CODE corre `test:unidad` siempre.
  - **Campo de archivo en la pantalla nueva.** `backup-aviso.spec.ts` (AC-15 de FEAT-010) recorre también "Artículos →
    Actualizar costos con planilla" y admite ahí un único campo de archivo `accept=".xlsx"` con la etiqueta "Planilla de
    costos (.xlsx)". Esa pantalla no restaura backups.
