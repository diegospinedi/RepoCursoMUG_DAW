using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Comun;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Optica.Infrastructure.Arca;
using Optica.Infrastructure.Persistencia;

namespace Optica.Api.Tests.Web;

/// <summary>
/// Spec FEAT-007, Block 6 (ronda 2): el puerto neutro <see cref="IProtectorDatos"/> que registra la Api cifra con la
/// Data Protection real (las claves de la sesión, en el directorio de datos), y la composición de la Api arma el cliente
/// de WSAA y WSFEv1 con homologación y credenciales completas, sin salir a la red.
/// </summary>
public sealed class ProtectorDatosTests
{
    [Fact]
    public async Task ProtectorDatos_DataProtectionReal_IdaYVueltaYOtroPropositoNoDescifra()
    {
        await using var fabrica = new FabricaApi();
        using var cliente = fabrica.CreateClient();
        var protector = fabrica.Services.GetRequiredService<IProtectorDatos>();
        var claro = Encoding.UTF8.GetBytes("ticket-centinela-de-prueba");

        var cifrado = protector.Proteger("Optica.Arca.Ticket", claro);

        Assert.Equal("ProtectorDatos", protector.GetType().Name);
        Assert.DoesNotContain("ticket-centinela", Encoding.UTF8.GetString(cifrado), StringComparison.Ordinal);
        Assert.Equal(claro, protector.Desproteger("Optica.Arca.Ticket", cifrado));
        Assert.ThrowsAny<CryptographicException>(() => protector.Desproteger("Otro.Proposito", cifrado));
        Assert.ThrowsAny<CryptographicException>(() =>
            protector.Desproteger("Optica.Arca.Ticket", [.. cifrado.Take(cifrado.Length - 1), (byte)(cifrado[^1] ^ 0xFF)]));
    }

    [Fact]
    public async Task Composicion_HomologacionConCredenciales_ResuelveAutorizadorWsfeSinRed()
    {
        var directorio = FabricaApi.NuevoDirectorioTemporal();
        try
        {
            var constructor = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = "Development",
            });
            constructor.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [FabricaConexion.ClaveDirectorio] = directorio,
                [OpcionesArca.ClaveEntorno] = "Homologacion",
                [OpcionesArca.ClaveCuit] = "20000000001",
                [OpcionesArca.ClaveCertificadoRuta] = Path.Combine(directorio, "no-existe.pfx"),
                [OpcionesArca.ClaveCertificadoClave] = "clave-de-prueba",
            });
            Program.ConfigurarServicios(constructor);
            await using var app = constructor.Build();

            var autorizador = app.Services.GetRequiredService<IAutorizadorComprobantes>();

            Assert.IsType<AutorizadorWsfe>(autorizador);
            Assert.Equal(EntornoArca.Homologacion, autorizador.Entorno);
            Assert.Equal("ProtectorDatos", app.Services.GetRequiredService<IProtectorDatos>().GetType().Name);

            // Un plazo ya vencido no sale a la red: no enviada.
            using var vencido = new CancellationTokenSource();
            await vencido.CancelAsync();
            Assert.IsType<ResultadoAutorizacion.NoEnviada>(
                await autorizador.AutorizarAsync(
                    new SolicitudComprobante(
                        TipoComprobante.FacturaC,
                        3,
                        new DateOnly(2026, 10, 30),
                        ConceptoFactura.Productos,
                        ReceptorFactura.SinIdentificar,
                        ImportesFactura.ParaFacturaC(1000m)),
                    vencido.Token));
        }
        finally
        {
            FabricaApi.BorrarDirectorioDePrueba(directorio);
        }
    }
}
