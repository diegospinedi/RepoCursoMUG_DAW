using Microsoft.AspNetCore.Mvc;
using Optica.Api.Acceso;
using Optica.Application.Facturacion;

namespace Optica.Api.Controllers;

/// <summary>
/// La respuesta 200 de <c>GET /api/facturacion/emisiones-pendientes</c>: <c>{ "presupuestos": [155, 160] }</c>, solo los
/// números de los presupuestos con una emisión pendiente de verificación, de menor a mayor (FEAT-008, FR-03).
/// </summary>
public sealed record EmisionesPendientes(IReadOnlyList<long> Presupuestos);

/// <summary>
/// Facturación fuera de un presupuesto (FEAT-008): la lista de emisiones pendientes para el aviso de la pantalla de
/// inicio (FR-03, AC-03, AC-04). Solo traduce HTTP. Exige sesión por la política por defecto (no está en
/// <see cref="RutasAnonimas"/>): sin sesión, 401. La respuesta lleva solo números, sin datos del cliente ni de la
/// pendiente, y <c>no-store</c> como todo <c>/api</c>.
/// </summary>
[ApiController]
[Route("api/facturacion")]
public sealed class FacturacionController(ListarEmisionesPendientes listarEmisionesPendientes) : ControllerBase
{
    /// <summary>Los presupuestos con una emisión pendiente, de menor a mayor; sin pendientes, la lista vacía.</summary>
    [HttpGet("emisiones-pendientes")]
    public async Task<IActionResult> ListarPendientes(CancellationToken cancelacion) =>
        Ok(new EmisionesPendientes(await listarEmisionesPendientes.EjecutarAsync(cancelacion)));
}
