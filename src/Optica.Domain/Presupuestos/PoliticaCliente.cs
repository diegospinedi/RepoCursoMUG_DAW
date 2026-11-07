using Optica.Domain.Catalogo;

namespace Optica.Domain.Presupuestos;

/// <summary>
/// Qué regla del cliente incumple un campo. Cada valor pertenece a un solo campo: el nombre empieza por el campo
/// (apellido, nombre, DNI, domicilio, email o teléfono).
/// </summary>
public enum InfraccionCliente
{
    ApellidoFaltante,
    ApellidoConCaracteresInvalidos,
    ApellidoDemasiadoLargo,
    NombreFaltante,
    NombreConCaracteresInvalidos,
    NombreDemasiadoLargo,
    DniFaltante,

    /// <summary>Después de quitar los puntos no quedan 7 u 8 dígitos ASCII (FR-04, D-09).</summary>
    DniInvalido,
    DomicilioConCaracteresInvalidos,
    DomicilioDemasiadoLargo,
    EmailConCaracteresInvalidos,
    EmailDemasiadoLargo,

    /// <summary>El email no tiene el formato usuario@dominio.extensión (FR-05, D-10).</summary>
    EmailInvalido,
    TelefonoConCaracteresInvalidos,
    TelefonoDemasiadoLargo,
}

/// <summary>
/// Reglas de los datos del cliente del presupuesto (FR-02 a FR-05, D-08 a D-11, D-25). Los textos se recortan con
/// <see cref="NormalizacionCodigo.Recortar"/> y el largo se mide sobre el texto recortado.
/// </summary>
public static class PoliticaCliente
{
    public const int LargoMaximoApellido = 100;
    public const int LargoMaximoNombre = 100;
    public const int LargoMaximoDomicilio = 200;
    public const int LargoMaximoEmail = 254;
    public const int LargoMaximoTelefono = 30;

    private const int LargoExtensionMinimo = 2;

    /// <summary>
    /// Evalúa los seis campos y devuelve sus infracciones, una por campo como máximo, en el orden apellido, nombre,
    /// DNI, domicilio, email, teléfono. Por campo gana la primera regla incumplida: faltante, UTF-16 inválido, largo y
    /// formato. Domicilio, email y teléfono vacíos o hechos solo de espacios cuentan como ausentes (FR-03, D-11).
    /// Nunca lanza.
    /// </summary>
    public static IReadOnlyList<InfraccionCliente> Evaluar(
        string? apellido, string? nombre, string? dni, string? domicilio, string? email, string? telefono)
    {
        var infracciones = new List<InfraccionCliente>(6);

        Agregar(infracciones, EvaluarObligatorio(apellido, LargoMaximoApellido, InfraccionCliente.ApellidoFaltante,
            InfraccionCliente.ApellidoConCaracteresInvalidos, InfraccionCliente.ApellidoDemasiadoLargo));
        Agregar(infracciones, EvaluarObligatorio(nombre, LargoMaximoNombre, InfraccionCliente.NombreFaltante,
            InfraccionCliente.NombreConCaracteresInvalidos, InfraccionCliente.NombreDemasiadoLargo));
        Agregar(infracciones, EvaluarDni(dni));
        Agregar(infracciones, EvaluarOpcional(domicilio, LargoMaximoDomicilio,
            InfraccionCliente.DomicilioConCaracteresInvalidos, InfraccionCliente.DomicilioDemasiadoLargo));
        Agregar(infracciones, EvaluarEmail(email));
        Agregar(infracciones, EvaluarOpcional(telefono, LargoMaximoTelefono,
            InfraccionCliente.TelefonoConCaracteresInvalidos, InfraccionCliente.TelefonoDemasiadoLargo));

        return infracciones;
    }

