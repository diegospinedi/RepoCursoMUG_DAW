using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Optica.Domain.Configuracion;

namespace Optica.Domain.Catalogo;

/// <summary>
/// Una celda de la planilla de costos tal como la entrega el lector (D-07 de FEAT-011): vacía, numérica, de texto u
/// otro tipo (booleano, error, fecha). El dominio decide qué significa cada una; el lector solo la tipa.
/// </summary>
/// <remarks>
/// El constructor privado impide otros subtipos fuera de este archivo. Aun así, los <c>switch</c> de
/// <see cref="PlanillaCostos"/> terminan con un brazo <c>_</c> a propósito: es la defensa ante un subtipo desconocido,
/// que cae en la rama segura (código vacío, es decir "código inexistente"; encabezado no válido; "precio inválido") en
/// lugar de lanzar.
/// </remarks>
public abstract record CeldaPlanilla
{
    private CeldaPlanilla()
    {
    }

    /// <summary>Una celda sin valor (D-08).</summary>
    public sealed record Vacia : CeldaPlanilla;

    /// <summary>Una celda numérica, ya leída como decimal con cultura invariante (D-09).</summary>
    public sealed record Numero(decimal Valor) : CeldaPlanilla;

    /// <summary>Una celda de texto, sin recortar (D-10).</summary>
    public sealed record Texto(string Valor) : CeldaPlanilla
    {
        /// <summary>El texto de la celda; nunca null.</summary>
        public string Valor { get; } = Valor ?? throw new ArgumentNullException(nameof(Valor));
    }

    /// <summary>Una celda de otro tipo: booleano, error, fecha u otro que no es número ni texto (D-09).</summary>
    public sealed record Otro : CeldaPlanilla;
}

/// <summary>
/// Una fila de datos de la planilla: su número en la hoja (la fila 1 son los encabezados), la celda del código en el
/// proveedor (columna A) y la del precio de costo nuevo (columna B).
/// </summary>
public sealed record FilaPlanilla(int Numero, CeldaPlanilla Codigo, CeldaPlanilla Precio);

/// <summary>
/// Lo que el lector encontró en la planilla (D-07 de FEAT-011): las celdas A y B de la fila 1, si alguna fila tiene
/// datos en una columna después de la B, y las filas de datos en el orden de la hoja.
/// </summary>
public sealed record ContenidoPlanilla(
    CeldaPlanilla EncabezadoA, CeldaPlanilla EncabezadoB, bool TieneColumnasDeMas, IReadOnlyList<FilaPlanilla> Filas);

/// <summary>Por qué una fila de la planilla no se aplicó (FR-13 de FEAT-011).</summary>
public enum RazonNoAplicada
{
    /// <summary>"código inexistente": vacío, inadmisible (D-10) o sin artículo con esa clave.</summary>
    CodigoInexistente,

    /// <summary>"precio inválido": no numérico (FR-08, D-04) o fuera de rango (D-09).</summary>
    PrecioInvalido,

    /// <summary>"código repetido en la planilla": su clave aparece en más de una fila (FR-10, D-11).</summary>
    CodigoRepetido,
}

/// <summary>Los textos de las razones de FR-13 de FEAT-011, como los ve la operadora.</summary>
public static class TextosRazonNoAplicada
{
    /// <summary>El texto de la razón: "código inexistente", "precio inválido" o "código repetido en la planilla".</summary>
    /// <exception cref="ArgumentOutOfRangeException">El valor no es una razón definida.</exception>
    public static string Texto(this RazonNoAplicada razon) => razon switch
    {
        RazonNoAplicada.CodigoInexistente => "código inexistente",
        RazonNoAplicada.PrecioInvalido => "precio inválido",
        RazonNoAplicada.CodigoRepetido => "código repetido en la planilla",
        _ => throw new ArgumentOutOfRangeException(nameof(razon), "Razón de fila no aplicada sin texto asignado."),
    };
}

