using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Domain.Configuracion;

namespace Optica.Application.Tests.Catalogo;

/// <summary>
/// Catálogo en memoria que imita la unidad de trabajo: <see cref="Agregar"/> deja el alta pendiente y solo
/// <see cref="ConfirmarAsync"/> la guarda con el siguiente código autonumérico, como el repositorio real.
/// </summary>
internal sealed class ArticulosEnMemoria : IArticuloRepository, IUnidadDeTrabajo
{
    private readonly List<Articulo> _pendientes = [];
    private long _ultimoCodigo;

    public List<Articulo> Guardados { get; } = [];

    public int Agregados { get; private set; }

    public int Confirmaciones { get; private set; }

    public int ConsultasDeClave { get; private set; }

    /// <summary>Si tiene valor, <see cref="ConfirmarAsync"/> lo lanza y no guarda nada.</summary>
    public Exception? ErrorAlConfirmar { get; set; }

    /// <summary>Si es verdadero, <see cref="ObtenerPorClaveAsync"/> devuelve null aunque el artículo exista.</summary>
    public bool OcultarAlLeerPorClave { get; set; }

    /// <summary>Guarda un artículo ya confirmado, como si existiera de antes.</summary>
    public Articulo Sembrar(string codigoProveedor, string descripcion = "Existente", decimal costo = 100m)
    {
        // Provisorio hasta FEAT-002b Block 3: el doble siembra siempre activos con versión 0.
        var articulo = new Articulo(
            ++_ultimoCodigo, codigoProveedor, descripcion, costo, 50m, costo * 1.5m, activo: true, version: 0);
        Guardados.Add(articulo);
        return articulo;
    }

    public Task<bool> ExisteClaveAsync(string clave, CancellationToken cancelacion = default)
    {
        ConsultasDeClave++;
        return Task.FromResult(Guardados.Any(articulo => articulo.CodigoProveedorClave == clave));
    }

    public void Agregar(Articulo articulo)
    {
        ArgumentNullException.ThrowIfNull(articulo);
        if (articulo.Codigo is not null)
        {
            throw new InvalidOperationException("El artículo ya tiene código.");
        }

        Agregados++;
        _pendientes.Add(articulo);
    }

    public Task<Articulo?> ObtenerAsync(long codigo, CancellationToken cancelacion = default) =>
        Task.FromResult(Guardados.SingleOrDefault(articulo => articulo.Codigo == codigo));

    /// <summary>Se ejecuta al confirmar con éxito, después de guardar (por ejemplo, para cancelar el pedido).</summary>
    public Action? DespuesDeConfirmar { get; set; }

    public Task<Articulo?> ObtenerPorClaveAsync(string clave, CancellationToken cancelacion = default)
    {
        // Como EF Core: un token cancelado corta la lectura.
        cancelacion.ThrowIfCancellationRequested();
        return Task.FromResult(
            OcultarAlLeerPorClave ? null : Guardados.SingleOrDefault(articulo => articulo.CodigoProveedorClave == clave));
    }

    // Provisorio hasta FEAT-002b Block 3: los cuatro miembros del contrato nuevo lanzan; el alta no los usa.
    public Task<PaginaArticulos> BuscarAsync(
        string? textoPlegado, int pagina, int tamano, CancellationToken cancelacion = default) =>
        throw new NotSupportedException("Pendiente de FEAT-002b, Block 3.");

    public Task<bool> ExisteClaveDeOtroAsync(string clave, long codigo, CancellationToken cancelacion = default) =>
        throw new NotSupportedException("Pendiente de FEAT-002b, Block 3.");

    public void Actualizar(Articulo articulo, long versionLeida) =>
        throw new NotSupportedException("Pendiente de FEAT-002b, Block 3.");

    public void CambiarEstado(long codigo, bool activo) =>
        throw new NotSupportedException("Pendiente de FEAT-002b, Block 3.");

    public Task ConfirmarAsync(CancellationToken cancelacion = default)
    {
        Confirmaciones++;
        if (ErrorAlConfirmar is { } error)
        {
            _pendientes.Clear();
            throw error;
        }

        foreach (var pendiente in _pendientes)
        {
            Guardados.Add(new Articulo(
                ++_ultimoCodigo,
                pendiente.CodigoProveedor,
                pendiente.Descripcion,
                pendiente.PrecioCosto,
                pendiente.MargenUtilidad,
                pendiente.PrecioVenta,
                // Provisorio hasta FEAT-002b Block 3: el doble guarda siempre activos con versión 0.
                activo: true,
                version: 0));
        }

        _pendientes.Clear();
        DespuesDeConfirmar?.Invoke();
        return Task.CompletedTask;
    }
}

/// <summary>
/// La configuración de negocio en memoria, leída en cada llamada. Sin fila lanza
/// <see cref="InvalidOperationException"/>, como el repositorio real (D-04 de FEAT-001c).
/// </summary>
internal sealed class ConfiguracionVariable : IConfiguracionNegocioRepository
{
    public ConfiguracionNegocio? Vigente { get; set; } =
        new(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null);

    public int Lecturas { get; private set; }

    public Task<ConfiguracionNegocio> ObtenerAsync(CancellationToken cancelacion = default)
    {
        Lecturas++;
        return Vigente is null
            ? throw new InvalidOperationException("No hay fila de configuración de negocio.")
            : Task.FromResult(Vigente);
    }

    public void Reemplazar(ConfiguracionNegocio nueva) =>
        throw new InvalidOperationException("El alta de artículos no reemplaza la configuración.");
}
