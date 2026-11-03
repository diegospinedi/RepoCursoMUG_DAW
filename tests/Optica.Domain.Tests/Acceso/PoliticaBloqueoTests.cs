using Optica.Domain.Acceso;

namespace Optica.Domain.Tests.Acceso;

public class PoliticaBloqueoTests
{
    private static readonly DateTime T0 = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PoliticaBloqueo_CincoRechazosConsecutivos_Bloqueado5Minutos()
    {
        Assert.Equal(5, PoliticaBloqueo.RechazosParaBloquear);
        Assert.Equal(TimeSpan.FromMinutes(5), PoliticaBloqueo.Duracion);

        var cuatro = Serie(T0, 4, TimeSpan.FromSeconds(10));
        Assert.False(PoliticaBloqueo.Evaluar(cuatro, cuatro[^1]).EstaBloqueado);

        var cinco = Serie(T0, 5, TimeSpan.FromSeconds(10));
        var quinto = cinco[4];

        // En el instante del quinto rechazo y hasta un instante antes de los 5 minutos, bloqueado con la hora de fin.
        foreach (var ahora in new[] { quinto, quinto.AddMinutes(1), quinto.AddMinutes(5).AddTicks(-1) })
        {
            var estado = PoliticaBloqueo.Evaluar(cinco, ahora);
            Assert.True(estado.EstaBloqueado, $"Debería estar bloqueado a las {ahora:O}.");
            Assert.Equal(quinto.AddMinutes(5), estado.Hasta);
        }
    }

    [Fact]
    public void PoliticaBloqueo_SeisRechazosPorConcurrencia_Bloqueado()
    {
        // Dos pedidos simultáneos con la cuenta en 4: la cuenta salta a 6 sin pasar por un 5 aislado.
        var rechazos = Serie(T0, 4, TimeSpan.FromSeconds(10));
        var quinto = rechazos[^1].AddSeconds(10);
        rechazos = [.. rechazos, quinto, quinto.AddMilliseconds(3)];

        var estado = PoliticaBloqueo.Evaluar(rechazos, rechazos[^1].AddSeconds(1));

        Assert.True(estado.EstaBloqueado);
        // El ancla es el quinto, no el sexto.
        Assert.Equal(quinto.AddMinutes(5), estado.Hasta);
        Assert.False(PoliticaBloqueo.Evaluar(rechazos, quinto.AddMinutes(5)).EstaBloqueado);
    }

    [Fact]
    public void PoliticaBloqueo_A5MinutosDelQuintoRechazo_Desbloqueado()
    {
        var rechazos = Serie(T0, 5, TimeSpan.FromSeconds(10));
        var quinto = rechazos[4];

        Assert.True(PoliticaBloqueo.Evaluar(rechazos, quinto.AddMinutes(5).AddTicks(-1)).EstaBloqueado);

        var aLos5 = PoliticaBloqueo.Evaluar(rechazos, quinto.AddMinutes(5));
        Assert.False(aLos5.EstaBloqueado);
        Assert.Null(aLos5.Hasta);
        Assert.False(PoliticaBloqueo.Evaluar(rechazos, quinto.AddHours(3)).EstaBloqueado);
    }

    [Fact]
    public void PoliticaBloqueo_ExitoReiniciaLaCuenta()
    {
        Assert.Equal(
            [
                (TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Exito),
                (TipoEventoAcceso.Definicion, ResultadoEventoAcceso.Exito),
                (TipoEventoAcceso.CambioContrasena, ResultadoEventoAcceso.Exito),
                (TipoEventoAcceso.Restablecimiento, ResultadoEventoAcceso.Exito),
            ],
            PoliticaBloqueo.EventosQueReinician.Select(e => (e.Tipo, e.Resultado)));

        foreach (var reinicio in PoliticaBloqueo.EventosQueReinician)
        {
            // 4 rechazos, un evento que reinicia y 4 rechazos más: la cuenta es 4, sin bloqueo.
            var eventos = new List<EventoAcceso>();
            eventos.AddRange(RechazosDeIngreso(T0, 4));
            eventos.Add(new EventoAcceso(reinicio.Tipo, reinicio.Resultado, T0.AddMinutes(1)));
            eventos.AddRange(RechazosDeIngreso(T0.AddMinutes(2), 4));

            var rechazos = PoliticaBloqueo.RechazosDesdeUltimoReinicio(eventos);

            Assert.Equal(eventos.Skip(5).Select(e => e.FechaHora), rechazos);
            Assert.False(PoliticaBloqueo.Evaluar(rechazos, rechazos[^1]).EstaBloqueado, $"{reinicio} no reinició.");
        }
    }

    [Fact]
    public void PoliticaBloqueo_BloqueadoYRechazoDeCambio_NoCuentanNiReinician()
    {
        var eventos = new List<EventoAcceso>();
        eventos.AddRange(RechazosDeIngreso(T0, 4));
        eventos.Add(new EventoAcceso(TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Bloqueado, T0.AddSeconds(50)));
        eventos.Add(new EventoAcceso(TipoEventoAcceso.CambioContrasena, ResultadoEventoAcceso.Rechazo, T0.AddSeconds(51)));
        eventos.Add(new EventoAcceso(TipoEventoAcceso.Salida, ResultadoEventoAcceso.Exito, T0.AddSeconds(52)));
        eventos.Add(new EventoAcceso(TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Rechazo, T0.AddSeconds(53)));

        var rechazos = PoliticaBloqueo.RechazosDesdeUltimoReinicio(eventos);

        Assert.Equal(5, rechazos.Count);
        var estado = PoliticaBloqueo.Evaluar(rechazos, T0.AddMinutes(1));
        Assert.True(estado.EstaBloqueado);
        Assert.Equal(T0.AddSeconds(53).AddMinutes(5), estado.Hasta);
        Assert.Empty(PoliticaBloqueo.RechazosDesdeUltimoReinicio([]));
    }

