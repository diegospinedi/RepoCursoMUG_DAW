using System.Reflection;
using Optica.Application.Configuracion;
using Optica.Domain.Configuracion;

namespace Optica.Application.Tests.Arquitectura;

/// <summary>
/// Dependencias entre módulos que el compilador no impide (D-10 de FEAT-002a, D-16 de FEAT-002c): Catálogo depende de
/// Configuración, nunca al revés; Presupuestos depende de Catálogo, nunca al revés (D-05 de FEAT-003a); Facturación
/// depende de Presupuestos y de Configuración, nunca al revés (FEAT-007).
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
/// <para>
/// Tampoco ve las constantes (<c>const</c>) de otro módulo: el compilador copia su valor en el código que las usa y el
/// tipo que las declara no queda en ninguna firma ni variable. Que <c>Optica.Application.Presupuestos</c> no use las
/// constantes de <c>Optica.Application.Catalogo</c> (D-08 de FEAT-003a) queda a la revisión de código.
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

    /// <summary>D-05 de FEAT-003a: Presupuestos depende de Catálogo, nunca al revés.</summary>
    [Fact]
    public void Catalogo_NoUsaTiposDePresupuestos()
    {
        var revisados = new[]
            {
                (Ensamblado: typeof(Optica.Application.Catalogo.AltaArticulo).Assembly,
                    Espacio: "Optica.Application.Catalogo"),
                (Ensamblado: typeof(Optica.Domain.Catalogo.Articulo).Assembly, Espacio: "Optica.Domain.Catalogo"),
            }
            .SelectMany(revisado => revisado.Ensamblado.GetTypes()
                .Where(tipo => EstaEn(tipo.Namespace, revisado.Espacio)))
            .ToList();

        var usos = revisados
            .SelectMany(tipo => TiposUsados(tipo).Select(usado => (Tipo: tipo, Usado: usado)))
            .Where(uso => EsDePresupuestos(uso.Usado))
            .Select(uso => $"{uso.Tipo.FullName} usa {uso.Usado.FullName ?? uso.Usado.Name}")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        // La revisión tiene que ver algo: los tipos que se conocen de cada módulo están entre los revisados.
        Assert.Contains(typeof(Optica.Application.Catalogo.BuscarArticulos), revisados);
        Assert.Contains(typeof(Optica.Domain.Catalogo.Articulo), revisados);
        Assert.Contains(revisados, tipo => tipo.DeclaringType is not null);
        Assert.True(usos.Count == 0, string.Join(Environment.NewLine, usos));
    }

    /// <summary>
    /// D-08 de FEAT-003a: la aplicación de Presupuestos tiene sus propios textos y no usa tipos de la aplicación de
    /// Catálogo. Sí puede usar el dominio de Catálogo (D-05): lee artículos con <c>IArticuloRepository</c>.
    /// </summary>
    [Fact]
    public void AplicacionDePresupuestos_NoUsaTiposDeAplicacionDeCatalogo()
    {
        var revisados = typeof(Optica.Application.Presupuestos.AltaPresupuesto).Assembly.GetTypes()
            .Where(tipo => EstaEn(tipo.Namespace, "Optica.Application.Presupuestos"))
            .ToList();

        var usos = revisados
            .SelectMany(tipo => TiposUsados(tipo).Select(usado => (Tipo: tipo, Usado: usado)))
            .Where(uso => EstaEn(uso.Usado.Namespace, "Optica.Application.Catalogo"))
            .Select(uso => $"{uso.Tipo.FullName} usa {uso.Usado.FullName ?? uso.Usado.Name}")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        // La revisión tiene que ver algo: los tipos que se conocen del módulo están entre los revisados.
        Assert.Contains(typeof(Optica.Application.Presupuestos.AltaPresupuesto), revisados);
        Assert.Contains(typeof(Optica.Application.Presupuestos.ValidacionPresupuesto), revisados);
        Assert.Contains(revisados, tipo => tipo.DeclaringType is not null);
        Assert.True(usos.Count == 0, string.Join(Environment.NewLine, usos));
    }

    /// <summary>
    /// Spec FEAT-007, Block 3: Facturación depende de Presupuestos y de Configuración, nunca al revés. Ni el dominio ni
    /// la aplicación de esos dos módulos usan tipos de Facturación, y tampoco las reglas comunes del dominio
    /// (<c>Optica.Domain.Comun</c>).
    /// </summary>
    [Fact]
    public void Dependencias_PresupuestosNoDependeDeFacturacion()
    {
        var revisados = new[]
            {
                (Ensamblado: typeof(Optica.Application.Presupuestos.AltaPresupuesto).Assembly,
                    Espacio: "Optica.Application.Presupuestos"),
                (Ensamblado: typeof(ValidacionConfiguracion).Assembly, Espacio: "Optica.Application.Configuracion"),
                (Ensamblado: typeof(Optica.Domain.Presupuestos.Presupuesto).Assembly,
                    Espacio: "Optica.Domain.Presupuestos"),
                (Ensamblado: typeof(ConfiguracionNegocio).Assembly, Espacio: "Optica.Domain.Configuracion"),
                (Ensamblado: typeof(Optica.Domain.Comun.ReglasImporte).Assembly, Espacio: "Optica.Domain.Comun"),
            }
            .SelectMany(revisado => revisado.Ensamblado.GetTypes()
                .Where(tipo => EstaEn(tipo.Namespace, revisado.Espacio)))
            .ToList();

        var usos = revisados
            .SelectMany(tipo => TiposUsados(tipo).Select(usado => (Tipo: tipo, Usado: usado)))
            .Where(uso => EsDeFacturacion(uso.Usado))
            .Select(uso => $"{uso.Tipo.FullName} usa {uso.Usado.FullName ?? uso.Usado.Name}")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        // La revisión tiene que ver algo: los tipos que se conocen de cada módulo están entre los revisados.
        Assert.Contains(typeof(Optica.Application.Presupuestos.AltaPresupuesto), revisados);
        Assert.Contains(typeof(DatosConfiguracion), revisados);
        Assert.Contains(typeof(Optica.Domain.Presupuestos.Presupuesto), revisados);
        Assert.Contains(typeof(ConfiguracionNegocio), revisados);
        Assert.Contains(typeof(Optica.Domain.Comun.ReglasImporte), revisados);
        Assert.Contains(revisados, tipo => tipo.DeclaringType is not null);
        Assert.True(usos.Count == 0, string.Join(Environment.NewLine, usos));
    }

    /// <summary>
    /// FEAT-007: el dominio de Facturación no usa tipos de Catálogo directamente; las reglas de la línea le llegan a
    /// través de Presupuestos.
    /// </summary>
    [Fact]
    public void Dependencias_FacturacionNoUsaCatalogoDirectamente()
    {
        var revisados = typeof(Optica.Domain.Facturacion.Factura).Assembly.GetTypes()
            .Where(tipo => EstaEn(tipo.Namespace, "Optica.Domain.Facturacion"))
            .ToList();

        var usos = revisados
            .SelectMany(tipo => TiposUsados(tipo).Select(usado => (Tipo: tipo, Usado: usado)))
            .Where(uso => EsDeCatalogo(uso.Usado))
            .Select(uso => $"{uso.Tipo.FullName} usa {uso.Usado.FullName ?? uso.Usado.Name}")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        // La revisión tiene que ver algo: los tipos que se conocen del módulo están entre los revisados.
        Assert.Contains(typeof(Optica.Domain.Facturacion.Factura), revisados);
        Assert.Contains(typeof(Optica.Domain.Facturacion.ReglasEmision), revisados);
        Assert.Contains(typeof(Optica.Domain.Facturacion.LineaFactura), revisados);
        Assert.True(usos.Count == 0, string.Join(Environment.NewLine, usos));
    }

    /// <summary>
    /// Prueba de la propia revisión: el dominio de Facturación sí usa Presupuestos y Configuración (la dirección
    /// permitida), y la revisión lo ve.
    /// </summary>
    [Fact]
    public void TiposUsados_ReglasEmision_DetectaPresupuestosYConfiguracion()
    {
        var usados = TiposUsados(typeof(Optica.Domain.Facturacion.ReglasEmision)).ToList();

        Assert.Contains(typeof(Optica.Domain.Presupuestos.Presupuesto), usados);
        Assert.Contains(typeof(ConfiguracionNegocio), usados);
        Assert.Contains(usados, EsDeFacturacion);
    }

    /// <summary>Prueba de la propia revisión: un tipo de Facturación usado solo en una variable local se detecta.</summary>
    [Fact]
    public void TiposUsados_FacturacionSoloEnVariableLocal_DetectaElUso()
    {
        var usados = TiposUsados(typeof(UsaFacturacionSoloEnVariableLocal)).ToList();

        Assert.Contains(usados, EsDeFacturacion);
    }

    /// <summary>Usa Facturación solo en una variable local: ni en la firma ni en atributos.</summary>
    private static class UsaFacturacionSoloEnVariableLocal
    {
        public static int Contar()
        {
            var comprobantes = new List<Optica.Domain.Facturacion.NumeroComprobante>();
            GC.KeepAlive(comprobantes);
            return comprobantes.Count;
        }
    }

    /// <summary>Prueba de la propia revisión: un tipo de Presupuestos sí usa tipos de Presupuestos.</summary>
    [Fact]
    public void TiposUsados_TipoDePresupuestos_DetectaElUso()
    {
        var usados = TiposUsados(typeof(Optica.Application.Presupuestos.AltaPresupuesto)).ToList();

        Assert.Contains(usados, EsDePresupuestos);
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

    private static bool EsDePresupuestos(Type tipo)
    {
        var espacio = tipo.Namespace;
        return espacio is not null && espacio.Split('.').Contains("Presupuestos", StringComparer.Ordinal);
    }

    private static bool EsDeFacturacion(Type tipo)
    {
        var espacio = tipo.Namespace;
        return espacio is not null && espacio.Split('.').Contains("Facturacion", StringComparer.Ordinal);
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
