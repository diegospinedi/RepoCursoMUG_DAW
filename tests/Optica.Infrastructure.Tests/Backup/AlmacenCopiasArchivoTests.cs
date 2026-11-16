using Optica.Application.Backup;
using Optica.Domain.Backup;

namespace Optica.Infrastructure.Tests.Backup;

/// <summary>
/// Spec FEAT-010, Block 4 (FR-10 a FR-12; AC-11; D-21, D-23; R-03): el almacén de copias sobre una carpeta temporal
/// propia, dentro del caso de uso completo con la copia real. Solo toca archivos con el patrón exacto
/// <c>optica-AAAA-MM-DD.db</c> (o su temporal) en la carpeta configurada: nunca subcarpetas ni otros archivos.
/// </summary>
public class AlmacenCopiasArchivoTests
{
    [Fact]
    public async Task Backup_Retencion_SoloArchivosConPatron()
    {
        using var entorno = new EntornoBackup();
        await entorno.GrabarUbicacionAsync();

        // Cinco meses y medio de copias diarias (del 10/05 al 29/10), con contenido cualquiera: la retención mira nombres.
        for (var fecha = new DateOnly(2026, 5, 10); fecha < EntornoBackup.Hoy; fecha = fecha.AddDays(1))
        {
            File.WriteAllText(entorno.RutaCopia(fecha), "copia");
        }

        // Archivos ajenos y casi iguales: nunca se tocan (R-03).
        string[] ajenos =
        [
            "notas.txt",
            "OPTICA-2025-01-01.DB",
            "optica-2026-05-03.db.bak",
            "optica-2026-02-30.db",
            "copia-optica-2026-05-05.db",
            "optica-2026-05-08.db-journal",
            "optica-2026-5-9.db",
        ];
        foreach (var ajeno in ajenos)
        {
            File.WriteAllText(Path.Combine(entorno.Carpeta, ajeno), "ajeno");
        }

        // Una subcarpeta con nombre de copia y una copia adentro: tampoco.
        var subcarpeta = Path.Combine(entorno.Carpeta, CopiaBackup.Nombre(new DateOnly(2026, 5, 6)));
        Directory.CreateDirectory(subcarpeta);
        var adentro = Path.Combine(subcarpeta, CopiaBackup.Nombre(new DateOnly(2026, 5, 7)));
        File.WriteAllText(adentro, "copia");

        // Lo que lista el almacén es exactamente el patrón, en la carpeta y sin subcarpetas.
        Assert.Equal(173, entorno.Almacen.ListarCopias(entorno.Carpeta).Count);

        var resultado = await entorno.CrearCasoDeUso().EjecutarAsync(MotivoBackup.Programado);

        // AC-11: 7 diarias (24/10 al 30/10, la de hoy incluida) y 4 mensuales (la última de junio a septiembre).
        Assert.Equal(EstadoBackup.Hecho, resultado.Estado);
        DateOnly[] esperadas =
        [
            new(2026, 6, 30), new(2026, 7, 31), new(2026, 8, 31), new(2026, 9, 30),
            new(2026, 10, 24), new(2026, 10, 25), new(2026, 10, 26), new(2026, 10, 27), new(2026, 10, 28),
            new(2026, 10, 29), new(2026, 10, 30),
        ];
        Assert.Equal(esperadas, entorno.Almacen.ListarCopias(entorno.Carpeta).Order());
        var quedan = entorno.Archivos();
        Assert.Equal(
            esperadas.Select(CopiaBackup.Nombre).Concat(ajenos).Order(StringComparer.Ordinal),
            quedan);
        Assert.True(Directory.Exists(subcarpeta));
        Assert.True(File.Exists(adentro));

        // El borrado directo tampoco toca lo que no tiene el patrón: una fecha sin copia no borra nada.
        Assert.Equal(0, entorno.Almacen.BorrarCopias(entorno.Carpeta, [new DateOnly(2026, 5, 6), new DateOnly(2025, 1, 1)]));
        Assert.True(Directory.Exists(subcarpeta));
        Assert.Equal(quedan, entorno.Archivos());
    }

