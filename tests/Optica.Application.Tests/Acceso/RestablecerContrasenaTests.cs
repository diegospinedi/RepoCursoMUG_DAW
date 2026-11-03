using Optica.Application.Acceso;
using Optica.Application.Comun;
using Optica.Domain.Acceso;

namespace Optica.Application.Tests.Acceso;

public class RestablecerContrasenaTests
{
    private static readonly DateTime Antes = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly AlmacenEnMemoria _almacen = new();
    private readonly DatosLocalesFalsos _datos;
    private readonly RelojFijo _reloj = new();

    public RestablecerContrasenaTests()
    {
        _datos = new DatosLocalesFalsos(_almacen);
    }

    private RestablecerContrasena CrearCasoDeUso() => new(_almacen, _almacen, _almacen, _datos, _reloj);

    [Fact]
    public async Task RestablecerContrasena_EliminaSoloLaCredencialYRegistraEvento()
    {
        _almacen.Credencial = new CredencialAcceso("falso$guardado", Antes);
        var anteriores = new[]
        {
            new EventoAcceso(TipoEventoAcceso.Definicion, ResultadoEventoAcceso.Exito, Antes),
            new EventoAcceso(TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Rechazo, Antes.AddMinutes(1)),
        };
        _almacen.Eventos.AddRange(anteriores);

        var resultado = await CrearCasoDeUso().EjecutarAsync();

        Assert.Equal(ResultadoRestablecimiento.Restablecida, resultado.Tipo);
        Assert.True(resultado.HabiaCredencial);
        Assert.Null(resultado.TipoError);
        Assert.Null(_almacen.Credencial);
        // Los eventos anteriores siguen iguales, en el mismo orden, y se agrega uno solo al final.
        Assert.Equal(3, _almacen.Eventos.Count);
        Assert.Same(anteriores[0], _almacen.Eventos[0]);
        Assert.Same(anteriores[1], _almacen.Eventos[1]);
        var evento = _almacen.Eventos[2];
        Assert.Equal(TipoEventoAcceso.Restablecimiento, evento.Tipo);
        Assert.Equal(ResultadoEventoAcceso.Exito, evento.Resultado);
        Assert.Equal(RelojFijo.Instante.UtcDateTime, evento.FechaHora);
        // Una sola confirmación, y las claves se renuevan después de ella, una vez.
        Assert.Equal(1, _almacen.Confirmaciones);
        Assert.Equal([1], _datos.ConfirmacionesAlRenovar);
    }

    [Fact]
    public async Task RestablecerContrasena_SinCredencial_RegistraEventoYRenuevaClaves()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync();

        Assert.Equal(ResultadoRestablecimiento.Restablecida, resultado.Tipo);
        Assert.False(resultado.HabiaCredencial);
        Assert.Null(_almacen.Credencial);
        var evento = Assert.Single(_almacen.Eventos);
        Assert.Equal(TipoEventoAcceso.Restablecimiento, evento.Tipo);
        Assert.Equal(ResultadoEventoAcceso.Exito, evento.Resultado);
        Assert.Equal(1, _almacen.Confirmaciones);
        Assert.Equal([1], _datos.ConfirmacionesAlRenovar);
    }

    [Fact]
    public async Task RestablecerContrasena_ErrorAlConfirmar_NoEliminaNada()
    {
        var credencial = new CredencialAcceso("falso$guardado", Antes);
        _almacen.Credencial = credencial;
        var falla = new InvalidOperationException("la base falló");
        _almacen.AlConfirmar = () => throw falla;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CrearCasoDeUso().EjecutarAsync());

        Assert.Same(falla, error);
        Assert.Same(credencial, _almacen.Credencial);
        Assert.Empty(_almacen.Eventos);
        // Sin confirmar no se tocan las claves: las sesiones siguen como estaban, igual que la credencial.
        Assert.Empty(_datos.ConfirmacionesAlRenovar);
    }

    [Fact]
    public async Task RestablecerContrasena_FallaAlRenovarClaves_Error()
    {
        _almacen.Credencial = new CredencialAcceso("falso$guardado", Antes);
        _datos.FallaAlRenovar = new IOException("/home/usuario-privado/claves está en uso");

        var resultado = await CrearCasoDeUso().EjecutarAsync();

        Assert.Equal(ResultadoRestablecimiento.ClavesSinRenovar, resultado.Tipo);
        Assert.True(resultado.HabiaCredencial);
        // Solo el tipo de la falla, nunca su texto (puede traer la ruta con el usuario de Windows).
        Assert.Equal(nameof(IOException), resultado.TipoError);
        // Lo confirmado queda: la credencial se eliminó y el evento está; falta repetir para renovar las claves.
        Assert.Null(_almacen.Credencial);
        Assert.Equal(TipoEventoAcceso.Restablecimiento, Assert.Single(_almacen.Eventos).Tipo);
        Assert.Equal([1], _datos.ConfirmacionesAlRenovar);
    }

    [Fact]
    public async Task RestablecerContrasena_FallaInesperadaAlRenovarClaves_ClavesSinRenovar()
    {
        // Cualquier falla al renovar ocurre después de confirmar: no puede llegar como si no se hubiera cambiado nada.
        _almacen.Credencial = new CredencialAcceso("falso$guardado", Antes);
        _datos.FallaAlRenovar = new InvalidOperationException("/home/usuario-privado/claves");

        var resultado = await CrearCasoDeUso().EjecutarAsync();

        Assert.Equal(ResultadoRestablecimiento.ClavesSinRenovar, resultado.Tipo);
        Assert.Equal(nameof(InvalidOperationException), resultado.TipoError);
        Assert.Null(_almacen.Credencial);
        Assert.Equal(TipoEventoAcceso.Restablecimiento, Assert.Single(_almacen.Eventos).Tipo);
    }

    [Fact]
    public async Task RestablecerContrasena_Repetido_RegistraOtroEventoYVuelveARenovar()
    {
        _almacen.Credencial = new CredencialAcceso("falso$guardado", Antes);

        await CrearCasoDeUso().EjecutarAsync();
        var segundo = await CrearCasoDeUso().EjecutarAsync();

        Assert.Equal(ResultadoRestablecimiento.Restablecida, segundo.Tipo);
        Assert.False(segundo.HabiaCredencial);
        Assert.Equal(2, _almacen.Eventos.Count(e => e.Tipo == TipoEventoAcceso.Restablecimiento));
        Assert.Equal([1, 2], _datos.ConfirmacionesAlRenovar);
    }

    /// <summary>
    /// Datos locales de prueba: anota cuántas confirmaciones había en el almacén cada vez que se renovaron las claves y,
    /// si se pide, falla al renovarlas.
    /// </summary>
    private sealed class DatosLocalesFalsos(AlmacenEnMemoria almacen) : IDatosLocales
    {
        public List<int> ConfirmacionesAlRenovar { get; } = [];

        public Exception? FallaAlRenovar { get; set; }

        public string Directorio => "/datos-de-prueba";

        public string DirectorioClaves => "/datos-de-prueba/claves";

        public string RutaVisible => "/datos-de-prueba";

        public bool BaseExiste => true;

        public bool MigracionesPendientes() => false;

        public void RenovarClaves()
        {
            ConfirmacionesAlRenovar.Add(almacen.Confirmaciones);
            if (FallaAlRenovar is not null)
            {
                throw FallaAlRenovar;
            }
        }

        public FallaDatos? DescribirFalla(Exception error) => null;
    }
}
