# PRD DISC-001-06: Emisión de factura a consumidor final (ARCA WSFEv1)

| Campo | Valor |
|-------|-------|
| Ticket | DISC-001 (PRD 06 de 9) |
| Tracker | ninguno |
| Fecha | 2026-10-30 |
| PRD de origen | `PRD.md` (PRD-001) |
| PRD loops | 0 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de PRD-001
> del que proviene. Los que surgen de decisiones de las dueñas tomadas en DISC-001 lo indican así.

## Context and Problem
Hoy la óptica factura entrando al sitio de ARCA con el usuario y la contraseña de la empresa, y
vuelve a cargar a mano los datos del cliente y los importes que ya tiene en el presupuesto. Es
trabajo repetido y cada transcripción es una oportunidad de error en un comprobante que, una vez
autorizado, no se puede anular desde el sistema (las notas de crédito están fuera de alcance).

Este PRD permite emitir la factura a consumidor final directamente desde un presupuesto en Final,
pidiendo la autorización (CAE) al web service de facturación electrónica de ARCA (WSFEv1). El tipo
de comprobante depende de la condición fiscal de la óptica: Factura B como Responsable Inscripto,
con neto e IVA discriminados, y Factura C como Monotributo, solo con el total.

## Goals
- Emitir la factura sin volver a cargar datos que ya están en el presupuesto.
- Que el importe facturado sea siempre el total del presupuesto Final.
- Que cada venta se facture una sola vez y que ningún error de ARCA deje un comprobante a medias.
- Que el receptor se identifique o no según el tope vigente, sin decisión manual.

## Functional Requirements

### Emisión
- FR-01: El sistema debe permitir emitir una factura a consumidor final desde un presupuesto en
  Final, tomando de él los datos del cliente y las líneas, sin que la operadora vuelva a cargarlos.
  [PRD-001 RF-25]
- FR-02: El sistema debe rechazar la emisión de una factura desde un presupuesto en Borrador, venga
  el pedido de la pantalla o de un pedido directo a la API, sin enviar ninguna solicitud a ARCA.
  [PRD-001 RF-62]
- FR-03: El sistema debe rechazar la emisión de una factura desde un presupuesto que ya tenga una
  factura autorizada, sin enviar ninguna solicitud a ARCA. [Decisión de las dueñas: una sola
  factura por presupuesto]
- FR-04: El sistema debe facturar como importe total el total del presupuesto. [PRD-001 RF-25,
  AC-16]
- FR-05: El sistema debe emitir Factura B cuando la condición fiscal configurada sea Responsable
  Inscripto. [PRD-001 RF-26]
- FR-06: El sistema debe emitir Factura C cuando la condición fiscal configurada sea Monotributo.
  [PRD-001 RF-76]
- FR-07: El sistema debe usar como fecha del comprobante la fecha del día de emisión, sin permitir
  modificarla. [Decisión de las dueñas]
- FR-08: El sistema debe pedir a la operadora, en cada factura, que elija el concepto "Productos" o
  "Productos y servicios". [Decisión de las dueñas]
- FR-09: El sistema debe informar a ARCA, cuando el concepto sea "Productos y servicios", la fecha
  de inicio del servicio, la de fin del servicio y la de vencimiento del pago iguales a la fecha
  del comprobante. [Decisión de las dueñas]

### Receptor
- FR-10: El sistema debe identificar al receptor como "Consumidor Final" sin identificar cuando el
  total sea menor o igual al tope de identificación, aunque el presupuesto tenga DNI.
  [PRD-001 RF-27]
- FR-11: El sistema debe identificar al receptor con el DNI del cliente cuando el total supere el
  tope de identificación. [PRD-001 RF-71]
- FR-12: El sistema debe tomar el tope de identificación del parámetro de configuración vigente al
  momento de emitir. [PRD-001 RF-49]
- FR-13: El sistema no debe emitir la factura mientras el tope de identificación no tenga valor
  cargado, y debe indicar a la operadora que lo cargue en la configuración. [Decisión de las
  dueñas; PRD 01 FR-20]

### Importes
- FR-14: El sistema debe calcular, cuando la condición fiscal sea Responsable Inscripto, el neto
  como total / (1 + alícuota / 100) redondeado a 2 decimales con mitad hacia arriba, y el IVA como
  total − neto, y enviarlos discriminados a ARCA. [PRD-001 RF-43; decisión de las dueñas: cálculo
  sobre el total]
