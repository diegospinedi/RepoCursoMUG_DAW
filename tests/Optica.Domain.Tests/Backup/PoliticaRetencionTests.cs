using Optica.Domain.Backup;

namespace Optica.Domain.Tests.Backup;

/// <summary>
/// Spec FEAT-010, Block 1 (FR-10, FR-11, NFR-02, AC-11, AC-12, D-21): qué copias se conservan. Las 7 diarias más
/// recientes que no pasan de hoy, más la última de cada uno de los 4 meses calendario terminados más recientes; las
/// futuras se conservan sin contar entre las diarias.
/// </summary>
public class PoliticaRetencionTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 16);

    [Fact]
    public void Retencion_CincoMeses_SieteDiariasYCuatroMensuales()
    {
        // NFR-02: más de 4 meses de copias diarias, del 01/06/2026 a hoy.
        var copias = Dias(new DateOnly(2026, 6, 1), Hoy);

        var conservadas = PoliticaRetencion.Conservar(copias, Hoy);

        // AC-11: las 7 diarias más recientes y 4 mensuales (la última de octubre, septiembre, agosto y julio).
        Assert.Equal(
            [
                new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 31),
                .. Dias(new DateOnly(2026, 11, 10), Hoy),
            ],
            conservadas);

        // El orden y los repetidos de la entrada no cambian el resultado.
        Assert.Equal(conservadas, PoliticaRetencion.Conservar([.. copias.AsEnumerable().Reverse(), .. copias], Hoy));

        // Sin copias no hay nada que conservar.
        Assert.Empty(PoliticaRetencion.Conservar([], Hoy));
    }

    [Fact]
    public void Retencion_UltimaDeOctubreEsMensual()
    {
        // AC-12: el último backup de octubre fue el del 31/10/2026 y el mes terminó; noviembre ya tiene más de 7 copias.
        var copias = Dias(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31))
            .Concat(Dias(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 20)))
            .ToList();
        var hoy = new DateOnly(2026, 11, 20);

        var conservadas = PoliticaRetencion.Conservar(copias, hoy);

        Assert.Equal([new DateOnly(2026, 10, 31), .. Dias(new DateOnly(2026, 11, 14), hoy)], conservadas);

        // Si la última de octubre fue del 29, esa es la mensual: "la última del mes" es la última que hay.
        var sinLasUltimas = copias.Where(c => c != new DateOnly(2026, 10, 30) && c != new DateOnly(2026, 10, 31));
        Assert.Contains(new DateOnly(2026, 10, 29), PoliticaRetencion.Conservar(sinLasUltimas, hoy));

        // Una copia puede ser diaria y mensual a la vez: el 03/11 (sin copia del 01/11), la del 31/10 está entre las 7 y
        // no se repite.
        var principioDeMes = new DateOnly(2026, 11, 3);
        Assert.Equal(
            [.. Dias(new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 31)), new DateOnly(2026, 11, 2), principioDeMes],
            PoliticaRetencion.Conservar(copias.Where(c => c <= principioDeMes), principioDeMes));
    }

    [Fact]
    public void Retencion_CopiasFuturas_NoDesplazanLasDiarias()
    {
        // D-21: con el reloj adelantado se hicieron copias con fecha posterior a hoy. Se conservan (no se borra lo que
        // no se entiende) y no cuentan entre las 7 diarias.
        var futuras = new[] { new DateOnly(2026, 11, 20), new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 15) };
        var copias = Dias(new DateOnly(2026, 11, 1), Hoy).Concat(futuras).ToList();

        var conservadas = PoliticaRetencion.Conservar(copias, Hoy);

        Assert.Equal([.. Dias(new DateOnly(2026, 11, 10), Hoy), .. futuras], conservadas);

        // Tampoco cuentan como mensuales de un mes terminado: diciembre y enero no terminaron.
        Assert.Equal(futuras, PoliticaRetencion.Conservar(futuras, Hoy));
    }

    [Fact]
    public void Retencion_MesSinCopias_TresMensuales()
    {
        // D-21: septiembre no tiene copias. No se reemplaza por junio: quedan 3 mensuales (octubre, agosto y julio).
        var copias = Dias(new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31))
            .Concat(Dias(new DateOnly(2026, 10, 1), Hoy))
            .ToList();

        var conservadas = PoliticaRetencion.Conservar(copias, Hoy);

        Assert.Equal(
            [
                new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 31), new DateOnly(2026, 10, 31),
                .. Dias(new DateOnly(2026, 11, 10), Hoy),
            ],
            conservadas);
    }

    private static List<DateOnly> Dias(DateOnly desde, DateOnly hasta)
    {
        var dias = new List<DateOnly>();
        for (var dia = desde; dia <= hasta; dia = dia.AddDays(1))
        {
            dias.Add(dia);
        }

        return dias;
    }
}
