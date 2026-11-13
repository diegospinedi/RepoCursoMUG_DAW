using Optica.Domain.Presupuestos;

namespace Optica.Domain.Tests.Presupuestos;

/// <summary>
/// El filtro de la búsqueda de presupuestos (Block 1 de FEAT-006): Apellido y Nombre plegados, DNI solo con lo que
/// queda sin puntos, guiones ni espacios, y el rango de fechas. Los datos de clientes son ficticios.
/// </summary>
public class FiltroPresupuestosTests
{
    private static readonly DateOnly Diez = new(2026, 3, 10);
    private static readonly DateOnly Veinte = new(2026, 3, 20);

    [Fact]
    public void Filtro_ApellidoConAcentosYMayusculas_SePliega()
    {
        var filtro = FiltroPresupuestos.Crear("  GONZÁLEZ ", " María José ", null, null, null);

        // FR-03: la misma regla que la búsqueda de artículos (PlegadoBusqueda, ADR-011).
        Assert.Equal("gonzalez", filtro.Apellido);
        Assert.Equal("maria jose", filtro.Nombre);
        Assert.Null(filtro.Dni);
        Assert.Null(filtro.Desde);
        Assert.Null(filtro.Hasta);
    }

    [Fact]
    public void Filtro_DniConPuntosYGuiones_SoloDigitos()
    {
        // FR-04, D-06: se quitan puntos, guiones y espacios; lo demás queda como está.
        Assert.Equal("234567", FiltroPresupuestos.Crear(null, null, "23.456-7", null, null).Dni);
        Assert.Equal("23456", FiltroPresupuestos.Crear(null, null, " 23 456 ", null, null).Dni);
        Assert.Equal("23a", FiltroPresupuestos.Crear(null, null, "2.3-a", null, null).Dni);
    }

    [Fact]
    public void Filtro_TextosVacios_NoCargados()
    {
        var blancos = FiltroPresupuestos.Crear("   ", "", "..-", null, null);
        var nulos = FiltroPresupuestos.Crear(null, null, null, null, null);

        // Un texto que queda vacío después de normalizar es un filtro no cargado (FR-02, FR-07).
        Assert.Null(blancos.Apellido);
        Assert.Null(blancos.Nombre);
        Assert.Null(blancos.Dni);
        Assert.Null(nulos.Apellido);
        Assert.Null(nulos.Nombre);
        Assert.Null(nulos.Dni);

        // Un texto hecho solo de marcas de acento queda vacío al plegarlo.
        Assert.Null(FiltroPresupuestos.Crear("́", " ̈ ", " . - ", null, null).Apellido);
        Assert.Null(FiltroPresupuestos.Crear(null, " ̈ ", null, null, null).Nombre);
    }

    [Fact]
    public void Filtro_DesdePosteriorAHasta_Rechazado()
    {
        var error = Assert.Throws<FiltroInvalidoException>(
            () => FiltroPresupuestos.Crear("Ficticiez", "Prueboncia", "23456789", Veinte, Diez));

        // FR-06: texto fijo, sin las fechas ni los datos del cliente.
        // Se mira el mensaje y no ToString: los números de línea de la traza podrían contener "10" o "20".
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Null(error.InnerException);
        foreach (var valor in new[] { "2026", "20", "10", "03", "Ficticiez", "Prueboncia", "23456789" })
        {
            Assert.DoesNotContain(valor, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Filtro_DesdeIgualAHasta_Valido()
    {
        var mismoDia = FiltroPresupuestos.Crear(null, null, null, Diez, Diez);
        Assert.Equal(Diez, mismoDia.Desde);
        Assert.Equal(Diez, mismoDia.Hasta);

        // Un solo límite también es válido (FR-05).
        Assert.Equal(Diez, FiltroPresupuestos.Crear(null, null, null, Diez, null).Desde);
        Assert.Equal(Veinte, FiltroPresupuestos.Crear(null, null, null, null, Veinte).Hasta);
    }

    [Fact]
    public void Filtro_ToString_SinDatosDelCliente()
    {
        var texto = FiltroPresupuestos.Crear("Ficticiez", "Prueboncia", "23.456.789", Diez, Veinte).ToString();

        // Ley 25.326: ni los textos recibidos ni los normalizados.
        Assert.NotNull(texto);
        foreach (var dato in new[] { "Ficticiez", "ficticiez", "Prueboncia", "prueboncia", "23.456.789", "23456789" })
        {
            Assert.DoesNotContain(dato, texto, StringComparison.Ordinal);
        }
    }
}
