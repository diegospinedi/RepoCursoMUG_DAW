using Optica.Application.Backup;

namespace Optica.Application.Tests.Backup;

/// <summary>
/// Spec FEAT-010, Block 7 (FR-13, D-27, D-28, D-29): el caso de uso de la restauración con un restaurador falso, sin disco
/// ni base. Las pruebas con SQLite real y archivos están en Optica.Infrastructure.Tests (<c>RestauradorSqliteTests</c>), y
/// las del comando de consola, en Optica.Api.Tests (<c>ComandoRestaurarBackupTests</c>).
/// </summary>
public class RestaurarBackupTests
{
    private static readonly string[] Conocidas =
        ["20261030220627_Inicial", "20261106221119_Presupuestos", "20261113170512_Facturas", "20261116200000_Backup"];

    private static readonly string Carpeta = Path.Combine(Path.GetTempPath(), "optica-pruebas-restauracion", "Pendrive");

    private static readonly string RutaCopia = Path.Combine(Carpeta, "optica-2026-10-30.db");

    [Fact]
    public async Task Restaurar_CopiaDeVersionMasNueva_Rechazada()
    {
        // Una copia hecha por una versión más nueva: tiene todas las migraciones conocidas y una más que esta versión no
        // conoce (D-28). Restaurarla dejaría una base que esta Api no sabe usar.
        var restaurador = new RestauradorFalso
        {
            Inspeccion = new InspeccionCopia(true, true, [.. Conocidas, "20270115090000_Futura"], 4),
        };
        var caso = new RestaurarBackup(restaurador, restaurador.Datos);

        var verificacion = await caso.VerificarAsync(RutaCopia);
        var resultado = await caso.RestaurarAsync(RutaCopia);

        Assert.Equal(RechazoCopia.VersionMasNueva, verificacion.Rechazo);
        Assert.False(verificacion.Valida);
        Assert.Equal(FallaRestauracion.CopiaRechazada, resultado.Falla);
        Assert.Equal(RechazoCopia.VersionMasNueva, resultado.Copia.Rechazo);
        Assert.False(resultado.Hecha);
        // No se tocó nada: ni el temporal, ni el apartado de la base actual.
        Assert.DoesNotContain("temporal", restaurador.Bitacora);
        Assert.DoesNotContain("intercambiar", restaurador.Bitacora);
        Assert.DoesNotContain("descartar", restaurador.Bitacora);
    }

    [Fact]
    public async Task Verificar_CopiaValida_InformaFechaYFacturasPosteriores()
    {
        var restaurador = new RestauradorFalso
        {
            Inspeccion = new InspeccionCopia(true, true, Conocidas, 12),
            FacturasPosteriores = 3,
        };

        var verificacion = await new RestaurarBackup(restaurador, restaurador.Datos).VerificarAsync(RutaCopia);

        Assert.True(verificacion.Valida);
        Assert.Null(verificacion.Rechazo);
        Assert.Equal("optica-2026-10-30.db", verificacion.NombreArchivo);
        Assert.Equal(new DateOnly(2026, 10, 30), verificacion.Fecha);
        Assert.False(verificacion.RequiereActualizar);
        Assert.Equal(3, verificacion.FacturasPosteriores);
        // Las facturas se cuentan desde la última de la copia.
        Assert.Equal([12L], restaurador.ContadasDesde);
        Assert.Equal(["inspeccionar", "contar"], restaurador.Bitacora);
        Assert.Equal([RutaCopia], restaurador.Inspeccionadas);
    }

    [Fact]
    public async Task Verificar_CopiaVieja_AceptadaYAvisaQueSeActualiza()
    {
        // Una copia de una versión anterior: tiene un prefijo de las migraciones conocidas. La Api la migra al arrancar.
        var restaurador = new RestauradorFalso { Inspeccion = new InspeccionCopia(true, true, Conocidas[..2], 0) };

        var verificacion = await new RestaurarBackup(restaurador, restaurador.Datos).VerificarAsync(RutaCopia);

        Assert.True(verificacion.Valida);
        Assert.True(verificacion.RequiereActualizar);
    }

