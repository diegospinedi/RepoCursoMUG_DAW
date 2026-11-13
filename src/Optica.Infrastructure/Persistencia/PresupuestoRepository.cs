using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Optica.Domain.Presupuestos;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Presupuestos sobre las tablas <c>Presupuesto</c> y <c>LineaPresupuesto</c> (FEAT-003a). Mapea filas y entidades sin
/// aritmética: la conversión a centésimos vive en <see cref="OpticaDbContext"/> (ADR-008) y la fecha usa el mapeo de
/// <see cref="DateOnly"/> de EF Core a TEXT <c>yyyy-MM-dd</c>. <see cref="Agregar"/> solo deja el alta en el contexto
/// compartido, que confirma <see cref="UnidadDeTrabajoEf"/>; un número repetido se informa ahí como conflicto de
/// unicidad, por la clave primaria (D-07, ADR-015).
/// </summary>
/// <remarks>
/// Lleva datos personales (Ley 25.326): no registra nada en el log y sus mensajes de error son fijos, sin valores (D-17).
/// </remarks>
public sealed class PresupuestoRepository(OpticaDbContext contexto) : IPresupuestoRepository
{
    private const string MensajeFilaInvalida = "El presupuesto guardado no es válido.";
    private const string MensajePresupuestoInvalido = "El presupuesto a grabar no tiene número, cliente o líneas.";
    private const string MensajeOtroPresupuesto = "El presupuesto modificado no es el mismo que el original.";
    private const string MensajeNoFinal = "Solo se registra el paso a Final de un presupuesto en Final.";
    private const string MensajeVersionOriginal = "La versión leída del presupuesto no es válida.";
    private const string MensajeAltaNoBorrador = "Un presupuesto nuevo se graba en Borrador.";
    private const string MensajePaginaInvalida = "La página pedida no es válida.";
    private const string MensajeTamanoPaginaInvalido = "El tamaño de página no es válido.";

    private const string MensajeEntradaConOtrosCambios =
        "El presupuesto ya tiene otros cambios registrados en el contexto.";

    /// <summary>Las columnas que escribe <see cref="CambiarEstado"/>: una entrada seguida solo puede traer estas modificadas.</summary>
    private static readonly HashSet<string> ColumnasDeCambiarEstado =
        new([nameof(PresupuestoFila.Estado), nameof(PresupuestoFila.Version)], StringComparer.Ordinal);

    /// <summary>
    /// Las columnas que escribe <see cref="Actualizar"/>, los seis datos del cliente y la versión; nunca el estado ni la
    /// fecha.
    /// </summary>
    private static readonly HashSet<string> ColumnasDeActualizar = new(
        [
            nameof(PresupuestoFila.Apellido), nameof(PresupuestoFila.Nombre), nameof(PresupuestoFila.Dni),
            nameof(PresupuestoFila.Domicilio), nameof(PresupuestoFila.Email), nameof(PresupuestoFila.Telefono),
            nameof(PresupuestoFila.Version),
        ],
        StringComparer.Ordinal);

    private const string EstadoBorrador = "borrador";
    private const string EstadoFinal = "final";

    /// <summary>
    /// <c>MAX(Numero)</c>, o null si no hay presupuestos (D-07). El alta lo llama dentro de la transacción de escritura
    /// (ADR-013), así ninguna otra grabación confirma entre la lectura y la grabación.
    /// </summary>
    public Task<long?> ObtenerUltimoNumeroAsync(CancellationToken cancelacion = default) =>
        contexto.Presupuestos.AsNoTracking().MaxAsync(p => (long?)p.Numero, cancelacion);

