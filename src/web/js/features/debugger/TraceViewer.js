import { html, useEffect, useMemo, useRef, useState } from '../../lib/preact.js';
import { Icon } from '../../components/Icon.js';
import { Button, Tabs, Callout } from '../../components/ui.js';
import { codeService } from '../../services/codeService.js';
import { engine } from '../../progress/engine.js';
import { buildTrace, stepInto, stepOver, stepOut, stepBack, continueTo, traceTable, methodsIn } from './trace.js';
import { CodeView } from './CodeView.js';

/**
 * Debugger op basis van een echte uitvoering: stap door elke regel, bekijk variabelen,
 * de call stack (activatierecords), output en een automatisch opgestelde traceertabel.
 * Werkt zelfstandig (met eigen codeweergave) of gekoppeld aan de editor (showCode=false + onLocation).
 */
export function TraceViewer({ files, stdin: initialStdin = '', autoStart, showCode = true, breakpoints: externalBreakpoints, onLocation, initialFiles, mainType, onStopped }) {
  const [stdin, setStdin] = useState(initialStdin);
  const [state, setState] = useState({ phase: 'idle' });
  const [index, setIndex] = useState(0);
  const [tab, setTab] = useState('vars');
  const [ownBreakpoints, setOwnBreakpoints] = useState(new Set());
  const [file, setFile] = useState(files[0]?.path || 'Program.cs');
  const [tableMethod, setTableMethod] = useState(null);
  const stats = useRef({ steps: 0, hit: false });
  const breakpoints = externalBreakpoints || ownBreakpoints;

  useEffect(() => { if (autoStart) start(); }, []);
  useEffect(() => () => {
    if (stats.current.steps > 0) engine.recordDebug({ steps: stats.current.steps, breakpointHit: stats.current.hit });
  }, []);

  async function start() {
    setState({ phase: 'running' });
    try {
      const res = await codeService.trace(files, { stdin, mainType, initialFiles });
      if (!res.compile.success) { setState({ phase: 'compile', diagnostics: res.compile.diagnostics }); return; }
      const trace = buildTrace(res.events || [], res.variables || []);
      engine.recordTrace();
      setState({ phase: 'ready', trace, run: res.run, warning: res.warning });
      setTableMethod(trace.steps.find((s) => s.frames.length)?.frames[0]?.method || null);
      let first = 0;
      if (breakpoints.size) {
        const r = continueTo(trace, -1, breakpoints, null);
        if (r.hit) { first = r.index; stats.current.hit = true; }
      }
      go(first, trace);
    } catch (e) {
      setState({ phase: 'error', message: e.message });
    }
  }

  const trace = state.trace;
  const step = trace?.steps[index];

  function go(i, t = trace) {
    if (!t) return;
    const next = Math.max(0, Math.min(t.steps.length - 1, i));
    setIndex(next);
    stats.current.steps++;
    const s = t.steps[next];
    if (s) {
      setFile(s.file);
      onLocation?.({ path: s.file, line: s.line });
    }
  }

  useEffect(() => () => onLocation?.(null), []);

  // Sneltoetsen zoals Visual Studio: F10 (over), F11 (into), Shift+F11 (out), F5 (doorgaan)
  useEffect(() => {
    if (!trace) return;
    const onKey = (e) => {
      if (e.target.closest?.('input, textarea, .monaco-editor')) return;
      if (e.key === 'F10') { e.preventDefault(); go(stepOver(trace, index)); }
      else if (e.key === 'F11' && e.shiftKey) { e.preventDefault(); go(stepOut(trace, index)); }
      else if (e.key === 'F11') { e.preventDefault(); go(stepInto(trace, index)); }
      else if (e.key === 'F5') { e.preventDefault(); cont(); }
      else if (e.key === 'ArrowLeft' && e.altKey) { e.preventDefault(); go(stepBack(trace, index)); }
    };
    addEventListener('keydown', onKey);
    return () => removeEventListener('keydown', onKey);
  });

  function cont() {
    const r = continueTo(trace, index, breakpoints, null);
    if (r.hit) stats.current.hit = true;
    go(r.index);
  }

  function toggleBp(line) {
    const s = new Set(ownBreakpoints);
    s.has(line) ? s.delete(line) : s.add(line);
    setOwnBreakpoints(s);
  }

  const codeOf = (path) => files.find((f) => f.path === path)?.content || files[0]?.content || '';
  const lineText = (s) => (codeOf(s.file).split('\n')[s.line - 1] || '').trim();
  const table = useMemo(() => (trace && tableMethod ? traceTable(trace, tableMethod) : null), [trace, tableMethod]);

  if (state.phase === 'idle' || state.phase === 'compile' || state.phase === 'error' || state.phase === 'running') {
    return html`<div class="trace-setup">
      <p class="muted small">De debugger voert je programma echt uit en registreert elke stap. Geef hieronder de invoer die je programma nodig heeft (één regel per Console.ReadLine).</p>
      <label class="label" for="trace-stdin">Invoer (stdin)</label>
      <textarea id="trace-stdin" class="textarea" rows="3" value=${stdin} onInput=${(e) => setStdin(e.currentTarget.value)} placeholder="bv.\n12\n5"></textarea>
      <div class="row" style="margin-top:10px">
        <${Button} variant="primary" icon="bug" loading=${state.phase === 'running'} onClick=${start}>Start debugger</${Button}>
        <span class="kbd-hint">Tip: zet breakpoints door in de marge naast de regelnummers te klikken.</span>
      </div>
      ${state.phase === 'compile' ? html`<${Callout} tone="danger" title="Je code compileert niet.">
        ${state.diagnostics.filter((d) => d.severity === 'error').slice(0, 4).map((d) => html`<div>Regel ${d.line}: ${d.message}</div>`)}
      </${Callout}>` : null}
      ${state.phase === 'error' ? html`<${Callout} tone="danger">${state.message}</${Callout}>` : null}
    </div>`;
  }

  const frame = step?.frames[step.frames.length - 1];
  const description = describe(step, lineText);
  const output = trace.output.slice(0, step?.outputEnd ?? 0);

  return html`<div class=${`debugger ${showCode ? '' : 'compact'}`}>
    <div class="debug-toolbar">
      <${Button} size="sm" icon="rotate" onClick=${() => { setState({ phase: 'idle' }); }} title="Opnieuw (andere invoer)" />
      <div class="debug-sep"></div>
      <${Button} size="sm" icon="skipBack" onClick=${() => go(0)} title="Naar begin" />
      <${Button} size="sm" icon="chevronLeft" onClick=${() => go(stepBack(trace, index))} title="Stap terug (Alt+←)" />
      <${Button} size="sm" icon="stepInto" onClick=${() => go(stepInto(trace, index))} title="Step Into (F11)">Into</${Button}>
      <${Button} size="sm" icon="stepOver" onClick=${() => go(stepOver(trace, index))} title="Step Over (F10)">Over</${Button}>
      <${Button} size="sm" icon="stepOut" onClick=${() => go(stepOut(trace, index))} title="Step Out (Shift+F11)">Out</${Button}>
      <${Button} size="sm" variant="primary" icon="play" onClick=${cont} title="Doorgaan tot volgend breakpoint (F5)">Doorgaan</${Button}>
      <span class="debug-pos mono">${index + 1}/${trace.steps.length}</span>
    </div>
    <input class="debug-slider" type="range" min="0" max=${Math.max(0, trace.steps.length - 1)} value=${index} onInput=${(e) => go(Number(e.currentTarget.value))} aria-label="Tijdlijn" />
    <div class=${`debug-explain ${step?.kind}`}>
      <${Icon} name=${step?.kind === 'exception' ? 'alert' : step?.kind === 'cond' ? (step.result ? 'check' : 'x') : step?.kind === 'enter' ? 'stepInto' : step?.kind === 'return' || step?.kind === 'exit' ? 'stepOut' : 'arrowRight'} />
      <span>${description}</span>
    </div>
    ${state.warning ? html`<${Callout} tone="warn">${state.warning}</${Callout}>` : null}
    ${trace.truncated ? html`<${Callout} tone="warn">De trace werd ingekort (te veel stappen). Je ziet de eerste ${trace.steps.length} stappen.</${Callout}>` : null}
    <div class="debug-body">
      ${showCode ? html`<div class="debug-code">
        ${files.length > 1 ? html`<div class="debug-files">${files.map((f) => html`<button class=${`chip ${f.path === file ? 'active' : ''}`} onClick=${() => setFile(f.path)}>${f.path}</button>`)}</div>` : null}
        <${CodeView} code=${codeOf(file)} currentLine=${step?.file === file ? step.line : 0} breakpoints=${breakpoints} onToggleBreakpoint=${externalBreakpoints ? null : toggleBp}
          lineNote=${step?.kind === 'cond' ? `→ ${step.result}` : step?.kind === 'return' ? `↩ ${step.value}` : null} />
      </div>` : null}
      <div class="debug-side">
        <${Tabs} active=${tab} onChange=${setTab} tabs=${[
          { id: 'vars', label: 'Variabelen', icon: 'list' },
          { id: 'stack', label: 'Call Stack', icon: 'layers', count: step?.frames.length || 0 },
          { id: 'table', label: 'Traceertabel', icon: 'database' },
          { id: 'out', label: 'Output', icon: 'terminal' },
        ]} />
        <div class="debug-panel">
          ${tab === 'vars' ? html`<${Variables} frame=${frame} changed=${step?.changed || []} />` : null}
          ${tab === 'stack' ? html`<${CallStack} frames=${step?.frames || []} />` : null}
          ${tab === 'table' ? html`<${TraceTable} table=${table} methods=${methodsIn(trace)} method=${tableMethod} onMethod=${setTableMethod} current=${index} onPick=${go} />` : null}
          ${tab === 'out' ? html`<pre class="debug-output">${output || '(nog geen output)'}</pre>` : null}
        </div>
      </div>
    </div>
    ${index === trace.steps.length - 1 ? html`<div class="debug-done">
      ${state.run?.exception ? html`<${Callout} tone="danger" title=${state.run.exception.title}>${state.run.exception.explanation}</${Callout}>`
        : html`<span class="success-text"><${Icon} name="checkCircle" size="16" /> Programma beëindigd.</span>`}
    </div>` : null}
  </div>`;
}