    /// <summary>
    /// El DNI recortado y sin ningún "." si quedan 7 u 8 dígitos ASCII (FR-04, D-09); si no, null. "23.456.789" da
    /// "23456789". Nunca lanza.
    /// </summary>
    public static string? NormalizarDni(string? texto)
    {
        if (texto is null)
        {
            return null;
        }

        var digitos = NormalizacionCodigo.Recortar(texto).Replace(".", string.Empty, StringComparison.Ordinal);
        if (digitos.Length is < 7 or > 8)
        {
            return null;
        }

        foreach (var caracter in digitos)
        {
            if (!char.IsAsciiDigit(caracter))
            {
                return null;
            }
        }

        return digitos;
    }

    /// <summary>El texto recortado, o null si es null o queda vacío: así se guarda un dato opcional (D-11).</summary>
    internal static string? RecortarOpcional(string? texto)
    {
        if (texto is null)
        {
            return null;
        }

        var recortado = NormalizacionCodigo.Recortar(texto);
        return recortado.Length == 0 ? null : recortado;
    }

    private static void Agregar(List<InfraccionCliente> infracciones, InfraccionCliente? infraccion)
    {
        if (infraccion is { } valor)
        {
            infracciones.Add(valor);
        }
    }

    private static InfraccionCliente? EvaluarObligatorio(
        string? texto, int largoMaximo, InfraccionCliente faltante, InfraccionCliente caracteresInvalidos,
        InfraccionCliente demasiadoLargo)
    {
        var recortado = RecortarOpcional(texto);
        return recortado is null ? faltante : EvaluarTexto(recortado, largoMaximo, caracteresInvalidos, demasiadoLargo);
    }

    private static InfraccionCliente? EvaluarOpcional(
        string? texto, int largoMaximo, InfraccionCliente caracteresInvalidos, InfraccionCliente demasiadoLargo)
    {
        var recortado = RecortarOpcional(texto);
        return recortado is null ? null : EvaluarTexto(recortado, largoMaximo, caracteresInvalidos, demasiadoLargo);
    }

    // UTF-16 inválido antes que el largo, como PoliticaArticulo (D-16 de FEAT-002a).
    private static InfraccionCliente? EvaluarTexto(
        string recortado, int largoMaximo, InfraccionCliente caracteresInvalidos, InfraccionCliente demasiadoLargo)
    {
        if (!NormalizacionCodigo.EsUtf16Valido(recortado))
        {
            return caracteresInvalidos;
        }

        return recortado.Length > largoMaximo ? demasiadoLargo : null;
    }

    // Un DNI con UTF-16 inválido no son dígitos: es inválido, no tiene una infracción propia.
    private static InfraccionCliente? EvaluarDni(string? dni)
    {
        if (RecortarOpcional(dni) is null)
        {
            return InfraccionCliente.DniFaltante;
        }

        return NormalizarDni(dni) is null ? InfraccionCliente.DniInvalido : null;
    }

    private static InfraccionCliente? EvaluarEmail(string? email)
    {
        var recortado = RecortarOpcional(email);
        if (recortado is null)
        {
            return null;
        }

        return EvaluarTexto(recortado, LargoMaximoEmail, InfraccionCliente.EmailConCaracteresInvalidos,
                InfraccionCliente.EmailDemasiadoLargo)
            ?? (TieneFormatoDeEmail(recortado) ? null : InfraccionCliente.EmailInvalido);
    }

    // D-10: parte local sin espacios ni "@", una sola "@" y un dominio con al menos un punto, sin espacios ni partes
    // vacías, cuya última parte tiene al menos 2 letras.
    private static bool TieneFormatoDeEmail(string email)
    {
        var arroba = email.IndexOf('@', StringComparison.Ordinal);
        if (arroba <= 0 || email.IndexOf('@', arroba + 1) >= 0)
        {
            return false;
        }

        foreach (var caracter in email)
        {
            if (char.IsWhiteSpace(caracter))
            {
                return false;
            }
        }

        var partes = email[(arroba + 1)..].Split('.');
        if (partes.Length < 2 || Array.Exists(partes, parte => parte.Length == 0))
        {
            return false;
        }

        var extension = partes[^1];
        if (extension.Length < LargoExtensionMinimo)
        {
            return false;
        }

        foreach (var caracter in extension)
        {
            if (!char.IsAsciiLetter(caracter))
            {
                return false;
            }
        }

        return true;
    }
}
