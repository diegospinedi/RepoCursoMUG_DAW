using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Domain.Acceso;
using Optica.Infrastructure.Persistencia;

namespace Optica.Api.Tests;

public sealed class ArranqueYErroresTests
{
    private const string RutaPrivada = "/home/usuario-privado/AppData/optica.db";

    [Fact]
    public void Arranque_DirectorioDatosInvalido_ImpideArrancar()
    {
        using var fabrica = new FabricaApi(directorioDatos: "carpeta/relativa");

        var error = Assert.ThrowsAny<Exception>(() => fabrica.CreateClient());

        Assert.Contains(Cadena(error), e => e is DirectorioDatosInvalidoException);
        Assert.Contains(
            fabrica.Registro.Lineas,
            l => l.Nivel == LogLevel.Critical && l.Mensaje.Contains(FabricaConexion.ClaveDirectorio, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pedido_ErrorDeBase_Error500GenericoSinDetalle()
    {
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<ICredencialAccesoRepository>();
            servicios.AddScoped<ICredencialAccesoRepository, RepositorioQueFalla>();
        });
        var cliente = fabrica.CrearClienteSinCookies();

        var respuesta = await Http.GetAsync(cliente, "/api/acceso/estado");

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("usuario-privado", cuerpo);
        Assert.DoesNotContain("SQLite", cuerpo, StringComparison.OrdinalIgnoreCase);

        // El log nombra el directorio (RutaVisible), el código de SQLite y la clave a revisar, sin la excepción de SQLite
        // tal cual.
        var lineas = fabrica.Registro.Lineas;
        Assert.Contains(
            lineas,
            l => l.Nivel == LogLevel.Error
                && l.Mensaje.Contains(RutasPrivadas.Ocultar(fabrica.DirectorioDatos), StringComparison.Ordinal)
                && l.Mensaje.Contains("código de error 14", StringComparison.Ordinal)
                && l.Mensaje.Contains(FabricaConexion.ClaveDirectorio, StringComparison.Ordinal));
        Assert.DoesNotContain(lineas, l => l.Mensaje.Contains("usuario-privado", StringComparison.Ordinal));
        Assert.DoesNotContain(lineas, l => l.Excepcion is not null && Cadena(l.Excepcion).Any(e => e is SqliteException));
    }

    private static IEnumerable<Exception> Cadena(Exception error)
    {
        for (Exception? actual = error; actual is not null; actual = actual.InnerException)
        {
            yield return actual;
            if (actual is AggregateException agregada)
            {
                foreach (var interna in agregada.InnerExceptions.SelectMany(Cadena))
                {
                    yield return interna;
                }
            }
        }
    }

    /// <summary>Repositorio cuya base no se puede abrir; el mensaje trae una ruta con un nombre de usuario.</summary>
    private sealed class RepositorioQueFalla : ICredencialAccesoRepository
    {
        private static SqliteException Falla() =>
            new($"SQLite Error 14: 'unable to open database file' ({RutaPrivada}).", 14);

        public Task<bool> ExisteAsync(CancellationToken cancelacion = default) => throw Falla();

        public Task<CredencialAcceso?> ObtenerAsync(CancellationToken cancelacion = default) => throw Falla();

        public void Agregar(CredencialAcceso credencial) => throw Falla();
    }
}
