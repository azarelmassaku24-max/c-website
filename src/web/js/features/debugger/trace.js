/**
 * Zet de ruwe trace-events van de server om in stappen voor de debugger en de traceertabel.
 * Events: E (method start), X (method einde), L (regel), V (waarden na regel), C (voorwaarde),
 * R (return), O (output), I (invoer), S (sleep), Z (exception), T (afgekapt).
 */
export function buildTrace(events, variables = []) {
  const steps = [];
  const frames = [];
  let output = '';
  let truncated = false;
  let frameId = 0;

  const top = () => frames[frames.length - 1];
  const snapshot = () => frames.map((f) => ({ id: f.id, method: f.method, file: f.file, line: f.line, vars: { ...f.vars }, ret: f.ret }));
  const push = (step) => {
    const prev = steps[steps.length - 1];
    const s = { ...step, frames: snapshot(), outputLength: output.length, changed: [] };
    const t = s.frames[s.frames.length - 1];
    const pt = prev?.frames[prev.frames.length - 1];
    if (t) {
      for (const [name, val] of Object.entries(t.vars)) {
        if (!pt || pt.id !== t.id || !pt.vars[name] || pt.vars[name].v !== val.v) s.changed.push(name);
      }
    }
    s.file = t?.file || 'Program.cs';
    steps.push(s);
    return s;
  };
  const setVars = (vars) => {
    const f = top();
    if (!f || !vars) return;
    for (const v of vars) f.vars[v.n] = { v: v.v, t: v.t };
  };

  for (const e of events) {
    switch (e.k) {
      case 'E': {
        frames.push({ id: ++frameId, method: e.m, file: e.f || 'Program.cs', line: e.l, vars: {}, ret: undefined });
        setVars(e.vars);
        push({ kind: 'enter', line: e.l, depth: e.d, method: e.m });
        break;
      }
      case 'X': {
        const f = frames.pop();
        if (f && frames.length > 0) {
          const caller = top();
          push({ kind: 'exit', line: caller.line, depth: frames.length, method: f.method, returned: f.ret, fromMethod: f.method });
        }
        break;
      }
      case 'L': {
        if (top()) top().line = e.l;
        push({ kind: 'line', line: e.l, depth: e.d });
        break;
      }
      case 'V': {
        setVars(e.vars);
        if (top()) top().line = e.l;
        const last = steps[steps.length - 1];
        // Waarden horen bij de regel die net werd uitgevoerd: we tonen ze als stap "na regel X".
        push({ kind: 'values', line: e.l, depth: e.d, after: true });
        if (last && last.kind === 'line' && last.line === e.l) last.hasValues = true;
        break;
      }
      case 'C': {
        setVars(e.vars);
        if (top()) top().line = e.l;
        push({ kind: 'cond', line: e.l, depth: e.d, result: e.r });
        break;
      }
      case 'R': {
        if (top()) top().ret = e.v;
        push({ kind: 'return', line: e.l, depth: e.d, value: e.v });
        break;
      }
      case 'O':
        output += e.s.replace(/\f/g, '\n[scherm gewist]\n');
        break;
      case 'I': {
        const last = steps[steps.length - 1];
        if (last) last.input = (last.input ? last.input + ' ' : '') + e.s;
        break;
      }
      case 'S':
        push({ kind: 'sleep', line: top()?.line || 0, depth: e.d, ms: e.ms });
        break;
      case 'Z':
        push({ kind: 'exception', line: top()?.line || 0, depth: e.d, type: e.t, message: e.m });
        break;
      case 'T':
        truncated = true;
        break;
    }
  }
  // Output bij elke stap = alles tot aan de volgende stap.
  for (let i = 0; i < steps.length; i++) steps[i].outputEnd = i + 1 < steps.length ? steps[i + 1].outputLength : output.length;
  return { steps, output, truncated, variables };
}

/** Navigatie zoals in Visual Studio. */
export function stepInto(trace, i) { return Math.min(trace.steps.length - 1, i + 1); }

export function stepOver(trace, i) {
  const d = trace.steps[i]?.frames.length || 0;
  for (let j = i + 1; j < trace.steps.length; j++) if (trace.steps[j].frames.length <= d) return j;
  return trace.steps.length - 1;
}

export function stepOut(trace, i) {
  const d = trace.steps[i]?.frames.length || 0;
  for (let j = i + 1; j < trace.steps.length; j++) if (trace.steps[j].frames.length < d) return j;
  return trace.steps.length - 1;
}

export function stepBack(trace, i) { return Math.max(0, i - 1); }

export function continueTo(trace, i, breakpoints, file) {
  for (let j = i + 1; j < trace.steps.length; j++) {
    const s = trace.steps[j];
    if (breakpoints.has(s.line) && (s.kind === 'line' || s.kind === 'cond' || s.kind === 'enter') && (!file || s.file === file)) return { index: j, hit: true };
  }
  return { index: trace.steps.length - 1, hit: false };
}

/**
 * Traceertabel zoals in de cursus: per uitgevoerde regel de waarde van elke variabele,
 * een kolom 'hoeveelste iteratie' en een kolom output. Enkel voor één method.
 */
export function traceTable(trace, method) {
  const rows = [];
  const columns = [];
  const iteration = new Map();
  for (let i = 0; i < trace.steps.length; i++) {
    const s = trace.steps[i];
    const frame = s.frames[s.frames.length - 1];
    if (!frame || frame.method !== method) continue;
    if (s.kind === 'values' && rows.length && rows[rows.length - 1].line === s.line) {
      rows[rows.length - 1].vars = frame.vars;
      rows[rows.length - 1].changed = s.changed;
      rows[rows.length - 1].output += trace.output.slice(s.outputLength, s.outputEnd);
      continue;
    }
    if (!['line', 'cond', 'enter', 'return', 'values'].includes(s.kind)) continue;
    for (const name of Object.keys(frame.vars)) if (!columns.includes(name)) columns.push(name);
    let iter = '';
    if (s.kind === 'cond') {
      const n = (iteration.get(s.line) || 0) + 1;
      iteration.set(s.line, n);
    }
    rows.push({
      step: i,
      line: s.line,
      kind: s.kind,
      result: s.result,
      vars: frame.vars,
      changed: s.changed,
      output: trace.output.slice(s.outputLength, s.outputEnd),
      input: s.input,
      iter,
    });
  }
  return { columns, rows };
}

export function methodsIn(trace) {
  const set = new Set();
  for (const s of trace.steps) for (const f of s.frames) set.add(f.method);
  return [...set];
}
