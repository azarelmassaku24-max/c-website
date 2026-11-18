import { html, useEffect, useMemo, useRef, useState, useCallback } from '../../lib/preact.js';
import { Icon } from '../../components/Icon.js';
import { Button, Tabs, Segmented, Spinner, ErrorBox, Callout } from '../../components/ui.js';
import { CodeEditor } from '../code-editor/CodeEditor.js';
import { Terminal } from '../terminal/Terminal.js';
import { TraceViewer } from '../debugger/TraceViewer.js';
import { TaskPanel } from './TaskPanel.js';
import { DiagnosticsList, TestResults, FeedbackView, FilesView, ExceptionBox } from './FeedbackPanels.js';
import { codeService, InteractiveRun } from '../../services/codeService.js';
import { curriculumService } from '../../services/curriculumService.js';
import { engine } from '../../progress/engine.js';
import { store } from '../../progress/store.js';
import { toast, floatXp } from '../../components/Notifications.js';
import { settings } from '../../progress/settings.js';

const STARTER_PLAYGROUND = [{ path: 'Program.cs', content: `using System;

namespace CodeLab {
    class Program {
        static void Main(string[] args) {
            Console.Write("Hoe heet je? ");
            string naam = Console.ReadLine();
            Console.WriteLine($"Welkom in het Code Lab, {naam}!");
        }
    }
}
` }];

let knowledgeCache = null;

/**
 * De programmeeromgeving met drie zones.
 * Props: exerciseId (of null voor de vrije playground), onGraded(result, events), embedded (in een les), daily
 */
export function Workbench({ exerciseId, onGraded, embedded, daily, initial }) {
  const [exercise, setExercise] = useState(null);
  const [loadError, setLoadError] = useState(null);
  useEffect(() => {
    setExercise(null);
    setLoadError(null);
    if (!exerciseId) return;
    curriculumService.exercise(exerciseId).then(setExercise, setLoadError);
  }, [exerciseId]);

  if (loadError) return html`<${ErrorBox} error=${loadError} />`;
  if (exerciseId && !exercise) return html`<div class="workbench-loading"><${Spinner} label="Opdracht laden…" /></div>`;
  return html`<${WorkbenchInner} key=${exerciseId || 'lab'} exercise=${exercise} onGraded=${onGraded} embedded=${embedded} daily=${daily} initial=${initial} />`;
}

