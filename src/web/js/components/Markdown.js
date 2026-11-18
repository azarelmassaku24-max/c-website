import { html, useEffect, useMemo, useRef, useState } from '../lib/preact.js';
import { marked } from 'marked';
import { Icon } from './Icon.js';
import { Button, Modal } from './ui.js';
import { colorize } from '../features/code-editor/monaco.js';
import { codeService } from '../services/codeService.js';
import { engine } from '../progress/engine.js';
import { TraceViewer } from '../features/debugger/TraceViewer.js';
import { navigate } from '../app/router.js';

marked.setOptions({ gfm: true, breaks: false });

/** Zet markdown om in HTML met callouts in GitHub-stijl: > [!TIP], > [!NOTE], > [!WARNING]. */
function renderHtml(md) {
  let out = marked.parse(md || '');
  out = out.replace(/<blockquote>\s*<p>\[!(TIP|NOTE|INFO|WARNING|BELANGRIJK|LET OP|DANGER)\]\s*/gi, (_, kind) => {
    const k = kind.toUpperCase();
    const tone = k === 'TIP' ? 'tip' : k === 'WARNING' || k === 'BELANGRIJK' || k === 'LET OP' ? 'warn' : k === 'DANGER' ? 'danger' : 'info';
    const label = { tip: 'Tip', warn: 'Belangrijk', danger: 'Let op', info: 'Goed om te weten' }[tone];
    return `<blockquote class="callout-quote ${tone}"><p><strong class="callout-label">${label}</strong> `;
  });
  return out;
}

/** Splitst markdown in tekst en codeblokken zodat codeblokken interactief kunnen zijn. */
function split(md) {
  const parts = [];
  const lines = (md || '').split('\n');
  let buffer = [];
  let code = null;
  for (const line of lines) {
    const fence = line.match(/^```(\S*)\s*(.*)$/);
    if (fence && code === null) {
      if (buffer.length) parts.push({ type: 'md', text: buffer.join('\n') });
      buffer = [];
      code = { lang: fence[1] || 'text', info: fence[2] || '', lines: [] };
    } else if (line.startsWith('```') && code !== null) {
      parts.push({ type: 'code', ...code, code: code.lines.join('\n') });
      code = null;
    } else if (code !== null) code.lines.push(line);
    else buffer.push(line);
  }
  if (code) parts.push({ type: 'code', ...code, code: code.lines.join('\n') });
  if (buffer.length) parts.push({ type: 'md', text: buffer.join('\n') });
  return parts;
}

function parseInfo(info) {
  const attrs = {};
  const re = /(\w[\w-]*)(?:="([^"]*)")?/g;
  let m;
  while ((m = re.exec(info))) attrs[m[1]] = m[2] ?? true;
  return attrs;
}

export function Markdown({ text, class: cls = '', interactive = true }) {
  const parts = useMemo(() => split(text), [text]);
  return html`<div class=${`prose ${cls}`}>
    ${parts.map((p, i) => p.type === 'md'
      ? html`<div key=${i} dangerouslySetInnerHTML=${{ __html: renderHtml(p.text) }}></div>`
      : html`<${CodeBlock} key=${i} code=${p.code} lang=${p.lang} info=${parseInfo(p.info)} interactive=${interactive} />`)}
  </div>`;
}

export function wrapSnippet(code) {
  if (/static\s+void\s+Main/.test(code) || /\bclass\s+\w+/.test(code) || /^\s*namespace\s/m.test(code)) return code;
  return `using System;\nusing System.Collections.Generic;\n\nclass Program {\n    static void Main() {\n${code.split('\n').map((l) => '        ' + l).join('\n')}\n    }\n}\n`;
}

/**
 * Codeblok met syntaxkleuring en acties.
 * Info-attributen na de taal: run (uitvoeren), trace (stap voor stap), lab (openen in Code Lab),
 * invoer="12\n5" (invoer voor uitvoeren/traceren), nooutput (geen output-knop).
 */
