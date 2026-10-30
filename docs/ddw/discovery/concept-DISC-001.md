# Concepto: Dividir PRD-001 en PRDs implementables

| Métrica | Valor |
|---------|-------|
| Ticket | DISC-001 |
| Fecha | 2026-10-29 |
| Estado | Explorando |
| PRD de origen | `PRD.md` (PRD-001) |

## Visión
Llevar a Óptica Sistema de presupuestos a mano y facturación manual en el sitio de ARCA a una
aplicación web local que arma presupuestos desde un catálogo con precio de venta calculado y emite
la factura electrónica a consumidor final desde el presupuesto Final.

## Problema / Oportunidad
PRD-001 describe el producto completo en un solo documento: 94 requerimientos funcionales, 13
no funcionales vigentes y 92 criterios de aceptación. Es demasiado grande para un único ticket de
implementación: no entra en una sola spec revisable y mezcla módulos con riesgos muy distintos
(cálculo de precios, integración con ARCA, backups). Hay que dividirlo en PRDs del tamaño de un
ticket, cada uno con sus RF, RNF y AC trazables a PRD-001.

## Usuarios objetivo
Las dos dueñas de la óptica, que operan el sistema todos los días desde la PC del local o la red
local. No hay roles: un único acceso con contraseña (RNF-04).

## Features candidatas
- Acceso, configuración y marca: contraseña, bloqueo, sesión, parámetros de negocio, logo y colores.
- Catálogo de artículos y precio de venta calculado (Responsable Inscripto y Monotributo).
- Actualización de precios de costo por planilla Excel.
- Presupuestos: alta, edición en Borrador, líneas, cálculos y paso a Final.
- Búsqueda de presupuestos y PDF del presupuesto.
- Emisión de factura B/C a consumidor final vía WSFEv1.
- Recuperación de emisiones sin respuesta de ARCA (consulta de último comprobante, sin duplicados).
- PDF, listado y búsqueda de facturas.
- Backup diario, retención, restauración y aviso de backup faltante.

## Restricciones y consideraciones
- Stack fijo según `AGENTS.md`: .NET 10 + EF Core 10 + SQLite, React 19 + Vite. Monolito modular.
- Datos personales de clientes (DNI, domicilio, teléfono, email) alcanzados por la Ley 25.326:
  el acceso protegido tiene que existir antes que cualquier pantalla con esos datos.
