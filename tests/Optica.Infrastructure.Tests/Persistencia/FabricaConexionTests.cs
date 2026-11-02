using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

public class FabricaConexionTests
{
    /// <summary>Eventos de error de EF Core que llevan la ruta de la base o la excepción de SQLite tal cual.</summary>
    private static readonly Dictionary<string, EventId> EventosDeError = new()
    {
        [nameof(RelationalEventId.ConnectionError)] = RelationalEventId.ConnectionError,
        [nameof(RelationalEventId.CommandError)] = RelationalEventId.CommandError,
        [nameof(RelationalEventId.TransactionError)] = RelationalEventId.TransactionError,
        [nameof(CoreEventId.SaveChangesFailed)] = CoreEventId.SaveChangesFailed,
        [nameof(CoreEventId.QueryIterationFailed)] = CoreEventId.QueryIterationFailed,
    };

    [Theory]
    [InlineData(nameof(RelationalEventId.ConnectionError))]
    [InlineData(nameof(RelationalEventId.CommandError))]
    [InlineData(nameof(RelationalEventId.TransactionError))]
    [InlineData(nameof(CoreEventId.SaveChangesFailed))]
    [InlineData(nameof(CoreEventId.QueryIterationFailed))]
    public void Configurar_EventoDeErrorDeEf_SeRegistraEnDebug(string evento)
    {
        var opciones = new DbContextOptionsBuilder<OpticaDbContext>();

        new FabricaConexion("/opt/optica-datos").Configurar(opciones);

        var avisos = opciones.Options.FindExtension<CoreOptionsExtension>()!.WarningsConfiguration;
        Assert.Equal(LogLevel.Debug, avisos.GetLevel(EventosDeError[evento]));
    }

    [Fact]
    public void RutaVisible_DirectorioConfiguradoEnLaCarpetaDelUsuario_SinElNombreDeUsuario()
    {
        var perfil = CarpetaDelUsuario();
        var configurado = Path.Combine(perfil, "OpticaDatos");

        var fabrica = new FabricaConexion(configurado);

        Assert.Equal(configurado, fabrica.Directorio);
        Assert.DoesNotContain(perfil, fabrica.RutaVisible, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RutasPrivadas.Ocultar(configurado), fabrica.RutaVisible);
        Assert.EndsWith("OpticaDatos", fabrica.RutaVisible, StringComparison.Ordinal);
    }

    [Fact]
    public void RutaVisible_DirectorioConfiguradoFueraDeLaCarpetaDelUsuario_TalCual()
    {
        var configurado = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "optica-datos-de-prueba");

        Assert.Equal(configurado, new FabricaConexion(configurado).RutaVisible);
    }

    [Fact]
    public void PrepararDirectorio_ConfiguradoEnLaCarpetaDelUsuarioQueNoSePuedeCrear_ErrorSinElNombreDeUsuario()
    {
        // Un nombre de más de 255 caracteres no se puede crear en ningún sistema: falla sin escribir nada.
        var perfil = CarpetaDelUsuario();
        var fabrica = new FabricaConexion(Path.Combine(perfil, new string('x', 300), "datos"));

        var error = Assert.Throws<DirectorioDatosInvalidoException>(fabrica.PrepararDirectorio);

        // Ni el mensaje ni una causa adjunta (cuyo texto trae la ruta expandida) llevan la carpeta del usuario.
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(perfil, error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fabrica.RutaVisible, error.Ruta);
    }

    private static string CarpetaDelUsuario()
    {
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrEmpty(perfil), "La prueba necesita la carpeta del usuario.");
        return perfil;
    }
}
