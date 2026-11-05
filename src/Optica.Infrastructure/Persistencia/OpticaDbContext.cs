using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Optica.Infrastructure.Persistencia;

/// <summary>Contexto de EF Core sobre la base SQLite de la aplicación.</summary>
public sealed class OpticaDbContext(DbContextOptions<OpticaDbContext> opciones) : DbContext(opciones)
{
    /// <summary>Fechas guardadas como TEXT en UTC con formato ISO 8601 de ida y vuelta ("O").</summary>
    private static readonly ValueConverter<DateTime, string> FechaUtcIso8601 = new(
        valor => AIso8601Utc(valor),
        texto => DesdeIso8601Utc(texto));

    /// <summary>
    /// Importes y porcentajes de hasta 2 decimales guardados como INTEGER en centésimos (ADR-008): ×100 al guardar,
    /// /100 al leer. El dominio garantiza que el valor tiene a lo sumo 2 decimales y entra en 64 bits; si igual llega
    /// uno con más decimales, se rechaza en lugar de truncarlo en silencio.
    /// </summary>
    private static readonly ValueConverter<decimal, long> Centesimos = new(
        valor => ACentesimos(valor),
        centesimos => centesimos / 100m);

    public DbSet<CredencialAccesoFila> CredencialesAcceso => Set<CredencialAccesoFila>();

    public DbSet<ConfiguracionNegocioFila> ConfiguracionesNegocio => Set<ConfiguracionNegocioFila>();

    public DbSet<EventoAccesoFila> EventosAcceso => Set<EventoAccesoFila>();

    public DbSet<ArticuloFila> Articulos => Set<ArticuloFila>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CredencialAccesoFila>(credencial =>
        {
            // A lo sumo una fila: la única contraseña del sistema.
            credencial.ToTable("CredencialAcceso", tabla =>
                tabla.HasCheckConstraint("CK_CredencialAcceso_Id", "\"Id\" = 1"));
            credencial.HasKey(c => c.Id);
            credencial.Property(c => c.Id).ValueGeneratedNever();
            // Token de concurrencia: el reemplazo filtra por el hash leído, así un cambio o un restablecimiento
            // en el medio da conflicto en lugar de pisarse (FEAT-001b).
            credencial.Property(c => c.Hash).IsRequired().IsConcurrencyToken();
            credencial.Property(c => c.DefinidaEn).IsRequired().HasConversion(FechaUtcIso8601);
        });

        modelBuilder.Entity<EventoAccesoFila>(evento =>
        {
            evento.ToTable("EventoAcceso");
            evento.HasKey(e => e.Id);
            evento.Property(e => e.Id).ValueGeneratedOnAdd();
            evento.Property(e => e.FechaHora).IsRequired().HasConversion(FechaUtcIso8601);
            evento.Property(e => e.Tipo).IsRequired();
            evento.Property(e => e.Resultado).IsRequired();
            evento.HasIndex(e => e.FechaHora);

            // La consulta del bloqueo busca por (Tipo, Resultado) sin recorrer la tabla (FEAT-001b, ADR-006).
            evento.HasIndex(e => new { e.Tipo, e.Resultado });
        });

        modelBuilder.Entity<ConfiguracionNegocioFila>(configuracion =>
        {
            // Una sola fila, con los numéricos en centésimos y CHECK de rango como segunda barrera (ADR-008). La fila
            // inicial la inserta la migración, no HasData: así cambiar el modelo no pisa lo que cargó la operadora.
            configuracion.ToTable("ConfiguracionNegocio", tabla =>
            {
                tabla.HasCheckConstraint("CK_ConfiguracionNegocio_Id", "\"Id\" = 1");
                tabla.HasCheckConstraint(
                    "CK_ConfiguracionNegocio_Alicuota", "\"AlicuotaIvaCentesimas\" BETWEEN 0 AND 10000");
                tabla.HasCheckConstraint(
                    "CK_ConfiguracionNegocio_Condicion",
                    "\"CondicionFiscal\" IN ('responsable_inscripto', 'monotributo')");
                tabla.HasCheckConstraint("CK_ConfiguracionNegocio_Multiplo", "\"MultiploRedondeoCentavos\" >= 1");
                tabla.HasCheckConstraint(
                    "CK_ConfiguracionNegocio_Tope",
                    "\"TopeIdentificacionCentavos\" IS NULL OR \"TopeIdentificacionCentavos\" > 0");
            });
            configuracion.HasKey(c => c.Id);
            configuracion.Property(c => c.Id).ValueGeneratedNever();
            configuracion.Property(c => c.AlicuotaIva)
                .HasColumnName("AlicuotaIvaCentesimas").IsRequired().HasConversion(Centesimos);
            configuracion.Property(c => c.CondicionFiscal).IsRequired();
            configuracion.Property(c => c.MultiploRedondeo)
                .HasColumnName("MultiploRedondeoCentavos").IsRequired().HasConversion(Centesimos);
            configuracion.Property(c => c.TopeIdentificacion)
                .HasColumnName("TopeIdentificacionCentavos").HasConversion(Centesimos);
        });