    [Theory]
    [InlineData("backup.db")]
    [InlineData("optica-2026-10-30.db.parcial")]
    [InlineData("optica-2026-02-30.db")]
    [InlineData("OPTICA-2026-10-30.DB")]
    [InlineData("optica.db")]
    [InlineData("")]
    public async Task Verificar_NombreFueraDelPatron_RechazadaSinAbrirla(string nombre)
    {
        var restaurador = new RestauradorFalso { Inspeccion = new InspeccionCopia(true, true, Conocidas, 0) };

        var verificacion = await new RestaurarBackup(restaurador, restaurador.Datos).VerificarAsync(Path.Combine(Carpeta, nombre));

        Assert.Equal(RechazoCopia.NombreInvalido, verificacion.Rechazo);
        // El nombre de un archivo cualquiera puede llevar el nombre de una persona: no se devuelve (D-12).
        Assert.Null(verificacion.NombreArchivo);
        Assert.Empty(restaurador.Bitacora);
    }

    /// <summary>
    /// H-02 del SAST: rutas UNC y de dispositivo con un nombre de copia válido. Abrir una UNC inicia una autenticación
    /// SMB que puede entregar el hash NTLM de la cuenta.
    /// </summary>
    public static TheoryData<string> RutasDeRed => new()
    {
        @"\\servidor-testigo\recurso\optica-2026-10-30.db",
        @"\\?\UNC\servidor-testigo\recurso\optica-2026-10-30.db",
        @"\\.\C:\Pendrive\optica-2026-10-30.db",
        "//servidor-testigo/recurso/optica-2026-10-30.db",
    };

    [Theory]
    [MemberData(nameof(RutasDeRed))]
    public async Task Verificar_RutaUncODeDispositivo_RechazadaPorLaFormaSinAbrirla(string ruta)
    {
        var restaurador = new RestauradorFalso { Inspeccion = new InspeccionCopia(true, true, Conocidas, 0) };

        var verificacion = await new RestaurarBackup(restaurador, restaurador.Datos).VerificarAsync(ruta);

        Assert.Equal(RechazoCopia.RutaDeRed, verificacion.Rechazo);
        // Por la forma del texto: el restaurador (el único que toca el disco) no se llamó, tampoco en Linux.
        Assert.Empty(restaurador.Bitacora);
        Assert.Null(verificacion.NombreArchivo);
        Assert.Null(verificacion.Fecha);
    }

    [Theory]
    [MemberData(nameof(RutasDeRed))]
    public async Task Restaurar_RutaUncODeDispositivo_RechazadaSinTocarNada(string ruta)
    {
        var restaurador = new RestauradorFalso { Inspeccion = new InspeccionCopia(true, true, Conocidas, 0) };

        var resultado = await new RestaurarBackup(restaurador, restaurador.Datos).RestaurarAsync(ruta);

        Assert.Equal(FallaRestauracion.CopiaRechazada, resultado.Falla);
        Assert.Equal(RechazoCopia.RutaDeRed, resultado.Copia.Rechazo);
        Assert.Empty(restaurador.Bitacora);
    }

    [Theory]
    [MemberData(nameof(RutasDeRed))]
    public void FormaRuta_EsUncODispositivo_PorLaFormaDelTexto(string ruta)
    {
        Assert.True(FormaRuta.EsUncODispositivo(ruta));
    }

    [Theory]
    [InlineData(@"C:\Pendrive\optica-2026-10-30.db")]
    [InlineData(@"E:\optica-2026-10-30.db")]
    [InlineData("/media/pendrive/optica-2026-10-30.db")]
    [InlineData(@"\Pendrive\optica-2026-10-30.db")]
    [InlineData("optica-2026-10-30.db")]
    [InlineData("")]
    public void FormaRuta_Local_NoEsUncNiDispositivo(string ruta)
    {
        Assert.False(FormaRuta.EsUncODispositivo(ruta));
    }

