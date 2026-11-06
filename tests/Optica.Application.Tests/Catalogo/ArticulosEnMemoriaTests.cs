using Optica.Domain.Catalogo;

namespace Optica.Application.Tests.Catalogo;

/// <summary>
/// El doble de la unidad de trabajo se comporta como <c>UnidadDeTrabajoEf</c> donde las pruebas de los casos de uso
/// dependen de eso (D-20 de FEAT-002c).
/// </summary>
public class ArticulosEnMemoriaTests
{
    public static TheoryData<string> Registros => new()
    {
        "Agregar",
        "Actualizar",
        "CambiarEstado",
        "RegistrarPrecioRecalculado",
    };

    /// <summary>
    /// Como la unidad real: una operación que termina con algo registrado sin confirmar no se confirma; se revierte, se
    /// descarta lo pendiente y lanza <see cref="InvalidOperationException"/>.
    /// </summary>
    [Theory]
    [MemberData(nameof(Registros))]
    public async Task Operacion_TerminaConRegistrosSinConfirmar_LanzaYRevierte(string registro)
    {
        var articulos = new ArticulosEnMemoria();
        var existente = articulos.Sembrar("ABC-1", costo: 1210m, version: 3);
        var codigo = existente.Codigo!.Value;

        await Assert.ThrowsAsync<InvalidOperationException>(() => articulos.EjecutarEnTransaccionAsync(
            _ =>
            {
                Registrar(articulos, registro, existente);
                return Task.FromResult(true);
            }));

        Assert.Equal(1, articulos.Operaciones);
        Assert.Equal(0, articulos.OperacionesConfirmadas);
        var guardado = Assert.Single(articulos.Guardados);
        Assert.Equal((codigo, 1815m, 3L, true), (guardado.Codigo!.Value, guardado.PrecioVenta, guardado.Version, guardado.Activo));

        // Lo pendiente se descartó: una confirmación posterior no lo graba.
        await articulos.ConfirmarAsync();
        guardado = Assert.Single(articulos.Guardados);
        Assert.Equal((codigo, 1815m, 3L, true), (guardado.Codigo!.Value, guardado.PrecioVenta, guardado.Version, guardado.Activo));
    }

    [Fact]
    public async Task Operacion_ConfirmaLoRegistrado_SeConfirma()
    {
        var articulos = new ArticulosEnMemoria();

        var resultado = await articulos.EjecutarEnTransaccionAsync(async cancelacion =>
        {
            articulos.Agregar(Articulo.Nuevo("ABC-1", "Armazón", 1210m, 50m, 1815m));
            await articulos.ConfirmarAsync(cancelacion);
            return 7;
        });

        Assert.Equal(7, resultado);
        Assert.Equal(1, articulos.OperacionesConfirmadas);
        Assert.Single(articulos.Guardados);
    }

    private static void Registrar(ArticulosEnMemoria articulos, string registro, Articulo existente)
    {
        switch (registro)
        {
            case "Agregar":
                articulos.Agregar(Articulo.Nuevo("DEF-2", "Armazón", 100m, 50m, 150m));
                break;
            case "Actualizar":
                articulos.Actualizar(
                    new Articulo(
                        existente.Codigo, "ABC-1", "Otra", 1210m, 60m, 1936m, existente.Activo, existente.Version),
                    existente.Version);
                break;
            case "CambiarEstado":
                articulos.CambiarEstado(existente.Codigo!.Value, activo: false);
                break;
            case "RegistrarPrecioRecalculado":
                articulos.RegistrarPrecioRecalculado(new Articulo(
                    existente.Codigo, "ABC-1", "Armazón", 1210m, 50m, 1850m, existente.Activo, existente.Version));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(registro));
        }
    }
}
