import { html, useState } from '../../lib/preact.js';
import { Icon } from '../../components/Icon.js';
import { Badge, Callout, Empty } from '../../components/ui.js';

export const CATEGORY = {
  syntax: { label: 'Syntaxfouten', icon: 'alert', tone: 'danger', help: 'De compiler kan je code niet lezen: er ontbreekt bv. een ; of }.' },
  compile: { label: 'Compileerfouten', icon: 'xCircle', tone: 'danger', help: 'De code is leesbaar, maar breekt een regel van C# (bv. verkeerd type, onbekende naam).' },
  security: { label: 'Sandbox', icon: 'shield', tone: 'danger', help: 'Deze code gebruikt iets wat niet toegelaten is in de afgeschermde uitvoeromgeving.' },
  runtime: { label: 'Runtimefouten', icon: 'bug', tone: 'danger', help: 'Het programma compileert, maar crasht tijdens de uitvoering (exception).' },
  logic: { label: 'Logische fouten', icon: 'brain', tone: 'warning', help: 'Het programma werkt, maar berekent of beslist iets verkeerd.' },
  output: { label: 'Foutieve output', icon: 'terminal', tone: 'warning', help: 'Het resultaat klopt bijna: de tekst of opmaak van de output wijkt af.' },
  requirement: { label: 'Opdrachtvereisten', icon: 'target', tone: 'warning', help: 'Je oplossing gebruikt niet de gevraagde aanpak (bv. een for-loop of een method).' },
  structure: { label: 'Slechte structuur', icon: 'layers', tone: 'info', help: 'Je code werkt, maar is moeilijk te lezen, te onderhouden of te hergebruiken.' },
  redundant: { label: 'Onnodige code', icon: 'trash', tone: 'info', help: 'Code die niets bijdraagt en weg mag.' },
  improvement: { label: 'Mogelijke verbeteringen', icon: 'sparkles', tone: 'info', help: 'Suggesties volgens de conventies van de cursus.' },
  warning: { label: 'Waarschuwingen', icon: 'alert', tone: 'warning', help: 'De compiler vermoedt een probleem.' },
};

export function DiagnosticItem({ d, onGoto }) {
  const [open, setOpen] = useState(d.severity === 'error');
  const cat = CATEGORY[d.category] || CATEGORY.compile;
  return html`<div class=${`diag ${d.severity}`}>
    <button class="diag-head" onClick=${() => setOpen(!open)}>
      <${Icon} name=${d.severity === 'error' ? 'xCircle' : d.severity === 'warning' ? 'alert' : 'info'} />
      <span class="diag-msg">${d.message}</span>
      ${d.line ? html`<span class="diag-loc mono" onClick=${(e) => { e.stopPropagation(); onGoto?.(d); }} title="Ga naar regel">${d.file && d.file !== 'Program.cs' ? d.file + ':' : 'r.'}${d.line}</span>` : null}
    </button>
    ${open ? html`<div class="diag-body">
      <div class="diag-cat"><${Badge} tone=${cat.tone}>${cat.label}</${Badge}>${d.code ? html`<span class="faint mono xs">${d.code}</span>` : null}</div>
      ${d.explanation ? html`<p><strong>Waarom?</strong> ${d.explanation}</p>` : null}
      ${d.tip ? html`<p><strong>Oplossing:</strong> ${d.tip}</p>` : null}
      ${d.original && d.original !== d.message ? html`<p class="faint xs mono">Compiler: ${d.original}</p>` : null}
    </div>` : null}
  </div>`;
}

export function DiagnosticsList({ diagnostics, onGoto }) {
  if (!diagnostics?.length) return html`<${Empty} icon="checkCircle" title="Geen problemen">De compiler vindt geen fouten in je code.</${Empty}>`;
  return html`<div class="diag-list">${diagnostics.map((d, i) => html`<${DiagnosticItem} key=${i} d=${d} onGoto=${onGoto} />`)}</div>`;
}

