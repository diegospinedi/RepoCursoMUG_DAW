namespace Optica.Application.Comun;

/// <summary>Cómo terminó un caso de uso.</summary>
public enum TipoResultado
{
    Exito,
    Invalido,
    Conflicto,
}

/// <summary>Resultado de un caso de uso: éxito, errores de validación o conflicto con el estado guardado.</summary>
public sealed class Resultado
{
    private static readonly Resultado ExitoUnico = new(TipoResultado.Exito, []);
    private static readonly Resultado ConflictoUnico = new(TipoResultado.Conflicto, []);

    private Resultado(TipoResultado tipo, IReadOnlyList<ErrorValidacion> errores)
    {
        Tipo = tipo;
        Errores = errores;
    }

    public TipoResultado Tipo { get; }

    /// <summary>Errores de validación; vacío salvo cuando <see cref="Tipo"/> es <see cref="TipoResultado.Invalido"/>.</summary>
    public IReadOnlyList<ErrorValidacion> Errores { get; }

    public bool EsExito => Tipo == TipoResultado.Exito;

    public static Resultado Exito() => ExitoUnico;

    public static Resultado Conflicto() => ConflictoUnico;

    public static Resultado Invalido(params ErrorValidacion[] errores)
    {
        if (errores is null || errores.Length == 0)
        {
            throw new ArgumentException("Un resultado inválido necesita al menos un error.", nameof(errores));
        }

        return new Resultado(TipoResultado.Invalido, errores.ToArray());
    }
}
