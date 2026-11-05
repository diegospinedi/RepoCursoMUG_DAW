using Microsoft.EntityFrameworkCore;
using Optica.Domain.Catalogo;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Artículos del catálogo sobre la tabla <c>Articulo</c>. Mapea la fila a la entidad de dominio sin aritmética: la
/// conversión a centésimos vive en <see cref="OpticaDbContext"/> (ADR-008). <see cref="Agregar"/>, <see cref="Actualizar"/>
/// y <see cref="CambiarEstado"/> solo dejan el cambio en el contexto compartido, que confirma
/// <see cref="UnidadDeTrabajoEf"/>; una clave repetida se informa ahí como conflicto de unicidad, por el índice UNIQUE de
/// la clave (ADR-009), y una versión o un estado que ya no son los leídos, como conflicto de concurrencia (D-08, D-22).
/// </summary>
public sealed class ArticuloRepository(OpticaDbContext contexto) : IArticuloRepository
{
    private const string MensajeFilaInvalida = "El artículo guardado no es válido.";

    private const long ValorActivo = 1;
    private const long ValorInactivo = 0;

    /// <summary>Compara la clave tal como llega, byte a byte: normalizarla es trabajo del dominio (ADR-009).</summary>
    public Task<bool> ExisteClaveAsync(string clave, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(clave);
        return contexto.Articulos.AsNoTracking().AnyAsync(a => a.CodigoProveedorClave == clave, cancelacion);
    }

    /// <summary>Como <see cref="ExisteClaveAsync"/>, sin contar al artículo de <paramref name="codigo"/>.</summary>
    public Task<bool> ExisteClaveDeOtroAsync(string clave, long codigo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(clave);
        return contexto.Articulos.AsNoTracking()
            .AnyAsync(a => a.CodigoProveedorClave == clave && a.Id != codigo, cancelacion);
    }

    /// <summary>Registra el alta con su estado y su versión: los DEFAULT de la tabla no participan (D-21).</summary>
    /// <exception cref="ArgumentException">El artículo ya tiene código: esto es un alta y el código lo asigna la base.</exception>
    public void Agregar(Articulo articulo)
    {
        ArgumentNullException.ThrowIfNull(articulo);
        if (articulo.Codigo is not null)
        {
            throw new ArgumentException("Un artículo nuevo no tiene código todavía.", nameof(articulo));
        }

        contexto.Articulos.Add(new ArticuloFila
        {
            CodigoProveedor = articulo.CodigoProveedor,
            CodigoProveedorClave = articulo.CodigoProveedorClave,
            Descripcion = articulo.Descripcion,
            PrecioCosto = articulo.PrecioCosto,
            MargenUtilidad = articulo.MargenUtilidad,
            PrecioVenta = articulo.PrecioVenta,
            Activo = articulo.Activo ? ValorActivo : ValorInactivo,
            Version = articulo.Version,
        });
    }

    /// <exception cref="InvalidOperationException">La fila no cumple la política: estado corrupto.</exception>
    public async Task<Articulo?> ObtenerAsync(long codigo, CancellationToken cancelacion = default)
    {
        var fila = await contexto.Articulos.AsNoTracking().SingleOrDefaultAsync(a => a.Id == codigo, cancelacion);
        return fila is null ? null : AEntidad(fila);
    }

    /// <exception cref="InvalidOperationException">La fila no cumple la política: estado corrupto.</exception>
    public async Task<Articulo?> ObtenerPorClaveAsync(string clave, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(clave);
        var fila = await contexto.Articulos.AsNoTracking()
            .SingleOrDefaultAsync(a => a.CodigoProveedorClave == clave, cancelacion);
        return fila is null ? null : AEntidad(fila);
    }

