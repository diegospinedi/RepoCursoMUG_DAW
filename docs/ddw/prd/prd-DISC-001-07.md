# PRD DISC-001-07: Recuperación de emisiones sin respuesta

| Campo | Valor |
|-------|-------|
| Ticket | DISC-001 (PRD 07 de 9) |
| Tracker | ninguno |
| Fecha | 2026-10-30 |
| PRD de origen | `PRD.md` (PRD-001) |
| PRD loops | 0 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de PRD-001
> del que proviene. Los que surgen de decisiones de las dueñas tomadas en DISC-001 lo indican así.

## Context and Problem
Cuando ARCA no responde a una solicitud de factura dentro del tiempo de espera, el sistema no sabe
si el comprobante se autorizó o no. Si se vuelve a enviar a ciegas y el primero sí se había
autorizado, la venta queda facturada dos veces, y ese comprobante duplicado no puede anularse desde
el sistema porque las notas de crédito están fuera de alcance. Si, en cambio, se da por no emitido
y sí se autorizó, la óptica tiene en ARCA una factura que su sistema no registra.

El PRD 06 deja esas emisiones pendientes y bloqueadas. Este PRD define cómo se resuelven: el
sistema registra antes de cada envío el número que va a pedir, y al verificar le pregunta a ARCA
por ese número. Según la respuesta, recupera el CAE ya otorgado, emite por primera vez o, si el
número figura con otro importe, frena todo y avisa para que se revise en ARCA.

## Goals
- Que ninguna venta quede facturada dos veces por un timeout.
- Que toda factura autorizada en ARCA quede registrada en el sistema con su CAE.
- Que la operadora sepa en todo momento qué emisiones están pendientes y qué hacer con ellas.

## Functional Requirements

### Registro previo al envío
- FR-01: El sistema debe registrar, antes de enviar cada solicitud de factura a ARCA, el número de
  comprobante que solicita (último autorizado del punto de venta para ese tipo + 1), junto con el
  tipo de comprobante, el importe total y el presupuesto. [PRD-001 RF-89]

### Aviso y verificación
- FR-02: El sistema debe mostrar en un presupuesto con emisión pendiente la acción "Verificar con
  ARCA" en lugar de Facturar. [PRD-001 RF-52; decisión de las dueñas: la verificación la pide la
  operadora]
- FR-03: El sistema debe avisar a la operadora, al iniciar sesión, cuando haya emisiones pendientes
  de verificación, indicando el número de cada presupuesto afectado. [Decisión de las dueñas]
- FR-04: El sistema debe consultar a ARCA, cuando la operadora pida verificar, si el número
  registrado según FR-01 figura autorizado para ese punto de venta y tipo, antes de enviar cualquier
  solicitud de factura. [PRD-001 RF-65]
- FR-05: El sistema debe recuperar el CAE y su vencimiento, guardar la factura como en el PRD 06
  FR-18 y no emitir un comprobante nuevo, cuando el número registrado figure autorizado en ARCA con
  el mismo importe total. [PRD-001 RF-66, RF-75, RF-90]
- FR-06: El sistema debe emitir la factura en esa misma acción, registrando un número nuevo según
  FR-01, cuando el número registrado no figure autorizado en ARCA. [PRD-001 RF-65, AC-65;
  decisión de las dueñas]
- FR-07: El sistema no debe emitir ni recuperar ningún comprobante cuando el número registrado
  figure autorizado en ARCA con un importe total distinto. [PRD-001 RF-91]
- FR-08: El sistema debe avisar a la operadora, en el caso de FR-07, que revise el punto de venta
  en ARCA, indicando el número de comprobante, el importe registrado y el importe que figura en
  ARCA. [PRD-001 RF-92]
- FR-09: El sistema debe permitir descartar la emisión pendiente después del aviso de FR-08,
  pidiendo confirmación, y dejar el presupuesto en condiciones de volver a facturarse.
  [Decisión de las dueñas]
- FR-10: El sistema debe registrar la fecha y la hora de cada descarte, con el número de
  comprobante, el importe registrado y el importe que figuraba en ARCA. [Decisión de las dueñas:
  trazabilidad del descarte; no hay usuarios que identificar]
- FR-11: El sistema no debe permitir descartar una emisión pendiente que no haya llegado al caso de
  FR-07, venga el pedido de la pantalla o de un pedido directo a la API. [Derivado de FR-09: el
  descarte solo es seguro cuando ARCA confirmó que el número no corresponde a esta venta]
- FR-12: El sistema debe dejar la emisión pendiente, informar a la operadora y no reintentar por su
  cuenta cuando ARCA no responda a la consulta de verificación. [Derivado de PRD-001 RNF-10]
- FR-13: El sistema debe dejar la emisión pendiente con el número nuevo registrado cuando la
  emisión de FR-06 quede sin respuesta de ARCA. [Derivado de PRD-001 RF-53, RF-89]

## Non-Functional Requirements
- NFR-01: El sistema debe dar por no disponible el web service de ARCA tras 30 segundos sin
  respuesta a la consulta de verificación. [PRD-001 RNF-10]
- NFR-02: Ningún presupuesto debe quedar con más de 1 comprobante autorizado en ARCA como resultado
  de una verificación. [PRD-001 RF-66]
- NFR-03: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge,
  en su versión estable vigente y en la inmediata anterior (4 combinaciones). [PRD-001 RNF-03]

## Acceptance Criteria
- AC-01 (FR-01): WHEN el último comprobante autorizado del punto de venta para Factura B es el 124 y
  la operadora factura un presupuesto de total $1.815,00, THE sistema SHALL registrar, antes de
  enviar la solicitud, el número 125, Factura B, el importe $1.815,00 y el presupuesto.
