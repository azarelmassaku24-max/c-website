import { api } from './api.js';
import { settings } from '../progress/settings.js';

/** @typedef {{path: string, content: string}} SourceFile */

const culture = () => settings.get().culture || 'nl-BE';

export const codeService = {
  /** Live diagnostics voor de editor. */
  diagnostics: (files, mainType, signal) => api.post('/api/code/diagnostics', { files, mainType }, { signal, timeout: 15000 }),
  review: (files, level) => api.post(`/api/code/review?level=${level ?? 20}`, { files }),
  /** Niet-interactieve uitvoering met vooraf gegeven invoer. */
  run: (files, { stdin = '', mainType, initialFiles, seed } = {}) =>
    api.post('/api/code/run', { files, stdin, mainType, initialFiles, seed, culture: culture() }, { timeout: 30000 }),
  /** Uitvoering met trace (traceertabel, call stack). */
  trace: (files, { stdin = '', mainType, initialFiles } = {}) =>
    api.post('/api/code/trace', { files, stdin, mainType, initialFiles, culture: culture() }, { timeout: 30000 }),
  complete: (files, file, line, column, signal) => api.post('/api/code/complete', { files, file, line, column }, { signal, timeout: 8000 }),
  hover: (files, file, line, column, signal) => api.post('/api/code/hover', { files, file, line, column }, { signal, timeout: 8000 }),
  grade: (exerciseId, files) => api.post(`/api/exercises/${encodeURIComponent(exerciseId)}/grade`, { files, culture: culture() }, { timeout: 90000 }),
};

/**
 * Interactieve sessie via WebSocket. Callbacks: onStatus, onCompiled, onStarted, onOutput, onError, onExit, onClose.
 */
export class InteractiveRun {
  constructor(handlers) {
    this.handlers = handlers;
    this.ws = null;
    this.running = false;
  }

  start({ files, mainType, cols, rows, initialFiles, seed }) {
    this.close();
    const proto = location.protocol === 'https:' ? 'wss' : 'ws';
    const ws = new WebSocket(`${proto}://${location.host}/ws/run`);
    this.ws = ws;
    ws.onopen = () => ws.send(JSON.stringify({ type: 'start', files, mainType, cols, rows, initialFiles, seed, culture: culture() }));
    ws.onmessage = (ev) => {
      for (const line of String(ev.data).split('\n')) {
        if (!line) continue;
        let msg;
        try { msg = JSON.parse(line); } catch { continue; }
        this.dispatch(msg);
      }
    };
    ws.onerror = () => this.handlers.onError?.('Verbinding met de uitvoeromgeving mislukt.');
    ws.onclose = () => {
      if (this.ws === ws) {
        this.running = false;
        this.handlers.onClose?.();
      }
    };
  }

  dispatch(msg) {
    const h = this.handlers;
    switch (msg.type) {
      case 'status': h.onStatus?.(msg.phase); break;
      case 'compiled': h.onCompiled?.(msg); if (!msg.success) this.close(); break;
      case 'started': this.running = true; h.onStarted?.(); break;
      case 'out': h.onOutput?.(msg.data, false); break;
      case 'err': h.onOutput?.(msg.data, true); break;
      case 'exit': this.running = false; h.onExit?.(msg); this.close(); break;
    }
  }

  input(data) {
    if (this.ws?.readyState === WebSocket.OPEN && this.running) this.ws.send(JSON.stringify({ type: 'input', data }));
  }

  stop() {
    if (this.ws?.readyState === WebSocket.OPEN) this.ws.send(JSON.stringify({ type: 'stop' }));
  }

  close() {
    const ws = this.ws;
    this.ws = null;
    this.running = false;
    if (ws && ws.readyState <= WebSocket.OPEN) {
      try { ws.close(); } catch { /* al gesloten */ }
    }
  }
}
