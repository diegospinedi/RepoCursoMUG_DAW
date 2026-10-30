# PRD DISC-001-08: PDF, listado y búsqueda de facturas

| Campo | Valor |
|-------|-------|
| Ticket | DISC-001 (PRD 08 de 9) |
| Tracker | ninguno |
| Fecha | 2026-10-30 |
| PRD de origen | `PRD.md` (PRD-001) |
| PRD loops | 0 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de PRD-001
> del que proviene. Los que surgen de decisiones de las dueñas tomadas en DISC-001 lo indican así.

## Context and Problem
El PRD 06 deja la factura autorizada y guardada con su CAE, pero el cliente necesita recibir el
comprobante y la óptica necesita volver a encontrarlo. Hoy eso se resuelve desde el sitio de
ARCA, buscando a mano cada comprobante.

Este PRD agrega el PDF de la factura, con los datos que exige el comprobante (emisor, receptor,
importes, CAE y código QR de ARCA), la carga de los datos del emisor que hoy el sistema no tiene,
y el listado y la búsqueda de las facturas emitidas. También asegura que ningún PDF emitido fuera
de producción pueda confundirse con un comprobante válido.

## Goals
- Entregar al cliente el PDF de su factura sin entrar al sitio de ARCA.
- Encontrar cualquier factura emitida por fecha, número o datos del cliente.
- Que ningún PDF de un comprobante simulado o de homologación parezca un comprobante válido.

## Functional Requirements

### Datos del emisor
- FR-01: El sistema debe permitir cargar y modificar, desde la pantalla de configuración protegida
  por el acceso del PRD 01, la razón social, el domicilio comercial, el CUIT, el número de
  Ingresos Brutos y la fecha de inicio de actividades de la óptica. [PRD-001 RF-30; decisión de las
  dueñas: se cargan en la configuración]
- FR-02: El sistema debe exigir los cinco datos de FR-01 para grabarlos. [Derivado de PRD-001
  RF-30]
- FR-03: El sistema debe admitir como CUIT únicamente 11 dígitos con dígito verificador válido,
  aceptando que se escriba con guiones y guardándolo sin ellos. [Derivado de PRD-001 RF-30]
- FR-04: El sistema no debe generar el PDF de una factura mientras falte alguno de los datos de
  FR-01, y debe indicar a la operadora que los complete en la configuración. [Derivado de PRD-001
  RF-30]

### PDF de la factura
- FR-05: El sistema debe permitir generar el PDF de una factura autorizada y descargarlo en la PC.
  [PRD-001 RF-50]
- FR-06: El sistema debe incluir en el PDF la razón social, el domicilio comercial, el CUIT, el
  número de Ingresos Brutos, la fecha de inicio de actividades y la condición frente al IVA de la
  óptica, esta última tomada del parámetro de condición fiscal. [PRD-001 RF-30]
- FR-07: El sistema debe incluir en el PDF el tipo de comprobante (B o C), el punto de venta, el
  número y la fecha de emisión. [PRD-001 RF-30]
- FR-08: El sistema debe identificar al receptor en el PDF como "Consumidor Final" cuando la
  factura se emitió sin identificar, y con su DNI, Apellido y Nombre cuando se emitió con DNI.
  [PRD-001 RF-30, RF-27, RF-71]
- FR-09: El sistema debe incluir en el PDF cada línea con su descripción, cantidad e importe
  (precio final de la línea), y el importe total, con los mismos importes de la factura guardada.
  [PRD-001 RF-30]
- FR-10: El sistema debe incluir en el PDF el CAE, la fecha de vencimiento del CAE y el código QR
  de ARCA con los datos del comprobante según la especificación vigente de ARCA. [PRD-001 RF-30]
- FR-11: El sistema debe incluir en el PDF de la Factura B la leyenda del Régimen de
  Transparencia Fiscal al Consumidor (Ley 27.743) con el importe de IVA contenido calculado al
  emitirla, sin discriminarlo por línea. [Decisión de las dueñas; a confirmar con el contador]
