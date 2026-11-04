namespace Optica.Domain.Configuracion;

/// <summary>
/// Condición fiscal del emisor frente al IVA (FR-02). Su texto, el del contrato de la Api y el de la base, se
/// obtiene solo con <see cref="PoliticaConfiguracionNegocio.TextoDe"/> y <see cref="PoliticaConfiguracionNegocio.LeerCondicion"/>.
/// </summary>
public enum CondicionFiscal
{
    ResponsableInscripto,
    Monotributo,
}