function describe(step, lineText) {
  if (!step) return '';
  const code = html`<code>${lineText(step)}</code>`;
  switch (step.kind) {
    case 'enter': return html`Methodactivatie: <strong>${step.method}</strong> start (nieuw activatierecord op de call stack).`;
    case 'exit': return html`<strong>${step.fromMethod}</strong> is klaar${step.returned !== undefined ? html` en gaf <code>${step.returned}</code> terug` : ''}. Terug in de oproepende method.`;
    case 'line': return html`Regel ${step.line} wordt uitgevoerd: ${code}`;
    case 'values': return html`Na regel ${step.line}: ${step.changed.length ? step.changed.map((n, i) => html`${i ? ', ' : ''}<code>${n}</code>`) : 'geen variabelen gewijzigd'}${step.changed.length ? ' kreeg een (nieuwe) waarde.' : ''}`;
    case 'cond': return html`Voorwaarde op regel ${step.line} (${code}) is <strong class=${step.result ? 'success-text' : 'danger-text'}>${String(step.result)}</strong>${step.result ? '' : ' → de code in het block wordt overgeslagen / de loop stopt.'}`;
    case 'return': return html`return op regel ${step.line}: de method geeft <code>${step.value}</code> terug.`;
    case 'sleep': return html`Het programma pauzeert ${step.ms} ms (Thread.Sleep).`;
    case 'exception': return html`<strong class="danger-text">Exception: ${step.type}</strong> — ${step.message}`;
    default: return '';
  }
}

