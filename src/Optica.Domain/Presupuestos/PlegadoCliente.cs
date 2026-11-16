using Optica.Domain.Catalogo;

namespace Optica.Domain.Presupuestos;

/// <summary>
/// El plegado de Apellido y Nombre del cliente para buscarlo (FR-03 de FEAT-006, FR-19 de FEAT-009): la misma regla que
/// <see cref="FiltroPresupuestos"/> y que <c>plegar</c> en la base (ADR-011), porque delega en
/// <see cref="PlegadoBusqueda.Plegar"/>. Existe para que Facturación pliegue a través de Presupuestos, que es el módulo
/// del que depende, y no use Catálogo directamente (D-13 de FEAT-009).
/// </summary>
public static class PlegadoCliente
{
    /// <summary>El texto plegado: "  GONZÁLEZ " da "gonzalez". Nunca lanza con un texto; null no es un texto.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="texto"/> es null.</exception>
    public static string Plegar(string texto) => PlegadoBusqueda.Plegar(texto);

    /// <summary>
    /// El texto recortado con la regla de <see cref="NormalizacionCodigo.Recortar"/>: quita los espacios de los extremos
    /// y también U+FEFF (D-17 de FEAT-002a), como la validación de la búsqueda de presupuestos.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="texto"/> es null.</exception>
    public static string Recortar(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        return NormalizacionCodigo.Recortar(texto);
    }

    /// <summary>
    /// Verdadero si el texto es UTF-16 válido (<see cref="NormalizacionCodigo.EsUtf16Valido"/>): ningún sustituto suelto.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="texto"/> es null.</exception>
    public static bool EsUtf16Valido(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        return NormalizacionCodigo.EsUtf16Valido(texto);
    }
}
