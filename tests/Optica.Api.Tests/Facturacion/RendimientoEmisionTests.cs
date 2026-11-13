using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Xunit.Abstractions;

namespace Optica.Api.Tests.Facturacion;

/// <summary>
/// AC-25 (NFR-02), medido como D-23: por la Api, 20 emisiones con un autorizador de prueba que demora y registra su
/// espera; la diferencia entre el tiempo total del pedido y la espera de ARCA queda por debajo de 2 s en al menos 19.
/// </summary>
public sealed class RendimientoEmisionTests(ITestOutputHelper salida)
{
    private const int Emisiones = 20;
    private static readonly TimeSpan Demora = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Emitir_20Facturas_DiferenciaMenorA2sEn19()
    {
        var autorizador = new AutorizadorQueDemora(Demora);
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IAutorizadorComprobantes>();
            servicios.AddSingleton<IAutorizadorComprobantes>(autorizador);
        });
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numeros = new List<long>();
        for (var indice = 0; indice < Emisiones; indice++)
        {
            numeros.Add(await escenario.FinalAsync(1000m + indice));
        }

        var diferencias = new List<TimeSpan>();
        foreach (var numero in numeros)
        {
            var reloj = Stopwatch.StartNew();
            var respuesta = await escenario.EmitirAsync(numero);
            reloj.Stop();

            Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
            Assert.True(autorizador.Esperas.TryGetValue(numero, out var espera), "El autorizador no registró su espera.");
            // La espera de ARCA es real: el autorizador demoró de verdad.
            Assert.True(espera >= Demora - TimeSpan.FromMilliseconds(20), $"Espera registrada demasiado corta: {espera}.");
            diferencias.Add(reloj.Elapsed - espera);
        }

        salida.WriteLine(
            "Diferencias (ms): " + string.Join(", ", diferencias.Select(d => ((int)d.TotalMilliseconds).ToString())));
        Assert.Equal(Emisiones, autorizador.Esperas.Count);
        Assert.True(
            diferencias.Count(diferencia => diferencia < Limite) >= Emisiones - 1,
            $"Menos de {Emisiones - 1} emisiones agregaron menos de 2 s a la espera de ARCA.");
    }

    /// <summary>
    /// Demora <paramref name="demora"/> de verdad y registra la espera medida por presupuesto (por el total, que es único
    /// en esta prueba); autoriza con números consecutivos.
    /// </summary>
    private sealed class AutorizadorQueDemora(TimeSpan demora) : IAutorizadorComprobantes
    {
        private long _numero;

        public ConcurrentDictionary<long, TimeSpan> Esperas { get; } = new();

        public EntornoArca Entorno => EntornoArca.Simulado;

        public TimeSpan TiempoMaximo => TimeSpan.FromSeconds(30);

        public async Task<ResultadoAutorizacion> AutorizarAsync(
            SolicitudComprobante solicitud, CancellationToken cancelacion)
        {
            var reloj = Stopwatch.StartNew();
            await Task.Delay(demora, CancellationToken.None);
            reloj.Stop();
            // Los presupuestos se numeran desde 1 y cada uno tiene total 1000 + (número - 1).
            var presupuesto = (long)solicitud.Importes.Total - 999;
            Esperas[presupuesto] = reloj.Elapsed;
            return new ResultadoAutorizacion.Autorizada(
                Interlocked.Increment(ref _numero), "71234567890123", solicitud.Fecha.AddDays(10));
        }
    }
}
