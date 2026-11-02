using Optica.Domain.Marca;

namespace Optica.Application.Marca;

/// <summary>
/// Entrega la identidad visual vigente: la marca instalada de la óptica o, si falta o es inválida, la marca de
/// ejemplo. Lanza solo si tampoco la marca de ejemplo se puede leer (instalación dañada).
/// </summary>
public interface IProveedorMarca
{
    IdentidadVisual Obtener();
}
