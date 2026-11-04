using Optica.Application.Acceso;
using Optica.Domain.Acceso;

namespace Optica.Application.Tests.Acceso;

public class IngresarConContrasenaTests
{
    private readonly AlmacenEnMemoria _almacen = new();
    private readonly HasherFalso _hasher = new();

    private IngresarConContrasena CrearCasoDeUso() =>
        new(_almacen, _almacen, _hasher, _almacen, new RelojFijo());

    private void DefinirCredencial(string contrasena) =>
        _almacen.Credencial = new CredencialAcceso(
            _hasher.Calcular(contrasena), new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task IngresarConContrasena_SinCredencial_RechazadoSinEvento()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync("cualquier-clave");

        Assert.Equal(ResultadoIngreso.SinCredencial, resultado.Tipo);
        Assert.Empty(_almacen.Eventos);
        Assert.Equal(0, _hasher.Verificaciones);
    }

    [Fact]
    public async Task IngresarConContrasena_Incorrecta_RechazadaYEventoRegistrado()
    {
        DefinirCredencial("clave-correcta");

        var resultado = await CrearCasoDeUso().EjecutarAsync("clave-incorrecta");

        Assert.Equal(ResultadoIngreso.Rechazo, resultado.Tipo);
        var evento = Assert.Single(_almacen.Eventos);
        Assert.Equal(TipoEventoAcceso.Ingreso, evento.Tipo);
        Assert.Equal(ResultadoEventoAcceso.Rechazo, evento.Resultado);
        Assert.Equal(RelojFijo.Instante.UtcDateTime, evento.FechaHora);
        Assert.Equal(DateTimeKind.Utc, evento.FechaHora.Kind);
    }

    [Fact]
    public async Task IngresarConContrasena_Correcta_ExitoYEventoRegistrado()
    {
        DefinirCredencial("clave-correcta");

        var resultado = await CrearCasoDeUso().EjecutarAsync("clave-correcta");

        Assert.Equal(ResultadoIngreso.Exito, resultado.Tipo);
        var evento = Assert.Single(_almacen.Eventos);
        Assert.Equal(TipoEventoAcceso.Ingreso, evento.Tipo);
        Assert.Equal(ResultadoEventoAcceso.Exito, evento.Resultado);
        Assert.Equal(RelojFijo.Instante.UtcDateTime, evento.FechaHora);
    }

    [Theory]
    [InlineData(null, ValidacionContrasena.AccionMuyCorta)]
    [InlineData("1234567", ValidacionContrasena.AccionMuyCorta)]
    [InlineData("x129", ValidacionContrasena.AccionMuyLarga)]
    [InlineData("sustituto-suelto", ValidacionContrasena.AccionCaracteresInvalidos)]
    [InlineData("un-millon", ValidacionContrasena.AccionMuyLarga)]
    public async Task IngresarConContrasena_FueraDePolitica_InvalidoSinEventoNiHash(string? contrasena, string accion)
    {
        // Los valores que no entran en un atributo (sustitutos sueltos) o son enormes se arman acá.
        contrasena = contrasena switch
        {
            "x129" => new string('x', 129),
            "sustituto-suelto" => "abcdefg\uD800",
            "un-millon" => new string('x', 1_000_000),
            _ => contrasena,
        };

        DefinirCredencial("clave-correcta");

        var resultado = await CrearCasoDeUso().EjecutarAsync(contrasena);

        // Decisión del usuario (2026-10-31, ADR-005): 400 con campo y acción, sin evento y sin calcular el hash.
        Assert.Equal(ResultadoIngreso.Invalido, resultado.Tipo);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(ValidacionContrasena.Campo, error.Campo);
        Assert.Equal(accion, error.Accion);
        Assert.Empty(_almacen.Eventos);
        Assert.Equal(0, _almacen.Confirmaciones);
        Assert.Equal(0, _almacen.Consultas);
        Assert.Equal(0, _hasher.Verificaciones);
    }

    [Fact]
    public async Task IngresarConContrasena_FueraDePoliticaSinCredencial_InvalidoSinConsultarLaBase()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync("corta");