- FR-12: El sistema no debe incluir el IVA contenido en el PDF de la Factura C. [Decisión de las
  dueñas; PRD-001 RF-85]
- FR-13: El sistema debe incluir en el PDF el logo de la óptica y aplicar su color primario sobre
  su color de fondo, tal como los define la marca del PRD 01. [PRD-001 RF-55]
- FR-14: El sistema debe incluir en el PDF, de forma visible en cada página, la leyenda
  "COMPROBANTE SIMULADO — SIN VALIDEZ FISCAL" cuando la factura se haya emitido en el entorno
  simulado o en el de homologación de ARCA. [`AGENTS.md`; decisión de las dueñas: también en
  homologación]
- FR-15: El sistema no debe incluir la leyenda de FR-14 en las facturas emitidas en el entorno de
  producción de ARCA. [Decisión de las dueñas]

### Listado y búsqueda
- FR-16: El sistema debe permitir listar las facturas emitidas. [PRD-001 RF-33]
- FR-17: El sistema debe permitir buscar facturas por fecha, número de comprobante, Apellido,
  Nombre y DNI del cliente. [PRD-001 RF-54]
- FR-18: El sistema debe combinar con "Y" los filtros cargados y mostrar solo las facturas que
  cumplen todos. [PRD-001 RF-81]
- FR-19: El sistema debe buscar el Apellido y el Nombre por coincidencia parcial, sin distinguir
  mayúsculas ni acentos. [PRD-001 RF-82]
- FR-20: El sistema debe buscar el DNI y el número de comprobante por coincidencia parcial,
  ignorando los puntos y guiones del texto ingresado. [PRD-001 RF-88]
- FR-21: El sistema debe filtrar por fecha con un rango Desde/Hasta, con ambos límites inclusive,
  admitiendo que se cargue uno solo de los dos, y no debe buscar cuando Desde sea posterior a
  Hasta. [PRD-001 RF-83; mismo criterio que el PRD 05]
- FR-22: El sistema debe listar todas las facturas cuando se busque sin filtros, ordenadas de la
  más reciente a la más antigua por fecha y número, en páginas de 50. [Decisión de las dueñas:
  igual que presupuestos]
- FR-23: El sistema debe mostrar en cada resultado el tipo, el punto de venta y número, la fecha,
  Apellido, Nombre, DNI, total y número del presupuesto de origen. [Decisión de las dueñas;
  PRD-001 AC-18]
- FR-24: El sistema debe permitir abrir una factura desde la lista de resultados y mostrar sus
  datos, incluido el número del presupuesto que le dio origen, sin acciones de edición ni de
  eliminación. [PRD-001 AC-18, RF-32]

## Non-Functional Requirements
- NFR-01: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge,
  en su versión estable vigente y en la inmediata anterior (4 combinaciones). [PRD-001 RNF-03]
- NFR-02: Los importes del PDF deben expresarse con 2 decimales. [PRD-001 RF-19]

## Acceptance Criteria
- AC-01 (FR-01, FR-02): WHEN la operadora carga en la configuración razón social, domicilio
  comercial, CUIT, Ingresos Brutos y fecha de inicio de actividades y graba, THE sistema SHALL
  guardar los cinco datos y mostrarlos al volver a abrir la configuración.
- AC-02 (FR-02): IF la operadora graba los datos del emisor sin razón social, THEN THE sistema SHALL
  no grabarlos e indicar junto al campo que debe completarlo.
- AC-03 (FR-03): WHEN la operadora graba el CUIT "20-12345678-6", THE sistema SHALL guardarlo como
  20123456786.
- AC-04 (FR-03): IF la operadora graba el CUIT "20-12345678-5" o "2012345678", THEN THE sistema
  SHALL no grabarlo e indicar junto al campo que el CUIT no es válido.
