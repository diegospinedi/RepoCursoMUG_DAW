import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router';
import { App } from './App';
import './estilos.css';

const raiz = document.getElementById('raiz');
if (raiz === null) {
  throw new Error('Falta el elemento raíz de la aplicación.');
}

createRoot(raiz).render(
  <StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
);