    [Fact]
    public async Task Verificar_CopiaInexistente_Rechazada()
    {
        var restaurador = new RestauradorFalso { Inspeccion = new InspeccionCopia(false, false, null, 0) };

        var verificacion = await new RestaurarBackup(restaurador, restaurador.Datos).VerificarAsync(RutaCopia);

        Assert.Equal(RechazoCopia.Inexistente, verificacion.Rechazo);
        Assert.Equal("optica-2026-10-30.db", verificacion.NombreArchivo);
        Assert.DoesNotContain("contar", restaurador.Bitacora);
    }

    [Fact]
    public async Task Verificar_CopiaDanada_Rechazada()
    {
        var restaurador = new RestauradorFalso { Inspeccion = new InspeccionCopia(true, false, null, 0) };

        var verificacion = await new RestaurarBackup(restaurador, restaurador.Datos).VerificarAsync(RutaCopia);

        Assert.Equal(RechazoCopia.Danada, verificacion.Rechazo);
        Assert.DoesNotContain("contar", restaurador.Bitacora);
    }

    public static TheoryData<string[]?> HistorialesAjenos => new()
    {
        // Sin la tabla de migraciones: no es una base de la óptica.
        null,
        // Con la tabla vacía.
        Array.Empty<string>(),
        // Con un hueco: no es un prefijo de las conocidas.
        new[] { Conocidas[0], Conocidas[2] },
        // Sin la primera.
        new[] { Conocidas[1] },
    };

    [Theory]
    [MemberData(nameof(HistorialesAjenos))]
    public async Task Verificar_HistorialAjeno_Rechazada(string[]? migraciones)
    {
        var restaurador = new RestauradorFalso { Inspeccion = new InspeccionCopia(true, true, migraciones, 0) };

        var verificacion = await new RestaurarBackup(restaurador, restaurador.Datos).VerificarAsync(RutaCopia);

        Assert.Equal(RechazoCopia.Ajena, verificacion.Rechazo);
        Assert.DoesNotContain("contar", restaurador.Bitacora);
    }

    [Fact]
    public async Task Verificar_NoSePudieronContarLasFacturas_ValidaSinCantidad()
    {
        // La base actual puede estar dañada (es una razón para restaurar): no contar no impide restaurar.
        var restaurador = new RestauradorFalso
        {
            Inspeccion = new InspeccionCopia(true, true, Conocidas, 0),
            FacturasPosteriores = null,
        };

        var verificacion = await new RestaurarBackup(restaurador, restaurador.Datos).VerificarAsync(RutaCopia);

        Assert.True(verificacion.Valida);
        Assert.Null(verificacion.FacturasPosteriores);
    }

    [Fact]
    public async Task Restaurar_CopiaValida_TemporalVerificadoYDespuesIntercambio()
    {
        var restaurador = new RestauradorFalso
        {
            Inspeccion = new InspeccionCopia(true, true, Conocidas, 7),
            FacturasPosteriores = 0,
            CarpetaAnterior = Path.Combine("anterior", "20261117-101500"),
        };

        var resultado = await new RestaurarBackup(restaurador, restaurador.Datos).RestaurarAsync(RutaCopia);

        Assert.True(resultado.Hecha);
        Assert.Null(resultado.Falla);
        Assert.Equal(Path.Combine("anterior", "20261117-101500"), resultado.CarpetaAnterior);
        Assert.Equal(new DateOnly(2026, 10, 30), resultado.Copia.Fecha);
        // Se vuelve a verificar la copia (pudo cambiar mientras se esperaba la confirmación), se restaura a un temporal y
        // recién con el temporal verificado se aparta la base actual (D-29).
        Assert.Equal(["inspeccionar", "contar", "temporal", "intercambiar", "renovar-claves"], restaurador.Bitacora);
        Assert.Equal([RutaCopia], restaurador.Inspeccionadas);
        Assert.Equal([RutaCopia], restaurador.Temporales);
    }

