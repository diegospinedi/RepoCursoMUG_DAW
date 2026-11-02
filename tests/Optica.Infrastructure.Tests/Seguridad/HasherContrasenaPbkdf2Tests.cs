using System.Diagnostics;
using Optica.Infrastructure.Seguridad;

namespace Optica.Infrastructure.Tests.Seguridad;

public class HasherContrasenaPbkdf2Tests
{
    private const string Contrasena = "clave-de-prueba";

    [Fact]
    public void Hasher_MismaContrasena_SalesDistintas()
    {
        var hasher = new HasherContrasenaPbkdf2();

        var primero = hasher.Calcular(Contrasena);
        var segundo = hasher.Calcular(Contrasena);

        Assert.NotEqual(primero, segundo);
        var partesPrimero = primero.Split('$');
        var partesSegundo = segundo.Split('$');
        Assert.Equal(4, partesPrimero.Length);
        Assert.Equal(4, partesSegundo.Length);
        Assert.Equal("PBKDF2-SHA256", partesPrimero[0]);
        Assert.Equal("600000", partesPrimero[1]);
        Assert.Equal(16, Convert.FromBase64String(partesPrimero[2]).Length);
        Assert.Equal(16, Convert.FromBase64String(partesSegundo[2]).Length);
        Assert.NotEqual(partesPrimero[2], partesSegundo[2]); // sal distinta
        Assert.NotEqual(partesPrimero[3], partesSegundo[3]); // hash distinto

        // Ambos verifican la misma contraseña, y ninguno una distinta.
        Assert.True(hasher.Verificar(Contrasena, primero));
        Assert.True(hasher.Verificar(Contrasena, segundo));
        Assert.False(hasher.Verificar(Contrasena + "x", primero));
    }

    [Fact]
    public void Hasher_SustitutoSuelto_VerificarDevuelveFalsoSinExcepcion()
    {
        var hasher = new HasherContrasenaPbkdf2();
        var valido = hasher.Calcular(Contrasena);
        var resultado = true;

        var excepcion = Record.Exception(() => resultado = hasher.Verificar("abcdefg\uD800", valido));

        Assert.Null(excepcion);
        Assert.False(resultado);
    }

