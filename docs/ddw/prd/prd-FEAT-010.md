# PRD FEAT-010: Backup y restauración

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-010 |
| Tracker | ninguno |
| Fecha | 2026-11-16 |
| PRD de origen | `docs/ddw/prd/prd-DISC-001-09.md` (PRD 09 de DISC-001) |
| PRD loops | 1 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de PRD-001 del que proviene. Los
> que surgen de decisiones de las dueñas tomadas en DISC-001 lo indican así, y los que surgen de decisiones del usuario
> en este ticket también. FR-01 a FR-14, NFR-01 a NFR-03 y AC-01 a AC-16 conservan la numeración de
> `prd-DISC-001-09.md`.
>
> **Alcance decidido.** El usuario decidió el 2026-11-16 implementar este PRD en un solo ticket, sin dividir, con sus
> 16 AC (W-PRD-06 aceptado). Se descartó la división en dos partes (ubicación, copia diaria, aviso y retención;
> restauración documentada).
>
> **Navegadores.** El usuario decidió el 2026-11-16, como en FEAT-007 a FEAT-009, que NFR-03 y AC-16 se cumplan en la
> versión estable vigente de Chrome y de Edge. La versión inmediata anterior queda como verificación manual y no se da
> por cumplida en este ticket.
>
> **Destino del backup (vuelta de PLAN, decisiones del usuario del 2026-11-16).** El análisis de impacto mostró que
> una carpeta sincronizada con la nube suele estar en el mismo disco que la base y chocaba con FR-02. El usuario
> decidió: (1) aceptar en el mismo disco una carpeta que esté dentro de la carpeta local de OneDrive, Google Drive o
> Dropbox (FR-02, AC-17); (2) que "disco" significa la unidad (letra), así que una partición distinta del mismo disco
> físico se acepta y la documentación recomienda un pendrive o disco externo; (3) guardar el historial de los backups
> verificados para que el aviso muestre la fecha del último aunque la ubicación no esté accesible (FR-09, AC-18).
>
> **Copias sin cifrar (decisión de las dueñas en DISC-001, reiterada como alerta de datos personales).** Las copias
> guardan sin cifrar todos los datos personales de los clientes (DNI, domicilio, teléfono, email; Ley 25.326). Se
> mantiene la decisión de no cifrar, con la mitigación documental de Risks and Mitigations; si cambia, el cifrado se
> agrega en un ticket posterior.

## Context and Problem
Toda la información del sistema (catálogo, presupuestos, facturas con su CAE y datos personales de
clientes) vive en una base local en la PC de la óptica. Si el disco de esa PC falla, se pierde
todo, incluidas facturas autorizadas que la óptica está obligada a conservar.

Este PRD define el backup automático diario a una ubicación fuera del disco principal, cuántas
copias se conservan, cómo se avisa cuando un backup no se hizo y cómo se restaura la base desde
una copia. Una copia sobrescrita todos los días no alcanza: si la base se corrompe y se detecta
tarde, la copia también está corrupta; por eso se conservan copias diarias y mensuales.

## Goals
- Que la pérdida de datos ante una falla del disco sea, como máximo, la de un día de trabajo.
- Que las dueñas se enteren al día siguiente si un backup no se hizo.
- Poder volver a operar desde una copia en menos de 4 horas.

## Functional Requirements

### Ubicación
- FR-01: El sistema debe permitir elegir y modificar la ubicación del backup desde la pantalla de
  configuración protegida por el acceso del PRD 01. [PRD-001 RNF-02; decisión de las dueñas]
- FR-02: El sistema no debe admitir como ubicación del backup una carpeta de la misma unidad en que
  está la base de datos, salvo que esté dentro de la carpeta local de OneDrive, Google Drive o
  Dropbox. [PRD-001 RNF-02; decisión del usuario del 2026-11-16: se acepta la nube conocida y "disco"
  es la unidad]
- FR-03: El sistema no debe admitir como ubicación del backup una carpeta inexistente o en la que
  no pueda escribir, comprobándolo al grabarla. [Derivado de PRD-001 RNF-02]

### Copia diaria
- FR-04: El sistema debe iniciar todos los días a las 20:00, con la PC del local encendida, un
  backup de la base de datos en la ubicación configurada. [PRD-001 RNF-02]
- FR-05: El sistema debe generar una copia consistente que incluya todos los cambios grabados hasta
  el inicio del backup, sin impedir que la operadora siga grabando mientras se hace.
  [`AGENTS.md`: no copiar la base con una copia de archivo común; derivado de PRD-001 RNF-13]
- FR-06: El sistema debe identificar cada copia con la fecha del día en que se hizo.
  [PRD-001 AC-52]
- FR-07: El sistema debe comprobar al terminar cada copia que se puede abrir y está íntegra, y no
  debe contar como backup del día una copia que no pase esa comprobación. [Derivado de PRD-001
  RNF-02: una copia ilegible no sirve para restaurar]