    public static TheoryData<InspeccionCopia, RechazoCopia> TemporalesRechazados => new()
    {
        // Dañado o no está en modo de diario de reversión.
        { new InspeccionCopia(true, false, null, 0), RechazoCopia.Danada },
        // Sin la tabla de migraciones, o con un hueco: la misma regla que la copia (D-28).
        { new InspeccionCopia(true, true, null, 0), RechazoCopia.Ajena },
        { new InspeccionCopia(true, true, new[] { Conocidas[0], Conocidas[2] }, 0), RechazoCopia.Ajena },
        // Con una migración desconocida.
        { new InspeccionCopia(true, true, new[] { Conocidas[0], "20991231000000_Futura" }, 0), RechazoCopia.VersionMasNueva },
        // Sin un trigger de protección de su historial.
        { new InspeccionCopia(true, true, Conocidas, 0, TriggersCompletos: false), RechazoCopia.Ajena },
    };

    [Theory]
    [MemberData(nameof(TemporalesRechazados))]
    public async Task Restaurar_TemporalNoPasaLaVerificacion_RechazaDescartaYNoAparta(
        InspeccionCopia temporal, RechazoCopia rechazo)
    {
        var restaurador = new RestauradorFalso
        {
            Inspeccion = new InspeccionCopia(true, true, Conocidas, 0),
            InspeccionTemporal = temporal,
        };

        var resultado = await new RestaurarBackup(restaurador, restaurador.Datos).RestaurarAsync(RutaCopia);

        // El temporal se juzga con la misma regla que la copia: rechazado, descartado y sin tocar la base actual.
        Assert.Equal(FallaRestauracion.CopiaRechazada, resultado.Falla);
        Assert.Equal(rechazo, resultado.Copia.Rechazo);
        Assert.Equal("optica-2026-10-30.db", resultado.Copia.NombreArchivo);
        Assert.Null(resultado.CarpetaAnterior);
        Assert.Equal(["inspeccionar", "contar", "temporal", "descartar"], restaurador.Bitacora);
        Assert.Equal(0, restaurador.Datos.Renovaciones);
    }

    [Fact]
    public async Task Verificar_SinTodosLosTriggersDeProteccion_Ajena()
    {
        var restaurador = new RestauradorFalso
        {
            Inspeccion = new InspeccionCopia(true, true, Conocidas, 0, TriggersCompletos: false),
        };

        var verificacion = await new RestaurarBackup(restaurador, restaurador.Datos).VerificarAsync(RutaCopia);

        Assert.Equal(RechazoCopia.Ajena, verificacion.Rechazo);
        Assert.DoesNotContain("contar", restaurador.Bitacora);
    }

    [Fact]
    public async Task Restaurar_CopiaValida_RenuevaLasClavesDespuesDelIntercambio()
    {
        var restaurador = new RestauradorFalso { Inspeccion = new InspeccionCopia(true, true, Conocidas, 0) };

        var resultado = await new RestaurarBackup(restaurador, restaurador.Datos).RestaurarAsync(RutaCopia);

        // Las cookies emitidas con las claves de antes dejan de valer: la base restaurada trae su propia contraseña.
        Assert.True(resultado.Hecha);
        Assert.Null(resultado.TipoErrorClaves);
        Assert.Equal(["inspeccionar", "contar", "temporal", "intercambiar", "renovar-claves"], restaurador.Bitacora);
        Assert.Equal(1, restaurador.Datos.Renovaciones);
    }

    [Fact]
    public async Task Restaurar_FallaAlRenovarLasClaves_HechaConElTipoYSinDeshacer()
    {
        var restaurador = new RestauradorFalso { Inspeccion = new InspeccionCopia(true, true, Conocidas, 0) };
        restaurador.Datos.FallaAlRenovar = new IOException("/home/usuario-privado/datos/claves/key-1.xml");

        var resultado = await new RestaurarBackup(restaurador, restaurador.Datos).RestaurarAsync(RutaCopia);

        // La base ya se restauró: no se deshace ni se descarta nada; solo se informa el tipo, nunca el texto.
        Assert.True(resultado.Hecha);
        Assert.Null(resultado.Falla);
        Assert.Equal(nameof(IOException), resultado.TipoErrorClaves);
        Assert.Equal(restaurador.CarpetaAnterior, resultado.CarpetaAnterior);
        Assert.DoesNotContain("descartar", restaurador.Bitacora);
    }