function WorkbenchInner({ exercise, onGraded, embedded, daily, initial }) {
  const id = exercise?.summary.id;
  const summary = id ? curriculumService.get()?.exercises.get(id) : null;
  const saved = id ? store.get().exercises[id]?.files : store.get().lab?.files;
  const [files, setFiles] = useState(() => initial?.files || saved || exercise?.starter || STARTER_PLAYGROUND);
  const [activePath, setActivePath] = useState(() => (initial?.files || saved || exercise?.starter || STARTER_PLAYGROUND)[0].path);
  const [diagnostics, setDiagnostics] = useState([]);
  const [entryPoints, setEntryPoints] = useState([]);
  const [mainType, setMainType] = useState(exercise?.mainType || null);
  const [rightTab, setRightTab] = useState('console');
  const [running, setRunning] = useState(false);
  const [grading, setGrading] = useState(false);
  const [result, setResult] = useState(null);
  const [sandboxFiles, setSandboxFiles] = useState(() => ({ ...(exercise?.files || {}) }));
  const [breakpoints, setBreakpoints] = useState({});
  const [debugLoc, setDebugLoc] = useState(null);
  const [debugKey, setDebugKey] = useState(0);
  const [reveal, setReveal] = useState(null);
  const [mobilePane, setMobilePane] = useState('code');
  const [showTask, setShowTask] = useState(true);
  const [knowledge, setKnowledge] = useState(knowledgeCache);
  const [lastExit, setLastExit] = useState(null);
  const term = useRef(null);
  const runner = useRef(null);
  const hadInput = useRef(false);
  const saveTimer = useRef(null);

  useEffect(() => {
    if (!knowledgeCache) curriculumService.knowledge().then((k) => { knowledgeCache = k.entries; setKnowledge(k.entries); }).catch(() => {});
    return () => runner.current?.close();
  }, []);

  // Code automatisch bewaren
  useEffect(() => {
    clearTimeout(saveTimer.current);
    saveTimer.current = setTimeout(() => (id ? engine.saveDraft(id, files) : engine.saveLab(files)), 900);
  }, [files]);

  const onDiagnostics = useCallback((list, eps) => { setDiagnostics(list); setEntryPoints(eps); }, []);
  const errors = diagnostics.filter((d) => d.severity === 'error');

  // ------------------------------------------------------------ uitvoeren
  function run() {
    if (running) { runner.current?.stop(); return; }
    setRightTab('console');
    setMobilePane('output');
    setLastExit(null);
    hadInput.current = false;
    const t = term.current;
    t?.clear();
    t?.write('\x1b[90m● Compileren…\x1b[0m\r\n');
    const r = new InteractiveRun({
      onCompiled: (msg) => {
        if (!msg.success) {
          t?.write('\x1b[31m✗ Je code compileert niet.\x1b[0m\r\n\r\n');
          for (const d of msg.diagnostics.filter((x) => x.severity === 'error').slice(0, 6))
            t?.write(`\x1b[31m  Regel ${d.line}:\x1b[0m ${d.message}\r\n\x1b[90m  → ${d.tip || ''}\x1b[0m\r\n`);
          setDiagnostics(msg.diagnostics);
          setRunning(false);
          setRightTab('problems');
          return;
        }
        if (msg.entryPoints) setEntryPoints(msg.entryPoints);
      },
      onStarted: () => { t?.write(`\x1b[90m● Programma gestart\x1b[0m\r\n\r\n`); t?.focus(); },
      onOutput: (data, isErr) => t?.write(isErr ? `\x1b[31m${data}\x1b[0m` : data),
      onExit: (msg) => {
        setRunning(false);
        setLastExit(msg);
        const secs = (msg.durationMs / 1000).toLocaleString('nl-BE', { maximumFractionDigits: 1 });
        if (msg.status === 'stopped') t?.write(`\r\n\x1b[90m■ Gestopt na ${secs} s\x1b[0m\r\n`);
        else if (msg.exception) {
          t?.write(`\r\n\x1b[31m✗ ${msg.exception.title}${msg.exception.line ? ` (regel ${msg.exception.line})` : ''}\x1b[0m\r\n\x1b[90m  ${msg.exception.message}\x1b[0m\r\n`);
          if (msg.exception.line) setReveal({ path: msg.exception.file || activePath, line: msg.exception.line, t: Date.now() });
        } else if (msg.status !== 'ok') t?.write(`\r\n\x1b[33m⚠ ${msg.message || msg.status}\x1b[0m\r\n`);
        else t?.write(`\r\n\x1b[90m✓ Programma beëindigd (exitcode ${msg.exitCode}) · ${secs} s\x1b[0m\r\n`);
        if (msg.files) setSandboxFiles(msg.files);
        engine.recordRun({ interactive: hadInput.current, files: Object.keys(msg.files || {}).length > Object.keys(exercise?.files || {}).length ? msg.files : {} });
      },
      onError: (m) => { t?.write(`\r\n\x1b[31m${m}\x1b[0m\r\n`); setRunning(false); },
      onClose: () => setRunning(false),
    });
    runner.current = r;
    setRunning(true);
    r.start({ files, mainType, cols: t?.cols || 100, rows: t?.rows || 30, initialFiles: sandboxFiles });
  }

  function onTermData(data) {
    if (runner.current?.running) { hadInput.current = true; runner.current.input(data); }
  }

  // ------------------------------------------------------------ controleren
  async function grade() {
    if (!exercise) return;
    setGrading(true);
    setMobilePane('output');
    try {
      const res = await codeService.grade(id, files);
      setResult(res);
      const evs = engine.recordGrade(summary || { ...exercise.summary, lesson: null }, res, files, { daily });
      const xp = evs.filter((e) => e.type === 'xp').reduce((s, e) => s + e.amount, 0);
      if (xp > 0) floatXp(xp);
      setRightTab(!res.compiled ? 'problems' : res.passed ? 'feedback' : res.tests.some((t) => !t.passed) ? 'tests' : 'feedback');
      if (!res.compiled) setDiagnostics(res.diagnostics);
      if (res.passed) toast('Geslaagd!', res.summary, 'success', 'checkCircle');
      onGraded?.(res, evs);
    } catch (e) {
      toast('Controleren mislukt', e.message, 'danger', 'alert');
    } finally {
      setGrading(false);
    }
  }

  // ------------------------------------------------------------ bestanden
  function addFile() {
    const name = prompt('Naam van het nieuwe bestand (bv. Persoon.cs):', 'Klasse.cs');
    if (!name) return;
    const path = name.endsWith('.cs') ? name : name + '.cs';
    if (files.some((f) => f.path === path)) { toast('Bestaat al', `Er is al een bestand ${path}.`, 'danger', 'alert'); return; }
    const cls = path.split('/').pop().replace(/\.cs$/, '').replace(/[^A-Za-z0-9_]/g, '');
    const ns = (files[0].content.match(/namespace\s+([\w.]+)/) || [])[1];
    const body = ns ? `namespace ${ns} {\n    class ${cls} {\n        \n    }\n}\n` : `class ${cls} {\n    \n}\n`;
    setFiles([...files, { path, content: body }]);
    setActivePath(path);
  }

  function removeFile(path) {
    if (files.length <= 1) return;
    if (!confirm(`Bestand ${path} verwijderen?`)) return;
    const next = files.filter((f) => f.path !== path);
    setFiles(next);
    if (activePath === path) setActivePath(next[0].path);
  }

  function renameFile(path) {
    const name = prompt('Nieuwe naam:', path);
    if (!name || name === path) return;
    const target = name.endsWith('.cs') ? name : name + '.cs';
    setFiles(files.map((f) => (f.path === path ? { ...f, path: target } : f)));
    if (activePath === path) setActivePath(target);
  }

  function resetCode() {
    if (!confirm('Je code terugzetten naar de startcode? Je huidige code gaat verloren.')) return;
    const start = exercise?.starter || STARTER_PLAYGROUND;
    setFiles(start.map((f) => ({ ...f })));
    setActivePath(start[0].path);
    setResult(null);
    setSandboxFiles({ ...(exercise?.files || {}) });
  }

  function toggleBreakpoint(line) {
    const set = new Set(breakpoints[activePath] || []);
    set.has(line) ? set.delete(line) : set.add(line);
    setBreakpoints({ ...breakpoints, [activePath]: set });
  }

  function startDebug() {
    setRightTab('debug');
    setMobilePane('output');
    setDebugKey((k) => k + 1);
  }

  const goto = (d) => { if (d?.line) { setReveal({ path: d.file || activePath, line: d.line, col: d.column, t: Date.now() }); setMobilePane('code'); } };

  // Sneltoetsen
  useEffect(() => {
    const onKey = (e) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') { e.preventDefault(); if (e.shiftKey) grade(); else run(); }
      else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') { e.preventDefault(); id ? engine.saveDraft(id, files) : engine.saveLab(files); toast('Opgeslagen', 'Je code is bewaard.', 'success', 'check'); }
    };
    addEventListener('keydown', onKey);
    return () => removeEventListener('keydown', onKey);
  });

  const allBreakpoints = useMemo(() => breakpoints[activePath] || new Set(), [breakpoints, activePath]);
  const failingTests = result?.tests?.filter((t) => !t.passed).length || 0;
  const qualityCount = result ? (result.quality?.length || 0) : 0;

  return html`<div class=${`workbench ${embedded ? 'embedded' : ''} ${showTask ? '' : 'no-task'}`} data-pane=${mobilePane}>
    <div class="wb-mobile-switch">
      <${Segmented} value=${mobilePane} onChange=${setMobilePane} options=${[
        { value: 'task', label: exercise ? 'Opdracht' : 'Info' }, { value: 'code', label: 'Code' }, { value: 'output', label: 'Output' }]} />
    </div>

    ${showTask ? html`<aside class="wb-left">
      ${exercise
        ? html`<${TaskPanel} exercise=${exercise} summary=${{ lesson: summary?.lesson, skillTitle: (s) => curriculumService.get()?.skillMap.get(s)?.title }} result=${result} knowledge=${knowledge}
            onLoadSolution=${(sol) => { setFiles(sol.map((f) => ({ ...f }))); setActivePath(sol[0].path); }} />`
        : html`<${PlaygroundInfo} onTemplate=${(f) => { setFiles(f); setActivePath(f[0].path); }} />`}
    </aside>` : null}

    <section class="wb-center">
      <div class="wb-toolbar">
        <${Button} size="sm" variant="ghost" icon="sidebar" onClick=${() => setShowTask(!showTask)} title=${showTask ? 'Opdracht verbergen' : 'Opdracht tonen'} class="hide-sm" />
        <div class="file-tabs">
          ${files.map((f) => html`<div class=${`file-tab ${f.path === activePath ? 'active' : ''}`} key=${f.path}>
            <button onClick=${() => setActivePath(f.path)} onDblClick=${() => renameFile(f.path)} title="Dubbelklik om te hernoemen">
              <${Icon} name="fileCode" size="14" />${f.path}
              ${errors.some((d) => d.file === f.path) ? html`<span class="tab-dot"></span>` : null}
            </button>
            ${files.length > 1 ? html`<button class="file-close" onClick=${() => removeFile(f.path)} aria-label=${`Sluit ${f.path}`}><${Icon} name="x" size="12" /></button>` : null}
          </div>`)}
          <button class="file-add" onClick=${addFile} title="Nieuw bestand"><${Icon} name="plus" size="14" /></button>
        </div>
        <div class="wb-actions">
          ${entryPoints.length > 1 ? html`<select class="select startup" value=${mainType || ''} onChange=${(e) => setMainType(e.currentTarget.value || null)} title="Opstartobject (startup object)">
            <option value="">Opstartobject…</option>
            ${entryPoints.filter((e) => !e.startsWith('(')).map((e) => html`<option value=${e}>${e}</option>`)}
          </select>` : null}
          <${Button} size="sm" variant="ghost" icon="rotate" onClick=${resetCode} title="Startcode herstellen" />
          <${Button} size="sm" icon="bug" onClick=${startDebug} title="Stap voor stap debuggen">Debug</${Button}>
          <${Button} size="sm" icon=${running ? 'stop' : 'play'} variant=${running ? 'danger' : undefined} onClick=${run} title="Uitvoeren (Ctrl+Enter)">${running ? 'Stop' : 'Uitvoeren'}</${Button}>
          ${exercise ? html`<${Button} size="sm" variant="primary" icon="checkCircle" loading=${grading} onClick=${grade} title="Controleer met tests (Ctrl+Shift+Enter)">Controleer</${Button}>` : null}
        </div>
      </div>
      <div class="wb-editor">
        <${CodeEditor} files=${files} activePath=${activePath} onChange=${setFiles} onDiagnostics=${onDiagnostics}
          breakpoints=${allBreakpoints} onToggleBreakpoint=${toggleBreakpoint} highlight=${debugLoc} mainType=${mainType} reveal=${reveal} />
      </div>
      <div class="wb-status">
        <span class=${errors.length ? 'danger-text' : 'success-text'}><${Icon} name=${errors.length ? 'xCircle' : 'checkCircle'} size="13" /> ${errors.length ? `${errors.length} fout${errors.length > 1 ? 'en' : ''}` : 'Compileert'}</span>
        <span class="faint">C# · .NET · ${settings.get().culture}</span>
        <span class="faint hide-sm">Ctrl+Enter uitvoeren${exercise ? ' · Ctrl+Shift+Enter controleren' : ''} · klik in de marge voor een breakpoint</span>
      </div>
    </section>

    <aside class="wb-right">
      <${Tabs} active=${rightTab} onChange=${setRightTab} tabs=${[
        { id: 'console', label: 'Console', icon: 'terminal' },
        { id: 'problems', label: 'Problemen', icon: 'alert', count: errors.length || null, tone: 'danger' },
        ...(exercise ? [{ id: 'tests', label: 'Tests', icon: 'check', count: result ? `${result.tests.length - failingTests}/${result.tests.length}` : null, tone: result ? (failingTests ? 'danger' : 'success') : '' }] : []),
        ...(exercise ? [{ id: 'feedback', label: 'Feedback', icon: 'sparkles', count: result ? (result.passed ? (qualityCount || '✓') : '!') : null, tone: result ? (result.passed ? 'success' : 'warning') : '' }] : []),
        { id: 'debug', label: 'Debugger', icon: 'bug' },
        { id: 'files', label: 'Bestanden', icon: 'folder', count: Object.keys(sandboxFiles).length || null },
      ]} />
      <div class="wb-panel">
        <div class="wb-console" style=${{ display: rightTab === 'console' ? 'flex' : 'none' }}>
          <${Terminal} api=${term} onData=${onTermData} />
          ${lastExit?.exception ? html`<div class="console-exception"><${ExceptionBox} ex=${lastExit.exception} /></div>` : null}
        </div>
        ${rightTab === 'problems' ? html`<div class="wb-scroll"><${DiagnosticsList} diagnostics=${diagnostics} onGoto=${goto} /></div>` : null}
        ${rightTab === 'tests' ? html`<div class="wb-scroll">${grading ? html`<${Spinner} label="Tests worden uitgevoerd…" />` : html`<${TestResults} result=${result} tests=${exercise?.tests} />`}</div>` : null}
        ${rightTab === 'feedback' ? html`<div class="wb-scroll">${grading ? html`<${Spinner} label="Je code wordt beoordeeld…" />` : html`<${FeedbackView} result=${result} onGoto=${goto} />`}</div>` : null}
        ${rightTab === 'debug' ? html`<div class="wb-scroll">
          <${TraceViewer} key=${debugKey} files=${files} showCode=${false} breakpoints=${allBreakpoints} onLocation=${setDebugLoc}
            initialFiles=${sandboxFiles} mainType=${mainType} stdin=${(exercise?.tests || []).find((t) => !t.hidden && t.input)?.input || ''} autoStart=${debugKey > 0} />
        </div>` : null}
        ${rightTab === 'files' ? html`<div class="wb-scroll"><${FilesView} files=${sandboxFiles} initial=${exercise?.files} onReset=${() => setSandboxFiles({ ...(exercise?.files || {}) })} /></div>` : null}
      </div>
    </aside>
  </div>`;
}

