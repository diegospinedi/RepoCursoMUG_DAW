# PRD DISC-001-01: Acceso, configuración y marca

| Campo | Valor |
|-------|-------|
| Ticket | DISC-001 (PRD 01 de 9) |
| Tracker | ninguno |
| Fecha | 2026-10-29 |
| PRD de origen | `PRD.md` (PRD-001) |
| PRD loops | 1 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de PRD-001
> del que proviene, por ejemplo `[PRD-001 RF-63]`.

## Context and Problem
La base de datos de Óptica Sistema va a guardar datos personales de clientes (DNI, domicilio,
teléfono y email) alcanzados por la Ley 25.326 de Protección de Datos Personales, además de datos
fiscales de la empresa. Antes de que exista cualquier pantalla con esos datos, el sistema necesita
un acceso protegido por contraseña. No hay módulo de usuarios ni administrador: las dos dueñas
comparten una única contraseña, y cualquier bloqueo tiene que liberarse solo.

Los cálculos de precios y la facturación dependen de cuatro parámetros de negocio (alícuota de IVA,
condición fiscal, tope de identificación del receptor y múltiplo de redondeo comercial). Hoy no hay
dónde administrarlos, y un cambio normativo no puede requerir intervención técnica.

Además, la aplicación tiene que mostrar la identidad visual de la óptica, definida en `Marca/`.

## Goals
- Que ninguna pantalla ni operación del sistema devuelva datos sin una sesión iniciada con la
  contraseña de acceso.
- Que las operadoras puedan cambiar los parámetros de negocio desde la aplicación, sin intervención
  técnica.
- Que la aplicación muestre el logo y los colores de la óptica en todas sus pantallas.
- Que el certificado digital y el punto de venta de ARCA nunca sean visibles ni editables desde la
  aplicación.

## Functional Requirements

### Acceso
- FR-01: El sistema debe exigir el ingreso de la contraseña de acceso antes de mostrar cualquier
  pantalla o responder cualquier operación que devuelva datos de clientes, presupuestos, facturas o
  configuración. [PRD-001 RNF-04, AC-79]
- FR-02: El sistema debe pedir que se defina la contraseña de acceso cuando todavía no exista
  ninguna. [PRD-001 RNF-04, AC-57]
- FR-03: El sistema no debe aceptar como contraseña de acceso un valor de menos de 8 caracteres.
  [PRD-001 RNF-04, AC-57]
- FR-04: El sistema debe bloquear el ingreso durante 5 minutos tras 5 intentos fallidos
  consecutivos, rechazando en ese lapso incluso la contraseña correcta. [PRD-001 RNF-08, AC-58]
- FR-05: El sistema debe liberar el bloqueo de FR-04 por sí solo al cumplirse los 5 minutos, sin
  intervención de un administrador. [PRD-001 RNF-08, AC-58]
- FR-06: El sistema debe cerrar la sesión tras 60 minutos sin actividad y volver a exigir la
  contraseña. [PRD-001 RNF-09, AC-59]
- FR-07: El sistema debe guardar la contraseña de acceso únicamente como hash con sal, nunca en
  texto plano. [PRD-001 RNF-14, AC-80]
- FR-17: El sistema debe permitir cambiar la contraseña de acceso con sesión iniciada, pidiendo la
  contraseña actual y la nueva, y aplicando a la nueva la longitud mínima de FR-03. (Decisión de
  las dueñas, 2026-10-29.)
- FR-18: El sistema debe ofrecer un procedimiento de restablecimiento de la contraseña, documentado
  y ejecutable solo en la PC donde corre la aplicación, que elimine la contraseña sin modificar
  ningún otro dato, de modo que el siguiente ingreso aplique FR-02. El procedimiento no debe estar
  disponible desde la interfaz ni desde la API. (Decisión de las dueñas, 2026-10-29: no hay
  administrador ni envío de emails.)

### Configuración
- FR-08: El sistema debe permitir consultar y modificar, desde una pantalla de configuración
  accesible solo con sesión iniciada, la alícuota de IVA, la condición fiscal, el tope de
  identificación del receptor y el múltiplo de redondeo comercial. [PRD-001 RF-63, RF-46, RF-48,
  RF-49, RF-57]
- FR-09: El sistema debe admitir como condición fiscal únicamente los valores Responsable
  Inscripto y Monotributo. [PRD-001 RF-48]
- FR-10: El sistema debe admitir como alícuota de IVA únicamente un porcentaje entre 0 y 100,
  expresado con hasta 2 decimales (por ejemplo, 21 o 10,5). [PRD-001 RF-46, RF-21]
- FR-11: El sistema no debe admitir un múltiplo de redondeo comercial menor a 0,01.
  [PRD-001 RF-72, AC-82]
- FR-12: El sistema no debe admitir un tope de identificación del receptor menor o igual a 0.
  [PRD-001 RF-49]
- FR-13: El sistema debe aplicar el valor grabado de cada parámetro a partir de la siguiente
  operación que lo use, sin reiniciar la aplicación. [PRD-001 RF-63, AC-49]