function Variables({ frame, changed }) {
  if (!frame) return html`<p class="muted small">Geen actieve method.</p>`;
  const entries = Object.entries(frame.vars);
  return html`<div>
    <div class="debug-frame-title"><${Icon} name="box" size="14" /> ${frame.method}</div>
    ${entries.length === 0 ? html`<p class="muted small">Nog geen variabelen in deze method.</p>` : html`
    <table class="vars-table">
      <thead><tr><th>Naam</th><th>Type</th><th>Waarde</th></tr></thead>
      <tbody>${entries.map(([name, val]) => html`<tr class=${changed.includes(name) ? 'changed' : ''}>
        <td class="mono">${name}</td><td class="mono faint">${val.t}</td><td class="mono value">${val.v}</td>
      </tr>`)}</tbody>
    </table>`}
    ${frame.ret !== undefined ? html`<p class="small">Return-waarde: <code>${frame.ret}</code></p>` : null}
  </div>`;
}

function CallStack({ frames }) {
  return html`<div class="callstack">
    <p class="muted xs">Bovenaan staat de method die nu actief is. Elk blok is een activatierecord met eigen lokale variabelen.</p>
    ${[...frames].reverse().map((f, i) => html`<div class=${`stack-frame ${i === 0 ? 'active' : ''}`}>
      <div class="row between"><strong class="mono">${f.method}</strong><span class="faint xs">regel ${f.line}</span></div>
      <div class="stack-vars">${Object.entries(f.vars).map(([n, v]) => html`<span class="mono">${n} = ${v.v}</span>`)}</div>
    </div>`)}
  </div>`;
}

function TraceTable({ table, methods, method, onMethod, current, onPick }) {
  if (!table) return html`<p class="muted small">Geen gegevens.</p>`;
  return html`<div>
    <div class="row wrap" style="margin-bottom:8px">
      <span class="small muted">Method:</span>
      <select class="select" style="width:auto;height:30px" value=${method} onChange=${(e) => onMethod(e.currentTarget.value)}>
        ${methods.map((m) => html`<option value=${m}>${m}</option>`)}
      </select>
    </div>
    <div class="trace-table-wrap">
      <table class="trace-table">
        <thead><tr><th>Stap</th><th>Regel</th>${table.columns.map((c) => html`<th class="mono">${c}</th>`)}<th>Output</th></tr></thead>
        <tbody>${table.rows.map((r, i) => html`<tr class=${r.step === current || (i + 1 < table.rows.length && r.step < current && table.rows[i + 1].step > current) ? 'current' : ''} onClick=${() => onPick(r.step)}>
          <td class="faint">${i + 1}</td>
          <td>${r.line}${r.kind === 'cond' ? html` <span class=${r.result ? 'success-text' : 'danger-text'}>${r.result ? '✓' : '✗'}</span>` : ''}</td>
          ${table.columns.map((c) => html`<td class=${`mono ${r.changed?.includes(c) ? 'changed' : ''}`}>${r.vars[c]?.v ?? ''}</td>`)}
          <td class="mono out">${r.output.replace(/\n$/, '').replace(/\n/g, '⏎')}</td>
        </tr>`)}</tbody>
      </table>
    </div>
  </div>`;
}