- FR-15: El sistema debe tomar la alícuota de IVA del parámetro de configuración vigente al momento
  de emitir. [PRD-001 RF-46]
- FR-16: El sistema debe enviar a ARCA la Factura C solo con el importe total, sin desglose de neto
  e IVA. [PRD-001 RF-85]

### Autorización y resultado
- FR-17: El sistema debe solicitar la autorización del comprobante (CAE) al web service de
  facturación electrónica de ARCA (WSFEv1), con el certificado digital de la empresa y el punto de
  venta habilitado para web services. [PRD-001 RF-28]
- FR-18: El sistema debe guardar la factura autorizada con su tipo, punto de venta, número, fecha,
  concepto, identificación del receptor, líneas, importes, CAE y fecha de vencimiento del CAE,
  vinculada al presupuesto que le dio origen. [PRD-001 RF-29]
- FR-19: El sistema debe mostrar, al autorizarse la factura, un mensaje de confirmación con el tipo,
  el punto de venta y el número del comprobante. [PRD-001 AC-18]
- FR-20: El sistema debe mostrar en el presupuesto el tipo, el punto de venta y el número de la
  factura emitida desde él. [Derivado de PRD-001 AC-18]
- FR-21: El sistema no debe registrar ninguna factura cuando ARCA rechace la solicitud, y debe
  mostrar a la operadora el código y la descripción del error informados por ARCA.
  [PRD-001 RF-31, RF-51, RF-53]
- FR-22: El sistema debe permitir volver a emitir la factura del mismo presupuesto después de un
  rechazo de ARCA. [PRD-001 RF-52]
- FR-23: El sistema debe abandonar la espera cuando ARCA no responda, no debe reintentar la
  emisión por su cuenta y debe mostrar el motivo a la operadora. [PRD-001 RNF-10, AC-60]
- FR-24: El sistema debe dejar pendiente de verificación la emisión que quedó sin respuesta y no
  debe enviar una nueva solicitud para ese presupuesto hasta que la verificación del PRD 07 la
  resuelva. [Derivado de PRD-001 RF-53, RF-65: un timeout no indica si ARCA autorizó]
- FR-25: El sistema no debe permitir modificar ni eliminar una factura autorizada con CAE, por
  ninguna vía. [PRD-001 RF-32]

## Non-Functional Requirements
- NFR-01: El sistema debe dar por no disponible el web service de ARCA tras 30 segundos sin
  respuesta. [PRD-001 RNF-10]
- NFR-02: El tiempo que el sistema agrega a la emisión, medido desde que la operadora presiona
  Facturar hasta el mensaje de confirmación y excluida la espera de la respuesta de ARCA, debe ser
  menor a 2 segundos en el percentil 95. [PRD-001 RNF-11]
- NFR-03: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge,
  en su versión estable vigente y en la inmediata anterior (4 combinaciones). [PRD-001 RNF-03]
- NFR-04: Los importes enviados a ARCA y guardados deben expresarse con 2 decimales.
  [PRD-001 RF-19]

## Acceptance Criteria
- AC-01 (FR-01, FR-04): WHEN la operadora presiona Facturar en un presupuesto en Final con 3 líneas
  y total $3.000,00, THE sistema SHALL generar la factura con esas 3 líneas y sus importes y enviar a
  ARCA un importe total de $3.000,00, sin pedir que se cargue ningún dato del cliente ni de las
  líneas. [PRD-001 AC-16]
- AC-02 (FR-02): IF la operadora abre un presupuesto en Borrador o pide facturarlo directamente a la
  API, THEN THE sistema SHALL mostrar deshabilitado el botón Facturar y rechazar el pedido sin
  enviar ninguna solicitud a ARCA. [PRD-001 AC-17]
- AC-03 (FR-03): IF la operadora abre un presupuesto que ya tiene una factura autorizada o pide
  facturarlo directamente a la API, THEN THE sistema SHALL mostrar deshabilitado el botón Facturar y
  rechazar el pedido sin enviar ninguna solicitud a ARCA.
- AC-04 (FR-05): WHEN, con la condición fiscal en Responsable Inscripto, la operadora emite una
  factura, THE sistema SHALL solicitar a ARCA una Factura B. [PRD-001 AC-31]
- AC-05 (FR-06): WHEN la operadora cambia la condición fiscal de Responsable Inscripto a
  Monotributo y emite una factura, THE sistema SHALL solicitar a ARCA una Factura C.
  [PRD-001 AC-73]
