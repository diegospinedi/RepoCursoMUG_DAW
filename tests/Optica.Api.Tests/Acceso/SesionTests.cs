using System.Net;

namespace Optica.Api.Tests.Acceso;

public sealed class SesionTests
{
    [Fact]
    public async Task Sesion_60MinutosSinActividad_NoAutorizado()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookieInicial = await Http.DefinirAsync(cliente);

        // A los 59 minutos sin pedidos la sesión sigue viva, y el pedido la renueva.
        fabrica.Reloj.Advance(TimeSpan.FromMinutes(59));
        var a59 = await Http.GetAsync(cliente, "/api/sesion", cookieInicial);
        Assert.Equal(HttpStatusCode.NoContent, a59.StatusCode);
        var cookieRenovada = Http.Cookie(a59);
        Assert.NotNull(cookieRenovada);

        // 60 minutos después del último pedido, vencida.
        fabrica.Reloj.Advance(TimeSpan.FromMinutes(60));
        var a60 = await Http.GetAsync(cliente, "/api/sesion", cookieRenovada);

        Assert.Equal(HttpStatusCode.Unauthorized, a60.StatusCode);
        Assert.Null(a60.Headers.Location);
        Assert.Equal(string.Empty, await a60.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sesion_PedidoIntermedio_RenuevaVencimiento()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookieInicial = await Http.DefinirAsync(cliente);

        fabrica.Reloj.Advance(TimeSpan.FromMinutes(40));
        var a40 = await Http.GetAsync(cliente, "/api/sesion", cookieInicial);
        Assert.Equal(HttpStatusCode.NoContent, a40.StatusCode);
        var cookieRenovada = Http.Cookie(a40);
        Assert.NotNull(cookieRenovada);

        // La cookie original vence a los 60 minutos; la renovada, a los 100.
        fabrica.Reloj.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Http.GetAsync(cliente, "/api/sesion", cookieInicial)).StatusCode);

        fabrica.Reloj.Advance(TimeSpan.FromMinutes(39));
        Assert.Equal(HttpStatusCode.NoContent, (await Http.GetAsync(cliente, "/api/sesion", cookieRenovada)).StatusCode);

        fabrica.Reloj.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Http.GetAsync(cliente, "/api/sesion", cookieRenovada)).StatusCode);
    }
}