    [Fact]
    public void PoliticaBloqueo_DecimoRechazo_BloqueadoOtraVez()
    {
        var primeros = Serie(T0, 5, TimeSpan.FromSeconds(10));
        var siguientes = Serie(primeros[4].AddMinutes(6), 4, TimeSpan.FromSeconds(10));
        List<DateTime> nueve = [.. primeros, .. siguientes];

        // Con 9, el ancla sigue en el quinto, ya vencido: sin bloqueo.
        Assert.False(PoliticaBloqueo.Evaluar(nueve, nueve[^1].AddSeconds(1)).EstaBloqueado);

        var decimo = nueve[^1].AddSeconds(10);
        List<DateTime> diez = [.. nueve, decimo];
        var estado = PoliticaBloqueo.Evaluar(diez, decimo.AddMinutes(1));

        Assert.True(estado.EstaBloqueado);
        Assert.Equal(decimo.AddMinutes(5), estado.Hasta);
        Assert.False(PoliticaBloqueo.Evaluar(diez, decimo.AddMinutes(5)).EstaBloqueado);
    }

    [Fact]
    public void PoliticaBloqueo_RelojAtrasado_SinBloqueoNiError()
    {
        var rechazos = Serie(T0, 5, TimeSpan.FromSeconds(10));
        var quinto = rechazos[4];

        foreach (var ahora in new[] { quinto.AddTicks(-1), quinto.AddHours(-1), quinto.AddDays(-3) })
        {
            EstadoBloqueo? estado = null;
            var error = Record.Exception(() => estado = PoliticaBloqueo.Evaluar(rechazos, ahora));

            Assert.Null(error);
            Assert.NotNull(estado);
            Assert.False(estado.EstaBloqueado, $"No debería bloquear con la hora en {ahora:O}.");
            Assert.Null(estado.Hasta);
        }
    }

    [Fact]
    public void PoliticaBloqueo_RelojAtrasado_DecimoRechazoBloqueaConLaHoraNueva()
    {
        // Cinco rechazos y, después, la hora de la PC atrasada dos horas.
        var primeros = Serie(T0, 5, TimeSpan.FromSeconds(10));
        var siguientes = Serie(T0.AddHours(-2), 4, TimeSpan.FromSeconds(10));
        List<DateTime> nueve = [.. primeros, .. siguientes];

        // El atraso libera como máximo 4 intentos.
        Assert.False(PoliticaBloqueo.Evaluar(nueve, nueve[^1].AddSeconds(1)).EstaBloqueado);

        var decimo = nueve[^1].AddSeconds(10);
        List<DateTime> diez = [.. nueve, decimo];
        var estado = PoliticaBloqueo.Evaluar(diez, decimo.AddSeconds(1));

        Assert.True(estado.EstaBloqueado);
        Assert.Equal(decimo.AddMinutes(5), estado.Hasta);
    }

    [Fact]
    public void PoliticaBloqueo_FechasNoUtcONulas_Rechazadas()
    {
        var rechazos = Serie(T0, 5, TimeSpan.FromSeconds(10));
        var local = DateTime.SpecifyKind(T0, DateTimeKind.Local);
        var sinTipo = DateTime.SpecifyKind(T0, DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(() => PoliticaBloqueo.Evaluar(rechazos, local));
        Assert.Throws<ArgumentException>(() => PoliticaBloqueo.Evaluar(rechazos, sinTipo));
        Assert.Throws<ArgumentException>(() => PoliticaBloqueo.Evaluar([.. rechazos[..4], sinTipo], T0));
        Assert.Throws<ArgumentNullException>(() => PoliticaBloqueo.Evaluar(null!, T0));
        Assert.Throws<ArgumentNullException>(() => PoliticaBloqueo.RechazosDesdeUltimoReinicio(null!));
    }

    [Fact]
    public void PoliticaBloqueo_EventoQueCuenta_EsIngresoRechazoYNoReinicia()
    {
        Assert.Equal((TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Rechazo), PoliticaBloqueo.EventoQueCuenta);
        Assert.DoesNotContain(PoliticaBloqueo.EventoQueCuenta, PoliticaBloqueo.EventosQueReinician);
    }

    [Fact]
    public void EstadoBloqueo_BloqueadoHastaNoUtc_Rechazado()
    {
        var local = DateTime.SpecifyKind(T0, DateTimeKind.Local);
        var sinTipo = DateTime.SpecifyKind(T0, DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(() => EstadoBloqueo.BloqueadoHasta(local));
        Assert.Throws<ArgumentException>(() => EstadoBloqueo.BloqueadoHasta(sinTipo));

        var estado = EstadoBloqueo.BloqueadoHasta(T0);
        Assert.True(estado.EstaBloqueado);
        Assert.Equal(T0, estado.Hasta);
        Assert.Equal(DateTimeKind.Utc, estado.Hasta?.Kind);
    }

    private static List<DateTime> Serie(DateTime inicio, int cantidad, TimeSpan paso) =>
        [.. Enumerable.Range(0, cantidad).Select(i => inicio + (paso * i))];

    private static IEnumerable<EventoAcceso> RechazosDeIngreso(DateTime inicio, int cantidad) =>
        Serie(inicio, cantidad, TimeSpan.FromSeconds(10))
            .Select(fecha => new EventoAcceso(TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Rechazo, fecha));
}