- AC-06 (FR-07): WHEN la operadora emite una factura el 30/10/2026, THE sistema SHALL enviar a ARCA
  y guardar la fecha de comprobante 30/10/2026, sin ofrecer un campo para cambiarla.
- AC-07 (FR-08, FR-09): WHEN la operadora elige el concepto "Productos y servicios" y emite una
  factura el 30/10/2026, THE sistema SHALL enviar a ARCA ese concepto con fecha de inicio del
  servicio, fecha de fin del servicio y fecha de vencimiento del pago 30/10/2026.
- AC-08 (FR-08): WHEN la operadora elige el concepto "Productos" y emite una factura, THE sistema
  SHALL enviar a ARCA ese concepto sin fechas de servicio ni de vencimiento del pago.
- AC-09 (FR-10, FR-12): WHEN, con el tope en $100.000,00, la operadora factura un presupuesto de
  total $50.000,00 con DNI 23456789 cargado, THE sistema SHALL identificar al receptor ante ARCA
  como "Consumidor Final" sin identificar. [PRD-001 AC-41]
- AC-10 (FR-10): WHEN, con el tope en $100.000,00, la operadora factura un presupuesto de total
  $100.000,00, THE sistema SHALL identificar al receptor como "Consumidor Final" sin identificar.
  [PRD-001 AC-77]
- AC-11 (FR-11): WHEN, con el tope en $100.000,00, la operadora factura un presupuesto de total
  $100.000,01 con DNI 23456789, THE sistema SHALL enviar a ARCA el DNI 23456789 como identificación
  del receptor. [PRD-001 AC-21]
- AC-12 (FR-12): WHEN la operadora cambia el tope de $100.000,00 a $40.000,00 en la configuración y
  luego factura un presupuesto de total $50.000,00, THE sistema SHALL enviar el DNI del cliente como
  identificación del receptor. [PRD-001 AC-49]
- AC-13 (FR-13): IF el tope de identificación no tiene valor cargado y la operadora presiona
  Facturar, THEN THE sistema SHALL no enviar ninguna solicitud a ARCA e indicar que debe cargar el
  tope en la configuración.
- AC-14 (FR-14, FR-15): WHEN, con Responsable Inscripto y alícuota 21, la operadora factura un
  presupuesto de total $1.815,00, THE sistema SHALL enviar a ARCA un neto de $1.500,00 y un IVA de
  $315,00. [PRD-001 AC-47, AC-48]
- AC-15 (FR-14, NFR-04): WHEN, con Responsable Inscripto y alícuota 21, la operadora factura un
  presupuesto de total $1.000,00, THE sistema SHALL enviar a ARCA un neto de $826,45 y un IVA de
  $173,55.
- AC-16 (FR-15): WHEN la operadora cambia la alícuota a 10,5 y factura, con Responsable Inscripto,
  un presupuesto de total $1.105,00, THE sistema SHALL enviar a ARCA un neto de $1.000,00 y un IVA de
  $105,00.
- AC-17 (FR-16): WHEN, con Monotributo, la operadora factura un presupuesto de total $1.815,00, THE
  sistema SHALL solicitar a ARCA una Factura C con importe total $1.815,00 y sin desglose de neto e
  IVA. [PRD-001 AC-84]
- AC-18 (FR-17, FR-18, FR-19): WHEN la operadora presiona Facturar y ARCA autoriza el comprobante,
  THE sistema SHALL guardar la factura con su tipo, punto de venta, número, CAE y vencimiento del
  CAE vinculada al presupuesto, y mostrar un mensaje de confirmación con el tipo, el punto de venta
  y el número del comprobante. [PRD-001 AC-18]
- AC-19 (FR-20): WHEN la operadora abre un presupuesto desde el que se emitió la Factura B
  0003-00000125, THE sistema SHALL mostrar en el presupuesto "Factura B 0003-00000125".
- AC-20 (FR-21, FR-22): IF ARCA rechaza la solicitud, THEN THE sistema SHALL no registrar ninguna
  factura, mostrar el código y la descripción del error informados por ARCA y mantener habilitado
  Facturar en ese presupuesto. [PRD-001 AC-20]
- AC-21 (FR-23, NFR-01): IF ARCA no responde cuando la operadora presiona Facturar, THEN THE sistema
  SHALL abandonar la espera a los 30 segundos, no reintentar por su cuenta y mostrar en pantalla que
  ARCA no respondió. [PRD-001 AC-60]
