# PRD-001: Presupuestos y Facturación Electrónica Online — Sistema web para Óptica Sistema: presupuestos con catálogo de precios calculados y factura electrónica a consumidor final vía ARCA.

## Contexto y Problema
Actualmente la empresa Óptica Sistema factura online ingresando al sitio de ARCA con el usuario y la contraseña de la empresa. Por otro lado, los presupuestos se realizan a mano: se escriben los productos y se colocan los precios que se buscan en un libro/folleto que envían los proveedores. Esto genera dos problemas: pueden equivocarse de producto y/o de precio, y además los precios del folleto son los precios de costo (lo que la empresa paga al proveedor), sin incluir el margen de ganancia, por lo que el cálculo del precio de venta también se hace a mano.

Este sistema lo usarán las dos dueñas del negocio, que a su vez son quienes trabajan todos los días en la empresa. Ellas necesitan poder hacer presupuestos online desde una página web, seleccionando los artículos desde un catálogo con precios de venta ya calculados, y que cada presupuesto quede guardado en una base de datos local. Además, esta versión incluye la facturación electrónica a consumidores finales mediante los web services de ARCA (ex AFIP), reemplazando la carga manual en el sitio web del organismo.

## Objetivos
- Tener un registro centralizado de los presupuestos: un solo lugar donde saben que están todos, con posibilidad de buscarlos si el cliente los olvida o los pide de nuevo.
- Evitar errores de precio y de escritura/transcripción de los productos, seleccionando los artículos desde un catálogo con precio de venta calculado automáticamente a partir del costo y el margen de utilidad.
- Poder generar el presupuesto en formato PDF para entregarlo o enviarlo manualmente por email o WhatsApp.
- Facturar electrónicamente a consumidores finales desde el sistema, integrándose con ARCA (ex AFIP), sin tener que cargar los datos manualmente en el sitio del organismo.

## Requerimientos Funcionales

### Presupuestos
- RF-01: El sistema debe permitir grabar un presupuesto nuevo.
- RF-02: El sistema debe permitir generar el presupuesto en formato PDF.
- RF-03: El sistema debe permitir buscar presupuestos por fecha y por datos del cliente (apellido, nombre, DNI).
- RF-04: El sistema debe manejar dos estados del presupuesto: Borrador y Final.
- RF-05: El sistema debe permitir cargar en el presupuesto los siguientes datos del cliente: Apellido, Nombre, DNI, Domicilio, Email y Nro. de Teléfono.
- RF-06: El sistema debe asignar a cada presupuesto, al grabarlo por primera vez, un número generado automáticamente igual al último número asignado más 1.
- RF-07: El sistema debe permitir modificar un presupuesto únicamente mientras se encuentre en estado Borrador.
- RF-08: El sistema no debe permitir modificar un presupuesto en estado Final.
- RF-09: El sistema no debe permitir descargar el PDF de un presupuesto cuyo estado sea Borrador.

### Líneas del presupuesto y cálculos
- RF-10: El sistema debe permitir cargar productos por línea en el presupuesto.
- RF-11: El sistema debe permitir buscar artículos del catálogo por código o por descripción al cargar una línea.
- RF-12: El sistema debe registrar en cada línea del presupuesto los siguientes campos: Código de Artículo, Descripción, Precio Unitario, Cantidad, Porcentaje de Descuento, Precio con Descuento y Precio Final.
- RF-13: El sistema debe calcular el precio con descuento de cada línea como: Precio Unitario × (1 − Porcentaje de Descuento / 100).
- RF-14: El sistema debe calcular el precio final de cada línea como el producto del precio con descuento por la cantidad.
- RF-15: El sistema debe calcular el total del presupuesto como la sumatoria de los precios finales, ya redondeados, de todas las líneas.
- RF-16: El sistema no debe admitir cantidades negativas ni iguales a cero.
- RF-17: El sistema no debe admitir precios unitarios negativos.
- RF-18: El sistema debe expresar con IVA incluido (precio final al consumidor) todos los precios que muestra en pantalla y en el presupuesto.
- RF-19: El sistema debe redondear los importes a 2 decimales usando redondeo estándar (mitad hacia arriba).

### Catálogo de artículos y precios
- RF-20: El sistema debe permitir cargar artículos con los siguientes campos: Código (autonumérico), Código en el proveedor, Descripción, Precio de Costo (precio final del folleto del proveedor, con IVA incluido) y Margen de Utilidad (%).
- RF-21: Cuando la condición fiscal configurada (RF-48) sea Responsable Inscripto, el sistema debe calcular el precio de venta de cada artículo en tres pasos: (a) Costo sin IVA = Precio de Costo / (1 + alícuota de IVA / 100); (b) Precio de Venta sin IVA = Costo sin IVA × (1 + Margen de Utilidad / 100); (c) Precio de Venta = Precio de Venta sin IVA × (1 + alícuota de IVA / 100). El margen y la alícuota se expresan en porcentaje (0 a 100), con el mismo criterio que RF-13. Cuando la alícuota de compra y la de venta coinciden, el resultado equivale a Precio de Costo × (1 + Margen de Utilidad / 100). Los pasos (a) y (b) se calculan sin redondear: solo se redondea el resultado final, según RF-56.
- RF-22: El sistema debe permitir cargar una planilla Excel con el formato predeterminado de RF-23 para actualizar los precios de costo de los artículos.
- RF-23: El sistema debe aceptar una planilla Excel con dos columnas: Código de artículo en el proveedor y Precio de Costo Nuevo (mismo criterio que RF-20: precio final del proveedor, con IVA incluido).
- RF-24: El sistema debe mostrar en pantalla la lista de los artículos que no pudo actualizar y la razón de cada caso (por ejemplo: código inexistente, precio inválido).

