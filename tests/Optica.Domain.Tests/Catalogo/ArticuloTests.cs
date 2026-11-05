using System.Globalization;
using Optica.Domain.Catalogo;
using Optica.Domain.Comun;
using Optica.Domain.Configuracion;

namespace Optica.Domain.Tests.Catalogo;

public class ArticuloTests
{
    [Fact]
    public void Clave_EspaciosYMayusculas_IgualQueABC1()
    {
        Assert.Equal("ABC-1", NormalizacionCodigo.Clave("ABC-1"));
        Assert.Equal(NormalizacionCodigo.Clave("ABC-1"), NormalizacionCodigo.Clave(" abc-1 "));
    }

    [Fact]
    public void Clave_EspacioDuroYTabulador_IgualQueABC1()
    {
        // D-13: todo espacio en blanco de Unicode en los extremos.
        Assert.Equal("ABC-1", NormalizacionCodigo.Clave("ABC-1\u00A0"));
        Assert.Equal("ABC-1", NormalizacionCodigo.Clave("\tABC-1"));
        Assert.Equal("ABC-1", NormalizacionCodigo.Clave("\u00A0\t abc-1\r\n"));
    }

    [Fact]
    public void Clave_BomYSiguienteLinea_IgualQueABC()
    {
        // D-17: char.IsWhiteSpace más U+FEFF (BOM) en los dos extremos.
        var clave = NormalizacionCodigo.Clave("ABC");

        Assert.Equal(clave, NormalizacionCodigo.Clave("\uFEFFABC"));
        Assert.Equal(clave, NormalizacionCodigo.Clave("ABC\u0085"));
        Assert.Equal(clave, NormalizacionCodigo.Clave("\uFEFF \u0085abc\uFEFF\u00A0"));
    }

    [Fact]
    public void Clave_FormasUnicodeEquivalentes_MismaClave()
    {
        // ADR-009: "é" precompuesta (U+00E9) y "e" + acento agudo combinado (U+0301) son el mismo código.
        Assert.Equal(NormalizacionCodigo.Clave("caf\u00E9"), NormalizacionCodigo.Clave("cafe\u0301"));
        Assert.Equal("CAF\u00C9", NormalizacionCodigo.Clave("cafe\u0301"));
    }

    [Fact]
    public void Recortar_ConservaLosEspaciosInternos()
    {
        Assert.Equal("ABC 1", NormalizacionCodigo.Recortar("\u00A0 ABC 1\t"));
        Assert.Equal("A\uFEFFB", NormalizacionCodigo.Recortar("\uFEFFA\uFEFFB\uFEFF"));
        Assert.Equal("abc", NormalizacionCodigo.Recortar("abc"));
        Assert.Equal(string.Empty, NormalizacionCodigo.Recortar("\u00A0\t \uFEFF"));
    }

    [Fact]
    public void Constructor_ValoresValidos_GuardaRecortadoYClave()
    {
        var articulo = new Articulo(
            null, "  abc-1\u00A0", "\tArmaz\u00F3n met\u00E1lico  ", 1210m, 50m, 1815m, activo: true, version: 0);

        Assert.Null(articulo.Codigo);
        Assert.Equal("abc-1", articulo.CodigoProveedor);
        Assert.Equal("ABC-1", articulo.CodigoProveedorClave);
        Assert.Equal("Armaz\u00F3n met\u00E1lico", articulo.Descripcion);
        Assert.Equal(1210m, articulo.PrecioCosto);
        Assert.Equal(50m, articulo.MargenUtilidad);
        Assert.Equal(1815m, articulo.PrecioVenta);
        Assert.False(articulo.CostoNoPositivo);
    }

    [Fact]
    public void Constructor_ConCodigo_ExponeElCodigo()
    {
        var articulo = new Articulo(42L, "ABC-1", "Lente", 1000m, 50m, 1500m, activo: true, version: 0);

        Assert.Equal(42L, articulo.Codigo);
    }

