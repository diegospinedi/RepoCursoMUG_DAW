using System.Reflection;
using Optica.Application.Configuracion;
using Optica.Domain.Configuracion;

namespace Optica.Application.Tests.Arquitectura;

/// <summary>
/// Dependencias entre módulos que el compilador no impide (D-10 de FEAT-002a, D-16 de FEAT-002c): Catálogo depende de
/// Configuración, nunca al revés.
/// </summary>
/// <remarks>
/// <para>
/// La revisión es por reflexión: firmas (campos, propiedades, eventos, parámetros, retornos, tipos base, interfaces,
/// argumentos genéricos, restricciones de los parámetros genéricos de tipos y métodos, y atributos) y variables locales
/// de todos los tipos de los espacios de nombres revisados, incluidos los anidados que genera el compilador (máquinas de
/// estado de los métodos async, clausuras de las lambdas). No ve las llamadas a miembros estáticos dentro de los cuerpos:
/// esas quedan para la revisión de código.
/// </para>
/// <para>
/// La detección de variables locales es confiable en Debug, que es como corre <c>dotnet test</c> por defecto: el
/// compilador conserva cada variable declarada. En Release puede eliminar una variable local que no hace falta, y su
/// tipo deja de verse en <see cref="MethodBody.LocalVariables"/>.
/// </para>
/// </remarks>
public class DependenciasDeModulosTests
{
    private const BindingFlags Todos =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    [Fact]
    public void Configuracion_NoUsaTiposDeCatalogo()
    {
        var revisados = new[]
            {
                (Ensamblado: typeof(ValidacionConfiguracion).Assembly, Espacio: "Optica.Application.Configuracion"),
                (Ensamblado: typeof(ConfiguracionNegocio).Assembly, Espacio: "Optica.Domain.Configuracion"),
            }
            .SelectMany(revisado => revisado.Ensamblado.GetTypes()
                .Where(tipo => EstaEn(tipo.Namespace, revisado.Espacio)))
            .ToList();

        var usos = revisados
            .SelectMany(tipo => TiposUsados(tipo).Select(usado => (Tipo: tipo, Usado: usado)))
            .Where(uso => EsDeCatalogo(uso.Usado))
            .Select(uso => $"{uso.Tipo.FullName} usa {uso.Usado.FullName ?? uso.Usado.Name}")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        // La revisión tiene que ver algo: los tipos que se conocen de cada módulo están entre los revisados.
        Assert.Contains(typeof(DatosConfiguracion), revisados);
        Assert.Contains(typeof(ConfiguracionNegocio), revisados);
        Assert.Contains(revisados, tipo => tipo.DeclaringType is not null);
        Assert.True(usos.Count == 0, string.Join(Environment.NewLine, usos));
    }

    /// <summary>Prueba de la propia revisión: un tipo de Catálogo sí usa tipos de Catálogo.</summary>
    [Fact]
    public void TiposUsados_TipoDeCatalogo_DetectaElUso()
    {
        var usados = TiposUsados(typeof(Optica.Application.Catalogo.AltaArticulo)).ToList();

        Assert.Contains(usados, EsDeCatalogo);
    }

    /// <summary>Prueba de la propia revisión: un tipo de Catálogo usado solo en una variable local se detecta.</summary>
    [Fact]
    public void TiposUsados_CatalogoSoloEnVariableLocal_DetectaElUso()
    {
        var usados = TiposUsados(typeof(UsaCatalogoSoloEnVariableLocal)).ToList();

        Assert.Contains(typeof(Optica.Domain.Catalogo.ResultadoRecalculo), usados);
    }

    /// <summary>Prueba de la propia revisión: un tipo de Catálogo en la restricción de un parámetro genérico del tipo.</summary>
    [Fact]
    public void TiposUsados_CatalogoEnRestriccionGenericaDelTipo_DetectaElUso()
    {
        var usados = TiposUsados(typeof(RestringidoACatalogo<>)).ToList();

        Assert.Contains(typeof(Optica.Domain.Catalogo.IArticuloRepository), usados);
    }

    /// <summary>Usa Catálogo solo en una variable local: ni en la firma ni en atributos.</summary>
    private static class UsaCatalogoSoloEnVariableLocal
    {
        public static int Contar()
        {
            var resultados = new List<Optica.Domain.Catalogo.ResultadoRecalculo>();
            GC.KeepAlive(resultados);
            return resultados.Count;
        }
    }

