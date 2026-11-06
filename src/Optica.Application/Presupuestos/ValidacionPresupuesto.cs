using System.Diagnostics;
using System.Globalization;
using Optica.Application.Comun;
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
    /// cada código faltante, no positivo o mayor que <see cref="CodigoArticuloMaximo"/> en <c>lineas[i]</c>. Un cliente
    /// ausente cuenta como seis datos ausentes. Vacío si los datos se pueden grabar.
    /// </summary>
    public static IReadOnlyList<ErrorValidacion> Validar(DatosPresupuesto datos)
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
            for (var indice = 0; indice < lineas.Count; indice++)
            {
                if (lineas[indice] is not (>= 1 and <= CodigoArticuloMaximo))
                {
                    errores.Add(ArticuloNoDisponible(indice));
                }
            }
        }

        return errores;
    }

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
