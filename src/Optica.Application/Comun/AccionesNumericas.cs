namespace Optica.Application.Comun;

/// <summary>
/// Las acciones de las reglas numéricas compartidas (<c>Optica.Domain.Comun.ReglasImporte</c>), con el mismo texto en
/// todos los módulos que guardan importes o porcentajes (D-12 de FEAT-002a). Texto fijo: no repiten el valor recibido.
/// </summary>
public static class AccionesNumericas
{
    /// <summary>El valor tiene más de 2 decimales.</summary>
    public const string HastaDosDecimales = "Ingresá hasta 2 decimales.";

    /// <summary>El valor no entra en el tipo (ADR-008).</summary>
    public const string ValorMenor = "Ingresá un valor menor.";
}