- AC-05 (FR-04): IF falta la razón social del emisor y la operadora pide el PDF de una factura,
  THEN THE sistema SHALL no generar el PDF e indicar que debe completar los datos del emisor en la
  configuración.
- AC-06 (FR-05, FR-13): WHEN la operadora presiona Descargar PDF en una factura autorizada, THE
  sistema SHALL descargar en la PC un PDF con el logo de la óptica y el color primario #0903A0
  sobre fondo #FFFFFF. [PRD-001 AC-19, AC-33]
- AC-07 (FR-06): WHEN, con la condición fiscal en Responsable Inscripto, la operadora descarga el
  PDF de una factura, THE sistema SHALL mostrar los cinco datos del emisor cargados y la condición
  "Responsable Inscripto".
- AC-08 (FR-07): WHEN la operadora descarga el PDF de la Factura B 0003-00000125 emitida el
  30/10/2026, THE sistema SHALL mostrar la letra B, el punto de venta 0003, el número 00000125 y la
  fecha 30/10/2026.
- AC-09 (FR-08): WHEN la operadora descarga el PDF de una factura emitida sin identificar al
  receptor, THE sistema SHALL mostrar el receptor como "Consumidor Final" sin DNI.
- AC-10 (FR-08): WHEN la operadora descarga el PDF de una factura emitida con DNI 23456789 a "Pérez,
  Ana", THE sistema SHALL mostrar como receptor el DNI 23456789, Apellido "Pérez" y Nombre "Ana".
- AC-11 (FR-09, NFR-02): WHEN la operadora descarga el PDF de una factura con una línea de 3
  unidades e importe $10,02 y otra de importe $2.700,00, THE sistema SHALL mostrar esas dos líneas
  con su descripción, cantidad e importe, y un total de $2.710,02.
- AC-12 (FR-10): WHEN la operadora descarga el PDF de una factura autorizada, THE sistema SHALL
  mostrar el CAE, su fecha de vencimiento y un código QR que, al escanearse, abre la consulta de
  ese comprobante en ARCA. [PRD-001 AC-19]
- AC-13 (FR-11): WHEN la operadora descarga el PDF de una Factura B de total $1.815,00 emitida con
  alícuota 21, THE sistema SHALL mostrar la leyenda del Régimen de Transparencia Fiscal al
  Consumidor (Ley 27.743) con IVA contenido $315,00.
- AC-14 (FR-12): WHEN la operadora descarga el PDF de una Factura C, THE sistema SHALL no mostrar
  ningún importe de IVA contenido.
- AC-15 (FR-14): WHEN la operadora descarga el PDF de una factura emitida en el entorno simulado o
  en el de homologación, THE sistema SHALL mostrar en cada página la leyenda "COMPROBANTE SIMULADO
  — SIN VALIDEZ FISCAL".
- AC-16 (FR-15): WHEN la operadora descarga el PDF de una factura emitida en el entorno de
  producción, THE sistema SHALL no mostrar la leyenda "COMPROBANTE SIMULADO — SIN VALIDEZ FISCAL".
- AC-17 (FR-16, FR-22): WHEN hay 120 facturas emitidas y la operadora busca sin filtros, THE sistema
  SHALL mostrar en la primera página las 50 más recientes, y en la tercera las 20 más antiguas.
- AC-18 (FR-17, FR-18, FR-19, FR-21): WHEN hay facturas emitidas a "González" el 10/03/2026 y el
  20/03/2026, y a "Gómez" el 20/03/2026, y la operadora busca Apellido "gonzalez" con fecha Desde
  15/03/2026, THE sistema SHALL mostrar solo la factura de "González" del 20/03/2026.
  [PRD-001 AC-22]
- AC-19 (FR-17, FR-20): WHEN hay una factura con número de comprobante 0003-00034561 y la operadora
  busca por número "34561", THE sistema SHALL mostrar esa factura. [PRD-001 AC-88]
