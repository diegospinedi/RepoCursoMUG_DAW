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

    public DbSet<CredencialAccesoFila> CredencialesAcceso => Set<CredencialAccesoFila>();

    public DbSet<EventoAccesoFila> EventosAcceso => Set<EventoAccesoFila>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CredencialAccesoFila>(credencial =>
        {
            // A lo sumo una fila: la única contraseña del sistema.
            credencial.ToTable("CredencialAcceso", tabla =>
                tabla.HasCheckConstraint("CK_CredencialAcceso_Id", "\"Id\" = 1"));
            credencial.HasKey(c => c.Id);
            credencial.Property(c => c.Id).ValueGeneratedNever();
            credencial.Property(c => c.Hash).IsRequired();
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
    }

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
