using System.Text;
using Optica.Application.Marca;
using Optica.Domain.Marca;

namespace Optica.Application.Tests.Marca;

public class ObtenerMarcaTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0];

    private static readonly byte[] Svg =
        Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\"/></svg>");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ejecutar_DevuelveColoresYSiEsEjemplo(bool esEjemplo)
    {
        var proveedor = new ProveedorFijo(IdentidadVisual.Crear("#0903A0", "#FFFFFF", LogoMarca.Crear("logo.png", Png), esEjemplo));

        var marca = new ObtenerMarca(proveedor).Ejecutar();

        Assert.Equal(new MarcaVisible("#0903A0", "#FFFFFF", esEjemplo), marca);
    }

    [Theory]
    [InlineData("logo.png", "image/png", false)]
    [InlineData("logo.jpg", "image/jpeg", false)]
    [InlineData("logo.svg", "image/svg+xml", true)]
    public void ObtenerLogo_DevuelveContenidoYTipo(string nombre, string tipo, bool esSvg)
    {
        var contenido = nombre.EndsWith(".png", StringComparison.Ordinal) ? Png
            : nombre.EndsWith(".jpg", StringComparison.Ordinal) ? Jpeg
            : Svg;
        var proveedor = new ProveedorFijo(IdentidadVisual.Crear("#0903A0", "#FFFFFF", LogoMarca.Crear(nombre, contenido), false));

        var logo = new ObtenerMarca(proveedor).ObtenerLogo();

        Assert.Equal(contenido, logo.Contenido);
        Assert.Equal(tipo, logo.TipoContenido);
        Assert.Equal(esSvg, logo.EsSvg);
    }

    [Fact]
    public void ObtenerLogo_DevuelveUnaCopia()
    {
        var proveedor = new ProveedorFijo(IdentidadVisual.Crear("#0903A0", "#FFFFFF", LogoMarca.Crear("logo.png", Png), false));
        var casoDeUso = new ObtenerMarca(proveedor);

        casoDeUso.ObtenerLogo().Contenido[0] = 0;

        // Modificar lo devuelto no cambia el logo que guarda el proveedor.
        Assert.Equal(Png, casoDeUso.ObtenerLogo().Contenido);
    }

    [Fact]
    public void Ejecutar_ProveedorQueFalla_PropagaElError()
    {
        var casoDeUso = new ObtenerMarca(new ProveedorQueFalla());

        Assert.Throws<InvalidOperationException>(() => casoDeUso.Ejecutar());
        Assert.Throws<InvalidOperationException>(() => casoDeUso.ObtenerLogo());
    }

    private sealed class ProveedorFijo(IdentidadVisual marca) : IProveedorMarca
    {
        public IdentidadVisual Obtener() => marca;
    }

    private sealed class ProveedorQueFalla : IProveedorMarca
    {
        public IdentidadVisual Obtener() => throw new InvalidOperationException("sin marca");
    }
}
