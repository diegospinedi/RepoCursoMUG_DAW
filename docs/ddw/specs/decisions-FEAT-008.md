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

## CODE (2026-11-14)

- **D-21 (del usuario).** Delegación: ante una decisión no escrita durante CODE, VERIFY o CLOSEOUT, el agente toma la
  opción recomendada que menos tiempo insuma y la registra aquí; el objetivo es terminar el ticket para la entrega.
- **D-22 (del agente, Block 1, WARN-1 del auditor).** Hasta el Block 4, `EmitirFactura` graba la pendiente con
  `EmisionPendiente.SinNumero` (forma de FEAT-007); el Block 4 la reemplaza por la pendiente con número (ADR-025). La
  rama no se despliega antes del Block 4.
- **D-23 (del agente, Block 1, supuestos del implementador, aprobados por D-21).** Migración
  `20261114120000_RecuperacionEmisiones` (la spec decía 13 dígitos, errata); `EmisionPendiente` deja de exponer la
  `SolicitudComprobante` (no guarda el DNI ni en memoria) y suma la fábrica `Registrada` para releer filas con número;
  lectura todo o nada (la alícuota puede ser nula en la C); CHECK de `importe_distinto` también en `DescarteEmision` y
  rango en `ObservacionEmision.Numero`; métodos `QuitarObservacion` y `ObtenerObservacionAsync`;
  `IDescarteEmisionRepository.ListarDescartesAsync` de solo lectura; alícuota como TEXT invariante ("21", "10.5").
- **D-24 (del agente, WARN-2 del auditor del Block 1).** Toda baja de una pendiente quita antes su observación en la
  misma transacción (FK Restrict): lo aplican los Blocks 4 y 5, con prueba de una pendiente con observación.
- **D-25 (del agente, Block 1).** `FacturaApiTests.Emitir_SinRespuesta_409YLaPendienteBloquea` falla corrida sola
  también sobre 12beb53 (antes de FEAT-008): `AutorizadorConSenal` avisa la llamada antes de que el simulador empiece a
  esperar, y si la prueba adelanta el reloj en ese hueco el resultado es "no enviada". Se corrige en el Block 2, que
  reescribe ese doble para el puerto nuevo (avisar después de que el autorizador real quedó esperando).
- **D-26 (del agente, Block 2, supuestos del implementador, aprobados por D-21).** Tipo, punto de venta o número
  fuera de rango en las operaciones del puerto lanzan `ArgumentException` (error de programación, no falla de ARCA);
  en la consulta, un `Resultado` distinto de "A", un 602 mezclado con otros códigos o un `CbteDesde` distinto del pedido
  son "sin respuesta" (camino conservador); la cancelación antes de consultar es "no consultada" con el texto de
  cancelación existente; hasta el Block 3 el simulador numera con su contador e ignora el número recibido;
  `ResultadoConsulta.Autorizado` exige total ≥ 0, CAE de 14 dígitos y fechas válidas. El implementador escribió algunos
  archivos del bloque con la shell en lugar de Write/Edit (fase CODE, archivos del bloque); queda registrado.
- **D-27 (del agente, Block 3, supuestos del implementador, aprobados por D-21).** El simulador responde "sin
  respuesta" (no "no autorizado") a la consulta de un número ≤ contador que ya no está en el archivo (descartado por
  el tope de 50 o de un archivo de FEAT-007), para no habilitar una reemisión; número > contador → "no autorizado".
  Validación estricta del archivo (rechaza propiedades desconocidas o repetidas, claves duplicadas o con ceros a la
  izquierda y comprobantes con número mayor que su contador). Si grabar superaría 256 KB, no graba y la autorización es
  "no enviada". Los valores de las opciones se comparan sin distinguir mayúsculas (como FEAT-007). "Primera solicitud
  del proceso" = primera `AutorizarAsync` de la instancia singleton. Con `rechazar`, `no_responder` y la primera de
  `no_responder_una_vez` no se compara el número. Veinte pedidos concurrentes del mismo número dan 1 autorizado y 19
  rechazos 10016.
- **D-28 (del agente, Block 4, supuestos del implementador, aprobados por D-21).** Quien toma el turno del candado lo
  libera después de la transacción 2 (no apenas responde ARCA como en FEAT-007), para que otro pedido no tome el turno
  con la factura o la baja todavía sin grabar (F-1). En modo reemplazo, si la pendiente guardada no es la verificada →
  conflicto `pendiente` sin tocar nada; si ya no hay pendiente → "no enviada" sin grabar. Un conflicto de unicidad
  después de confirmar la baja de la vieja revierte toda la transacción (la vieja queda, D-11). Un solo control de
  margen, justo antes de FECAESolicitar (D-10). Toda baja de la pendiente quita antes su observación (D-24). API para
  el Block 5: `ProcesoEmision.EjecutarAsync(presupuesto, solicitud, turno, plazo, reemplazada)`; la solicitud de FR-06
  se arma con el presupuesto, el concepto guardado y la configuración vigente (la spec, Block 5).