    /// <summary>
    /// Registra el presupuesto y sus líneas con <c>Orden</c> 1..n en el orden recibido; se guarda al confirmar la unidad de
    /// trabajo.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="presupuesto"/> es null.</exception>
    /// <exception cref="ArgumentException">
    /// El presupuesto no tiene número, cliente o líneas: no salió de sus constructores; o no está en Borrador: un Final no
    /// se graba directo, se graba en Borrador y pasa a Final con <see cref="CambiarEstado"/> (AGENTS.md). Los mensajes no
    /// llevan valores.
    /// </exception>
    public void Agregar(Presupuesto presupuesto)
    {
        ArgumentNullException.ThrowIfNull(presupuesto);
        if (presupuesto.Numero < 1 || presupuesto.Cliente is null || presupuesto.Lineas is not { Count: > 0 })
        {
            throw new ArgumentException(MensajePresupuestoInvalido, nameof(presupuesto));
        }

        if (presupuesto.Estado != EstadoPresupuesto.Borrador)
        {
            throw new ArgumentException(MensajeAltaNoBorrador, nameof(presupuesto));
        }

        var cliente = presupuesto.Cliente;
        contexto.Presupuestos.Add(new PresupuestoFila
        {
            Numero = presupuesto.Numero,
            Fecha = presupuesto.Fecha,
            Estado = TextoDe(presupuesto.Estado),
            Apellido = cliente.Apellido,
            Nombre = cliente.Nombre,
            Dni = cliente.Dni,
            Domicilio = cliente.Domicilio,
            Email = cliente.Email,
            Telefono = cliente.Telefono,
            Version = presupuesto.Version,
            Lineas = FilasDeLineas(presupuesto),
        });
    }

    /// <summary>Sin seguimiento, con las líneas ordenadas por <c>Orden</c>; null si no existe.</summary>
    /// <exception cref="InvalidOperationException">La fila no cumple las reglas del dominio: estado corrupto.</exception>
    public async Task<Presupuesto?> ObtenerAsync(long numero, CancellationToken cancelacion = default)
    {
        PresupuestoFila? fila;
        try
        {
            fila = await contexto.Presupuestos.AsNoTracking()
                .Include(p => p.Lineas.OrderBy(l => l.Orden))
                .SingleOrDefaultAsync(p => p.Numero == numero, cancelacion);
        }
        catch (FormatException)
        {
            // Microsoft.Data.Sqlite no pudo leer la fecha guardada ("2026-02-30" pasa el CHECK de formato). Sin la causa:
            // su mensaje repite el texto guardado.
            throw new InvalidOperationException(MensajeFilaInvalida);
        }

        return fila is null ? null : AEntidad(fila);
    }

    /// <summary>
    /// Deja la modificación en el contexto compartido, en un único <c>SaveChanges</c> al confirmar (D-10): adjunta la fila
    /// con <paramref name="versionOriginal"/> como valor original del token, escribe el cliente y la versión + 1, marca
    /// como borradas las líneas de <paramref name="original"/> y agrega las de <paramref name="modificado"/> con
    /// <c>Orden</c> 1..n. Nunca usa <c>ExecuteDelete</c> ni <c>ExecuteUpdate</c>. Si otra escritura confirmó en el medio,
    /// la confirmación falla sin escribir encabezado ni líneas: con <see cref="Optica.Application.Comun.MotivoConflicto"/>
    /// <c>Concurrencia</c> si el presupuesto sigue en Borrador (el <c>UPDATE ... WHERE Version</c> no encuentra la fila),
    /// y con <c>RegistroCerrado</c> si otra sesión lo pasó a Final, porque los comandos de las líneas se filtran solo por
    /// clave y el trigger de cierre aborta antes (D-14). <see cref="Presupuesto.Version"/> no se usa.
    /// </summary>
    /// <exception cref="ArgumentNullException">Alguno de los dos presupuestos es null.</exception>
    /// <exception cref="ArgumentException">
    /// Los dos presupuestos no tienen el mismo número, o <paramref name="versionOriginal"/> es negativa o la máxima (la
    /// siguiente no entraría en la columna). Los mensajes son fijos y no llevan valores.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// El contexto ya sigue ese presupuesto con cambios fuera de los datos del cliente y la versión (por ejemplo, el
    /// estado de un <see cref="CambiarEstado"/> anterior), o como alta o baja. El mensaje es fijo.
    /// </exception>
    public void Actualizar(Presupuesto original, Presupuesto modificado, long versionOriginal)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(modificado);
        if (modificado.Numero != original.Numero)
        {
            throw new ArgumentException(MensajeOtroPresupuesto, nameof(modificado));
        }