        modelBuilder.Entity<ArticuloFila>(articulo =>
        {
            // ADVERTENCIA (ADR-012): toda migración que toque Articulo se revisa antes de aplicarla para que EF Core no
            // reconstruya la tabla (lo hace con AddCheckConstraint, DropCheckConstraint, AlterColumn y similares). La tabla
            // reconstruida pierde los DEFAULT de Activo y Version, reinicia la secuencia AUTOINCREMENT (un código usado
            // podría reutilizarse) y pierde los triggers creados con Sql(). Si hace falta, se escribe a mano como
            // CatalogoActivo (D-23).
            // Importes y margen en centésimos (ADR-008); los CHECK son la segunda barrera detrás de PoliticaArticulo. La
            // clave no lleva CHECK de largo: la normalización NFC y las mayúsculas pueden alargarla.
            articulo.ToTable("Articulo", tabla =>
            {
                tabla.HasCheckConstraint(
                    "CK_Articulo_CodigoProveedor", "length(\"CodigoProveedor\") BETWEEN 1 AND 50");
                tabla.HasCheckConstraint("CK_Articulo_Descripcion", "length(\"Descripcion\") BETWEEN 1 AND 200");
                tabla.HasCheckConstraint("CK_Articulo_Margen", "\"MargenCentesimos\" BETWEEN 0 AND 100000");
                tabla.HasCheckConstraint("CK_Articulo_Activo", "\"Activo\" IN (0, 1)");
                tabla.HasCheckConstraint("CK_Articulo_Version", "\"Version\" >= 0");
            });
            articulo.HasKey(a => a.Id);

            // INTEGER PRIMARY KEY AUTOINCREMENT: un código usado no se reutiliza.
            articulo.Property(a => a.Id).ValueGeneratedOnAdd();
            articulo.Property(a => a.CodigoProveedor).IsRequired();
            articulo.Property(a => a.CodigoProveedorClave).IsRequired();
            articulo.Property(a => a.Descripcion).IsRequired();
            articulo.Property(a => a.PrecioCosto)
                .HasColumnName("PrecioCostoCentavos").IsRequired().HasConversion(Centesimos);
            articulo.Property(a => a.MargenUtilidad)
                .HasColumnName("MargenCentesimos").IsRequired().HasConversion(Centesimos);
            articulo.Property(a => a.PrecioVenta)
                .HasColumnName("PrecioVentaCentavos").IsRequired().HasConversion(Centesimos);

            // Unicidad del código en el proveedor sobre la clave normalizada por el dominio (ADR-009). Es la única
            // restricción única de la tabla: un conflicto al confirmar el alta solo puede venir de acá (D-07).
            articulo.HasIndex(a => a.CodigoProveedorClave).IsUnique();

            // FEAT-002b. Los dos son tokens de concurrencia: la versión cubre la edición y el cambio de estado (D-08), y el
            // estado impide que una fila inactiva reciba datos nuevos (D-22). Sin HasDefaultValue: el DEFAULT vive solo en
            // la migración, para las filas que ya estaban, y el repositorio asigna siempre los dos (D-21).
            articulo.Property(a => a.Activo).IsRequired().IsConcurrencyToken();
            articulo.Property(a => a.Version).IsRequired().IsConcurrencyToken();
        });

        // Las funciones de la búsqueda de artículos, registradas en cada conexión por FabricaConexion (ADR-011).
        modelBuilder.HasDbFunction(typeof(FuncionesSqlite).GetMethod(nameof(FuncionesSqlite.Plegar))!)
            .HasName(FuncionesSqlite.NombrePlegar);
        modelBuilder.HasDbFunction(typeof(FuncionesSqlite).GetMethod(nameof(FuncionesSqlite.OrdenArticulo))!)
            .HasName(FuncionesSqlite.NombreOrdenArticulo);
    }

    /// <exception cref="ArgumentException">El valor tiene más de 2 decimales. El mensaje no incluye el valor.</exception>
    private static long ACentesimos(decimal valor) =>
        valor == decimal.Round(valor, 2)
            ? decimal.ToInt64(valor * 100m)
            : throw new ArgumentException(
                "Un valor guardado en centésimos tiene más de 2 decimales.", nameof(valor));

    private static string AIso8601Utc(DateTime valor)
    {
        var utc = valor.Kind == DateTimeKind.Local
            ? valor.ToUniversalTime()
            : DateTime.SpecifyKind(valor, DateTimeKind.Utc);
        return utc.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTime DesdeIso8601Utc(string texto) =>
        DateTime.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}

