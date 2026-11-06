using Optica.Domain.Presupuestos;

namespace Optica.Domain.Tests.Presupuestos;

// Datos ficticios (D-17): ninguno corresponde a una persona real.
public class PoliticaClienteTests
{
    private const string Apellido = "Pérez";
    private const string Nombre = "Ana";
    private const string Dni = "23456789";
    private const string Domicilio = "Calle Falsa 123";
    private const string Email = "ana@mail.com";
    private const string Telefono = "11 5555-0000";

    // Invisibles: van como escape para que se vean en el código.
    private const string MarcaDeOrdenDeBytes = "\U0000FEFF";
    private const string EspacioDuro = "\U000000A0";

    // UTF-16 inválido (D-25): sustituto alto suelto, bajo suelto entre letras y alto al final. Van en un [Fact] y no
    // en [InlineData] porque el runner serializa los argumentos.
    private static readonly string[] TextosConUtf16Invalido = ["\uD800", "a\uDC00b", "ab\uD83D"];

    [Fact]
    public void Evaluar_SinApellidoOSinNombre_InfraccionFaltante()
    {
        foreach (var faltante in new[] { null, "", "   " })
        {
            Assert.Equal(
                [InfraccionCliente.ApellidoFaltante],
                PoliticaCliente.Evaluar(faltante, Nombre, Dni, Domicilio, Email, Telefono));
            Assert.Equal(
                [InfraccionCliente.NombreFaltante],
                PoliticaCliente.Evaluar(Apellido, faltante, Dni, Domicilio, Email, Telefono));
        }

        Assert.Equal(
            [InfraccionCliente.ApellidoFaltante, InfraccionCliente.NombreFaltante],
            PoliticaCliente.Evaluar(null, null, Dni, null, null, null));

        var error = Assert.Throws<ArgumentException>(
            () => new ClientePresupuesto("", Nombre, Dni, Domicilio, Email, Telefono));
        Assert.Equal("apellido", error.ParamName);
        var errorNombre = Assert.Throws<ArgumentException>(
            () => new ClientePresupuesto(Apellido, " ", Dni, Domicilio, Email, Telefono));
        Assert.Equal("nombre", errorNombre.ParamName);
    }