/// <summary>
/// El resultado de una fila de la planilla (FR-12 a FR-15 de FEAT-011): aplicada, con el artículo nuevo y sus
/// indicaciones, o no aplicada con su <see cref="RazonNoAplicada"/>.
/// </summary>
public sealed class ResultadoFilaPlanilla
{
    private ResultadoFilaPlanilla(int numero, string codigo, RazonNoAplicada? razon, Articulo? articulo, bool cambia)
    {
        Numero = numero;
        Codigo = codigo;
        Razon = razon;
        Articulo = articulo;
        Cambia = cambia;
    }

    /// <summary>El número de la fila en la hoja.</summary>
    public int Numero { get; }

    /// <summary>
    /// El código tal como vino en la planilla, recortado; vacío si la celda no tenía texto ni número. Puede ser más
    /// largo que <see cref="PoliticaArticulo.LargoMaximoCodigo"/> o no ser UTF-16 válido: quien lo muestra lo acota
    /// (D-10).
    /// </summary>
    public string Codigo { get; }

    /// <summary>La razón por la que no se aplicó; null si se aplicó.</summary>
    public RazonNoAplicada? Razon { get; }

    /// <summary>El artículo con el costo y la venta nuevos; null si la fila no se aplicó.</summary>
    public Articulo? Articulo { get; }

    /// <summary>Verdadero si la fila se aplicó: <see cref="Articulo"/> tiene el artículo nuevo.</summary>
    [MemberNotNullWhen(true, nameof(Articulo))]
    public bool EsAplicada => Articulo is not null;

    /// <summary>Verdadero si la fila aplicada cambia el costo o la venta guardados; falso si son iguales (D-13).</summary>
    public bool Cambia { get; }

    /// <summary>Verdadero si se aplicó con un costo cero o negativo (FR-14).</summary>
    public bool PrecioNoPositivo => Articulo is { CostoNoPositivo: true };

    /// <summary>Verdadero si se aplicó a un artículo desactivado, que sigue desactivado (FR-11, FR-15, D-12).</summary>
    public bool Inactivo => Articulo is { Activo: false };

    /// <summary>
    /// Las indicaciones de la fila aplicada, en este orden: <see cref="PlanillaCostos.IndicacionPrecioNoPositivo"/>
    /// (FR-14) e <see cref="PlanillaCostos.IndicacionInactivo"/> (FR-15). Vacía si no lleva ninguna o no se aplicó.
    /// </summary>
    public IReadOnlyList<string> Indicaciones => (PrecioNoPositivo, Inactivo) switch
    {
        (true, true) => [PlanillaCostos.IndicacionPrecioNoPositivo, PlanillaCostos.IndicacionInactivo],
        (true, false) => [PlanillaCostos.IndicacionPrecioNoPositivo],
        (false, true) => [PlanillaCostos.IndicacionInactivo],
        (false, false) => [],
    };

    internal static ResultadoFilaPlanilla NoAplicada(int numero, string codigo, RazonNoAplicada razon) =>
        new(numero, codigo, razon, null, cambia: false);

    internal static ResultadoFilaPlanilla Aplicada(int numero, string codigo, Articulo articulo, bool cambia)
    {
        ArgumentNullException.ThrowIfNull(articulo);
        return new ResultadoFilaPlanilla(numero, codigo, null, articulo, cambia);
    }
}

/// <summary>Las reglas de la planilla de actualización de costos (FR-03 a FR-15 de FEAT-011).</summary>
public static class PlanillaCostos
{
    /// <summary>El encabezado de la columna A, como lo indica el mensaje de formato (FR-03, FR-05).</summary>
    public const string EncabezadoCodigo = "Codigo en el proveedor";

    /// <summary>El encabezado de la columna B, como lo indica el mensaje de formato (FR-03, FR-05).</summary>
    public const string EncabezadoPrecio = "Precio de Costo Nuevo";

    /// <summary>La indicación de una fila aplicada con costo cero o negativo (FR-14).</summary>
    public const string IndicacionPrecioNoPositivo = "actualizado con precio negativo o cero";

