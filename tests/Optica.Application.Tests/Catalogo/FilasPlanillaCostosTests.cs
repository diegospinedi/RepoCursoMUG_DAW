using Optica.Application.Catalogo;
using Optica.Domain.Catalogo;
using Optica.Domain.Configuracion;

namespace Optica.Application.Tests.Catalogo;

/// <summary>
/// Las filas de la planilla sin tipos del dominio, para la Api (FEAT-011, Block 4): orden de la hoja, texto de la
/// razón, costo aplicado, indicaciones, código sin acotar y las ramas defensivas. Las filas salen de
/// <see cref="PlanillaCostos.Evaluar"/> con artículos y configuración en memoria. Códigos y precios ficticios.
/// </summary>
public class FilasPlanillaCostosTests
{
    private static readonly ConfiguracionNegocio Configuracion =
        new(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: null);

    [Fact]
    public void FilasNoAplicadas_OrdenDeLaHojaYTextoDeLaRazon()
    {
        var aplicada = Aplicada(
            Fila(2, "NO-1", 10m),
            new FilaPlanilla(3, new CeldaPlanilla.Texto("ABC-1"), new CeldaPlanilla.Texto("abc")),
            Fila(4, "DEF-2", 200m),
            Fila(5, "REP-3", 30m),
            Fila(6, "REP-3", 40m));

        Assert.Equal(
            [
                (2, "NO-1", "código inexistente"),
                (3, "ABC-1", "precio inválido"),
                (5, "REP-3", "código repetido en la planilla"),
                (6, "REP-3", "código repetido en la planilla"),
            ],
            aplicada.FilasNoAplicadas().Select(fila => (fila.Fila, fila.Codigo, fila.Razon)));
    }

    [Fact]
    public void FilasParaRevisar_CostoAplicadoEIndicacionesEnElOrdenDeLaHoja()
    {
        var aplicada = Aplicada(
            Fila(2, "ABC-1", 2420m),
            Fila(3, "NEG-4", -100m),
            Fila(4, "INA-5", 400m),
            Fila(5, "AMB-6", 0m));

        var filas = aplicada.FilasParaRevisar();

        Assert.Equal([3, 4, 5], filas.Select(fila => fila.Fila));
        Assert.Equal(["NEG-4", "INA-5", "AMB-6"], filas.Select(fila => fila.Codigo));
        Assert.Equal([-100m, 400m, 0m], filas.Select(fila => fila.PrecioCosto));
        Assert.Equal(["actualizado con precio negativo o cero"], filas[0].Indicaciones);
        Assert.Equal(["inactivo"], filas[1].Indicaciones);
        Assert.Equal(["actualizado con precio negativo o cero", "inactivo"], filas[2].Indicaciones);
    }

    /// <summary>D-10: el código va tal como vino (recortado por el dominio); acotarlo para mostrarlo es de la Api.</summary>
    [Fact]
    public void Filas_CodigoLargo_SinAcotar()
    {
        var largo = new string('L', 70);

        var fila = Assert.Single(Aplicada(Fila(2, largo, 10m)).FilasNoAplicadas());

        Assert.Equal(largo, fila.Codigo);
    }

    [Fact]
    public void Filas_AplicadaNull_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FilasPlanillaCostos.FilasNoAplicadas(null!));
        Assert.Throws<ArgumentNullException>(() => FilasPlanillaCostos.FilasParaRevisar(null!));
    }

    /// <summary>
    /// Un resultado armado fuera del caso de uso con una fila aplicada entre las no aplicadas (sin razón) o una no
    /// aplicada entre las de revisar (sin artículo) es un error de programación: lanza con un mensaje fijo, sin el código.
    /// </summary>
    [Fact]
    public void Filas_ResultadoIncoherente_LanzaSinElCodigo()
    {
        var filas = PlanillaCostos.Evaluar([Fila(2, "ABC-1", 2420m), Fila(3, "NO-QZ7", 10m)], Catalogo(), Configuracion);
        var aplicadaEntreNoAplicadas = new ResultadoPlanillaCostos.Aplicada(2, 1, [filas[0]], []);
        var noAplicadaEntreRevisar = new ResultadoPlanillaCostos.Aplicada(2, 1, [], [filas[1]]);

        var sinRazon = Assert.Throws<InvalidOperationException>(() => aplicadaEntreNoAplicadas.FilasNoAplicadas());
        var sinArticulo = Assert.Throws<InvalidOperationException>(() => noAplicadaEntreRevisar.FilasParaRevisar());

        Assert.DoesNotContain("ABC", sinRazon.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("QZ7", sinArticulo.Message, StringComparison.Ordinal);
    }

    // ---- Auxiliares ----

    /// <summary>
    /// ABC-1 (costo 1210), DEF-2, REP-3 y NEG-4 activos; INA-5 y AMB-6 inactivos; los demás con costo 100; todos con
    /// margen 50.
    /// </summary>
    private static Dictionary<string, Articulo> Catalogo()
    {
        Articulo[] articulos =
        [
            new(1, "ABC-1", "Armazón", 1210m, 50m, 1815m, true, 0),
            new(2, "DEF-2", "Lente", 100m, 50m, 150m, true, 0),
            new(3, "REP-3", "Estuche", 100m, 50m, 150m, true, 0),
            new(4, "NEG-4", "Cordón", 100m, 50m, 150m, true, 0),
            new(5, "INA-5", "Paño", 100m, 50m, 150m, false, 0),
            new(6, "AMB-6", "Líquido", 100m, 50m, 150m, false, 0),
        ];
        return articulos.ToDictionary(articulo => articulo.CodigoProveedorClave, StringComparer.Ordinal);
    }

    /// <summary>Evalúa las filas y arma el resultado como lo hace el caso de uso.</summary>
    private static ResultadoPlanillaCostos.Aplicada Aplicada(params FilaPlanilla[] filas)
    {
        var resultados = PlanillaCostos.Evaluar(filas, Catalogo(), Configuracion);
        var aplicadas = resultados.Where(fila => fila.EsAplicada).ToList();
        return new ResultadoPlanillaCostos.Aplicada(
            resultados.Count,
            aplicadas.Count,
            [.. resultados.Where(fila => !fila.EsAplicada)],
            [.. aplicadas.Where(fila => fila.PrecioNoPositivo || fila.Inactivo)]);
    }

    private static FilaPlanilla Fila(int numero, string codigo, decimal precio) =>
        new(numero, new CeldaPlanilla.Texto(codigo), new CeldaPlanilla.Numero(precio));
}