        Assert.Equal(ResultadoIngreso.Invalido, resultado.Tipo);
        Assert.Equal(ValidacionContrasena.AccionMuyCorta, Assert.Single(resultado.Errores).Accion);
        Assert.Equal(0, _almacen.Consultas);
        Assert.Empty(_almacen.Eventos);
    }

    /// <summary>Deja guardados (confirmados) <paramref name="cantidad"/> rechazos de ingreso, cada 10 segundos antes de ahora.</summary>
    private void RegistrarRechazosConfirmados(int cantidad)
    {
        for (var i = cantidad; i >= 1; i--)
        {
            _almacen.Eventos.Add(new EventoAcceso(
                TipoEventoAcceso.Ingreso,
                ResultadoEventoAcceso.Rechazo,
                RelojFijo.Instante.UtcDateTime.AddSeconds(-10 * i)));
        }
    }

    [Fact]
    public async Task IngresarConContrasena_Bloqueado_RechazadoSinCalcularHashYEventoRegistrado()
    {
        DefinirCredencial("clave-correcta");
        RegistrarRechazosConfirmados(5);
        var eventosAntes = _almacen.Eventos.Count;

        // Ni siquiera con la contraseña correcta: durante el bloqueo no se verifica el hash.
        var resultado = await CrearCasoDeUso().EjecutarAsync("clave-correcta");

        Assert.Equal(ResultadoIngreso.Bloqueado, resultado.Tipo);
        Assert.Empty(resultado.Errores);
        Assert.Equal(0, _hasher.Verificaciones);
        Assert.Equal(eventosAntes + 1, _almacen.Eventos.Count);
        var evento = _almacen.Eventos[^1];
        Assert.Equal(TipoEventoAcceso.Ingreso, evento.Tipo);
        Assert.Equal(ResultadoEventoAcceso.Bloqueado, evento.Resultado);
        Assert.Equal(RelojFijo.Instante.UtcDateTime, evento.FechaHora);
        Assert.Equal(1, _almacen.Confirmaciones);
    }

    [Fact]
    public async Task IngresarConContrasena_QuintoRechazo_DevuelveBloqueado()
    {
        DefinirCredencial("clave-correcta");
        var reloj = new RelojAjustable();
        var ingresar = new IngresarConContrasena(_almacen, _almacen, _hasher, _almacen, reloj);

        for (var intento = 1; intento <= 4; intento++)
        {
            Assert.Equal(ResultadoIngreso.Rechazo, (await ingresar.EjecutarAsync("clave-incorrecta")).Tipo);
            reloj.Avanzar(TimeSpan.FromSeconds(1));
        }

        // El quinto rechazo bloquea en ese mismo intento: la persona se entera sin volver a probar.
        var quinto = await ingresar.EjecutarAsync("clave-incorrecta");

        Assert.Equal(ResultadoIngreso.Bloqueado, quinto.Tipo);
        Assert.Equal(5, _hasher.Verificaciones);
        Assert.Equal(5, _almacen.Eventos.Count(e => e.Resultado == ResultadoEventoAcceso.Rechazo));
        Assert.Equal(ResultadoEventoAcceso.Rechazo, _almacen.Eventos[^1].Resultado);
        Assert.Equal(5, _almacen.Confirmaciones);

        // Pasados 5 minutos desde el quinto rechazo, la correcta vuelve a entrar.
        reloj.Avanzar(PoliticaBloqueo.Duracion);
        Assert.Equal(ResultadoIngreso.Exito, (await ingresar.EjecutarAsync("clave-correcta")).Tipo);
    }

    [Fact]
    public async Task IngresarConContrasena_FueraDePoliticaDuranteBloqueo_Invalido400SinEvento()
    {
        DefinirCredencial("clave-correcta");
        RegistrarRechazosConfirmados(5);
        var eventosAntes = _almacen.Eventos.Count;

        var resultado = await CrearCasoDeUso().EjecutarAsync("corta");

        // ADR-005 antes que todo, también durante un bloqueo: 400 sin evento, sin consultar la base ni el hash.
        Assert.Equal(ResultadoIngreso.Invalido, resultado.Tipo);
        Assert.Equal(ValidacionContrasena.AccionMuyCorta, Assert.Single(resultado.Errores).Accion);
        Assert.Equal(eventosAntes, _almacen.Eventos.Count);
        Assert.Equal(0, _almacen.Confirmaciones);
        Assert.Equal(0, _almacen.Consultas);
        Assert.Equal(0, _hasher.Verificaciones);
    }

    [Fact]
    public async Task IngresarConContrasena_QuintoRechazoConfirmadoJustoAntesDeLaConsulta_Bloqueado()
    {
        DefinirCredencial("clave-correcta");
        RegistrarRechazosConfirmados(4);
        var reloj = new RelojAjustable();
        var ingresar = new IngresarConContrasena(_almacen, _almacen, _hasher, _almacen, reloj);

        // Otro pedido confirma el quinto rechazo un segundo después de que este empezó, justo antes de la consulta.
        _almacen.AlConsultarRechazos = () =>
        {
            reloj.Avanzar(TimeSpan.FromSeconds(1));
            _almacen.Eventos.Add(new EventoAcceso(
                TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Rechazo, reloj.GetUtcNow().UtcDateTime));
        };

        // La hora del intento se toma después de la consulta: ese rechazo no queda en el futuro y el bloqueo vale.
        var resultado = await ingresar.EjecutarAsync("clave-correcta");

        Assert.Equal(ResultadoIngreso.Bloqueado, resultado.Tipo);
        Assert.Equal(0, _hasher.Verificaciones);
        Assert.Equal(ResultadoEventoAcceso.Bloqueado, _almacen.Eventos[^1].Resultado);
        Assert.Equal(reloj.GetUtcNow().UtcDateTime, _almacen.Eventos[^1].FechaHora);
    }

    [Fact]
    public async Task IngresarConContrasena_CarreraInversa_QuintoConfirmadoPorOtroConHoraPosterior_Bloqueado()
    {
        DefinirCredencial("clave-correcta");
        RegistrarRechazosConfirmados(4);
        var ahora = RelojFijo.Instante.UtcDateTime;

        // Los dos pedidos leyeron cuatro rechazos y este tomó la hora antes que el otro. El otro confirma primero, con
        // una hora posterior: su rechazo es el quinto (el ancla) y queda en el futuro para este pedido. El reloj de
        // este pedido no avanza, así que releer con la hora nueva no alcanza y el bloqueo depende de lo leído.
        _almacen.AlConfirmar = () => _almacen.Eventos.Add(new EventoAcceso(
            TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Rechazo, ahora.AddSeconds(1)));

        var resultado = await CrearCasoDeUso().EjecutarAsync("clave-incorrecta");

        Assert.Equal(ResultadoIngreso.Bloqueado, resultado.Tipo);
        Assert.Equal(6, _almacen.Eventos.Count(e => e.Resultado == ResultadoEventoAcceso.Rechazo));
        Assert.Equal(ResultadoEventoAcceso.Rechazo, _almacen.Eventos[^1].Resultado);
        Assert.Equal(ahora, _almacen.Eventos[^1].FechaHora);
        Assert.Equal(1, _almacen.Confirmaciones);
    }

    [Fact]
    public async Task IngresarConContrasena_SinCredencialConRechazosPrevios_SinCredencialSinConsultarRechazos()
    {
        RegistrarRechazosConfirmados(5);
        var eventosAntes = _almacen.Eventos.Count;

        var resultado = await CrearCasoDeUso().EjecutarAsync("cualquier-clave");

        // Sin credencial responde 409 antes que el bloqueo: no se consultan los rechazos ni se registra un evento.
        Assert.Equal(ResultadoIngreso.SinCredencial, resultado.Tipo);
        Assert.Equal(eventosAntes, _almacen.Eventos.Count);
        Assert.Equal(0, _almacen.Confirmaciones);
        Assert.Equal(1, _almacen.Consultas);
        Assert.Equal(0, _hasher.Verificaciones);
    }

    [Theory]
    [InlineData("contrase" + "ñ" + "a", "contraseña")] // definida en NFD, ingresada en NFC
    [InlineData("contraseña", "contrase" + "ñ" + "a")] // definida en NFC, ingresada en NFD
    [InlineData("caf" + "é" + "-con-leche", "café-con-leche")] // vocal con acento combinante
    public async Task IngresarConContrasena_FormasUnicodeDistintas_Exito(string definida, string ingresada)
    {
        Assert.NotEqual(definida, ingresada);
        var definir = new DefinirContrasena(_almacen, _almacen, _hasher, _almacen, new RelojFijo());
        Assert.True((await definir.EjecutarAsync(definida)).EsExito);

        var resultado = await CrearCasoDeUso().EjecutarAsync(ingresada);

        Assert.Equal(ResultadoIngreso.Exito, resultado.Tipo);
        Assert.Equal(ResultadoEventoAcceso.Exito, _almacen.Eventos[^1].Resultado);
    }
}