    /// <summary>Usa Catálogo solo en la restricción de su parámetro genérico.</summary>
    private sealed class RestringidoACatalogo<T>
        where T : Optica.Domain.Catalogo.IArticuloRepository
    {
    }

    private static bool EstaEn(string? espacio, string raiz) =>
        espacio is not null && (espacio == raiz || espacio.StartsWith(raiz + ".", StringComparison.Ordinal));

    private static bool EsDeCatalogo(Type tipo)
    {
        var espacio = tipo.Namespace;
        return espacio is not null && espacio.Split('.').Contains("Catalogo", StringComparer.Ordinal);
    }

    /// <summary>Los tipos que aparecen en la firma y en las variables locales de <paramref name="tipo"/>, desplegados.</summary>
    private static IEnumerable<Type> TiposUsados(Type tipo)
    {
        var directos = new List<Type>();
        if (tipo.BaseType is { } baseType)
        {
            directos.Add(baseType);
        }

        directos.AddRange(tipo.GetInterfaces());
        directos.AddRange(TiposDeAtributos(tipo.GetCustomAttributesData()));
        if (tipo.IsGenericTypeDefinition)
        {
            directos.AddRange(tipo.GetGenericArguments().SelectMany(argumento => argumento.GetGenericParameterConstraints()));
        }

        foreach (var campo in tipo.GetFields(Todos))
        {
            directos.Add(campo.FieldType);
            directos.AddRange(TiposDeAtributos(campo.GetCustomAttributesData()));
        }

        foreach (var propiedad in tipo.GetProperties(Todos))
        {
            directos.Add(propiedad.PropertyType);
            directos.AddRange(TiposDeAtributos(propiedad.GetCustomAttributesData()));
        }

        foreach (var evento in tipo.GetEvents(Todos))
        {
            if (evento.EventHandlerType is { } manejador)
            {
                directos.Add(manejador);
            }
        }

        foreach (var metodo in tipo.GetMethods(Todos).Cast<MethodBase>().Concat(tipo.GetConstructors(Todos)))
        {
            if (metodo is MethodInfo conRetorno)
            {
                directos.Add(conRetorno.ReturnType);
                directos.AddRange(TiposDeAtributos(conRetorno.ReturnParameter.GetCustomAttributesData()));
            }

            if (metodo.IsGenericMethodDefinition)
            {
                directos.AddRange(metodo.GetGenericArguments().SelectMany(argumento => argumento.GetGenericParameterConstraints()));
            }

            directos.AddRange(TiposDeAtributos(metodo.GetCustomAttributesData()));
            foreach (var parametro in metodo.GetParameters())
            {
                directos.Add(parametro.ParameterType);
                directos.AddRange(TiposDeAtributos(parametro.GetCustomAttributesData()));
            }

            if (metodo.GetMethodBody() is { } cuerpo)
            {
                directos.AddRange(cuerpo.LocalVariables.Select(local => local.LocalType));
            }
        }

        return directos.SelectMany(Desplegar).Distinct();
    }

    private static IEnumerable<Type> TiposDeAtributos(IEnumerable<CustomAttributeData> atributos) =>
        atributos.SelectMany(atributo => new[] { atributo.AttributeType }
            .Concat(atributo.ConstructorArguments.Concat(atributo.NamedArguments.Select(nombrado => nombrado.TypedValue))
                .SelectMany(argumento => argumento.Value is Type valor ? new[] { argumento.ArgumentType, valor } : [argumento.ArgumentType])));

    /// <summary>El tipo y los que lleva adentro: elemento de un arreglo, puntero o referencia y argumentos genéricos.</summary>
    private static IEnumerable<Type> Desplegar(Type tipo)
    {
        yield return tipo;
        if (tipo.HasElementType && tipo.GetElementType() is { } elemento)
        {
            foreach (var interno in Desplegar(elemento))
            {
                yield return interno;
            }
        }

        if (tipo.IsGenericType)
        {
            foreach (var argumento in tipo.GetGenericArguments())
            {
                foreach (var interno in Desplegar(argumento))
                {
                    yield return interno;
                }
            }
        }
    }
}