### Facturación electrónica a consumidor final (ARCA)
- RF-25: El sistema debe permitir generar una factura electrónica a consumidor final a partir de un presupuesto en estado Final, tomando de él los datos del cliente y las líneas de artículos.
- RF-26: El sistema debe emitir Factura B como comprobante a consumidor final cuando la condición fiscal configurada (RF-48) sea Responsable Inscripto.
- RF-27: El sistema debe identificar al receptor como "Consumidor Final" sin identificar cuando el total de la operación sea menor o igual al tope de identificación vigente (RF-49).
- RF-28: El sistema debe solicitar la autorización del comprobante (CAE) mediante el web service de facturación electrónica de ARCA (WSFEv1), utilizando el certificado digital de la empresa y el punto de venta habilitado para web services.
- RF-29: El sistema debe guardar el comprobante autorizado con su número (por punto de venta), CAE y fecha de vencimiento del CAE, asociado al presupuesto que le dio origen.
- RF-30: El sistema debe generar el PDF de la factura con los siguientes datos: razón social, domicilio, CUIT y condición frente al IVA del emisor; tipo de comprobante (B o C, según RF-26 y RF-76), punto de venta, número y fecha de emisión; identificación del receptor según RF-27 y RF-71; líneas con descripción, cantidad e importe; importe total; CAE; fecha de vencimiento del CAE; y código QR de ARCA. (Lista a validar con el contador de la óptica.)
- RF-31: El sistema no debe emitir el comprobante si ARCA rechaza la solicitud o si el servicio no está disponible.
- RF-32: El sistema no debe permitir modificar ni eliminar una factura emitida con CAE.
- RF-33: El sistema debe permitir listar las facturas emitidas.

