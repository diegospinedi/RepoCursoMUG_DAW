# Decisiones FEAT-008: Recuperación de emisiones sin respuesta de ARCA

Registro de las decisiones del ticket. "Del usuario" = la tomó el usuario; "del agente" = la tomó el agente
dentro de lo que el PRD y las convenciones permiten, y se puede revisar.

## DEFINE (2026-11-14)

- **D-01 (del usuario).** Sin certificado de homologación: todo se verifica contra el simulador; AC-13 queda pendiente
  de verificación manual contra homologación.
- **D-02 (del usuario).** NFR-03 y AC-14 solo en la versión estable vigente de Chrome y Edge.
- **D-03 (del usuario).** Una pendiente sin número registrado (grabada por FEAT-007) se trata como el caso de importe
  distinto: aviso y descarte confirmado y registrado (FR-14, AC-15).
- **D-04 (del usuario).** Una pendiente de un entorno distinto del configurado no se consulta ni se emite: aviso y
  descarte confirmado y registrado (FR-15, AC-16).
- **D-05 (del usuario).** Un solo ticket con todos los AC (W-PRD-06 aceptado).

## PLAN (2026-11-14)

- **D-06 (del usuario).** AC-15 y AC-16 se verifican con pruebas de integración de la Api sobre SQLite real (filas
  cargadas por SQL respetando los triggers) y con Vitest; sin e2e para ellos (la suite e2e no puede sembrar la base).
- **D-07 (del usuario, lazo PLAN→DEFINE).** La verificación exige 1 minuto desde que la emisión quedó pendiente
  (FR-16, AC-17), por W-6 de la auditoría: evitar un duplicado si ARCA autoriza tarde el envío original.
- **D-08 (del agente, F-1 de la auditoría).** La transacción que graba la pendiente con número (BEGIN IMMEDIATE)
  vuelve a comprobar, antes de agregarla, que el presupuesto sigue en Final, sin factura y sin pendiente. Prueba
  explícita: dos pedidos en paralelo con la transacción 2 del primero confirmada antes de que el segundo grabe.
- **D-09 (del agente, F-2 de la auditoría).** `ProcesoEmision` no toma el candado: recibe el turno ya tomado y el token
  del plazo. `EmitirFactura` toma el turno y lo pasa; `VerificarEmision` lo toma una sola vez y lo conserva desde la
  consulta hasta FECAESolicitar.
- **D-10 (del agente, W-1).** La verificación tiene un plazo de 30 s (`Arca:TiempoMaximoSegundos`) que cuenta desde
  que se pide el candado y cubre la consulta (NFR-01, AC-11). La emisión de FR-06 abre un plazo nuevo de 30 s. El margen
  (`MargenMaximo`) se controla justo antes de FECAESolicitar, después de grabar la pendiente con número. Las
  transacciones que graban no se cancelan con el token del plazo; una pendiente grabada sin envío es segura (la
  consulta dirá "no autorizado").
- **D-11 (del agente, W-2).** En FR-06, con el turno tomado: primero el próximo número; después una sola transacción
  con dos confirmaciones (quitar la vieja, confirmar, agregar la nueva con el número nuevo, confirmar), así siempre
  existe una pendiente. Si el próximo número falla, la pendiente vieja queda tal cual y se informa "no enviada".
- **D-12 (del agente, W-3).** `ObservacionEmision` lleva trigger `BEFORE UPDATE` (se agrega y se quita). Verificar
  una pendiente que ya tiene observación vuelve a evaluar: en una transacción se quita la observación vieja antes de
  grabar el resultado nuevo. El descarte vuelve a comprobar el motivo contra el estado actual (sin número, o entorno
  distinto del configurado; importe distinto con la observación vigente) antes de borrar. CHECK de tabla:
  `importe_distinto` exige número y total de ARCA.
- **D-13 (del agente, W-4).** El archivo del simulador guarda, por tipo y punto de venta, solo los últimos 50
  comprobantes autorizados (total en centavos, CAE, vencimiento, fecha); el límite de tamaño sube a 256 KB. Con prueba
  de crecimiento.
- **D-14 (del agente, W-5).** `ClienteWsfe` lee de FECompConsultar solo `ImpTotal`, `CodAutorizacion`, `FchVto`,
  `Resultado`, `CbteFch` y los errores; descarta `DocTipo` y `DocNro`. Ni el XML crudo ni las excepciones van al log
  (solo `GetType().Name`). `ResultadoConsulta` no lleva el DNI. Los XML de prueba usan DNI ficticios (que empiezan con
  00).
- **D-15 (del agente, W-7).** La migración `RecuperacionEmisiones` agrega las columnas por `ALTER TABLE ADD COLUMN`
  (SQL a mano) con CHECK que nombran solo su columna, declarados también en `OpticaDbContext` (ADR-012). La regla
  "todo o nada" de las columnas nuevas la imponen el dominio y la lectura del repositorio. El Down quita las tablas
  nuevas y las columnas con `DROP COLUMN` sin reconstruir `EmisionPendiente`.
- **D-16 (del agente, I-1).** ADR-025 reemplaza en parte a ADR-021 y ADR-022, que quedan "Accepted" (el validador de
  ADR solo admite "Accepted" o "Superseded by" completo; ADR-025 nombra lo que reemplaza); ADR-026 registra la
  observación y el descarte. ADR-023 sigue vigente: FECompConsultar usa el mismo cliente SOAP.
- **D-17 (del agente, I-2).** `ProcesoEmision` es `public sealed` en Application y se registra en `Program.cs`.
- **D-18 (del agente, I-3).** `Factura.Recuperada` valida que el entorno de la pendiente coincide con el configurado y
  que el vencimiento del CAE no es anterior a la fecha; si no, la verificación responde "no enviada" con el motivo y
  la pendiente queda.
- **D-19 (del agente).** El simulador suma las respuestas `autorizar_sin_responder` (AC-05) y `no_responder_una_vez`
  (AC-06 de punta a punta) y la clave `Arca:Simulado:Consulta` = `responder` | `no_responder` (AC-11).
- **D-20 (del agente).** La espera de FR-16 se lee de `Arca:EsperaVerificacionSegundos` (60 por defecto, entero de 1 a
  3600), igual que el plazo de `Arca:TiempoMaximoSegundos`: la óptica usa 60 s; el harness e2e usa 3 s para no esperar
  un minuto por prueba. Las pruebas de .NET usan el valor por defecto con `TimeProvider` controlado (AC-17).
