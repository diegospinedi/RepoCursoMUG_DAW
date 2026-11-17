using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Optica.Application.Catalogo;
using Optica.Domain.Catalogo;
using Optica.Infrastructure.Planillas;
using Xunit.Abstractions;

namespace Optica.Infrastructure.Tests.Planillas;

/// <summary>
/// El lector de la planilla de costos (Block 2 de FEAT-011, ADR-031). Los .xlsx válidos se arman en memoria con el SDK
/// (<see cref="SpreadsheetDocument.Create(Stream, SpreadsheetDocumentType)"/>); los armados a propósito (entidad
/// externa, bomba, límites, paquetes rotos) se escriben a mano con <see cref="ZipArchive"/>, con un control que prueba
/// que el mismo paquete sin el defecto sí se lee. Códigos y precios ficticios.
/// </summary>
public sealed class LectorPlanillaCostosXlsxTests
{
    private const string Encabezado1 = "Codigo en el proveedor";
    private const string Encabezado2 = "Precio de Costo Nuevo";

    private readonly LectorPlanillaCostosXlsx _lector = new();
    private readonly ITestOutputHelper _salida;

    public LectorPlanillaCostosXlsxTests(ITestOutputHelper salida)
    {
        _salida = salida;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Lectura

    [Fact]
    public void Leer_PlanillaValida_DevuelveEncabezadosYFilasTipadas()
    {
        var archivo = LibroSdk(libro =>
        {
            var compartidas = libro.AddNewPart<SharedStringTablePart>();
            compartidas.SharedStringTable = new SharedStringTable(
                new SharedStringItem(new Text(Encabezado1)),
                new SharedStringItem(new Text("AB-123")),
                // Texto enriquecido: el texto de cada <r> concatenado.
                new SharedStringItem(new Run(new Text("AB")), new Run(new Text("-9"))));

            AgregarHoja(libro, "Costos",
                Fila(1, Compartida("A1", 0), EnLinea("B1", Encabezado2)),
                Fila(2, Compartida("A2", 1), Numerica("B2", "2420")),
                Fila(3, EnLinea("A3", "x-1"), Numerica("B3", "1234.565")),
                Fila(4, Compartida("A4", 2), Tipada("B4", CellValues.String, "abc")),
                // A y B solo con formato: la fila se descarta (D-08).
                Fila(5, ConFormato("A5"), ConFormato("B5")),
                Fila(6, EnLinea("A6", "SOLO-A")));
        });

        var contenido = Leida(_lector.Leer(archivo));

        Assert.Equal(new CeldaPlanilla.Texto(Encabezado1), contenido.EncabezadoA);
        Assert.Equal(new CeldaPlanilla.Texto(Encabezado2), contenido.EncabezadoB);
        Assert.False(contenido.TieneColumnasDeMas);
        Assert.Equal(
            [
                new FilaPlanilla(2, new CeldaPlanilla.Texto("AB-123"), new CeldaPlanilla.Numero(2420m)),
                new FilaPlanilla(3, new CeldaPlanilla.Texto("x-1"), new CeldaPlanilla.Numero(1234.565m)),
                new FilaPlanilla(4, new CeldaPlanilla.Texto("AB-9"), new CeldaPlanilla.Texto("abc")),
                new FilaPlanilla(6, new CeldaPlanilla.Texto("SOLO-A"), new CeldaPlanilla.Vacia()),
            ],
            contenido.Filas);
    }

    [Fact]
    public void Leer_TerceraColumnaConDato_MarcaColumnasDeMas()
    {
        // En la fila de encabezados, en una fila de datos lejana (AA) y en una fila con A y B vacías.
        var enEncabezado = LibroSdk(libro => AgregarHoja(libro, "Costos",
            Fila(1, EnLinea("A1", Encabezado1), EnLinea("B1", Encabezado2), EnLinea("C1", "Notas")),
            Fila(2, EnLinea("A2", "AB-1"), Numerica("B2", "10"))));
        var enDatos = LibroSdk(libro => AgregarHoja(libro, "Costos",
            Fila(1, EnLinea("A1", Encabezado1), EnLinea("B1", Encabezado2)),
            Fila(2, EnLinea("A2", "AB-1"), Numerica("B2", "10"), Numerica("AA2", "1"))));
        var soloTercera = LibroSdk(libro => AgregarHoja(libro, "Costos",
            Fila(1, EnLinea("A1", Encabezado1), EnLinea("B1", Encabezado2)),
            Fila(2, EnLinea("A2", "AB-1"), Numerica("B2", "10")),
            Fila(3, Numerica("C3", "0"))));

        foreach (var archivo in new[] { enEncabezado, enDatos, soloTercera })
        {
            var contenido = Leida(_lector.Leer(archivo));
            Assert.True(contenido.TieneColumnasDeMas);
            Assert.Equal(
                [new FilaPlanilla(2, new CeldaPlanilla.Texto("AB-1"), new CeldaPlanilla.Numero(10m))],
                contenido.Filas);
        }
    }

    [Fact]
    public void Leer_CeldaConFormatoSinValorEnC_NoMarcaColumnasDeMas()
    {
        // D-08: C y D con formato (estilo) pero sin <v> ni <is> son vacías; Excel las escribe así al dar formato.
        var archivo = LibroSdk(libro => AgregarHoja(libro, "Costos",
            Fila(1, EnLinea("A1", Encabezado1), EnLinea("B1", Encabezado2), ConFormato("C1")),
            Fila(2, EnLinea("A2", "AB-1"), Numerica("B2", "10"), ConFormato("C2"), ConFormato("D2"))));

        var contenido = Leida(_lector.Leer(archivo));

        Assert.False(contenido.TieneColumnasDeMas);
        Assert.Equal(
            [new FilaPlanilla(2, new CeldaPlanilla.Texto("AB-1"), new CeldaPlanilla.Numero(10m))],
            contenido.Filas);
    }

    [Fact]
    public void Leer_PrecioTextoNumeroYCientifico_TiposCorrectos()
    {
        var archivo = LibroSdk(libro =>
        {
            var compartidas = libro.AddNewPart<SharedStringTablePart>();
            compartidas.SharedStringTable = new SharedStringTable(new SharedStringItem(new Text("2420")));

            AgregarHoja(libro, "Costos",
                Fila(1, EnLinea("A1", Encabezado1), EnLinea("B1", Encabezado2)),
                Fila(2, EnLinea("A2", "N"), Numerica("B2", "2420")),
                Fila(3, EnLinea("A3", "DEC"), Numerica("B3", "1234.565")),
                Fila(4, EnLinea("A4", "CIENT"), Numerica("B4", "1.5E3")),
                Fila(5, EnLinea("A5", "NEG"), Numerica("B5", "-0.005")),
                // D-04: texto que parece número sigue siendo texto.
                Fila(6, EnLinea("A6", "COMP"), Compartida("B6", 0)),
                Fila(7, EnLinea("A7", "LIN"), EnLinea("B7", "abc")),
                Fila(8, EnLinea("A8", "BOOL"), Tipada("B8", CellValues.Boolean, "1")),
                Fila(9, EnLinea("A9", "ERR"), Tipada("B9", CellValues.Error, "#N/A")),
                Fila(10, EnLinea("A10", "FECHA"), Tipada("B10", CellValues.Date, "2026-11-17T00:00:00")),
                // Fuera del rango de decimal: TryParse falla y queda "otro", sin excepción.
                Fila(11, EnLinea("A11", "ENORME"), Numerica("B11", "1E+300")),
                Fila(12, EnLinea("A12", "VACIA"), ConFormato("B12")),
                Fila(13, EnLinea("A13", "TIPO-N"), Tipada("B13", CellValues.Number, "7.25")));
        });

        var filas = Leida(_lector.Leer(archivo)).Filas;
        Assert.Equal(12, filas.Count);
        var precios = filas.ToDictionary(f => Assert.IsType<CeldaPlanilla.Texto>(f.Codigo).Valor, f => f.Precio);

        Assert.Equal(new CeldaPlanilla.Numero(2420m), precios["N"]);
        Assert.Equal(new CeldaPlanilla.Numero(1234.565m), precios["DEC"]);
        Assert.Equal(new CeldaPlanilla.Numero(1500m), precios["CIENT"]);
        Assert.Equal(new CeldaPlanilla.Numero(-0.005m), precios["NEG"]);
        Assert.Equal(new CeldaPlanilla.Texto("2420"), precios["COMP"]);
        Assert.Equal(new CeldaPlanilla.Texto("abc"), precios["LIN"]);
        Assert.IsType<CeldaPlanilla.Otro>(precios["BOOL"]);
        Assert.IsType<CeldaPlanilla.Otro>(precios["ERR"]);
        Assert.IsType<CeldaPlanilla.Otro>(precios["FECHA"]);
        Assert.IsType<CeldaPlanilla.Otro>(precios["ENORME"]);
        Assert.IsType<CeldaPlanilla.Vacia>(precios["VACIA"]);
        Assert.Equal(new CeldaPlanilla.Numero(7.25m), precios["TIPO-N"]);
    }

    [Fact]
    public void Leer_CodigoNumerico_TextoInvariante()
    {
        var archivo = LibroSdk(libro => AgregarHoja(libro, "Costos",
            Fila(1, EnLinea("A1", Encabezado1), EnLinea("B1", Encabezado2)),
            Fila(2, Numerica("A2", "100"), Numerica("B2", "1")),
            Fila(3, Numerica("A3", "1200.50"), Numerica("B3", "1")),
            Fila(4, Numerica("A4", "100.0"), Numerica("B4", "1")),
            Fila(5, Numerica("A5", "1.5E3"), Numerica("B5", "1")),
            Fila(6, Numerica("A6", "-7"), Numerica("B6", "1")),
            Fila(7, EnLinea("A7", "00123"), Numerica("B7", "1")),
            Fila(8, Numerica("A8", "1E+300"), Numerica("B8", "1"))));

        // Con una cultura que usa coma decimal: el texto no puede depender de la cultura de la PC.
        var cultura = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-AR");
        IReadOnlyList<FilaPlanilla> filas;
        try
        {
            filas = Leida(_lector.Leer(archivo)).Filas;
        }
        finally
        {
            CultureInfo.CurrentCulture = cultura;
        }

        Assert.Equal(
            [
                new CeldaPlanilla.Texto("100"),
                new CeldaPlanilla.Texto("1200.5"),
                new CeldaPlanilla.Texto("100"),
                new CeldaPlanilla.Texto("1500"),
                new CeldaPlanilla.Texto("-7"),
                new CeldaPlanilla.Texto("00123"),
                new CeldaPlanilla.Otro(),
            ],
            filas.Select(f => f.Codigo));
    }

    [Fact]
    public void Leer_PrimeraHojaPorOrdenDelLibro()
    {
        // La parte de "Alfa" se crea primero (rId, nombre de archivo y sheetId menores, y su nombre va antes en orden
        // alfabético), pero el libro lista "Zeta" primero: manda el orden de workbook.xml.
        var archivo = LibroSdk(libro =>
        {
            var alfa = CrearParteHoja(libro,
                Fila(1, EnLinea("A1", Encabezado1), EnLinea("B1", Encabezado2)),
                Fila(2, EnLinea("A2", "DE-LA-OTRA"), Numerica("B2", "1")));
            var zeta = CrearParteHoja(libro,
                Fila(1, EnLinea("A1", Encabezado1), EnLinea("B1", Encabezado2)),
                Fila(2, EnLinea("A2", "DE-COSTOS"), Numerica("B2", "2")));
            libro.Workbook.Sheets!.Append(
                new Sheet { Id = libro.GetIdOfPart(zeta), SheetId = 2, Name = "Zeta" },
                new Sheet { Id = libro.GetIdOfPart(alfa), SheetId = 1, Name = "Alfa" });
        });

        var contenido = Leida(_lector.Leer(archivo));

        Assert.Equal(
            [new FilaPlanilla(2, new CeldaPlanilla.Texto("DE-COSTOS"), new CeldaPlanilla.Numero(2m))],
            contenido.Filas);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Rechazos

    [Fact]
    public void Leer_ArchivoXlsYCsv_NoEsXlsxRechazado()
    {
        // Firma OLE de un .xls (Excel 97-2003) seguida de relleno.
        var xls = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.Concat(new byte[1024]).ToArray();
        var csv = Encoding.UTF8.GetBytes($"{Encabezado1},{Encabezado2}\r\nAB-1,2420\r\n");
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%âã\n1 0 obj\n<<>>\nendobj\n");
        var zipSinLibro = Zip(new Dictionary<string, string> { ["hola.txt"] = "hola" });

        // Control: el lector sí acepta un .xlsx; sin esto, un lector que rechaza todo pasaría la prueba.
        Assert.IsType<LecturaPlanilla.Leida>(_lector.Leer(PaqueteAMano(HojaMinima())));

        foreach (var archivo in new[] { xls, csv, pdf, zipSinLibro, [] })
        {
            Assert.Same(LecturaPlanilla.NoEsXlsx.Instancia, _lector.Leer(archivo));
        }
    }

    [Fact]
    public void Leer_XmlConEntidadExterna_RechazadoSinLeerElArchivo()
    {
        // La entidad apunta a un TcpListener local en un puerto que eligió el sistema (sin carrera por el puerto): si
        // alguien la resolviera, el listener contaría la conexión.
        var escucha = new TcpListener(IPAddress.Loopback, 0);
        escucha.Start();
        var puerto = ((IPEndPoint)escucha.LocalEndpoint).Port;
        var pedidos = 0;
        var atencion = Task.Run(async () =>
        {
            while (true)
            {
                using var cliente = await escucha.AcceptTcpClientAsync();
                Interlocked.Increment(ref pedidos);
            }
        });

        try
        {
            var doctype = $"<!DOCTYPE x [<!ENTITY xxe SYSTEM \"http://127.0.0.1:{puerto}/xxe\">]>";
            var hojaConEntidad = Prologo + doctype + HojaMinima("&xxe;")[Prologo.Length..];

            // Control 1: un XmlReader que sí procesa DTD y resuelve entidades llega al listener; si no, que el lector
            // no le pida nada no probaría nada.
            // El listener cierra sin responder HTTP, así que el resolvedor termina con error: lo que importa es la conexión.
            try
            {
                using var resolvedor = XmlReader.Create(
                    new StringReader(hojaConEntidad),
                    new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = new XmlUrlResolver() });
                while (resolvedor.Read())
                {
                }
            }
            catch (Exception error) when (error is IOException or System.Net.Http.HttpRequestException or WebException
                or XmlException)
            {
            }

            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref pedidos) > 0, 2_000));
            Interlocked.Exchange(ref pedidos, 0);