    [Theory]
    [InlineData("0", "0", true)]
    [InlineData("-100", "-150", true)]
    [InlineData("-0.01", "-0.01", true)]
    [InlineData("0.01", "0.02", false)]
    public void Articulo_CostoNoPositivo_Advertencia(string costo, string venta, bool esperado)
    {
        var articulo = new Articulo(null, "ABC-1", "Lente", Leer(costo), 50m, Leer(venta), activo: true, version: 0);

        Assert.Equal(esperado, articulo.CostoNoPositivo);
    }

    // El último valor es el texto que no debe aparecer en el mensaje; null cuando el campo no trae ningún valor que
    // pueda filtrarse (ausente o solo espacios).
    public static TheoryData<string?, string?, decimal, decimal, decimal, string, string?> ValoresInvalidos => new()
    {
        { null, "Lente", 10m, 10m, 11m, "codigoProveedor", null },
        { "\u00A0 ", "Lente", 10m, 10m, 11m, "codigoProveedor", null },
        { new string('Q', 51), "Lente", 10m, 10m, 11m, "codigoProveedor", "QQQQ" },
        { "ABC-1", " ", 10m, 10m, 11m, "descripcion", null },
        { "ABC-1", new string('W', 201), 10m, 10m, 11m, "descripcion", "WWWW" },
        { "ABC-1", "Lente", 10.555m, 10m, 11m, "precioCosto", "10.555" },
        { "ABC-1", "Lente", ReglasImporte.ImporteMaximo + 0.01m, 10m, 11m, "precioCosto", "92233720368547758" },
        { "ABC-1", "Lente", 10m, 1000.01m, 11m, "margenUtilidad", "1000.01" },
        { "ABC-1", "Lente", 10m, -1m, 11m, "margenUtilidad", "-1" },
        { "ABC-1", "Lente", 10m, 10.555m, 11m, "margenUtilidad", "10.555" },
        { "ABC-1", "Lente", 10m, 10m, 11.555m, "precioVenta", "11.555" },
        { "ABC-1", "Lente", 10m, 10m, ReglasImporte.ImporteMaximo + 0.01m, "precioVenta", "92233720368547758" },
    };

