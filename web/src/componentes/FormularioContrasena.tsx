import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router';
import type { Resultado } from '../api/cliente';
import { validarContrasena } from '../acceso/politicaContrasena';
import { CampoContrasena } from './CampoContrasena';

interface Props {
  titulo: string;
  boton: string;
  autocompletar: 'new-password' | 'current-password';
  enviar: (contrasena: string) => Promise<Resultado<void>>;
  /** Enlace que se ofrece ante un 409 (la operación no corresponde al estado del acceso). */
  enlaceConflicto: { a: string; texto: string };
}

/**
 * Formulario de una sola contraseña, compartido por la definición y el ingreso. Valida en el navegador antes de
 * enviar; los errores del servidor van junto al campo (400 y 401 con cuerpo) o como mensaje general. Al tener éxito
 * vuelve al arranque, que muestra el inicio. La contraseña vive solo en el estado de este componente.
 */
export function FormularioContrasena({ titulo, boton, autocompletar, enviar, enlaceConflicto }: Props) {
  const navegar = useNavigate();
  const [valor, setValor] = useState('');
  const [errorCampo, setErrorCampo] = useState<string | null>(null);
  const [general, setGeneral] = useState<{ mensaje: string; conflicto: boolean } | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function alEnviar(evento: FormEvent<HTMLFormElement>) {
    evento.preventDefault();
    setGeneral(null);
    const errorLocal = validarContrasena(valor);
    setErrorCampo(errorLocal);
    if (errorLocal !== null) {
      return;
    }

    setEnviando(true);
    const resultado = await enviar(valor);
    setEnviando(false);
    switch (resultado.tipo) {
      case 'ok':
        navegar('/', { replace: true });
        break;
      case 'campo':
        setErrorCampo(resultado.mensaje);
        break;
      case 'conflicto':
        setGeneral({ mensaje: resultado.mensaje, conflicto: true });
        break;
      case 'general':
        setGeneral({ mensaje: resultado.mensaje, conflicto: false });
        break;
      // 'sinSesion': el cliente ya llevó a /ingreso.
    }
  }

  return (
    <section className="pantalla">
      <h1>{titulo}</h1>
      {/* method="post": si el envío por JavaScript no llegara a correr, el navegador no pone la contraseña en la URL. */}
      <form method="post" onSubmit={alEnviar} noValidate>
        <CampoContrasena valor={valor} alCambiar={setValor} error={errorCampo} autocompletar={autocompletar} />
        {general !== null && (
          <div className="mensaje-general">
            <p role="alert">{general.mensaje}</p>
            {general.conflicto && <Link to={enlaceConflicto.a}>{enlaceConflicto.texto}</Link>}
          </div>
        )}
        <button type="submit" disabled={enviando}>
          {boton}
        </button>
      </form>
    </section>
  );
}