    /// <summary>La indicación de una fila aplicada a un artículo desactivado (FR-15).</summary>
    public const string IndicacionInactivo = "inactivo";

    // Hasta 28 decimales, los que admite decimal, sin ceros al final ni separador de miles.
    private const string FormatoCodigoNumerico = "0.############################";

    private static readonly string EncabezadoCodigoPlegado = PlegadoBusqueda.Plegar(EncabezadoCodigo);
    private static readonly string EncabezadoPrecioPlegado = PlegadoBusqueda.Plegar(EncabezadoPrecio);

    /// <summary>
    /// Verdadero si la planilla tiene exactamente dos columnas y la fila 1 dice <see cref="EncabezadoCodigo"/> en A y
    /// <see cref="EncabezadoPrecio"/> en B, comparados con <see cref="PlegadoBusqueda.Plegar"/>: sin distinguir
    /// mayúsculas, acentos ni espacios al principio y al final (FR-03, AC-06). Falso con columnas de más, con un
    /// encabezado distinto, en otro orden, vacío o que no es texto (FR-04, AC-04, AC-05). Nunca lanza por el contenido.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="contenido"/> es null.</exception>
    public static bool TieneFormatoEsperado(ContenidoPlanilla contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);

        return !contenido.TieneColumnasDeMas
            && EsEncabezado(contenido.EncabezadoA, EncabezadoCodigoPlegado)
            && EsEncabezado(contenido.EncabezadoB, EncabezadoPrecioPlegado);
    }

    /// <summary>
    /// Un resultado por fila, en el mismo orden, sin lanzar por el contenido de ninguna (FR-12). Por fila gana la
    /// primera razón que se cumple, en este orden (D-11):
    /// <list type="number">
    /// <item>"código repetido en la planilla": la clave de ADR-009 (FR-07) aparece en más de una fila; todas esas
    /// filas se rechazan y el artículo queda sin cambios (FR-10, AC-11).</item>
    /// <item>"código inexistente": la celda no es texto ni número, el código recortado no es admisible según
    /// <see cref="PoliticaArticulo.EsCodigoAdmisible"/> (D-10), o no hay artículo con esa clave (AC-02).</item>
    /// <item>"precio inválido": la celda no es numérica, aunque el texto parezca un número (FR-08, D-04, AC-03), o el
    /// costo redondeado al centavo con la mitad alejándose del cero (D-03) o el precio de venta recalculado quedan
    /// fuera de rango (D-09).</item>
    /// </list>
    /// Si ninguna se cumple, la fila queda aplicada con <see cref="Articulo.ConCostoNuevo"/> (FR-06), también con costo
    /// cero o negativo (FR-09, FR-14) y en artículos desactivados (FR-11, FR-15, D-12), y con
    /// <see cref="ResultadoFilaPlanilla.Cambia"/> falso si costo y venta no cambian (D-13). Recorre las filas dos veces
    /// y busca por clave: lineal en la cantidad de filas.
    /// </summary>
    /// <param name="filas">Las filas de datos, sin la de encabezados.</param>
    /// <param name="articulosPorClave">El catálogo por <see cref="Articulo.CodigoProveedorClave"/>.</param>
    /// <param name="configuracion">La configuración vigente para recalcular el precio de venta.</param>
    /// <exception cref="ArgumentNullException">Algún argumento, o alguna fila, es null.</exception>
    public static IReadOnlyList<ResultadoFilaPlanilla> Evaluar(
        IReadOnlyList<FilaPlanilla> filas, IReadOnlyDictionary<string, Articulo> articulosPorClave,
        ConfiguracionNegocio configuracion)
    {
        ArgumentNullException.ThrowIfNull(filas);
        ArgumentNullException.ThrowIfNull(articulosPorClave);
        ArgumentNullException.ThrowIfNull(configuracion);

        var codigos = new string[filas.Count];
        var claves = new string?[filas.Count];
        var apariciones = new Dictionary<string, int>(filas.Count, StringComparer.Ordinal);
        for (var i = 0; i < filas.Count; i++)
        {
            var fila = filas[i] ?? throw new ArgumentNullException(nameof(filas), "La planilla tiene una fila nula.");
            codigos[i] = CodigoDe(fila.Codigo);

            // Solo un código admisible llega a Clave: con UTF-16 inválido Normalize lanzaría (D-10, D-16 de FEAT-002a).
            if (PoliticaArticulo.EsCodigoAdmisible(codigos[i]))
            {
                var clave = NormalizacionCodigo.Clave(codigos[i]);
                claves[i] = clave;
                apariciones[clave] = apariciones.GetValueOrDefault(clave) + 1;
            }
        }

        var resultados = new List<ResultadoFilaPlanilla>(filas.Count);
        for (var i = 0; i < filas.Count; i++)
        {
            resultados.Add(EvaluarFila(
                filas[i], codigos[i], claves[i], apariciones, articulosPorClave, configuracion));
        }

        return resultados;
    }

    private static ResultadoFilaPlanilla EvaluarFila(
        FilaPlanilla fila, string codigo, string? clave, Dictionary<string, int> apariciones,
        IReadOnlyDictionary<string, Articulo> articulosPorClave, ConfiguracionNegocio configuracion)
    {
        if (clave is not null && apariciones[clave] > 1)
        {
            return ResultadoFilaPlanilla.NoAplicada(fila.Numero, codigo, RazonNoAplicada.CodigoRepetido);
        }

        if (clave is null || !articulosPorClave.TryGetValue(clave, out var articulo))
        {
            return ResultadoFilaPlanilla.NoAplicada(fila.Numero, codigo, RazonNoAplicada.CodigoInexistente);
        }

        if (fila.Precio is not CeldaPlanilla.Numero { Valor: var valor })
        {
            return ResultadoFilaPlanilla.NoAplicada(fila.Numero, codigo, RazonNoAplicada.PrecioInvalido);
        }

        // D-03: al centavo, la mitad alejándose del cero (1234,565 → 1234,57; −0,005 → −0,01). Round no desborda.
        // Con 2 decimales, ConCostoNuevo no lanza: el costo o la venta fuera de rango vuelven como FueraDeRango (D-09).
        var costo = decimal.Round(valor, 2, MidpointRounding.AwayFromZero);
        var nuevo = articulo.ConCostoNuevo(costo, configuracion);
        if (!nuevo.EsAplicado)
        {
            return ResultadoFilaPlanilla.NoAplicada(fila.Numero, codigo, RazonNoAplicada.PrecioInvalido);
        }

        return ResultadoFilaPlanilla.Aplicada(fila.Numero, codigo, nuevo.Articulo, nuevo.Cambia);
    }

    // D-10: el lector entrega el código como texto; un número se toma con cultura invariante y sin los ceros de la
    // escala (100.0 da "100" y 1200.50 da "1200.5"). Otro tipo queda vacío.
    private static string CodigoDe(CeldaPlanilla celda) => celda switch
    {
        CeldaPlanilla.Texto texto => NormalizacionCodigo.Recortar(texto.Valor),
        CeldaPlanilla.Numero numero => TextoDeCodigoNumerico(numero.Valor),
        _ => string.Empty,
    };

    /// <summary>
    /// El texto de un código que llegó como número (D-10 de FEAT-011): cultura invariante, sin separador de miles y sin
    /// los ceros de la escala (100.0 da "100" y 1200.50 da "1200.5"), con hasta los 28 decimales de decimal. Es la misma
    /// regla para el lector de la planilla y para <see cref="Evaluar"/>.
    /// </summary>
    public static string TextoDeCodigoNumerico(decimal valor) =>
        valor.ToString(FormatoCodigoNumerico, CultureInfo.InvariantCulture);

    private static bool EsEncabezado(CeldaPlanilla celda, string esperadoPlegado) =>
        celda is CeldaPlanilla.Texto texto
        && string.Equals(PlegadoBusqueda.Plegar(texto.Valor), esperadoPlegado, StringComparison.Ordinal);
}
