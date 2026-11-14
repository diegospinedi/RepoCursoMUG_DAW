using System.Reflection;
using System.Reflection.Emit;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Optica.Infrastructure;
using Optica.Infrastructure.Arca;

namespace Optica.Api.Tests.Arquitectura;

/// <summary>
/// Spec FEAT-007, Block 6 (AGENTS.md: "Integración ARCA WSFEv1 aislada en su propio módulo", RF-27): los tipos de
/// <c>Optica.Infrastructure.Arca</c> (SOAP, certificado, códigos de WSFEv1, simulador) solo los usa ese mismo espacio de
/// nombres y la raíz de composición <see cref="ConfiguracionInfraestructura"/>. Ni el dominio, ni la aplicación, ni el
/// resto de la infraestructura, ni la Api los conocen: la Api cifra el ticket de WSAA a través del puerto neutro
/// <c>IProtectorDatos</c> (D-30) y la emisión llega a ARCA solo por <see cref="IAutorizadorComprobantes"/>.
/// </summary>
/// <remarks>
/// La revisión es por reflexión, como la de <c>DependenciasDeModulosTests</c>: firmas (campos, propiedades, eventos,
/// parámetros, retornos, tipos base, interfaces, argumentos y restricciones genéricas, atributos) y variables locales de
/// cada tipo, incluidos los anidados que genera el compilador, y además el IL de cada método y constructor (operandos
/// de <c>newobj</c>, <c>call</c>, <c>callvirt</c>, <c>ldsfld</c>, <c>ldftn</c>, <c>ldtoken</c>, etc.): ve también las
/// llamadas directas sin variable local. No ve las constantes copiadas por el compilador: eso queda a la revisión de
/// código.
/// </remarks>
public sealed class AislamientoArcaTests
{
    private const string EspacioArca = "Optica.Infrastructure.Arca";

    private const BindingFlags Todos =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    [Fact]
    public void Dependencias_SoloInfrastructureArcaUsaTiposDeArca()
    {
        var revisados = new[]
            {
                typeof(Factura).Assembly,
                typeof(IAutorizadorComprobantes).Assembly,
                typeof(ConfiguracionInfraestructura).Assembly,
                typeof(Program).Assembly,
            }
            .Distinct()
            .SelectMany(ensamblado => ensamblado.GetTypes())
            .ToList();

        var usos = revisados
            .Where(tipo => !EstaEn(tipo.Namespace, EspacioArca) && !EsRaizDeComposicion(tipo))
            .SelectMany(tipo => TiposUsados(tipo).Select(usado => (Tipo: tipo, Usado: usado)))
            .Where(uso => EstaEn(uso.Usado.Namespace, EspacioArca))
            .Select(uso => $"{uso.Tipo.FullName} usa {uso.Usado.FullName ?? uso.Usado.Name}")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        // La revisión tiene que ver algo: los dos autorizadores de ARCA (el simulador y el cliente de WSFEv1) están entre
        // los revisados, y también tipos de cada capa. El cliente es interno: se lo busca por nombre.
        Assert.Contains(typeof(SimuladorArca), revisados);
        Assert.Contains(revisados, tipo => tipo.FullName == EspacioArca + ".ClienteWsfe");
        Assert.Contains(typeof(Factura), revisados);
        Assert.Contains(typeof(IAutorizadorComprobantes), revisados);
        Assert.Contains(typeof(Program), revisados);
        Assert.True(usos.Count == 0, string.Join(Environment.NewLine, usos));
    }

    /// <summary>
    /// Prueba de la propia revisión: la raíz de composición sí usa tipos de ARCA (la excepción permitida), y la revisión
    /// lo ve; si no lo viera, la prueba de arriba no probaría nada.
    /// </summary>
    [Fact]
    public void TiposUsados_RaizDeComposicion_DetectaTiposDeArca()
    {
        var usados = typeof(ConfiguracionInfraestructura).Assembly.GetTypes()
            .Where(EsRaizDeComposicion)
            .SelectMany(TiposUsados)
            .ToList();

        Assert.Contains(typeof(OpcionesArca), usados);
    }

    /// <summary>
    /// Ronda 2, prueba de la propia revisión: una llamada directa a un miembro estático de ARCA, sin variable local ni
    /// firma que la delate, la detecta el recorrido del IL (y no la de firmas).
    /// </summary>
    [Fact]
    public void TiposUsados_LlamadaDirectaSinVariable_LaDetectaElIl()
    {
        Assert.DoesNotContain(typeof(CodigosWsfe), TiposDeFirma(typeof(UsaArcaSinVariable)).SelectMany(Desplegar));
        Assert.Contains(typeof(CodigosWsfe), TiposUsados(typeof(UsaArcaSinVariable)));
        Assert.Contains(typeof(SimuladorArca), TiposUsados(typeof(UsaArcaConNewobjYLdftn)));
        Assert.Contains(typeof(OpcionesArca), TiposUsados(typeof(UsaArcaConNewobjYLdftn)));
    }

    /// <summary>Llama a ARCA sin guardar nada en una variable: solo el IL lo muestra.</summary>
    private static class UsaArcaSinVariable
    {
        public static int Tipo() => CodigosWsfe.Tipo(TipoComprobante.FacturaB) + 1;
    }

