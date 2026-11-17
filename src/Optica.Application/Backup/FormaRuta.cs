namespace Optica.Application.Backup;

/// <summary>
/// Reglas sobre la forma del texto de una ruta, sin tocar el sistema de archivos (D-17). Están en Application porque las
/// usan la verificación de una copia a restaurar (<see cref="RestaurarBackup"/>) y la inspección de la ubicación del
/// backup en la infraestructura (<c>InspectorUbicaciones</c>): un solo criterio para las dos.
/// </summary>
public static class FormaRuta
{
    /// <summary>
    /// Verdadero si la ruta empieza con dos separadores (<c>\\servidor\recurso</c>, <c>//servidor/recurso</c>,
    /// <c>\\?\</c>, <c>\\.\</c>): UNC o de dispositivo. Abrir una UNC inicia una autenticación SMB contra el servidor que
    /// nombra (y puede entregarle el hash NTLM de la cuenta), y una de dispositivo esquiva la normalización de la ruta. Se
    /// decide por el texto, igual en Windows que en Linux.
    /// </summary>
    public static bool EsUncODispositivo(string ruta) =>
        ruta is { Length: >= 2 } && EsSeparador(ruta[0]) && EsSeparador(ruta[1]);

    private static bool EsSeparador(char caracter) => caracter is '\\' or '/';
}
