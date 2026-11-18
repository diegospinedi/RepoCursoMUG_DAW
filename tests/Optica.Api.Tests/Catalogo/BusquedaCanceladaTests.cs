using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Optica.Domain.Catalogo;

namespace Optica.Api.Tests.Catalogo;

/// <summary>
/// FEAT-013, D-16 y R-05: la búsqueda en vivo cancela el pedido anterior en cada pausa de escritura. Un pedido que el
/// navegador abandona a mitad de camino no es un error del sistema: no deja el registro "Error no controlado".
/// </summary>
/// <remarks>
/// Pasa sin cambios en <c>ManejadorErroresNoControlados</c> porque el <c>ExceptionHandlerMiddleware</c> de ASP.NET Core
/// (.NET 8 o posterior) no llama a los <c>IExceptionHandler</c> cuando la excepción es una
/// <see cref="OperationCanceledException"/> con <c>RequestAborted</c> cancelado: la registra en Debug como pedido
/// abortado y termina. Si una versión futura dejara de hacerlo, esta prueba falla y corresponde el cambio de D-16 en el
/// manejador. Para saber que el pedido terminó no lee el log: un middleware de prueba, el primero del pipeline, avisa al
/// salir (después de él, el manejador de errores ya no puede escribir nada de este pedido).
/// </remarks>
public sealed class BusquedaCanceladaTests
{
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task BusquedaCancelada_NoRegistraErrorNoControlado()
    {
        var encuentro = new Encuentro();
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IArticuloRepository>();
            servicios.AddScoped<IArticuloRepository>(_ => new ArticulosQueEsperan(encuentro));
            servicios.AddSingleton<IStartupFilter>(new AvisoDeFin(encuentro));
        });
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        using var cancelacion = new CancellationTokenSource();
        using var pedido = new HttpRequestMessage(HttpMethod.Get, "/api/articulos?texto=arm&pagina=1");
        pedido.Headers.Add("Cookie", cookie);
        var envio = cliente.SendAsync(pedido, cancelacion.Token);

        // La búsqueda ya está en la base cuando el navegador la abandona, como un pedido viejo de la búsqueda en vivo.
        await encuentro.Entro.Task.WaitAsync(Espera);
        await cancelacion.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => envio);
        await encuentro.Termino.Task.WaitAsync(Espera);
        Assert.True(encuentro.Cancelada, "La Api no recibió la cancelación del pedido.");
        var lineas = fabrica.Registro.Lineas;
        Assert.DoesNotContain(lineas, l => l.Mensaje.Contains("Error no controlado", StringComparison.Ordinal));
        Assert.DoesNotContain(lineas, l => l.Nivel >= LogLevel.Error);
    }

    /// <summary>Lo que la prueba sabe de la búsqueda que se cancela.</summary>
    private sealed class Encuentro
    {
        public TaskCompletionSource Entro { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>El pedido del listado salió del pipeline completo, manejador de errores incluido.</summary>
        public TaskCompletionSource Termino { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public volatile bool Cancelada;
    }

    /// <summary>Pone primero en el pipeline un middleware que avisa cuando termina un pedido al listado.</summary>
    private sealed class AvisoDeFin(Encuentro encuentro) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> siguiente) => aplicacion =>
        {
            aplicacion.Use(async (contexto, proximo) =>
            {
                try
                {
                    await proximo(contexto);
                }
                finally
                {
                    if (contexto.Request.Path.StartsWithSegments("/api/articulos", StringComparison.Ordinal))
                    {
                        encuentro.Termino.TrySetResult();
                    }
                }
            });
            siguiente(aplicacion);
        };
    }

    /// <summary>Un repositorio cuya búsqueda queda esperando hasta que el pedido se cancela, como una consulta lenta.</summary>
    private sealed class ArticulosQueEsperan(Encuentro encuentro) : IArticuloRepository
    {
        private static NotSupportedException NoUsado() => new("No lo usa esta prueba.");

        public async Task<PaginaArticulos> BuscarAsync(
            string? textoPlegado, int pagina, int tamano, bool soloActivos = false,
            CancellationToken cancelacion = default)
        {
            encuentro.Entro.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancelacion);
            }
            catch (OperationCanceledException)
            {
                encuentro.Cancelada = true;
                throw;
            }

            throw NoUsado();
        }

        public Task<bool> ExisteClaveAsync(string clave, CancellationToken cancelacion = default) => throw NoUsado();

        public void Agregar(Articulo articulo) => throw NoUsado();

        public Task<Articulo?> ObtenerAsync(long codigo, CancellationToken cancelacion = default) => throw NoUsado();

        public Task<Articulo?> ObtenerPorClaveAsync(string clave, CancellationToken cancelacion = default) =>
            throw NoUsado();

        public Task<bool> ExisteClaveDeOtroAsync(string clave, long codigo, CancellationToken cancelacion = default) =>
            throw NoUsado();

        public void Actualizar(Articulo articulo, long versionLeida) => throw NoUsado();

        public void CambiarEstado(long codigo, bool activo) => throw NoUsado();

        public Task<IReadOnlyList<Articulo>> ListarParaRecalculoAsync(CancellationToken cancelacion = default) =>
            throw NoUsado();

        public void RegistrarPrecioRecalculado(Articulo articulo) => throw NoUsado();

        public void RegistrarCostoActualizado(Articulo articulo) => throw NoUsado();
    }
}