- FR-08: El sistema debe intentar el backup al iniciar cuando falte el backup del día anterior.
  [Decisión de las dueñas: la PC puede estar apagada a las 20:00]
- FR-09: El sistema debe avisar a la operadora, al ingresar, cuando el backup del día anterior no
  se haya hecho, indicando la fecha del último backup verificado o que no hay ninguno, aunque la
  ubicación del backup no esté accesible. [PRD-001 RF-77; decisión del usuario del 2026-11-16:
  historial de backups]

### Retención
- FR-10: El sistema debe conservar las 7 copias diarias más recientes. [PRD-001 RNF-05]
- FR-11: El sistema debe conservar como copia mensual la última copia diaria de cada uno de los 4
  meses calendario terminados más recientes. [PRD-001 RNF-05; decisión de las dueñas: la última
  del mes]
- FR-12: El sistema debe eliminar las copias que no cumplan FR-10 ni FR-11 solo después de que la
  copia del día haya pasado la comprobación de FR-07. [Derivado de PRD-001 RNF-05: un backup
  fallido no debe reducir las copias buenas]

### Restauración
- FR-13: El sistema debe contar con un procedimiento documentado para restaurar la base a partir de
  una copia elegida, ejecutado en la PC del local. [PRD-001 RNF-06]
- FR-14: El sistema no debe ofrecer la restauración desde la interfaz ni desde la API.
  [Derivado de PRD-001 RNF-06: restaurar reemplaza todos los datos y es un procedimiento técnico]

## Non-Functional Requirements
- NFR-01: La restauración de la base a partir de una copia debe completarse en menos de 4 horas,
  siguiendo el procedimiento documentado, en una PC con el sistema instalado según ese
  procedimiento. [PRD-001 RNF-06]
- NFR-02: La ubicación de backup debe contener, con el sistema operando desde hace más de 4 meses,
  7 copias diarias y 4 copias mensuales. [PRD-001 RNF-05]
- NFR-03: Todos los criterios de aceptación de este documento que usan la interfaz deben cumplirse
  en Chrome y en Edge, en su versión estable vigente (2 combinaciones). [PRD-001 RNF-03; decisión del
  usuario del 2026-11-16: solo la versión estable]

## Acceptance Criteria
- AC-01 (FR-01): WHEN la operadora elige en la configuración la carpeta "Backups" de un pendrive y
  graba, THE sistema SHALL guardar esa ubicación y mostrarla al volver a abrir la configuración.
- AC-02 (FR-02): IF la operadora elige una carpeta de la misma unidad en que está la base y fuera
  de la carpeta local de OneDrive, Google Drive o Dropbox, THEN THE sistema SHALL no grabarla e
  indicar junto al campo que debe estar en otra unidad o en una carpeta sincronizada con la nube.
- AC-03 (FR-03): IF la operadora elige una carpeta inexistente o sin permiso de escritura, THEN THE
  sistema SHALL no grabarla e indicar junto al campo que no se puede escribir en ella.
- AC-04 (FR-04, FR-06): WHEN se cumplen las 20:00 del 30/10/2026 con el sistema en operación y la PC
  encendida, THE sistema SHALL iniciar el backup y, al terminar, dejar en la ubicación configurada
  una copia de la base identificada con la fecha 30/10/2026. [PRD-001 AC-52]
- AC-05 (FR-05): WHEN la operadora graba un presupuesto a las 19:59 y el backup se hace a las
  20:00, THE sistema SHALL incluir ese presupuesto en la copia, de modo que se vea al restaurarla.
- AC-06 (FR-05): WHILE el backup está en curso, THE sistema SHALL permitir que la operadora grabe un
  presupuesto sin recibir un error.
- AC-07 (FR-07): IF el pendrive se desconecta durante el backup y la copia queda incompleta, THEN
  THE sistema SHALL no contarla como backup del día.
- AC-08 (FR-08): WHEN la PC estuvo apagada a las 20:00 del 30/10/2026 y el sistema se inicia el
  31/10/2026 a las 9:00, THE sistema SHALL hacer en ese momento el backup que faltaba.
- AC-09 (FR-08, FR-09): IF el backup del 30/10/2026 no se hizo, el intento al iniciar también falla
  y el último backup disponible es del 29/10/2026, THEN THE sistema SHALL mostrar al ingresar un
  aviso de que falta el backup del día anterior, con la fecha 29/10/2026. [PRD-001 AC-55]
- AC-10 (FR-09): IF no existe ningún backup y falta el del día anterior, THEN THE sistema SHALL
  mostrar al ingresar un aviso que indique que no hay ningún backup disponible.
- AC-11 (FR-10, FR-11, NFR-02): WHEN el sistema opera desde hace más de 4 meses, THE sistema SHALL
  dejar en la ubicación de backup las 7 copias diarias más recientes y 4 copias mensuales.
  [PRD-001 AC-53]
