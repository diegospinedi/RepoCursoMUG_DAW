using QuestPDF.Infrastructure;

namespace Optica.Infrastructure.Pdf;

/// <summary>
/// La configuración global de QuestPDF en un solo lugar (D-25 de FEAT-009), para los dos generadores de PDF (presupuesto
/// y factura): licencia Community (D-01, D-13 de FEAT-006), sin fuentes del sistema para que el PDF sea el mismo en
/// Windows y en Linux (D-14), una familia que falta (el Arial del SVG de ejemplo) se dibuja con la fuente incluida
/// (D-14) y un carácter que la fuente no tiene se dibuja con el símbolo de reemplazo (D-15).
/// </summary>
internal static class ConfiguracionQuestPdf
{
    /// <summary>Se ejecuta una sola vez, antes del primer acceso a <see cref="Asegurar"/>.</summary>
    static ConfiguracionQuestPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = false;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
    }

    /// <summary>
    /// Garantiza que la configuración global quedó fijada. Cada generador lo llama antes de su primer documento; las
    /// llamadas siguientes no hacen nada.
    /// </summary>
    public static void Asegurar()
    {
        // El cuerpo vacío alcanza: llamar a un miembro estático ejecuta el constructor estático una sola vez.
    }
}
