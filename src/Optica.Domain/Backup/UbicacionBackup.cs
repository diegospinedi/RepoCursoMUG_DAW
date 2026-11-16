using System.Diagnostics.CodeAnalysis;
using Optica.Domain.Configuracion;

namespace Optica.Domain.Backup;

/// <summary>
/// Qué regla de la ubicación del backup incumple el campo <c>ruta</c>, el único de la ubicación. Gana la primera regla
/// incumplida, en el orden de los valores.
/// </summary>
public enum InfraccionUbicacionBackup
{
    /// <summary>Null o vacía después de recortar.</summary>
    RutaFaltante,

    /// <summary>Tiene caracteres de control (saltos de línea incluidos) o UTF-16 inválido.</summary>
    RutaConCaracteresInvalidos,

    /// <summary>Más de <see cref="UbicacionBackup.LargoMaximoRuta"/> caracteres después de recortar.</summary>
    RutaDemasiadoLarga,

    /// <summary>No es absoluta (<see cref="Path.IsPathFullyQualified(string)"/>): dependería de la carpeta de arranque.</summary>
    RutaNoAbsoluta,
}

/// <summary>
/// La carpeta donde se guardan las copias de la base (FR-01 de FEAT-010), fila única elegida en la configuración.
/// </summary>
/// <remarks>
/// Dos niveles, como <see cref="DatosEmisor"/>: <see cref="ParaGrabar"/> valida la ruta y devuelve la infracción del
/// campo; <see cref="Leer"/> reconstruye la fila guardada con las mismas reglas y lanza si no las cumple. Que la carpeta
/// exista, admita escritura y esté en otra unidad o en una carpeta de nube (FR-02, FR-03) lo comprueba Application con
/// su puerto, no esta clase: depende del sistema de archivos. Sin <c>ToString</c> con la ruta: puede llevar el nombre de
/// una persona (D-12).
/// </remarks>
public sealed class UbicacionBackup
{
    /// <summary>El largo máximo de la ruta recortada, el <c>MAX_PATH</c> clásico de Windows.</summary>
    public const int LargoMaximoRuta = 260;

    private UbicacionBackup(string ruta) => Ruta = ruta;

    /// <summary>Absoluta, recortada, de 1 a 260 caracteres, sin caracteres de control.</summary>
    public string Ruta { get; }

    /// <summary>
    /// La ruta que se va a grabar, tal como llega de la operadora. Sin infracciones, el resultado trae la entidad con la
    /// ruta recortada; si no, la única infracción del campo (gana la primera regla incumplida: faltante, caracteres,
    /// largo, absoluta). Nunca lanza.
    /// </summary>
    public static ResultadoUbicacionBackup ParaGrabar(string? ruta)
    {
        var recortada = ruta is null ? string.Empty : RecorteTexto.Recortar(ruta);
        return Evaluar(recortada) is { } infraccion
            ? ResultadoUbicacionBackup.Invalido(infraccion)
            : ResultadoUbicacionBackup.Valido(new UbicacionBackup(recortada));
    }

    /// <summary>La ubicación tal como está guardada (lectura), con las mismas reglas de <see cref="ParaGrabar"/>.</summary>
    /// <exception cref="ArgumentException">
    /// La ruta incumple alguna regla. El mensaje nombra el campo y no incluye el valor recibido.
    /// </exception>
    public static UbicacionBackup Leer(string ruta)
    {
        var recortada = ruta is null ? string.Empty : RecorteTexto.Recortar(ruta);
        if (Evaluar(recortada) is not null)
        {
            throw new ArgumentException("La ruta de la ubicación del backup no es válida.", nameof(ruta));
        }

        return new UbicacionBackup(recortada);
    }

    // Largo en string.Length sobre el texto recortado, como DatosEmisor. Path.IsPathFullyQualified depende del sistema
    // en que corre: en Windows exige letra de unidad con barra (C:\...) o UNC, y en Linux, una ruta que empiece con '/'
    // (ahí D:\Backups es relativa). La publicación es win-x64 (ADR-020), así que en producción rige la regla de Windows;
    // las pruebas usan rutas armadas sobre la carpeta temporal para valer en los dos.
    private static InfraccionUbicacionBackup? Evaluar(string recortada) =>
        recortada.Length == 0 ? InfraccionUbicacionBackup.RutaFaltante
        : !TieneCaracteresValidos(recortada) ? InfraccionUbicacionBackup.RutaConCaracteresInvalidos
        : recortada.Length > LargoMaximoRuta ? InfraccionUbicacionBackup.RutaDemasiadoLarga
        : !Path.IsPathFullyQualified(recortada) ? InfraccionUbicacionBackup.RutaNoAbsoluta
        : null;

    // Sin caracteres de control y UTF-16 válido: cada sustituto alto seguido de uno bajo, ningún sustituto bajo suelto.
    private static bool TieneCaracteresValidos(string texto)
    {
        for (var i = 0; i < texto.Length; i++)
        {
            var caracter = texto[i];
            if (char.IsControl(caracter) || char.IsLowSurrogate(caracter))
            {
                return false;
            }

            if (char.IsHighSurrogate(caracter))
            {
                if (i + 1 >= texto.Length || !char.IsLowSurrogate(texto[i + 1]))
                {
                    return false;
                }

                i++;
            }
        }

        return true;
    }
}

/// <summary>
/// El resultado de <see cref="UbicacionBackup.ParaGrabar"/>: la ubicación válida, o la infracción del campo <c>ruta</c>,
/// sin entidad.
/// </summary>
public sealed class ResultadoUbicacionBackup
{
    private ResultadoUbicacionBackup(UbicacionBackup? ubicacion, IReadOnlyList<InfraccionUbicacionBackup> infracciones)
    {
        Ubicacion = ubicacion;
        Infracciones = infracciones;
    }

    /// <summary>La ubicación lista para grabar; null si la ruta incumple las reglas.</summary>
    public UbicacionBackup? Ubicacion { get; }

    /// <summary>A lo sumo una infracción, la del campo <c>ruta</c>; vacío si la ruta es válida.</summary>
    public IReadOnlyList<InfraccionUbicacionBackup> Infracciones { get; }

    /// <summary>Verdadero si no hay infracciones: <see cref="Ubicacion"/> tiene la entidad.</summary>
    [MemberNotNullWhen(true, nameof(Ubicacion))]
    public bool EsValido => Ubicacion is not null;

    internal static ResultadoUbicacionBackup Valido(UbicacionBackup ubicacion) => new(ubicacion, []);

    internal static ResultadoUbicacionBackup Invalido(InfraccionUbicacionBackup infraccion) => new(null, [infraccion]);
}
