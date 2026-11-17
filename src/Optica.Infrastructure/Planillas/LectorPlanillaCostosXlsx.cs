using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Optica.Application.Catalogo;
using Optica.Domain.Catalogo;

namespace Optica.Infrastructure.Planillas;

/// <summary>
/// Lee la planilla de costos .xlsx con <c>DocumentFormat.OpenXml</c> 3.1.1 (ADR-031, D-05 de FEAT-011): la primera hoja
/// según el orden de <c>workbook.xml</c>, en modo SAX con <see cref="OpenXmlReader"/>, con los límites de D-14 y D-20.
/// Solo tipa las celdas (D-07): el formato, los precios y los códigos los decide <see cref="PlanillaCostos"/>.
/// </summary>
/// <remarks>
/// <para><b>Antes de abrir el paquete</b> (D-20): mira el registro final del ZIP (EOCD, y el ZIP64 si lo hay) sin crear
/// el <see cref="ZipArchive"/>, que arma un objeto por entrada del directorio central; después descomprime cada entrada
/// contando los bytes reales con un búfer fijo, sin creer en el tamaño declarado. Más de <see cref="MaxEntradasZip"/>
/// entradas, una de más de <see cref="MaxBytesPorEntrada"/> o más de <see cref="MaxBytesDelPaquete"/> en total es
/// <see cref="LecturaPlanilla.ExcedeLimites"/>; una firma o un EOCD que no son de ZIP, o un ZIP roto, es
/// <see cref="LecturaPlanilla.NoEsXlsx"/>. En esa misma descompresión, cada entrada XML pasa por un recorrido de bytes del
/// marcado (<see cref="EscanerMarcado"/>): un tag de más de <see cref="MaxLargoTag"/> bytes (muchos atributos, con costo
/// cuadrático en XmlReader) o más de <see cref="MaxProfundidad"/> niveles de anidamiento excede los límites. Así quedan
/// acotados también <c>[Content_Types].xml</c> y los <c>.rels</c>, que System.IO.Packaging lee al abrir (R-02). Cuesta
/// una descompresión más, sobre a lo sumo 10 MB.</para>
/// <para><b><see cref="OpenSettings.MaxCharactersInPart"/></b> queda en 100 millones como pide D-14, pero es una segunda
/// barrera, inerte en producción: con <see cref="MaxBytesPorEntrada"/> ninguna parte llega a esa cantidad de caracteres.
/// Solo actúa si se baja el tope por el constructor interno.</para>
/// <para><b>Lectura:</b> solo <c>workbook.xml</c> (hasta la primera hoja), esa hoja y <c>sharedStrings.xml</c>, en SAX y
/// sin cargar elementos: cada celda se lee por sus atributos y sus hijos a medida que pasan, y las que están fuera de una
/// fila se saltean. Más de <see cref="MaxCeldasRecorridas"/> celdas recorridas excede los límites; más de
/// <see cref="MaxCeldasPorFila"/> en una fila (el tope del formato) no es .xlsx. No toca la base ni el disco y no
/// ejecuta nada del archivo. No usa <c>XmlDocument</c> ni <c>XmlReader</c> propios: el SDK no procesa DTD, así que un XML
/// con entidades externas se rechaza sin pedir el recurso referido (R-03).</para>
/// <para><b>Excepciones:</b> las del ZIP, del XML y del SDK se traducen a <see cref="LecturaPlanilla.NoEsXlsx"/> sin
/// mensaje ni causa (D-17, R-05), y solo alrededor de cada llamada al SDK o al ZIP
/// (<see cref="TraducirFallasDelArchivo{TEstado, T}(TEstado, Func{TEstado, T})"/>): una excepción de la lógica propia del
/// lector es un error y sigue su camino, igual que <see cref="OutOfMemoryException"/> y
/// <see cref="OperationCanceledException"/>. Quedan fuera de esa traducción las propiedades de estado del lector SAX
/// (<c>IsStartElement</c>, <c>IsEndElement</c>, <c>ElementType</c>, <c>LocalName</c>, <c>EOF</c>, <c>Depth</c>), que no
/// leen el archivo, y los <c>Dispose</c>, que en solo lectura no escriben. Los números se leen con
/// <see cref="decimal.TryParse(string, NumberStyles, IFormatProvider, out decimal)"/>, nunca con <c>Parse</c>, cuyo
/// mensaje trae el texto de la celda.</para>
/// <para>Sin estado: una instancia sirve para toda la Api.</para>
/// </remarks>
public sealed class LectorPlanillaCostosXlsx : ILectorPlanillaCostos
{
    /// <summary>
    /// D-14: el tope de caracteres por parte del paquete. Segunda barrera: con <see cref="MaxBytesPorEntrada"/> no se
    /// alcanza en producción.
    /// </summary>
    internal const long MaxCaracteresPorParte = 100_000_000;

    /// <summary>D-14: las filas con datos que admite una planilla, sin contar la de encabezados.</summary>
    internal const int MaxFilasConDatos = 50_000;

    /// <summary>D-14: el índice más alto que puede tener una fila con datos.</summary>
    internal const int MaxIndiceFila = 50_001;

    /// <summary>D-14: los caracteres que puede tener una celda, y también cada cadena compartida.</summary>
    internal const int MaxCaracteresPorCelda = 65_536;

    /// <summary>Las cadenas compartidas que admite el libro; con más, la planilla pasa los límites (R-02).</summary>
    internal const int MaxCadenasCompartidas = 200_000;

    /// <summary>
    /// El índice de fila más alto del formato (la última fila de una hoja de Excel). Uno mayor, o menor que 1, hace
    /// inválida la planilla.
    /// </summary>
    internal const int MaxIndiceFilaFormato = 1_048_576;

    /// <summary>Las celdas (<c>&lt;c&gt;</c>) que se recorren en la hoja, dentro o fuera de filas; con más, excede.</summary>
    internal const int MaxCeldasRecorridas = 1_000_000;

    /// <summary>Las celdas de una fila: el tope del formato (la columna XFD). Con más, la planilla es inválida.</summary>
    internal const int MaxCeldasPorFila = 16_384;