- FR-19: El sistema debe iniciar, al instalarse, la alícuota de IVA en 21, la condición fiscal en
  Responsable Inscripto y el múltiplo de redondeo comercial en 0,01. (Decisión de las dueñas,
  2026-10-29.)
- FR-20: El sistema debe iniciar el tope de identificación del receptor sin valor al instalarse, y
  mostrar en la pantalla de configuración un aviso de que falta cargarlo mientras no tenga valor,
  porque depende de la normativa vigente de ARCA. (Decisión de las dueñas, 2026-10-29.)
- FR-14: El sistema no debe mostrar ni permitir editar, en ninguna pantalla, el certificado
  digital de la empresa ni el punto de venta habilitado para web services. [PRD-001 RF-64, AC-50]

### Marca y mensajes
- FR-15: El sistema debe mostrar en todas sus pantallas el logo de la óptica y aplicar su color
  primario sobre su color de fondo, tal como los define `Marca/branding.json` (logo
  `Marca/logo.png`, primario #0903A0, fondo #FFFFFF). [PRD-001 RF-34, AC-33]
- FR-16: El sistema debe indicar, en todo error de validación, el campo afectado y la acción
  correctiva que debe tomar la operadora. [PRD-001 RF-35]

## Non-Functional Requirements
- NFR-01: La contraseña de acceso debe tener 8 caracteres como mínimo. [PRD-001 RNF-04]
- NFR-02: El bloqueo por intentos fallidos debe activarse con 5 intentos consecutivos y durar
  5 minutos. [PRD-001 RNF-08]
- NFR-03: La sesión debe cerrarse tras 60 minutos de inactividad. [PRD-001 RNF-09]
- NFR-04: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge,
  en su versión estable vigente y en la inmediata anterior (4 combinaciones). [PRD-001 RNF-03]
- NFR-05: El 100 % de las operaciones que devuelven datos de clientes, presupuestos, facturas o
  configuración deben rechazar el pedido sin sesión iniciada. [PRD-001 RNF-04, AC-79]

## Acceptance Criteria
- AC-01 (FR-01, NFR-05): IF la operadora no inició sesión y abre cualquier pantalla del sistema,
  THEN THE sistema SHALL redirigirla a la pantalla de ingreso sin mostrar datos de clientes,
  presupuestos, facturas ni configuración. [PRD-001 AC-79]
- AC-02 (FR-01, NFR-05): IF se llama a cualquier operación de la API sin sesión iniciada, THEN THE
  sistema SHALL responder 401 sin devolver datos de clientes, presupuestos, facturas ni
  configuración. [PRD-001 AC-79]
- AC-03 (FR-02): WHEN la operadora abre el sistema y todavía no hay una contraseña definida, THE
  sistema SHALL mostrar la pantalla de definición de la contraseña en lugar de la de ingreso.
- AC-04 (FR-03, NFR-01): IF en la pantalla de definición de la contraseña se ingresa una contraseña
  de 7 caracteres, THEN THE sistema SHALL rechazarla e indicar junto al campo que la longitud
  mínima es de 8 caracteres. [PRD-001 AC-57]
- AC-05 (FR-04, NFR-02): IF se ingresa 5 veces seguidas una contraseña incorrecta, THEN THE sistema
  SHALL rechazar durante los 5 minutos siguientes todo ingreso, incluida la contraseña correcta,
  e informar que el acceso está bloqueado temporalmente. [PRD-001 AC-58]
- AC-06 (FR-05, NFR-02): WHEN transcurren 5 minutos desde el bloqueo de AC-05, THE sistema SHALL
  admitir el ingreso con la contraseña correcta sin intervención de un administrador.
  [PRD-001 AC-58]
- AC-07 (FR-06, NFR-03): WHILE una sesión iniciada permanece 60 minutos sin actividad, THE sistema
  SHALL cerrarla y exigir nuevamente la contraseña en la siguiente acción. [PRD-001 AC-59]
- AC-08 (FR-07): WHEN se inspeccionan la base de datos y los archivos de configuración del sistema
  después de definir la contraseña, THE sistema SHALL no contener la contraseña en texto plano en
  ninguno de ellos. [PRD-001 AC-80]
- AC-09 (FR-08, FR-13): WHEN la operadora modifica el tope de identificación del receptor en la
  pantalla de configuración y graba, THE sistema SHALL mostrar el nuevo valor al volver a abrir la
  pantalla y usarlo en la siguiente operación que lo consulte, sin reiniciar la aplicación.
  [PRD-001 AC-49]
- AC-10 (FR-08, FR-09): WHEN la operadora cambia la condición fiscal de Responsable Inscripto a
  Monotributo y graba, THE sistema SHALL mostrar Monotributo como condición fiscal vigente al
  volver a abrir la pantalla de configuración.
- AC-11 (FR-10, FR-16): IF la operadora ingresa una alícuota de IVA de 101 o de -1, THEN THE
  sistema SHALL no grabarla y mostrar junto al campo que la alícuota debe estar entre 0 y 100.
- AC-12 (FR-11, FR-16): IF la operadora ingresa un múltiplo de redondeo comercial de 0,001, THEN THE
  sistema SHALL no grabarlo e indicar junto al campo que el valor mínimo es 0,01. [PRD-001 AC-82]
- AC-13 (FR-12, FR-16): IF la operadora ingresa un tope de identificación del receptor de 0, THEN
  THE sistema SHALL no grabarlo e indicar junto al campo que el tope debe ser mayor a 0.
- AC-14 (FR-14): WHEN la operadora recorre todas las pantallas con sesión iniciada, THE sistema
  SHALL no mostrar ni permitir editar en ninguna el certificado digital ni el punto de venta
  habilitado para web services. [PRD-001 AC-50]
- AC-15 (FR-15, NFR-04): WHEN la operadora abre cualquier pantalla del sistema, incluida la de
  ingreso, THE sistema SHALL mostrar el logo `Marca/logo.png` y aplicar el color primario #0903A0
  sobre fondo #FFFFFF. [PRD-001 AC-33]

- AC-16 (FR-17): WHEN la operadora ingresa la contraseña actual correcta y una contraseña nueva de
  8 caracteres o más y confirma el cambio, THE sistema SHALL aceptar la contraseña nueva en el
  siguiente ingreso y rechazar la anterior.
- AC-17 (FR-17): IF al cambiar la contraseña la contraseña actual ingresada es incorrecta, THEN THE
  sistema SHALL no cambiarla e indicar junto al campo que la contraseña actual no es válida.
- AC-18 (FR-18): WHEN se ejecuta en la PC del local el procedimiento documentado de
  restablecimiento, THE sistema SHALL mostrar en el siguiente ingreso la pantalla de definición de
  la contraseña, y los presupuestos, facturas, artículos y parámetros SHALL seguir sin cambios.
- AC-19 (FR-18): IF se busca restablecer la contraseña desde cualquier pantalla o llamando a
  cualquier operación de la API, THEN THE sistema SHALL no ofrecer ni ejecutar el restablecimiento.
- AC-20 (FR-19): WHEN la operadora abre la pantalla de configuración por primera vez después de
  instalar el sistema, THE sistema SHALL mostrar alícuota de IVA 21, condición fiscal Responsable
  Inscripto y múltiplo de redondeo comercial 0,01.
- AC-21 (FR-20): WHILE el tope de identificación del receptor no tiene valor, THE sistema SHALL
  mostrar en la pantalla de configuración el aviso de que falta cargarlo.

## Out of Scope
- Módulo de usuarios, roles o más de una contraseña de acceso. [PRD-001 Fuera de Alcance]
- Recuperación de la contraseña olvidada por email u otro canal.
- Recálculo de precios del catálogo al grabar la configuración: corresponde al PRD 02
  (PRD-001 RF-86).
- Uso de los parámetros en el cálculo de precios y en la facturación: corresponde a los PRDs 02, 06
  y 07. Este PRD solo los administra.
- Logo y colores en los PDF de presupuesto y factura: corresponden a los PRDs 05 y 08
  (PRD-001 RF-55).
- Instalación y resguardo del certificado digital y del punto de venta: se hacen fuera de la
  aplicación.
- Acceso desde internet: el sistema solo se usa desde la PC o la red local del negocio.

## Risks and Mitigations
- Riesgo: acceso no autorizado a datos personales de clientes (Ley 25.326). Mitigación: FR-01,
  bloqueo por intentos (FR-04) y hash con sal (FR-07).
- Riesgo: contraseña olvidada sin administrador que la restablezca. Mitigación: procedimiento de
  restablecimiento en la PC del local (FR-18), que no borra datos.
- Riesgo: el procedimiento de FR-18 permite a quien tenga acceso físico a la PC tomar el control
  del sistema. Mitigación: solo se ejecuta en esa PC, no desde la red ni la API (AC-19); la
  protección física de la PC queda a cargo del negocio.
- Riesgo: facturar con el tope sin cargar. Mitigación: aviso de FR-20; el PRD 06 debe impedir la
  emisión mientras el tope no tenga valor.
- Riesgo: un parámetro mal cargado (por ejemplo, IVA 210 en lugar de 21) altera todos los precios.
  Mitigación: rangos válidos de FR-10 a FR-12 con mensaje junto al campo (FR-16).
- Riesgo: el bloqueo de FR-04 deja a las dos operadoras sin acceso a la vez, porque comparten la
  contraseña. Mitigación: el bloqueo dura solo 5 minutos y se libera solo (FR-05).

## Dependencies
- `Marca/branding.json` y `Marca/logo.png` (presentes en el repositorio).
- Este PRD no depende de otros PRDs de DISC-001. Los PRDs 02 a 09 dependen de su acceso (FR-01) y
  los PRDs 02, 06 y 07 de sus parámetros (FR-08).
- El PRD 06 (emisión) debe rechazar la emisión mientras el tope de identificación del receptor no
  tenga valor (FR-20).
