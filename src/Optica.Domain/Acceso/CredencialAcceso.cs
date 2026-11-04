namespace Optica.Domain.Acceso;

/// <summary>
/// La única credencial de acceso del sistema: el hash de la contraseña (nunca la contraseña) y la fecha y
/// hora UTC en que se definió.
/// </summary>
public sealed class CredencialAcceso
{
    public CredencialAcceso(string hash, DateTime definidaEn)
    {
        ValidarHash(hash, nameof(hash));
        ValidarUtc(definidaEn, nameof(definidaEn));

        Hash = hash;
        DefinidaEn = definidaEn;
    }

    /// <summary>Hash con sal en el formato del hasher configurado.</summary>
    public string Hash { get; private set; }

    /// <summary>Fecha y hora UTC de definición (la del último cambio, si se reemplazó).</summary>
    public DateTime DefinidaEn { get; private set; }

    /// <summary>
    /// Reemplaza el hash por el de una contraseña nueva y fija la fecha de definición en <paramref name="ahora"/>. Las
    /// validaciones son las del constructor; si alguna falla, la credencial queda como estaba.
    /// </summary>
    /// <exception cref="ArgumentException">El hash está vacío o la fecha no está en UTC.</exception>
    public void ReemplazarHash(string nuevo, DateTime ahora)
    {
        ValidarHash(nuevo, nameof(nuevo));
        ValidarUtc(ahora, nameof(ahora));

        Hash = nuevo;
        DefinidaEn = ahora;
    }

    // Los mensajes no incluyen el valor recibido: un hash es un dato de la contraseña.
    private static void ValidarHash(string hash, string parametro)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new ArgumentException("El hash de la credencial no puede estar vacío.", parametro);
        }
    }

    private static void ValidarUtc(DateTime fecha, string parametro)
    {
        if (fecha.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("La fecha de definición debe estar en UTC.", parametro);
        }
    }
}
