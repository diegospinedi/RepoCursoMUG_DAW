using System.Diagnostics;
using System.Globalization;
using Optica.Application.Comun;
using Optica.Domain.Comun;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Presupuestos;

/// <summary>
/// Traduce las infracciones de <see cref="PoliticaCliente"/> y los rechazos de las líneas a un
/// <see cref="ErrorValidacion"/> con el campo de la Api (D-20) y la acción que lo corrige (D-24). Las acciones son texto
/// fijo: nunca repiten el valor recibido (D-17).
/// </summary>
/// <remarks>
/// Los textos de largo y de caracteres no válidos repiten los del catálogo sin depender de
/// <c>Optica.Application.Catalogo</c> (D-08): Presupuestos puede usar Catálogo, pero estas constantes son propias para que
/// un cambio de texto en el catálogo no cambie el del presupuesto sin una prueba que lo note.
/// </remarks>
public static class ValidacionPresupuesto
{
    public const string CampoApellido = "apellido";
    public const string CampoNombre = "nombre";
    public const string CampoDni = "dni";
    public const string CampoDomicilio = "domicilio";
    public const string CampoEmail = "email";
    public const string CampoTelefono = "telefono";
    public const string CampoLineas = "lineas";

    /// <summary>La versión leída del presupuesto, en la modificación y en el paso a Final (D-01 de FEAT-003c).</summary>
    public const string CampoVersion = "version";

    /// <summary>El estado pedido en el paso a Final (D-05 de FEAT-003c).</summary>
    public const string CampoEstado = "estado";

    /// <summary>Versión faltante o fuera de 0 a <see cref="Presupuesto.VersionMaxima"/> (D-07 de FEAT-003c).</summary>
    public const string AccionVersionInvalida = "Volvé a abrir el presupuesto para editarlo.";

    /// <summary>
    /// Un <c>estado</c> que no es <see cref="PresupuestoGrabado.EstadoFinal"/>, o "borrador" sobre un Borrador
    /// (D-05, D-10 de FEAT-003c).
    /// </summary>
    public const string AccionEstadoInvalido = "El presupuesto solo puede pasar a Final.";

    /// <summary>Un <c>orden</c> repetido, menor que 1, inexistente o de una línea con otro código (D-03 de FEAT-003c).</summary>
    public const string AccionLineaAjena = "La línea no corresponde a este presupuesto.";

    /// <summary>El <c>detail</c> del 409 <c>cambiado</c>: otra sesión grabó el presupuesto (D-01, D-07 de FEAT-003c).</summary>
    public const string DetalleCambiado = "El presupuesto cambió mientras lo editabas. Volvé a abrirlo.";

    /// <summary>El <c>detail</c> del 409 <c>final</c>: un presupuesto en Final no se modifica (FR-05, D-07 de FEAT-003c).</summary>
    public const string DetalleFinal = "El presupuesto está en Final y no se puede modificar.";

    public const string AccionApellidoFaltante = "Ingresá el apellido del cliente";
    public const string AccionNombreFaltante = "Ingresá el nombre del cliente";

    /// <summary>El texto de AC-02, tal cual lo fija el PRD.</summary>
    public const string AccionDniFaltante = "Ingresá el DNI del cliente";

    public const string AccionDniInvalido = "El DNI debe tener 7 u 8 dígitos";
    public const string AccionEmailInvalido = "El email debe tener el formato usuario@dominio.extensión";
    public const string AccionSinLineas = "Agregá al menos una línea al presupuesto";
    public const string AccionArticuloNoDisponible = "Ese artículo ya no está disponible. Quitá la línea y buscalo de nuevo";

    /// <summary>El texto de AC-16, tal cual lo fija el PRD.</summary>
    public const string AccionPrecioNegativo = "El precio unitario no puede ser negativo";

    public const string AccionCaracteresInvalidos = "El texto tiene caracteres no válidos. Escribilo de nuevo.";
    public const string AccionApellidoLargo = "Usá hasta 100 caracteres.";
    public const string AccionNombreLargo = "Usá hasta 100 caracteres.";
    public const string AccionDomicilioLargo = "Usá hasta 200 caracteres.";
    public const string AccionEmailLargo = "Usá hasta 254 caracteres.";
    public const string AccionTelefonoLargo = "Usá hasta 30 caracteres.";

