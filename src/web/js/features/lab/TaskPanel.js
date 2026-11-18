import { html, useEffect, useState } from '../../lib/preact.js';
import { Icon } from '../../components/Icon.js';
import { Badge, Button, Callout, Difficulty, Modal, Tabs } from '../../components/ui.js';
import { Markdown } from '../../components/Markdown.js';
import { curriculumService } from '../../services/curriculumService.js';
import { engine } from '../../progress/engine.js';
import { store } from '../../progress/store.js';
import { hintMultiplier } from '../../progress/xp.js';

const ROLE_LABEL = { oefenen: 'Oefening', meesterschap: 'Meesterschap', boss: 'Boss fight', uitdaging: 'Uitdaging', project: 'Project' };
const HINT_LABELS = ['Richting', 'Aanpak', 'Concreet stappenplan'];

/** Linkerzone: opdracht, benodigde theorie, doel, randvoorwaarden en hints. */
export function TaskPanel({ exercise, summary, result, onLoadSolution, knowledge }) {
  const [tab, setTab] = useState('task');
  const p = store.get();
  const st = p.exercises[exercise.summary.id] || {};
  const passed = st.status === 'passed';

  return html`<div class="task-panel">
    <div class="task-head">
      <div class="row wrap" style="gap:6px">
        <${Badge} tone=${exercise.summary.role === 'boss' ? 'danger' : exercise.summary.role === 'meesterschap' ? 'accent' : 'info'} icon=${exercise.summary.role === 'boss' ? 'sword' : 'code'}>${ROLE_LABEL[exercise.summary.role] || 'Oefening'}</${Badge}>
        <${Badge} tone="xp" icon="zap">${exercise.summary.xp} XP</${Badge}>
        <${Difficulty} value=${exercise.summary.difficulty} />
        ${passed ? html`<${Badge} tone="success" icon="check">Geslaagd</${Badge}>` : null}
      </div>
      <h2 class="task-title">${exercise.summary.phase ? html`<span class="faint">${exercise.summary.phase} · </span>` : null}${exercise.summary.title}</h2>
      ${exercise.summary.source ? html`<div class="source-tag"><${Icon} name="book" size="13" /> Cursus: ${exercise.summary.source}</div>` : null}
    </div>
    <${Tabs} active=${tab} onChange=${setTab} tabs=${[
      { id: 'task', label: 'Opdracht', icon: 'target' },
      { id: 'theory', label: 'Theorie', icon: 'book' },
      { id: 'hints', label: 'Hints', icon: 'lightbulb', count: `${st.hintsUsed || 0}/${exercise.hintCount}` },
    ]} />
    <div class="task-body">
      ${tab === 'task' ? html`<${TaskTab} exercise=${exercise} result=${result} />` : null}
      ${tab === 'theory' ? html`<${TheoryTab} exercise=${exercise} summary=${summary} knowledge=${knowledge} />` : null}
      ${tab === 'hints' ? html`<${HintsTab} exercise=${exercise} onLoadSolution=${onLoadSolution} />` : null}
    </div>
  </div>`;
}

function TaskTab({ exercise, result }) {
  const rules = exercise.rules || [];
  const visibleTests = (exercise.tests || []).filter((t) => !t.hidden && t.kind === 'io' && t.expected);
  return html`<div>
    <${Markdown} text=${exercise.description} />
    ${rules.length ? html`<div class="task-section">
      <h4><${Icon} name="check" size="15" /> Randvoorwaarden</h4>
      <ul class="rule-list">${rules.map((r, i) => {
        const res = result?.rules?.[i];
        return html`<li class=${res ? (res.passed ? 'ok' : 'fail') : ''}><${Icon} name=${res ? (res.passed ? 'check' : 'x') : 'dot'} size="14" />${r.message}</li>`;
      })}</ul>
    </div>` : null}
    ${visibleTests.length ? html`<div class="task-section">
      <h4><${Icon} name="terminal" size="15" /> Voorbeelduitvoering</h4>
      ${visibleTests.slice(0, 2).map((t) => html`<div class="io-block"><span class="io-label">${t.name}</span><pre>${t.expected}</pre></div>`)}
      <p class="faint xs">De ingetypte invoer van de gebruiker staat mee in het voorbeeld. Kleine verschillen in spaties of hoofdletters worden genegeerd${visibleTests.some((t) => t.mode === 'exact') ? ' (behalve bij exacte tests)' : ''}.</p>
    </div>` : null}
    ${Object.keys(exercise.files || {}).length ? html`<div class="task-section">
      <h4><${Icon} name="folder" size="15" /> Bestanden in de map van je programma</h4>
      ${Object.entries(exercise.files).map(([n, c]) => html`<div class="io-block"><span class="io-label">${n}</span><pre>${c.trimEnd()}</pre></div>`)}
    </div>` : null}
  </div>`;
}