export function CodeBlock({ code, lang = 'csharp', info = {}, interactive = true }) {
  const ref = useRef(null);
  const [output, setOutput] = useState(null);
  const [busy, setBusy] = useState(null);
  const [trace, setTrace] = useState(null);
  const [copied, setCopied] = useState(false);
  const isCs = lang === 'csharp' || lang === 'cs' || lang === 'c#';
  const stdin = (info.invoer || '').replace(/\\n/g, '\n');

  useEffect(() => {
    if (!isCs) return;
    let alive = true;
    colorize(code).then((h) => { if (alive && ref.current) ref.current.innerHTML = h; }).catch(() => {});
    return () => { alive = false; };
  }, [code]);

  const files = () => [{ path: 'Program.cs', content: wrapSnippet(code) }];

  async function run() {
    setBusy('run');
    try {
      const res = await codeService.run(files(), { stdin });
      engine.recordRun({ files: res.run?.files });
      if (!res.compile.success) setOutput({ error: true, text: res.compile.diagnostics.filter((d) => d.severity === 'error').map((d) => `Regel ${d.line}: ${d.message}`).join('\n') });
      else {
        let text = stripAnsi(res.run.output || '');
        if (stdin) text = text; // invoer wordt meegeëchood door de runtime
        if (res.run.exception) text += `\n⚠ ${res.run.exception.title}${res.run.exception.line ? ` (regel ${res.run.exception.line})` : ''}`;
        if (res.run.message) text += `\n⚠ ${res.run.message}`;
        setOutput({ error: !!res.run.exception, text: text || '(geen output)' });
      }
    } catch (e) {
      setOutput({ error: true, text: e.message });
    } finally { setBusy(null); }
  }

  async function startTrace() {
    setTrace({ files: files(), stdin });
  }

  function copy() {
    navigator.clipboard?.writeText(code).then(() => { setCopied(true); setTimeout(() => setCopied(false), 1200); });
  }

  function openLab() {
    sessionStorage.setItem('csq.lab.import', JSON.stringify({ files: files(), stdin }));
    navigate('/lab');
  }

  const canRun = interactive && isCs && !info.norun;
  return html`<div class="codeblock">
    <div class="codeblock-bar">
      <span class="codeblock-lang">${isCs ? 'C#' : lang}${info.titel ? ` · ${info.titel}` : ''}</span>
      <div class="codeblock-actions">
        ${canRun ? html`<${Button} size="sm" variant="ghost" icon="play" loading=${busy === 'run'} onClick=${run} title="Uitvoeren">Uitvoeren</${Button}>` : null}
        ${canRun ? html`<${Button} size="sm" variant="ghost" icon="layers" onClick=${startTrace} title="Stap voor stap door de code">Stap voor stap</${Button}>` : null}
        ${canRun ? html`<${Button} size="sm" variant="ghost" icon="external" onClick=${openLab} title="Openen in Code Lab" />` : null}
        <${Button} size="sm" variant="ghost" icon=${copied ? 'check' : 'copy'} onClick=${copy} title="Kopiëren" />
      </div>
    </div>
    <pre><code ref=${ref}>${code}</code></pre>
    ${output ? html`<div class="codeblock-output"><span class="label">${output.error ? 'Fout' : 'Output'}</span>${output.text}</div>` : null}
    ${trace ? html`<${Modal} title="Stap voor stap door de code" wide onClose=${() => setTrace(null)}>
      <${TraceViewer} files=${trace.files} stdin=${trace.stdin} autoStart />
    </${Modal}>` : null}
  </div>`;
}

export function stripAnsi(s) {
  return s.replace(/\u001b\[[0-9;?]*[A-Za-z]/g, (m) => (m.endsWith('J') ? '\n[scherm gewist]\n' : '')).replace(/\u0007/g, '');
}

export function inlineMd(text) {
  return html`<span dangerouslySetInnerHTML=${{ __html: marked.parseInline(text || '') }}></span>`;
}

export { Icon };