- Certificado digital y punto de venta de ARCA fuera de la interfaz (RF-64) y fuera del repositorio.
- Todo desarrollo contra homologación de ARCA, nunca producción.
- Registros cerrados inmutables: presupuesto Final y factura con CAE.
- La marca (RF-34) está en `Marca/`: `branding.json` (primario #0903A0, fondo #FFFFFF),
  `logo.png` y el original `Marca.jpg`.
- Requisitos transversales que aplican a todos los PRDs: RF-35 (mensajes de validación) y
  RNF-03 (Chrome y Edge).

## Decisiones tomadas
- 2026-10-29: Se clasifica como DISCOVERY y no como FEATURE — PRD-001 cubre todo el producto y
  hay que dividirlo antes de implementar.
- 2026-10-29: División aprobada en 9 PRDs. El acceso (01) va primero porque ninguna pantalla con
  datos personales debe existir sin login; la recuperación por timeout (07) se separa de la
  emisión (06) por su riesgo de comprobante duplicado no anulable.
- 2026-10-29: RF-35 y RNF-03 son transversales: cada PRD los referencia para sus propias pantallas.
- 2026-10-29 (PRD 01): la contraseña se puede cambiar desde la aplicación pidiendo la actual; si se
  olvida, se restablece con un procedimiento documentado ejecutado solo en la PC del local; los
  parámetros arrancan en IVA 21, Responsable Inscripto y múltiplo 0,01, y el tope se carga a mano.
- 2026-10-29 (PRD 02): margen de utilidad de 0 a 1.000 % (PRD-001 decía 0 a 100); costo ≤ 0
  admitido a mano con advertencia, igual que por planilla; código en el proveedor único y
  obligatorio; los artículos se desactivan y reactivan, no se eliminan.
- 2026-10-29 (PRD 03): la planilla actualiza también artículos inactivos; encabezados "Codigo en
  el proveedor" y "Precio de Costo Nuevo"; un código repetido en la planilla no se aplica; solo
  se aceptan archivos .xlsx.
- 2026-10-30 (PRD 04): las líneas salen solo del catálogo, con precio unitario fijo y descuento
  por línea; la fecha del presupuesto es la del primer grabado y no se edita; el paso a Final pide
  confirmación y todo presupuesto exige al menos una línea; DNI de 7 u 8 dígitos guardado sin
  puntos y email con formato válido si se carga; numeración desde 1; los artículos inactivos no se
  ofrecen al cargar una línea.
- 2026-10-30 (PRD 05): el PDF del presupuesto es completo (número, fecha, datos del cliente
  cargados, líneas con sus 7 campos, total y leyenda) con solo logo y colores en el encabezado;
  la búsqueda usa solo los filtros de PRD-001 (fecha, Apellido, Nombre, DNI), sin filtros lista
  todos, ordenados por número descendente en páginas de 50.
- 2026-10-30 (PRD 06): una sola factura por presupuesto; neto e IVA de la Factura B calculados
  sobre el total (IVA = total − neto); fecha del comprobante siempre la del día; la operadora
  elige en cada factura el concepto "Productos" o "Productos y servicios", y con servicios las
  tres fechas que exige ARCA son la del comprobante; sin tope cargado no se factura; una emisión
  sin respuesta queda pendiente hasta la verificación del PRD 07, que debe implementarse antes de
  usar el PRD 06 en producción.
- 2026-10-30 (PRD 07): la verificación de una emisión sin respuesta la pide la operadora con
  "Verificar con ARCA" y, si el número no estaba autorizado, emite en esa misma acción; al
  ingresar se avisa de las emisiones pendientes; con importe distinto en ARCA, la operadora puede
  descartar la emisión pendiente tras confirmar, y el descarte queda registrado con fecha y hora.
- 2026-10-30 (PRD 08): los datos del emisor (razón social, domicilio comercial, CUIT, Ingresos
  Brutos, inicio de actividades) se cargan en la configuración; la leyenda "COMPROBANTE SIMULADO —
  SIN VALIDEZ FISCAL" va en los PDF emitidos en el entorno simulado y en homologación; la Factura
  B muestra el IVA contenido de la Ley 27.743 (a confirmar con el contador); la búsqueda de
  facturas funciona igual que la de presupuestos. El PRD 06 guarda con cada factura el entorno de
  ARCA en que se emitió.
- 2026-10-30 (PRD 09): la ubicación del backup se elige en la configuración y no puede estar en el
  disco de la base; si falta el backup del día anterior, se intenta al iniciar; como copia mensual
  queda la última de cada mes terminado; las copias se guardan sin cifrar (riesgo de datos
  personales aceptado por las dueñas, mitigado con resguardo físico y verificación en dos pasos
  en la nube).

## Dependencias entre PRDs
- 02 depende de 01 (parámetros de configuración).
- 03 depende de 02 (artículos existentes).
- 04 depende de 02 (selecciona artículos del catálogo).
- 05 depende de 04.
- 06 depende de 04 (factura desde presupuesto Final) y de 01 (condición fiscal, tope, IVA).
- 07 y 08 dependen de 06.
- 09 depende solo de que exista la base; puede ir en paralelo.

## PRDs identificados

| # | Título | Archivo | Estado |
|---|--------|---------|--------|
| 01 | Acceso, configuración y marca | prd-DISC-001-01.md | validado |
| 02 | Catálogo de artículos y precio de venta | prd-DISC-001-02.md | validado |
| 03 | Actualización de costos por planilla Excel | prd-DISC-001-03.md | validado |
| 04 | Presupuestos: alta, líneas y cálculos | prd-DISC-001-04.md | validado |
| 05 | Búsqueda y PDF de presupuestos | prd-DISC-001-05.md | validado |
| 06 | Emisión de factura a consumidor final (ARCA WSFEv1) | prd-DISC-001-06.md | validado |
| 07 | Recuperación de emisiones sin respuesta | prd-DISC-001-07.md | validado |
| 08 | PDF, listado y búsqueda de facturas | prd-DISC-001-08.md | validado |
| 09 | Backup y restauración | prd-DISC-001-09.md | validado |
