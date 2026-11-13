using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;

namespace Optica.Api.Tests;

/// <summary>
/// R-14 (spec FEAT-007, Block 5): las pruebas de la Api nunca emiten contra producción, aunque la PC tenga variables de
/// entorno de ARCA. <see cref="FabricaApi"/> fija <c>Arca:Entorno = Simulado</c> y <c>Arca:Simulado:Respuesta =
/// autorizar</c> en memoria, por encima de las variables de entorno. La prueba cambia una variable de entorno del
/// proceso: corre en su propia colección, sin paralelismo, y la restaura al terminar.
/// </summary>
[Collection(Nombre)]
public sealed class FabricaApiArcaTests
{
    public const string Nombre = "Variables de entorno de ARCA";

    [Fact]
    public async Task Fabrica_ConVariableArcaEntornoProduccion_QuedaEnSimulado()
    {
        var entornoAnterior = Environment.GetEnvironmentVariable("Arca__Entorno");
        var respuestaAnterior = Environment.GetEnvironmentVariable("Arca__Simulado__Respuesta");
        Environment.SetEnvironmentVariable("Arca__Entorno", "Produccion");
        Environment.SetEnvironmentVariable("Arca__Simulado__Respuesta", "rechazar");
        try
        {
            await using var fabrica = new FabricaApi();
            using var cliente = fabrica.CreateClient();

            var autorizador = fabrica.Services.GetRequiredService<IAutorizadorComprobantes>();

            Assert.Equal(EntornoArca.Simulado, autorizador.Entorno);
            var resultado = await autorizador.AutorizarAsync(
                new SolicitudComprobante(
                    TipoComprobante.FacturaC,
                    3,
                    new DateOnly(2026, 10, 30),
                    ConceptoFactura.Productos,
                    ReceptorFactura.SinIdentificar,
                    ImportesFactura.ParaFacturaC(1000m)),
                CancellationToken.None);
            Assert.IsType<ResultadoAutorizacion.Autorizada>(resultado);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Arca__Entorno", entornoAnterior);
            Environment.SetEnvironmentVariable("Arca__Simulado__Respuesta", respuestaAnterior);
        }
    }
}

/// <summary>Sin paralelismo: cambia variables de entorno del proceso, que leen todas las Api que arrancan.</summary>
[CollectionDefinition(FabricaApiArcaTests.Nombre, DisableParallelization = true)]
public sealed class ColeccionVariablesArca
{
}