    /// <summary>Más de <see cref="LineasMaximas"/> líneas (D-16).</summary>
    public const string AccionDemasiadasLineas = "Usá hasta 100 líneas.";

    /// <summary>Líneas que admite un presupuesto (D-16).</summary>
    public const int LineasMaximas = 100;

    /// <summary>El código de artículo más alto que se acepta: 2^53 − 1, el entero exacto más grande de JavaScript.</summary>
    public const long CodigoArticuloMaximo = 9_007_199_254_740_991;

    /// <summary>El texto de AC-02, tal cual lo fija el PRD: cantidad ausente, no entera o menor que 1.</summary>
    public const string AccionCantidadInvalida = "La cantidad debe ser un número entero mayor a 0";

    /// <summary>Cantidad mayor que <see cref="CalculoLinea.CantidadMaxima"/> (AC-09, D-01).</summary>
    public const string AccionCantidadMaxima = "Usá hasta 9.999 unidades.";

    /// <summary>El texto de AC-03, tal cual lo fija el PRD: descuento ausente o fuera de 0..100 (D-02).</summary>
    public const string AccionDescuentoFueraDeRango = "El descuento debe estar entre 0 y 100";

    /// <summary>Precio Final de la línea por encima de <see cref="ImporteMaximoContrato"/> (D-04).</summary>
    public const string AccionImporteLineaExcedido = "El precio final de la línea es demasiado alto.";

    /// <summary>Total del presupuesto por encima de <see cref="ImporteMaximoContrato"/> (D-04).</summary>
    public const string AccionTotalExcedido = "El total del presupuesto es demasiado alto.";

    /// <summary>
    /// El importe más alto que admite el contrato con la web: <c>Number.MAX_SAFE_INTEGER / 100</c>, el mayor número de
    /// centésimos que JavaScript lee exacto (D-04, ADR-016). Es un límite técnico, como
    /// <see cref="CodigoArticuloMaximo"/>, y más bajo que el límite del tipo del dominio
    /// (<c>ReglasImporte.ImporteMaximo</c>).
    /// </summary>
    public const decimal ImporteMaximoContrato = 90_071_992_547_409.91m;

    /// <summary>El campo de la cantidad de la línea <paramref name="indice"/>: <c>lineas[i].cantidad</c> (D-06).</summary>
    public static string CampoCantidad(int indice) =>
        string.Create(CultureInfo.InvariantCulture, $"{CampoLinea(indice)}.cantidad");

    /// <summary>
    /// El campo del descuento de la línea <paramref name="indice"/>: <c>lineas[i].porcentajeDescuento</c> (D-06).
    /// </summary>
    public static string CampoDescuento(int indice) =>
        string.Create(CultureInfo.InvariantCulture, $"{CampoLinea(indice)}.porcentajeDescuento");

    /// <summary>
    /// Los topes de importe del contrato, evaluados en un solo lugar (D-04, D-11): cada línea cuyo Precio Final supera
    /// <see cref="ImporteMaximoContrato"/>, o que el dominio rechazó con <see cref="ResultadoLinea.ImporteExcedido"/>,
    /// da <see cref="AccionImporteLineaExcedido"/> en <c>lineas[i].cantidad</c>; si todas entran y la suma de sus
    /// Precios Finales supera el tope, <see cref="AccionTotalExcedido"/> en <c>lineas</c>. Vacío si se puede grabar.
    /// </summary>
    /// <remarks>
    /// Con todas las líneas dentro del tope, la suma también entra en el límite del tipo del dominio: el constructor de
    /// <see cref="Presupuesto"/> no la rechaza después.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="resultados"/> o alguno de ellos es null.</exception>
    /// <exception cref="ArgumentException">
    /// Algún resultado es un rechazo que no es de importe (inactivo, precio negativo): se traduce antes de controlar los
    /// topes.
    /// </exception>
    public static IReadOnlyList<ErrorValidacion> ControlarTopes(IReadOnlyList<ResultadoLinea> resultados)
    {
        ArgumentNullException.ThrowIfNull(resultados);
        var errores = new List<ErrorValidacion>();
        var total = 0m;
        for (var indice = 0; indice < resultados.Count; indice++)
        {
            var resultado = resultados[indice] ?? throw new ArgumentNullException(nameof(resultados));
            if (resultado.EsValida && resultado.Linea.PrecioFinal <= ImporteMaximoContrato)
            {
                // Cada sumando es como máximo ImporteMaximoContrato (~9 × 10^13): la suma entra en decimal sin desbordar.
                total += resultado.Linea.PrecioFinal;
            }
            else if (resultado.EsValida || resultado.EsImporteExcedido)
            {
                errores.Add(new ErrorValidacion(CampoCantidad(indice), AccionImporteLineaExcedido));
            }
            else
            {
                throw new ArgumentException("Hay un rechazo de línea que no es de importe.", nameof(resultados));
            }
        }

        // Con una línea por encima ya no se graba, y la suma deja de ser un importe que la web lea: no se informa.
        if (errores.Count == 0 && total > ImporteMaximoContrato)
        {
            errores.Add(new ErrorValidacion(CampoLineas, AccionTotalExcedido));
        }

        return errores;
    }

