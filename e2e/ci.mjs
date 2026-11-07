// Reglas de la integración continua (FEAT-005). Leen texto (rutas, XML, YAML) sin dependencias nuevas, así corren
// dentro de `npm run test:unidad` en la PC de desarrollo y en la corrida de CI.

/**
 * Proyectos de .NET sin su archivo de bloqueo de NuGet.
 * @param {string[]} archivos rutas relativas versionadas (`git ls-files`), separadas por `/`.
 * @returns {string[]} cada `.csproj` sin un `packages.lock.json` en su misma carpeta, en el orden recibido.
 */
export function proyectosSinLockfile(archivos) {
  const carpeta = (ruta) => (ruta.includes('/') ? ruta.slice(0, ruta.lastIndexOf('/')) : '');
  const nombre = (ruta) => ruta.slice(ruta.lastIndexOf('/') + 1);

  const carpetasConLockfile = new Set(
    archivos.filter((ruta) => nombre(ruta) === 'packages.lock.json').map(carpeta),
  );
  return archivos.filter((ruta) => ruta.endsWith('.csproj') && !carpetasConLockfile.has(carpeta(ruta)));
}

/**
 * La condición que GitHub Actions cumple (define `CI=true`) y la PC de desarrollo no. Acepta los espacios libres y
 * las comillas simples o dobles alrededor de `$(CI)` y de `true`, como las acepta MSBuild.
 */
const CONDICION_CI = /^\s*(['"]?)\$\(CI\)\1\s*==\s*(['"]?)true\2\s*$/i;

/** Valor de un atributo `Condition` (entre comillas dobles o simples), o `null` si la etiqueta no tiene. */
function condicion(atributos) {
  const coincidencia = /\bCondition\s*=\s*(?:"([^"]*)"|'([^']*)')/i.exec(atributos ?? '');
  return coincidencia ? (coincidencia[1] ?? coincidencia[2]) : null;
}

/** Elementos dentro de los cuales un PropertyGroup no es una asignación global de la evaluación. */
const NO_GLOBALES = new Set(['Target', 'Choose']);

/**
 * Cada asignación de propiedad de MSBuild (hijo directo de un PropertyGroup), en orden de aparición, con su valor, las
 * condiciones que la rigen (la del elemento y la de su PropertyGroup) y si es global (fuera de Target y de Choose).
 * Se descartan los comentarios XML: una propiedad nombrada en un comentario no cuenta.
 */
function asignaciones(texto) {
  const sinComentarios = texto.replace(/<!--[\s\S]*?-->/g, '');
  const encontradas = [];
  /** @type {{ nombre: string, atributos: string, fin: number }[]} */
  const pila = [];
  const registrar = (nombre, atributos, valor) => {
    const grupo = pila.at(-1);
    if (grupo?.nombre !== 'PropertyGroup') return;
    encontradas.push({
      nombre,
      valor: valor.trim(),
      condiciones: [condicion(grupo.atributos), condicion(atributos)].filter((c) => c !== null),
      global: !pila.some((e) => NO_GLOBALES.has(e.nombre)),
    });
  };

  const etiquetas = /<(\/?)([A-Za-z_][\w.:-]*)((?:[^>"']|"[^"]*"|'[^']*')*?)(\/?)>/g;
  for (const etiqueta of sinComentarios.matchAll(etiquetas)) {
    const [completa, cierre, nombre, atributos, autocierre] = etiqueta;
    if (autocierre) {
      registrar(nombre, atributos, '');
    } else if (!cierre) {
      pila.push({ nombre, atributos, fin: etiqueta.index + completa.length });
    } else {
      const posicion = pila.findLastIndex((e) => e.nombre === nombre);
      if (posicion === -1) continue;
      const [abierto] = pila.splice(posicion);
      registrar(nombre, abierto.atributos, sinComentarios.slice(abierto.fin, etiqueta.index));
    }
  }
  return encontradas;
}

/**
 * Problemas del `Directory.Build.props` para el bloqueo de dependencias de NuGet.
 * @param {string} texto contenido del archivo.
 * @returns {{ regla: string, detalle: string }[]} vacío si cumple todo.
 */
export function revisarPropsNuGet(texto) {
  if (typeof texto !== 'string' || texto.trim() === '') {
    return [{ regla: 'vacio', detalle: 'El Directory.Build.props está vacío.' }];
  }

  const problemas = [];
  const todas = asignaciones(texto);
  const de = (nombre) => todas.filter((p) => p.nombre === nombre);
  const esTrue = (p) => p.valor.toLowerCase() === 'true';
  const esCi = (p) => p.condiciones.length === 1 && CONDICION_CI.test(p.condiciones[0]);

  // En MSBuild gana la última asignación, así que se exige que TODAS sean `true` y globales: ninguna puede apagarla
  // después, ni quedar escondida en un Target o un Choose (que no son la evaluación global de la restauración).
  // Para todos los proyectos: al menos una sin condición (ni del elemento ni del grupo).
  const lockfile = de('RestorePackagesWithLockFile');
  const conLockfile =
    lockfile.every((p) => p.global && esTrue(p)) && lockfile.some((p) => p.condiciones.length === 0);
  if (!conLockfile) {
    problemas.push({
      regla: 'archivo-de-bloqueo',
      detalle:
        'Falta RestorePackagesWithLockFile en true, sin condición, para todos los proyectos (y ninguna otra ' +
        'asignación con otro valor ni dentro de un Target o un Choose).',
    });
  }

  // Solo en la corrida de CI: en la PC de desarrollo un cambio de paquete tiene que poder actualizar el archivo.
  const modoBloqueado = de('RestoreLockedMode');
  const modoBloqueadoEnCi =
    modoBloqueado.length > 0 && modoBloqueado.every((p) => p.global && esTrue(p) && esCi(p));
  if (!modoBloqueadoEnCi) {
    problemas.push({
      regla: 'modo-bloqueado',
      detalle:
        "Falta RestoreLockedMode en true con la condición '$(CI)' == 'true' (y ninguna otra asignación sin esa " +
        'condición, con otro valor ni dentro de un Target o un Choose).',
    });
  }

  return problemas;
}
