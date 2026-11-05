using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Domain.Configuracion;

namespace Optica.Application.Tests.Catalogo;

/// <summary>
/// Catálogo en memoria que imita la unidad de trabajo: <see cref="Agregar"/>, <see cref="Actualizar"/> y
/// <see cref="CambiarEstado"/> dejan el cambio pendiente y solo <see cref="ConfirmarAsync"/> lo guarda, como el
/// repositorio real: el alta con el siguiente código autonumérico; la modificación y el cambio de estado incrementan la
/// versión y fallan con <see cref="ConflictoAlConfirmarException"/> (motivo <see cref="MotivoConflicto.Concurrencia"/>)
/// si el artículo se borró o su versión o su estado ya no son los leídos (D-08, D-22), o con
/// <see cref="MotivoConflicto.Unicidad"/> si la clave ya la tiene otro artículo. <see cref="AntesDeConfirmar"/> permite
/// simular una escritura cruzada entre la lectura y la confirmación.
/// </summary>
/// <remarks>
/// <see cref="EjecutarEnTransaccionAsync"/> imita la transacción de ADR-013 (D-20 de FEAT-002c): guarda una copia de lo
/// guardado al entrar y la restaura si la operación lanza, rechaza el anidamiento con
/// <see cref="InvalidOperationException"/> y permite simular una falla del commit externo con
/// <see cref="ErrorAlConfirmarOperacion"/> (la falla de <see cref="ConfirmarAsync"/> sigue siendo
/// <see cref="ErrorAlConfirmar"/>). <see cref="RegistrarPrecioRecalculado"/> registra aparte, en
/// <see cref="PreciosRecalculadosRegistrados"/>, y acepta artículos inactivos (AC-03).
/// </remarks>
internal sealed class ArticulosEnMemoria : IArticuloRepository, IUnidadDeTrabajo
{
    private readonly List<Articulo> _pendientes = [];
    private readonly List<(Articulo Articulo, long VersionLeida)> _actualizaciones = [];
    private readonly List<(long Codigo, bool Activo, long VersionLeida)> _cambiosDeEstado = [];
    private readonly List<Articulo> _preciosRecalculados = [];
    private long _ultimoCodigo;
    private bool _enOperacion;

    public List<Articulo> Guardados { get; } = [];

    public int Agregados { get; private set; }

    public int Confirmaciones { get; private set; }

    public int ConsultasDeClave { get; private set; }

    public int ConsultasDeClaveDeOtro { get; private set; }

    public int Actualizaciones { get; private set; }

    public int CambiosDeEstado { get; private set; }

    public int Busquedas { get; private set; }

    /// <summary>Los argumentos de la última llamada a <see cref="BuscarAsync"/>; null si no hubo ninguna.</summary>
    public (string? TextoPlegado, int Pagina, int Tamano)? UltimaBusqueda { get; private set; }

    /// <summary>Si tiene valor, <see cref="ConfirmarAsync"/> lo lanza y no guarda nada.</summary>
    public Exception? ErrorAlConfirmar { get; set; }

    /// <summary>Si es verdadero, <see cref="ObtenerPorClaveAsync"/> devuelve null aunque el artículo exista.</summary>
    public bool OcultarAlLeerPorClave { get; set; }

    /// <summary>
    /// Se ejecuta al empezar <see cref="ConfirmarAsync"/>, antes de guardar: sirve para simular otra escritura que
    /// confirma entre la lectura y la grabación (por ejemplo, con <see cref="EscribirDesdeOtraPestana"/>).
    /// </summary>
    public Action? AntesDeConfirmar { get; set; }

    /// <summary>Se ejecuta al confirmar con éxito, después de guardar (por ejemplo, para cancelar el pedido).</summary>
    public Action? DespuesDeConfirmar { get; set; }

    /// <summary>Cuántas operaciones recibió <see cref="EjecutarEnTransaccionAsync"/>, incluidas las que fallaron.</summary>
    public int Operaciones { get; private set; }