- **D-29 (del agente por D-21, WARN-1 del auditor del Block 4).** La garantía de D-11 ("siempre existe una pendiente")
  cubre la transacción de registro del modo reemplazo. Después, si falta margen, ARCA rechaza o la solicitud no se envía,
  se quita la pendiente nueva como en la emisión normal (spec Block 4, FR-21 y FR-22 del PRD 06): no abre un duplicado,
  porque ARCA ya informó que el número viejo no estaba autorizado y el nuevo no se envió o fue rechazado. Se corrige el
  comentario de `ProcesoEmision` para que lo diga.
- **D-30 (del agente, Block 5, supuestos del implementador, aprobados por D-21).** La espera llega a Application como
  `OpcionesVerificacion(TimeSpan)` (la registra el Block 6 desde `OpcionesArca.EsperaVerificacion`). Presupuesto
  inexistente → `NoEncontrado` (404). Sin turno del candado en el plazo → `no_enviada`. Con el turno se relee la
  pendiente: si cambió → `no_enviada` sin grabar; si desapareció → `sin_pendiente`. Traducción de `ProcesoEmision`:
  sin respuesta o pendiente de verificación → `sin_respuesta_emision`; no enviada, pendiente, Borrador, sin tope, sin
  punto de venta, alícuota no admitida → `no_enviada` (la pendiente vieja queda si la solicitud no se puede armar);
  rechazada → con código y descripción. La factura recuperada toma la fecha de la pendiente; los totales se comparan en
  centavos. El descarte no toma el candado. AC-13 en Application corre contra un ARCA en memoria; contra el simulador
  real, en Blocks 6 y 8.
- **D-31 (del agente por D-21, WARN de la auditoría del Block 5).** La espera de verificación tiene que ser mayor o
  igual que el plazo de ARCA más el margen: el Block 6 lo valida al arrancar (si `Arca:EsperaVerificacionSegundos` es
  menor que `Arca:TiempoMaximoSegundos` + margen, error de configuración). Producción usa 60/30 y el harness 3/2.
- **D-32 (del agente por D-21, INFO de la auditoría del Block 5).** Si una verificación de una pendiente con
  observación `importe_distinto` termina sin respuesta, la observación anterior queda vigente (FR-12: pendiente
  intacta); el descarte vuelve a comprobar el número (D-12). La Api no registra el cuerpo de las respuestas 409
  (la descripción de un rechazo de ARCA puede repetir datos de la solicitud): lo verifica el Block 6.
- **D-33 (del agente, Block 6, supuestos del implementador, aprobados por D-21).** La regla de D-31 (espera ≥ plazo +
  margen, margen = mínimo entre 10 s y un tercio del plazo) y el registro de `OpcionesVerificacion` viven en
  `ConfiguracionInfraestructura` (`AislamientoArcaTests` no deja a `Program` leer `OpcionesArca`); `ProcesoEmision.Margen`
  pasa a público. Los textos de motivo y entorno de la observación salen de Application (`TextoMotivo`, `TextoEntorno`)
  porque la Api no referencia Domain. Las extensiones del rechazo en la verificación se llaman `codigoArca` y
  `descripcionArca` (contrato de la spec), recortadas a 500 caracteres. Títulos y detalles fijos de los 409 definidos
  por el agente. Verificación y descarte con límite de 1 KB y `Content-Type: application/json` (como todo POST). El
  entorno se responde en minúscula y sin tildes. Una instalación con plazo de ARCA alto necesita subir la espera
  (documentar en el Block 8).
- **D-34 (del agente por D-21 y la política de datos personales de la organización, H1 de la auditoría del
  Block 6).** La descripción de un rechazo de ARCA puede repetir el DNI del receptor; antes de armar el resultado,
  `ProcesoEmision` reemplaza el número de documento del receptor por asteriscos donde aparezca en la descripción (con y
  sin ceros a la izquierda o puntos). Cubre la verificación y la emisión de FEAT-007, que tenía el mismo riesgo. H2
  (regla de D-31 en Infrastructure) queda como está, aprobado en D-33.
- **D-35 (del agente, Block 7, supuestos del implementador, aprobados por D-21).** Textos de los 409 de verificación y
  descarte escritos en el cliente con el mismo contenido que el `detail` de la Api (sin_respuesta_emision, no_enviada,
  sin_pendiente, sin_observacion; facturado y rechazada como en FEAT-007). Singular con 1 segundo y con un solo
  pendiente en Inicio. Un 401 en Inicio sigue el manejo global de sesión. El diálogo de descarte reutiliza el de "Pasar
  a Final" (`DialogoConfirmacion`). "Descartar la emisión" aparece también si la respuesta trajo observación (la Api
  vuelve a comprobar). Las pruebas que terminan en Inicio pasan sin cambios (404 de la ruta nueva → sin aviso).
- **D-36 (del agente, Block 8, supuestos del implementador, aprobados por D-21).** El e2e de importe distinto no
  siembra el archivo del simulador (D-27 rechaza un comprobante por encima del contador): usa `no_responder_una_vez`,
  dejando pendiente el presupuesto A con la 0003-00000001 sin registrar y facturando el B con ese mismo número por otro
  importe. AC-11 y AC-12 van en archivos separados (configuraciones distintas del simulador). AC-17 se prueba en
  `verificacion-emite` con una pestaña paralela durante la emisión. El harness compara los valores de ARCA exactos en
  minúsculas. El registro del descarte (AC-08) y el orden consulta → envío (AC-06) se comprueban en .NET, no en e2e.