    /// <summary>El campo de la línea <paramref name="indice"/>, desde 0: <c>lineas[i]</c> (D-20).</summary>
    public static string CampoLinea(int indice) =>
        string.Create(CultureInfo.InvariantCulture, $"{CampoLineas}[{indice}]");

    /// <summary>El error de una línea cuyo artículo no existe, está inactivo o no se identifica (D-14).</summary>
    public static ErrorValidacion ArticuloNoDisponible(int indice) => new(CampoLinea(indice), AccionArticuloNoDisponible);

    /// <summary>El error de una línea cuyo artículo tiene precio de venta negativo (FR-12, AC-16).</summary>
    public static ErrorValidacion PrecioNegativo(int indice) => new(CampoLinea(indice), AccionPrecioNegativo);

    /// <summary>
    /// Evalúa el cliente con <see cref="PoliticaCliente"/> y la forma de las líneas, sin leer la base, y devuelve todos
    /// los errores: primero los del cliente, uno por campo como máximo, en el orden apellido, nombre, DNI, domicilio,
    /// email, teléfono; después los de las líneas: sin líneas o más de <see cref="LineasMaximas"/> en <c>lineas</c>, o
    /// por cada línea, en este orden, el código faltante, no positivo o mayor que <see cref="CodigoArticuloMaximo"/> en
    /// <c>lineas[i]</c>, la cantidad en <c>lineas[i].cantidad</c> (D-01) y el descuento en
    /// <c>lineas[i].porcentajeDescuento</c> (D-02). Un cliente ausente cuenta como seis datos ausentes, y una línea
    /// ausente, como sus tres datos ausentes. Vacío si los datos se pueden grabar.
    /// </summary>
    public static IReadOnlyList<ErrorValidacion> Validar(DatosPresupuesto datos) => ValidarDatos(datos, conOrden: false);

    /// <summary>
    /// El error de una versión faltante o fuera de 0 a <see cref="Presupuesto.VersionMaxima"/>, en <c>version</c>
    /// (D-01, D-07 de FEAT-003c); null si es válida.
    /// </summary>
    public static ErrorValidacion? ValidarVersion(long? version) =>
        version is >= 0 and <= Presupuesto.VersionMaxima ? null : new ErrorValidacion(CampoVersion, AccionVersionInvalida);

    /// <summary>
    /// Un <c>estado</c> que no se puede pedir: cualquiera distinto de "final", o "borrador" sobre un Borrador (D-10 de
    /// FEAT-003c).
    /// </summary>
    public static ErrorValidacion EstadoInvalido { get; } = new(CampoEstado, AccionEstadoInvalido);

    /// <summary>El error de la línea <paramref name="indice"/> cuyo <c>orden</c> no corresponde al presupuesto (D-03).</summary>
    public static ErrorValidacion LineaAjena(int indice) => new(CampoLinea(indice), AccionLineaAjena);