- AC-12 (FR-11): WHEN el último backup de octubre de 2026 fue el del 31/10/2026 y ya terminó el
  mes, THE sistema SHALL conservar la copia del 31/10/2026 como copia mensual de octubre.
- AC-13 (FR-12): IF el backup del día falla, THEN THE sistema SHALL no eliminar ninguna copia
  existente.
- AC-14 (FR-13, NFR-01): WHEN, con una copia de backup, el procedimiento documentado y una PC con el
  sistema instalado según ese procedimiento, se restaura la base, THE sistema SHALL permitir abrir un
  presupuesto que figura en la copia en menos de 4 horas desde el inicio de la restauración.
  [PRD-001 AC-54]
- AC-15 (FR-14): WHEN se recorren todas las pantallas y operaciones de la API con sesión iniciada,
  THE sistema SHALL no ofrecer ninguna forma de restaurar la base.
- AC-16 (NFR-03): WHEN se ejecutan en Chrome y en Edge, en su versión estable vigente, los criterios
  de este documento que usan la interfaz, THE sistema SHALL cumplirlos en las 2 combinaciones.
  [PRD-001 AC-56]
- AC-17 (FR-02): WHEN la operadora elige una carpeta "Backups" dentro de la carpeta local de
  OneDrive que está en la misma unidad que la base y graba, THE sistema SHALL guardar esa ubicación.
- AC-18 (FR-09): IF falta el backup del día anterior, el último backup verificado es del 29/10/2026 y
  la ubicación del backup no está accesible (pendrive desconectado), THEN THE sistema SHALL mostrar
  al ingresar el aviso con la fecha 29/10/2026 e indicar que la ubicación no está accesible.

## Out of Scope
- Cifrado de las copias de backup (decisión de las dueñas: las copias se guardan sin cifrar; ver
  Risks and Mitigations).
- Botón para hacer un backup manual desde la interfaz.
- Restauración desde la interfaz o la API.
- Restauración parcial (de un solo presupuesto o factura).
- Copias a más de una ubicación a la vez.
- Envío de avisos por email cuando un backup falla.
- La versión inmediata anterior de Chrome y Edge (verificación manual, decisión del usuario).
- Comprobar que el servicio de nube realmente sincroniza la carpeta: el sistema solo reconoce la carpeta local.
- Distinguir discos físicos: una partición distinta del mismo disco se acepta (decisión del usuario).

## Risks and Mitigations
- Riesgo: las copias guardan sin cifrar todos los datos personales de clientes (DNI, domicilio,
  teléfono, email), alcanzados por la Ley 25.326. Si se pierde el pendrive o se compromete la cuenta
  de la nube, quedan expuestos. Mitigación: decisión explícita de las dueñas de no cifrar; el
  procedimiento documentado debe indicar guardar el pendrive en un lugar bajo llave y usar una
  cuenta de nube con contraseña propia y verificación en dos pasos. Si cambia la decisión, se agrega
  el cifrado en este PRD.
- Riesgo: una carpeta de nube en la misma unidad deja de sincronizarse (sesión cerrada, cuota llena) y las copias
  quedan solo en el disco principal. Mitigación: la documentación pide revisar periódicamente en la nube que las
  copias lleguen; la falla del disco principal sin sincronización pierde también las copias (riesgo aceptado por el
  usuario al aceptar la nube en la misma unidad).
- Riesgo: el pendrive queda desconectado y los backups dejan de hacerse sin que nadie lo note.
  Mitigación: intento al iniciar (FR-08) y aviso al ingresar (FR-09).
- Riesgo: un backup copia la base a mitad de una escritura y sale inservible. Mitigación: copia
  consistente (FR-05) y comprobación de integridad (FR-07).
- Riesgo: un backup fallido borra copias buenas al aplicar la retención. Mitigación: FR-12.
- Riesgo: el procedimiento de restauración nunca se prueba y falla cuando hace falta. Mitigación:
  AC-14 exige restaurar una copia real como parte de la verificación.
- Riesgo: el certificado digital de ARCA no está en la base y no queda en el backup. Mitigación: el
  procedimiento documentado debe indicar cómo resguardarlo aparte; reinstalar el sistema sin él
  impide facturar.

## Dependencies
- PRD 01 (DISC-001): sesión iniciada, pantalla de configuración y mensajes de validación junto al
  campo; el aviso de FR-09 se muestra al ingresar.
- PRD 04 (DISC-001): presupuestos, usados en AC-05, AC-06 y AC-14 para comprobar la copia.
- FEAT-001a a FEAT-009: la base local con catálogo, presupuestos, facturas y datos del emisor que se respalda; la
  configuración donde se elige la ubicación.
- `AGENTS.md` ("What NOT to do"): la base no se copia con una copia de archivo común, por los cambios que pueden estar
  todavía en el registro de escritura anticipada (FR-05).
- Externas: un disco distinto del principal (pendrive, disco externo o carpeta sincronizada con la
  nube) conectado a la PC del local. [PRD-001 RNF-02]