function TheoryTab({ exercise, summary, knowledge }) {
  const lesson = summary?.lesson;
  const related = (knowledge || []).filter((k) => k.tags?.some((t) => exercise.summary.skills.includes(t))).slice(0, 4);
  return html`<div class="stack">
    ${lesson ? html`<${Callout} tone="info" icon="book">
      Deze opdracht hoort bij de les <a href=${`#/les/${lesson.id}`}><strong>${lesson.title}</strong></a>${lesson.source ? html` (${lesson.source})` : ''}.
    </${Callout}>` : null}
    <div>
      <h4>Vaardigheden</h4>
      <div class="chip-row">${exercise.summary.skills.map((s) => html`<${Badge} tone="accent">${summary?.skillTitle?.(s) || s}</${Badge}>`)}</div>
    </div>
    ${related.length ? related.map((k) => html`<details class="kb-mini">
      <summary><${Icon} name="library" size="14" /> ${k.title}</summary>
      <${Markdown} text=${k.body} />
    </details>`) : html`<p class="muted small">Bekijk de <a href="#/kennisbank">kennisbank</a> voor begrippen en syntax.</p>`}
  </div>`;
}

function HintsTab({ exercise, onLoadSolution }) {
  const id = exercise.summary.id;
  const st = store.get().exercises[id] || {};
  const [hints, setHints] = useState([]);
  const [loading, setLoading] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const [solution, setSolution] = useState(null);
  const used = st.hintsUsed || 0;

  useEffect(() => {
    // Eerder gebruikte hints opnieuw tonen (zonder extra kost).
    (async () => {
      const list = [];
      for (let n = 1; n <= used; n++) list.push((await curriculumService.hint(id, n)).hint);
      setHints(list);
    })().catch(() => {});
  }, [id]);

  async function reveal() {
    const n = hints.length + 1;
    setLoading(true);
    try {
      const res = await curriculumService.hint(id, n);
      engine.recordHint(id, n);
      setHints([...hints, res.hint]);
    } finally { setLoading(false); }
  }

  async function showSolution() {
    setConfirm(false);
    engine.recordSolutionView(id);
    setSolution(await curriculumService.solution(id));
  }

  const nextMultiplier = hintMultiplier(hints.length + 1);
  return html`<div class="stack">
    <p class="muted small">Hints helpen je op weg zonder de oplossing te verklappen. Elke hint verlaagt de XP voor deze opdracht een beetje — probeer eerst zelf!</p>
    ${hints.map((h, i) => html`<div class="hint-card fade-in">
      <div class="hint-label"><${Icon} name="lightbulb" size="14" /> Hint ${i + 1} · ${HINT_LABELS[i] || ''}</div>
      <${Markdown} text=${h} />
    </div>`)}
    ${hints.length < exercise.hintCount ? html`<${Button} icon="lightbulb" loading=${loading} onClick=${reveal}>
      Toon hint ${hints.length + 1} (${HINT_LABELS[hints.length] || ''}) · XP ×${nextMultiplier}
    </${Button}>` : null}
    <hr />
    <div>
      <h4>Volledige oplossing</h4>
      <p class="muted small">Pas als je echt vastzit. Je krijgt dan geen XP voor deze opdracht, en ze komt later terug om zelfstandig op te lossen.</p>
      <${Button} variant="danger" icon="eye" onClick=${() => setConfirm(true)}>Bekijk volledige oplossing</${Button}>
    </div>
    ${confirm ? html`<${Modal} title="Oplossing bekijken?" onClose=${() => setConfirm(false)} footer=${html`
      <${Button} onClick=${() => setConfirm(false)}>Toch nog proberen</${Button}>
      <${Button} variant="danger" onClick=${showSolution}>Ja, toon de oplossing</${Button}>`}>
      <p>Weet je het zeker? Leren gebeurt vooral door zelf te zoeken. Heb je alle hints al bekeken en de debugger gebruikt?</p>
      <ul class="small muted"><li>Je verdient geen XP meer voor deze opdracht.</li><li>De opdracht komt morgen terug als herhaling.</li></ul>
    </${Modal}>` : null}
    ${solution ? html`<${Modal} title="Modeloplossing" wide onClose=${() => setSolution(null)} footer=${html`
      <${Button} onClick=${() => setSolution(null)}>Sluiten</${Button}>
      <${Button} variant="primary" icon="code" onClick=${() => { onLoadSolution?.(solution.files); setSolution(null); }}>Laad in de editor</${Button}>`}>
      ${solution.files.map((f) => html`<${Markdown} text=${'```csharp titel="' + f.path + '"\n' + f.content.trimEnd() + '\n```'} />`)}
      ${solution.explanation ? html`<div class="solution-explain"><h3><${Icon} name="lightbulb" size="18" /> Hoe werkt deze oplossing?</h3><${Markdown} text=${solution.explanation} /></div>` : null}
    </${Modal}>` : null}
  </div>`;
}
