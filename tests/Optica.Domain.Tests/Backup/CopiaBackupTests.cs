using Optica.Domain.Backup;

namespace Optica.Domain.Tests.Backup;

/// <summary>
/// Spec FEAT-010, Block 1 (FR-06, AC-04, ADR-029): el nombre de cada copia lleva la fecha del día en que se hizo
/// (<c>optica-AAAA-MM-DD.db</c>); el temporal lleva el sufijo <c>.parcial</c> y nunca cuenta como copia.
/// </summary>
public class CopiaBackupTests
{
    [Fact]
    public void CopiaBackup_NombreYFecha_IdaYVuelta()
    {
        // AC-04: la copia de las 20:00 del 30/10/2026 queda identificada con esa fecha.
        var fecha = new DateOnly(2026, 10, 30);
        Assert.Equal("optica-2026-10-30.db", CopiaBackup.Nombre(fecha));
        Assert.True(CopiaBackup.TryLeerFecha("optica-2026-10-30.db", out var leida));
        Assert.Equal(fecha, leida);

        // Ida y vuelta en los bordes del calendario, con ceros a la izquierda.
        foreach (var otra in new[] { new DateOnly(2026, 1, 1), new DateOnly(2028, 2, 29), new DateOnly(2026, 12, 31) })
        {
            Assert.True(CopiaBackup.TryLeerFecha(CopiaBackup.Nombre(otra), out var vuelta));
            Assert.Equal(otra, vuelta);
        }

        // El temporal y sus auxiliares de SQLite son parciales, de una fecha válida; una copia terminada no lo es.
        Assert.Equal("optica-2026-10-30.db.parcial", CopiaBackup.NombreParcial(fecha));
        Assert.True(CopiaBackup.EsParcial("optica-2026-10-30.db.parcial"));
        Assert.True(CopiaBackup.EsParcial("optica-2026-10-30.db.parcial-journal"));
        Assert.True(CopiaBackup.EsParcial("optica-2026-10-30.db.parcial-wal"));
        Assert.True(CopiaBackup.EsParcial("optica-2026-10-30.db.parcial-shm"));
        Assert.False(CopiaBackup.EsParcial("optica-2026-10-30.db"));
        Assert.False(CopiaBackup.EsParcial("optica-2026-10-30.db.parcial-otro"));
        Assert.False(CopiaBackup.EsParcial("optica-2026-02-30.db.parcial"));
        Assert.False(CopiaBackup.EsParcial("informe.db.parcial"));
        Assert.False(CopiaBackup.EsParcial(null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("optica.db")]
    [InlineData("optica-2026-10-30.db.parcial")] // un temporal nunca cuenta como copia (ADR-029)
    [InlineData("optica-2026-10-30.db-wal")]
    [InlineData("optica-2026-10-30.sqlite")]
    [InlineData("OPTICA-2026-10-30.DB")]
    [InlineData("copia-optica-2026-10-30.db")]
    [InlineData("optica-2026-10-30.db ")]
    [InlineData("optica-2026-1-30.db")]
    [InlineData("optica-26-10-30.db")]
    [InlineData("optica-2026_10_30.db")]
    [InlineData("optica-２０２６-10-30.db")] // dígitos de ancho completo
    [InlineData("carpeta/optica-2026-10-30.db")]
    public void CopiaBackup_NombreAjeno_Rechazado(string? nombre)
    {
        // La retención solo borra archivos con el patrón exacto (ADR-029): cualquier otro nombre no es una copia.
        Assert.False(CopiaBackup.TryLeerFecha(nombre, out var fecha));
        Assert.Equal(default, fecha);
    }

    [Theory]
    [InlineData("optica-2026-02-30.db")]
    [InlineData("optica-2026-02-29.db")] // 2026 no es bisiesto
    [InlineData("optica-2026-13-01.db")]
    [InlineData("optica-2026-00-10.db")]
    [InlineData("optica-2026-04-31.db")]
    [InlineData("optica-0000-01-01.db")]
    public void CopiaBackup_FechaInexistente_Rechazada(string nombre)
    {
        Assert.False(CopiaBackup.TryLeerFecha(nombre, out var fecha));
        Assert.Equal(default, fecha);
    }
}