- AC-20 (FR-17, FR-20): WHEN hay una factura de un cliente con DNI 23456789 y la operadora busca por
  DNI "3456" o "23.456", THE sistema SHALL mostrar esa factura. [PRD-001 AC-87]
- AC-21 (FR-19): WHEN la operadora busca facturas por Nombre "maria" y hay facturas de "María José"
  y "Ana", THE sistema SHALL mostrar solo la de "María José".
- AC-22 (FR-21): IF la operadora busca facturas con fecha Desde 20/03/2026 y Hasta 10/03/2026, THEN
  THE sistema SHALL no buscar e indicar junto al campo Desde que no puede ser posterior a Hasta.
- AC-23 (FR-23): WHEN la búsqueda devuelve la Factura B 0003-00000125 emitida desde el presupuesto
  155, THE sistema SHALL mostrar en ese resultado el tipo, 0003-00000125, la fecha, Apellido,
  Nombre, DNI, total y el presupuesto 155.
- AC-24 (FR-24): WHEN la operadora abre desde la lista una factura emitida desde el presupuesto 155,
  THE sistema SHALL mostrar sus datos y el presupuesto 155, sin acciones de edición ni de
  eliminación. [PRD-001 AC-18, AC-32]
- AC-25 (NFR-01): WHEN se ejecutan los criterios de este documento en Chrome y en Edge, en su
  versión estable vigente y en la inmediata anterior, THE sistema SHALL cumplirlos en las 4
  combinaciones. [PRD-001 AC-56]

## Out of Scope
- Emisión de la factura y su inmutabilidad: corresponden al PRD 06.
- Verificación de emisiones sin respuesta: corresponde al PRD 07.
- Envío automático del PDF por email o WhatsApp e impresión directa. [PRD-001 Fuera de Alcance]
- Facturas A, notas de crédito, notas de débito y anulaciones. [PRD-001 Fuera de Alcance]
- Historial de facturas por cliente. [PRD-001 Fuera de Alcance]
- Exportación del listado de facturas (por ejemplo, a planilla para el contador).
- Versiones históricas de los datos del emisor: el PDF muestra los datos vigentes al generarlo.

## Risks and Mitigations
- Riesgo: la lista de datos del PDF no cumple todo lo que exige la normativa vigente de ARCA.
  Mitigación: PRD-001 RF-30 la marca "a validar con el contador"; la leyenda de la Ley 27.743
  (FR-11) también debe confirmarse con él antes de pasar a producción.
- Riesgo: si cambian los datos del emisor (por ejemplo, el domicilio), un PDF de una factura vieja
  descargado después muestra los datos nuevos. Mitigación: se acepta en esta versión (Out of
  Scope); si el contador lo objeta, se agrega guardar los datos del emisor con cada factura.
- Riesgo: un PDF de homologación o simulado se entrega a un cliente como si fuera válido.
  Mitigación: la leyenda de FR-14 en cada página.
- Riesgo: el PDF contiene DNI, nombre y apellido del cliente (Ley 25.326) y, una vez descargado,
  queda fuera de la protección del sistema. Mitigación: solo se genera con sesión iniciada (PRD 01)
  y el DNI aparece solo cuando la factura se emitió identificando al receptor.
- Riesgo: el CUIT de la óptica queda guardado en la base. Mitigación: es un dato público del
  comprobante; la base no se versiona en el repositorio (`.gitignore`).

## Dependencies
- PRD 01 (DISC-001): sesión iniciada, pantalla de configuración, condición fiscal, logo y colores
  de la marca y mensajes de validación junto al campo.
- PRD 04 (DISC-001): número del presupuesto de origen y datos del cliente.
- PRD 06 (DISC-001): facturas autorizadas guardadas con tipo, punto de venta, número, fecha,
  receptor, líneas, importes, IVA calculado, CAE y vencimiento (FR-14 y FR-18), y entorno en que se
  emitieron.
- Externas: especificación vigente de ARCA para el código QR de los comprobantes electrónicos.
