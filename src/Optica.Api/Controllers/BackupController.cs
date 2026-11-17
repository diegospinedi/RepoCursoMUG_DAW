using Microsoft.AspNetCore.Mvc;
using Optica.Api.Errores;
using Optica.Application.Backup;

namespace Optica.Api.Controllers;

/// <summary>
/// El estado del backup para el aviso al ingresar (FEAT-010, FR-09). Solo traduce HTTP: delega en
/// <see cref="ObtenerEstadoBackup"/>. Exige sesión por la política por defecto (no está en
/// <see cref="Acceso.RutasAnonimas"/>): sin sesión, 401. La respuesta lleva <c>no-store</c> como toda la Api y nunca la
/// ruta del backup (D-12).
/// </summary>
[ApiController]
[Route("api/backup")]
public sealed class BackupController(ObtenerEstadoBackup obtenerEstado) : ControllerBase
{
    /// <summary>
    /// 200 con <c>{ faltaAyer, ultimo, ubicacion, accesible }</c>: <c>ultimo</c> es <c>yyyy-MM-dd</c> o null y
    /// <c>ubicacion</c>, <c>elegida</c> o <c>sin_elegir</c>. Una carpeta desconectada o ilegible responde
    /// <c>accesible: false</c>, no un error; una fila corrupta termina en el 500 genérico de
    /// <see cref="ManejadorErroresNoControlados"/>.
    /// </summary>
    [HttpGet("estado")]
    public async Task<ActionResult<EstadoBackupVigente>> ObtenerEstado(CancellationToken cancelacion) =>
        await obtenerEstado.EjecutarAsync(cancelacion);
}