        ValidarVersionOriginal(versionOriginal);

        var entrada = AdjuntarEncabezado(original, versionOriginal, ColumnasDeActualizar);
        var cliente = modificado.Cliente;
        Modificar(entrada.Property(p => p.Apellido), cliente.Apellido);
        Modificar(entrada.Property(p => p.Nombre), cliente.Nombre);
        Modificar(entrada.Property(p => p.Dni), cliente.Dni);
        Modificar(entrada.Property(p => p.Domicilio), cliente.Domicilio);
        Modificar(entrada.Property(p => p.Email), cliente.Email);
        Modificar(entrada.Property(p => p.Telefono), cliente.Telefono);

        // Las líneas grabadas se borran y las nuevas se agregan; EF convierte en UPDATE el par borrada/agregada que comparte
        // clave (PresupuestoNumero, Orden).
        var lineas = contexto.Set<LineaPresupuestoFila>();
        foreach (var fila in FilasDeLineas(original))
        {
            var seguida = lineas.Local.FindEntryUntyped([fila.PresupuestoNumero, fila.Orden]);
            if (seguida is null)
            {
                lineas.Attach(fila).State = EntityState.Deleted;
            }
            else
            {
                seguida.State = EntityState.Deleted;
            }
        }

        lineas.AddRange(FilasDeLineas(modificado));
    }

    /// <summary>
    /// Deja el paso a Final en el contexto compartido: adjunta solo la fila del encabezado y marca modificados únicamente
    /// <c>Estado</c> y <c>Version</c> (<c>UPDATE Presupuesto SET Estado, Version WHERE Numero AND Version</c>), sin
    /// comandos sobre las líneas, así ningún trigger de cierre lo rechaza sea cual sea el orden de EF (D-10). Si la versión
    /// guardada ya no es <paramref name="versionOriginal"/>, la unidad de trabajo lo informa como conflicto de
    /// concurrencia.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="presupuesto"/> es null.</exception>
    /// <exception cref="ArgumentException">
    /// El presupuesto no está en Final (este método no hace la transición: la hizo el dominio), o
    /// <paramref name="versionOriginal"/> es negativa o la máxima. Los mensajes son fijos y no llevan valores.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// El contexto ya sigue ese presupuesto con cambios en otras columnas, o como alta o baja: reutilizar la entrada
    /// escribiría más que <c>Estado</c> y <c>Version</c>. El mensaje es fijo.
    /// </exception>
    public void CambiarEstado(Presupuesto presupuesto, long versionOriginal)
    {
        ArgumentNullException.ThrowIfNull(presupuesto);
        if (presupuesto.Estado != EstadoPresupuesto.Final)
        {
            throw new ArgumentException(MensajeNoFinal, nameof(presupuesto));
        }

        ValidarVersionOriginal(versionOriginal);

        var entrada = AdjuntarEncabezado(presupuesto, versionOriginal, ColumnasDeCambiarEstado);
        Modificar(entrada.Property(p => p.Estado), EstadoFinal);
    }

    /// <summary>
    /// Una página de la búsqueda (FR-01 a FR-08 de FEAT-006), sin seguimiento. Combina con "Y" solo los filtros cargados:
    /// <c>instr(plegar(Apellido), @apellido) &gt; 0</c>, lo mismo con Nombre, <c>instr(Dni, @dni) &gt; 0</c> y
    /// <c>Fecha &gt;= @desde</c> / <c>Fecha &lt;= @hasta</c>, ambos inclusive. Sin <c>LIKE</c>: <c>%</c> y <c>_</c> se
    /// buscan tal cual (D-05). Ordena por <c>Numero</c> descendente; el total de cada presupuesto es la suma en SQL de
    /// <c>PrecioFinalCentavos</c> de sus líneas (D-08) y <see cref="PaginaPresupuestos.Total"/> cuenta con el mismo
    /// filtro. Los textos viajan como parámetros y llegan ya normalizados por <see cref="FiltroPresupuestos"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="filtro"/> es null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="pagina"/> es menor que 1, <paramref name="tamanoPagina"/> no es positivo, o el desplazamiento de
    /// la página no entra en un <c>int</c> (D-17). No se consulta la base.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Una fila guardada no se puede leer (una fecha que pasa el CHECK de formato pero no existe, o un estado
    /// desconocido): estado corrupto. El mensaje es fijo y no lleva valores.
    /// </exception>
    public async Task<PaginaPresupuestos> BuscarAsync(
        FiltroPresupuestos filtro, int pagina, int tamanoPagina, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        // Textos fijos, sin el valor recibido: ThrowIfLessThan y ThrowIfNegativeOrZero lo agregarían al mensaje.
        if (pagina < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pagina), MensajePaginaInvalida);
        }

        if (tamanoPagina < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tamanoPagina), MensajeTamanoPaginaInvalido);
        }

        var desplazamiento = (pagina - 1L) * tamanoPagina;
        if (desplazamiento > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(pagina), MensajePaginaInvalida);
        }

        var filtradas = Filtrar(contexto.Presupuestos.AsNoTracking(), filtro);

        List<FilaResumen> filas;
        int total;
        try
        {
            filas = await filtradas
                .OrderByDescending(p => p.Numero)
                .Skip((int)desplazamiento)
                .Take(tamanoPagina)
                .Select(p => new FilaResumen(
                    p.Numero, p.Fecha, p.Apellido, p.Nombre, p.Dni, p.Estado, p.Lineas.Sum(l => l.PrecioFinal)))
                .ToListAsync(cancelacion);
            total = await filtradas.CountAsync(cancelacion);
        }
        catch (FormatException)
        {
            // Microsoft.Data.Sqlite no pudo leer la fecha guardada, como en ObtenerAsync. Sin la causa: su mensaje
            // repite el texto guardado.
            throw new InvalidOperationException(MensajeFilaInvalida);
        }

        // Las dos consultas no comparten transacción: una edición de un Borrador entre una y otra puede sacarlo del
        // filtro. Math.Max solo evita que PaginaPresupuestos lance si el total quedó por debajo de la página
        // (como ArticuloRepository.BuscarAsync).
        return new PaginaPresupuestos([.. filas.Select(AResumen)], Math.Max(total, filas.Count));
    }

    /// <summary>Los filtros cargados, combinados con "Y" (FR-02); sin ninguno, todos (FR-07).</summary>
    private static IQueryable<PresupuestoFila> Filtrar(IQueryable<PresupuestoFila> presupuestos, FiltroPresupuestos filtro)
    {
        // EF Core traduce Contains sobre texto a instr(...) > 0 en SQLite, no a LIKE (D-05).
        if (filtro.Apellido is { } apellido)
        {
            presupuestos = presupuestos.Where(p => FuncionesSqlite.Plegar(p.Apellido).Contains(apellido));
        }

        if (filtro.Nombre is { } nombre)
        {
            presupuestos = presupuestos.Where(p => FuncionesSqlite.Plegar(p.Nombre).Contains(nombre));
        }

        if (filtro.Dni is { } dni)
        {
            presupuestos = presupuestos.Where(p => p.Dni.Contains(dni));
        }

        if (filtro.Desde is { } desde)
        {
            presupuestos = presupuestos.Where(p => p.Fecha >= desde);
        }

        if (filtro.Hasta is { } hasta)
        {
            presupuestos = presupuestos.Where(p => p.Fecha <= hasta);
        }

        return presupuestos;
    }

    private static ResumenPresupuesto AResumen(FilaResumen fila)
    {
        var estado = fila.Estado switch
        {
            EstadoBorrador => EstadoPresupuesto.Borrador,
            EstadoFinal => EstadoPresupuesto.Final,
            _ => throw new InvalidOperationException(MensajeFilaInvalida),
        };

        return new ResumenPresupuesto(fila.Numero, fila.Fecha, fila.Apellido, fila.Nombre, fila.Dni, estado, fila.Total);
    }

    /// <summary>
    /// Las columnas de un resultado de la búsqueda, con el total ya sumado en SQL. Una clase y no un record: no muestra
    /// los datos del cliente en <see cref="object.ToString"/>.
    /// </summary>
    private sealed class FilaResumen(
        long numero, DateOnly fecha, string apellido, string nombre, string dni, string estado, decimal total)
    {
        public long Numero { get; } = numero;

        public DateOnly Fecha { get; } = fecha;

        public string Apellido { get; } = apellido;

        public string Nombre { get; } = nombre;

        public string Dni { get; } = dni;

        public string Estado { get; } = estado;

        public decimal Total { get; } = total;
    }

    private static void ValidarVersionOriginal(long versionOriginal)
    {
        if (versionOriginal is < 0 or >= Presupuesto.VersionMaxima)
        {
            throw new ArgumentException(MensajeVersionOriginal, nameof(versionOriginal));
        }
    }

    /// <summary>
    /// La entrada del encabezado, la que el contexto ya sigue o una adjuntada sin líneas, con <paramref name="versionOriginal"/>
    /// como valor original del token y la siguiente como valor nuevo. Las demás columnas quedan sin modificar. Una entrada
    /// seguida solo se reutiliza si sus columnas modificadas están entre <paramref name="columnasPermitidas"/>, las que
    /// el método que llama escribe.
    /// </summary>
    private EntityEntry<PresupuestoFila> AdjuntarEncabezado(
        Presupuesto presupuesto, long versionOriginal, IReadOnlySet<string> columnasPermitidas)
    {
        var cliente = presupuesto.Cliente;
        var seguida = contexto.Presupuestos.Local.FindEntry(presupuesto.Numero);
        seguida?.DetectChanges();
        if (seguida is not null && !SinOtrosCambios(seguida, columnasPermitidas))
        {
            // Reutilizarla escribiría también esos cambios: CambiarEstado dejaría de ser solo Estado y Version, y
            // Actualizar escribiría un Estado o una Fecha que no le corresponden. Sin valores.
            throw new InvalidOperationException(MensajeEntradaConOtrosCambios);
        }

        var entrada = seguida
            ?? contexto.Presupuestos.Attach(new PresupuestoFila
            {
                Numero = presupuesto.Numero,
                Fecha = presupuesto.Fecha,
                Estado = TextoDe(presupuesto.Estado),
                Apellido = cliente.Apellido,
                Nombre = cliente.Nombre,
                Dni = cliente.Dni,
                Domicilio = cliente.Domicilio,
                Email = cliente.Email,
                Telefono = cliente.Telefono,
                Version = versionOriginal,
            });

        // El valor original es el leído, no el que tenga la entrada: el contexto podría seguir una versión vieja.
        var version = entrada.Property(p => p.Version);
        version.OriginalValue = versionOriginal;
        version.CurrentValue = versionOriginal + 1;
        version.IsModified = true;
        return entrada;
    }

    /// <summary>
    /// Verdadero si la entrada seguida está sin cambios, o modificada solo en <paramref name="columnasPermitidas"/>: sobre
    /// ella, el <c>UPDATE</c> no escribe más que lo que registra el método que llama. Agregada o borrada, nunca.
    /// </summary>
    private static bool SinOtrosCambios(EntityEntry<PresupuestoFila> entrada, IReadOnlySet<string> columnasPermitidas) =>
        entrada.State switch
        {
            EntityState.Unchanged => true,
            EntityState.Modified => entrada.Properties
                .Where(propiedad => propiedad.IsModified)
                .All(propiedad => columnasPermitidas.Contains(propiedad.Metadata.Name)),
            _ => false,
        };

    private static void Modificar<T>(PropertyEntry<PresupuestoFila, T> propiedad, T valor)
    {
        propiedad.CurrentValue = valor;
        propiedad.IsModified = true;
    }

    /// <summary>Las filas de las líneas del presupuesto, con <c>Orden</c> 1..n en el orden de la lista (D-03).</summary>
    private static List<LineaPresupuestoFila> FilasDeLineas(Presupuesto presupuesto) =>
    [
        .. presupuesto.Lineas.Select((linea, indice) => new LineaPresupuestoFila
        {
            PresupuestoNumero = presupuesto.Numero,
            Orden = indice + 1L,
            CodigoArticulo = linea.CodigoArticulo,
            Descripcion = linea.Descripcion,
            PrecioUnitario = linea.PrecioUnitario,
            PrecioConDescuento = linea.PrecioConDescuento,
            PrecioFinal = linea.PrecioFinal,
            Cantidad = linea.Cantidad,
            PorcentajeDescuento = linea.PorcentajeDescuento,
        }),
    ];

    private static string TextoDe(EstadoPresupuesto estado) => estado switch
    {
        EstadoPresupuesto.Borrador => EstadoBorrador,
        EstadoPresupuesto.Final => EstadoFinal,
        _ => throw new ArgumentException("El estado del presupuesto no es válido.", nameof(estado)),
    };

    /// <summary>
    /// La entidad de una fila. Una fila que eludió los CHECK o que el dominio guardaría de otra forma (un estado
    /// desconocido, un DNI o un texto que se normalizarían a otro valor, una cantidad que no entra en un <c>int</c>, un
    /// presupuesto sin líneas) no puede representarse: se informa como estado corrupto y no con el
    /// <see cref="ArgumentException"/> del dominio, que significa entrada inválida. El mensaje no lleva valores; la causa
    /// del dominio nombra el campo y no el valor, así que se puede adjuntar.
    /// </summary>
    private static Presupuesto AEntidad(PresupuestoFila fila)
    {
        var estado = fila.Estado switch
        {
            EstadoBorrador => EstadoPresupuesto.Borrador,
            EstadoFinal => EstadoPresupuesto.Final,
            _ => throw new InvalidOperationException(MensajeFilaInvalida),
        };

        // El orden de la Api es la posición + 1 de la línea (D-03): con huecos, ese orden no identificaría la línea.
        for (var indice = 0; indice < fila.Lineas.Count; indice++)
        {
            if (fila.Lineas[indice].Orden != indice + 1L)
            {
                throw new InvalidOperationException(MensajeFilaInvalida);
            }
        }

        Presupuesto presupuesto;
        try
        {
            var cliente = new ClientePresupuesto(
                fila.Apellido, fila.Nombre, fila.Dni, fila.Domicilio, fila.Email, fila.Telefono);
            if (!ComoSeGuarda(cliente, fila))
            {
                throw new InvalidOperationException(MensajeFilaInvalida);
            }

            presupuesto = new Presupuesto(
                fila.Numero, fila.Fecha, estado, cliente, [.. fila.Lineas.Select(ALinea)], fila.Version);
        }
        catch (ArgumentException error)
        {
            throw new InvalidOperationException(MensajeFilaInvalida, error);
        }

        return presupuesto;
    }

    private static LineaPresupuesto ALinea(LineaPresupuestoFila fila)
    {
        if (fila.Cantidad is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidOperationException(MensajeFilaInvalida);
        }

        return new LineaPresupuesto(
            fila.CodigoArticulo, fila.Descripcion, fila.PrecioUnitario, (int)fila.Cantidad, fila.PorcentajeDescuento,
            fila.PrecioConDescuento, fila.PrecioFinal);
    }

    // El cliente normalizado tiene que ser exactamente el guardado: si no, al volver a grabarlo cambiaría.
    private static bool ComoSeGuarda(ClientePresupuesto cliente, PresupuestoFila fila) =>
        string.Equals(cliente.Apellido, fila.Apellido, StringComparison.Ordinal)
        && string.Equals(cliente.Nombre, fila.Nombre, StringComparison.Ordinal)
        && string.Equals(cliente.Dni, fila.Dni, StringComparison.Ordinal)
        && string.Equals(cliente.Domicilio, fila.Domicilio, StringComparison.Ordinal)
        && string.Equals(cliente.Email, fila.Email, StringComparison.Ordinal)
        && string.Equals(cliente.Telefono, fila.Telefono, StringComparison.Ordinal);
}
