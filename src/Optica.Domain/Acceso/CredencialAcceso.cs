namespace Optica.Domain.Acceso;

/// <summary>
/// La única credencial de acceso del sistema: el hash de la contraseña (nunca la contraseña) y la fecha y
/// hora UTC en que se definió.
/// </summary>
public sealed class CredencialAcceso
{
    public CredencialAcceso(string hash, DateTime definidaEn)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new ArgumentException("El hash de la credencial no puede estar vacío.", nameof(hash));
        }

        if (definidaEn.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("La fecha de definición debe estar en UTC.", nameof(definidaEn));
        }

        Hash = hash;
        DefinidaEn = definidaEn;
    }

    /// <summary>Hash con sal en el formato del hasher configurado.</summary>
    public string Hash { get; }

    /// <summary>Fecha y hora UTC de definición.</summary>
    public DateTime DefinidaEn { get; }
}