    /// <summary>Cuántas operaciones terminaron con el commit externo.</summary>
    public int OperacionesConfirmadas { get; private set; }

    /// <summary>
    /// Si tiene valor, el commit externo de <see cref="EjecutarEnTransaccionAsync"/> lo lanza después de que la operación
    /// terminó, y lo guardado vuelve a como estaba al entrar.
    /// </summary>
    public Exception? ErrorAlConfirmarOperacion { get; set; }

    /// <summary>Cuántas veces se llamó a <see cref="ListarParaRecalculoAsync"/>.</summary>
    public int ListadosParaRecalculo { get; private set; }

    /// <summary>Cada artículo recibido por <see cref="RegistrarPrecioRecalculado"/>, en orden, se haya guardado o no.</summary>
    public List<Articulo> PreciosRecalculadosRegistrados { get; } = [];

    /// <summary>Guarda un artículo ya confirmado, como si existiera de antes; el precio de venta es costo × 1,5.</summary>
    public Articulo Sembrar(
        string codigoProveedor,
        string descripcion = "Existente",
        decimal costo = 100m,
        bool activo = true,
        long version = 0)
    {
        var articulo = new Articulo(
            ++_ultimoCodigo, codigoProveedor, descripcion, costo, 50m, costo * 1.5m, activo, version);
        Guardados.Add(articulo);
        return articulo;
    }

    /// <summary>El artículo guardado con ese código; falla si no existe.</summary>
    public Articulo Guardado(long codigo) => Guardados.Single(articulo => articulo.Codigo == codigo);

