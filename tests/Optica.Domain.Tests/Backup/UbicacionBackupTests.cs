using Optica.Domain.Backup;

namespace Optica.Domain.Tests.Backup;

/// <summary>
/// Spec FEAT-010, Block 1 (FR-01, D-12): la ubicación del backup. <see cref="UbicacionBackup.ParaGrabar"/> valida la ruta
/// (absoluta, recortada, de 1 a 260 caracteres, sin caracteres de control) y devuelve la infracción del campo;
/// <see cref="UbicacionBackup.Leer"/> reconstruye la fila guardada. Rutas ficticias armadas sobre la carpeta temporal,
/// para que sean absolutas en Windows y en Linux.
/// </summary>
public class UbicacionBackupTests
{
    private static readonly string RutaAbsoluta = Path.Combine(Path.GetTempPath(), "Backups", "Optica");

    [Theory]
    [InlineData("Backups")]
    [InlineData("Backups/Optica")]
    [InlineData("./Backups")]
    [InlineData("../Backups")]
    public void UbicacionBackup_RutaRelativa_Rechazada(string ruta)
    {
        var resultado = UbicacionBackup.ParaGrabar(ruta);

        // FR-01: una ruta relativa dependería de la carpeta desde la que arranca la Api; es infracción del campo ruta.
        Assert.False(resultado.EsValido);
        Assert.Null(resultado.Ubicacion);
        Assert.Equal([InfraccionUbicacionBackup.RutaNoAbsoluta], resultado.Infracciones);

        // El resto de las reglas del campo, en su orden: caracteres de control (antes que absoluta) y largo.
        Assert.Equal(
            [InfraccionUbicacionBackup.RutaConCaracteresInvalidos],
            UbicacionBackup.ParaGrabar(RutaAbsoluta + "\nOtra").Infracciones);
        Assert.Equal(
            [InfraccionUbicacionBackup.RutaConCaracteresInvalidos],
            UbicacionBackup.ParaGrabar(RutaAbsoluta + "\uD800").Infracciones);
        var larga = Path.Combine(RutaAbsoluta, new string('a', UbicacionBackup.LargoMaximoRuta));
        Assert.Equal(
            [InfraccionUbicacionBackup.RutaDemasiadoLarga], UbicacionBackup.ParaGrabar(larga).Infracciones);

        // Justo en el límite, y con espacios alrededor que se recortan, es válida.
        var limite = RutaAbsoluta + new string('b', UbicacionBackup.LargoMaximoRuta - RutaAbsoluta.Length);
        var valida = UbicacionBackup.ParaGrabar("  " + limite + "  ");
        Assert.True(valida.EsValido);
        Assert.Empty(valida.Infracciones);
        Assert.Equal(limite, valida.Ubicacion.Ruta);

        // La lectura aplica las mismas reglas, con un error que nombra el campo y no lleva el valor.
        var error = Assert.Throws<ArgumentException>(() => UbicacionBackup.Leer(ruta));
        Assert.Equal("ruta", error.ParamName);
        Assert.DoesNotContain(ruta, error.Message, StringComparison.Ordinal);
        Assert.Equal(RutaAbsoluta, UbicacionBackup.Leer(RutaAbsoluta).Ruta);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u00A0\t\uFEFF")] // espacio duro, tabulador y marca de orden de bytes
    public void UbicacionBackup_RutaVacia_Rechazada(string? ruta)
    {
        var resultado = UbicacionBackup.ParaGrabar(ruta);

        Assert.False(resultado.EsValido);
        Assert.Null(resultado.Ubicacion);
        Assert.Equal([InfraccionUbicacionBackup.RutaFaltante], resultado.Infracciones);
        Assert.Throws<ArgumentException>(() => UbicacionBackup.Leer(ruta ?? string.Empty));
    }

    [Fact]
    public void UbicacionBackup_ToString_SinRuta()
    {
        // D-12: la ruta puede llevar el nombre de una persona (C:\Users\...); no aparece si la entidad termina en un log.
        var ubicacion = UbicacionBackup.ParaGrabar(RutaAbsoluta).Ubicacion!;
        var resultado = UbicacionBackup.ParaGrabar(RutaAbsoluta);

        Assert.DoesNotContain("Backups", ubicacion.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("Backups", resultado.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("Backups", UbicacionBackup.Leer(RutaAbsoluta).ToString() ?? string.Empty,
            StringComparison.Ordinal);
    }
}
