import { html, useEffect, useRef } from '../../lib/preact.js';
import { Terminal as XTerm } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';

function themeColors() {
  const light = document.documentElement.dataset.theme === 'light';
  return light
    ? { background: '#fbfbfd', foreground: '#151826', cursor: '#6d4cff', selectionBackground: '#d9d2ff', black: '#151826', brightBlack: '#555d72', white: '#c8ccd8', brightWhite: '#ffffff' }
    : { background: '#0a0c11', foreground: '#d7dbe6', cursor: '#b8a6ff', selectionBackground: '#3a3166', black: '#0a0c11', brightBlack: '#5c6478', white: '#d7dbe6', brightWhite: '#ffffff',
        red: '#fb7185', brightRed: '#fda4af', green: '#34d399', brightGreen: '#6ee7b7', yellow: '#fbbf24', brightYellow: '#fde68a',
        blue: '#60a5fa', brightBlue: '#93c5fd', magenta: '#c4a7ff', brightMagenta: '#ddd0ff', cyan: '#5eead4', brightCyan: '#99f6e4' };
}

/**
 * Een echte terminal-emulator. Via `api` (ref-object) kan de ouder schrijven en lezen:
 *   api.current = { write, writeln, clear, focus, cols, rows, fit }
 * onData(data) ontvangt elke toetsaanslag (wordt doorgestuurd naar het programma).
 */
export function Terminal({ api, onData, fontSize = 13.5 }) {
  const host = useRef(null);
  const onDataRef = useRef(onData);
  onDataRef.current = onData;

  useEffect(() => {
    const term = new XTerm({
      convertEol: true,
      cursorBlink: true,
      fontFamily: 'JetBrains Mono, Cascadia Code, Consolas, monospace',
      fontSize,
      lineHeight: 1.25,
      scrollback: 5000,
      theme: themeColors(),
      allowProposedApi: false,
    });
    const fit = new FitAddon();
    term.loadAddon(fit);
    term.open(host.current);
    const doFit = () => { try { fit.fit(); } catch { /* verborgen */ } };
    requestAnimationFrame(doFit);
    const ro = new ResizeObserver(doFit);
    ro.observe(host.current);
    const sub = term.onData((d) => onDataRef.current?.(d));
    const observer = new MutationObserver(() => { term.options.theme = themeColors(); });
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });

    if (api) {
      api.current = {
        write: (s) => term.write(s),
        writeln: (s) => term.writeln(s),
        clear: () => { term.reset(); },
        focus: () => term.focus(),
        fit: doFit,
        get cols() { return term.cols; },
        get rows() { return term.rows; },
      };
    }
    return () => { sub.dispose(); ro.disconnect(); observer.disconnect(); term.dispose(); if (api) api.current = null; };
  }, []);

  return html`<div class="terminal-host" ref=${host} onClick=${() => api?.current?.focus()}></div>`;
}