    [Theory]
    [InlineData("rechazada")]
    [InlineData("temporal")]
    [InlineData("intercambio")]
    [InlineData("vuelta")]
    [InlineData("verificacion")]
    public async Task Restaurar_SinIntercambio_NoRenuevaLasClaves(string falla)
    {
        var restaurador = new RestauradorFalso
        {
            Inspeccion = falla == "rechazada"
                ? new InspeccionCopia(true, false, null, 0)
                : new InspeccionCopia(true, true, Conocidas, 0),
            FallaTemporal = falla == "temporal" ? new IOException("disco lleno") : null,
            FallaIntercambio = falla switch
            {
                "intercambio" => new IOException("en uso"),
                "vuelta" => new VueltaAtrasFallidaException("anterior", new IOException("en uso")),
                _ => null,
            },
            FallaInspeccion = falla == "verificacion" ? new IOException("ilegible") : null,
        };

        var resultado = await new RestaurarBackup(restaurador, restaurador.Datos).RestaurarAsync(RutaCopia);

        Assert.False(resultado.Hecha);
        Assert.Equal(0, restaurador.Datos.Renovaciones);
    }

    [Fact]
    public async Task Restaurar_FallaAlVerificar_InformaElTipoSinTocarNada()
    {
        var restaurador = new RestauradorFalso
        {
            FallaInspeccion = new SystemException("/home/usuario-privado/pendrive: dispositivo no listo"),
        };

        var resultado = await new RestaurarBackup(restaurador, restaurador.Datos).RestaurarAsync(RutaCopia);

        // Antes de tocar nada: el comando puede decir que la base actual no se modificó.
        Assert.Equal(FallaRestauracion.Verificacion, resultado.Falla);
        Assert.Equal(nameof(SystemException), resultado.TipoError);
        Assert.Null(resultado.CarpetaAnterior);
        Assert.Equal("optica-2026-10-30.db", resultado.Copia.NombreArchivo);
        Assert.Equal(["inspeccionar"], restaurador.Bitacora);
    }

    [Fact]
    public async Task Restaurar_FallaElTemporal_DescartaYInformaElTipo()
    {
        var restaurador = new RestauradorFalso
        {
            Inspeccion = new InspeccionCopia(true, true, Conocidas, 0),
            FallaTemporal = new IOException("/home/usuario-privado/datos: disco lleno"),
        };

        var resultado = await new RestaurarBackup(restaurador, restaurador.Datos).RestaurarAsync(RutaCopia);

        Assert.Equal(FallaRestauracion.Temporal, resultado.Falla);
        Assert.Equal(nameof(IOException), resultado.TipoError);
        Assert.Equal(["inspeccionar", "contar", "temporal", "descartar"], restaurador.Bitacora);
    }

    [Fact]
    public async Task Restaurar_FallaElIntercambio_BaseActualVueltaYTemporalDescartado()
    {
        var restaurador = new RestauradorFalso
        {
            Inspeccion = new InspeccionCopia(true, true, Conocidas, 0),
            FallaIntercambio = new UnauthorizedAccessException("/home/usuario-privado/datos/optica.db"),
        };

        var resultado = await new RestaurarBackup(restaurador, restaurador.Datos).RestaurarAsync(RutaCopia);

        Assert.Equal(FallaRestauracion.Intercambio, resultado.Falla);
        Assert.Equal(nameof(UnauthorizedAccessException), resultado.TipoError);
        Assert.Null(resultado.CarpetaAnterior);
        Assert.Equal(["inspeccionar", "contar", "temporal", "intercambiar", "descartar"], restaurador.Bitacora);
    }

    [Fact]
    public async Task Restaurar_FallaLaVueltaAtras_InformaDondeQuedoLaBase()
    {
        var anterior = Path.Combine("anterior", "20261117-101500");
        var restaurador = new RestauradorFalso
        {
            Inspeccion = new InspeccionCopia(true, true, Conocidas, 0),
            FallaIntercambio = new VueltaAtrasFallidaException(anterior, new IOException("/home/usuario-privado")),
        };

        var resultado = await new RestaurarBackup(restaurador, restaurador.Datos).RestaurarAsync(RutaCopia);

        Assert.Equal(FallaRestauracion.VueltaAtras, resultado.Falla);
        Assert.Equal(anterior, resultado.CarpetaAnterior);
        Assert.Equal(nameof(IOException), resultado.TipoError);
        // El temporal restaurado no se borra: puede ser la única base completa que queda en la carpeta de datos.
        Assert.DoesNotContain("descartar", restaurador.Bitacora);
    }