            // Control 2: el mismo paquete sin DTD se lee; la entidad se reemplaza por texto común.
            var control = _lector.Leer(PaqueteAMano(HojaMinima("AB-1")));
            Assert.Equal(new CeldaPlanilla.Texto("AB-1"), Assert.Single(Leida(control).Filas).Codigo);

            var enHoja = PaqueteAMano(hojaConEntidad);
            var enCompartidas = PaqueteAMano(
                HojaMinima(codigoCompartido: 0),
                compartidas: Prologo + doctype + $"<sst xmlns=\"{Ns}\"><si><t>&xxe;</t></si></sst>");
            var enLibro = PaqueteAMano(HojaMinima("AB-1"), libro: Prologo + doctype + LibroXml()[Prologo.Length..]);

            foreach (var archivo in new[] { enHoja, enCompartidas, enLibro })
            {
                Assert.Same(LecturaPlanilla.NoEsXlsx.Instancia, _lector.Leer(archivo));
            }

            // Una conexión tardía también contaría: se la espera un momento corto.
            Assert.False(SpinWait.SpinUntil(() => Volatile.Read(ref pedidos) > 0, 200));
        }
        finally
        {
            escucha.Stop();
            // Al detenerse, AcceptTcpClientAsync termina con error: es el final esperado de la atención.
            _ = atencion.ContinueWith(t => t.Exception, TaskScheduler.Default);
        }
    }

    [Fact]
    public void Leer_BombaZip_RechazadoSinAgotarMemoria()
    {
        // En producción, una bomba la frena primero el recorrido del ZIP (D-20): ninguna entrada puede pasar de 16 MiB
        // descomprimidos, así que ninguna parte llega a los 100 millones de caracteres de MaxCharactersInPart (D-14), que
        // queda como segunda barrera, inerte en producción. 40 millones de espacios: pasa el tope del ZIP.
        Assert.Equal(100_000_000, LectorPlanillaCostosXlsx.MaxCaracteresPorParte);
        foreach (var parte in new[] { "xl/worksheets/sheet1.xml", "xl/sharedStrings.xml" })
        {
            var (enProduccion, asignadosProduccion) = Medir(_lector, PaqueteConRelleno(parte, 40_000_000));
            _salida.WriteLine($"Bomba de 40 millones en {parte}, lector de producción: {asignadosProduccion} bytes asignados.");
            Assert.Same(LecturaPlanilla.ExcedeLimites.Instancia, enProduccion);
            Assert.True(asignadosProduccion < 1_000_000, $"La lectura asignó {asignadosProduccion} bytes.");
        }

        // La segunda barrera, por sí sola: un tope de 1.000.000 de caracteres (constructor interno) y 10 millones de
        // espacios, debajo del tope del ZIP. El mecanismo es el mismo (OpenSettings.MaxCharactersInPart).
        const int Tope = 1_000_000;
        const int Relleno = 10_000_000;
        var lector = new LectorPlanillaCostosXlsx(Tope);

        // Hoja: espacios entre filas. Cadenas compartidas: espacios entre elementos.
        var bombaHoja = PaqueteConRelleno("xl/worksheets/sheet1.xml", Relleno);
        var bombaCompartidas = PaqueteConRelleno("xl/sharedStrings.xml", Relleno);

        // Control: con un tope que alcanza, la misma hoja con poco relleno se lee.
        Assert.IsType<LecturaPlanilla.Leida>(lector.Leer(PaqueteConRelleno("xl/worksheets/sheet1.xml", 10_000)));
        Assert.IsType<LecturaPlanilla.Leida>(lector.Leer(PaqueteConRelleno("xl/sharedStrings.xml", 10_000)));

        foreach (var bomba in new[] { bombaHoja, bombaCompartidas })
        {
            // Es una bomba: el ZIP es cientos de veces más chico que lo que descomprime.
            Assert.True(bomba.Length < Relleno / 100, $"El ZIP de la prueba mide {bomba.Length} bytes.");

            var antes = GC.GetAllocatedBytesForCurrentThread();
            var lectura = lector.Leer(bomba);
            var asignados = GC.GetAllocatedBytesForCurrentThread() - antes;

            Assert.Same(LecturaPlanilla.NoEsXlsx.Instancia, lectura);
            // Sin agotar memoria: muy por debajo de los 10 MB descomprimidos (20 MB como caracteres).
            Assert.True(asignados < 8_000_000, $"La lectura asignó {asignados} bytes.");
        }
    }

    [Fact]
    public void Leer_MasDe50000FilasOIndiceAlto_ExcedeLimitesRechazado()
    {
        // Límite exacto: 50.000 filas de datos (2 a 50.001) y una celda de 65.536 caracteres se leen.
        var enElLimite = new StringBuilder();
        for (var fila = 2; fila <= 50_001; fila++)
        {
            enElLimite.Append(FilaXml(fila, $"C{fila}"));
        }

        var leida = Leida(_lector.Leer(PaqueteAMano(Hoja(enElLimite.ToString()))));
        Assert.Equal(50_000, leida.Filas.Count);
        Assert.Equal(50_001, leida.Filas[^1].Numero);

        var celdaEnElLimite = new string('x', 65_536);
        Assert.Equal(
            new CeldaPlanilla.Texto(celdaEnElLimite),
            Assert.Single(Leida(_lector.Leer(PaqueteAMano(Hoja(FilaXml(2, celdaEnElLimite))))).Filas).Codigo);

        // Una fila vacía con formato después del límite no cuenta: no tiene datos.
        Assert.IsType<LecturaPlanilla.Leida>(_lector.Leer(PaqueteAMano(Hoja(
            FilaXml(2, "AB-1") + "<row r=\"60000\"><c r=\"A60000\" s=\"1\"/></row>"))));

        // Índice de fila 50.002 con datos.
        var indiceAlto = PaqueteAMano(Hoja(FilaXml(2, "AB-1") + FilaXml(50_002, "AB-2")));

        // 50.001 filas con datos: un paquete armado que repite el índice de fila (no lo frena el índice).
        var demasiadas = new StringBuilder();
        for (var i = 0; i < 50_001; i++)
        {
            demasiadas.Append(FilaXml(2, "R"));
        }

        // Una celda de 65.537 caracteres, en el código, en el precio (texto) o en una columna de más.
        var largo = new string('x', 65_537);
        var celdaLargaA = PaqueteAMano(Hoja(FilaXml(2, largo)));
        var celdaLargaB = PaqueteAMano(Hoja($"<row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>AB</t></is></c>" +
            $"<c r=\"B2\" t=\"inlineStr\"><is><t>{largo}</t></is></c></row>"));
        var celdaLargaC = PaqueteAMano(Hoja($"<row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>AB</t></is></c>" +
            $"<c r=\"B2\"><v>10</v></c><c r=\"C2\" t=\"inlineStr\"><is><t>{largo}</t></is></c></row>"));
        var celdaLargaCompartida = PaqueteAMano(
            HojaMinima(codigoCompartido: 0), compartidas: $"{Prologo}<sst xmlns=\"{Ns}\"><si><t>{largo}</t></si></sst>");

        foreach (var archivo in new[]
            {
                indiceAlto, PaqueteAMano(Hoja(demasiadas.ToString())), celdaLargaA, celdaLargaB, celdaLargaC,
                celdaLargaCompartida,
            })
        {
            Assert.Same(LecturaPlanilla.ExcedeLimites.Instancia, LeerOExcepcion(_lector, archivo));
        }
    }

    [Fact]
    public void Leer_ExcepcionDelPaquete_NoEsXlsxSinMensaje()
    {
        var valido = PaqueteAMano(HojaMinima("AB-1"));
        // Control: el paquete base es válido.
        Assert.IsType<LecturaPlanilla.Leida>(_lector.Leer(valido));

        var truncado = valido[..(valido.Length / 2)];
        var corrupto = valido.ToArray();
        for (var i = 40; i < corrupto.Length - 40; i += 7)
        {
            corrupto[i] ^= 0x5A;
        }

        var hojaMalFormada = PaqueteAMano(Prologo + $"<worksheet xmlns=\"{Ns}\"><sheetData><row r=\"2\">");
        var libroSinHojas = PaqueteAMano(HojaMinima(), libro: $"{Prologo}<workbook xmlns=\"{Ns}\"><sheets/></workbook>");
        var hojaSinParte = PaqueteAMano(HojaMinima(), libro: LibroXml("rId9"));
        var compartidaFueraDeRango = PaqueteAMano(
            HojaMinima(codigoCompartido: 5), compartidas: $"{Prologo}<sst xmlns=\"{Ns}\"><si><t>a</t></si></sst>");
        var compartidaSinTabla = PaqueteAMano(HojaMinima(codigoCompartido: 0));
        var filaNoNumerica = PaqueteAMano(Hoja("<row r=\"dos\"><c r=\"A2\" t=\"inlineStr\"><is><t>AB</t></is></c></row>"));
        var referenciaInvalida = PaqueteAMano(Hoja("<row r=\"2\"><c r=\"2A\" t=\"inlineStr\"><is><t>AB</t></is></c></row>"));
        var sinTiposDeContenido = Zip(new Dictionary<string, string>
        {
            ["_rels/.rels"] = RelacionesRaiz,
            ["xl/workbook.xml"] = LibroXml(),
            ["xl/_rels/workbook.xml.rels"] = RelacionesLibro,
            ["xl/worksheets/sheet1.xml"] = HojaMinima(),
        });
        // Una relación interna con una URI absoluta: System.IO.Packaging lanza ArgumentException.
        var destinoAbsoluto = Zip(new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = TiposDeContenido,
            ["_rels/.rels"] = RelacionesRaiz,
            ["xl/workbook.xml"] = LibroXml(),
            ["xl/_rels/workbook.xml.rels"] = RelacionesLibro.Replace(
                "worksheets/sheet1.xml", "file:///etc/passwd", StringComparison.Ordinal),
            ["xl/worksheets/sheet1.xml"] = HojaMinima(),
        });

        foreach (var archivo in new[]
            {
                truncado, corrupto, hojaMalFormada, libroSinHojas, hojaSinParte, compartidaFueraDeRango,
                compartidaSinTabla, filaNoNumerica, referenciaInvalida, sinTiposDeContenido, destinoAbsoluto,
            })
        {
            Assert.Same(LecturaPlanilla.NoEsXlsx.Instancia, LeerOExcepcion(_lector, archivo));
        }

        // D-17, R-05: el rechazo no tiene dónde llevar un mensaje, una causa ni el contenido de una celda.
        var propiedades = typeof(LecturaPlanilla.NoEsXlsx)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name);
        Assert.Empty(propiedades);
        Assert.Equal("NoEsXlsx { }", LecturaPlanilla.NoEsXlsx.Instancia.ToString());
    }

    [Fact]
    public void Leer_BombaEnTiposDeContenidoYRelaciones_RechazadoSinAgotarMemoria()
    {
        // MaxCharactersInPart no cubre [Content_Types].xml ni los .rels, que System.IO.Packaging lee al abrir: los acota
        // el recorrido previo del ZIP (D-20). Un atributo de 200 millones de caracteres en cada uno.
        const int Caracteres = 200_000_000;
        var tiposPrefijo = TiposDeContenido[..^"</Types>".Length] +
            "<Default Extension=\"zz\" ContentType=\"application/x-";
        const string TiposSufijo = "\"/></Types>";
        var relacionesPrefijo = RelacionesRaiz[..^"</Relationships>".Length] +
            "<Relationship Id=\"rId9\" Type=\"http://example.invalid/x\" TargetMode=\"External\" " +
            "Target=\"http://example.invalid/";
        const string RelacionesSufijo = "\"/></Relationships>";

        // Control: los mismos paquetes con un atributo corto se leen.
        Assert.IsType<LecturaPlanilla.Leida>(
            LeerOExcepcion(_lector, PaqueteConParte("[Content_Types].xml", tiposPrefijo, "a", 10, TiposSufijo)));
        Assert.IsType<LecturaPlanilla.Leida>(
            LeerOExcepcion(_lector, PaqueteConParte("_rels/.rels", relacionesPrefijo, "a", 10, RelacionesSufijo)));

        foreach (var (nombre, bomba) in new[]
            {
                ("[Content_Types].xml", PaqueteConParte("[Content_Types].xml", tiposPrefijo, "a", Caracteres, TiposSufijo)),
                ("_rels/.rels", PaqueteConParte("_rels/.rels", relacionesPrefijo, "a", Caracteres, RelacionesSufijo)),
            })
        {
            Assert.True(bomba.Length < 10 * 1024 * 1024, $"El ZIP de la prueba mide {bomba.Length} bytes.");
            var (lectura, asignados) = Medir(_lector, bomba);
            _salida.WriteLine($"Bomba en {nombre}: ZIP de {bomba.Length} bytes, {asignados} bytes asignados.");

            // Pasa el tope de bytes por entrada del ZIP: es un límite, no un archivo roto.
            Assert.Same(LecturaPlanilla.ExcedeLimites.Instancia, lectura);
            Assert.True(asignados < 1_000_000, $"La lectura asignó {asignados} bytes.");
        }
    }

    [Fact]
    public void Leer_PaqueteFueraDeLosTopesDelZip_ExcedeLimites()
    {
        // D-20: más de 100 entradas, una entrada de más de 16 MiB descomprimidos o más de 32 MiB en total, aunque las
        // entradas no sean partes que el lector use. Pasar un tope es "excede los límites", no "no es .xlsx".
        const int MiB = 1024 * 1024;

        // Controles: 100 entradas, una entrada de 16 MiB justos y 28 MiB en total se leen.
        Assert.IsType<LecturaPlanilla.Leida>(LeerOExcepcion(_lector, PaqueteConEntradasExtra(100 - 6, 1)));
        Assert.IsType<LecturaPlanilla.Leida>(LeerOExcepcion(_lector, PaqueteConEntradasExtra(1, 16 * MiB)));
        Assert.IsType<LecturaPlanilla.Leida>(LeerOExcepcion(_lector, PaqueteConEntradasExtra(2, 14 * MiB)));

        foreach (var archivo in new[]
            {
                PaqueteConEntradasExtra(101 - 6, 1),
                PaqueteConEntradasExtra(1, (16 * MiB) + 1),
                PaqueteConEntradasExtra(3, 11 * MiB),
            })
        {
            var (lectura, asignados) = Medir(_lector, archivo);
            _salida.WriteLine($"Fuera de los topes del ZIP: {archivo.Length} bytes, {asignados} bytes asignados.");
            Assert.Same(LecturaPlanilla.ExcedeLimites.Instancia, lectura);
            Assert.True(asignados < 1_000_000, $"La lectura asignó {asignados} bytes.");
        }
    }

    [Fact]
    public void Leer_PaqueteZip64ConPocasEntradas_SeLee()
    {
        var valido = PaqueteAMano(HojaMinima("AB-1"));

        // Un paquete válido reescrito con registro final y localizador ZIP64: se lee como el original.
        var zip64 = ComoZip64(valido);
        var (lectura, asignados) = Medir(_lector, zip64);
        _salida.WriteLine($"Paquete ZIP64: {zip64.Length} bytes, {asignados} bytes asignados.");
        Assert.Equal(
            new CeldaPlanilla.Texto("AB-1"),
            Assert.Single(Leida(Assert.IsAssignableFrom<LecturaPlanilla>(lectura)).Filas).Codigo);

        // ZIP64 que declara 200 entradas: excede. Registro ZIP64 fuera del archivo o con otra firma: no es .xlsx.
        Assert.Same(LecturaPlanilla.ExcedeLimites.Instancia, LeerOExcepcion(_lector, ComoZip64(valido, entradas: 200)));
        foreach (var archivo in new[]
            {
                ComoZip64(valido, desplazamientoRegistro: (ulong)valido.Length * 4),
                ComoZip64(valido, desplazamientoRegistro: ulong.MaxValue),
                ComoZip64(valido, firmaRegistro: 0x06054b50),
                ComoZip64(valido, desplazamientoDirectorio: (ulong)valido.Length * 4),
            })
        {
            Assert.Same(LecturaPlanilla.NoEsXlsx.Instancia, LeerOExcepcion(_lector, archivo));
        }
    }

    [Fact]
    public void Leer_MillonesDeCeldasVacias_RechazadoSinAgotarMemoria()
    {
        // Un <c/> pesa 4 bytes: cada uno no puede costar cargar un elemento. Tope de 16.384 celdas por fila (el del
        // formato) y de 1.000.000 de <c> recorridos en la hoja.
        static string Celdas(int cantidad) => string.Concat(Enumerable.Repeat("<c/>", cantidad));
        var cabecera = Hoja(string.Empty)[..^"</sheetData></worksheet>".Length];
        const string Cierre = "</sheetData></worksheet>";

        // Control: 60 filas de 16.384 <c/> (983.040, debajo del tope total) y una fila de datos se leen.
        var filasEnElLimite = new StringBuilder(cabecera).Append(FilaXml(2, "AB-1"));
        for (var fila = 3; fila < 63; fila++)
        {
            filasEnElLimite.Append($"<row r=\"{fila}\">").Append(Celdas(16_384)).Append("</row>");
        }

        var (control, asignadosControl) = Medir(_lector, PaqueteAMano(filasEnElLimite.Append(Cierre).ToString()));
        _salida.WriteLine($"983.040 <c/> en 60 filas: {asignadosControl} bytes asignados.");
        Assert.Equal(new CeldaPlanilla.Texto("AB-1"), Assert.Single(Leida(Assert.IsAssignableFrom<LecturaPlanilla>(control)).Filas).Codigo);

        // 3 millones de <c/> en una fila: pasa el tope del formato, no es .xlsx.
        var enUnaFila = PaqueteConParte(
            "xl/worksheets/sheet1.xml", cabecera + "<row r=\"2\">", "<c/>", 3_000_000, "</row>" + Cierre);
        // 3 millones de <c/> fuera de toda fila, y 100 filas de 12.000: pasan el tope de <c> recorridos.
        var fueraDeFilas = PaqueteConParte(
            "xl/worksheets/sheet1.xml", cabecera + FilaXml(2, "AB-1"), "<c/>", 3_000_000, Cierre);
        var muchasFilas = PaqueteConParte(
            "xl/worksheets/sheet1.xml", cabecera, "<row>" + Celdas(12_000) + "</row>", 100, Cierre);

        foreach (var (nombre, archivo, esperado) in new (string, byte[], LecturaPlanilla)[]
            {
                ("3.000.000 de <c/> en una fila", enUnaFila, LecturaPlanilla.NoEsXlsx.Instancia),
                ("3.000.000 de <c/> fuera de filas", fueraDeFilas, LecturaPlanilla.ExcedeLimites.Instancia),
                ("100 filas de 12.000 <c/>", muchasFilas, LecturaPlanilla.ExcedeLimites.Instancia),
            })
        {
            var cronometro = System.Diagnostics.Stopwatch.StartNew();
            var (lectura, asignados) = Medir(_lector, archivo);
            _salida.WriteLine(
                $"{nombre}: ZIP de {archivo.Length} bytes, {asignados} bytes asignados, {cronometro.ElapsedMilliseconds} ms.");
            Assert.Same(esperado, lectura);
            // Asignación transitoria del lector SAX del SDK (unos 180 bytes por <c/>), acotada por el tope de 1.000.000.
            Assert.True(asignados < 250_000_000, $"La lectura asignó {asignados} bytes.");
        }
    }

    [Fact]
    public void Leer_ElementoConMillonesDeAtributos_ExcedeLimitesEnPocoTiempo()
    {
        // El costo de XmlReader crece con el cuadrado de los atributos de un elemento: un tag de más de 8 KB se corta
        // en el recorrido de bytes del ZIP, antes de llegar al XML (D-20).
        const string NsRelacionesPaquete = "http://schemas.openxmlformats.org/package/2006/relationships";
        var cabecera = Hoja(string.Empty)[..^CierreHoja.Length];
        var finRaiz = RelacionesRaiz.IndexOf('>', Prologo.Length);
        var raizRelaciones = RelacionesRaiz[..finRaiz];
        var restoRelaciones = RelacionesRaiz[finRaiz..];
        Assert.StartsWith($"{Prologo}<Relationships xmlns=\"{NsRelacionesPaquete}\"", raizRelaciones, StringComparison.Ordinal);

        byte[] FilaConAtributos(int cantidad) => PaqueteConParteEscrita("xl/worksheets/sheet1.xml", e =>
        {
            e.Write(cabecera + "<row r=\"2\"");
            for (var i = 0; i < cantidad; i++)
            {
                e.Write($" a{i}=\"1\"");
            }

            e.Write("><c r=\"A2\" t=\"inlineStr\"><is><t>AB-1</t></is></c></row>" + CierreHoja);
        });

        byte[] RelacionesConDeclaraciones(int cantidad) => PaqueteConParteEscrita("_rels/.rels", e =>
        {
            e.Write(raizRelaciones);
            for (var i = 0; i < cantidad; i++)
            {
                e.Write($" xmlns:p{i}=\"u\"");
            }

            e.Write(restoRelaciones);
        });

        // Controles: 100 atributos en una fila y 10 declaraciones en el .rels se leen.
        Assert.Equal(
            new CeldaPlanilla.Texto("AB-1"),
            Assert.Single(Leida(Assert.IsAssignableFrom<LecturaPlanilla>(LeerOExcepcion(_lector, FilaConAtributos(100)))).Filas).Codigo);
        Assert.IsType<LecturaPlanilla.Leida>(LeerOExcepcion(_lector, RelacionesConDeclaraciones(10)));

        foreach (var (nombre, archivo) in new[]
            {
                ("<row> con 1.000.000 de atributos", FilaConAtributos(1_000_000)),
                ("_rels/.rels con 700.000 atributos", RelacionesConDeclaraciones(700_000)),
            })
        {
            var cronometro = System.Diagnostics.Stopwatch.StartNew();
            var (lectura, asignados) = Medir(_lector, archivo);
            cronometro.Stop();
            _salida.WriteLine($"{nombre}: ZIP de {archivo.Length} bytes, {asignados} bytes asignados, " +
                $"{cronometro.ElapsedMilliseconds} ms.");

            Assert.Same(LecturaPlanilla.ExcedeLimites.Instancia, lectura);
            Assert.True(cronometro.ElapsedMilliseconds < 1_000, $"La lectura tardó {cronometro.ElapsedMilliseconds} ms.");
            Assert.True(asignados < 4_000_000, $"La lectura asignó {asignados} bytes.");
        }
    }

    [Fact]
    public void Leer_AnidamientoProfundo_ExcedeLimitesSinAgotarMemoria()
    {
        // Sin tope de anidamiento, el XmlReader guarda un nodo por nivel: 2 millones de <x> en un ZIP de pocos KB. El
        // recorrido de bytes del ZIP corta en 64 niveles, también en UTF-16 (D-20).
        var cabecera = Hoja(string.Empty)[..^CierreHoja.Length];
        const string PrologoUtf16 = "<?xml version=\"1.0\" encoding=\"UTF-16\" standalone=\"yes\"?>";

        byte[] Anidado(string parte, string antes, int niveles, string despues, Encoding? codificacion = null) =>
            PaqueteConParteEscrita(
                parte,
                e =>
                {
                    e.Write(antes);
                    EscribirRepetida(e, "<x>", niveles);
                    EscribirRepetida(e, "</x>", niveles);
                    e.Write(despues);
                },
                codificacion);

        var celdaAntes = cabecera + "<row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is>";
        const string CeldaDespues = "<t>AB-1</t></is></c></row>" + CierreHoja;
        var compartidasAntes = $"{Prologo}<sst xmlns=\"{Ns}\"><si>";
        const string CompartidasDespues = "<t>AB-1</t></si></sst>";
        var utf16Antes = PrologoUtf16 + celdaAntes[Prologo.Length..];
        var utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);

        // Controles: 50 niveles en cada lugar se leen, con el código "AB-1".
        foreach (var control in new[]
            {
                Anidado("xl/worksheets/sheet1.xml", cabecera + FilaXml(2, "AB-1"), 50, CierreHoja),
                Anidado("xl/worksheets/sheet1.xml", celdaAntes, 50, CeldaDespues),
                Anidado("xl/sharedStrings.xml", compartidasAntes, 50, CompartidasDespues),
                Anidado("xl/worksheets/sheet1.xml", utf16Antes, 50, CeldaDespues, utf16),
            })
        {
            var lectura = Assert.IsAssignableFrom<LecturaPlanilla>(LeerOExcepcion(_lector, control));
            Assert.Equal(new CeldaPlanilla.Texto("AB-1"), Assert.Single(Leida(lectura).Filas).Codigo);
        }

        foreach (var (nombre, archivo) in new[]
            {
                ("2.000.000 de niveles en sheetData",
                    Anidado("xl/worksheets/sheet1.xml", cabecera, 2_000_000, CierreHoja)),
                ("2.000.000 de niveles en una celda",
                    Anidado("xl/worksheets/sheet1.xml", celdaAntes, 2_000_000, CeldaDespues)),
                ("2.000.000 de niveles en sharedStrings",
                    Anidado("xl/sharedStrings.xml", compartidasAntes, 2_000_000, CompartidasDespues)),
                ("1.000.000 de niveles en una celda, UTF-16",
                    Anidado("xl/worksheets/sheet1.xml", utf16Antes, 1_000_000, CeldaDespues, utf16)),
            })
        {
            var (lectura, asignados) = Medir(_lector, archivo);
            _salida.WriteLine($"{nombre}: ZIP de {archivo.Length} bytes, {asignados} bytes asignados.");
            Assert.Same(LecturaPlanilla.ExcedeLimites.Instancia, lectura);
            Assert.True(asignados < 4_000_000, $"La lectura asignó {asignados} bytes.");
        }
    }

    [Fact]
    public void Leer_HojaEnUcs4DeOrdenInusual_NoEsXlsxEnPocoTiempo()
    {
        // XmlReader decodifica UCS-4 en los órdenes 2143 y 3412, que el recorrido de bytes no sigue: una entrada con un
        // 0x00 en sus primeros 4 bytes que no es UTF-16 ni UTF-32 se rechaza (falla cerrada), aunque traiga una fila con
        // 300.000 atributos que de otro modo llegaría al XML.
        var hoja = Hoja(FilaXml(2, "AB-1"))[Prologo.Length..];
        var cabecera = Hoja(string.Empty)[..^CierreHoja.Length][Prologo.Length..];
        var conAtributos = new StringBuilder(cabecera).Append("<row r=\"2\"");
        for (var i = 0; i < 300_000; i++)
        {
            conAtributos.Append(CultureInfo.InvariantCulture, $" a{i}=\"1\"");
        }

        conAtributos.Append("><c r=\"A2\" t=\"inlineStr\"><is><t>AB-1</t></is></c></row>").Append(CierreHoja);

        // Control: UTF-32 en orden 1234 (big endian), sin y con BOM, se lee.
        foreach (var bom in new[] { false, true })
        {
            var lectura = Assert.IsAssignableFrom<LecturaPlanilla>(LeerOExcepcion(
                _lector, PaqueteConParteEnBytes("xl/worksheets/sheet1.xml", Ucs4(hoja, "1234", bom))));
            Assert.Equal(new CeldaPlanilla.Texto("AB-1"), Assert.Single(Leida(lectura).Filas).Codigo);
        }

        foreach (var orden in new[] { "2143", "3412" })
        {
            foreach (var bom in new[] { false, true })
            {
                foreach (var (caso, texto) in new[] { ("mínima", hoja), ("300.000 atributos", conAtributos.ToString()) })
                {
                    var archivo = PaqueteConParteEnBytes("xl/worksheets/sheet1.xml", Ucs4(texto, orden, bom));
                    var cronometro = System.Diagnostics.Stopwatch.StartNew();
                    var lectura = LeerOExcepcion(_lector, archivo);
                    cronometro.Stop();
                    _salida.WriteLine($"UCS-4 {orden}{(bom ? " con BOM" : string.Empty)}, {caso}: " +
                        $"{cronometro.ElapsedMilliseconds} ms.");

                    Assert.Same(LecturaPlanilla.NoEsXlsx.Instancia, lectura);
                    Assert.True(cronometro.ElapsedMilliseconds < 1_000, $"La lectura tardó {cronometro.ElapsedMilliseconds} ms.");
                }
            }
        }
    }

    [Fact]
    public void Leer_HojaConCodificacionEbcdic_NoEsXlsx()
    {
        // Sin CodePagesEncodingProvider registrado (ProteccionTests lo vigila), XmlReader no conoce IBM037 y rechaza la
        // parte: el recorrido de bytes, que supone una codificación compatible con ASCII, no queda esquivado.
        var hoja = "<?xml version=\"1.0\" encoding=\"ibm037\"?>" + HojaMinima()[Prologo.Length..];

        // Control: la misma hoja declarada en UTF-8 se lee.
        Assert.IsType<LecturaPlanilla.Leida>(LeerOExcepcion(_lector, PaqueteAMano(HojaMinima())));
        Assert.Same(LecturaPlanilla.NoEsXlsx.Instancia, LeerOExcepcion(_lector, PaqueteAMano(hoja)));
    }

    [Fact]
    public void Leer_MillonesDeFilasRepetidas_ExcedeLimites()
    {
        // WARN-J: hasta 1.048.576 <row> recorridos, las filas del formato, aunque repitan el índice.
        var cabecera = Hoja(string.Empty)[..^CierreHoja.Length] + FilaXml(2, "AB-1");

        var (control, asignadosControl) = Medir(_lector, PaqueteConParte(
            "xl/worksheets/sheet1.xml", cabecera, "<row r=\"2\"/>", 1_000_000, CierreHoja));
        _salida.WriteLine($"1.000.000 de <row r=\"2\"/>: {asignadosControl} bytes asignados.");
        Assert.Equal(
            new CeldaPlanilla.Texto("AB-1"),
            Assert.Single(Leida(Assert.IsAssignableFrom<LecturaPlanilla>(control)).Filas).Codigo);

        var archivo = PaqueteConParte("xl/worksheets/sheet1.xml", cabecera, "<row r=\"2\"/>", 1_300_000, CierreHoja);
        var (lectura, asignados) = Medir(_lector, archivo);
        _salida.WriteLine($"1.300.000 de <row r=\"2\"/>: ZIP de {archivo.Length} bytes, {asignados} bytes asignados.");
        Assert.Same(LecturaPlanilla.ExcedeLimites.Instancia, lectura);
    }

    [Fact]
    public void Leer_MarcadoConComentariosCdataComillasYBinarios_SinFalsosPositivos()
    {
        // El recorrido de bytes no cuenta "<" dentro de comentarios, CDATA ni valores entre comillas, ni ">" ni "/>"
        // entre comillas, y no recorre entradas que no son XML (una imagen con muchos "<").
        var muchosMenores = string.Concat(Enumerable.Repeat("<x", 100));
        var hoja = $"{Prologo}<!-- {muchosMenores} > --><worksheet xmlns=\"{Ns}\" xmlns:o=\"urn:otro\"><sheetData>" +
            "<row r=\"2\" o:a=\"x>/>y\" o:b='z>&gt;'><c r=\"A2\" t=\"inlineStr\"><is>" +
            $"<t><![CDATA[{muchosMenores}]]></t></is></c><c r=\"B2\"><v>10</v></c></row></sheetData></worksheet>";

        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (nombre, contenido) in PartesBase())
            {
                Escribir(zip, nombre, nombre == "xl/worksheets/sheet1.xml" ? hoja : contenido);
            }

            var muchosA = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("<a", 10_000)));
            using (var imagen = zip.CreateEntry("xl/media/image1.png").Open())
            {
                imagen.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
                imagen.Write(muchosA);
            }

            // Binarios con 0x00 al principio, sin "<" ni marca de orden: las opciones de impresión que guarda Excel
            // (el nombre de la impresora en UTF-16) y una imagen EMF. No son XML: no se recorren ni se rechazan.
            using (var impresora = zip.CreateEntry("xl/printerSettings/printerSettings1.bin").Open())
            {
                impresora.Write(Encoding.Unicode.GetBytes("HP LaserJet"));
                impresora.Write(muchosA);
            }

            using var emf = zip.CreateEntry("xl/media/image2.emf").Open();
            emf.Write([0x01, 0x00, 0x00, 0x00]);
            emf.Write(muchosA);
        }

        var lectura = Assert.IsAssignableFrom<LecturaPlanilla>(LeerOExcepcion(_lector, memoria.ToArray()));
        Assert.Equal(new CeldaPlanilla.Texto(muchosMenores), Assert.Single(Leida(lectura).Filas).Codigo);

        // El recorrido por sí solo: dentro de los límites con 64 niveles y un tag de 8.192 bytes; fuera con 65 niveles,
        // con un tag de 8.193 bytes, y con 65 niveles en UTF-16.
        static byte[] Utf8(string texto) => Encoding.UTF8.GetBytes(texto);
        var muchosMenoresBytes = Utf8(string.Concat(Enumerable.Repeat(muchosMenores, 10)));
        var anidado64 = string.Concat(Enumerable.Repeat("<x>", 64)) + string.Concat(Enumerable.Repeat("</x>", 64));
        var anidado65 = string.Concat(Enumerable.Repeat("<x>", 65)) + string.Concat(Enumerable.Repeat("</x>", 65));
        var tag8192 = "<x a=\"" + new string('v', 8192 - 9) + "\"/>";
        var tag8193 = "<x a=\"" + new string('v', 8193 - 9) + "\"/>";
        Assert.Equal(8192, tag8192.Length);

        Assert.True(LectorPlanillaCostosXlsx.MarcadoDentroDeLimites(Utf8(hoja)));
        Assert.True(LectorPlanillaCostosXlsx.MarcadoDentroDeLimites(Utf8(anidado64)));
        Assert.True(LectorPlanillaCostosXlsx.MarcadoDentroDeLimites(Utf8(tag8192)));
        Assert.True(LectorPlanillaCostosXlsx.MarcadoDentroDeLimites(Utf8("<a/><b/>" + string.Concat(Enumerable.Repeat("<c/>", 1000)))));
        Assert.False(LectorPlanillaCostosXlsx.MarcadoDentroDeLimites(Utf8(anidado65)));
        Assert.False(LectorPlanillaCostosXlsx.MarcadoDentroDeLimites(Utf8(tag8193)));
        Assert.False(LectorPlanillaCostosXlsx.MarcadoDentroDeLimites(
            [.. new UnicodeEncoding(false, true).GetPreamble(), .. Encoding.Unicode.GetBytes(anidado65)]));

        // Falla cerrada solo donde XmlReader podría ver XML: 0x00 con "<" o con marca de orden.
        Assert.False(LectorPlanillaCostosXlsx.MarcadoDentroDeLimites([0x00, 0x00, 0x3C, 0x00, 0x00, 0x00, 0x61, 0x00]));
        Assert.False(LectorPlanillaCostosXlsx.MarcadoDentroDeLimites([0xFE, 0xFF, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x00]));
        Assert.True(LectorPlanillaCostosXlsx.MarcadoDentroDeLimites([0x48, 0x00, 0x50, 0x00, .. muchosMenoresBytes]));
        Assert.True(LectorPlanillaCostosXlsx.MarcadoDentroDeLimites([0x01, 0x00, 0x00, 0x00, .. muchosMenoresBytes]));
    }

    [Fact]
    public void Leer_PlanillaGeneradaPorElSdk_SinFalsosPositivos()
    {
        // Un libro real del SDK: estilos, propiedades del documento, cadenas compartidas y 50.000 filas.
        var archivo = LibroSdk(libro =>
        {
            var estilos = libro.AddNewPart<WorkbookStylesPart>();
            var borde = new Border(
                new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder());
            estilos.Stylesheet = new Stylesheet(
                new Fonts(new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" })) { Count = 1 },
                new Fills(new Fill(new PatternFill { PatternType = PatternValues.None })) { Count = 1 },
                new Borders(borde) { Count = 1 },
                new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 2, ApplyNumberFormat = true }) { Count = 2 });

            var compartidas = libro.AddNewPart<SharedStringTablePart>();
            compartidas.SharedStringTable = new SharedStringTable(
                Enumerable.Range(2, 50_000).Select(f => new SharedStringItem(new Text($"PROV-{f:D8}"))));

            var filas = new List<Row> { Fila(1, EnLinea("A1", Encabezado1), EnLinea("B1", Encabezado2)) };
            for (var fila = 2u; fila <= 50_001; fila++)
            {
                filas.Add(Fila(fila, Compartida($"A{fila}", (int)fila - 2), Numerica($"B{fila}", "2420.5")));
            }

            AgregarHoja(libro, "Costos", [.. filas]);
        });

        using (var editable = new MemoryStream())
        {
            editable.Write(archivo);
            using (var documento = SpreadsheetDocument.Open(editable, true))
            {
                var propiedades = documento.AddCoreFilePropertiesPart();
                using var flujo = propiedades.GetStream(FileMode.Create);
                flujo.Write(Encoding.UTF8.GetBytes(
                    $"{Prologo}<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                    "xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:creator>Prueba</dc:creator></cp:coreProperties>"));
            }

            archivo = editable.ToArray();
        }

        var lectura = Assert.IsAssignableFrom<LecturaPlanilla>(LeerOExcepcion(_lector, archivo));
        Assert.Equal(50_000, Leida(lectura).Filas.Count);

        using var zip = new ZipArchive(new MemoryStream(archivo), ZipArchiveMode.Read);
        foreach (var entrada in zip.Entries)
        {
            using var datos = new MemoryStream();
            using (var origen = entrada.Open())
            {
                origen.CopyTo(datos);
            }

            var cronometro = System.Diagnostics.Stopwatch.StartNew();
            var dentro = LectorPlanillaCostosXlsx.MarcadoDentroDeLimites(datos.ToArray());
            cronometro.Stop();
            _salida.WriteLine($"SDK, {entrada.FullName}: {datos.Length} bytes, {cronometro.Elapsed.TotalMilliseconds:F1} ms.");
            Assert.True(dentro, entrada.FullName);
        }
    }

    [Fact]
    public void Leer_PlanillaLegitimaDe50000Filas_EntraEnLosTopesDelZip()
    {
        // Una planilla como la de Excel, del lado pesado: códigos de 50 caracteres, estilos, spans y precios con todos
        // los decimales. Una variante con cadenas compartidas y otra con texto en línea, con prefijo de espacio de
        // nombres. Ninguna entrada puede acercarse al tope de 16 MiB ni el total al de 32 MiB (D-20).
        const string Prefijado = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        string Codigo(int fila) => $"PROV-{fila:D8}-".PadRight(50, 'X');

        var compartidas = new StringBuilder($"{Prologo}<sst xmlns=\"{Ns}\" count=\"50000\" uniqueCount=\"50000\">");
        var hojaCompartida = new StringBuilder($"{Prologo}<worksheet xmlns=\"{Ns}\"><sheetData>" +
            $"<row r=\"1\" spans=\"1:2\"><c r=\"A1\" t=\"inlineStr\"><is><t>{Encabezado1}</t></is></c>" +
            $"<c r=\"B1\" t=\"inlineStr\"><is><t>{Encabezado2}</t></is></c></row>");
        var hojaEnLinea = new StringBuilder($"{Prologo}<x:worksheet xmlns:x=\"{Prefijado}\"><x:sheetData>" +
            $"<x:row r=\"1\" spans=\"1:2\"><x:c r=\"A1\" t=\"inlineStr\"><x:is><x:t>{Encabezado1}</x:t></x:is></x:c>" +
            $"<x:c r=\"B1\" t=\"inlineStr\"><x:is><x:t>{Encabezado2}</x:t></x:is></x:c></x:row>");
        for (var fila = 2; fila <= 50_001; fila++)
        {
            const string Precio = "12345.678901234567";
            compartidas.Append($"<si><t>{Codigo(fila)}</t></si>");
            hojaCompartida.Append($"<row r=\"{fila}\" spans=\"1:2\" x14ac:dyDescent=\"0.25\">")
                .Append($"<c r=\"A{fila}\" s=\"1\" t=\"s\"><v>{fila - 2}</v></c>")
                .Append($"<c r=\"B{fila}\" s=\"2\"><v>{Precio}</v></c></row>");
            hojaEnLinea.Append($"<x:row r=\"{fila}\" spans=\"1:2\">")
                .Append($"<x:c r=\"A{fila}\" s=\"1\" t=\"inlineStr\"><x:is><x:t>{Codigo(fila)}</x:t></x:is></x:c>")
                .Append($"<x:c r=\"B{fila}\" s=\"2\"><x:v>{Precio}</x:v></x:c></x:row>");
        }

        compartidas.Append("</sst>");
        var conCompartidas = hojaCompartida.Append("</sheetData></worksheet>").ToString()
            .Replace("<worksheet ", "<worksheet xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" " +
                "mc:Ignorable=\"x14ac\" xmlns:x14ac=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac\" ",
                StringComparison.Ordinal);
        var enLinea = hojaEnLinea.Append("</x:sheetData></x:worksheet>").ToString();

        foreach (var (nombre, archivo, textos) in new[]
            {
                ("cadenas compartidas", PaqueteAMano(conCompartidas, compartidas.ToString()),
                    new[] { conCompartidas, compartidas.ToString() }),
                ("texto en línea", PaqueteAMano(enLinea), new[] { enLinea }),
            })
        {
            var partes = textos.Select(t => t.Length).ToArray();

            // El recorrido de bytes del marcado (tags de hasta 8 KB, profundidad hasta 64) no da falsos positivos.
            foreach (var texto in textos)
            {
                var bytes = Encoding.UTF8.GetBytes(texto);
                var escaneo = System.Diagnostics.Stopwatch.StartNew();
                var dentro = LectorPlanillaCostosXlsx.MarcadoDentroDeLimites(bytes);
                escaneo.Stop();
                _salida.WriteLine($"50.000 filas, {nombre}: recorrido del marcado de {bytes.Length} bytes en " +
                    $"{escaneo.Elapsed.TotalMilliseconds:F1} ms.");
                Assert.True(dentro);
            }

            var cronometro = System.Diagnostics.Stopwatch.StartNew();
            var lectura = LeerOExcepcion(_lector, archivo);
            cronometro.Stop();
            _salida.WriteLine($"50.000 filas, {nombre}: ZIP de {archivo.Length} bytes; partes de " +
                $"{string.Join(" y ", partes)} bytes ({partes.Sum()} en total); {cronometro.ElapsedMilliseconds} ms.");

            var filas = Leida(Assert.IsAssignableFrom<LecturaPlanilla>(lectura)).Filas;
            Assert.Equal(50_000, filas.Count);
            Assert.Equal(new CeldaPlanilla.Texto(Codigo(50_001)), filas[^1].Codigo);
            Assert.Equal(new CeldaPlanilla.Numero(12345.678901234567m), filas[^1].Precio);
            // Medido: la parte más grande ronda los 10,5 MB (texto en línea con prefijo), debajo de los 16 MiB por entrada
            // con margen; el total ronda los 10,6 MB, debajo de los 32 MiB.
            Assert.All(partes, largo => Assert.True(largo < 12_000_000, $"Una parte mide {largo} bytes."));
            Assert.True(partes.Sum() < 16 * 1024 * 1024, $"Las partes suman {partes.Sum()} bytes.");
        }
    }

    [Fact]
    public void Leer_DirectorioCentralConDemasiadasEntradas_RechazadoSinLeerlo()
    {
        // D-20: la cantidad de entradas se lee del registro final del ZIP (EOCD) antes de crear el ZipArchive, que si no
        // arma un objeto por cada entrada del directorio central antes de que se las pueda contar.

        // Control: un paquete válido con un comentario de ZIP (el EOCD no queda en los últimos 22 bytes) se lee.
        Assert.IsType<LecturaPlanilla.Leida>(LeerOExcepcion(_lector, PaqueteConComentario(new string('c', 60_000))));

        // 200.000 entradas reales y vacías: el ZIP pasa a ZIP64 y su registro final declara 200.000. Excede el tope.
        var muchas = PaqueteConEntradasVacias(200_000);
        var (lectura, asignados) = Medir(_lector, muchas);
        _salida.WriteLine($"200.000 entradas: ZIP de {muchas.Length} bytes, {asignados} bytes asignados.");
        Assert.Same(LecturaPlanilla.ExcedeLimites.Instancia, lectura);
        Assert.True(asignados < 1_000_000, $"La lectura asignó {asignados} bytes.");

        // EOCD sintéticos sobre el paquete válido: 200 entradas declaradas excede; 0xFFFF sin registros ZIP64 y la
        // falta de EOCD (truncado al final) son un ZIP roto.
        var valido = PaqueteAMano(HojaMinima());
        Assert.Same(LecturaPlanilla.ExcedeLimites.Instancia, LeerOExcepcion(_lector, ConEntradasDeclaradas(valido, 200)));
        foreach (var archivo in new[] { ConEntradasDeclaradas(valido, 0xFFFF), valido[..^22] })
        {
            Assert.Same(LecturaPlanilla.NoEsXlsx.Instancia, LeerOExcepcion(_lector, archivo));
        }
    }

    [Fact]
    public void Leer_MillonesDeCadenasCompartidas_ExcedeLimitesSinAgotarMemoria()
    {
        var prefijo = $"{Prologo}<sst xmlns=\"{Ns}\">";
        const string Sufijo = "</sst>";

        // Control: 200.000 cadenas, el límite, se leen.
        var (control, asignadosControl) = Medir(
            _lector, PaqueteConParte("xl/sharedStrings.xml", prefijo, "<si/>", 200_000, Sufijo));
        _salida.WriteLine($"200.000 cadenas compartidas: {asignadosControl} bytes asignados.");
        Assert.IsType<LecturaPlanilla.Leida>(control);

        var bomba = PaqueteConParte("xl/sharedStrings.xml", prefijo, "<si/>", 3_000_000, Sufijo);
        var (lectura, asignados) = Medir(_lector, bomba);
        _salida.WriteLine($"3.000.000 de cadenas compartidas: ZIP de {bomba.Length} bytes, {asignados} bytes asignados.");

        Assert.Same(LecturaPlanilla.ExcedeLimites.Instancia, lectura);
        Assert.True(asignados < 64_000_000, $"La lectura asignó {asignados} bytes.");
    }

    [Fact]
    public void Leer_LibroConNombresDefinidosGrandes_LeeSinCargarElLibro()
    {
        // Los nombres definidos van después de <sheets> en workbook.xml: el lector se detiene en la primera hoja.
        var prefijo = LibroXml()[..^"</workbook>".Length] + "<definedNames>";
        const string Unidad = "<definedName name=\"n\">Costos!$A$1</definedName>";
        const string Sufijo = "</definedNames></workbook>";

        // Control: con pocos nombres se lee.
        Assert.IsType<LecturaPlanilla.Leida>(
            LeerOExcepcion(_lector, PaqueteConParte("xl/workbook.xml", prefijo, Unidad, 10, Sufijo)));

        // 330.000 nombres: unos 15,5 MB, debajo del tope de 16 MiB por entrada del ZIP.
        var grande = PaqueteConParte("xl/workbook.xml", prefijo, Unidad, 330_000, Sufijo);
        var (lectura, asignados) = Medir(_lector, grande);
        _salida.WriteLine($"workbook.xml con 330.000 nombres: ZIP de {grande.Length} bytes, {asignados} bytes asignados.");

        Assert.Equal(
            new CeldaPlanilla.Texto("AB-1"),
            Assert.Single(Leida(Assert.IsAssignableFrom<LecturaPlanilla>(lectura)).Filas).Codigo);
        Assert.True(asignados < 32_000_000, $"La lectura asignó {asignados} bytes.");
    }

    [Fact]
    public void Leer_IndiceDeFilaFueraDeRango_NoEsXlsx()
    {
        // Control: la última fila de una hoja de Excel (1.048.576), sin datos, se lee.
        Assert.IsType<LecturaPlanilla.Leida>(LeerOExcepcion(_lector, PaqueteAMano(Hoja(
            FilaXml(2, "AB-1") + "<row r=\"1048576\"><c r=\"A1048576\" s=\"1\"/></row>"))));

        foreach (var archivo in new[]
            {
                PaqueteAMano(Hoja(FilaXml(2, "AB-1") + FilaXml(1_048_577, "AB-2"))),
                PaqueteAMano(Hoja(FilaXml(2, "AB-1") + "<row r=\"5000000000\"><c r=\"A5000000000\"><v>1</v></c></row>")),
                PaqueteAMano(Hoja(FilaXml(2, "AB-1") + "<row r=\"2000000\"/>")),
                PaqueteAMano(Hoja(FilaXml(2, "AB-1") + "<row r=\"0\"/>")),
                // Sin r, la fila siguiente a la 1.048.576 se pasa del formato.
                PaqueteAMano(Hoja(FilaXml(2, "AB-1") + "<row r=\"1048576\"/><row><c><v>1</v></c></row>")),
            })
        {
            Assert.Same(LecturaPlanilla.NoEsXlsx.Instancia, LeerOExcepcion(_lector, archivo));
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Auxiliares: medición

    /// <summary>La lectura, o el nombre de la excepción si se escapó: así una excepción falla una aserción.</summary>
    private static object LeerOExcepcion(LectorPlanillaCostosXlsx lector, byte[] archivo)
    {
        try
        {
            return lector.Leer(archivo);
        }
        catch (Exception error)
        {
            return $"lanzó {error.GetType().Name}";
        }
    }

    private static (object Resultado, long Asignados) Medir(LectorPlanillaCostosXlsx lector, byte[] archivo)
    {
        var antes = GC.GetAllocatedBytesForCurrentThread();
        var resultado = LeerOExcepcion(lector, archivo);
        return (resultado, GC.GetAllocatedBytesForCurrentThread() - antes);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Auxiliares: libros con el SDK

    private static LecturaPlanilla.Leida LeidaDe(LecturaPlanilla lectura) => Assert.IsType<LecturaPlanilla.Leida>(lectura);

    private static ContenidoPlanilla Leida(LecturaPlanilla lectura) => LeidaDe(lectura).Contenido;

    private static byte[] LibroSdk(Action<WorkbookPart> armar)
    {
        using var memoria = new MemoryStream();
        using (var documento = SpreadsheetDocument.Create(memoria, SpreadsheetDocumentType.Workbook))
        {
            var libro = documento.AddWorkbookPart();
            libro.Workbook = new Workbook(new Sheets());
            armar(libro);
        }

        return memoria.ToArray();
    }

    private static WorksheetPart CrearParteHoja(WorkbookPart libro, params Row[] filas)
    {
        var hoja = libro.AddNewPart<WorksheetPart>();
        hoja.Worksheet = new Worksheet(new SheetData(filas));
        return hoja;
    }

    private static void AgregarHoja(WorkbookPart libro, string nombre, params Row[] filas)
    {
        var hoja = CrearParteHoja(libro, filas);
        var hojas = libro.Workbook.Sheets!;
        hojas.Append(new Sheet { Id = libro.GetIdOfPart(hoja), SheetId = (uint)hojas.Count() + 1, Name = nombre });
    }

    private static Row Fila(uint numero, params Cell[] celdas) => new(celdas) { RowIndex = numero };

    private static Cell Numerica(string referencia, string valor) =>
        new() { CellReference = referencia, CellValue = new CellValue(valor) };

    private static Cell Tipada(string referencia, CellValues tipo, string valor) =>
        new() { CellReference = referencia, DataType = tipo, CellValue = new CellValue(valor) };

    private static Cell Compartida(string referencia, int indice) =>
        Tipada(referencia, CellValues.SharedString, indice.ToString(CultureInfo.InvariantCulture));

    private static Cell EnLinea(string referencia, string texto) => new()
    {
        CellReference = referencia,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(texto)),
    };

    private static Cell ConFormato(string referencia) => new() { CellReference = referencia, StyleIndex = 1 };

    // ---------------------------------------------------------------------------------------------------------------
    // Auxiliares: paquetes armados a mano

    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Prologo = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
    private const string CierreHoja = "</sheetData></worksheet>";

    private const string TiposDeContenido = Prologo +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" " +
        "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" " +
        "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/sharedStrings.xml\" " +
        "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>" +
        "</Types>";

    private const string RelacionesRaiz = Prologo +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"" + NsRel + "/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private const string RelacionesLibro = Prologo +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"" + NsRel + "/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "</Relationships>";

    private const string RelacionesLibroConCompartidas = Prologo +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"" + NsRel + "/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"" + NsRel + "/sharedStrings\" Target=\"sharedStrings.xml\"/>" +
        "</Relationships>";

    private static string LibroXml(string idHoja = "rId1") =>
        $"{Prologo}<workbook xmlns=\"{Ns}\" xmlns:r=\"{NsRel}\"><sheets>" +
        $"<sheet name=\"Costos\" sheetId=\"1\" r:id=\"{idHoja}\"/></sheets></workbook>";

    private static string Hoja(string filasDeDatos) =>
        $"{Prologo}<worksheet xmlns=\"{Ns}\"><sheetData>" +
        $"<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>{Encabezado1}</t></is></c>" +
        $"<c r=\"B1\" t=\"inlineStr\"><is><t>{Encabezado2}</t></is></c></row>" +
        filasDeDatos + "</sheetData></worksheet>";

    private static string FilaXml(int numero, string codigo) =>
        $"<row r=\"{numero}\"><c r=\"A{numero}\" t=\"inlineStr\"><is><t>{codigo}</t></is></c>" +
        $"<c r=\"B{numero}\"><v>10</v></c></row>";

    private static string HojaMinima(string codigo = "AB-1") => Hoja(FilaXml(2, codigo));

    private static string HojaMinima(int codigoCompartido) =>
        Hoja($"<row r=\"2\"><c r=\"A2\" t=\"s\"><v>{codigoCompartido}</v></c><c r=\"B2\"><v>10</v></c></row>");

    private static byte[] PaqueteAMano(string hoja, string? compartidas = null, string? libro = null)
    {
        var partes = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = TiposDeContenido,
            ["_rels/.rels"] = RelacionesRaiz,
            ["xl/workbook.xml"] = libro ?? LibroXml(),
            ["xl/_rels/workbook.xml.rels"] = compartidas is null ? RelacionesLibro : RelacionesLibroConCompartidas,
            ["xl/worksheets/sheet1.xml"] = hoja,
        };
        if (compartidas is not null)
        {
            partes["xl/sharedStrings.xml"] = compartidas;
        }

        return Zip(partes);
    }

    private static byte[] Zip(IReadOnlyDictionary<string, string> partes)
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (nombre, contenido) in partes)
            {
                using var escritor = new StreamWriter(zip.CreateEntry(nombre).Open(), new UTF8Encoding(false));
                escritor.Write(contenido);
            }
        }

        return memoria.ToArray();
    }

    /// <summary>
    /// Un paquete válido cuya hoja (o tabla de cadenas compartidas) lleva <paramref name="espacios"/> espacios entre
    /// elementos: se comprime a casi nada y descomprime a eso.
    /// </summary>
    private static byte[] PaqueteConRelleno(string parte, int espacios)
    {
        var enHoja = parte == "xl/worksheets/sheet1.xml";
        var hoja = enHoja ? HojaMinima() : HojaMinima(codigoCompartido: 0);
        var compartidas = $"{Prologo}<sst xmlns=\"{Ns}\"><si><t>AB-1</t></si></sst>";
        var conRelleno = enHoja ? hoja : compartidas;
        var corte = conRelleno.LastIndexOf("</", StringComparison.Ordinal);

        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            Escribir(zip, "[Content_Types].xml", TiposDeContenido);
            Escribir(zip, "_rels/.rels", RelacionesRaiz);
            Escribir(zip, "xl/workbook.xml", LibroXml());
            Escribir(zip, "xl/_rels/workbook.xml.rels", RelacionesLibroConCompartidas);
            Escribir(zip, enHoja ? "xl/sharedStrings.xml" : "xl/worksheets/sheet1.xml", enHoja ? compartidas : hoja);

            using var escritor = new StreamWriter(zip.CreateEntry(parte).Open(), new UTF8Encoding(false));
            escritor.Write(conRelleno[..corte]);
            var bloque = new string(' ', 64 * 1024);
            for (var restantes = espacios; restantes > 0; restantes -= bloque.Length)
            {
                escritor.Write(bloque.AsSpan(0, Math.Min(bloque.Length, restantes)));
            }

            escritor.Write(conRelleno[corte..]);
        }

        return memoria.ToArray();
    }

    /// <summary>Las seis entradas de un paquete válido cuya celda A2 es la cadena compartida 0 ("AB-1").</summary>
    private static Dictionary<string, string> PartesBase() => new()
    {
        ["[Content_Types].xml"] = TiposDeContenido,
        ["_rels/.rels"] = RelacionesRaiz,
        ["xl/workbook.xml"] = LibroXml(),
        ["xl/_rels/workbook.xml.rels"] = RelacionesLibroConCompartidas,
        ["xl/worksheets/sheet1.xml"] = HojaMinima(codigoCompartido: 0),
        ["xl/sharedStrings.xml"] = $"{Prologo}<sst xmlns=\"{Ns}\"><si><t>AB-1</t></si></sst>",
    };

    /// <summary>
    /// El paquete base con <paramref name="parte"/> reemplazada por <paramref name="prefijo"/>, <paramref name="unidad"/>
    /// repetida <paramref name="veces"/> veces y <paramref name="sufijo"/>, escrita por bloques sin armar el texto entero.
    /// </summary>
    private static byte[] PaqueteConParte(string parte, string prefijo, string unidad, int veces, string sufijo) =>
        PaqueteConParteEscrita(parte, escritor =>
        {
            escritor.Write(prefijo);
            EscribirRepetida(escritor, unidad, veces);
            escritor.Write(sufijo);
        });

    /// <summary>
    /// El paquete base con <paramref name="parte"/> reemplazada por lo que escribe <paramref name="escribir"/>, en
    /// <paramref name="codificacion"/> (UTF-8 sin BOM si es null), sin armar el texto entero en memoria.
    /// </summary>
    private static byte[] PaqueteConParteEscrita(string parte, Action<TextWriter> escribir, Encoding? codificacion = null)
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (nombre, contenido) in PartesBase())
            {
                if (nombre != parte)
                {
                    Escribir(zip, nombre, contenido);
                    continue;
                }

                using var escritor = new StreamWriter(zip.CreateEntry(nombre).Open(), codificacion ?? new UTF8Encoding(false));
                escribir(escritor);
            }
        }

        return memoria.ToArray();
    }

    /// <summary>El paquete base con <paramref name="parte"/> reemplazada por <paramref name="contenido"/> tal cual.</summary>
    private static byte[] PaqueteConParteEnBytes(string parte, byte[] contenido)
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (nombre, texto) in PartesBase())
            {
                if (nombre != parte)
                {
                    Escribir(zip, nombre, texto);
                    continue;
                }

                using var destino = zip.CreateEntry(nombre).Open();
                destino.Write(contenido);
            }
        }

        return memoria.ToArray();
    }

    /// <summary>
    /// <paramref name="texto"/> en UCS-4 con los bytes de cada carácter en el <paramref name="orden"/> dado ("1234" es
    /// big endian; "2143" y "3412" son los órdenes inusuales), con la marca U+FEFF al principio si <paramref name="bom"/>.
    /// </summary>
    private static byte[] Ucs4(string texto, string orden, bool bom)
    {
        var grande = new UTF32Encoding(bigEndian: true, byteOrderMark: false).GetBytes((bom ? "﻿" : string.Empty) + texto);
        var salida = new byte[grande.Length];
        for (var i = 0; i < grande.Length; i += 4)
        {
            for (var j = 0; j < 4; j++)
            {
                salida[i + j] = grande[i + orden[j] - '1'];
            }
        }

        return salida;
    }

    private static void EscribirRepetida(TextWriter escritor, string unidad, int veces)
    {
        var porBloque = Math.Max(1, 64 * 1024 / unidad.Length);
        var bloque = string.Concat(Enumerable.Repeat(unidad, porBloque));
        var restantes = veces;
        for (; restantes >= porBloque; restantes -= porBloque)
        {
            escritor.Write(bloque);
        }

        escritor.Write(bloque.AsSpan(0, restantes * unidad.Length));
    }

    /// <summary>El paquete base (seis entradas) más <paramref name="cantidad"/> entradas de ceros que nadie usa.</summary>
    private static byte[] PaqueteConEntradasExtra(int cantidad, int bytesPorEntrada)
    {
        var ceros = new byte[64 * 1024];
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (nombre, contenido) in PartesBase())
            {
                Escribir(zip, nombre, contenido);
            }

            for (var i = 0; i < cantidad; i++)
            {
                using var destino = zip.CreateEntry($"xl/extra/relleno{i}.bin").Open();
                for (var restantes = bytesPorEntrada; restantes > 0; restantes -= ceros.Length)
                {
                    destino.Write(ceros, 0, Math.Min(ceros.Length, restantes));
                }
            }
        }

        return memoria.ToArray();
    }

    /// <summary>El paquete base con un comentario de ZIP, que va después del EOCD.</summary>
    private static byte[] PaqueteConComentario(string comentario)
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.Comment = comentario;
            foreach (var (nombre, contenido) in PartesBase())
            {
                Escribir(zip, nombre, contenido);
            }
        }

        return memoria.ToArray();
    }

    /// <summary>El paquete base más <paramref name="cantidad"/> entradas vacías, sin comprimir.</summary>
    private static byte[] PaqueteConEntradasVacias(int cantidad)
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (nombre, contenido) in PartesBase())
            {
                Escribir(zip, nombre, contenido);
            }

            for (var i = 0; i < cantidad; i++)
            {
                zip.CreateEntry(i.ToString("x", CultureInfo.InvariantCulture), CompressionLevel.NoCompression);
            }
        }

        return memoria.ToArray();
    }

    /// <summary>
    /// Una copia de un ZIP sin comentario cuyo EOCD (los últimos 22 bytes) declara <paramref name="cantidad"/> entradas,
    /// en este disco y en total.
    /// </summary>
    private static byte[] ConEntradasDeclaradas(byte[] zip, ushort cantidad)
    {
        var copia = zip.ToArray();
        var eocd = copia.Length - 22;
        Assert.Equal(new byte[] { 0x50, 0x4B, 0x05, 0x06 }, copia[eocd..(eocd + 4)]);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(copia.AsSpan(eocd + 8), cantidad);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(copia.AsSpan(eocd + 10), cantidad);
        return copia;
    }

    /// <summary>
    /// Un ZIP sin comentario reescrito en ZIP64: el contenido y el directorio central quedan iguales; después van el
    /// registro final ZIP64 (firma 0x06064b50, con las cantidades y el directorio en campos de 64 bits), el localizador
    /// ZIP64 (0x07064b50) y un EOCD con las marcas 0xFFFF y 0xFFFFFFFF. Los parámetros opcionales lo arruinan a propósito.
    /// </summary>
    private static byte[] ComoZip64(
        byte[] zip, ulong? entradas = null, ulong? desplazamientoRegistro = null, uint firmaRegistro = 0x06064b50,
        ulong? desplazamientoDirectorio = null)
    {
        var eocd = zip.Length - 22;
        Assert.Equal(0x06054b50u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(eocd)));
        var cantidad = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(eocd + 10));
        var tamanoDirectorio = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(eocd + 12));
        var inicioDirectorio = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(eocd + 16));

        using var salida = new MemoryStream();
        using var escritor = new BinaryWriter(salida);
        escritor.Write(zip, 0, eocd);

        // Registro final ZIP64 (56 bytes).
        var registro = (ulong)salida.Position;
        escritor.Write(firmaRegistro);
        escritor.Write(44UL);
        escritor.Write((ushort)45);
        escritor.Write((ushort)45);
        escritor.Write(0u);
        escritor.Write(0u);
        escritor.Write(entradas ?? cantidad);
        escritor.Write(entradas ?? cantidad);
        escritor.Write((ulong)tamanoDirectorio);
        escritor.Write(desplazamientoDirectorio ?? inicioDirectorio);

        // Localizador ZIP64 (20 bytes).
        escritor.Write(0x07064b50u);
        escritor.Write(0u);
        escritor.Write(desplazamientoRegistro ?? registro);
        escritor.Write(1u);

        // EOCD con las marcas de ZIP64.
        escritor.Write(0x06054b50u);
        escritor.Write((ushort)0);
        escritor.Write((ushort)0);
        escritor.Write(ushort.MaxValue);
        escritor.Write(ushort.MaxValue);
        escritor.Write(uint.MaxValue);
        escritor.Write(uint.MaxValue);
        escritor.Write((ushort)0);
        escritor.Flush();
        return salida.ToArray();
    }

    private static void Escribir(ZipArchive zip, string nombre, string contenido)
    {
        using var escritor = new StreamWriter(zip.CreateEntry(nombre).Open(), new UTF8Encoding(false));
        escritor.Write(contenido);
    }
}