    [Fact]
    public void Hasher_SustitutoSuelto_CalcularLanzaExcepcionGenerica()
    {
        var hasher = new HasherContrasenaPbkdf2();

        var excepcion = Record.Exception(() => hasher.Calcular("abcdefg\uD800"));

        var propia = Assert.IsType<ContrasenaNoCodificableException>(excepcion);
        Assert.Null(propia.InnerException);
        var texto = propia.ToString();
        Assert.DoesNotContain("\uD800", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("D800", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("index", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abcdefg", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Hasher_IteracionesSobreElTope_RechazaSinDerivar()
    {
        var hasher = new HasherContrasenaPbkdf2();
        var partes = hasher.Calcular(Contrasena).Split('$');
        var sobreElTope = string.Join('$', partes[0], "10000001", partes[2], partes[3]);
        hasher.Verificar(Contrasena, "calentamiento"); // descarta el costo de la primera llamada

        var cronometro = Stopwatch.StartNew();
        var resultado = hasher.Verificar(Contrasena, sobreElTope);
        cronometro.Stop();

        Assert.False(resultado);
        // Derivar 10.000.001 iteraciones tarda más de un segundo; rechazar por formato, microsegundos.
        Assert.True(
            cronometro.Elapsed < TimeSpan.FromMilliseconds(250),
            $"Rechazar 10.000.001 iteraciones tardó {cronometro.Elapsed}: se derivó el hash en lugar de cortar por el tope.");
    }

    [Fact]
    public void Hasher_PisoDeIteraciones_FijoEn600000()
    {
        // El piso es el de ADR-002 y no sigue a Iteraciones: subir el costo no invalida los hashes de 600.000.
        Assert.Equal(600_000, HasherContrasenaPbkdf2.MinimoIteraciones);
        Assert.Equal(10_000_000, HasherContrasenaPbkdf2.MaximoIteraciones);
    }

    [Fact]
    public void Hasher_HashMalFormado_InvalidoDevuelveFalso()
    {
        var hasher = new HasherContrasenaPbkdf2();
        var valido = hasher.Calcular(Contrasena);
        Assert.True(hasher.Verificar(Contrasena, valido));

        var partes = valido.Split('$');
        var sal = Convert.FromBase64String(partes[2]);
        var hash = Convert.FromBase64String(partes[3]);
        var hashAlterado = (byte[])hash.Clone();
        hashAlterado[0] ^= 0x01;
        var salAlterada = (byte[])sal.Clone();
        salAlterada[^1] ^= 0x80;

        string Armar(string algoritmo, string iteraciones, string salB64, string hashB64) =>
            string.Join('$', algoritmo, iteraciones, salB64, hashB64);

        // Hashes correctos de la misma contraseña, pero degradados a menos iteraciones que las del ADR-002.
        string Degradado(int iteraciones) => Armar(
            partes[0],
            iteraciones.ToString(System.Globalization.CultureInfo.InvariantCulture),
            partes[2],
            Convert.ToBase64String(System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
                Contrasena, sal, iteraciones, System.Security.Cryptography.HashAlgorithmName.SHA256, 32)));

        var casos = new Dictionary<string, string?>
        {
            ["nulo"] = null,
            ["vacío"] = string.Empty,
            ["espacios"] = "   ",
            ["texto cualquiera"] = "no-es-un-hash",
            ["solo el prefijo"] = "PBKDF2-SHA256",
            ["segmentos vacíos"] = "PBKDF2-SHA256$$$",
            ["algoritmo distinto"] = Armar("PBKDF2-SHA1", partes[1], partes[2], partes[3]),
            ["algoritmo en minúsculas"] = Armar("pbkdf2-sha256", partes[1], partes[2], partes[3]),
            ["iteraciones no numéricas"] = Armar(partes[0], "abc", partes[2], partes[3]),
            ["iteraciones cero"] = Armar(partes[0], "0", partes[2], partes[3]),
            ["iteraciones negativas"] = Armar(partes[0], "-600000", partes[2], partes[3]),
            ["iteraciones desbordadas"] = Armar(partes[0], "99999999999", partes[2], partes[3]),
            ["iteraciones absurdas"] = Armar(partes[0], int.MaxValue.ToString(), partes[2], partes[3]),
            ["iteraciones con espacios"] = Armar(partes[0], " 600000", partes[2], partes[3]),
            ["iteraciones alteradas"] = Armar(partes[0], "600001", partes[2], partes[3]),
            ["sal no base64"] = Armar(partes[0], partes[1], "%%%%", partes[3]),
            ["sal vacía"] = Armar(partes[0], partes[1], string.Empty, partes[3]),
            ["sal alterada"] = Armar(partes[0], partes[1], Convert.ToBase64String(salAlterada), partes[3]),
            ["hash no base64"] = Armar(partes[0], partes[1], partes[2], "@@@@"),
            ["hash vacío"] = Armar(partes[0], partes[1], partes[2], string.Empty),
            ["hash truncado"] = Armar(partes[0], partes[1], partes[2], Convert.ToBase64String(hash[..^2])),
            ["hash alargado"] = Armar(partes[0], partes[1], partes[2], Convert.ToBase64String([.. hash, 0x00])),
            ["hash alterado"] = Armar(partes[0], partes[1], partes[2], Convert.ToBase64String(hashAlterado)),
            ["segmento de más"] = valido + "$extra",
            ["degradado a 1 iteración"] = Degradado(1),
            ["degradado a 599.999 iteraciones"] = Degradado(599_999),
            ["iteraciones sobre el tope"] = Armar(partes[0], "10000001", partes[2], partes[3]),
        };

        foreach (var (nombre, manipulado) in casos)
        {
            var cronometro = Stopwatch.StartNew();
            Assert.False(hasher.Verificar(Contrasena, manipulado!), $"El hash manipulado ({nombre}) verificó.");
            // Un hash manipulado no debe poder imponer un costo de CPU desmedido.
            Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(20), $"El caso ({nombre}) tardó {cronometro.Elapsed}.");
        }
    }
}