const TEMPLATES = [
  { name: 'Hallo + invoer', icon: 'terminal', files: STARTER_PLAYGROUND },
  { name: 'For-loop', icon: 'repeat', files: [{ path: 'Program.cs', content: `using System;\n\nclass Program {\n    static void Main(string[] args) {\n        for (int i = 1; i <= 5; i++) {\n            Console.WriteLine($"Iteratie {i}");\n        }\n    }\n}\n` }] },
  { name: 'Array + methods', icon: 'puzzle', files: [{ path: 'Program.cs', content: `using System;\n\nclass Program {\n    static int Som(int[] getallen) {\n        int som = 0;\n        foreach (int getal in getallen) {\n            som += getal;\n        }\n        return som;\n    }\n\n    static void Main(string[] args) {\n        int[] punten = { 12, 15, 9, 18 };\n        Console.WriteLine($"Totaal: {Som(punten)}");\n    }\n}\n` }] },
  { name: 'Klasse in apart bestand', icon: 'box', files: [
    { path: 'Program.cs', content: `using System;\n\nnamespace Demo {\n    class Program {\n        static void Main(string[] args) {\n            Persoon p = new Persoon("Jan", 34);\n            Console.WriteLine(p.Beschrijving());\n        }\n    }\n}\n` },
    { path: 'Persoon.cs', content: `namespace Demo {\n    class Persoon {\n        private string _naam;\n        private int _leeftijd;\n\n        public Persoon(string naam, int leeftijd) {\n            _naam = naam;\n            _leeftijd = leeftijd;\n        }\n\n        public string Beschrijving() {\n            return $"{_naam} is {_leeftijd} jaar.";\n        }\n    }\n}\n` }] },
  { name: 'Console-spel (ReadKey)', icon: 'keyboard', files: [{ path: 'Program.cs', content: `using System;\n\nclass Program {\n    static void Main(string[] args) {\n        int x = 10;\n        Console.CursorVisible = false;\n        while (true) {\n            Console.Clear();\n            Console.SetCursorPosition(x, 3);\n            Console.Write("@");\n            Console.SetCursorPosition(0, 6);\n            Console.Write("Pijltjes links/rechts, Q om te stoppen");\n            ConsoleKeyInfo toets = Console.ReadKey(true);\n            if (toets.Key == ConsoleKey.LeftArrow && x > 0) x--;\n            else if (toets.Key == ConsoleKey.RightArrow && x < 40) x++;\n            else if (toets.Key == ConsoleKey.Q) break;\n        }\n        Console.Clear();\n        Console.WriteLine("Tot ziens!");\n    }\n}\n` }] },
];