    [Fact]
    public void VueltaAtrasFallida_Mensaje_SinRutas()
    {
        var error = new VueltaAtrasFallidaException(
            Path.Combine("anterior", "20261117-101500"), new IOException("/home/usuario-privado"));

        Assert.DoesNotContain("usuario-privado", error.Message, StringComparison.Ordinal);
        Assert.Equal(Path.Combine("anterior", "20261117-101500"), error.CarpetaAnterior);
    }

    /// <summary>Restaurador en memoria: anota cada llamada y responde lo que la prueba le pida.</summary>
    private sealed class RestauradorFalso : IRestauradorBase
    {
        public InspeccionCopia Inspeccion { get; init; } = new(true, true, Conocidas, 0);

        public int? FacturasPosteriores { get; init; }

        /// <summary>La inspección del temporal; si no se pide otra, la misma de la copia.</summary>
        public InspeccionCopia? InspeccionTemporal { get; init; }

        public Exception? FallaInspeccion { get; init; }

        public Exception? FallaTemporal { get; init; }

        public DatosFalsos Datos { get; }

        public RestauradorFalso() => Datos = new DatosFalsos(Bitacora);

        public Exception? FallaIntercambio { get; init; }

        public string CarpetaAnterior { get; init; } = Path.Combine("anterior", "20261030-200000");

        public List<string> Bitacora { get; } = [];

        public List<string> Inspeccionadas { get; } = [];

        public List<string> Temporales { get; } = [];

        public List<long> ContadasDesde { get; } = [];

        public IReadOnlyList<string> MigracionesConocidas() => Conocidas;

        public Task<InspeccionCopia> InspeccionarCopiaAsync(string rutaCopia, CancellationToken cancelacion = default)
        {
            Bitacora.Add("inspeccionar");
            Inspeccionadas.Add(rutaCopia);
            return FallaInspeccion is null ? Task.FromResult(Inspeccion) : Task.FromException<InspeccionCopia>(FallaInspeccion);
        }

        public Task<int?> ContarFacturasPosterioresAsync(long ultimaFacturaDeLaCopia, CancellationToken cancelacion = default)
        {
            Bitacora.Add("contar");
            ContadasDesde.Add(ultimaFacturaDeLaCopia);
            return Task.FromResult(FacturasPosteriores);
        }

        public Task<InspeccionCopia> RestaurarATemporalAsync(string rutaCopia, CancellationToken cancelacion = default)
        {
            Bitacora.Add("temporal");
            Temporales.Add(rutaCopia);
            return FallaTemporal is null
                ? Task.FromResult(InspeccionTemporal ?? Inspeccion)
                : Task.FromException<InspeccionCopia>(FallaTemporal);
        }

        public string ApartarYPonerRestaurada()
        {
            Bitacora.Add("intercambiar");
            return FallaIntercambio is null ? CarpetaAnterior : throw FallaIntercambio;
        }

        public void DescartarTemporal() => Bitacora.Add("descartar");
    }

    /// <summary>Datos locales en memoria: anotan cada renovación de claves en la bitácora del restaurador.</summary>
    private sealed class DatosFalsos(List<string> bitacora) : Optica.Application.Comun.IDatosLocales
    {
        public Exception? FallaAlRenovar { get; set; }

        public int Renovaciones { get; private set; }

        public string Directorio => "/home/usuario-privado/datos";

        public string DirectorioClaves => "/home/usuario-privado/datos/claves";

        public string RutaVisible => "~/datos";

        public bool BaseExiste => true;

        public bool MigracionesPendientes() => false;

        public void RenovarClaves()
        {
            bitacora.Add("renovar-claves");
            Renovaciones++;
            if (FallaAlRenovar is not null)
            {
                throw FallaAlRenovar;
            }
        }

        public Optica.Application.Comun.FallaDatos? DescribirFalla(Exception error) => null;
    }
}