    [Theory]
    [MemberData(nameof(ValoresInvalidos))]
    public void Articulo_ValorInvalido_LanzaSinIncluirElValor(
        string? codigo, string? descripcion, decimal costo, decimal margen, decimal venta, string parametro,
        string? valor)
    {
        var error = Assert.Throws<ArgumentException>(
            () => new Articulo(null, codigo!, descripcion!, costo, margen, venta, activo: true, version: 0));

        Assert.Equal(parametro, error.ParamName);
        if (valor is not null)
        {
            Assert.DoesNotContain(valor, error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(valor.Replace('.', ','), error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Articulo_TextoConUtf16Invalido_LanzaConElParametroDelCampo()
    {
        // D-16: la política lo rechaza antes de que Clave llegue a Normalize, que lanzaría sin ParamName.
        // En un [Fact] porque el runner serializa los argumentos de [InlineData].
        var casos = new (string Codigo, string Descripcion, string Parametro)[]
        {
            ("\uD800", "Lente", "codigoProveedor"),
            ("a\uDC00b", "Lente", "codigoProveedor"),
            ("ABC-1", "\uD800", "descripcion"),
            ("ABC-1", "a\uDC00b", "descripcion"),
        };

        foreach (var (codigo, descripcion, parametro) in casos)
        {
            var error = Assert.Throws<ArgumentException>(
                () => new Articulo(null, codigo, descripcion, 10m, 10m, 11m, activo: true, version: 0));

            Assert.Equal(parametro, error.ParamName);
        }
    }

    [Fact]
    public void Constructor_VersionNegativaInvalida_Lanza()
    {
        var error = Assert.Throws<ArgumentException>(
            () => new Articulo(1L, "ABC-1", "Lente", 10m, 10m, 11m, activo: true, version: -1L));

        Assert.Equal("version", error.ParamName);
        Assert.DoesNotContain("-1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nuevo_QuedaActivoConVersionCero()
    {
        var articulo = Articulo.Nuevo(" abc-1 ", " Lente ", 1210m, 50m, 1815m);

        Assert.Null(articulo.Codigo);
        Assert.True(articulo.Activo);
        Assert.Equal(0L, articulo.Version);
        Assert.Equal("abc-1", articulo.CodigoProveedor);
        Assert.Equal("ABC-1", articulo.CodigoProveedorClave);
        Assert.Equal("Lente", articulo.Descripcion);
        Assert.Equal(1815m, articulo.PrecioVenta);
    }

    [Fact]
    public void Modificar_InactivoRechazado_DevuelveResultadoInactivo()
    {
        // D-02, D-20: con datos que la política rechazaría, el resultado sigue siendo Inactivo.
        var inactivo = Guardado(activo: false);

        var resultado = inactivo.Modificar(null, null, null, null, ResponsableInscripto());

        Assert.True(resultado.EsInactivo);
        Assert.False(resultado.EsModificado);
        Assert.False(resultado.EsInvalido);
        Assert.Null(resultado.Articulo);
        Assert.Empty(resultado.Infracciones);
    }

    [Fact]
    public void Modificar_DatosInvalidos_DevuelveTodasLasInfracciones()
    {
        var resultado = Guardado().Modificar(
            null, new string('W', 201), 10.555m, 1000.01m, ResponsableInscripto());

        Assert.False(resultado.EsModificado);
        Assert.False(resultado.EsInactivo);
        Assert.True(resultado.EsInvalido);
        Assert.Equal(
            [
                InfraccionArticulo.CodigoFaltante,
                InfraccionArticulo.DescripcionDemasiadoLarga,
                InfraccionArticulo.CostoConMasDeDosDecimales,
                InfraccionArticulo.MargenFueraDeRango,
            ],
            resultado.Infracciones);
    }

    [Fact]
    public void Modificar_PrecioFueraDeRango_DevuelveInfraccionDeRango()
    {
        // D-06 de FEAT-002a: un error de rango en la lista, no un desborde.
        var resultado = Guardado().Modificar(
            "ABC-1", "Lente", ReglasImporte.ImporteMaximo, 1000m, ResponsableInscripto());

        Assert.False(resultado.EsModificado);
        Assert.True(resultado.EsInvalido);
        Assert.Equal([InfraccionArticulo.PrecioVentaFueraDeRango], resultado.Infracciones);
    }

    [Fact]
    public void Modificar_CostoYMargenInvalidos_NoAgregaInfraccionDeRango()
    {
        // Con el margen inválido el precio no se calcula: solo la infracción del margen, aunque el costo máximo con
        // margen 1000 daría fuera de rango.
        var resultado = Guardado().Modificar(
            "ABC-1", "Lente", ReglasImporte.ImporteMaximo, 1000.01m, ResponsableInscripto());

        Assert.True(resultado.EsInvalido);
        Assert.Equal([InfraccionArticulo.MargenFueraDeRango], resultado.Infracciones);
    }

    [Fact]
    public void Modificar_MargenDe50A60_Recalcula1936()
    {
        // AC-02: alícuota 21, múltiplo 0,01; 1210 × 1,60 = 1936.
        var resultado = Guardado().Modificar("ABC-1", "Lente", 1210m, 60m, ResponsableInscripto());

        Assert.True(resultado.EsModificado);
        Assert.False(resultado.EsInvalido);
        Assert.Empty(resultado.Infracciones);
        Assert.Equal(1936m, resultado.Articulo.PrecioVenta);
        Assert.Equal(60m, resultado.Articulo.MargenUtilidad);
        Assert.Equal(7L, resultado.Articulo.Codigo);
        Assert.Equal(3L, resultado.Articulo.Version);
        Assert.True(resultado.Articulo.Activo);
    }

    [Fact]
    public void Modificar_CostoDe1210A2420_Recalcula3630()
    {
        // AC-03: alícuota 21, múltiplo 0,01; 2420 × 1,50 = 3630.
        var resultado = Guardado().Modificar("ABC-1", "Lente", 2420m, 50m, ResponsableInscripto());

        Assert.True(resultado.EsModificado);
        Assert.Equal(3630m, resultado.Articulo.PrecioVenta);
        Assert.Equal(2420m, resultado.Articulo.PrecioCosto);
        Assert.Equal(7L, resultado.Articulo.Codigo);
        Assert.Equal(3L, resultado.Articulo.Version);
    }

    [Fact]
    public void Modificar_SoloDescripcion_ConservaPrecioVenta()
    {
        // D-03: con múltiplo 100 un recálculo daría 1900; sin cambios de costo ni margen se conserva 1815.
        var resultado = Guardado().Modificar(
            " xyz-9 ", " Armaz\U000000F3n nuevo ", 1210.00m, 50.0m, ResponsableInscripto(multiplo: 100m));

        Assert.True(resultado.EsModificado);
        Assert.Equal(1815m, resultado.Articulo.PrecioVenta);
        Assert.Equal("Armaz\U000000F3n nuevo", resultado.Articulo.Descripcion);
        Assert.Equal("xyz-9", resultado.Articulo.CodigoProveedor);
        Assert.Equal("XYZ-9", resultado.Articulo.CodigoProveedorClave);
        Assert.Equal(7L, resultado.Articulo.Codigo);
        Assert.Equal(3L, resultado.Articulo.Version);
    }

    [Fact]
    public void ConEstado_Desactivar_ConservaLosDatos()
    {
        // AC-05: solo cambia Activo (D-09).
        var original = Guardado();

        var desactivado = original.ConEstado(false);

        Assert.False(desactivado.Activo);
        Assert.Equal(original.Codigo, desactivado.Codigo);
        Assert.Equal(original.CodigoProveedor, desactivado.CodigoProveedor);
        Assert.Equal(original.CodigoProveedorClave, desactivado.CodigoProveedorClave);
        Assert.Equal(original.Descripcion, desactivado.Descripcion);
        Assert.Equal(original.PrecioCosto, desactivado.PrecioCosto);
        Assert.Equal(original.MargenUtilidad, desactivado.MargenUtilidad);
        Assert.Equal(original.PrecioVenta, desactivado.PrecioVenta);
        Assert.Equal(original.Version, desactivado.Version);
        Assert.True(original.Activo);
    }

    [Fact]
    public void ConEstado_Reactivar_QuedaActivo()
    {
        // AC-06.
        var inactivo = Guardado(activo: false);

        var reactivado = inactivo.ConEstado(true);

        Assert.True(reactivado.Activo);
        Assert.Equal(inactivo.Descripcion, reactivado.Descripcion);
        Assert.Equal(inactivo.PrecioVenta, reactivado.PrecioVenta);
        Assert.Equal(inactivo.Version, reactivado.Version);
    }

    [Fact]
    public void ConPrecioRecalculado_PrecioFueraDeRango_Rechazado()
    {
        // FEAT-002c, Block 1: un precio inválido con la configuración nueva es FueraDeRango, nunca un desborde.
        var articulo = new Articulo(
            9L, "MAX-1", "Lente", ReglasImporte.ImporteMaximo, 0m, ReglasImporte.ImporteMaximo, activo: true, version: 1L);

        var resultado = articulo.ConPrecioRecalculado(ResponsableInscripto(multiplo: 50m));

        Assert.Same(ResultadoRecalculo.FueraDeRango, resultado);
        Assert.True(resultado.EsFueraDeRango);
        Assert.False(resultado.EsCambiado);
        Assert.False(resultado.EsSinCambio);
        Assert.Null(resultado.Articulo);
    }

    [Fact]
    public void ConPrecioRecalculado_Multiplo50_1815A1850()
    {
        // AC-02 de FEAT-002c: costo 1210 y margen 50 pasan de 1815 a 1850 con múltiplo 50, sin editar el artículo.
        var original = Guardado();

        var resultado = original.ConPrecioRecalculado(ResponsableInscripto(multiplo: 50m));

        Assert.True(resultado.EsCambiado);
        Assert.False(resultado.EsSinCambio);
        Assert.False(resultado.EsFueraDeRango);
        Assert.Equal(1850m, resultado.Articulo.PrecioVenta);
        Assert.Equal(original.Codigo, resultado.Articulo.Codigo);
        Assert.Equal(original.CodigoProveedor, resultado.Articulo.CodigoProveedor);
        Assert.Equal(original.CodigoProveedorClave, resultado.Articulo.CodigoProveedorClave);
        Assert.Equal(original.Descripcion, resultado.Articulo.Descripcion);
        Assert.Equal(original.PrecioCosto, resultado.Articulo.PrecioCosto);
        Assert.Equal(original.MargenUtilidad, resultado.Articulo.MargenUtilidad);
        Assert.True(resultado.Articulo.Activo);
        Assert.Equal(original.Version, resultado.Articulo.Version);
        Assert.Equal(1815m, original.PrecioVenta);
    }

    [Fact]
    public void ConPrecioRecalculado_Inactivo_SeRecalcula()
    {
        // AC-03 de FEAT-002c: los inactivos también se recalculan y siguen inactivos.
        var inactivo = Guardado(activo: false);

        var resultado = inactivo.ConPrecioRecalculado(ResponsableInscripto(multiplo: 50m));

        Assert.True(resultado.EsCambiado);
        Assert.Equal(1850m, resultado.Articulo.PrecioVenta);
        Assert.False(resultado.Articulo.Activo);
        Assert.Equal(inactivo.Codigo, resultado.Articulo.Codigo);
        Assert.Equal(inactivo.Version, resultado.Articulo.Version);
    }

    [Fact]
    public void ConPrecioRecalculado_AlicuotaDistinta_SinCambio()
    {
        // ADR-010 y AC-01 de FEAT-002c: la alícuota se cancela; 1105 y 50 % dan 1657,50 con 21 y con 10,5.
        var articulo = new Articulo(11L, "ABC-2", "Lente", 1105m, 50m, 1657.50m, activo: true, version: 2L);

        var resultado = articulo.ConPrecioRecalculado(
            new ConfiguracionNegocio(10.5m, CondicionFiscal.ResponsableInscripto, 0.01m, null));

        Assert.Same(ResultadoRecalculo.SinCambio, resultado);
        Assert.True(resultado.EsSinCambio);
        Assert.False(resultado.EsCambiado);
        Assert.False(resultado.EsFueraDeRango);
        Assert.Null(resultado.Articulo);
    }

    [Fact]
    public void ConPrecioRecalculado_VigenteNula_LanzaError()
    {
        var error = Assert.Throws<ArgumentNullException>(() => Guardado().ConPrecioRecalculado(null!));

        Assert.Equal("vigente", error.ParamName);
        Assert.DoesNotContain("1815", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("1210", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConPrecioRecalculado_MismoPrecioOtraEscala_SinCambio()
    {
        // El precio guardado vuelve de la base como centésimos / 100 (escala 0) y el cálculo con múltiplo 0,01 lo da
        // con escala 2: el mismo importe con otra escala no es un cambio.
        var guardado = new Articulo(7L, "ABC-1", "Lente", 1210m, 50m, 181500 / 100m, activo: true, version: 3L);
        var configuracion = ResponsableInscripto(multiplo: 0.01m);
        var calculado = CalculadoraPrecioVenta.Calcular(1210m, 50m, configuracion).Precio!.PrecioVenta;
        Assert.Equal(0, guardado.PrecioVenta.Scale);
        Assert.Equal(2, calculado.Scale);
        Assert.Equal("1815.00", calculado.ToString(CultureInfo.InvariantCulture));

        var resultado = guardado.ConPrecioRecalculado(configuracion);

        Assert.Same(ResultadoRecalculo.SinCambio, resultado);
    }

    private static Articulo Guardado(bool activo = true) =>
        new(7L, "ABC-1", "Lente", 1210m, 50m, 1815m, activo, version: 3L);

    private static ConfiguracionNegocio ResponsableInscripto(decimal multiplo = 0.01m) =>
        new(21m, CondicionFiscal.ResponsableInscripto, multiplo, null);

    private static decimal Leer(string texto) => decimal.Parse(texto, CultureInfo.InvariantCulture);
}