- AC-02 (FR-02): WHEN la operadora abre un presupuesto con emisión pendiente, THE sistema SHALL
  mostrar la acción "Verificar con ARCA" y no mostrar Facturar.
- AC-03 (FR-03): WHEN la operadora inicia sesión y hay emisiones pendientes de los presupuestos 155
  y 160, THE sistema SHALL mostrar un aviso con los números 155 y 160.
- AC-04 (FR-03): WHEN la operadora inicia sesión y no hay emisiones pendientes, THE sistema SHALL
  no mostrar el aviso de emisiones pendientes.
- AC-05 (FR-04, FR-05): WHEN una emisión quedó pendiente con el número 125 y $1.815,00, ARCA tiene
  autorizado el 125 por $1.815,00 y la operadora verifica, THE sistema SHALL guardar la factura con
  el CAE otorgado y no enviar ninguna solicitud de factura nueva. [PRD-001 AC-64]
- AC-06 (FR-04, FR-06): WHEN una emisión quedó pendiente con el número 125, ARCA no tiene autorizado
  el 125 y la operadora verifica, THE sistema SHALL comprobar primero que el 125 no figura
  autorizado y recién entonces enviar la solicitud, registrando el número según FR-01.
  [PRD-001 AC-65]
- AC-07 (FR-07, FR-08): IF una emisión quedó pendiente con el número 125 y $1.815,00 y ARCA tiene
  autorizado el 125 por $2.000,00, THEN THE sistema SHALL no emitir ni recuperar ningún comprobante
  y mostrar un aviso para revisar el punto de venta en ARCA con el número 125, $1.815,00 y
  $2.000,00. [PRD-001 AC-89]
- AC-08 (FR-09, FR-10): WHEN, después del aviso de AC-07, la operadora descarta la emisión
  pendiente y confirma, THE sistema SHALL mostrar Facturar en el presupuesto y registrar la fecha y
  la hora del descarte con el número 125, $1.815,00 y $2.000,00.
- AC-09 (FR-09): IF la operadora cancela la confirmación del descarte, THEN THE sistema SHALL dejar
  la emisión pendiente y el presupuesto con "Verificar con ARCA".
- AC-10 (FR-11): IF la operadora intenta descartar, desde la pantalla o con un pedido directo a la
  API, una emisión pendiente que todavía no se verificó, THEN THE sistema SHALL rechazar el pedido y
  dejar la emisión pendiente.
- AC-11 (FR-12, NFR-01): IF ARCA no responde a la consulta de verificación, THEN THE sistema SHALL
  abandonar la espera a los 30 segundos, dejar la emisión pendiente, informar que ARCA no respondió
  y no reintentar por su cuenta.
- AC-12 (FR-13): IF la emisión enviada en la verificación de AC-06 queda sin respuesta de ARCA, THEN
  THE sistema SHALL dejar la emisión pendiente con el número nuevo registrado.
- AC-13 (NFR-02): WHEN se ejecutan los casos de AC-05 a AC-12 contra el entorno de homologación de
  ARCA, THE sistema SHALL dejar cada presupuesto con 1 comprobante autorizado como máximo.
- AC-14 (NFR-03): WHEN se ejecutan los criterios de este documento en Chrome y en Edge, en su
  versión estable vigente y en la inmediata anterior, THE sistema SHALL cumplirlos en las 4
  combinaciones. [PRD-001 AC-56]

## Out of Scope
- Verificación automática, sin que la operadora la pida (decisión de las dueñas).
- Reintento automático de la emisión. [PRD-001 RNF-10]
- Anulación o corrección de un comprobante autorizado con importe distinto: se resuelve por fuera
  del sistema (en ARCA o con el contador). [PRD-001 Fuera de Alcance: notas de crédito]
- Emisión normal de la factura: corresponde al PRD 06.
- PDF, listado y búsqueda de facturas: corresponden al PRD 08.

## Risks and Mitigations
- Riesgo: comprobante duplicado no anulable. Mitigación: registro del número antes de enviar
  (FR-01), consulta obligatoria antes de cualquier reenvío (FR-04) y recuperación del CAE cuando ya
  estaba autorizado (FR-05).
- Riesgo: descartar una emisión que en realidad era de esta venta deja en ARCA una factura que el
  sistema no registra. Mitigación: el descarte solo existe en el caso de importe distinto (FR-11),
  pide confirmación (FR-09) y queda registrado (FR-10) para conciliarlo con el contador.
- Riesgo: alguien emite comprobantes en el mismo punto de venta desde otro sistema y el número
  registrado aparece con otro importe. Mitigación: el punto de venta de web services es distinto
  del de la facturación manual (PRD-001 Dependencias), y si pasa, cae en el caso de FR-07.
- Riesgo: las pruebas de timeout no se pueden provocar a voluntad contra ARCA. Mitigación: la spec
  debe definir cómo simular la falta de respuesta sin emitir contra producción.
- Riesgo: ARCA sigue sin responder durante días. Mitigación: la emisión queda pendiente y visible
  al ingresar (FR-03) hasta que se pueda verificar.

## Dependencies
- PRD 01 (DISC-001): sesión iniciada; el aviso de FR-03 se muestra al ingresar.
- PRD 06 (DISC-001): emisión de la factura, guardado de la factura autorizada (FR-18), rechazo de
  ARCA (FR-21) y emisiones pendientes bloqueadas (FR-24). El PRD 06 no debe usarse en producción
  sin este PRD implementado.
- Externas: servicios de ARCA para consultar el último comprobante autorizado y un comprobante por
  número, y entorno de homologación de ARCA. [PRD-001 Riesgos y Dependencias]