### Interfaz
- RF-34: El sistema debe mostrar en su interfaz el logo y la paleta de colores de la óptica, definidos en `Marca/branding.json` (logo `Marca/logo.png`; color primario #0903A0 sobre fondo blanco #FFFFFF).
- RF-35: El sistema debe indicar, en todo error de validación, el campo afectado y la acción correctiva que debe tomar la operadora.

### Requisitos derivados
Los siguientes requisitos surgen de desagregar RF anteriores que reunían más de una acción, y de resolver las contradicciones y los vacíos detectados al auditar el documento. Se numeran a continuación de RF-35 para no alterar la numeración ya existente; cada uno indica su origen.

- RF-36: El sistema debe permitir descargar en la PC el PDF del presupuesto generado, para que la operadora lo adjunte manualmente en un email o en un mensaje de WhatsApp. (Deriva de RF-02.)
- RF-37: El sistema debe incluir en el PDF del presupuesto la leyenda "Precios finales, IVA incluido". (Deriva de RF-02; ver RF-18.)
- RF-38: El sistema debe guardar los datos del cliente dentro del propio presupuesto. (Deriva de RF-05.)
- RF-39: El sistema debe completar automáticamente el código, la descripción y el precio unitario de la línea al seleccionarse un artículo, tomando como precio unitario el precio de venta del catálogo. (Deriva de RF-11.)
- RF-40: El sistema debe permitir ajustar la cantidad y el porcentaje de descuento de la línea después de seleccionar el artículo. (Deriva de RF-11.)
- RF-41: El sistema debe mostrar el precio con descuento en todas las líneas, incluidas aquellas cuyo porcentaje de descuento es cero, en cuyo caso es igual al precio unitario. (Deriva de RF-13.)
- RF-42: El sistema no debe discriminar IVA en el presupuesto. (Deriva de RF-18.)
- RF-43: Cuando la condición fiscal configurada (RF-48) sea Responsable Inscripto, el sistema debe calcular el desglose de neto e IVA únicamente al emitir la factura electrónica, porque lo exige el web service de ARCA (RF-28). (Deriva de RF-18.)
- RF-44: El sistema debe aplicar el redondeo a nivel de línea, primero sobre el precio con descuento y luego sobre el precio final de la línea. (Deriva de RF-19.)
- RF-45: El sistema debe recalcular automáticamente el precio de venta de un artículo cuando se modifique su precio de costo o su margen de utilidad. (Deriva de RF-21.)
- RF-46: El sistema debe tomar la alícuota de IVA, expresada en porcentaje (por ejemplo, 21), de un único parámetro de configuración, aplicable a todo el catálogo y al desglose de neto e IVA de la factura (RF-43). (Deriva de RF-21.)
- RF-47: El sistema debe actualizar el precio de costo de cada artículo cuyo Código en el proveedor coincida con una fila de la planilla cargada, recalculando su precio de venta según RF-21 o RF-84. (Deriva de RF-23.)
- RF-48: El sistema debe tomar la condición fiscal de la óptica (Responsable Inscripto o Monotributo) de un parámetro de configuración. (Deriva de RF-26.)
- RF-49: El sistema debe tomar el tope de identificación del receptor de un parámetro configurable. (Deriva de RF-27.)
- RF-50: El sistema debe permitir descargar el PDF de la factura. (Deriva de RF-30.)
- RF-51: El sistema debe mostrar a la operadora el código y la descripción del error informados por ARCA cuando rechaza la solicitud. (Deriva de RF-31.)
- RF-52: El sistema debe permitir reintentar más tarde la emisión del comprobante. (Deriva de RF-31.)
- RF-53: El sistema debe dejar cada emisión en uno de dos estados finales: autorizada con CAE (RF-29) o no registrada. (Deriva de RF-31.)
- RF-54: El sistema debe permitir buscar las facturas emitidas por fecha, número de comprobante y datos del cliente. (Deriva de RF-33.)
- RF-55: El sistema debe incluir el logo y la paleta de colores de la óptica en los PDF generados de presupuesto y de factura. (Deriva de RF-34.)
- RF-56: El sistema debe redondear el precio de venta resultante de RF-21 o de RF-84 hacia arriba, hasta el múltiplo de redondeo comercial definido en la configuración, de modo que el precio de venta nunca quede por debajo del valor calculado y el margen cargado opere como piso. (Resuelve la contradicción entre RF-20 y RF-21.)
- RF-57: El sistema debe tomar el múltiplo de redondeo comercial de un parámetro de configuración. (Deriva de RF-56.)
- RF-58: El sistema debe guardar el precio de venta ya redondeado según RF-56. (Resuelve el alcance del redondeo entre RF-19 y RF-44.)
- RF-59: El sistema no debe admitir porcentajes de descuento menores a 0 ni mayores a 100. (Completa las validaciones de RF-16 y RF-17; evita el precio de línea negativo que resultaría de RF-13.)
- RF-60: El sistema debe admitir únicamente cantidades enteras en las líneas del presupuesto. (Completa RF-16.)
- RF-61: El sistema debe exigir Apellido, Nombre y DNI del cliente para grabar un presupuesto. (Completa RF-05 y da sustento a AC-34.)
- RF-62: El sistema no debe permitir emitir una factura a partir de un presupuesto en estado Borrador. (Hace explícita la prohibición que AC-17 deducía de RF-25; simétrica con RF-09.)
- RF-63: El sistema debe permitir administrar, desde una pantalla de configuración protegida por el acceso de RNF-04, los siguientes parámetros de negocio: alícuota de IVA (RF-46), condición fiscal (RF-48), tope de identificación del receptor (RF-49) y múltiplo de redondeo comercial (RF-57).
- RF-64: El sistema no debe exponer en su interfaz el certificado digital de la empresa ni el punto de venta habilitado para web services (RF-28): ambos se instalan y se resguardan fuera de la aplicación. (Sostiene la mitigación del riesgo de acceso no autorizado a datos fiscales.)
- RF-65: El sistema debe consultar a ARCA el último comprobante autorizado del punto de venta antes de reintentar una emisión que haya quedado sin respuesta (RF-52). (Un timeout no distingue si ARCA autorizó o no; sin esta consulta, RF-53 no puede cumplirse.)
- RF-66: El sistema no debe emitir un comprobante nuevo cuando la consulta de RF-65 indique que la operación ya fue autorizada. (Evita el comprobante duplicado, que no puede anularse desde el sistema porque las notas de crédito están fuera de alcance.)
- RF-67: El sistema no debe permitir devolver un presupuesto en estado Final al estado Borrador. (Deriva de RF-08.)
- RF-68: El sistema debe permitir seleccionar para la línea un artículo encontrado según RF-11. (Deriva de RF-11.)
- RF-69: El sistema debe calcular el Precio de Venta de cada artículo (precio final al consumidor, con IVA incluido) según RF-21 o RF-84, sin que la operadora lo cargue. (Deriva de RF-20.)
- RF-70: El sistema debe mostrar el Precio de Venta del artículo como un campo de solo lectura. (Deriva de RF-20.)
- RF-71: El sistema debe identificar al receptor con su DNI cuando el total de la operación supere el tope de identificación vigente (RF-49). (Deriva de RF-27.)
- RF-72: El sistema no debe admitir un múltiplo de redondeo comercial menor a 0,01, valor que equivale a redondear únicamente a 2 decimales según RF-19. (Deriva de RF-57.)
- RF-73: El sistema debe usar el precio de venta guardado, sin volver a calcularlo, como precio unitario de la línea del presupuesto (RF-39). (Deriva de RF-58.)
- RF-74: El sistema debe permitir grabar un presupuesto sin Domicilio, Email ni Nro. de Teléfono del cliente. (Deriva de RF-61.)
- RF-75: El sistema debe recuperar el CAE ya otorgado cuando la consulta de RF-65 indique que la operación ya fue autorizada, y guardarlo según RF-29. (Deriva de RF-66.)
- RF-76: El sistema debe emitir Factura C como comprobante a consumidor final cuando la condición fiscal configurada (RF-48) sea Monotributo. (Deriva de RF-48.)
- RF-77: El sistema debe avisar a la operadora, al ingresar, cuando el backup del día anterior no se haya ejecutado, indicando la fecha del último backup disponible. (Movido desde RNF-07: es un comportamiento de la aplicación, no una cualidad. Sin este aviso, un backup que dejó de correr solo se descubre cuando ya hace falta.)
- RF-78: El sistema debe rechazar completa, sin actualizar ningún artículo, una planilla Excel que no respete el formato de RF-23 (columnas faltantes, columnas de más o encabezados distintos). (Completa RF-23.)
- RF-79: El sistema debe informar a la operadora que el formato de la planilla no es el esperado cuando la rechace según RF-78. (Deriva de RF-78.)
- RF-80: El sistema debe considerar inválido un Precio de Costo Nuevo vacío o no numérico. Los valores negativos y el cero se aceptan (ver RF-93 y Riesgos). (Completa RF-24.)
- RF-81: El sistema debe combinar con "Y" los filtros cargados en la búsqueda de presupuestos y en la de facturas: solo muestra los registros que cumplen todos. (Completa RF-03 y RF-54.)
- RF-82: El sistema debe buscar el apellido y el nombre por coincidencia parcial (el dato guardado contiene el texto ingresado), sin distinguir mayúsculas ni acentos. (Completa RF-03 y RF-54.)
- RF-83: El sistema debe filtrar por fecha mediante un rango Desde/Hasta, con ambos límites inclusive, admitiendo que se cargue uno solo de los dos. (Completa RF-03 y RF-54.)
- RF-84: Cuando la condición fiscal configurada (RF-48) sea Monotributo, el sistema debe calcular el precio de venta de cada artículo como Precio de Costo × (1 + Margen de Utilidad / 100), sin descontar IVA del costo. (Deriva de RF-21 y RF-76.)
- RF-85: Cuando la condición fiscal configurada (RF-48) sea Monotributo, el sistema debe enviar a ARCA la Factura C solo con el importe total, sin desglose de neto e IVA. (Deriva de RF-43 y RF-76.)
- RF-86: El sistema debe recalcular y guardar el precio de venta de todos los artículos del catálogo cuando se grabe un cambio en la alícuota de IVA, la condición fiscal o el múltiplo de redondeo comercial. (Deriva de RF-63.)
- RF-87: El sistema no debe modificar los precios de las líneas de los presupuestos ya grabados cuando recalcule el catálogo según RF-86. (Deriva de RF-86.)
- RF-88: El sistema debe buscar el DNI y el número de comprobante por coincidencia parcial (el dato guardado contiene el texto ingresado), ignorando puntos y guiones. (Completa RF-03 y RF-54.)
- RF-89: El sistema debe registrar, antes de enviar cada solicitud a ARCA, el número de comprobante que solicita (último autorizado del punto de venta + 1). (Deriva de RF-65.)
- RF-90: El sistema debe considerar autorizada la emisión pendiente cuando, al reintentar, el número registrado según RF-89 figure autorizado en ARCA con el mismo importe total. (Completa RF-66 y RF-75.)
- RF-91: El sistema no debe emitir ni recuperar ningún comprobante cuando el número registrado según RF-89 figure autorizado en ARCA con un importe total distinto. (Deriva de RF-90.)
- RF-92: El sistema debe avisar a la operadora que revise el punto de venta en ARCA cuando se dé el caso de RF-91. (Deriva de RF-91.)
- RF-93: El sistema debe listar, junto con el resultado de RF-24, las filas de la planilla que actualizó con un Precio de Costo Nuevo negativo o igual a cero, con la indicación "actualizado con precio negativo o cero". (Mitiga el riesgo derivado de RF-80.)
- RF-94: El sistema debe comparar el Código en el proveedor sin distinguir mayúsculas de minúsculas e ignorando los espacios en blanco al principio y al final: por ejemplo, "ABC-1", "abc-1" y " ABC-1 " identifican al mismo artículo. (Completa RF-20 y RF-47.)

## Requerimientos No Funcionales
- RNF-01: La búsqueda de presupuestos debe responder en menos de 2 segundos en el percentil 95, con hasta 10.000 presupuestos almacenados. El tiempo se mide desde que la operadora presiona Buscar hasta que la grilla muestra los resultados en pantalla, no en la respuesta del API.
- RNF-02: El sistema debe realizar un backup automático diario de la base de datos en una ubicación distinta al disco principal (pendrive, carpeta sincronizada en la nube u otra), ejecutado todos los días a las 20:00, con la PC del local encendida.
- RNF-03: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge, tanto en su versión estable vigente como en la inmediata anterior.
- RNF-04: El acceso a la aplicación debe estar protegido por una contraseña de 8 caracteres como mínimo, dado que la base de datos almacena datos personales de clientes (DNI, domicilio, teléfono, email) alcanzados por la Ley 25.326 de Protección de Datos Personales.
- RNF-05: El sistema debe conservar las 7 copias diarias de backup más recientes y 4 copias mensuales. (Deriva de RNF-02; la retención es lo que cubre la corrupción detectada tarde, que la sola copia diaria sobreescrita no cubre.)
- RNF-06: La restauración de la base a partir de una copia de backup debe completarse en menos de 4 horas, siguiendo un procedimiento documentado, en una PC con el sistema instalado según ese procedimiento. (Deriva de RNF-02.)
- RNF-07: (Movido a RF-77: es un comportamiento de la aplicación, no una cualidad.)
- RNF-08: El sistema debe bloquear el acceso durante 5 minutos tras 5 intentos de ingreso fallidos consecutivos. El bloqueo debe ser temporal y liberarse solo, dado que no existe un módulo de usuarios con un administrador que pueda desbloquearlo. (Deriva de RNF-04.)
- RNF-09: El sistema debe cerrar la sesión tras 60 minutos de inactividad y volver a exigir la contraseña. (Deriva de RNF-04.)
- RNF-10: El sistema debe dar por no disponible el web service de ARCA tras 30 segundos sin respuesta, y no debe reintentar la emisión por su cuenta: el reintento lo decide la operadora (RF-52).
- RNF-11: El tiempo que el sistema agrega a la emisión de una factura, medido desde que la operadora presiona Facturar hasta el mensaje de confirmación y excluido el tiempo de espera de la respuesta de ARCA, debe ser menor a 2 segundos en el percentil 95. (No se fija un tiempo total porque la demora de ARCA no depende del sistema.)
- RNF-12: El sistema debe soportar un catálogo de hasta 10.000 artículos y procesar una planilla Excel de hasta 10.000 filas en menos de 120 segundos.
- RNF-13: El sistema debe soportar 2 sesiones simultáneas grabando presupuestos sin que ninguna reciba errores de bloqueo de la base de datos.
- RNF-14: El sistema debe guardar la contraseña de acceso únicamente como hash con sal (PBKDF2, bcrypt o equivalente), nunca en texto plano. (Deriva de RNF-04.)

## Criterios de Aceptación
- AC-01 (RF-01): Dado un presupuesto nuevo con Apellido, Nombre, DNI, Domicilio, Email y Nro. de Teléfono cargados y al menos 1 línea con un artículo, cuando presiono Grabar, entonces el sistema graba el presupuesto y muestra un mensaje que confirma que se grabó.
- AC-02 (RF-02, RF-09, RF-36, RF-55): Dado un presupuesto en estado Final, cuando presiono Descargar PDF, entonces debe generarse un PDF con el logo y colores de la óptica y descargarse en la PC.
- AC-03 (RF-03, RF-82): Dados presupuestos de clientes con apellido "González" y "Gómez", cuando busco por apellido el texto "ONZALEZ", entonces la grilla muestra solo el presupuesto de "González".
- AC-04 (RF-04): Dado un presupuesto nuevo con Apellido, Nombre y DNI cargados y al menos 1 línea, cuando presiono Grabar por primera vez, entonces el presupuesto queda en estado Borrador y el mensaje de confirmación indica ese estado.
- AC-05 (RF-04): Dado un presupuesto en estado Borrador, cuando cambio el estado a Final y grabo, entonces el sistema lo graba en estado Final.
- AC-06 (RF-06): Dado que el último presupuesto grabado tiene el número 154, cuando grabo un presupuesto nuevo por primera vez, entonces el sistema le asigna el número 155.
- AC-07 (RF-07): Dado un presupuesto en estado Borrador, cuando modifico su Domicilio y presiono Grabar, entonces el sistema graba el cambio, muestra un mensaje de confirmación y el nuevo Domicilio se ve al volver a abrir el presupuesto.
- AC-08 (RF-09): Dado un presupuesto en estado Borrador, cuando lo abro o pido su PDF directamente a la API, entonces el botón Descargar PDF está deshabilitado y la API rechaza el pedido sin generar el PDF.
- AC-09 (RF-09, RF-36): Dado un presupuesto en estado Final, cuando presiono Descargar PDF, entonces el PDF del presupuesto se descarga en la PC.
- AC-10 (RF-11, RF-39, RF-68): Dado que estoy cargando una línea del presupuesto, cuando busco un artículo por código o descripción y lo selecciono, entonces el sistema completa automáticamente el código, la descripción y el precio unitario con el precio de venta del catálogo.
- AC-11 (RF-15): Dado un presupuesto con una línea cuyo precio con descuento sin redondear es $3,335 y cantidad 3 (precio final de línea $10,02, según AC-29), cuando agrego una segunda línea con precio final $2.700,00, entonces el total del presupuesto es $2.710,02 (y no $2.710,01, que resultaría de sumar importes sin redondear).
- AC-12 (RF-16, RF-17, RF-35): Dada una línea del presupuesto, cuando ingreso una cantidad negativa o cero, o un precio unitario negativo, entonces el sistema no acepta el valor y muestra junto al campo afectado el mensaje "La cantidad debe ser un número entero mayor a 0" o "El precio unitario no puede ser negativo", según corresponda.
- AC-13 (RF-21, RF-57): Dado el múltiplo de redondeo comercial configurado en $0,01, cuando grabo un artículo con precio de costo $1.000 y margen de utilidad 50%, entonces el precio de venta guardado es $1.500,00.
- AC-14 (RF-22, RF-23, RF-45, RF-47, RF-56): Dados la alícuota de IVA en 21, el múltiplo de redondeo comercial en $0,01 y un artículo con Código en el proveedor "ABC-1", precio de costo $1.210 y margen de utilidad 50%, cuando cargo una planilla con el formato predeterminado que contiene la fila "ABC-1" con precio $2.420, entonces el precio de costo del artículo pasa a $2.420,00 y su precio de venta a $3.630,00.
- AC-15 (RF-24): Dada una planilla Excel que contiene un código de proveedor inexistente, cuando la proceso, entonces el sistema actualiza los artículos válidos y muestra en pantalla una lista con los artículos no actualizados y la razón.
- AC-16 (RF-25): Dado un presupuesto en estado Final con 3 líneas, cuando presiono Facturar, entonces la factura generada contiene las 3 líneas del presupuesto con sus importes, el importe total enviado a ARCA es igual al total del presupuesto y la operadora no vuelve a cargar ningún dato.
- AC-17 (RF-25, RF-62): Dado un presupuesto en estado Borrador, cuando lo abro o pido facturarlo directamente a la API, entonces el botón Facturar está deshabilitado y la API rechaza el pedido sin enviar ninguna solicitud a ARCA.
- AC-18 (RF-28, RF-29): Dado un presupuesto en estado Final, cuando presiono Facturar y ARCA autoriza el comprobante, entonces el sistema guarda la factura con número, CAE y vencimiento del CAE, la vincula al presupuesto de origen (cuyo número se muestra al consultar la factura) y muestra un mensaje de confirmación con el número de comprobante.
- AC-19 (RF-30, RF-50): Dada una factura autorizada, cuando presiono Descargar PDF, entonces se descarga un PDF que contiene todos los datos enumerados en RF-30, incluido el código QR de ARCA.
- AC-20 (RF-31, RF-51, RF-52, RF-53): Dado un presupuesto en estado Final, cuando presiono Facturar y ARCA rechaza la solicitud (por ejemplo, por datos inválidos), entonces el sistema no registra ninguna factura, muestra el código y la descripción del error devueltos por ARCA y permite reintentar. (La falta de respuesta de ARCA se cubre en AC-60, AC-64 y AC-65.)
- AC-21 (RF-71, RF-49): Dado un presupuesto cuyo total supera el tope configurado para identificar al receptor, cuando presiono Facturar, entonces el sistema envía a ARCA el DNI del cliente como identificación del receptor.
- AC-22 (RF-33, RF-54, RF-81, RF-82, RF-83): Dadas facturas emitidas a "González" el 10/03/2026 y el 20/03/2026, y a "Gómez" el 20/03/2026, cuando busco facturas con apellido "gonzalez" y fecha Desde 15/03/2026, entonces la grilla muestra solo la factura de "González" del 20/03/2026.
- AC-23 (RF-05, RF-38): Dado un presupuesto nuevo, cuando cargo Apellido, Nombre, DNI, Domicilio, Email y Nro. de Teléfono del cliente y grabo, entonces el sistema guarda esos seis datos dentro del presupuesto y los muestra al volver a abrirlo.
- AC-24 (RF-10): Dado un presupuesto en estado Borrador, cuando agrego una línea de producto y grabo, entonces la línea queda guardada en el presupuesto y se muestra en la grilla de líneas.
- AC-25 (RF-12): Dado un presupuesto con una línea cargada, cuando lo abro, entonces la grilla muestra para esa línea los siete campos: Código de Artículo, Descripción, Precio Unitario, Cantidad, Porcentaje de Descuento, Precio con Descuento y Precio Final.
- AC-26 (RF-13, RF-41): Dada una línea con precio unitario $1.000, cuando cargo un porcentaje de descuento igual a cero, entonces el precio con descuento se muestra y es $1.000,00.
- AC-27 (RF-14): Dada una línea con precio con descuento $900, cuando cargo cantidad 3, entonces el precio final de la línea es $2.700,00.
- AC-28 (RF-18, RF-42): Dado un presupuesto con líneas cargadas, cuando lo abro en pantalla, entonces ni las líneas ni el total discriminan IVA, y los importes mostrados corresponden al precio final al consumidor con IVA incluido.
- AC-29 (RF-19, RF-44): Dada una línea cuyo precio con descuento sin redondear es $3,335, cuando cargo cantidad 3, entonces el sistema muestra un precio con descuento de $3,34 (2 decimales, mitad hacia arriba) y un precio final de línea de $10,02, de modo que la multiplicación exhibida cierra.
- AC-30 (RF-20, RF-58, RF-69): Dado que cargo un artículo nuevo indicando Código en el proveedor, Descripción, Precio de Costo y Margen de Utilidad, cuando grabo, entonces el sistema asigna automáticamente el Código autonumérico y guarda el artículo con su Precio de Venta.
- AC-31 (RF-26, RF-48): Dada la condición fiscal de la óptica configurada como Responsable Inscripto, cuando emito una factura a consumidor final, entonces el comprobante solicitado a ARCA es Factura B.
- AC-32 (RF-32): Dada una factura ya autorizada con CAE, cuando la abro o intento modificarla o eliminarla por cualquier vía, entonces el sistema no ofrece acciones de edición ni de eliminación, rechaza el intento y la factura queda sin cambios.
- AC-33 (RF-34, RF-55): Dado el sistema con sesión iniciada, cuando abro cualquier pantalla o genero el PDF de un presupuesto o de una factura, entonces se muestra el logo `Marca/logo.png` y el color primario aplicado es #0903A0 sobre fondo #FFFFFF, tal como figuran en `Marca/branding.json`.
- AC-34 (RF-35): Dado un presupuesto nuevo con Apellido y Nombre cargados y sin DNI, cuando presiono Grabar, entonces el sistema no lo graba y muestra junto al campo DNI el mensaje "Ingresá el DNI del cliente".
- AC-35 (RF-13): Dada una línea con precio unitario $1.000, cuando cargo un porcentaje de descuento de 10%, entonces el precio con descuento calculado es $900,00.
- AC-36 (RF-21, RF-57): Dados la condición fiscal configurada como Responsable Inscripto, el múltiplo de redondeo comercial configurado en $0,01 y la alícuota de IVA en 21, cuando grabo un artículo con precio de costo $1.210 y margen de utilidad 50%, entonces el costo sin IVA es $1.000,00, el precio de venta sin IVA es $1.500,00 y el precio de venta final es $1.815,00.
- AC-37 (RF-02, RF-18, RF-37): Dado un presupuesto en estado Final, cuando descargo su PDF, entonces el documento muestra la leyenda "Precios finales, IVA incluido".
- AC-38 (RF-56, RF-57, RF-58): Dado el múltiplo de redondeo comercial configurado en $50 y un artículo cuyo precio de venta calculado según RF-21 es $1.815,37, cuando grabo el artículo, entonces el sistema guarda un precio de venta de $1.850,00.
- AC-39 (RF-56, RF-57, RF-58): Dado el múltiplo de redondeo comercial configurado en $0,01 y un artículo cuyo precio de venta calculado según RF-21 es $1.666,656, cuando grabo el artículo, entonces el sistema guarda un precio de venta de $1.666,66.
- AC-40 (RF-73): Dado un artículo cuyo precio de venta guardado es $1.850,00, cuando lo selecciono en una línea del presupuesto, entonces el precio unitario de la línea es $1.850,00, tomado tal cual del catálogo y sin volver a calcularlo.
- AC-41 (RF-27, RF-49): Dado un presupuesto cuyo total no supera el tope configurado, cuando presiono Facturar, entonces el sistema identifica al receptor ante ARCA como "Consumidor Final" sin identificar, aun cuando el presupuesto tenga DNI cargado.
- AC-42 (RF-59, RF-35): Dada una línea del presupuesto, cuando ingreso un porcentaje de descuento negativo o mayor a 100, entonces el sistema no acepta el valor y muestra junto al campo el mensaje "El descuento debe estar entre 0 y 100".
- AC-43 (RF-60, RF-35): Dada una línea del presupuesto, cuando ingreso una cantidad con decimales, por ejemplo 2,5, entonces el sistema no acepta el valor y muestra junto al campo el mensaje "La cantidad debe ser un número entero mayor a 0".
- AC-44 (RF-61): Dado un presupuesto sin Apellido, sin Nombre o sin DNI, cuando presiono Grabar, entonces el sistema no lo graba y muestra un mensaje que identifica el campo faltante.
- AC-45 (RF-74): Dado un presupuesto con Apellido, Nombre y DNI cargados y sin Domicilio, Email ni Nro. de Teléfono, cuando presiono Grabar, entonces el sistema lo graba sin reclamar esos tres campos.
- AC-46 (RF-40): Dada una línea con un artículo ya seleccionado, cuando modifico la cantidad y el porcentaje de descuento, entonces el sistema toma los nuevos valores y recalcula el precio con descuento y el precio final de la línea.
- AC-47 (RF-43): Dada la condición fiscal configurada como Responsable Inscripto y un presupuesto en estado Final cuyas líneas no discriminan IVA, cuando emito la factura, entonces el sistema calcula el neto y el IVA a partir de los importes finales y los envía discriminados a ARCA.
- AC-48 (RF-46): Dadas la condición fiscal configurada como Responsable Inscripto y la alícuota de IVA configurada en 21, cuando cargo un artículo y cuando emito una factura, entonces el sistema aplica esa misma alícuota tanto en el cálculo del precio de venta (RF-21) como en el desglose de neto e IVA (RF-43).
- AC-49 (RF-63): Dada la pantalla de configuración abierta con el acceso de RNF-04, cuando modifico el tope de identificación del receptor y grabo, entonces el sistema aplica el nuevo tope en la siguiente facturación, sin intervención técnica.
- AC-50 (RF-64): Dado el sistema con sesión iniciada, cuando recorro todas sus pantallas, entonces ninguna muestra ni permite editar el certificado digital de la empresa ni el punto de venta habilitado para web services.
- AC-51 (RNF-01): Dada una base con 10.000 presupuestos, cuando ejecuto 20 búsquedas por apellido, entonces al menos 19 muestran la grilla de resultados en menos de 2 segundos contados desde la pulsación de Buscar.
- AC-52 (RNF-02): Dado el sistema en operación y la PC del local encendida, cuando se cumplen las 20:00, entonces el sistema inicia el backup y, al terminar, existe en la ubicación de backup configurada una copia de la base con la fecha del día.
- AC-53 (RNF-05): Dado el sistema operando desde hace más de cuatro meses, cuando reviso la ubicación de backup, entonces contiene las 7 copias diarias más recientes y 4 copias mensuales.
- AC-54 (RNF-06): Dados una copia de backup, el procedimiento documentado y una PC con el sistema instalado según ese procedimiento, cuando restauro la base, entonces en menos de 4 horas desde el inicio de la restauración puedo abrir en el sistema un presupuesto que figura en la copia.
- AC-55 (RF-77): Dado que el backup del día anterior no se ejecutó, cuando ingreso al sistema, entonces se muestra un aviso que indica esa situación y la fecha del último backup disponible.
- AC-56 (RNF-03): Dado Chrome o Edge en su versión estable vigente o en la inmediata anterior, cuando ejecuto los criterios de aceptación de este documento en ese navegador, entonces todos pasan.
- AC-57 (RNF-04): Dada la pantalla de definición de la contraseña, cuando ingreso una contraseña de 7 caracteres, entonces el sistema no la acepta e indica que la longitud mínima es de 8 caracteres.
- AC-58 (RNF-08): Dada la pantalla de ingreso, cuando ingreso 5 veces seguidas una contraseña incorrecta, entonces el sistema bloquea el acceso durante 5 minutos (en ese lapso rechaza incluso la contraseña correcta) y, transcurridos los 5 minutos, vuelve a admitir el ingreso sin intervención de un administrador.
- AC-59 (RNF-09): Dada una sesión iniciada, cuando transcurren 60 minutos sin actividad, entonces el sistema cierra la sesión y exige ingresar nuevamente la contraseña.
- AC-60 (RNF-10): Dado que ARCA no responde, cuando presiono Facturar, entonces el sistema abandona la espera a los 30 segundos, no reintenta por su cuenta y muestra el motivo en pantalla.
- AC-61: (Eliminado en la segunda auditoría: RNF-11 ya no fija un tiempo total de emisión; el tiempo propio del sistema lo verifica AC-78.)
- AC-62 (RNF-12): Dado un catálogo de 10.000 artículos y una planilla Excel de 10.000 filas, cuando la proceso, entonces el sistema actualiza los precios de costo y recalcula los de venta en menos de 120 segundos.
- AC-63 (RNF-13, RF-06): Dadas dos sesiones abiertas al mismo tiempo, cuando ambas graban un presupuesto nuevo simultáneamente, entonces los dos presupuestos quedan grabados con números distintos y consecutivos, y ninguna sesión recibe un error de bloqueo de la base.
- AC-64 (RF-65, RF-66, RF-75, RF-89, RF-90): Dada una emisión que quedó sin respuesta de ARCA pero que en realidad fue autorizada con el número registrado y el mismo importe total, cuando la operadora reintenta, entonces el sistema detecta el comprobante ya autorizado, guarda su CAE y no emite uno nuevo.
- AC-65 (RF-65, RF-89): Dada una emisión que quedó sin respuesta de ARCA y que no llegó a autorizarse, cuando la operadora reintenta, entonces el sistema verifica que el número registrado no figura autorizado en ARCA y recién entonces envía el pedido.
- AC-66 (RF-08): Dado un presupuesto en estado Final, cuando intento modificar cualquiera de sus datos o líneas, entonces el sistema rechaza el cambio y el presupuesto queda sin modificaciones.
- AC-67 (RF-67): Dado un presupuesto en estado Final, cuando intento cambiar su estado a Borrador, entonces el sistema rechaza el cambio y el presupuesto sigue en estado Final.
- AC-68 (RF-81, RF-83): Dados dos presupuestos de clientes con apellido "González", uno del 10/03/2026 y otro del 20/03/2026, cuando busco por apellido "González" con fecha Desde 15/03/2026 y sin fecha Hasta, entonces la grilla muestra solo el presupuesto del 20/03/2026.
- AC-69 (RF-83): Dados presupuestos del 09/03/2026, 10/03/2026, 20/03/2026 y 21/03/2026, cuando busco con fecha Desde 10/03/2026 y Hasta 20/03/2026, entonces la grilla muestra solo los del 10/03/2026 y del 20/03/2026.
- AC-70 (RF-45): Dados la alícuota de IVA en 21, el múltiplo de redondeo comercial en $0,01 y un artículo con precio de costo $1.210, margen de utilidad 50% y precio de venta $1.815,00, cuando cambio el margen a 60% y grabo, entonces el precio de venta pasa a $1.936,00.
- AC-71 (RF-45): Dados la alícuota de IVA en 21, el múltiplo de redondeo comercial en $0,01 y un artículo con precio de costo $1.210, margen de utilidad 50% y precio de venta $1.815,00, cuando cambio a mano el precio de costo a $2.420 y grabo, entonces el precio de venta pasa a $3.630,00.
- AC-72 (RF-46, RF-63): Dada la pantalla de configuración con la alícuota de IVA en 21 y el múltiplo de redondeo comercial en $0,01, cuando cambio la alícuota a 10,5 y luego grabo un artículo con precio de costo $1.105 y margen de utilidad 50%, entonces el precio de venta guardado es $1.657,50.
- AC-73 (RF-48, RF-63, RF-76): Dada la pantalla de configuración con la condición fiscal en Responsable Inscripto, cuando la cambio a Monotributo y emito una factura a consumidor final, entonces el comprobante solicitado a ARCA es Factura C.
- AC-74 (RF-57, RF-63): Dada la pantalla de configuración con el múltiplo de redondeo comercial en $0,01, cuando lo cambio a $50 y luego grabo un artículo cuyo precio de venta calculado según RF-21 es $1.815,37, entonces el sistema guarda un precio de venta de $1.850,00.
- AC-75 (RF-24, RF-80): Dada una planilla con el formato predeterminado que contiene una fila con precio vacío, otra con el precio "abc" y otras filas válidas, cuando la proceso, entonces el sistema actualiza las filas válidas y lista las dos filas mencionadas como no actualizadas con la razón "precio inválido".
- AC-76 (RF-78, RF-79): Dada una planilla con una tercera columna además de las dos del formato predeterminado, cuando la proceso, entonces el sistema no actualiza ningún artículo y muestra un mensaje que indica que el formato de la planilla no es el esperado.
- AC-77 (RF-27, RF-49): Dado un presupuesto cuyo total es exactamente igual al tope configurado, cuando presiono Facturar, entonces el sistema identifica al receptor ante ARCA como "Consumidor Final" sin identificar.
- AC-78 (RNF-11): Dado ARCA respondiendo sin error, cuando emito 20 facturas registrando en cada una el tiempo total y el tiempo de espera de la respuesta de ARCA, entonces en al menos 19 la diferencia entre ambos es menor a 2 segundos.
- AC-79 (RNF-04): Dado que no inicié sesión, cuando abro cualquier pantalla del sistema o llamo a cualquier endpoint de la API, entonces el sistema me redirige a la pantalla de ingreso o responde 401, sin devolver datos de clientes, presupuestos, facturas ni configuración.
- AC-80 (RNF-14): Dada una contraseña de acceso ya definida, cuando inspecciono la base de datos y los archivos de configuración del sistema, entonces la contraseña no aparece en texto plano en ninguno de ellos.
- AC-81 (RF-70): Dado un artículo grabado, cuando lo abro para editarlo, entonces el campo Precio de Venta se muestra y no permite modificar su valor.
- AC-82 (RF-72): Dada la pantalla de configuración, cuando ingreso un múltiplo de redondeo comercial de 0,001, entonces el sistema no lo acepta e indica que el valor mínimo es 0,01.
- AC-83 (RF-84): Dados la condición fiscal configurada como Monotributo y el múltiplo de redondeo comercial en $0,01, cuando grabo un artículo con precio de costo $1.210 y margen de utilidad 50%, entonces el precio de venta guardado es $1.815,00.
- AC-84 (RF-76, RF-85): Dados la condición fiscal configurada como Monotributo y un presupuesto en estado Final con total $1.815,00, cuando emito la factura, entonces la solicitud enviada a ARCA es de Factura C, con importe total $1.815,00 y sin desglose de neto e IVA.
- AC-85 (RF-86): Dados el múltiplo de redondeo comercial en $0,01 y un artículo con precio de costo $1.210, margen de utilidad 50% y precio de venta guardado $1.815,00, cuando cambio el múltiplo a $50 en la pantalla de configuración y grabo, entonces el precio de venta guardado del artículo pasa a $1.850,00 sin que se edite el artículo.
- AC-86 (RF-87): Dado un presupuesto grabado con una línea de precio unitario $1.815,00, cuando el catálogo se recalcula según RF-86 y el artículo pasa a $1.850,00, entonces la línea del presupuesto conserva el precio unitario $1.815,00.
- AC-87 (RF-88): Dados un presupuesto y una factura de un cliente con DNI 23.456.789, cuando busco por DNI el texto "3456", entonces la grilla de presupuestos y la de facturas muestran los registros de ese cliente.
- AC-88 (RF-88): Dada una factura con número de comprobante 0003-00034561, cuando busco facturas por número con el texto "34561", entonces la grilla muestra esa factura.
- AC-89 (RF-91, RF-92): Dada una emisión que quedó sin respuesta de ARCA, cuando la operadora reintenta y el número registrado figura autorizado en ARCA con un importe total distinto, entonces el sistema no emite ni recupera ningún comprobante y muestra un aviso para revisar el punto de venta en ARCA.
- AC-90 (RF-80, RF-93): Dada una planilla con el formato predeterminado que contiene una fila con precio -100 y otra con precio 0 para artículos existentes, cuando la proceso, entonces el sistema actualiza ambos artículos y los lista con la indicación "actualizado con precio negativo o cero".
- AC-91 (RF-47, RF-94): Dado un artículo con Código en el proveedor "ABC-1", cuando cargo una planilla con el formato predeterminado que contiene la fila "abc-1" con precio $2.420, entonces el sistema actualiza el precio de costo de ese artículo a $2.420,00 y no lo lista como no actualizado.
- AC-92 (RF-47, RF-94): Dado un artículo con Código en el proveedor "ABC-1", cuando cargo una planilla con el formato predeterminado que contiene la fila " ABC-1 " (con espacios al principio y al final) con precio $2.420, entonces el sistema actualiza el precio de costo de ese artículo a $2.420,00 y no lo lista como no actualizado.

## Fuera de Alcance
- Facturas A (a responsables inscriptos con CUIT) y comprobantes distintos de la factura a consumidor final: notas de crédito, notas de débito y anulaciones. Si una factura emitida debe corregirse, en esta versión se resuelve por fuera del sistema.
- Envío automático de emails o mensajes de WhatsApp desde el sistema. En esta versión, la operadora descarga el PDF y lo adjunta manualmente.
- Módulo de usuarios (ABM de usuarios y roles). Solo se contempla el acceso con contraseña de RNF-04.
- Módulo/ABM de clientes, incluido el historial de presupuestos y facturas por cliente. Los datos del cliente se cargan directamente en cada presupuesto y no se persisten como entidad independiente.
- Eliminación de presupuestos, incluidos los que están en estado Borrador.
- Control de stock: el sistema no lleva existencias de artículos.
- Registro de cobros, señas y medios de pago.
- Impresión directa desde el sistema: presupuestos y facturas se imprimen desde el PDF descargado.

Todos los puntos anteriores quedan como pendientes para una versión futura.

## Riesgos y Dependencias
- Riesgo: La IA que genere el código puede alucinar o interpretar mal los requerimientos. Mitigación: criterios de aceptación explícitos y verificables.
- Riesgo: Fallas en la conexión a internet. Mitigación: la base de datos es local (SQLite), por lo que presupuestos y catálogo operan sin conectividad; la facturación sí requiere conexión y, en caso de corte, se reintenta cuando se restablece (RF-52).
- Riesgo: Indisponibilidad temporal de los web services de ARCA. Mitigación: el sistema informa el error, no deja comprobantes en estado intermedio y permite reintentar (RF-31, RF-51, RF-52, RF-53).
- Riesgo: Cambios normativos de ARCA (topes de identificación del receptor, formato del comprobante). Mitigación: los topes son parámetros configurables (RF-49) y la integración se aísla en un módulo propio para facilitar su actualización.
- Riesgo: Pérdida de datos por falla del disco de la PC del local (presupuestos, facturas, catálogo y datos personales de clientes). Mitigación: backup automático diario en ubicación externa (RNF-02).
- Riesgo: Acceso no autorizado a datos personales de clientes y a datos fiscales de la empresa (certificado digital, claves) almacenados localmente. Mitigación: acceso con contraseña (RNF-04) guardada solo como hash (RNF-14), sistema accesible únicamente desde la PC o la red local del negocio, resguardo del certificado en ubicación protegida y fuera del alcance de la interfaz (RF-64) y, a futuro, módulo de usuarios con roles.
- Riesgo: Un Precio de Costo Nuevo negativo o igual a cero importado por planilla (RF-80) produce un precio de venta negativo o cero, en conflicto con RF-17 (no se admiten precios unitarios negativos). Aceptar esos valores fue una decisión explícita del responsable del proyecto. Mitigación: el sistema lista esas filas para que la operadora las revise (RF-93); además, RF-17 impide usar en un presupuesto un artículo con precio de venta negativo.
- Dependencia: PC del local donde corre la aplicación, accesible únicamente desde esa PC o la red local del negocio; el sistema no se publica en internet.
- Dependencia: SQLite como motor de base de datos.
- Dependencia: Certificado digital de la empresa emitido por ARCA y asociado al servicio de facturación electrónica (WSFEv1).
- Dependencia: Punto de venta habilitado en ARCA para facturación por web services (distinto del punto de venta del facturador online manual).
- Dependencia: Entorno de homologación (testing) de ARCA para probar la integración antes de emitir comprobantes reales.

