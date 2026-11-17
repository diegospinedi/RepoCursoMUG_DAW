// Los datos del menú de Inicio (FEAT-012, D-03). La spec lo llama menuInicio.ts, pero ese nombre solo difiere en
// mayúsculas de MenuInicio.tsx: en un disco que no las distingue (Windows), './MenuInicio' resolvía a este archivo.

import type { NombreIcono } from '../componentes/IconoMenu';

/** Una opción del menú: el nombre es el único texto del enlace y la descripción va fuera de él (D-06). */
export interface OpcionMenu {
  readonly nombre: string;
  readonly destino: string;
  readonly descripcion: string;
  readonly icono: NombreIcono;
}

/** Una sección del menú, con su título y sus opciones. */
export interface SeccionMenu {
  readonly titulo: string;
  readonly opciones: readonly OpcionMenu[];
}

/**
 * El menú de Inicio (FEAT-012, FR-01): Ventas, Catálogo y Sistema, en el orden en que se recorren con Tab. Los nombres y
 * destinos son los de antes (FR-03); las descripciones evitan las palabras que vigilan otras pruebas (D-09).
 */
export const SECCIONES_MENU = [
  {
    titulo: 'Ventas',
    opciones: [
      {
        nombre: 'Nuevo presupuesto',
        destino: '/presupuestos/nuevo',
        descripcion: 'Armá un presupuesto con artículos del catálogo.',
        icono: 'presupuesto-nuevo',
      },
      {
        nombre: 'Buscar presupuestos',
        destino: '/presupuestos',
        descripcion: 'Encontrá presupuestos por fecha o por cliente.',
        icono: 'buscar',
      },
      {
        nombre: 'Facturas',
        destino: '/facturas',
        descripcion: 'Buscá facturas emitidas y descargá su PDF.',
        icono: 'factura',
      },
    ],
  },
  {
    titulo: 'Catálogo',
    opciones: [
      {
        nombre: 'Artículos',
        destino: '/articulos',
        descripcion: 'Buscá, editá y actualizá los costos del catálogo.',
        icono: 'articulos',
      },
      {
        nombre: 'Nuevo artículo',
        destino: '/articulos/nuevo',
        descripcion: 'Cargá un artículo con su costo y su margen.',
        icono: 'articulo-nuevo',
      },
    ],
  },
  {
    titulo: 'Sistema',
    opciones: [
      {
        nombre: 'Configuración',
        destino: '/configuracion',
        descripcion: 'Alícuota, redondeo, datos del emisor y backup.',
        icono: 'configuracion',
      },
      {
        nombre: 'Cambiar la contraseña',
        destino: '/cambiar-contrasena',
        descripcion: 'Cambiá la contraseña de acceso al sistema.',
        icono: 'llave',
      },
    ],
  },
] as const satisfies readonly SeccionMenu[];