    [Fact]
    public async Task Backup_ParcialVerificadoSinCopia_SeRecuperaAlEmpezar()
    {
        using var entorno = new EntornoBackup();
        await entorno.GrabarUbicacionAsync();
        await Persistencia.FacturasPrueba.GrabarPresupuestoFinal(entorno.Base, 1);
        var ayer = EntornoBackup.Hoy.AddDays(-1);
        var anteayer = EntornoBackup.Hoy.AddDays(-2);
        var hace3 = EntornoBackup.Hoy.AddDays(-3);

        // En FAT/exFAT, File.Move con reemplazo no es atómico: un corte entre el borrado de la copia y el renombre deja
        // solo el temporal, ya verificado. El de ayer es una copia íntegra sin su copia terminada: se recupera.
        await entorno.Copiador.CopiarAsync(entorno.Almacen.RutaTemporal(entorno.Carpeta, ayer));
        Assert.True(await entorno.Copiador.VerificarAsync(entorno.Almacen.RutaTemporal(entorno.Carpeta, ayer)));
        var huellaAyer = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(entorno.Almacen.RutaTemporal(entorno.Carpeta, ayer))));

        // Un temporal que no pasa la verificación (escritura cortada) no se recupera: se borra.
        File.WriteAllText(entorno.Almacen.RutaTemporal(entorno.Carpeta, anteayer), "copia a medio escribir");

        // Un temporal íntegro cuya copia terminada existe es un huérfano común: se borra y la copia queda como estaba.
        await entorno.Copiador.CopiarAsync(entorno.Almacen.RutaTemporal(entorno.Carpeta, hace3));
        File.WriteAllText(entorno.RutaCopia(hace3), "copia terminada de hace tres días");

        var resultado = await entorno.CrearCasoDeUso().EjecutarAsync(MotivoBackup.Programado);

        Assert.Equal(EstadoBackup.Hecho, resultado.Estado);
        Assert.Equal(
            new[] { hace3, ayer, EntornoBackup.Hoy }.Select(CopiaBackup.Nombre).Order(StringComparer.Ordinal),
            entorno.Archivos());
        Assert.Equal(
            huellaAyer,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(entorno.RutaCopia(ayer)))));
        Assert.Equal([1L], EntornoBackup.PresupuestosDe(entorno.RutaCopia(ayer)));
        Assert.Equal("copia terminada de hace tres días", File.ReadAllText(entorno.RutaCopia(hace3)));
        // El historial anota solo el backup de hoy: la recuperación no inventa un momento para la copia de ayer.
        Assert.Equal(["2026-10-30"], entorno.FechasHistorial());
    }

    [Fact]
    public async Task Backup_ParcialHuerfano_SeBorraAlEmpezar()
    {
        using var entorno = new EntornoBackup();
        await entorno.GrabarUbicacionAsync();
        var ayer = EntornoBackup.Hoy.AddDays(-1);

        // Temporales de un backup interrumpido (y sus auxiliares de SQLite), más uno con basura en el nombre de hoy: si
        // no se borrara antes de copiar, la copia de hoy fallaría al escribir sobre un archivo que no es una base.
        string[] huerfanos =
        [
            CopiaBackup.NombreParcial(ayer),
            CopiaBackup.NombreParcial(ayer) + "-journal",
            CopiaBackup.NombreParcial(ayer) + "-wal",
            CopiaBackup.NombreParcial(ayer) + "-shm",
            CopiaBackup.NombreParcial(EntornoBackup.Hoy),
            CopiaBackup.NombreParcial(EntornoBackup.Hoy) + "-journal",
        ];
        foreach (var huerfano in huerfanos)
        {
            File.WriteAllText(Path.Combine(entorno.Carpeta, huerfano), "basura que no es una base SQLite");
        }

        // Casi iguales, que no son temporales del sistema: quedan.
        string[] ajenos =
        [
            CopiaBackup.NombreParcial(ayer) + ".txt",
            CopiaBackup.NombreParcial(ayer) + "-otro",
            "notas.parcial",
            "optica-2026-02-30.db.parcial",
        ];
        foreach (var ajeno in ajenos)
        {
            File.WriteAllText(Path.Combine(entorno.Carpeta, ajeno), "ajeno");
        }

        var subcarpeta = Path.Combine(entorno.Carpeta, CopiaBackup.NombreParcial(ayer.AddDays(-1)));
        Directory.CreateDirectory(subcarpeta);

        var resultado = await entorno.CrearCasoDeUso().EjecutarAsync(MotivoBackup.Programado);

        // D-23: los huérfanos se borraron al empezar; la copia de hoy quedó; lo ajeno sigue ahí.
        Assert.Equal(EstadoBackup.Hecho, resultado.Estado);
        Assert.Equal(
            ajenos.Append(CopiaBackup.Nombre(EntornoBackup.Hoy)).Order(StringComparer.Ordinal),
            entorno.Archivos());
        Assert.True(Directory.Exists(subcarpeta));
        Assert.Empty(EntornoBackup.PresupuestosDe(entorno.RutaCopia(EntornoBackup.Hoy)));
    }
}