    /// <summary>Los <c>&lt;row&gt;</c> que se recorren en la hoja, aunque repitan el índice: las filas del formato.</summary>
    internal const int MaxFilasRecorridas = 1_048_576;

    /// <summary>
    /// D-20: los bytes (o unidades, en UTF-16 y UTF-32) que puede tener un tag, de <c>&lt;</c> a <c>&gt;</c>. Acota el
    /// costo de un elemento con muchos atributos, que en XmlReader crece con su cuadrado.
    /// </summary>
    internal const int MaxLargoTag = 8_192;

    /// <summary>D-20: los elementos abiertos uno dentro de otro que admite una parte XML.</summary>
    internal const int MaxProfundidad = 64;

    /// <summary>D-20: las entradas que puede tener el ZIP.</summary>
    internal const int MaxEntradasZip = 100;

    /// <summary>
    /// D-20: los bytes descomprimidos que puede tener una entrada del ZIP (16 MiB). Una planilla legítima de 50.000 filas
    /// tiene partes de unos 10,5 MB como mucho.
    /// </summary>
    internal const long MaxBytesPorEntrada = 16L * 1024 * 1024;

    /// <summary>D-20: los bytes descomprimidos que puede tener el ZIP entero (32 MiB).</summary>
    internal const long MaxBytesDelPaquete = 32L * 1024 * 1024;

    private const string NsRelaciones = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // Columnas: A es el código, B el precio; cualquier otra cuenta como "de más".
    private const int ColumnaCodigo = 1;
    private const int ColumnaPrecio = 2;
    private const int ColumnaDeMas = 3;

    private readonly long _maxCaracteresPorParte;

    /// <summary>Un lector con el tope de producción, <see cref="MaxCaracteresPorParte"/>.</summary>
    public LectorPlanillaCostosXlsx()
        : this(MaxCaracteresPorParte)
    {
    }