export function TestResults({ result, tests }) {
  if (!result) {
    return html`<div>
      <p class="muted small">Klik op <strong>Controleer</strong> om je code te laten testen. Deze testscenario's worden uitgevoerd:</p>
      ${(tests || []).map((t) => html`<div class="test-preview">
        <div class="row"><${Icon} name=${t.hidden ? 'lock' : 'circle'} size="14" /><strong class="small">${t.name}</strong>${t.hidden ? html`<span class="faint xs">verborgen test</span>` : null}</div>
        ${!t.hidden && t.input ? html`<div class="io-block"><span class="io-label">Invoer</span><pre>${t.input.trimEnd()}</pre></div>` : null}
      </div>`)}
    </div>`;
  }
  if (!result.compiled) return html`<${Callout} tone="danger" title="Niet getest:">Je code compileert niet. Los eerst de fouten op in het tabblad Problemen.</${Callout}>`;
  return html`<div class="test-list">
    <div class="test-summary">
      <strong>${result.tests.filter((t) => t.passed).length}/${result.tests.length}</strong> tests geslaagd
      ${result.rules.length ? html` · <strong>${result.rules.filter((r) => r.passed).length}/${result.rules.length}</strong> vereisten` : null}
    </div>
    ${result.rules.length ? html`<div class="rules">
      ${result.rules.map((r) => html`<div class=${`rule ${r.passed ? 'ok' : 'fail'}`}><${Icon} name=${r.passed ? 'check' : 'x'} size="14" />${r.message}</div>`)}
    </div>` : null}
    ${result.tests.map((t, i) => html`<${TestCard} key=${i} t=${t} />`)}
  </div>`;
}

function TestCard({ t }) {
  const [open, setOpen] = useState(!t.passed);
  const cat = CATEGORY[t.category];
  return html`<div class=${`test-card ${t.passed ? 'pass' : 'fail'}`}>
    <button class="test-head" onClick=${() => setOpen(!open)}>
      <${Icon} name=${t.passed ? 'checkCircle' : 'xCircle'} />
      <span class="grow">${t.name}${t.hidden ? html` <span class="faint xs">(verborgen)</span>` : null}</span>
      ${!t.passed && cat ? html`<${Badge} tone=${cat.tone}>${cat.label}</${Badge}>` : null}
      <${Icon} name=${open ? 'chevronDown' : 'chevronRight'} size="14" />
    </button>
    ${open ? html`<div class="test-body">
      ${t.message ? html`<p class="small">${t.message}</p>` : null}
      ${t.exception ? html`<${ExceptionBox} ex=${t.exception} />` : null}
      ${!t.hidden && t.input ? html`<div class="io-block"><span class="io-label">Invoer</span><pre>${t.input.trimEnd()}</pre></div>` : null}
      ${!t.hidden && t.diff?.length && !t.passed ? html`<div class="io-block"><span class="io-label">Verschil <span class="faint">(− verwacht, + jouw output)</span></span>
        <pre class="diff">${t.diff.map((l) => html`<div class=${`diff-${l.kind}`}>${l.kind === 'expected' ? '− ' : l.kind === 'actual' ? '+ ' : '  '}${l.text || ' '}</div>`)}</pre></div>` : null}
      ${!t.hidden && t.passed && t.actual ? html`<div class="io-block"><span class="io-label">Output</span><pre>${t.actual.trimEnd()}</pre></div>` : null}
      ${t.checks?.length ? html`<div class="checks">${t.checks.map((c) => html`<div class=${`rule ${c.passed ? 'ok' : 'fail'}`}>
        <${Icon} name=${c.passed ? 'check' : 'x'} size="14" /><span class="grow">${c.label}</span>
        ${!c.passed ? html`<span class="mono xs">verwacht ${c.expected} · kreeg ${c.actual}</span>` : null}</div>`)}</div>` : null}
      ${t.fileChecks?.map((f) => html`<div class="io-block"><span class="io-label">Bestand ${f.name} ${f.passed ? '✓' : '✗'}</span>
        ${!f.passed && !t.hidden ? html`<pre class="diff"><div class="diff-expected">verwacht:\n${f.expected}</div><div class="diff-actual">jouw bestand:\n${f.actual ?? '(niet aangemaakt)'}</div></pre>` : null}</div>`)}
    </div>` : null}
  </div>`;
}