    /// <summary>
    /// Una página ordenada por <c>orden_articulo(Descripcion)</c> y después por código (D-01, D-15). Con texto, filtra
    /// con <c>instr</c> sobre la descripción y el código en el proveedor plegados con <c>plegar</c> y sobre el código como
    /// texto (D-05, D-06): sin <c>LIKE</c>, así <c>%</c> y <c>_</c> se buscan tal cual. El texto viaja como parámetro y
    /// llega ya plegado (Block 3). <see cref="PaginaArticulos.Total"/> cuenta con el mismo filtro.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="pagina"/> es menor que 1, <paramref name="tamano"/> no es positivo, o el desplazamiento de la
    /// página no entra en un <c>int</c> (D-21).
    /// </exception>
    /// <exception cref="InvalidOperationException">Alguna fila no cumple la política: estado corrupto.</exception>
    public async Task<PaginaArticulos> BuscarAsync(
        string? textoPlegado, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pagina, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tamano);
        var desplazamiento = (pagina - 1L) * tamano;
        if (desplazamiento > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(pagina), "La página pedida no es válida.");
        }

        var filtradas = contexto.Articulos.AsNoTracking();
        if (textoPlegado is not null)
        {
            // EF Core traduce Contains sobre texto a instr(...) > 0 en SQLite, no a LIKE.
            filtradas = filtradas.Where(a =>
                FuncionesSqlite.Plegar(a.Descripcion).Contains(textoPlegado)
                || FuncionesSqlite.Plegar(a.CodigoProveedor).Contains(textoPlegado)
                || a.Id.ToString().Contains(textoPlegado));
        }

        var filas = await filtradas
            .OrderBy(a => FuncionesSqlite.OrdenArticulo(a.Descripcion))
            .ThenBy(a => a.Id)
            .Skip((int)desplazamiento)
            .Take(tamano)
            .ToListAsync(cancelacion);
        var total = await filtradas.CountAsync(cancelacion);

        // Las dos consultas no comparten transacción. Un alta, o una edición de la descripción que haga entrar o salir un
        // artículo del filtro, entre una y otra deja el total aproximado; no hay bajas físicas. Se acepta (D-24).
        // Math.Max solo evita que PaginaArticulos lance si el total quedó por debajo de lo que trajo la página.
        return new PaginaArticulos([.. filas.Select(AEntidad)], Math.Max(total, filas.Count));
    }

    /// <summary>
    /// Deja la fila marcada como modificada en el contexto, con el patrón de
    /// <see cref="CredencialAccesoRepository.Reemplazar"/>: reutiliza la entrada que el contexto ya sigue o adjunta una.
    /// Solo se escriben el código en el proveedor, su clave, la descripción, los tres importes y la versión, que pasa a
    /// <paramref name="versionLeida"/> + 1. El <c>UPDATE</c> filtra por <c>Version = versionLeida</c> y
    /// <c>Activo = 1</c> (tokens de concurrencia): si otra escritura confirmó en el medio, o el artículo está inactivo,
    /// afecta 0 filas y la unidad de trabajo lo informa como conflicto de concurrencia (D-08, D-22).
    /// <see cref="Articulo.Version"/> no se usa: vale la leída (D-17).
    /// </summary>
    /// <exception cref="ArgumentException">El artículo no tiene código.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="versionLeida"/> es negativa o la siguiente no entra en un <c>long</c>.
    /// </exception>
    public void Actualizar(Articulo articulo, long versionLeida)
    {
        ArgumentNullException.ThrowIfNull(articulo);
        if (articulo.Codigo is not { } codigo)
        {
            throw new ArgumentException("El artículo a modificar no tiene código.", nameof(articulo));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(versionLeida);
        ArgumentOutOfRangeException.ThrowIfEqual(versionLeida, long.MaxValue);

        var entrada = contexto.Articulos.Local.FindEntry(codigo)
            ?? contexto.Articulos.Attach(new ArticuloFila
            {
                Id = codigo,
                CodigoProveedor = articulo.CodigoProveedor,
                CodigoProveedorClave = articulo.CodigoProveedorClave,
                Descripcion = articulo.Descripcion,
                PrecioCosto = articulo.PrecioCosto,
                MargenUtilidad = articulo.MargenUtilidad,
                PrecioVenta = articulo.PrecioVenta,
                Activo = ValorActivo,
                Version = versionLeida,
            });

        // Activo no se escribe: solo condiciona el UPDATE, con valor original verdadero (D-22).
        var activo = entrada.Property(a => a.Activo);
        activo.OriginalValue = ValorActivo;
        activo.CurrentValue = ValorActivo;
        activo.IsModified = false;

        // El valor original es el leído, no el que tenga la entrada: el contexto podría seguir una versión vieja.
        var version = entrada.Property(a => a.Version);
        version.OriginalValue = versionLeida;
        version.CurrentValue = versionLeida + 1;
        version.IsModified = true;

        Modificar(entrada.Property(a => a.CodigoProveedor), articulo.CodigoProveedor);
        Modificar(entrada.Property(a => a.CodigoProveedorClave), articulo.CodigoProveedorClave);
        Modificar(entrada.Property(a => a.Descripcion), articulo.Descripcion);
        Modificar(entrada.Property(a => a.PrecioCosto), articulo.PrecioCosto);
        Modificar(entrada.Property(a => a.MargenUtilidad), articulo.MargenUtilidad);
        Modificar(entrada.Property(a => a.PrecioVenta), articulo.PrecioVenta);
    }

    /// <summary>
    /// Lee la fila con seguimiento (<c>Find</c>, D-22) y cambia solo el estado y la versión (+1), sin reescribir los
    /// demás datos (D-09). Precondición: el llamador ya verificó que <paramref name="activo"/> difiere del estado
    /// guardado, porque el método siempre incrementa la versión, aunque el estado pedido sea el actual. Los tokens de
    /// concurrencia (versión y estado) son los de la lectura con <c>Find</c>: el <c>UPDATE</c> solo detecta las escrituras
    /// que confirmen entre esa lectura y la confirmación de la unidad de trabajo, que las informa como conflicto de
    /// concurrencia (D-16); lo que haya pasado antes de la lectura ya está en los valores leídos.
    /// </summary>
    /// <exception cref="InvalidOperationException">No existe un artículo con ese código; el caso de uso lo verifica antes.</exception>
    public void CambiarEstado(long codigo, bool activo)
    {
        var fila = contexto.Articulos.Find(codigo)
            ?? throw new InvalidOperationException("El artículo a cambiar de estado no existe.");

        fila.Activo = activo ? ValorActivo : ValorInactivo;
        fila.Version = checked(fila.Version + 1);
    }

    private static void Modificar<T>(
        Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<ArticuloFila, T> propiedad, T valor)
    {
        propiedad.CurrentValue = valor;
        propiedad.IsModified = true;
    }

    /// <summary>
    /// La entidad de una fila. Una fila que eludió los CHECK (incluido un <c>Activo</c> fuera de 0 y 1 o una versión
    /// negativa), o cuya clave guardada no es la que el dominio calcula para su código (la unicidad dejaría de valer),
    /// no puede representarse: se informa como estado corrupto y no con el <see cref="ArgumentException"/> del dominio,
    /// que significa entrada inválida. El mensaje no lleva valores.
    /// </summary>
    private static Articulo AEntidad(ArticuloFila fila)
    {
        if (fila.Activo is not (ValorActivo or ValorInactivo))
        {
            throw new InvalidOperationException(MensajeFilaInvalida);
        }

        Articulo articulo;
        try
        {
            articulo = new Articulo(
                fila.Id, fila.CodigoProveedor, fila.Descripcion, fila.PrecioCosto, fila.MargenUtilidad, fila.PrecioVenta,
                activo: fila.Activo == ValorActivo, fila.Version);
        }
        catch (ArgumentException error)
        {
            // La causa nombra el campo y no el valor (invariante del dominio), así que se puede adjuntar.
            throw new InvalidOperationException(MensajeFilaInvalida, error);
        }

        return string.Equals(articulo.CodigoProveedorClave, fila.CodigoProveedorClave, StringComparison.Ordinal)
            ? articulo
            : throw new InvalidOperationException(MensajeFilaInvalida);
    }
}
