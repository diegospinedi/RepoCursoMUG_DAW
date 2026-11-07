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

/**
 * Los comandos que AGENTS.md declara (sección Stack) y que la corrida de CI tiene que correr, con el directorio donde
 * corren (`.` es la raíz del repositorio). Una prueba verifica que cada uno sigue en AGENTS.md: un cambio de un lado
 * sin el otro falla.
 * @type {ReadonlyArray<Readonly<{ directorio: string, comando: string }>>}
 */
export const COMANDOS_DEL_PROYECTO = Object.freeze(
  [
    ['web', 'npm ci'],
    ['e2e', 'npm ci'],
    ['.', 'dotnet build Optica.sln'],
    ['.', 'dotnet test Optica.sln'],
    ['web', 'npm run lint'],
    ['web', 'npm run test:lint'],
    ['web', 'npm run test:coverage'],
    ['web', 'npm run build'],
    ['e2e', 'npm run test:unidad'],
    ['e2e', 'npm run test:harness'],
  ].map(([directorio, comando]) => Object.freeze({ directorio, comando })),
);

/**
 * La sección "Stack" de AGENTS.md: el texto entre `## Stack` y el siguiente título de nivel 2 o la siguiente línea
 * `---`.
 * @param {string} texto contenido de AGENTS.md.
 * @returns {string}
 */