export function ExceptionBox({ ex }) {
  return html`<div class="exception-box">
    <div class="row"><${Icon} name="bug" size="16" /><strong>${ex.title}</strong>${ex.line ? html`<span class="mono xs">regel ${ex.line}${ex.method ? ' in ' + ex.method : ''}</span>` : null}</div>
    <p class="small mono faint">${ex.type}: ${ex.message}</p>
    <p class="small"><strong>Wat betekent dit?</strong> ${ex.explanation}</p>
    <p class="small"><strong>Tip:</strong> ${ex.tip}</p>
  </div>`;
}

/** Overzicht van alle feedback, ingedeeld in categorieën. */
export function FeedbackView({ result, onGoto }) {
  if (!result) return html`<${Empty} icon="sparkles" title="Nog geen feedback">Klik op <strong>Controleer</strong>. Je krijgt dan feedback over fouten, output, structuur en verbeterpunten.</${Empty}>`;
  const groups = {};
  const add = (cat, item) => (groups[cat] ??= []).push(item);
  for (const d of result.diagnostics || []) add(d.category, { kind: 'diag', d });
  for (const t of result.tests || []) if (!t.passed) add(t.category, { kind: 'test', t });
  for (const r of result.rules || []) if (!r.passed) add('requirement', { kind: 'rule', r });
  for (const q of result.quality || []) add(q.category, { kind: 'diag', d: q });
  const order = ['syntax', 'compile', 'security', 'runtime', 'logic', 'output', 'requirement', 'warning', 'structure', 'redundant', 'improvement'];
  const present = order.filter((c) => groups[c]?.length);
  return html`<div class="feedback">
    <div class=${`feedback-summary ${result.passed ? 'pass' : 'fail'}`}>
      <${Icon} name=${result.passed ? 'checkCircle' : 'info'} size="22" />
      <div><strong>${result.passed ? 'Geslaagd!' : 'Nog niet geslaagd'}</strong><p>${result.summary}</p></div>
    </div>
    ${present.length === 0 ? html`<p class="muted small">Geen opmerkingen. Netjes!</p>` : null}
    ${present.map((c) => html`<div class="feedback-group">
      <div class="feedback-group-head"><${Icon} name=${CATEGORY[c].icon} size="16" /><strong>${CATEGORY[c].label}</strong><span class="count">${groups[c].length}</span></div>
      <p class="faint xs">${CATEGORY[c].help}</p>
      ${groups[c].map((item, i) => item.kind === 'diag'
        ? html`<${DiagnosticItem} key=${i} d=${item.d} onGoto=${onGoto} />`
        : item.kind === 'test'
          ? html`<div class="diag warning"><div class="diag-head"><${Icon} name="xCircle" /><span class="diag-msg"><strong>${item.t.hidden ? 'Verborgen test' : item.t.name}:</strong> ${item.t.message}</span>
              ${item.t.exception?.line ? html`<span class="diag-loc mono" onClick=${() => onGoto?.({ line: item.t.exception.line, file: item.t.exception.file })}>r.${item.t.exception.line}</span>` : null}</div>
              ${item.t.exception ? html`<div class="diag-body"><p>${item.t.exception.explanation}</p><p><strong>Tip:</strong> ${item.t.exception.tip}</p></div>` : null}</div>`
          : html`<div class="diag warning"><div class="diag-head"><${Icon} name="target" /><span class="diag-msg">${item.r.message}</span></div></div>`)}
    </div>`)}
  </div>`;
}

export function FilesView({ files, initial, onReset }) {
  const names = Object.keys(files || {});
  return html`<div>
    <p class="muted small">Bestanden in de werkmap van je programma (de sandbox). Wat je programma schrijft, verschijnt hier en blijft bewaard voor de volgende uitvoering.</p>
    ${names.length === 0 ? html`<p class="faint small">Nog geen bestanden.</p>` : names.map((n) => html`<div class="file-preview">
      <div class="row between"><span class="row"><${Icon} name="file" size="14" /><strong class="mono small">${n}</strong></span><span class="faint xs">${(files[n] || '').length} tekens</span></div>
      <pre>${files[n]}</pre>
    </div>`)}
    ${onReset ? html`<button class="btn btn-sm" onClick=${onReset}><${Icon} name="rotate" />Beginsituatie herstellen</button>` : null}
    ${initial && Object.keys(initial).length ? html`<p class="faint xs">Beginsituatie: ${Object.keys(initial).join(', ')}</p>` : null}
  </div>`;
}
