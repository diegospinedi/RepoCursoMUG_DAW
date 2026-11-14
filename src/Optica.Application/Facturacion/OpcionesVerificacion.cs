namespace Optica.Application.Facturacion;

/// <summary>
/// Las opciones de la verificación de una emisión pendiente: la espera desde que quedó pendiente antes de poder consultar
/// a ARCA (FR-16, D-07, D-20). La raíz de composición la arma con <c>Arca:EsperaVerificacionSegundos</c> (60 s por
/// defecto, de 1 a 3600); Application no depende de la lectura de la configuración.
/// </summary>
public sealed class OpcionesVerificacion
{
    /// <summary>La espera por defecto: 1 minuto (FR-16).</summary>
    public static readonly TimeSpan EsperaPorDefecto = TimeSpan.FromSeconds(60);

    /// <exception cref="ArgumentOutOfRangeException">La espera no es positiva.</exception>
    public OpcionesVerificacion(TimeSpan esperaVerificacion)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(esperaVerificacion, TimeSpan.Zero);
        EsperaVerificacion = esperaVerificacion;
    }

    /// <summary>Cuánto tiene que pasar desde el momento de la pendiente para consultar a ARCA.</summary>
    public TimeSpan EsperaVerificacion { get; }
}