export function seccionStack(texto) {
  const lineas = String(texto ?? '').split(/\r?\n/);
  const inicio = lineas.findIndex((linea) => /^##\s+Stack\s*$/.test(linea));
  if (inicio === -1) {
    throw new Error('AGENTS.md no tiene la sección Stack');
  }
  const resto = lineas.slice(inicio + 1);
  const fin = resto.findIndex((linea) => linea.startsWith('## ') || linea.startsWith('---'));
  return (fin === -1 ? resto : resto.slice(0, fin)).join('\n');
}

/** Las únicas acciones permitidas, fijadas por el SHA de 40 caracteres de su commit (D-02). */
const ACCION_PERMITIDA = /^actions\/(?:checkout|setup-dotnet|setup-node)@[0-9a-f]{40}$/;

/**
 * Subcomandos de npm que instalan o actualizan paquetes sin respetar el archivo de bloqueo, o que bajan y corren uno
 * del registro: install con sus abreviaturas y errores de tipeo que npm acepta, install-test, add, exec, update.
 */
const SUBCOMANDOS_QUE_INSTALAN = [
  'install-test', 'install', 'instal', 'insta', 'inst', 'ins', 'in', 'isntall', 'isntal', 'isnta', 'isnt', 'it', 'i',
  'add', 'exec', 'x', 'update', 'upgrade', 'udpate', 'up',
];

/**
 * `npm`, opciones (`-w x`, `--prefix web`, `--prefix=web`, `--silent`) y uno de esos subcomandos como palabra completa.
 */
const NPM_INSTALA = new RegExp(
  String.raw`\bnpm(?:\s+-{1,2}[\w-]+(?:=\S*|\s+(?!-)\S+)?)*\s+(?:${SUBCOMANDOS_QUE_INSTALAN.join('|')})(?![\w-])`,
);

/** `npx` sin `--no-install` inmediatamente después: podría bajar un paquete sin fijar del registro. */
const NPX_SIN_NO_INSTALL = /\bnpx\b(?!\s+--no-install\b)/;

/**
 * Lo que queda de una línea sin su comentario de YAML: un `#` al principio o después de un espacio, fuera de comillas.
 * Dentro de un `run: |` el `#` es contenido para YAML, pero también empieza un comentario de la consola.
 */
function sinComentario(linea) {
  let comilla = null;
  for (let i = 0; i < linea.length; i += 1) {
    const caracter = linea[i];
    if (comilla) {
      if (caracter === comilla) comilla = null;
    } else if (caracter === '"' || caracter === "'") {
      comilla = caracter;
    } else if (caracter === '#' && (i === 0 || /\s/.test(linea[i - 1]))) {
      return linea.slice(0, i).trimEnd();
    }
  }
  return linea.trimEnd();
}

/** Quita las comillas que rodean un valor escalar de YAML. */
function sinComillas(valor) {
  const limpio = valor.trim();
  const coincidencia = /^(["'])([\s\S]*)\1$/.exec(limpio);
  return coincidencia ? coincidencia[2] : limpio;
}

/**
 * Las líneas con contenido (sin las vacías ni las de solo comentario), con su sangría, su contenido sin comentario y,
 * si son `clave: valor` (o `- clave: valor`), la clave y el valor.
 */
function lineasDe(texto) {
  return texto
    .split(/\r?\n/)
    .map((cruda) => {
      const contenido = sinComentario(cruda);
      const sangria = cruda.length - cruda.trimStart().length;
      const coincidencia = /^\s*(-\s+)?(["']?)([\w.-]+)\2\s*:(?:\s+(.*))?$/.exec(contenido);
      return {
        sangria,
        contenido: contenido.trim(),
        guion: contenido.trimStart().startsWith('- ') || contenido.trim() === '-',
        clave: coincidencia ? coincidencia[3] : null,
        valor: coincidencia ? (coincidencia[4] ?? '').trim() : null,
        // Sangría de la clave (en `- clave:` la clave queda dos columnas después del guion).
        sangriaDeClave: coincidencia ? sangria + (coincidencia[1]?.length ?? 0) : null,
      };
    })
    .filter((linea) => linea.contenido !== '');
}

/** Las líneas que cuelgan de `lineas[indice]`: las siguientes con más sangría, hasta la primera que no la tiene. */
function hijos(lineas, indice) {
  const base = lineas[indice].sangriaDeClave ?? lineas[indice].sangria;
  const resultado = [];
  for (let i = indice + 1; i < lineas.length && lineas[i].sangria > base; i += 1) {
    resultado.push(lineas[i]);
  }
  return resultado;
}

/** Las claves directas de un bloque: las de sus hijos con la sangría del primero. */
function clavesDirectas(lineasHijas) {
  if (lineasHijas.length === 0) return [];
  const sangria = lineasHijas[0].sangriaDeClave ?? lineasHijas[0].sangria;
  return lineasHijas.filter((linea) => linea.clave !== null && linea.sangriaDeClave === sangria);
}

/** Las claves de primer nivel del documento, con su índice en `lineas`. */
function primerNivel(lineas, clave) {
  return lineas.flatMap((linea, indice) => (linea.sangria === 0 && linea.clave === clave ? [indice] : []));
}

/** Indicador de escalar de bloque (`|`, `>`, con o sin `-`, `+` o sangría explícita). */
const ESCALAR_DE_BLOQUE = /^[|>][-+0-9]*$/;

/**
 * Los pasos de cada `steps:`. La línea `steps:` fija la sangría n; cada línea con n + 2 espacios que empieza con `- `
 * abre un paso, que termina en el siguiente `- ` de esa sangría o en una línea con sangría ≤ n. Las claves del paso son
 * la de la línea `- ` y las de sangría n + 4; lo que tiene más sangría (el contenido de un `run: |`, los valores de un
 * `with:`) no son claves del paso.
 */
function pasosDe(lineas) {
  const pasos = [];
  lineas.forEach((linea, indice) => {
    if (linea.clave !== 'steps' || linea.guion) return;
    const n = linea.sangria;
    // El job es la línea anterior más cercana con menos sangría que `steps:`; si tiene continue-on-error, sus pasos
    // pueden fallar sin que falle el job.
    const indiceDelJob = lineas.findLastIndex((otra, i) => i < indice && otra.sangria < n);
    const jobPuedeFallar =
      indiceDelJob !== -1 &&
      clavesDirectas(hijos(lineas, indiceDelJob)).some((otra) => otra.clave === 'continue-on-error' && !esFalse(otra));
    let actual = null;
    for (let i = indice + 1; i < lineas.length && lineas[i].sangria > n; i += 1) {
      const otra = lineas[i];
      if (otra.sangria === n + 2 && otra.guion) {
        actual = { indice: i, claves: new Map(), lineas: [otra], jobPuedeFallar };
        pasos.push(actual);
        if (otra.clave !== null) actual.claves.set(otra.clave, i);
        continue;
      }
      if (actual === null) continue;
      actual.lineas.push(otra);
      if (otra.sangria === n + 4 && otra.clave !== null && !otra.guion) actual.claves.set(otra.clave, i);
    }
  });

  return pasos.map((paso) => {
    const valor = (clave) => (paso.claves.has(clave) ? lineas[paso.claves.get(clave)].valor : null);
    // Las líneas del run y su texto: un escalar de bloque `|` conserva los saltos de línea; uno plano (o `>`) sigue en
    // las líneas con más sangría, que YAML junta con un espacio.
    const run = () => {
      if (!paso.claves.has('run')) return null;
      const indice = paso.claves.get('run');
      const propio = lineas[indice].valor;
      const siguientes = hijos(lineas, indice).map((l) => l.contenido);
      if (ESCALAR_DE_BLOQUE.test(propio)) {
        return { lineas: siguientes, texto: siguientes.join(propio.startsWith('|') ? '\n' : ' ').trim() };
      }
      const todas = [sinComillas(propio), ...siguientes].filter((l, i) => i === 0 || l !== '');
      return { lineas: todas, texto: todas.join(' ').trim() };
    };
    const puedeFallar = paso.claves.has('continue-on-error') && !esFalse(lineas[paso.claves.get('continue-on-error')]);
    return {
      claves: paso.claves,
      uses: valor('uses') === null ? null : sinComillas(valor('uses')),
      directorio: valor('working-directory') === null ? null : sinComillas(valor('working-directory')),
      run: run(),
      con: paso.claves.has('with') ? clavesDirectas(hijos(lineas, paso.claves.get('with'))) : [],
      // Un paso que puede no correr (if) o fallar sin cortar el job (continue-on-error) no cumple un comando.
      opcional: paso.jobPuedeFallar || puedeFallar || paso.claves.has('if'),
    };
  });
}

/** Una clave con el valor `false` (con o sin comillas). */
function esFalse(linea) {
  return sinComillas(linea.valor ?? '') === 'false';
}

/**
 * Las entradas de primer nivel de un flujo de YAML (`[a, b]` o `{a: x, b: {…}}`): el elemento, o la clave en un mapa.
 * Las comas y los dos puntos dentro de corchetes, llaves o comillas no parten.
 */
function entradasDeFlujo(flujo) {
  const interior = flujo.trim().slice(1, -1);
  const partes = [];
  let actual = '';
  let profundidad = 0;
  let comilla = null;
  for (const caracter of interior) {
    if (comilla) {
      if (caracter === comilla) comilla = null;
    } else if (caracter === '"' || caracter === "'") {
      comilla = caracter;
    } else if ('[{'.includes(caracter)) {
      profundidad += 1;
    } else if (']}'.includes(caracter)) {
      profundidad -= 1;
    } else if (caracter === ',' && profundidad === 0) {
      partes.push(actual);
      actual = '';
      continue;
    }
    actual += caracter;
  }
  partes.push(actual);
  return partes
    .map((parte) => sinComillas(parte.split(/:(?:\s|$)/)[0]))
    .filter((parte) => parte !== '');
}

/** Corchetes y llaves abiertos menos cerrados, fuera de comillas. */
function balance(texto) {
  let resultado = 0;
  let comilla = null;
  for (const caracter of texto) {
    if (comilla) {
      if (caracter === comilla) comilla = null;
    } else if (caracter === '"' || caracter === "'") {
      comilla = caracter;
    } else if ('[{'.includes(caracter)) {
      resultado += 1;
    } else if (']}'.includes(caracter)) {
      resultado -= 1;
    }
  }
  return resultado;
}

/** Marca de un flujo de `on:` que no se cierra: nunca es `pull_request`, así `disparador` lo rechaza. */
const FLUJO_SIN_CERRAR = '(flujo sin cerrar)';

/**
 * Los eventos de `on:`: en línea (`on: x`, `on: [x, y]`, `on: {x: …}`, también partido en varias líneas hasta que se
 * cierra) o en bloque (claves o elementos `- x`).
 */
function eventosDe(lineas) {
  return primerNivel(lineas, 'on').flatMap((indice) => {
    const propio = lineas[indice].valor;
    if (/^[[{]/.test(propio)) {
      let flujo = propio;
      for (let i = indice + 1; balance(flujo) > 0 && i < lineas.length; i += 1) {
        flujo += ` ${lineas[i].contenido}`;
      }
      return balance(flujo) === 0 && /[\]}]$/.test(flujo) ? entradasDeFlujo(flujo) : [FLUJO_SIN_CERRAR];
    }
    if (propio !== '') {
      return [sinComillas(propio)];
    }
    const directas = hijos(lineas, indice);
    if (directas.length === 0) return [];
    const sangria = directas[0].sangria;
    return directas
      .filter((linea) => linea.sangria === sangria)
      .map((linea) => (linea.guion && linea.clave === null ? sinComillas(linea.contenido.slice(1)) : linea.clave))
      .filter((evento) => evento !== null);
  });
}

/**
 * Problemas del workflow de integración continua (`.github/workflows/ci.yml`). Lee el texto línea por línea, sin un
 * parser de YAML (D-07): cubre la forma en que está escrito el workflow del proyecto, no todo YAML.
 * @param {string} texto contenido del workflow.
 * @returns {{ regla: string, detalle: string }[]} vacío si cumple todo.
 */
export function revisarWorkflow(texto) {
  if (typeof texto !== 'string' || texto.trim() === '') {
    return [{ regla: 'vacio', detalle: 'El workflow está vacío.' }];
  }

  const problemas = [];
  const problema = (regla, detalle) => problemas.push({ regla, detalle });
  const lineas = lineasDe(texto);
  const pasos = pasosDe(lineas);

  const eventos = eventosDe(lineas);
  if (eventos.length !== 1 || eventos[0] !== 'pull_request') {
    problema(
      'disparador',
      `El workflow tiene que dispararse solo con pull_request; tiene: ${eventos.join(', ') || '(ninguno)'}.`,
    );
  }

  // Sobre el texto completo, comentarios incluidos: dentro de un `run: |` un `#` no es comentario de YAML, y GitHub
  // evalúa las expresiones `${{ }}` también ahí.
  if (/pull_request_target|workflow_run/.test(texto)) {
    problema(
      'disparador-inseguro',
      'pull_request_target y workflow_run corren con permisos y secretos del repositorio sobre código de terceros.',
    );
  }

  const permisos = primerNivel(lineas, 'permissions');
  const soloLectura = permisos.some((indice) => {
    const propio = lineas[indice].valor;
    if (propio !== '') return /^\{.*\bcontents\s*:\s*read\b.*\}$/.test(propio);
    return clavesDirectas(hijos(lineas, indice)).some(
      (linea) => linea.clave === 'contents' && sinComillas(linea.valor) === 'read',
    );
  });
  // En cualquier nivel (workflow o job), solo los valores de los bloques permissions: un nombre de paso con "write"
  // no es un permiso.
  const amplia = lineas.some((linea, indice) => {
    if (linea.clave !== 'permissions') return false;
    const valores = [linea.valor, ...hijos(lineas, indice).map((hija) => hija.valor ?? hija.contenido)];
    return valores.some((valor) => /\b(?:write|read-all)\b/.test(valor));
  });
  if (!soloLectura || amplia) {
    problema(
      'permisos',
      'Hace falta permissions: con contents: read a nivel del workflow, y ningún permiso write, write-all ni read-all.',
    );
  }

  if (/\bsecrets\b|\bgithub\s*(?:\.\s*token\b|\[\s*['"]token['"]\s*\])|\bgithub_token\b/i.test(texto)) {
    problema(
      'secretos',
      'El workflow no puede usar secretos ni el token de GitHub (secrets., github.token, GITHUB_TOKEN).',
    );
  }

  for (const paso of pasos) {
    if (paso.uses === null || !paso.uses.startsWith('actions/checkout@')) continue;
    const persiste = paso.con.find((linea) => linea.clave === 'persist-credentials');
    if (!persiste || sinComillas(persiste.valor) !== 'false') {
      problema('credenciales', `${paso.uses} tiene que tener persist-credentials: false en su with.`);
    }
  }

  for (const linea of lineas) {
    if (linea.clave !== 'uses') continue;
    const accion = sinComillas(linea.valor);
    if (!ACCION_PERMITIDA.test(accion)) {
      problema(
        'acciones',
        `Acción no permitida: ${accion}. Solo actions/checkout, actions/setup-dotnet y actions/setup-node ` +
          'fijadas por SHA.',
      );
    }
  }

  const jobs = primerNivel(lineas, 'jobs').flatMap((indice) => {
    const directas = hijos(lineas, indice);
    return clavesDirectas(directas).map((job) => lineas.indexOf(job));
  });
  const sinTiempo = jobs.filter((indice) => {
    const tiempo = clavesDirectas(hijos(lineas, indice)).find((linea) => linea.clave === 'timeout-minutes');
    const minutos = tiempo && /^\d+$/.test(sinComillas(tiempo.valor)) ? Number(sinComillas(tiempo.valor)) : NaN;
    return !(minutos >= 1 && minutos <= 45);
  });
  if (jobs.length === 0 || sinTiempo.length > 0) {
    const nombres = sinTiempo.map((indice) => lineas[indice].clave).join(', ');
    problema(
      'tiempo-maximo',
      `Cada job necesita timeout-minutes entre 1 y 45 a su nivel${nombres ? `: ${nombres}` : ''}.`,
    );
  }

  const concurrencias = lineas.flatMap((linea, indice) => (linea.clave === 'concurrency' ? [indice] : []));
  const cancela =
    concurrencias.length > 0 &&
    concurrencias.every((indice) => {
      const claves = clavesDirectas(hijos(lineas, indice));
      const valor = (clave) => sinComillas(claves.find((linea) => linea.clave === clave)?.valor ?? '');
      return valor('cancel-in-progress') === 'true' && valor('group').includes('github.event.pull_request.number');
    });
  if (!cancela) {
    problema(
      'cancelacion',
      'Hace falta concurrency con cancel-in-progress: true y un group con github.event.pull_request.number.',
    );
  }

  for (const paso of pasos) {
    if (paso.run === null) continue;
    // Cada línea y, para un escalar plano partido (`npm` en una línea, `install` en la siguiente), el texto junto.
    const candidatos = [...paso.run.lineas.map(sinComentario), sinComentario(paso.run.texto)];
    const instala = candidatos.find((comando) => NPM_INSTALA.test(comando) || NPX_SIN_NO_INSTALL.test(comando));
    if (instala !== undefined) {
      problema(
        'instalacion',
        `Instalación sin el archivo de bloqueo: ${instala.trim()}. Usar npm ci y npx --no-install.`,
      );
    }
  }

  const sinNombre = pasos.filter((paso) => !paso.claves.has('name')).length;
  if (sinNombre > 0) {
    problema('pasos-con-nombre', `Hay ${sinNombre} paso(s) sin name: el pull request no diría qué paso falló.`);
  }

  for (const { directorio, comando } of COMANDOS_DEL_PROYECTO) {
    const corre = pasos.some(
      (paso) =>
        paso.run !== null &&
        !paso.opcional &&
        paso.run.texto === comando &&
        (directorio === '.' ? paso.directorio === null : paso.directorio === directorio),
    );
    if (!corre) {
      problema('comandos', `Falta un paso que corra ${comando} en ${directorio === '.' ? 'la raíz' : directorio}.`);
    }
  }

  // defaults con working-directory (del workflow o de un job) cambia el directorio de los pasos sin que lo diga cada
  // uno: el directorio de cada comando dejaría de ser el que se lee en su paso.
  const directorioPorDefecto = lineas.some(
    (linea, indice) =>
      linea.clave === 'defaults' && hijos(lineas, indice).some((hija) => hija.clave === 'working-directory'),
  );
  if (directorioPorDefecto) {
    problema('comandos', 'defaults no puede fijar working-directory: cada paso dice dónde corre su comando.');
  }

  return problemas;
}