- AC-22 (FR-24): IF la emisión de un presupuesto quedó sin respuesta de ARCA y la operadora vuelve
  a presionar Facturar, THEN THE sistema SHALL no enviar una nueva solicitud e indicar que la
  emisión anterior está pendiente de verificación.
- AC-23 (FR-25): IF la operadora intenta modificar o eliminar una factura autorizada con CAE, desde
  la pantalla o con un pedido directo a la API, THEN THE sistema SHALL rechazar el intento y dejar
  la factura sin cambios. [PRD-001 AC-32]
- AC-24 (FR-17): WHEN la operadora emite una factura en el entorno de homologación de ARCA, THE
  sistema SHALL obtener un CAE para el punto de venta habilitado para web services, sin pedir el
  certificado ni el punto de venta en ninguna pantalla.
- AC-25 (NFR-02): WHEN, con ARCA respondiendo sin error, la operadora emite 20 facturas y se
  registra en cada una el tiempo total y el tiempo de espera de ARCA, THE sistema SHALL mantener la
  diferencia por debajo de 2 segundos en al menos 19 de ellas. [PRD-001 AC-78]
- AC-26 (NFR-03): WHEN se ejecutan los criterios de este documento en Chrome y en Edge, en su
  versión estable vigente y en la inmediata anterior, THE sistema SHALL cumplirlos en las 4
  combinaciones. [PRD-001 AC-56]

## Out of Scope
- Verificación de una emisión sin respuesta (consulta del último comprobante, recuperación del CAE
  y aviso de importe distinto): corresponde al PRD 07.
- PDF de la factura, listado y búsqueda de facturas: corresponden al PRD 08.
- Facturas A, notas de crédito, notas de débito y anulaciones. [PRD-001 Fuera de Alcance]
- Más de una factura por presupuesto y facturación parcial (decisión de las dueñas).
- Elección de la fecha del comprobante y carga manual de las fechas de servicio (decisión de las
  dueñas).
- Emisión contra el entorno de producción de ARCA durante el desarrollo y las pruebas.

## Risks and Mitigations
- Riesgo: un comprobante autorizado por error no puede anularse desde el sistema. Mitigación: solo
  se factura un presupuesto en Final (FR-02), una única vez (FR-03), y todo el desarrollo y las
  pruebas se hacen contra homologación de ARCA, nunca contra producción.
- Riesgo: un timeout de ARCA no indica si el comprobante se autorizó, y reenviar podría duplicarlo.
  Mitigación: la emisión queda pendiente y bloqueada (FR-24) hasta la verificación del PRD 07. El
  PRD 07 debe estar implementado antes de usar este PRD en producción; sin él, un presupuesto que
  quedó sin respuesta no se puede volver a facturar.
- Riesgo: exposición del certificado digital y de la clave de la empresa. Mitigación: no se
  muestran en la interfaz (PRD 01 FR-14), se instalan fuera de la aplicación y no se versionan en
  el repositorio, que es público.
- Riesgo: el DNI del cliente (dato personal, Ley 25.326) se envía a ARCA. Mitigación: solo cuando
  el total supera el tope (FR-11), que es lo que exige la normativa.
- Riesgo: cambios normativos de ARCA (topes, formato del comprobante). Mitigación: el tope es un
  parámetro (FR-12) y la integración con ARCA queda aislada en un módulo propio, como exige
  `AGENTS.md`.
- Riesgo: el neto calculado sobre el total puede diferir en un centavo del que resultaría línea por
  línea. Mitigación: decisión de las dueñas; neto + IVA siempre suman exactamente el total
  (FR-14).

## Dependencies
- PRD 01 (DISC-001): sesión iniciada, condición fiscal, alícuota de IVA y tope de identificación
  (FR-08 a FR-13 y FR-20), y certificado y punto de venta fuera de la interfaz (FR-14).
- PRD 04 (DISC-001): presupuestos en Final inmodificables, con total, líneas y DNI guardado sin
  puntos.
- PRD 07 depende de este: verifica las emisiones pendientes de FR-24.
- PRD 08 depende de este: genera el PDF y lista y busca las facturas guardadas según FR-18.
- Externas: certificado digital de la empresa asociado a WSFEv1, punto de venta habilitado para web
  services y acceso al entorno de homologación de ARCA. [PRD-001 Riesgos y Dependencias]