    [Fact]
    public void Evaluar_SinDni_DniFaltante()
    {
        foreach (var faltante in new[] { null, "", "   " })
        {
            Assert.Equal(
                [InfraccionCliente.DniFaltante],
                PoliticaCliente.Evaluar(Apellido, Nombre, faltante, Domicilio, Email, Telefono));
        }

        var error = Assert.Throws<ArgumentException>(
            () => new ClientePresupuesto(Apellido, Nombre, "", Domicilio, Email, Telefono));
        Assert.Equal("dni", error.ParamName);
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("123456789")]
    [InlineData("23A56789")]
    [InlineData("23 456 789")]
    [InlineData("23-456-789")]
    [InlineData("...")]
    [InlineData("\U00000661\U00000662\U00000663\U00000664\U00000665\U00000666\U00000667")]
    public void Evaluar_Dni123456_123456789_23A56789_DniInvalido(string dni)
    {
        Assert.Equal(
            [InfraccionCliente.DniInvalido],
            PoliticaCliente.Evaluar(Apellido, Nombre, dni, Domicilio, Email, Telefono));
        Assert.Null(PoliticaCliente.NormalizarDni(dni));

        var error = Assert.Throws<ArgumentException>(
            () => new ClientePresupuesto(Apellido, Nombre, dni, Domicilio, Email, Telefono));
        Assert.Equal("dni", error.ParamName);
        Assert.DoesNotContain(dni, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ana@")]
    [InlineData("ana@mail")]
    [InlineData("ana@.com")]
    [InlineData("ana@mail.")]
    [InlineData("@mail.com")]
    [InlineData("ana@@mail.com")]
    [InlineData("ana@ma@il.com")]
    [InlineData("an a@mail.com")]
    [InlineData("ana@ma il.com")]
    [InlineData("ana@mail..com")]
    [InlineData("anamail.com")]
    public void Evaluar_EmailAnaArrobaYAnaArrobaMail_EmailInvalido(string email)
    {
        Assert.Equal(
            [InfraccionCliente.EmailInvalido],
            PoliticaCliente.Evaluar(Apellido, Nombre, Dni, Domicilio, email, Telefono));

        var error = Assert.Throws<ArgumentException>(
            () => new ClientePresupuesto(Apellido, Nombre, Dni, Domicilio, email, Telefono));
        Assert.Equal("email", error.ParamName);
        Assert.DoesNotContain(email, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ana@mail.c")]
    [InlineData("ana@mail.c1")]
    [InlineData("ana@mail.12")]
    public void Evaluar_EmailExtensionDeUnaLetra_EmailInvalido(string email)
    {
        Assert.Equal(
            [InfraccionCliente.EmailInvalido],
            PoliticaCliente.Evaluar(Apellido, Nombre, Dni, Domicilio, email, Telefono));
    }

    [Fact]
    public void Evaluar_EmailAnaArrobaMailComAr_Valido()
    {
        Assert.Empty(PoliticaCliente.Evaluar(Apellido, Nombre, Dni, Domicilio, "ana@mail.com.ar", Telefono));

        var cliente = new ClientePresupuesto(Apellido, Nombre, Dni, Domicilio, "ana@mail.com.ar", Telefono);
        Assert.Equal("ana@mail.com.ar", cliente.Email);
    }

    [Fact]
    public void Evaluar_DomicilioYTelefonoLargos_Rechazados()
    {
        Assert.Empty(PoliticaCliente.Evaluar(
            new string('a', 100), new string('b', 100), Dni, new string('c', 200), Email, new string('1', 30)));

        Assert.Equal(
            [InfraccionCliente.DomicilioDemasiadoLargo],
            PoliticaCliente.Evaluar(Apellido, Nombre, Dni, new string('c', 201), Email, Telefono));
        Assert.Equal(
            [InfraccionCliente.TelefonoDemasiadoLargo],
            PoliticaCliente.Evaluar(Apellido, Nombre, Dni, Domicilio, Email, new string('1', 31)));
        Assert.Equal(
            [InfraccionCliente.ApellidoDemasiadoLargo, InfraccionCliente.NombreDemasiadoLargo],
            PoliticaCliente.Evaluar(new string('a', 101), new string('b', 101), Dni, Domicilio, Email, Telefono));

        // 254 caracteres es el máximo del email (D-08); uno más ya es demasiado largo, antes que el formato.
        var emailMaximo = new string('a', 245) + "@mail.com";
        Assert.Equal(254, emailMaximo.Length);
        Assert.Empty(PoliticaCliente.Evaluar(Apellido, Nombre, Dni, Domicilio, emailMaximo, Telefono));
        Assert.Equal(
            [InfraccionCliente.EmailDemasiadoLargo],
            PoliticaCliente.Evaluar(Apellido, Nombre, Dni, Domicilio, "a" + emailMaximo, Telefono));

        var error = Assert.Throws<ArgumentException>(
            () => new ClientePresupuesto(Apellido, Nombre, Dni, Domicilio, Email, new string('1', 31)));
        Assert.Equal("telefono", error.ParamName);
        var errorDomicilio = Assert.Throws<ArgumentException>(
            () => new ClientePresupuesto(Apellido, Nombre, Dni, new string('c', 201), Email, Telefono));
        Assert.Equal("domicilio", errorDomicilio.ParamName);
    }

    [Fact]
    public void Evaluar_Utf16Invalido_Rechazado()
    {
        foreach (var texto in TextosConUtf16Invalido)
        {
            Assert.Equal(
                [InfraccionCliente.ApellidoConCaracteresInvalidos],
                PoliticaCliente.Evaluar(texto, Nombre, Dni, Domicilio, Email, Telefono));
            Assert.Equal(
                [InfraccionCliente.NombreConCaracteresInvalidos],
                PoliticaCliente.Evaluar(Apellido, texto, Dni, Domicilio, Email, Telefono));
            Assert.Equal(
                [InfraccionCliente.DniInvalido],
                PoliticaCliente.Evaluar(Apellido, Nombre, texto, Domicilio, Email, Telefono));
            Assert.Equal(
                [InfraccionCliente.DomicilioConCaracteresInvalidos],
                PoliticaCliente.Evaluar(Apellido, Nombre, Dni, texto, Email, Telefono));
            Assert.Equal(
                [InfraccionCliente.EmailConCaracteresInvalidos],
                PoliticaCliente.Evaluar(Apellido, Nombre, Dni, Domicilio, texto, Telefono));
            Assert.Equal(
                [InfraccionCliente.TelefonoConCaracteresInvalidos],
                PoliticaCliente.Evaluar(Apellido, Nombre, Dni, Domicilio, Email, texto));
        }

        // Inválido y largo a la vez: gana el UTF-16 inválido, como en PoliticaArticulo.
        Assert.Equal(
            [InfraccionCliente.DomicilioConCaracteresInvalidos],
            PoliticaCliente.Evaluar(Apellido, Nombre, Dni, new string('c', 250) + "\uD800", Email, Telefono));
    }

    [Fact]
    public void Evaluar_ApellidoConEspaciosAlrededor_SeRecorta()
    {
        var cliente = new ClientePresupuesto(
            "  Pérez  ", EspacioDuro + "Ana\t", " 23456789 ", "  Calle Falsa 123 ", " ana@mail.com ",
            MarcaDeOrdenDeBytes + "11 5555-0000 ");

        Assert.Equal("Pérez", cliente.Apellido);
        Assert.Equal("Ana", cliente.Nombre);
        Assert.Equal("23456789", cliente.Dni);
        Assert.Equal("Calle Falsa 123", cliente.Domicilio);
        Assert.Equal("ana@mail.com", cliente.Email);
        Assert.Equal("11 5555-0000", cliente.Telefono);

        Assert.Equal(
            [InfraccionCliente.ApellidoFaltante],
            PoliticaCliente.Evaluar("   ", Nombre, Dni, Domicilio, Email, Telefono));

        // El largo se mide después del recorte (D-08).
        Assert.Empty(PoliticaCliente.Evaluar(
            "  " + new string('a', 100) + "  ", Nombre, Dni, Domicilio, Email, Telefono));
    }

    [Fact]
    public void Evaluar_SeisDatosValidos_SinInfracciones()
    {
        Assert.Empty(PoliticaCliente.Evaluar(Apellido, Nombre, Dni, Domicilio, Email, Telefono));

        var cliente = new ClientePresupuesto(Apellido, Nombre, Dni, Domicilio, Email, Telefono);

        Assert.Equal(Apellido, cliente.Apellido);
        Assert.Equal(Nombre, cliente.Nombre);
        Assert.Equal(Dni, cliente.Dni);
        Assert.Equal(Domicilio, cliente.Domicilio);
        Assert.Equal(Email, cliente.Email);
        Assert.Equal(Telefono, cliente.Telefono);
    }

    [Fact]
    public void Evaluar_SinDomicilioEmailNiTelefono_SinInfracciones()
    {
        Assert.Empty(PoliticaCliente.Evaluar(Apellido, Nombre, Dni, null, null, null));
        Assert.Empty(PoliticaCliente.Evaluar(Apellido, Nombre, Dni, "", " ", EspacioDuro + "\t"));

        var cliente = new ClientePresupuesto(Apellido, Nombre, Dni, "  ", "", MarcaDeOrdenDeBytes);

        Assert.Null(cliente.Domicilio);
        Assert.Null(cliente.Email);
        Assert.Null(cliente.Telefono);
    }

    [Fact]
    public void NormalizarDni_23Punto456Punto789_23456789()
    {
        Assert.Equal("23456789", PoliticaCliente.NormalizarDni("23.456.789"));
        Assert.Equal("1234567", PoliticaCliente.NormalizarDni(" 1.234.567 "));
        Assert.Equal("01234567", PoliticaCliente.NormalizarDni("01.234.567"));
        Assert.Null(PoliticaCliente.NormalizarDni(null));
        Assert.Null(PoliticaCliente.NormalizarDni("   "));

        Assert.Empty(PoliticaCliente.Evaluar(Apellido, Nombre, "23.456.789", Domicilio, Email, Telefono));
        Assert.Equal("23456789", new ClientePresupuesto(Apellido, Nombre, "23.456.789", null, null, null).Dni);
    }

    [Fact]
    public void Evaluar_EmailAnaArrobaMailCom_Valido()
    {
        Assert.Empty(PoliticaCliente.Evaluar(Apellido, Nombre, Dni, Domicilio, "ana@mail.com", Telefono));

        // Se guarda como se escribió, sin pasarlo a minúsculas (D-10).
        var cliente = new ClientePresupuesto(Apellido, Nombre, Dni, Domicilio, "Ana@Mail.com", Telefono);
        Assert.Equal("Ana@Mail.com", cliente.Email);
    }
}
