using Optica.Domain.Comun;

namespace Optica.Domain.Catalogo;

/// <summary>
/// Qué regla del artículo incumple un campo. Cada valor pertenece a un solo campo: el nombre empieza por el campo
/// (código, descripción, costo, margen o precio de venta).
/// </summary>
public enum InfraccionArticulo
{
    CodigoFaltante,
    CodigoConCaracteresInvalidos,
    CodigoDemasiadoLargo,
    DescripcionFaltante,
    DescripcionConCaracteresInvalidos,
    DescripcionDemasiadoLarga,
    CostoFaltante,
    CostoConMasDeDosDecimales,
    CostoFueraDeRango,
    MargenFaltante,
    MargenFueraDeRango,
    MargenConMasDeDosDecimales,

    /// <summary>
    /// El precio de venta calculado o redondeado no entra en el tipo (D-06). La informa
    /// <see cref="CalculadoraPrecioVenta"/>, no <see cref="PoliticaArticulo.Evaluar"/>.
    /// </summary>
    PrecioVentaFueraDeRango,
}

/// <summary>Reglas de los datos del artículo (FR-02 a FR-05, D-02, D-03, D-13, D-16 y D-17 de FEAT-002a).</summary>
public static class PoliticaArticulo
{
    public const int LargoMaximoCodigo = 50;
    public const int LargoMaximoDescripcion = 200;
    public const decimal MargenMinimo = 0m;
    public const decimal MargenMaximo = 1000m;

    /// <summary>
    /// Evalúa los cuatro campos y devuelve sus infracciones, una por campo como máximo, en el orden código,
    /// descripción, costo, margen. Por campo gana la primera regla incumplida. Nunca lanza.
    /// </summary>
    public static IReadOnlyList<InfraccionArticulo> Evaluar(
        string? codigoProveedor, string? descripcion, decimal? precioCosto, decimal? margenUtilidad)
    {
        var infracciones = new List<InfraccionArticulo>(4);

        if (EvaluarTexto(codigoProveedor, LargoMaximoCodigo, InfraccionArticulo.CodigoFaltante,
                InfraccionArticulo.CodigoConCaracteresInvalidos, InfraccionArticulo.CodigoDemasiadoLargo) is { } codigo)
        {
            infracciones.Add(codigo);
        }

        if (EvaluarTexto(descripcion, LargoMaximoDescripcion, InfraccionArticulo.DescripcionFaltante,
                InfraccionArticulo.DescripcionConCaracteresInvalidos,
                InfraccionArticulo.DescripcionDemasiadoLarga) is { } textoDescripcion)
        {
            infracciones.Add(textoDescripcion);
        }

        if (EvaluarCosto(precioCosto) is { } costo)
        {
            infracciones.Add(costo);
        }

        if (EvaluarMargen(margenUtilidad) is { } margen)
        {
            infracciones.Add(margen);
        }

        return infracciones;
    }

    // Faltante, después UTF-16 inválido (D-16, para que Clave nunca llegue a Normalize con un sustituto suelto) y por
    // último el largo, medido en string.Length sobre el texto recortado (D-02, D-13).
    private static InfraccionArticulo? EvaluarTexto(
        string? texto, int largoMaximo, InfraccionArticulo faltante, InfraccionArticulo caracteresInvalidos,
        InfraccionArticulo demasiadoLargo)
    {
        if (texto is null)
        {
            return faltante;
        }

        var recortado = NormalizacionCodigo.Recortar(texto);
        if (recortado.Length == 0)
        {
            return faltante;
        }

        if (!NormalizacionCodigo.EsUtf16Valido(recortado))
        {
            return caracteresInvalidos;
        }

        return recortado.Length > largoMaximo ? demasiadoLargo : null;
    }

    private static InfraccionArticulo? EvaluarCosto(decimal? valor)
    {
        if (valor is not { } costo)
        {
            return InfraccionArticulo.CostoFaltante;
        }

        if (ReglasImporte.TieneMasDeDosDecimales(costo))
        {
            return InfraccionArticulo.CostoConMasDeDosDecimales;
        }

        // FR-05: cero y negativos son válidos; solo se limita el módulo al tipo.
        return Math.Abs(costo) > ReglasImporte.ImporteMaximo ? InfraccionArticulo.CostoFueraDeRango : null;
    }

    private static InfraccionArticulo? EvaluarMargen(decimal? valor)
    {
        if (valor is not { } margen)
        {
            return InfraccionArticulo.MargenFaltante;
        }

        // AC-05: el rango antes que la escala, así 1.000,01 informa el rango.
        if (margen < MargenMinimo || margen > MargenMaximo)
        {
            return InfraccionArticulo.MargenFueraDeRango;
        }

        return ReglasImporte.TieneMasDeDosDecimales(margen) ? InfraccionArticulo.MargenConMasDeDosDecimales : null;
    }
}
