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