    /// <summary>Usa ARCA con <c>newobj</c> descartado y con <c>ldftn</c> de un método estático.</summary>
    private static class UsaArcaConNewobjYLdftn
    {
        public static bool Crear() =>
            new SimuladorArca(
                "x", RespuestaSimulador.Autorizar, TimeSpan.Zero,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SimuladorArca>.Instance).Entorno == default;

        public static Delegate Referencia() => new Func<string, bool>(OpcionesArca.EsCuitValido);
    }

    private static bool EsRaizDeComposicion(Type tipo)
    {
        var exterior = tipo;
        while (exterior.DeclaringType is { } contenedor)
        {
            exterior = contenedor;
        }

        return exterior == typeof(ConfiguracionInfraestructura);
    }

    private static bool EstaEn(string? espacio, string raiz) =>
        espacio is not null && (espacio == raiz || espacio.StartsWith(raiz + ".", StringComparison.Ordinal));

    /// <summary>
    /// Los tipos que usa <paramref name="tipo"/>: los de su firma y variables locales, y los que nombra el IL de sus
    /// métodos y constructores; desplegados.
    /// </summary>
    private static IEnumerable<Type> TiposUsados(Type tipo) =>
        TiposDeFirma(tipo)
            .Concat(tipo.GetMethods(Todos).Cast<MethodBase>().Concat(tipo.GetConstructors(Todos)).SelectMany(TiposDelIl))
            .SelectMany(Desplegar)
            .Distinct();

    /// <summary>Los códigos de operación del IL por valor (los de dos bytes empiezan con 0xFE).</summary>
    private static readonly Dictionary<short, OpCode> CodigosOperacion = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(campo => (OpCode)campo.GetValue(null)!)
        .ToDictionary(codigo => codigo.Value);

    /// <summary>
    /// Los tipos que nombra el IL del método: los de los operandos de metadatos (<c>newobj</c>, <c>call</c>,
    /// <c>callvirt</c>, <c>ldsfld</c>, <c>ldfld</c>, <c>ldftn</c>, <c>ldtoken</c>, <c>box</c>, <c>castclass</c>, etc.):
    /// el tipo mismo o el que declara el miembro, más el tipo del campo y el retorno del método. Ve las llamadas a
    /// miembros estáticos sin variable local; sigue sin ver las constantes, que el compilador copia como valores.
    /// </summary>
    private static IEnumerable<Type> TiposDelIl(MethodBase metodo)
    {
        var il = metodo.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            return [];
        }

        var argumentosTipo = metodo.DeclaringType is { IsGenericType: true } declarante
            ? declarante.GetGenericArguments()
            : null;
        var argumentosMetodo = metodo is MethodInfo { IsGenericMethod: true } generico
            ? generico.GetGenericArguments()
            : null;
        var tipos = new List<Type>();
        var posicion = 0;
        while (posicion < il.Length)
        {
            short valor = il[posicion++];
            if (valor == 0xFE && posicion < il.Length)
            {
                valor = (short)(0xFE00 | il[posicion++]);
            }

            if (!CodigosOperacion.TryGetValue(valor, out var codigo))
            {
                throw new InvalidOperationException(
                    $"Código de IL desconocido 0x{valor:X} en {metodo.DeclaringType?.FullName}.{metodo.Name}.");
            }

            switch (codigo.OperandType)
            {
                case OperandType.InlineMethod:
                case OperandType.InlineField:
                case OperandType.InlineType:
                case OperandType.InlineTok:
                    tipos.AddRange(Resolver(metodo.Module, BitConverter.ToInt32(il, posicion), argumentosTipo, argumentosMetodo));
                    posicion += 4;
                    break;
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    posicion += 1;
                    break;
                case OperandType.InlineVar:
                    posicion += 2;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    posicion += 8;
                    break;
                case OperandType.InlineSwitch:
                    posicion += 4 + (4 * BitConverter.ToInt32(il, posicion));
                    break;
                default:
                    // InlineBrTarget, InlineI, InlineSig, InlineString, ShortInlineR: 4 bytes.
                    posicion += 4;
                    break;
            }
        }

        return tipos;
    }

    private static IEnumerable<Type> Resolver(Module modulo, int token, Type[]? argumentosTipo, Type[]? argumentosMetodo)
    {
        MemberInfo? miembro;
        try
        {
            miembro = modulo.ResolveMember(token, argumentosTipo, argumentosMetodo);
        }
        catch (ArgumentException)
        {
            // Un token genérico fuera de su contexto: se intenta sin contexto.
            miembro = modulo.ResolveMember(token);
        }

        return miembro switch
        {
            Type tipo => [tipo],
            FieldInfo campo => [campo.DeclaringType!, campo.FieldType],
            MethodInfo llamado => [llamado.DeclaringType!, llamado.ReturnType],
            ConstructorInfo constructor => [constructor.DeclaringType!],
            _ => [],
        };
    }

    /// <summary>Los tipos que aparecen en la firma y en las variables locales de <paramref name="tipo"/>, sin desplegar.</summary>
    private static IEnumerable<Type> TiposDeFirma(Type tipo)
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

        return directos;
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
