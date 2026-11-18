import { html, useEffect, useRef, useState } from '../../lib/preact.js';
import { loadMonaco, monacoTheme, modelOwners } from './monaco.js';
import { codeService } from '../../services/codeService.js';
import { settings } from '../../progress/settings.js';

let instanceCounter = 0;

/**
 * Monaco-editor voor een project met meerdere bestanden.
 * Props:
 *  files: [{path, content}], activePath, onChange(files), onDiagnostics(list, entryPoints)
 *  breakpoints: Set<number> (regels in actief bestand), onToggleBreakpoint(line)
 *  highlight: { path, line } — huidige regel in de debugger
 *  mainType, readOnly, reveal: {path, line, col}
 */
export function CodeEditor({ files, activePath, onChange, onDiagnostics, breakpoints, onToggleBreakpoint, highlight, mainType, readOnly, reveal, onReady, height }) {
  const host = useRef(null);
  const editorRef = useRef(null);
  const monacoRef = useRef(null);
  const models = useRef(new Map());
  const filesRef = useRef(files);
  const activeRef = useRef(activePath);
  const idRef = useRef(++instanceCounter);
  const decorations = useRef([]);
  const diagTimer = useRef(null);
  const diagAbort = useRef(null);
  const [error, setError] = useState(null);
  const onChangeRef = useRef(onChange);
  const onDiagRef = useRef(onDiagnostics);
  const toggleRef = useRef(onToggleBreakpoint);
  onChangeRef.current = onChange;
  onDiagRef.current = onDiagnostics;
  toggleRef.current = onToggleBreakpoint;
  filesRef.current = files;
  activeRef.current = activePath;

  const uriFor = (monaco, path) => monaco.Uri.parse(`inmemory://quest/${idRef.current}/${path}`);

  // Editor aanmaken
  useEffect(() => {
    let disposed = false;
    loadMonaco().then((monaco) => {
      if (disposed || !host.current) return;
      monacoRef.current = monaco;
      const s = settings.get();
      const editor = monaco.editor.create(host.current, {
        language: 'csharp',
        theme: monacoTheme(),
        automaticLayout: true,
        fontFamily: 'JetBrains Mono, Cascadia Code, Consolas, monospace',
        fontSize: s.editorFontSize,
        fontLigatures: true,
        minimap: { enabled: s.minimap },
        scrollBeyondLastLine: false,
        tabSize: 4,
        insertSpaces: true,
        renderWhitespace: 'selection',
        glyphMargin: true,
        lineNumbersMinChars: 3,
        smoothScrolling: true,
        cursorSmoothCaretAnimation: 'on',
        bracketPairColorization: { enabled: true },
        guides: { bracketPairs: true, indentation: true },
        padding: { top: 12, bottom: 12 },
        readOnly: !!readOnly,
        fixedOverflowWidgets: true,
        quickSuggestions: { other: true, comments: false, strings: false },
        suggest: { showWords: false },
        stickyScroll: { enabled: false },
      });
      editorRef.current = editor;
      editor.onMouseDown((e) => {
        const t = e.target.type;
        if (t === monaco.editor.MouseTargetType.GUTTER_GLYPH_MARGIN || t === monaco.editor.MouseTargetType.GUTTER_LINE_NUMBERS) {
          if (e.target.position && toggleRef.current) toggleRef.current(e.target.position.lineNumber);
        }
      });
      syncModels();
      onReady?.(editor, monaco);
      scheduleDiagnostics(50);
    }).catch((e) => setError(e.message));

    const unsubSettings = settings.subscribe((s) => {
      editorRef.current?.updateOptions({ fontSize: s.editorFontSize, minimap: { enabled: s.minimap } });
      monacoRef.current?.editor.setTheme(monacoTheme());
    });
    const observer = new MutationObserver(() => monacoRef.current?.editor.setTheme(monacoTheme()));
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });

    return () => {
      disposed = true;
      unsubSettings();
      observer.disconnect();
      clearTimeout(diagTimer.current);
      diagAbort.current?.abort();
      for (const m of models.current.values()) { modelOwners.delete(m.uri.toString()); m.dispose(); }
      models.current.clear();
      editorRef.current?.dispose();
    };
  }, []);

  function syncModels() {
    const monaco = monacoRef.current;
    const editor = editorRef.current;
    if (!monaco || !editor) return;
    const wanted = new Set(filesRef.current.map((f) => f.path));
    for (const [path, model] of models.current) {
      if (!wanted.has(path)) { modelOwners.delete(model.uri.toString()); model.dispose(); models.current.delete(path); }
    }
    for (const f of filesRef.current) {
      let model = models.current.get(f.path);
      if (!model) {
        model = monaco.editor.createModel(f.content, 'csharp', uriFor(monaco, f.path));
        model.onDidChangeContent(() => {
          const updated = filesRef.current.map((x) => (x.path === f.path ? { ...x, content: model.getValue() } : x));
          filesRef.current = updated;
          onChangeRef.current?.(updated);
          scheduleDiagnostics();
        });
        modelOwners.set(model.uri.toString(), () => ({ files: filesRef.current, path: f.path }));
        models.current.set(f.path, model);
      } else if (model.getValue() !== f.content) {
        // Extern gewijzigd (reset, oplossing laden): inhoud vervangen met undo-geschiedenis.
        model.pushEditOperations([], [{ range: model.getFullModelRange(), text: f.content }], () => null);
      }
    }
    const active = models.current.get(activeRef.current) || models.current.values().next().value;
    if (active && editor.getModel() !== active) editor.setModel(active);
  }

  function scheduleDiagnostics(delay = 650) {
    if (!settings.get().liveDiagnostics) return;
    clearTimeout(diagTimer.current);
    diagTimer.current = setTimeout(runDiagnostics, delay);
  }

  async function runDiagnostics() {
    const monaco = monacoRef.current;
    if (!monaco) return;
    diagAbort.current?.abort();
    const controller = new AbortController();
    diagAbort.current = controller;
    try {
      const res = await codeService.diagnostics(filesRef.current, mainType, controller.signal);
      if (controller.signal.aborted) return;
      applyMarkers(res.diagnostics || []);
      onDiagRef.current?.(res.diagnostics || [], res.entryPoints || []);
    } catch { /* server tijdelijk onbereikbaar */ }
  }

  function applyMarkers(list) {
    const monaco = monacoRef.current;
    for (const [path, model] of models.current) {
      const markers = list.filter((d) => d.line > 0 && (d.file === path || (!d.file && path === filesRef.current[0]?.path))).map((d) => ({
        severity: d.severity === 'error' ? monaco.MarkerSeverity.Error : d.severity === 'warning' ? monaco.MarkerSeverity.Warning : monaco.MarkerSeverity.Info,
        message: `${d.message}${d.tip ? '\n💡 ' + d.tip : ''}`,
        code: d.code,
        source: d.category === 'syntax' ? 'Syntaxfout' : d.category === 'security' ? 'Sandbox' : d.severity === 'error' ? 'Compileerfout' : 'Waarschuwing',
        startLineNumber: d.line, startColumn: d.column, endLineNumber: d.endLine || d.line, endColumn: Math.max(d.endColumn || d.column + 1, d.column + 1),
      }));
      monaco.editor.setModelMarkers(model, 'quest', markers);
    }
  }

  // Bestanden of actief bestand veranderd
  useEffect(() => { syncModels(); }, [files, activePath]);

  useEffect(() => { editorRef.current?.updateOptions({ readOnly: !!readOnly }); }, [readOnly]);
  useEffect(() => { scheduleDiagnostics(100); }, [mainType]);

  // Breakpoints + huidige debugregel als decoraties
  useEffect(() => {
    const monaco = monacoRef.current;
    const editor = editorRef.current;
    if (!monaco || !editor) return;
    const list = [];
    for (const line of breakpoints || []) {
      list.push({ range: new monaco.Range(line, 1, line, 1), options: { glyphMarginClassName: 'bp-glyph', glyphMarginHoverMessage: { value: 'Breakpoint (klik om te verwijderen)' } } });
    }
    if (highlight && highlight.path === activePath && highlight.line > 0) {
      list.push({ range: new monaco.Range(highlight.line, 1, highlight.line, 1), options: { isWholeLine: true, className: 'debug-line', glyphMarginClassName: 'debug-arrow' } });
      editor.revealLineInCenterIfOutsideViewport(highlight.line);
    }
    decorations.current = editor.deltaDecorations(decorations.current, list);
  });

  // Naar een fout springen
  useEffect(() => {
    if (!reveal || !editorRef.current) return;
    const editor = editorRef.current;
    const model = models.current.get(reveal.path);
    if (model && editor.getModel() !== model) editor.setModel(model);
    editor.revealLineInCenter(reveal.line);
    editor.setPosition({ lineNumber: reveal.line, column: reveal.col || 1 });
    editor.focus();
  }, [reveal]);

  if (error) return html`<div class="editor-error">${error}</div>`;
  return html`<div class="code-editor" ref=${host} style=${height ? { height } : null}></div>`;
}
