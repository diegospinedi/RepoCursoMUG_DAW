using Optica.Application.Comun;
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
        "RegistrarCostoActualizado",
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

    /// <summary>
    /// Como el repositorio real (Block 3 de FEAT-011): el costo actualizado cambia solo el costo, la venta y la versión;
    /// un inactivo sigue inactivo, y con una versión o un estado que ya no son los leídos, nada se guarda.
    /// </summary>
    [Fact]
    public async Task RegistrarCostoActualizado_TokensYSoloCostoVentaYVersion()
    {
        var articulos = new ArticulosEnMemoria();
        var inactivo = articulos.Sembrar("ABC-1", "Armazón", costo: 1210m, activo: false, version: 2);
        var codigo = inactivo.Codigo!.Value;

        articulos.RegistrarCostoActualizado(
            new Articulo(codigo, "ZZZ-9", "Otra", 2420m, 10m, 3630m, activo: false, version: 2));
        await articulos.ConfirmarAsync();

        var guardado = articulos.Guardado(codigo);
        Assert.Equal(
            ("ABC-1", "Armazón", 2420m, 50m, 3630m, false, 3L),
            (guardado.CodigoProveedor, guardado.Descripcion, guardado.PrecioCosto, guardado.MargenUtilidad,
                guardado.PrecioVenta, guardado.Activo, guardado.Version));
        Assert.Single(articulos.CostosActualizadosRegistrados);

        articulos.RegistrarCostoActualizado(
            new Articulo(codigo, "ABC-1", "Armazón", 1m, 50m, 1.5m, activo: false, version: 2));
        var porVersion = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(() => articulos.ConfirmarAsync());
        articulos.RegistrarCostoActualizado(
            new Articulo(codigo, "ABC-1", "Armazón", 1m, 50m, 1.5m, activo: true, version: 3));
        var porEstado = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(() => articulos.ConfirmarAsync());

        Assert.Equal(MotivoConflicto.Concurrencia, porVersion.Motivo);
        Assert.Equal(MotivoConflicto.Concurrencia, porEstado.Motivo);
        Assert.Equal((2420m, 3L), (articulos.Guardado(codigo).PrecioCosto, articulos.Guardado(codigo).Version));

        // Como el real: la versión siguiente se calcula con checked al registrar, y si desborda no queda nada registrado.
        Assert.Throws<OverflowException>(() => articulos.RegistrarCostoActualizado(
            new Articulo(codigo, "ABC-1", "Armazón", 1m, 50m, 1.5m, activo: false, version: long.MaxValue)));
        Assert.Equal(3, articulos.CostosActualizadosRegistrados.Count);
        await articulos.ConfirmarAsync();
        Assert.Equal((2420m, 3L), (articulos.Guardado(codigo).PrecioCosto, articulos.Guardado(codigo).Version));
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
            case "RegistrarCostoActualizado":
                articulos.RegistrarCostoActualizado(new Articulo(
                    existente.Codigo, "ABC-1", "Armazón", 2420m, 50m, 3630m, existente.Activo, existente.Version));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(registro));
        }
    }
}
