using Optica.Domain.Configuracion;

namespace Optica.Domain.Tests.Configuracion;

/// <summary>
/// Spec FEAT-009, Block 1 (FR-01 a FR-03, AC-02, D-10, D-19): los datos del emisor. <see cref="DatosEmisor.ParaGrabar"/>
/// valida los cinco datos, incluida la fecha de inicio no posterior a hoy, y devuelve las infracciones por campo;
/// <see cref="DatosEmisor.Leer"/> reconstruye la fila guardada sin la regla de la fecha. Datos ficticios.
/// </summary>
public class DatosEmisorTests
{
    private const string RazonSocial = "Óptica Ficticia Prueba S.R.L.";
    private const string Domicilio = "Calle Inventada 123, Localidad Imaginaria";
    private const string Cuit = "20-00000000-1";
    private const string IngresosBrutos = "901-123456-7";
    private const string Inicio = "2020-03-01";

    private static readonly DateOnly Hoy = new(2026, 11, 16);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u00A0\t\uFEFF")] // espacio duro, tabulador y marca de orden de bytes
    public void DatosEmisor_SinRazonSocial_Rechazado(string? razonSocial)
    {
        var resultado = DatosEmisor.ParaGrabar(razonSocial, Domicilio, Cuit, IngresosBrutos, Inicio, Hoy);

        // AC-02: no se construye y la infracción es del campo razón social, sola.
        Assert.False(resultado.EsValido);
        Assert.Null(resultado.Datos);
        Assert.Equal([InfraccionDatosEmisor.RazonSocialFaltante], resultado.Infracciones);

        // FR-02: los cinco son obligatorios; faltando todos, una infracción por campo, en el orden del formulario.
        var vacio = DatosEmisor.ParaGrabar(razonSocial, null, " ", "", null, Hoy);
        Assert.Equal(
            [
                InfraccionDatosEmisor.RazonSocialFaltante,
                InfraccionDatosEmisor.DomicilioComercialFaltante,
                InfraccionDatosEmisor.CuitFaltante,
                InfraccionDatosEmisor.IngresosBrutosFaltante,
                InfraccionDatosEmisor.InicioActividadesFaltante,
            ],
            vacio.Infracciones);
        Assert.Null(vacio.Datos);
    }

    [Fact]
    public void DatosEmisor_InicioFuturo_Rechazado()
    {
        var futuro = DatosEmisor.ParaGrabar(RazonSocial, Domicilio, Cuit, IngresosBrutos, "2026-11-17", Hoy);

        Assert.Null(futuro.Datos);
        Assert.Equal([InfraccionDatosEmisor.InicioActividadesPosteriorAHoy], futuro.Infracciones);

        // Una fecha que no es yyyy-MM-dd o no existe es otra infracción del mismo campo.
        foreach (var invalida in new[] { "2026-02-30", "16/11/2026", "2026-1-5", "ayer" })
        {
            Assert.Equal(
                [InfraccionDatosEmisor.InicioActividadesInvalida],
                DatosEmisor.ParaGrabar(RazonSocial, Domicilio, Cuit, IngresosBrutos, invalida, Hoy).Infracciones);
        }

        // El mismo día de hoy es válido, y la entidad queda con los cinco datos recortados y el CUIT sin guiones.
        var hoy = DatosEmisor.ParaGrabar(
            "  " + RazonSocial + " ", Domicilio + "\t", Cuit, " " + IngresosBrutos, "2026-11-16", Hoy);
        Assert.True(hoy.EsValido);
        Assert.Empty(hoy.Infracciones);
        var datos = hoy.Datos!;
        Assert.Equal(RazonSocial, datos.RazonSocial);
        Assert.Equal(Domicilio, datos.DomicilioComercial);
        Assert.Equal("20000000001", datos.Cuit.Valor);
        Assert.Equal(IngresosBrutos, datos.IngresosBrutos);
        Assert.Equal(Hoy, datos.InicioActividades);
    }

    [Theory]
    [InlineData(121, 1, 1, InfraccionDatosEmisor.RazonSocialDemasiadoLarga)]
    [InlineData(1, 201, 1, InfraccionDatosEmisor.DomicilioComercialDemasiadoLargo)]
    [InlineData(1, 1, 31, InfraccionDatosEmisor.IngresosBrutosDemasiadoLargo)]
    public void DatosEmisor_TextoLargo_Rechazado(
        int largoRazonSocial, int largoDomicilio, int largoIngresosBrutos, InfraccionDatosEmisor esperada)
    {
        var largo = DatosEmisor.ParaGrabar(
            new string('a', largoRazonSocial), new string('b', largoDomicilio), Cuit, new string('1', largoIngresosBrutos),
            Inicio, Hoy);

        Assert.Null(largo.Datos);
        Assert.Equal([esperada], largo.Infracciones);

        // D-10: el máximo justo es válido (120, 200 y 30), medido sobre el texto recortado.
        var maximo = DatosEmisor.ParaGrabar(
            new string('a', 120) + "  ", new string('b', 200), Cuit, " " + new string('1', 30), Inicio, Hoy);
        Assert.True(maximo.EsValido);

        // Un carácter de control o un sustituto suelto es una infracción de caracteres del campo, no de largo.
        Assert.Equal(
            [
                InfraccionDatosEmisor.RazonSocialConCaracteresInvalidos,
                InfraccionDatosEmisor.DomicilioComercialConCaracteresInvalidos,
                InfraccionDatosEmisor.CuitInvalido,
                InfraccionDatosEmisor.IngresosBrutosConCaracteresInvalidos,
            ],
            DatosEmisor.ParaGrabar("Óptica\u0000", "Calle\nInventada", "20-00000000-2", "901\uD800", Inicio, Hoy)
                .Infracciones);
    }

    [Fact]
    public void DatosEmisor_LeerConInicioFuturo_NoEsCorrupta()
    {
        // D-19: un reloj atrasado (o una fila grabada con otro reloj) no vuelve corrupta una fila válida.
        var futuro = new DateOnly(2099, 1, 1);
        var leidos = DatosEmisor.Leer(RazonSocial, Domicilio, "20000000001", IngresosBrutos, futuro);

        Assert.Equal(RazonSocial, leidos.RazonSocial);
        Assert.Equal(Domicilio, leidos.DomicilioComercial);
        Assert.Equal("20000000001", leidos.Cuit.Valor);
        Assert.Equal(IngresosBrutos, leidos.IngresosBrutos);
        Assert.Equal(futuro, leidos.InicioActividades);

        // Sin ToString con valores (R-06): ni la razón social ni el CUIT.
        Assert.DoesNotContain("Ficticia", leidos.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("20000000001", leidos.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("20000000001", leidos.Cuit.ToString(), StringComparison.Ordinal);

        // El resto de las reglas sí rige al leer: un CUIT con guiones o un dato vacío no se reconstruye, y el error
        // nombra el campo sin el valor.
        var conGuiones = Assert.Throws<ArgumentException>(
            () => DatosEmisor.Leer(RazonSocial, Domicilio, Cuit, IngresosBrutos, futuro));
        Assert.Equal("cuit", conGuiones.ParamName);
        Assert.DoesNotContain("00000000", conGuiones.Message, StringComparison.Ordinal);
        var sinRazonSocial = Assert.Throws<ArgumentException>(
            () => DatosEmisor.Leer(" ", Domicilio, "20000000001", IngresosBrutos, futuro));
        Assert.Equal("razonSocial", sinRazonSocial.ParamName);
    }
}