function PlaygroundInfo({ onTemplate }) {
  return html`<div class="task-panel">
    <div class="task-head">
      <h2 class="task-title"><${Icon} name="code" size="20" /> Vrij Code Lab</h2>
      <p class="muted small">Experimenteer vrij met C#. Je code wordt echt gecompileerd en in een afgeschermde sandbox uitgevoerd. Alles wordt automatisch bewaard.</p>
    </div>
    <div class="task-body stack">
      <div>
        <h4>Sjablonen</h4>
        <div class="template-list">${TEMPLATES.map((t) => html`<button class="template" onClick=${() => { if (confirm('Je huidige code vervangen door dit sjabloon?')) onTemplate(t.files.map((f) => ({ ...f }))); }}>
          <${Icon} name=${t.icon} size="16" />${t.name}</button>`)}</div>
      </div>
      <${Callout} tone="tip" title="Zo werkt het:">
        <ul class="small" style="margin:0;padding-left:1.2em">
          <li><strong>Uitvoeren</strong> start je programma in de console rechts. Typ je invoer rechtstreeks in de console.</li>
          <li><strong>Debug</strong> voert je code stap voor stap uit met variabelen, call stack en traceertabel.</li>
          <li>Klik in de marge naast een regelnummer voor een <strong>breakpoint</strong>.</li>
          <li>Voeg bestanden toe met <strong>+</strong> (bv. een klasse <code>Persoon.cs</code>).</li>
        </ul>
      </${Callout}>
    </div>
  </div>`;
}
