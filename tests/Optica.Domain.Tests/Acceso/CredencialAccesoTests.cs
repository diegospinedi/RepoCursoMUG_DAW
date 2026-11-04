using Optica.Domain.Acceso;

namespace Optica.Domain.Tests.Acceso;

public class CredencialAccesoTests
{
    private const string HashAnterior = "PBKDF2-SHA256$600000$YW50ZXJpb3I=$YW50ZXJpb3I=";
    private const string HashNuevo = "PBKDF2-SHA256$600000$bnVldm8=$bnVldm8=";
    private static readonly DateTime Definida = new(2026, 10, 30, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Ahora = new(2026, 11, 3, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void CredencialAcceso_ReemplazarHash_ActualizaHashYFecha()
    {
        var credencial = new CredencialAcceso(HashAnterior, Definida);

        credencial.ReemplazarHash(HashNuevo, Ahora);

        Assert.Equal(HashNuevo, credencial.Hash);
        Assert.Equal(Ahora, credencial.DefinidaEn);
        Assert.Equal(DateTimeKind.Utc, credencial.DefinidaEn.Kind);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void CredencialAcceso_ReemplazarConFechaNoUtc_Rechazado(DateTimeKind tipo)
    {
        var credencial = new CredencialAcceso(HashAnterior, Definida);

        var error = Assert.Throws<ArgumentException>(
            () => credencial.ReemplazarHash(HashNuevo, DateTime.SpecifyKind(Ahora, tipo)));

        Assert.Equal("ahora", error.ParamName);
        // Sin datos de la contraseña ni del hash en el mensaje, y la credencial queda como estaba.
        Assert.DoesNotContain(HashNuevo, error.Message, StringComparison.Ordinal);
        Assert.Equal(HashAnterior, credencial.Hash);
        Assert.Equal(Definida, credencial.DefinidaEn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CredencialAcceso_ReemplazarConHashVacio_RechazadoComoAlCrearla(string? hash)
    {
        var credencial = new CredencialAcceso(HashAnterior, Definida);

        var error = Assert.Throws<ArgumentException>(() => credencial.ReemplazarHash(hash!, Ahora));

        Assert.Equal("nuevo", error.ParamName);
        Assert.Equal(HashAnterior, credencial.Hash);
        Assert.Equal(Definida, credencial.DefinidaEn);
    }
}
