using System.Diagnostics;
using Optica.Domain.Catalogo;

namespace Optica.Domain.Presupuestos;

/// <summary>
/// El cliente de un presupuesto (FR-01 a FR-05). Siempre cumple <see cref="PoliticaCliente"/>: textos recortados, DNI
/// sin puntos y null en los datos opcionales vacíos. Lleva datos personales (Ley 25.326): no sobrescribe
/// <see cref="object.ToString"/> ni se registra en el log (D-17).
/// </summary>
public sealed class ClientePresupuesto
{
    /// <exception cref="ArgumentException">
    /// Algún dato incumple <see cref="PoliticaCliente"/>. El mensaje nombra el campo y no incluye el valor recibido
    /// (D-17).
    /// </exception>
    public ClientePresupuesto(
        string apellido, string nombre, string dni, string? domicilio, string? email, string? telefono)
    {
        var infracciones = PoliticaCliente.Evaluar(apellido, nombre, dni, domicilio, email, telefono);
        if (infracciones.Count > 0)
        {
            throw Error(infracciones[0]);
        }

        // Sin infracciones, los obligatorios no son null y el DNI se normaliza.
        Apellido = NormalizacionCodigo.Recortar(apellido);
        Nombre = NormalizacionCodigo.Recortar(nombre);
        Dni = PoliticaCliente.NormalizarDni(dni)!;
        Domicilio = PoliticaCliente.RecortarOpcional(domicilio);
        Email = PoliticaCliente.RecortarOpcional(email);
        Telefono = PoliticaCliente.RecortarOpcional(telefono);
    }

    /// <summary>El apellido recortado, de 1 a 100 caracteres.</summary>
    public string Apellido { get; }

    /// <summary>El nombre recortado, de 1 a 100 caracteres.</summary>
    public string Nombre { get; }

    /// <summary>El DNI sin puntos: 7 u 8 dígitos (D-09).</summary>
    public string Dni { get; }

    /// <summary>El domicilio recortado, hasta 200 caracteres; null si no se cargó (D-11).</summary>
    public string? Domicilio { get; }

    /// <summary>El email recortado, como se escribió, hasta 254 caracteres; null si no se cargó (D-10, D-11).</summary>
    public string? Email { get; }

    /// <summary>El teléfono recortado, texto libre de hasta 30 caracteres; null si no se cargó (D-08, D-11).</summary>
    public string? Telefono { get; }

    // Textos fijos: nunca repiten el valor recibido (D-17).
    private static ArgumentException Error(InfraccionCliente infraccion) => infraccion switch
    {
        InfraccionCliente.ApellidoFaltante
            or InfraccionCliente.ApellidoConCaracteresInvalidos
            or InfraccionCliente.ApellidoDemasiadoLargo => new ArgumentException("El apellido no es válido.", "apellido"),
        InfraccionCliente.NombreFaltante
            or InfraccionCliente.NombreConCaracteresInvalidos
            or InfraccionCliente.NombreDemasiadoLargo => new ArgumentException("El nombre no es válido.", "nombre"),
        InfraccionCliente.DniFaltante
            or InfraccionCliente.DniInvalido => new ArgumentException("El DNI no es válido.", "dni"),
        InfraccionCliente.DomicilioConCaracteresInvalidos
            or InfraccionCliente.DomicilioDemasiadoLargo => new ArgumentException("El domicilio no es válido.", "domicilio"),
        InfraccionCliente.EmailConCaracteresInvalidos
            or InfraccionCliente.EmailDemasiadoLargo
            or InfraccionCliente.EmailInvalido => new ArgumentException("El email no es válido.", "email"),
        InfraccionCliente.TelefonoConCaracteresInvalidos
            or InfraccionCliente.TelefonoDemasiadoLargo => new ArgumentException("El teléfono no es válido.", "telefono"),
        _ => throw new UnreachableException("Infracción de cliente sin mensaje asignado."),
    };
}
