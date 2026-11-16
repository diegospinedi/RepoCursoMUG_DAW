using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Optica.Domain.Backup;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

/// <summary>
/// Spec FEAT-010, Block 1 (FR-01, AC-01, D-12): la ubicación del backup en la tabla <c>ConfiguracionBackup</c>, fila
/// única con <c>Id = 1</c>. Rutas ficticias sobre la carpeta temporal.
/// </summary>
public class UbicacionBackupRepositoryTests
{
    private const string SelectFila = "SELECT Id, Ruta FROM ConfiguracionBackup";

    private static readonly string RutaPendrive = Path.Combine(Path.GetTempPath(), "Pendrive", "Backups");
    private static readonly string RutaDiscoExterno = Path.Combine(Path.GetTempPath(), "DiscoExterno", "Copias");

    [Fact]
    public async Task Repositorio_GrabaYLeeLaUbicacion()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);

        // Una base recién creada no tiene ubicación: la migración no inserta una fila.
        Assert.Null(await new UbicacionBackupRepository(baseTemporal.CrearContexto()).ObtenerAsync());

        // La primera grabación inserta la fila, y solo al confirmar la unidad de trabajo.
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new UbicacionBackupRepository(contexto);
        repositorio.Reemplazar(Ubicacion(RutaPendrive));
        Assert.Empty(FacturasPrueba.Filas(cadena, SelectFila));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
        Assert.Equal(["1", RutaPendrive], Assert.Single(FacturasPrueba.Filas(cadena, SelectFila)));

        // AC-01: al volver a abrir la configuración (otro alcance) se lee la misma ubicación.
        var leida = await new UbicacionBackupRepository(baseTemporal.CrearContexto()).ObtenerAsync();
        Assert.NotNull(leida);
        Assert.Equal(RutaPendrive, leida.Ruta);

        // Modificarla reemplaza la misma fila, también desde un contexto que ya la había leído.
        var otroContexto = baseTemporal.CrearContexto();
        var otroRepositorio = new UbicacionBackupRepository(otroContexto);
        Assert.NotNull(await otroRepositorio.ObtenerAsync());
        otroRepositorio.Reemplazar(Ubicacion(RutaDiscoExterno));
        await new UnidadDeTrabajoEf(otroContexto).ConfirmarAsync();
        Assert.Equal(["1", RutaDiscoExterno], Assert.Single(FacturasPrueba.Filas(cadena, SelectFila)));

        // La última grabación gana aunque la entrada reutilizada conserve el valor original viejo.
        repositorio.Reemplazar(Ubicacion(RutaPendrive));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
        Assert.Equal(["1", RutaPendrive], Assert.Single(FacturasPrueba.Filas(cadena, SelectFila)));

        // Registrado en la raíz de composición, por alcance como el resto de los repositorios.
        using var alcance = baseTemporal.Servicios.CreateScope();
        Assert.IsType<UbicacionBackupRepository>(
            alcance.ServiceProvider.GetRequiredService<IUbicacionBackupRepository>());

        // Los CHECK de la tabla son la segunda barrera: ni otra fila ni una ruta vacía o de más de 260 caracteres.
        foreach (var (sentencia, restriccion) in new[]
                 {
                     ("INSERT INTO ConfiguracionBackup (Id, Ruta) VALUES (2, '/otra')", "CK_ConfiguracionBackup_Id"),
                     ("UPDATE ConfiguracionBackup SET Ruta = ''", "CK_ConfiguracionBackup_Ruta"),
                     ($"UPDATE ConfiguracionBackup SET Ruta = '/{new string('a', 260)}'", "CK_ConfiguracionBackup_Ruta"),
                 })
        {
            var check = Assert.Throws<SqliteException>(() => FacturasPrueba.Ejecutar(cadena, sentencia));
            Assert.Contains($"CHECK constraint failed: {restriccion}", check.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Backups/Relativa")]
    [InlineData("   ")]
    public async Task Repositorio_FilaCorrupta_ErrorSinRuta(string ruta)
    {
        using var baseTemporal = new BaseTemporal();

        // Una fila editada a mano: una ruta relativa pasa el CHECK; una en blanco, solo con los CHECK desactivados. La
        // conexión es propia y sin grupo, así el pragma no queda activo en una conexión que reutilice el contexto.
        var sinGrupo = new SqliteConnectionStringBuilder(FacturasPrueba.CadenaDe(baseTemporal)) { Pooling = false }
            .ToString();
        using (var conexion = new SqliteConnection(sinGrupo))
        {
            conexion.Open();
            using (var pragma = conexion.CreateCommand())
            {
                pragma.CommandText = "PRAGMA ignore_check_constraints = ON;";
                pragma.ExecuteNonQuery();
            }

            FacturasPrueba.Insertar(conexion, "ConfiguracionBackup", new(StringComparer.Ordinal)
            {
                ["Id"] = 1L,
                ["Ruta"] = ruta,
            });
        }

        var repositorio = new UbicacionBackupRepository(baseTemporal.CrearContexto());

        // Estado corrupto, con texto fijo y sin la ruta (D-12), ni en el mensaje ni en las causas.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repositorio.ObtenerAsync());
        Assert.Equal("La ubicación del backup guardada no es válida.", error.Message);
        for (Exception? causa = error; causa is not null; causa = causa.InnerException)
        {
            Assert.DoesNotContain("Relativa", causa.Message, StringComparison.Ordinal);
        }
    }

    private static UbicacionBackup Ubicacion(string ruta) =>
        UbicacionBackup.ParaGrabar(ruta).Ubicacion ?? throw new InvalidOperationException("Ruta de prueba inválida.");
}
