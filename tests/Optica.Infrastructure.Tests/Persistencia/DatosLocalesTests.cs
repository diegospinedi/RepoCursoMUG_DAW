using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Comun;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

public class DatosLocalesTests
{
    private const string RutaPrivada = "/home/usuario-privado/AppData/optica.db";

    [Fact]
    public void DescribirFalla_ErrorDeLaBaseEnLaCadena_DescribeSinElTextoOriginal()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            var datos = new DatosLocales(new FabricaConexion(directorio));
            var error = new InvalidOperationException(
                "envoltorio", new SqliteException($"SQLite Error 14: 'unable to open database file' ({RutaPrivada}).", 14));

            var falla = datos.DescribirFalla(error);

            Assert.NotNull(falla);
            Assert.Equal(FabricaConexion.NombreArchivo, falla.Archivo);
            Assert.Equal(RutasPrivadas.Ocultar(directorio), falla.Directorio);
            Assert.Equal(nameof(SqliteException), falla.TipoError);
            Assert.Equal(14, falla.CodigoError);
            Assert.Equal(FabricaConexion.ClaveDirectorio, falla.Clave);
            Assert.DoesNotContain("usuario-privado", falla.ToString());
            Assert.Equal(directorio, datos.Directorio);
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public void DescribirFalla_ErrorDeBaseQueNoEsDeSqlite_SinCodigo()
    {
        var datos = new DatosLocales(new FabricaConexion("/opt/optica-datos"));

        var falla = datos.DescribirFalla(new OtraFallaDeBase());

        Assert.NotNull(falla);
        Assert.Equal(nameof(OtraFallaDeBase), falla.TipoError);
        Assert.Null(falla.CodigoError);
    }

    [Fact]
    public void DescribirFalla_ErrorQueNoEsDeLaBase_Null()
    {
        var datos = new DatosLocales(new FabricaConexion("/opt/optica-datos"));

        Assert.Null(datos.DescribirFalla(new InvalidOperationException("otra cosa", new IOException("disco"))));
    }

    [Fact]
    public void AgregarInfraestructura_RegistraLosDatosLocales()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            using var servicios = BaseTemporal.CrearServicios(directorio);

            var datos = servicios.GetRequiredService<IDatosLocales>();

            Assert.Equal(directorio, datos.Directorio);
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    /// <summary>Una falla de otra base, que no informa un código de SQLite.</summary>
    private sealed class OtraFallaDeBase : System.Data.Common.DbException;
}
