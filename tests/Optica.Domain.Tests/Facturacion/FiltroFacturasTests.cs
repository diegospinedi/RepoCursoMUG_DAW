using Optica.Domain.Facturacion;

namespace Optica.Domain.Tests.Facturacion;

/// <summary>
/// El filtro de la búsqueda de facturas (Block 6 de FEAT-009): Apellido y Nombre plegados con la regla de Presupuestos
/// (D-13), DNI y número sin puntos, guiones ni espacios (FR-20), y el rango de fechas (FR-21). Datos ficticios (D-08).
/// </summary>
public class FiltroFacturasTests
{
    private static readonly DateOnly Diez = new(2026, 3, 10);
    private static readonly DateOnly Veinte = new(2026, 3, 20);

    [Fact]
    public void Filtro_DesdePosteriorAHasta_Rechazado()
    {
        // AC-22: Desde 20/03/2026 y Hasta 10/03/2026 no se busca; el mensaje es fijo y no repite valores.
        var error = Assert.Throws<FiltroFacturasInvalidoException>(
            () => FiltroFacturas.Crear("González", "María José", "00345678", "0003-00034561", Veinte, Diez));

        Assert.Equal("La fecha Desde no puede ser posterior a Hasta.", error.Message);
        Assert.DoesNotContain("2026", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("00345678", error.Message, StringComparison.Ordinal);

        // Los límites iguales o uno solo cargado son válidos (FR-21, ambos inclusive).
        var mismoDia = FiltroFacturas.Crear(null, null, null, null, Diez, Diez);
        Assert.Equal(Diez, mismoDia.Desde);
        Assert.Equal(Diez, mismoDia.Hasta);
        Assert.Null(FiltroFacturas.Crear(null, null, null, null, Veinte, null).Hasta);
        Assert.Null(FiltroFacturas.Crear(null, null, null, null, null, Diez).Desde);

        // La clase no muestra los filtros en ToString (Ley 25.326).
        var filtro = FiltroFacturas.Crear("González", "María José", "00345678", "34561", Diez, Veinte);
        Assert.DoesNotContain("gonzalez", filtro.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("00345678", filtro.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Filtro_DniYNumero_SinPuntosNiGuiones()
    {
        // AC-20: "00.345" busca "00345"; AC-19: "0003-00034561" busca los dígitos del texto visible.
        var filtro = FiltroFacturas.Crear("  GONZÁLEZ ", " María José ", "00.345", "0003-00034561", null, null);

        Assert.Equal("00345", filtro.Dni);
        Assert.Equal("000300034561", filtro.Numero);

        // Apellido y Nombre plegados como en la búsqueda de presupuestos (FR-19, D-13).
        Assert.Equal("gonzalez", filtro.Apellido);
        Assert.Equal("maria jose", filtro.Nombre);

        // Espacios también; cualquier otro carácter queda y entonces no coincide con ninguna factura.
        Assert.Equal("00345678", FiltroFacturas.Crear(null, null, " 00 345-678 ", null, null, null).Dni);
        Assert.Equal("34561", FiltroFacturas.Crear(null, null, null, " 345.61 ", null, null).Numero);
        Assert.Equal("3a", FiltroFacturas.Crear(null, null, "3.-a", null, null, null).Dni);

        // Un texto que queda vacío después de normalizar es un filtro no cargado (FR-22).
        var vacio = FiltroFacturas.Crear("  ", "", " .- ", "--", null, null);
        Assert.Null(vacio.Apellido);
        Assert.Null(vacio.Nombre);
        Assert.Null(vacio.Dni);
        Assert.Null(vacio.Numero);
    }
}