    /// <summary>
    /// Simula otra escritura ya confirmada sobre el artículo: incrementa su versión y, si se indica, cambia su estado;
    /// con <paramref name="borrar"/> lo quita del catálogo.
    /// </summary>
    /// <remarks>
    /// <paramref name="borrar"/> simula un borrado que el producto no hace (no hay bajas físicas de artículos): existe
    /// solo para cubrir la rama <c>NoEncontrado</c> de la relectura tras un conflicto de concurrencia.
    /// </remarks>
    public void EscribirDesdeOtraPestana(long codigo, bool? activo = null, bool borrar = false)
    {
        var actual = Guardado(codigo);
        Guardados.Remove(actual);
        if (!borrar)
        {
            Guardados.Add(Copia(actual, activo ?? actual.Activo, actual.Version + 1));
        }
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

    public Task<Articulo?> ObtenerAsync(long codigo, CancellationToken cancelacion = default)
    {
        // Como EF Core: un token cancelado corta la lectura.
        cancelacion.ThrowIfCancellationRequested();
        return Task.FromResult(Guardados.SingleOrDefault(articulo => articulo.Codigo == codigo));
    }

    public Task<Articulo?> ObtenerPorClaveAsync(string clave, CancellationToken cancelacion = default)
    {
        // Como EF Core: un token cancelado corta la lectura.
        cancelacion.ThrowIfCancellationRequested();
        return Task.FromResult(
            OcultarAlLeerPorClave ? null : Guardados.SingleOrDefault(articulo => articulo.CodigoProveedorClave == clave));
    }

    /// <summary>
    /// Como el repositorio real: filtra por el texto plegado en la descripción, el código en el proveedor o el código
    /// decimal, ordena por <see cref="PlegadoBusqueda.ClaveOrden"/> de la descripción y después por código, y pagina.
    /// </summary>
    public Task<PaginaArticulos> BuscarAsync(
        string? textoPlegado, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pagina, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(tamano, 1);
        Busquedas++;
        UltimaBusqueda = (textoPlegado, pagina, tamano);

        var coincidentes = Guardados
            .Where(articulo => textoPlegado is null
                || PlegadoBusqueda.Plegar(articulo.Descripcion).Contains(textoPlegado, StringComparison.Ordinal)
                || PlegadoBusqueda.Plegar(articulo.CodigoProveedor).Contains(textoPlegado, StringComparison.Ordinal)
                || articulo.Codigo!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    .Contains(textoPlegado, StringComparison.Ordinal))
            .OrderBy(articulo => PlegadoBusqueda.ClaveOrden(articulo.Descripcion), StringComparer.Ordinal)
            .ThenBy(articulo => articulo.Codigo)
            .ToList();
        var desplazamiento = (long)(pagina - 1) * tamano;
        var deLaPagina = coincidentes.Skip((int)Math.Min(desplazamiento, int.MaxValue)).Take(tamano).ToList();
        return Task.FromResult(new PaginaArticulos(deLaPagina, coincidentes.Count));
    }

    public Task<bool> ExisteClaveDeOtroAsync(string clave, long codigo, CancellationToken cancelacion = default)
    {
        ConsultasDeClaveDeOtro++;
        return Task.FromResult(
            Guardados.Any(articulo => articulo.CodigoProveedorClave == clave && articulo.Codigo != codigo));
    }

    public void Actualizar(Articulo articulo, long versionLeida)
    {
        ArgumentNullException.ThrowIfNull(articulo);
        if (articulo.Codigo is null)
        {
            throw new InvalidOperationException("El artículo no tiene código.");
        }

        Actualizaciones++;
        _actualizaciones.Add((articulo, versionLeida));
    }

    public void CambiarEstado(long codigo, bool activo)
    {
        // Como el repositorio real: lee la fila al registrar el cambio y la versión leída es el token.
        var actual = Guardados.SingleOrDefault(articulo => articulo.Codigo == codigo)
            ?? throw new InvalidOperationException("No existe el artículo cuyo estado se cambia.");
        CambiosDeEstado++;
        _cambiosDeEstado.Add((codigo, activo, actual.Version));
    }

    /// <summary>Como el repositorio real: todos, activos e inactivos, ordenados por código.</summary>
    public Task<IReadOnlyList<Articulo>> ListarParaRecalculoAsync(CancellationToken cancelacion = default)
    {
        cancelacion.ThrowIfCancellationRequested();
        ListadosParaRecalculo++;
        return Task.FromResult<IReadOnlyList<Articulo>>([.. Guardados.OrderBy(articulo => articulo.Codigo)]);
    }

    /// <summary>
    /// Registra el precio recalculado; al confirmar, falla por concurrencia si el estado o la versión del artículo ya no
    /// son los guardados, y si no, cambia solo el precio de venta e incrementa la versión (D-04).
    /// </summary>
    public void RegistrarPrecioRecalculado(Articulo articulo)
    {
        ArgumentNullException.ThrowIfNull(articulo);
        if (articulo.Codigo is null)
        {
            throw new ArgumentException("El artículo no tiene código.", nameof(articulo));
        }

        PreciosRecalculadosRegistrados.Add(articulo);
        _preciosRecalculados.Add(articulo);
    }

    public async Task<T> EjecutarEnTransaccionAsync<T>(
        Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(operacion);
        if (_enOperacion)
        {
            throw new InvalidOperationException("La operación en transacción no se anida.");
        }

        Operaciones++;
        var guardadosAlEntrar = new List<Articulo>(Guardados);
        var ultimoCodigoAlEntrar = _ultimoCodigo;
        _enOperacion = true;
        try
        {
            var resultado = await operacion(cancelacion);
            if (ErrorAlConfirmarOperacion is { } error)
            {
                throw error;
            }

            OperacionesConfirmadas++;
            return resultado;
        }
        catch
        {
            Guardados.Clear();
            Guardados.AddRange(guardadosAlEntrar);
            _ultimoCodigo = ultimoCodigoAlEntrar;
            DescartarPendientes();
            throw;
        }
        finally
        {
            _enOperacion = false;
        }
    }

    public Task ConfirmarAsync(CancellationToken cancelacion = default)
    {
        Confirmaciones++;
        AntesDeConfirmar?.Invoke();
        try
        {
            if (ErrorAlConfirmar is { } error)
            {
                throw error;
            }

            Guardar();
        }
        finally
        {
            DescartarPendientes();
        }

        DespuesDeConfirmar?.Invoke();
        return Task.CompletedTask;
    }

    // Todo o nada: valida los cambios sobre una copia y solo entonces la publica.
    private void Guardar()
    {
        var resultado = new List<Articulo>(Guardados);
        var ultimoCodigo = _ultimoCodigo;

        foreach (var (articulo, versionLeida) in _actualizaciones)
        {
            var actual = resultado.SingleOrDefault(guardado => guardado.Codigo == articulo.Codigo);
            if (actual is null || actual.Version != versionLeida || !actual.Activo)
            {
                throw Conflicto(MotivoConflicto.Concurrencia);
            }

            if (resultado.Any(guardado =>
                    guardado.Codigo != articulo.Codigo && guardado.CodigoProveedorClave == articulo.CodigoProveedorClave))
            {
                throw Conflicto(MotivoConflicto.Unicidad);
            }

            resultado.Remove(actual);
            // Ignora la versión del artículo recibido: la persistencia la incrementa desde la leída (D-17).
            resultado.Add(Copia(articulo, activo: true, versionLeida + 1));
        }

        foreach (var (codigo, activo, versionLeida) in _cambiosDeEstado)
        {
            var actual = resultado.SingleOrDefault(guardado => guardado.Codigo == codigo);
            if (actual is null || actual.Version != versionLeida)
            {
                throw Conflicto(MotivoConflicto.Concurrencia);
            }

            resultado.Remove(actual);
            resultado.Add(Copia(actual, activo, actual.Version + 1));
        }

        foreach (var recalculado in _preciosRecalculados)
        {
            // Los tokens son el estado y la versión leídos; un inactivo también se actualiza (AC-03).
            var actual = resultado.SingleOrDefault(guardado => guardado.Codigo == recalculado.Codigo);
            if (actual is null || actual.Version != recalculado.Version || actual.Activo != recalculado.Activo)
            {
                throw Conflicto(MotivoConflicto.Concurrencia);
            }

            resultado.Remove(actual);
            resultado.Add(new Articulo(
                actual.Codigo,
                actual.CodigoProveedor,
                actual.Descripcion,
                actual.PrecioCosto,
                actual.MargenUtilidad,
                recalculado.PrecioVenta,
                actual.Activo,
                checked(actual.Version + 1)));
        }

        foreach (var pendiente in _pendientes)
        {
            if (resultado.Any(guardado => guardado.CodigoProveedorClave == pendiente.CodigoProveedorClave))
            {
                throw Conflicto(MotivoConflicto.Unicidad);
            }

            resultado.Add(new Articulo(
                ++ultimoCodigo,
                pendiente.CodigoProveedor,
                pendiente.Descripcion,
                pendiente.PrecioCosto,
                pendiente.MargenUtilidad,
                pendiente.PrecioVenta,
                pendiente.Activo,
                pendiente.Version));
        }

        Guardados.Clear();
        Guardados.AddRange(resultado);
        _ultimoCodigo = ultimoCodigo;
    }

    private void DescartarPendientes()
    {
        _pendientes.Clear();
        _actualizaciones.Clear();
        _cambiosDeEstado.Clear();
        _preciosRecalculados.Clear();
    }

    private static ConflictoAlConfirmarException Conflicto(MotivoConflicto motivo) =>
        new("Conflicto al confirmar.", motivo);

    private static Articulo Copia(Articulo articulo, bool activo, long version) => new(
        articulo.Codigo,
        articulo.CodigoProveedor,
        articulo.Descripcion,
        articulo.PrecioCosto,
        articulo.MargenUtilidad,
        articulo.PrecioVenta,
        activo,
        version);
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