    /// <summary>
    /// Un lector con otro tope de caracteres por parte. Solo para pruebas: la de la bomba baja el tope para ejercitar
    /// <see cref="OpenSettings.MaxCharactersInPart"/> debajo del tope de bytes por entrada del ZIP.
    /// </summary>
    internal LectorPlanillaCostosXlsx(long maxCaracteresPorParte)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCaracteresPorParte);
        _maxCaracteresPorParte = maxCaracteresPorParte;
    }

    private enum DirectorioDeclarado
    {
        Admisible,
        Excede,
        Invalido,
    }

    /// <inheritdoc />
    public LecturaPlanilla Leer(ReadOnlyMemory<byte> archivo)
    {
        // Un .xlsx es un ZIP: un .xls (firma OLE), un .csv o un PDF se rechazan por el contenido, sin abrirlos (R-09).
        if (!EmpiezaComoZip(archivo.Span))
        {
            return LecturaPlanilla.NoEsXlsx.Instancia;
        }

        // El EOCD se mira antes de crear el ZipArchive, que arma un objeto por entrada del directorio central (D-20).
        switch (EvaluarDirectorio(archivo.Span))
        {
            case DirectorioDeclarado.Excede:
                return LecturaPlanilla.ExcedeLimites.Instancia;
            case DirectorioDeclarado.Invalido:
                return LecturaPlanilla.NoEsXlsx.Instancia;
        }

        try
        {
            using var flujo = FlujoDeSoloLectura(archivo);
            RecorrerZip(flujo);
            return new LecturaPlanilla.Leida(LeerPaquete(flujo));
        }
        catch (LimiteExcedidoException)
        {
            return LecturaPlanilla.ExcedeLimites.Instancia;
        }
        catch (PlanillaInvalidaException)
        {
            // D-17, R-05: ni el mensaje ni la causa salen de acá; pueden traer el contenido de una celda.
            return LecturaPlanilla.NoEsXlsx.Instancia;
        }
    }

    /// <summary>
    /// Una llamada al SDK de OpenXml o al ZIP, y solo eso: sus excepciones de archivo ilegible
    /// (<see cref="EsFallaDelArchivo"/>) se traducen a <see cref="PlanillaInvalidaException"/>, sin mensaje ni causa. Las
    /// demás pasan como están.
    /// </summary>
    /// <remarks>
    /// Lambdas estáticas con el estado por parámetro: la hoja hace una llamada por evento SAX (millones en una planilla
    /// armada), y una lambda que capturara variables asignaría un cierre en cada una. Estáticas, el compilador las
    /// guarda una sola vez y no pueden capturar por descuido.
    /// </remarks>
    private static T TraducirFallasDelArchivo<TEstado, T>(TEstado estado, Func<TEstado, T> llamada)
    {
        try
        {
            return llamada(estado);
        }
        catch (Exception error) when (EsFallaDelArchivo(error))
        {
            throw new PlanillaInvalidaException();
        }
    }

    /// <inheritdoc cref="TraducirFallasDelArchivo{TEstado, T}(TEstado, Func{TEstado, T})"/>
    private static void TraducirFallasDelArchivo<TEstado>(TEstado estado, Action<TEstado> llamada)
    {
        try
        {
            llamada(estado);
        }
        catch (Exception error) when (EsFallaDelArchivo(error))
        {
            throw new PlanillaInvalidaException();
        }
    }

    /// <summary>
    /// Las excepciones que significan "el archivo no es un .xlsx legible": las de D-17 (paquete, ZIP, XML, E/S y estado
    /// del SDK), <see cref="FormatException"/>, porque System.IO.Packaging lanza <c>FileFormatException</c> (que deriva
    /// de ella) con un ZIP corrupto, y <see cref="ArgumentException"/>, que lanza con una relación interna a una URI
    /// absoluta. Solo se usa al traducir una llamada al SDK o al ZIP.
    /// </summary>
    private static bool EsFallaDelArchivo(Exception error) => error
        is OpenXmlPackageException
        or InvalidDataException
        or XmlException
        or IOException
        or InvalidOperationException
        or FormatException
        or ArgumentException;

    /// <summary>
    /// Verdadero si una entrada entera pasa el recorrido de bytes del marcado (<see cref="EscanerMarcado"/>): tags de
    /// hasta <see cref="MaxLargoTag"/> bytes y hasta <see cref="MaxProfundidad"/> niveles. Para pruebas: el lector usa
    /// el mismo recorrido por bloques dentro de <see cref="RecorrerZip"/>.
    /// </summary>
    internal static bool MarcadoDentroDeLimites(ReadOnlySpan<byte> parte)
    {
        var marcado = new EscanerMarcado();
        return marcado.Avanzar(parte) && !marcado.Rechazada;
    }

    private static bool EmpiezaComoZip(ReadOnlySpan<byte> bytes) =>
        bytes is [0x50, 0x4B, 0x03, 0x04, ..];

    /// <summary>
    /// D-20: lee la cantidad de entradas que declara el ZIP sin crear el <see cref="ZipArchive"/> y sin asignar memoria.
    /// Busca el EOCD (firma 0x06054b50) hacia atrás en los últimos 65.557 bytes (22 del registro más un comentario de
    /// hasta 65.535) y exige que su largo de comentario llegue justo al final del archivo. Si lo precede el localizador
    /// ZIP64 (0x07064b50), toma los campos de 64 bits del registro final ZIP64 (0x06064b50) al que apunta, que tiene que
    /// estar dentro del archivo y antes del localizador. Inválido: sin EOCD, con marcas de ZIP64 (0xFFFF, 0xFFFFFFFF) sin
    /// registros ZIP64, con un registro ZIP64 fuera de lugar o con otra firma, o con un directorio central que no termina
    /// antes del registro final. Excede: más de <see cref="MaxEntradasZip"/> entradas, en el disco o en total.
    /// </summary>
    private static DirectorioDeclarado EvaluarDirectorio(ReadOnlySpan<byte> bytes)
    {
        const int LargoEocd = 22;
        const int LargoLocalizador = 20;
        const int LargoRegistroZip64 = 56;
        var desde = Math.Max(0, bytes.Length - LargoEocd - ushort.MaxValue);
        for (var i = bytes.Length - LargoEocd; i >= desde; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[i..]) != 0x06054b50
                || i + LargoEocd + BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i + 20)..]) != bytes.Length)
            {
                continue;
            }

            ulong enElDisco = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i + 8)..]);
            ulong total = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i + 10)..]);
            ulong tamano = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i + 12)..]);
            ulong inicio = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i + 16)..]);
            var conMarcasZip64 = enElDisco == ushort.MaxValue || total == ushort.MaxValue
                || tamano == uint.MaxValue || inicio == uint.MaxValue;
            ulong finDelDirectorio = (ulong)i;

            if (i >= LargoLocalizador
                && BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i - LargoLocalizador)..]) == 0x07064b50)
            {
                var registro = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(i - LargoLocalizador + 8)..]);
                var ultimoPosible = i - LargoLocalizador - LargoRegistroZip64;
                if (ultimoPosible < 0 || registro > (ulong)ultimoPosible)
                {
                    return DirectorioDeclarado.Invalido;
                }

                var r = (int)registro;
                if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[r..]) != 0x06064b50)
                {
                    return DirectorioDeclarado.Invalido;
                }

                enElDisco = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(r + 24)..]);
                total = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(r + 32)..]);
                tamano = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(r + 40)..]);
                inicio = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(r + 48)..]);
                finDelDirectorio = registro;
            }
            else if (conMarcasZip64)
            {
                return DirectorioDeclarado.Invalido;
            }

            if (inicio > finDelDirectorio || tamano > finDelDirectorio - inicio)
            {
                return DirectorioDeclarado.Invalido;
            }

            return enElDisco > MaxEntradasZip || total > MaxEntradasZip
                ? DirectorioDeclarado.Excede
                : DirectorioDeclarado.Admisible;
        }

        return DirectorioDeclarado.Invalido;
    }

    private static MemoryStream FlujoDeSoloLectura(ReadOnlyMemory<byte> archivo) =>
        MemoryMarshal.TryGetArray(archivo, out var segmento) && segmento.Array is not null
            ? new MemoryStream(segmento.Array, segmento.Offset, segmento.Count, writable: false)
            : new MemoryStream(archivo.ToArray(), writable: false);

    /// <summary>
    /// D-20: recorre el ZIP y descomprime cada entrada contando los bytes que salen de verdad, con un búfer fijo, y corta
    /// apenas pasa un tope (excede los límites): el tamaño declarado en el ZIP no se cree. Deja el flujo al principio.
    /// </summary>
    private static void RecorrerZip(MemoryStream flujo)
    {
        using (var zip = TraducirFallasDelArchivo(
            flujo, static f => new ZipArchive(f, ZipArchiveMode.Read, leaveOpen: true)))
        {
            var entradas = TraducirFallasDelArchivo(zip, static z => z.Entries);
            if (entradas.Count > MaxEntradasZip)
            {
                throw new LimiteExcedidoException();
            }

            var bufer = new byte[81_920];
            long total = 0;
            foreach (var entrada in entradas)
            {
                using var datos = TraducirFallasDelArchivo(entrada, static e => e.Open());
                var marcado = new EscanerMarcado();
                long deLaEntrada = 0;
                int leidos;
                while ((leidos = TraducirFallasDelArchivo(
                    (Datos: datos, Bufer: bufer), static e => e.Datos.Read(e.Bufer, 0, e.Bufer.Length))) > 0)
                {
                    deLaEntrada += leidos;
                    total += leidos;
                    if (deLaEntrada > MaxBytesPorEntrada || total > MaxBytesDelPaquete
                        || !marcado.Avanzar(bufer.AsSpan(0, leidos)))
                    {
                        throw new LimiteExcedidoException();
                    }

                    if (marcado.Rechazada)
                    {
                        // Una codificación que el recorrido no sigue (UCS-4 2143 o 3412, u otra con 0x00): no es .xlsx.
                        throw new PlanillaInvalidaException();
                    }
                }
            }
        }

        flujo.Position = 0;
    }

    private ContenidoPlanilla LeerPaquete(MemoryStream flujo)
    {
        var opciones = new OpenSettings { MaxCharactersInPart = _maxCaracteresPorParte };
        using var documento = TraducirFallasDelArchivo(
            (Flujo: flujo, Opciones: opciones),
            static e => SpreadsheetDocument.Open(e.Flujo, isEditable: false, e.Opciones));

        var libro = TraducirFallasDelArchivo(documento, static d => d.WorkbookPart)
            ?? throw new PlanillaInvalidaException();
        var id = IdPrimeraHoja(libro);
        // Sin relación, con una relación a una parte que no existe, o una hoja de gráfico: no hay celdas que leer.
        var hoja = string.IsNullOrEmpty(id)
            ? null
            : TraducirFallasDelArchivo(
                (Libro: libro, Id: id), static e => e.Libro.TryGetPartById(e.Id, out var parte) ? parte : null);
        if (hoja is not WorksheetPart hojaDeCalculo)
        {
            throw new PlanillaInvalidaException();
        }

        var tabla = TraducirFallasDelArchivo(libro, static l => l.SharedStringTablePart);
        var compartidas = tabla is null ? [] : LeerCompartidas(tabla);
        return LeerHoja(hojaDeCalculo, compartidas);
    }

    /// <summary>
    /// D-05: el id de relación de la primera <c>&lt;sheet&gt;</c> de <c>workbook.xml</c>, en SAX y sin cargar el libro:
    /// la lectura se detiene ahí, antes de los nombres definidos y lo demás que viene después.
    /// </summary>
    private static string? IdPrimeraHoja(WorkbookPart libro)
    {
        using var lector = TraducirFallasDelArchivo(libro, static l => OpenXmlReader.Create(l));
        while (TraducirFallasDelArchivo(lector, static l => l.Read()))
        {
            ExigirProfundidad(lector);
            if (lector.IsStartElement && lector.ElementType == typeof(Sheet))
            {
                return Atributo(TraducirFallasDelArchivo(lector, static l => l.Attributes), "id", NsRelaciones);
            }
        }

        return null;
    }

    /// <summary>
    /// La tabla de cadenas compartidas, en SAX y sin cargar cada <c>&lt;si&gt;</c> como elemento: su texto es su
    /// <c>&lt;t&gt;</c> o los <c>&lt;t&gt;</c> de sus <c>&lt;r&gt;</c> concatenados (texto enriquecido); la guía
    /// fonética (<c>&lt;rPh&gt;</c>) no cuenta. Más de <see cref="MaxCadenasCompartidas"/> cadenas, o una de más de
    /// <see cref="MaxCaracteresPorCelda"/> caracteres, cortan la lectura: la planilla pasa los límites.
    /// </summary>
    /// <remarks>
    /// Se ubica cada <c>&lt;t&gt;</c> por su profundidad respecto del <c>&lt;si&gt;</c> abierto y por el último hijo
    /// directo visto.
    /// </remarks>
    private static List<string> LeerCompartidas(SharedStringTablePart parte)
    {
        var cadenas = new List<string>();
        var cantidad = 0;
        StringBuilder? actual = null;
        var profundidadItem = -1;
        Type? hijoDirecto = null;

        void Cerrar()
        {
            if (actual is not null)
            {
                cadenas.Add(actual.ToString());
                actual = null;
            }
        }

        using var lector = TraducirFallasDelArchivo(parte, static p => OpenXmlReader.Create(p));
        while (TraducirFallasDelArchivo(lector, static l => l.Read()))
        {
            ExigirProfundidad(lector);
            if (lector.IsEndElement)
            {
                if (lector.ElementType == typeof(SharedStringItem))
                {
                    Cerrar();
                }

                continue;
            }

            if (!lector.IsStartElement)
            {
                continue;
            }

            var tipo = lector.ElementType;
            if (tipo == typeof(SharedStringItem))
            {
                Cerrar();
                if (++cantidad > MaxCadenasCompartidas)
                {
                    throw new LimiteExcedidoException();
                }

                actual = new StringBuilder();
                profundidadItem = lector.Depth;
                hijoDirecto = null;
                continue;
            }

            if (actual is null)
            {
                continue;
            }

            var profundidad = lector.Depth - profundidadItem;
            if (profundidad == 1)
            {
                hijoDirecto = tipo;
            }

            if (tipo == typeof(Text) && (profundidad == 1 || (profundidad == 2 && hijoDirecto == typeof(Run))))
            {
                actual.Append(TraducirFallasDelArchivo(lector, static l => l.GetText()));
                Acotar(actual.Length);
            }
        }

        Cerrar();
        return cadenas;
    }

    /// <summary>
    /// La primera hoja, en SAX y sin cargar elementos. Por cada <c>&lt;c&gt;</c>: cuenta para
    /// <see cref="MaxCeldasRecorridas"/>; fuera de una fila se saltea con <c>Skip</c>; dentro, cuenta para
    /// <see cref="MaxCeldasPorFila"/>, se toman <c>r</c> y <c>t</c> de sus atributos y, si tiene hijos, el texto de
    /// <c>&lt;v&gt;</c> o de <c>&lt;is&gt;</c> a medida que pasan (D-08: sin ninguno de los dos, vacía).
    /// </summary>
    private static ContenidoPlanilla LeerHoja(WorksheetPart parte, List<string> compartidas)
    {
        var hoja = new LecturaHoja(compartidas);
        var celda = new CeldaEnCurso();
        var recorridas = 0;
        var filasRecorridas = 0;
        using var lector = TraducirFallasDelArchivo(parte, static p => OpenXmlReader.Create(p));
        var hay = TraducirFallasDelArchivo(lector, static l => l.Read());
        while (hay)
        {
            ExigirProfundidad(lector);
            if (lector.IsStartElement)
            {
                var tipo = lector.ElementType;
                if (tipo == typeof(Row))
                {
                    if (++filasRecorridas > MaxFilasRecorridas)
                    {
                        throw new LimiteExcedidoException();
                    }

                    hoja.EmpezarFila(Atributo(TraducirFallasDelArchivo(lector, static l => l.Attributes), "r", string.Empty));
                }
                else if (tipo == typeof(Cell) || (tipo == typeof(OpenXmlUnknownElement) && lector.LocalName == "c"))
                {
                    if (++recorridas > MaxCeldasRecorridas)
                    {
                        throw new LimiteExcedidoException();
                    }

                    if (tipo != typeof(Cell) || !hoja.FilaAbierta)
                    {
                        // Fuera de una fila no es parte de la hoja. Skip deja el lector en lo que sigue a la celda, que
                        // se examina sin otro Read.
                        TraducirFallasDelArchivo(lector, static l => l.Skip());
                        hay = !lector.EOF;
                        continue;
                    }

                    hoja.ContarCelda();
                    var atributos = TraducirFallasDelArchivo(lector, static l => l.Attributes);
                    celda.Empezar(Atributo(atributos, "r", string.Empty), Atributo(atributos, "t", string.Empty), lector.Depth);
                }
                else if (celda.Abierta)
                {
                    LeerHijoDeCelda(lector, tipo, celda);
                }
            }
            else if (lector.IsEndElement)
            {
                if (lector.ElementType == typeof(Cell) && celda.Abierta)
                {
                    hoja.Celda(celda.Terminar());
                }
                else if (lector.ElementType == typeof(Row))
                {
                    hoja.TerminarFila();
                }
            }

            hay = TraducirFallasDelArchivo(lector, static l => l.Read());
        }

        hoja.TerminarFila();
        return hoja.Contenido();
    }

    /// <summary>
    /// Un hijo de la celda abierta: <c>&lt;v&gt;</c> da el valor; <c>&lt;is&gt;</c> abre el texto en línea, que es su
    /// <c>&lt;t&gt;</c> o los <c>&lt;t&gt;</c> de sus <c>&lt;r&gt;</c> (la guía fonética no cuenta). La fórmula y lo demás
    /// se ignoran.
    /// </summary>
    private static void LeerHijoDeCelda(OpenXmlReader lector, Type tipo, CeldaEnCurso celda)
    {
        switch (lector.Depth - celda.Profundidad)
        {
            case 1:
                celda.HijoDirecto = tipo;
                if (tipo == typeof(CellValue))
                {
                    celda.Valor = TraducirFallasDelArchivo(lector, static l => l.GetText());
                    Acotar(celda.Valor.Length);
                }
                else if (tipo == typeof(InlineString))
                {
                    celda.EnLinea = new StringBuilder();
                }

                break;
            case 2 when celda.EnLinea is not null && celda.HijoDirecto == typeof(InlineString):
                celda.HijoDeTexto = tipo;
                if (tipo == typeof(Text))
                {
                    celda.EnLinea.Append(TraducirFallasDelArchivo(lector, static l => l.GetText()));
                    Acotar(celda.EnLinea.Length);
                }

                break;
            case 3 when celda.EnLinea is not null && celda.HijoDirecto == typeof(InlineString)
                && celda.HijoDeTexto == typeof(Run) && tipo == typeof(Text):
                celda.EnLinea.Append(TraducirFallasDelArchivo(lector, static l => l.GetText()));
                Acotar(celda.EnLinea.Length);
                break;
        }
    }

    private static string? Atributo(IEnumerable<OpenXmlAttribute> atributos, string nombre, string espacio)
    {
        foreach (var atributo in atributos)
        {
            if (atributo.LocalName == nombre
                && string.Equals(atributo.NamespaceUri ?? string.Empty, espacio, StringComparison.Ordinal))
            {
                return atributo.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Defensa en profundidad del tope de anidamiento: el recorrido de bytes ya corta en <see cref="MaxProfundidad"/>
    /// niveles; si algo pasara igual, el lector SAX no sigue más abajo.
    /// </summary>
    private static void ExigirProfundidad(OpenXmlReader lector)
    {
        if (lector.Depth > MaxProfundidad)
        {
            throw new PlanillaInvalidaException();
        }
    }

    private static void Acotar(int caracteres)
    {
        if (caracteres > MaxCaracteresPorCelda)
        {
            throw new LimiteExcedidoException();
        }
    }

    /// <summary>
    /// La celda tipada (D-08, D-09): sin <c>&lt;v&gt;</c> ni <c>&lt;is&gt;</c>, vacía; con texto en línea, texto; tipo
    /// <c>s</c>, el texto de la tabla compartida; <c>str</c> o <c>inlineStr</c>, texto; <c>n</c> o sin tipo, número si
    /// el texto del XML se lee como decimal con cultura invariante y "otro" si no; <c>b</c>, <c>e</c>, <c>d</c> o un tipo
    /// desconocido, "otro". Los textos ya llegan acotados.
    /// </summary>
    private static CeldaPlanilla Tipar(DatosCelda celda, List<string> compartidas)
    {
        if (celda.EnLinea is not null)
        {
            return new CeldaPlanilla.Texto(celda.EnLinea);
        }

        if (celda.Valor is not { } texto)
        {
            return new CeldaPlanilla.Vacia();
        }

        return celda.Tipo switch
        {
            "s" => new CeldaPlanilla.Texto(Compartida(texto, compartidas)),
            "str" or "inlineStr" => new CeldaPlanilla.Texto(texto),
            null or "" or "n" => decimal.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var numero)
                ? new CeldaPlanilla.Numero(numero)
                : new CeldaPlanilla.Otro(),
            _ => new CeldaPlanilla.Otro(),
        };
    }

    private static string Compartida(string indice, List<string> compartidas) =>
        int.TryParse(indice, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < compartidas.Count
            ? compartidas[i]
            : throw new PlanillaInvalidaException();

    /// <summary>
    /// La columna de la referencia de la celda (A1, B7, AA3): <see cref="ColumnaCodigo"/>, <see cref="ColumnaPrecio"/>
    /// o <see cref="ColumnaDeMas"/>. Sin referencia, la siguiente a la anterior de la fila, como admite el formato. Una
    /// referencia mal formada hace inválida la planilla.
    /// </summary>
    private static int Columna(string? referencia, int anterior)
    {
        if (referencia is null)
        {
            return Math.Min(anterior + 1, ColumnaDeMas);
        }

        var letras = 0;
        while (letras < referencia.Length && char.IsAsciiLetter(referencia[letras]))
        {
            letras++;
        }

        if (letras == 0 || letras > 3 || letras == referencia.Length
            || referencia.AsSpan(letras).ContainsAnyExceptInRange('0', '9'))
        {
            throw new PlanillaInvalidaException();
        }

        return letras > 1 ? ColumnaDeMas : Math.Min(char.ToUpperInvariant(referencia[0]) - 'A' + 1, ColumnaDeMas);
    }

    /// <summary>
    /// D-20: el recorrido de bytes del marcado de una entrada del ZIP, por bloques, dentro de la descompresión que ya hace
    /// <see cref="RecorrerZip"/>. Una máquina de estados sin asignaciones por byte: dentro o fuera de un tag
    /// (<c>&lt;</c> … <c>&gt;</c>) y, dentro de un tag, dentro o fuera de comillas (<c>"</c> o <c>'</c>), así un
    /// <c>&gt;</c> o un <c>/&gt;</c> en un valor de atributo no cierra nada. Mide el largo de cada tag
    /// (<see cref="MaxLargoTag"/>) y la profundidad de elementos abiertos (<see cref="MaxProfundidad"/>):
    /// <c>&lt;x</c> abre, <c>&lt;/</c> cierra, <c>/&gt;</c> cierra el recién abierto; <c>&lt;?</c> y <c>&lt;!</c> no
    /// cuentan, y los comentarios (<c>&lt;!-- … --&gt;</c>) y las secciones CDATA (<c>&lt;![CDATA[ … ]]&gt;</c>) se
    /// atraviesan sin mirar su contenido.
    /// </summary>
    /// <remarks>
    /// <para>La codificación se detecta como lo hace XmlReader, por los primeros 4 bytes: UTF-8 (con o sin BOM), UTF-16 y
    /// UTF-32 en orden 1234 o 4321 (con BOM o con un <c>&lt;</c> al principio), y en esos dos se recorre por unidades de 2
    /// o 4 bytes. Falla cerrada: una entrada con un 0x00 en sus primeros 4 bytes junto a un <c>&lt;</c> o a una marca
    /// FE FF / FF FE que no es ninguna de esas, como UCS-4 en los órdenes 2143 y 3412 (que XmlReader decodifica y Excel
    /// nunca usa), hace la planilla inválida (<see cref="Rechazada"/>). Sin <c>&lt;</c> ni marca, XmlReader toma la
    /// entrada como UTF-8 y la rechaza: es un binario (las opciones de impresión que guarda Excel, una imagen EMF) y no se
    /// recorre, como tampoco una entrada sin 0x00 que no empieza, después de espacios, con <c>&lt;</c> (PNG, JPEG).</para>
    /// <para>Supone que no hay <c>CodePagesEncodingProvider</c> ni otro proveedor de codificaciones registrado en el
    /// proceso: así XmlReader solo conoce UTF-8, UTF-16, UTF-32 y codificaciones compatibles con ASCII en los caracteres
    /// del marcado, y una parte declarada en EBCDIC (IBM037 y similares) se rechaza al abrirla en lugar de esquivar este
    /// recorrido. <c>ProteccionTests.Src_NoRegistraProveedoresDeCodificacion</c> vigila que ningún ensamblado de src lo
    /// registre.</para>
    /// </remarks>
    private sealed class EscanerMarcado
    {
        private const string InicioCdata = "[CDATA[";

        private readonly byte[] _inicio = new byte[4];
        private readonly byte[] _unidad = new byte[4];
        private Modo _modo = Modo.Detectando;
        private int _enInicio;
        private int _enUnidad;

        private Estado _estado = Estado.Texto;
        private TipoTag _tipo;
        private int _largo;
        private int _profundidad;
        private int _comillas;
        private int _previo;
        private int _progresoBang;
        private int _repetidos;

        private enum Modo
        {
            Detectando,
            Utf8Pendiente,
            Utf8,
            Utf16Le,
            Utf16Be,
            Utf32Le,
            Utf32Be,
            NoXml,
            Rechazado,
        }

        /// <summary>
        /// Verdadero si los primeros bytes de la entrada traen 0x00 y no son UTF-16 ni UTF-32 que este recorrido siga:
        /// la planilla no es un .xlsx admitido.
        /// </summary>
        public bool Rechazada => _modo == Modo.Rechazado;

        private enum Estado
        {
            Texto,
            AbreTag,
            Bang,
            Tag,
            Comentario,
            Cdata,
        }

        private enum TipoTag
        {
            Apertura,
            Cierre,
            Especial,
        }

        /// <summary>
        /// Recorre el bloque siguiente de la entrada; falso si pasó un límite. Si la codificación no se admite, deja de
        /// recorrer y queda <see cref="Rechazada"/>.
        /// </summary>
        public bool Avanzar(ReadOnlySpan<byte> bytes)
        {
            if (_modo == Modo.Detectando)
            {
                var faltan = Math.Min(_inicio.Length - _enInicio, bytes.Length);
                bytes[..faltan].CopyTo(_inicio.AsSpan(_enInicio));
                _enInicio += faltan;
                bytes = bytes[faltan..];
                if (_enInicio < _inicio.Length)
                {
                    // Menos de 4 bytes en total: no hay marcado que acotar.
                    return true;
                }

                var bom = Detectar();
                if (!Recorrer(_inicio.AsSpan(bom)))
                {
                    return false;
                }
            }

            return Recorrer(bytes);
        }

        /// <summary>Decide la codificación por los primeros 4 bytes y devuelve el largo de la marca de orden (BOM).</summary>
        private int Detectar()
        {
            ReadOnlySpan<byte> b = _inicio;
            (_modo, var bom) = b switch
            {
                [0xEF, 0xBB, 0xBF, _] => (Modo.Utf8Pendiente, 3),
                [0xFF, 0xFE, 0x00, 0x00] => (Modo.Utf32Le, 4),
                [0x00, 0x00, 0xFE, 0xFF] => (Modo.Utf32Be, 4),

                // UCS-4 3412 con BOM (FE FF 00 00) y sin BOM (00 3C 00 00), antes de los patrones de UTF-16 que los
                // tomarían. El 2143 (00 00 FF FE y 00 00 3C 00) cae en la regla del 0x00.
                [0xFE, 0xFF, 0x00, 0x00] or [0x00, 0x3C, 0x00, 0x00] => (Modo.Rechazado, 0),
                [0xFF, 0xFE, _, _] => (Modo.Utf16Le, 2),
                [0xFE, 0xFF, _, _] => (Modo.Utf16Be, 2),
                [0x3C, 0x00, 0x00, 0x00] => (Modo.Utf32Le, 0),
                [0x00, 0x00, 0x00, 0x3C] => (Modo.Utf32Be, 0),
                [0x3C, 0x00, _, _] => (Modo.Utf16Le, 0),
                [0x00, 0x3C, _, _] => (Modo.Utf16Be, 0),

                // Falla cerrada: un 0x00 al principio junto a un "<" (0x3C) o a una marca FE FF / FF FE, que no es
                // ninguna de las anteriores. Son las únicas formas en que XmlReader reconoce una codificación con 0x00
                // en los primeros 4 bytes; sin ellas la toma como UTF-8 y la rechaza, así que un binario con 0x00 (las
                // opciones de impresión que guarda Excel, una imagen EMF) no es XML y no se recorre.
                _ when b.Contains((byte)0x00) && PodriaSerXml(b) => (Modo.Rechazado, 0),
                _ when b.Contains((byte)0x00) => (Modo.NoXml, 0),
                _ => (Modo.Utf8Pendiente, 0),
            };
            return bom;
        }

        private static bool PodriaSerXml(ReadOnlySpan<byte> inicio) =>
            inicio.Contains((byte)0x3C)
            || inicio.IndexOf([(byte)0xFE, (byte)0xFF]) >= 0
            || inicio.IndexOf([(byte)0xFF, (byte)0xFE]) >= 0;

        private bool Recorrer(ReadOnlySpan<byte> bytes)
        {
            if (_modo is Modo.Utf8Pendiente or Modo.Utf8)
            {
                foreach (var b in bytes)
                {
                    if (_modo == Modo.NoXml)
                    {
                        return true;
                    }

                    if (!Unidad(b))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (_modo is Modo.NoXml or Modo.Rechazado)
            {
                return true;
            }

            var ancho = _modo is Modo.Utf16Le or Modo.Utf16Be ? 2 : 4;
            foreach (var b in bytes)
            {
                _unidad[_enUnidad++] = b;
                if (_enUnidad < ancho)
                {
                    continue;
                }

                _enUnidad = 0;
                var unidad = _modo switch
                {
                    Modo.Utf16Le => _unidad[0] | (_unidad[1] << 8),
                    Modo.Utf16Be => (_unidad[0] << 8) | _unidad[1],
                    Modo.Utf32Le => (int)BinaryPrimitives.ReadUInt32LittleEndian(_unidad),
                    _ => (int)BinaryPrimitives.ReadUInt32BigEndian(_unidad),
                };
                if (!Unidad(unidad))
                {
                    return false;
                }
            }

            return true;
        }

        private bool Unidad(int c)
        {
            if (_modo == Modo.Utf8Pendiente)
            {
                if (c is ' ' or '\t' or '\r' or '\n')
                {
                    return true;
                }

                // XmlReader solo acepta un documento que, después de espacios, empieza con "<".
                _modo = c == '<' ? Modo.Utf8 : Modo.NoXml;
                if (_modo == Modo.NoXml)
                {
                    return true;
                }
            }

            switch (_estado)
            {
                case Estado.Texto:
                    if (c == '<')
                    {
                        _estado = Estado.AbreTag;
                        _largo = 1;
                    }

                    return true;

                case Estado.AbreTag:
                    _largo++;
                    _comillas = 0;
                    _previo = c;
                    switch (c)
                    {
                        case '/':
                            _estado = Estado.Tag;
                            _tipo = TipoTag.Cierre;
                            _profundidad = Math.Max(0, _profundidad - 1);
                            return true;
                        case '?':
                            _estado = Estado.Tag;
                            _tipo = TipoTag.Especial;
                            return true;
                        case '!':
                            _estado = Estado.Bang;
                            _progresoBang = 0;
                            return true;
                        default:
                            _estado = Estado.Tag;
                            _tipo = TipoTag.Apertura;
                            return ++_profundidad <= MaxProfundidad;
                    }

                case Estado.Bang:
                    if (_progresoBang == 0 && c == '-')
                    {
                        _progresoBang = -1;
                        _largo++;
                        return true;
                    }

                    if (_progresoBang == -1 && c == '-')
                    {
                        _estado = Estado.Comentario;
                        _repetidos = 0;
                        return true;
                    }

                    if (_progresoBang >= 0 && c == InicioCdata[_progresoBang])
                    {
                        _largo++;
                        if (++_progresoBang == InicioCdata.Length)
                        {
                            _estado = Estado.Cdata;
                            _repetidos = 0;
                        }

                        return true;
                    }

                    // <!DOCTYPE, <!ENTITY y lo demás: un tag especial que no cuenta para la profundidad.
                    _estado = Estado.Tag;
                    _tipo = TipoTag.Especial;
                    _previo = 0;
                    return EnTag(c);

                case Estado.Comentario:
                    if (c == '>' && _repetidos >= 2)
                    {
                        _estado = Estado.Texto;
                    }
                    else
                    {
                        _repetidos = c == '-' ? _repetidos + 1 : 0;
                    }

                    return true;

                case Estado.Cdata:
                    if (c == '>' && _repetidos >= 2)
                    {
                        _estado = Estado.Texto;
                    }
                    else
                    {
                        _repetidos = c == ']' ? _repetidos + 1 : 0;
                    }

                    return true;

                default:
                    return EnTag(c);
            }
        }

        private bool EnTag(int c)
        {
            if (++_largo > MaxLargoTag)
            {
                return false;
            }

            if (_comillas != 0)
            {
                if (c == _comillas)
                {
                    _comillas = 0;
                }
            }
            else if (c is '"' or '\'')
            {
                _comillas = c;
            }
            else if (c == '>')
            {
                if (_tipo == TipoTag.Apertura && _previo == '/')
                {
                    _profundidad--;
                }

                _estado = Estado.Texto;
            }

            _previo = c;
            return true;
        }
    }

    /// <summary>Lo que el lector usa de una celda: referencia, tipo, texto de <c>&lt;v&gt;</c> y texto en línea.</summary>
    private readonly record struct DatosCelda(string? Referencia, string? Tipo, string? Valor, string? EnLinea);

    /// <summary>La celda que se está leyendo en SAX; una sola instancia por hoja, que se reinicia en cada celda.</summary>
    private sealed class CeldaEnCurso
    {
        private string? _referencia;
        private string? _tipo;

        public bool Abierta { get; private set; }

        public int Profundidad { get; private set; }

        public Type? HijoDirecto { get; set; }

        public Type? HijoDeTexto { get; set; }

        public string? Valor { get; set; }

        public StringBuilder? EnLinea { get; set; }

        public void Empezar(string? referencia, string? tipo, int profundidad)
        {
            _referencia = referencia;
            _tipo = tipo;
            Profundidad = profundidad;
            HijoDirecto = null;
            HijoDeTexto = null;
            Valor = null;
            EnLinea = null;
            Abierta = true;
        }

        public DatosCelda Terminar()
        {
            Abierta = false;
            return new DatosCelda(_referencia, _tipo, Valor, EnLinea?.ToString());
        }
    }

    /// <summary>Lo leído de la hoja mientras se recorre: la fila abierta, los encabezados y las filas con datos.</summary>
    private sealed class LecturaHoja(List<string> compartidas)
    {
        private readonly List<FilaPlanilla> _filas = [];
        private CeldaPlanilla _encabezadoA = new CeldaPlanilla.Vacia();
        private CeldaPlanilla _encabezadoB = new CeldaPlanilla.Vacia();
        private bool _hayEncabezados;
        private bool _columnasDeMas;

        private int _indiceFila;
        private int _ultimaColumna;
        private int _celdasEnFila;
        private CeldaPlanilla _a = new CeldaPlanilla.Vacia();
        private CeldaPlanilla _b = new CeldaPlanilla.Vacia();
        private bool _filaConDatos;

        public bool FilaAbierta { get; private set; }

        public void EmpezarFila(string? referencia)
        {
            TerminarFila();
            if (referencia is null)
            {
                // Sin r, la siguiente a la anterior, como admite el formato.
                _indiceFila = checked(_indiceFila + 1);
            }
            else if (!int.TryParse(referencia, NumberStyles.None, CultureInfo.InvariantCulture, out _indiceFila))
            {
                throw new PlanillaInvalidaException();
            }

            if (_indiceFila is < 1 or > MaxIndiceFilaFormato)
            {
                throw new PlanillaInvalidaException();
            }

            FilaAbierta = true;
            _ultimaColumna = 0;
            _celdasEnFila = 0;
            _a = new CeldaPlanilla.Vacia();
            _b = new CeldaPlanilla.Vacia();
            _filaConDatos = false;
        }

        /// <summary>Cuenta una celda de la fila abierta; más de <see cref="MaxCeldasPorFila"/> no es .xlsx.</summary>
        public void ContarCelda()
        {
            if (++_celdasEnFila > MaxCeldasPorFila)
            {
                throw new PlanillaInvalidaException();
            }
        }

        public void Celda(DatosCelda celda)
        {
            var columna = Columna(celda.Referencia, _ultimaColumna);
            _ultimaColumna = columna;
            var valor = Tipar(celda, compartidas);
            if (valor is CeldaPlanilla.Vacia)
            {
                return;
            }

            _filaConDatos = true;
            switch (columna)
            {
                case ColumnaCodigo:
                    _a = valor;
                    break;
                case ColumnaPrecio:
                    _b = valor;
                    break;
                default:
                    _columnasDeMas = true;
                    break;
            }
        }

        public void TerminarFila()
        {
            if (!FilaAbierta)
            {
                return;
            }

            FilaAbierta = false;

            // Una fila sin ninguna celda con valor no cuenta para los límites: puede ser solo formato.
            if (!_filaConDatos)
            {
                return;
            }

            if (_indiceFila > MaxIndiceFila)
            {
                throw new LimiteExcedidoException();
            }

            if (_indiceFila == 1)
            {
                if (!_hayEncabezados)
                {
                    _hayEncabezados = true;
                    _encabezadoA = _a;
                    _encabezadoB = _b;
                }

                return;
            }

            // D-08: con A y B vacías la fila se ignora (puede tener datos de más, ya marcados).
            if (_a is CeldaPlanilla.Vacia && _b is CeldaPlanilla.Vacia)
            {
                return;
            }

            if (_filas.Count == MaxFilasConDatos)
            {
                throw new LimiteExcedidoException();
            }

            _filas.Add(new FilaPlanilla(_indiceFila, CodigoComoTexto(_a), _b));
        }

        public ContenidoPlanilla Contenido() => new(_encabezadoA, _encabezadoB, _columnasDeMas, _filas);

        // D-10: el código llega como texto, con la misma regla que usa el dominio para un código numérico.
        private static CeldaPlanilla CodigoComoTexto(CeldaPlanilla codigo) => codigo is CeldaPlanilla.Numero numero
            ? new CeldaPlanilla.Texto(PlanillaCostos.TextoDeCodigoNumerico(numero.Valor))
            : codigo;
    }

    /// <summary>Un límite de D-14 o D-20 superado. Sin mensaje: no sale del lector.</summary>
    private sealed class LimiteExcedidoException : Exception
    {
    }

    /// <summary>El archivo no es una planilla legible. Sin mensaje: no sale del lector.</summary>
    private sealed class PlanillaInvalidaException : Exception
    {
    }
}
