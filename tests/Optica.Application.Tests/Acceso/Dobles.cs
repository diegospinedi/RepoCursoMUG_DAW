using Optica.Application.Acceso;
using Optica.Application.Comun;
using Optica.Domain.Acceso;

namespace Optica.Application.Tests.Acceso;

/// <summary>
/// Almacén en memoria que imita la unidad de trabajo: los repositorios dejan cambios pendientes y solo
/// <see cref="ConfirmarAsync"/> los pasa a lo guardado, todos juntos.
/// </summary>
internal sealed class AlmacenEnMemoria : ICredencialAccesoRepository, IRegistroEventosAcceso, IUnidadDeTrabajo
{
    private CredencialAcceso? _credencialPendiente;
    private readonly List<EventoAcceso> _eventosPendientes = [];

    public CredencialAcceso? Credencial { get; set; }

    public List<EventoAcceso> Eventos { get; } = [];

    public int Confirmaciones { get; private set; }

    /// <summary>Cuántas consultas a la base hubo: la credencial guardada y los rechazos de ingreso.</summary>
    public int Consultas { get; private set; }

    /// <summary>Se ejecuta una sola vez, al comienzo de la primera consulta de rechazos (antes de leerlos).</summary>
    public Action? AlConsultarRechazos { get; set; }

    /// <summary>
    /// Se ejecuta una sola vez, al comienzo de la primera confirmación y antes de pasar lo pendiente a lo guardado:
    /// simula que otro pedido confirmó lo suyo justo antes, así sus eventos quedan registrados primero.
    /// </summary>
    public Action? AlConfirmar { get; set; }

    public Task<bool> ExisteAsync(CancellationToken cancelacion = default)
    {
        Consultas++;
        return Task.FromResult(Credencial is not null);
    }

    public Task<CredencialAcceso?> ObtenerAsync(CancellationToken cancelacion = default)
    {
        Consultas++;
        return Task.FromResult(Credencial);
    }

    public void Agregar(CredencialAcceso credencial) => _credencialPendiente = credencial;

    public void Registrar(EventoAcceso evento) => _eventosPendientes.Add(evento);

    /// <summary>Como la consulta real, solo ve los eventos confirmados.</summary>
    public Task<IReadOnlyList<DateTime>> ObtenerRechazosIngresoDesdeUltimoReinicioAsync(
        CancellationToken cancelacion = default)
    {
        Consultas++;
        var alConsultar = AlConsultarRechazos;
        AlConsultarRechazos = null;
        alConsultar?.Invoke();
        return Task.FromResult(PoliticaBloqueo.RechazosDesdeUltimoReinicio(Eventos));
    }

    public Task ConfirmarAsync(CancellationToken cancelacion = default)
    {
        Confirmaciones++;
        var alConfirmar = AlConfirmar;
        AlConfirmar = null;
        alConfirmar?.Invoke();
        if (_credencialPendiente is not null)
        {
            Credencial = _credencialPendiente;
            _credencialPendiente = null;
        }

        Eventos.AddRange(_eventosPendientes);
        _eventosPendientes.Clear();
        return Task.CompletedTask;
    }
}

/// <summary>Hasher de prueba: guarda un identificador opaco y recuerda a qué contraseña corresponde.</summary>
internal sealed class HasherFalso : IHasherContrasena
{
    private readonly Dictionary<string, string> _contrasenas = [];

    public int Calculos { get; private set; }

    public int Verificaciones { get; private set; }

    public string Calcular(string contrasena)
    {
        Calculos++;
        var hash = "falso$" + Guid.NewGuid().ToString("N");
        _contrasenas[hash] = contrasena;
        return hash;
    }

    public bool Verificar(string contrasena, string hash)
    {
        Verificaciones++;
        return _contrasenas.TryGetValue(hash, out var guardada) && guardada == contrasena;
    }
}

/// <summary>Reloj fijo, en UTC.</summary>
internal sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
{
    public static readonly DateTimeOffset Instante = new(2026, 10, 30, 15, 0, 0, TimeSpan.Zero);

    public RelojFijo()
        : this(Instante)
    {
    }

    public override DateTimeOffset GetUtcNow() => ahora;
}

/// <summary>Reloj en UTC que la prueba adelanta a mano.</summary>
internal sealed class RelojAjustable(DateTimeOffset inicio) : TimeProvider
{
    private DateTimeOffset _ahora = inicio;

    public RelojAjustable()
        : this(RelojFijo.Instante)
    {
    }

    public override DateTimeOffset GetUtcNow() => _ahora;

    public void Avanzar(TimeSpan lapso) => _ahora += lapso;
}