    /// <summary>
    /// La validación de la modificación sin leer la base (FEAT-003c, D-10): primero la versión, después el cliente y las
    /// líneas con las reglas y los textos del alta (<see cref="Validar(DatosPresupuesto)"/>) y, en cada línea, la forma
    /// de su <c>orden</c>: si lo trae, tiene que ser al menos 1 y no repetir el de una línea anterior; si no, va
    /// <see cref="AccionLineaAjena"/> en <c>lineas[i]</c>, salvo que esa línea ya tenga un error en ese campo. Que el
    /// <c>orden</c> exista y tenga el mismo código lo decide <see cref="LineaGrabadaDe"/>, después de comparar la versión.
    /// Vacío si los datos se pueden grabar.
    /// </summary>
    public static IReadOnlyList<ErrorValidacion> ValidarModificacion(DatosPresupuesto datos, long? version)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var errores = new List<ErrorValidacion>();
        if (ValidarVersion(version) is { } errorVersion)
        {
            errores.Add(errorVersion);
        }

        errores.AddRange(ValidarDatos(datos, conOrden: true));
        return errores;
    }

    /// <summary>
    /// La validación del paso a Final sin leer la base (FEAT-003c, D-05, D-10): un <c>estado</c> que no es "final" ni
    /// "borrador" (comparado tal cual, en minúsculas) es <see cref="EstadoInvalido"/>, y después la versión. "borrador"
    /// pasa esta validación: si es un error (400) o un presupuesto en Final (409) lo decide el caso de uso según el estado
    /// leído. Vacío si se puede seguir.
    /// </summary>
    public static IReadOnlyList<ErrorValidacion> ValidarPasoAFinal(string? estado, long? version)
    {
        var errores = new List<ErrorValidacion>();
        if (estado is not (PresupuestoGrabado.EstadoFinal or PresupuestoGrabado.EstadoBorrador))
        {
            errores.Add(EstadoInvalido);
        }

        if (ValidarVersion(version) is { } errorVersion)
        {
            errores.Add(errorVersion);
        }

        return errores;
    }

    /// <summary>
    /// La validación semántica del <c>orden</c> (D-03), contra las líneas grabadas que leyó el caso de uso dentro de su
    /// transacción: la línea grabada en la posición <paramref name="orden"/> − 1 si existe y tiene el mismo
    /// <paramref name="codigoArticulo"/>; si no, null (una línea ajena).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="grabadas"/> es null.</exception>
    public static LineaPresupuesto? LineaGrabadaDe(int orden, long codigoArticulo, IReadOnlyList<LineaPresupuesto> grabadas)
    {
        ArgumentNullException.ThrowIfNull(grabadas);
        if (orden < 1 || orden > grabadas.Count)
        {
            return null;
        }

        var grabada = grabadas[orden - 1];
        return grabada.CodigoArticulo == codigoArticulo ? grabada : null;
    }

    /// <summary>
    /// <see cref="Validar(DatosPresupuesto)"/>; con <paramref name="conOrden"/>, además la forma del <c>orden</c> de cada
    /// línea (solo la modificación: el alta no lo usa).
    /// </summary>
    private static List<ErrorValidacion> ValidarDatos(DatosPresupuesto datos, bool conOrden)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var cliente = datos.Cliente;
        var errores = PoliticaCliente
            .Evaluar(cliente?.Apellido, cliente?.Nombre, cliente?.Dni, cliente?.Domicilio, cliente?.Email,
                cliente?.Telefono)
            .Select(Traducir)
            .ToList();

        var lineas = datos.Lineas;
        if (lineas is null || lineas.Count == 0)
        {
            errores.Add(new ErrorValidacion(CampoLineas, AccionSinLineas));
        }
        else if (lineas.Count > LineasMaximas)
        {
            errores.Add(new ErrorValidacion(CampoLineas, AccionDemasiadasLineas));
        }
        else
        {
            var ordenes = new HashSet<int>();
            for (var indice = 0; indice < lineas.Count; indice++)
            {
                var cantidadAntes = errores.Count;
                ValidarLinea(lineas[indice], indice, errores);
                if (conOrden && lineas[indice]?.Orden is { } orden)
                {
                    // Solo la primera aparición de un orden es la línea grabada; las repetidas son ajenas (D-03).
                    var repetido = !ordenes.Add(orden);
                    var campo = CampoLinea(indice);
                    var yaTieneError = errores.Skip(cantidadAntes).Any(error => error.Campo == campo);
                    if ((orden < 1 || repetido) && !yaTieneError)
                    {
                        errores.Add(LineaAjena(indice));
                    }
                }
            }
        }

        return errores;
    }

    /// <summary>
    /// Los errores de la línea <paramref name="indice"/>, en el orden artículo, cantidad, descuento. Una línea ausente
    /// cuenta como sus tres datos ausentes, como el cliente ausente. La regla de cantidad y descuento es la del dominio
    /// (<see cref="CalculoLinea"/>); acá solo se elige el campo y el texto de cada rechazo.
    /// </summary>
    private static void ValidarLinea(DatosLinea? linea, int indice, List<ErrorValidacion> errores)
    {
        if (linea?.CodigoArticulo is not (>= 1 and <= CodigoArticuloMaximo))
        {
            errores.Add(ArticuloNoDisponible(indice));
        }

        if (AccionDeLaCantidad(linea?.Cantidad) is { } accionCantidad)
        {
            errores.Add(new ErrorValidacion(CampoCantidad(indice), accionCantidad));
        }

        if (AccionDelDescuento(linea?.PorcentajeDescuento) is { } accionDescuento)
        {
            errores.Add(new ErrorValidacion(CampoDescuento(indice), accionDescuento));
        }
    }

    /// <summary>Ausente o menor que el mínimo, AC-02; mayor que el máximo, AC-09 (D-01); si no, null.</summary>
    private static string? AccionDeLaCantidad(int? cantidad) => cantidad switch
    {
        null or < CalculoLinea.CantidadMinima => AccionCantidadInvalida,
        > CalculoLinea.CantidadMaxima => AccionCantidadMaxima,
        _ => null,
    };

    /// <summary>Primero el rango (ausente o fuera de 0..100, AC-03), después la escala (D-02); si no, null.</summary>
    private static string? AccionDelDescuento(decimal? descuento) => descuento switch
    {
        null or < CalculoLinea.DescuentoMinimo or > CalculoLinea.DescuentoMaximo => AccionDescuentoFueraDeRango,
        { } valor when ReglasImporte.TieneMasDeDosDecimales(valor) => AccionesNumericas.HastaDosDecimales,
        _ => null,
    };

    /// <summary>El campo y la acción de una infracción del cliente.</summary>
    /// <exception cref="UnreachableException">
    /// La infracción no tiene traducción: el dominio sumó un valor y falta agregarlo aquí.
    /// </exception>
    public static ErrorValidacion Traducir(InfraccionCliente infraccion) => infraccion switch
    {
        InfraccionCliente.ApellidoFaltante => new(CampoApellido, AccionApellidoFaltante),
        InfraccionCliente.ApellidoConCaracteresInvalidos => new(CampoApellido, AccionCaracteresInvalidos),
        InfraccionCliente.ApellidoDemasiadoLargo => new(CampoApellido, AccionApellidoLargo),
        InfraccionCliente.NombreFaltante => new(CampoNombre, AccionNombreFaltante),
        InfraccionCliente.NombreConCaracteresInvalidos => new(CampoNombre, AccionCaracteresInvalidos),
        InfraccionCliente.NombreDemasiadoLargo => new(CampoNombre, AccionNombreLargo),
        InfraccionCliente.DniFaltante => new(CampoDni, AccionDniFaltante),
        InfraccionCliente.DniInvalido => new(CampoDni, AccionDniInvalido),
        InfraccionCliente.DomicilioConCaracteresInvalidos => new(CampoDomicilio, AccionCaracteresInvalidos),
        InfraccionCliente.DomicilioDemasiadoLargo => new(CampoDomicilio, AccionDomicilioLargo),
        // D-28: un email con UTF-16 inválido lleva el texto del catálogo, no el del formato.
        InfraccionCliente.EmailConCaracteresInvalidos => new(CampoEmail, AccionCaracteresInvalidos),
        InfraccionCliente.EmailDemasiadoLargo => new(CampoEmail, AccionEmailLargo),
        InfraccionCliente.EmailInvalido => new(CampoEmail, AccionEmailInvalido),
        InfraccionCliente.TelefonoConCaracteresInvalidos => new(CampoTelefono, AccionCaracteresInvalidos),
        InfraccionCliente.TelefonoDemasiadoLargo => new(CampoTelefono, AccionTelefonoLargo),
        _ => throw new UnreachableException("Infracción de cliente sin campo ni acción asignados."),
    };
}