/// <summary>Fila de la tabla <c>CredencialAcceso</c>: el hash de la contraseña y cuándo se definió.</summary>
public sealed class CredencialAccesoFila
{
    public int Id { get; set; }

    /// <summary>Formato <c>PBKDF2-SHA256$&lt;iteraciones&gt;$&lt;sal base64&gt;$&lt;hash base64&gt;</c>.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>Fecha y hora UTC de definición.</summary>
    public DateTime DefinidaEn { get; set; }
}

/// <summary>
/// Fila de la tabla <c>ConfiguracionNegocio</c> (única, <c>Id = 1</c>). Los numéricos se ven como <c>decimal</c> y se
/// guardan en centésimos enteros (ADR-008); la condición es el texto de
/// <see cref="Optica.Domain.Configuracion.PoliticaConfiguracionNegocio.TextoDe"/>.
/// </summary>
public sealed class ConfiguracionNegocioFila
{
    public int Id { get; set; }

    /// <summary>Porcentaje; columna <c>AlicuotaIvaCentesimas</c>.</summary>
    public decimal AlicuotaIva { get; set; }

    /// <summary><c>responsable_inscripto</c> o <c>monotributo</c>.</summary>
    public string CondicionFiscal { get; set; } = string.Empty;

    /// <summary>Importe; columna <c>MultiploRedondeoCentavos</c>.</summary>
    public decimal MultiploRedondeo { get; set; }

    /// <summary>Importe, o null mientras no se cargue (D-02); columna <c>TopeIdentificacionCentavos</c>.</summary>
    public decimal? TopeIdentificacion { get; set; }
}

/// <summary>
/// Fila de la tabla <c>Articulo</c>. Los importes y el margen se ven como <c>decimal</c> y se guardan en centésimos
/// enteros (ADR-008); la clave es <see cref="Optica.Domain.Catalogo.NormalizacionCodigo.Clave"/> del código (ADR-009).
/// </summary>
public sealed class ArticuloFila
{
    /// <summary>El código autonumérico del artículo.</summary>
    public long Id { get; set; }

    /// <summary>El código en el proveedor recortado, tal como se escribió.</summary>
    public string CodigoProveedor { get; set; } = string.Empty;

    /// <summary>La clave normalizada del código en el proveedor, con índice UNIQUE.</summary>
    public string CodigoProveedorClave { get; set; } = string.Empty;

    public string Descripcion { get; set; } = string.Empty;

    /// <summary>Importe; columna <c>PrecioCostoCentavos</c>.</summary>
    public decimal PrecioCosto { get; set; }

    /// <summary>Porcentaje; columna <c>MargenCentesimos</c>.</summary>
    public decimal MargenUtilidad { get; set; }

    /// <summary>Importe ya redondeado al múltiplo; columna <c>PrecioVentaCentavos</c>.</summary>
    public decimal PrecioVenta { get; set; }

    /// <summary>
    /// 1 si el artículo está activo, 0 si está dado de baja (FR-05). Es <c>long</c> y no <c>bool</c> a propósito: SQLite
    /// lee como verdadero cualquier entero distinto de 0, y así el repositorio puede reconocer una fila que eludió el
    /// CHECK como estado corrupto.
    /// </summary>
    public long Activo { get; set; }

    /// <summary>La versión para la concurrencia optimista, desde 0; cada escritura la incrementa (D-08).</summary>
    public long Version { get; set; }
}

/// <summary>
/// Fila de la tabla <c>EventoAcceso</c>. Tipo: <c>definicion</c>, <c>ingreso</c>, <c>salida</c>,
/// <c>cambio_contrasena</c>, <c>restablecimiento</c>; resultado: <c>exito</c>, <c>rechazo</c>, <c>bloqueado</c>. La base
/// no restringe los textos: una fila con un texto que esta versión no conoce se ignora al consultar. No guarda
/// contraseñas, IP ni otros datos personales.
/// </summary>
public sealed class EventoAccesoFila
{
    public long Id { get; set; }

    /// <summary>Fecha y hora UTC del evento.</summary>
    public DateTime FechaHora { get; set; }

    public string Tipo { get; set; } = string.Empty;

    public string Resultado { get; set; } = string.Empty;
}
