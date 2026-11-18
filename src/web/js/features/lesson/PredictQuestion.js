import { html, useState } from '../../lib/preact.js';
import { Icon } from '../../components/Icon.js';
import { Button, Callout } from '../../components/ui.js';
import { Markdown } from '../../components/Markdown.js';
import { curriculumService } from '../../services/curriculumService.js';
import { engine } from '../../progress/engine.js';
import { store } from '../../progress/store.js';
import { settings } from '../../progress/settings.js';

const TYPE_LABEL = { choice: 'Meerkeuze', output: 'Wat drukt deze code af?', text: 'Kort antwoord' };

/**
 * Een voorspelvraag. De uitleg verschijnt pas nadat de leerling geantwoord heeft.
 * Bij type 'output' wordt het juiste antwoord bepaald door de code écht uit te voeren.
 */
export function PredictQuestion({ lesson, q, index, total, onDone }) {
  const saved = store.get().lessons[lesson.id]?.predictions?.[q.id];
  const [choices, setChoices] = useState([]);
  const [text, setText] = useState('');
  const [feedback, setFeedback] = useState(saved?.correct || saved?.revealed ? { correct: !!saved.correct, restored: true } : null);
  const [busy, setBusy] = useState(false);
  const [shake, setShake] = useState(false);
  const attempts = store.get().lessons[lesson.id]?.predictions?.[q.id]?.attempts || 0;

  async function check() {
    setBusy(true);
    try {
      const res = await curriculumService.checkPrediction(lesson.id, q.id, { text, choices, culture: settings.get().culture });
      engine.recordPrediction(lesson, q.id, res.correct);
      setFeedback(res);
      if (!res.correct) { setShake(true); setTimeout(() => setShake(false), 450); }
      else onDone?.();
    } finally { setBusy(false); }
  }

  async function reveal() {
    const res = await curriculumService.checkPrediction(lesson.id, q.id, { text: '\u0000', choices: [-1], culture: settings.get().culture });
    store.update((p) => {
      const l = (p.lessons[lesson.id] ??= { predictions: {} });
      l.predictions[q.id] = { ...(l.predictions[q.id] || { attempts: 1 }), revealed: true };
    });
    setFeedback({ ...res, correct: false, revealed: true });
    onDone?.();
  }

  const answered = feedback && (feedback.correct || feedback.revealed);
  const toggle = (i) => setChoices(q.multiple ? (choices.includes(i) ? choices.filter((c) => c !== i) : [...choices, i]) : [i]);
  const canSubmit = q.type === 'choice' ? choices.length > 0 : text.trim().length > 0;

  return html`<div class=${`predict ${shake ? 'shake' : ''} ${answered ? (feedback.correct ? 'is-correct' : 'is-revealed') : ''}`}>
    <div class="predict-head">
      <span class="predict-num">Vraag ${index + 1}/${total}</span>
      <span class="badge badge-accent"><${Icon} name="eye" />${TYPE_LABEL[q.type] || 'Vraag'}</span>
      ${answered ? html`<span class=${`badge ${feedback.correct ? 'badge-success' : 'badge-warning'}`}>${feedback.correct ? 'Juist' : 'Antwoord bekeken'}</span>` : null}
    </div>
    <${Markdown} text=${q.question} interactive=${false} />

    ${q.type === 'choice' ? html`<div class="options" role=${q.multiple ? 'group' : 'radiogroup'}>
      ${q.options.map((o, i) => html`<button class=${`option ${choices.includes(i) ? 'selected' : ''}`} disabled=${answered} onClick=${() => toggle(i)} role=${q.multiple ? 'checkbox' : 'radio'} aria-checked=${choices.includes(i)}>
        <span class="option-key">${String.fromCharCode(65 + i)}</span><span>${o}</span>
      </button>`)}
      ${q.multiple ? html`<p class="faint xs">Meerdere antwoorden mogelijk.</p>` : null}
    </div>` : q.type === 'output' ? html`<div>
      <label class="label">Jouw voorspelling van de output</label>
      <textarea class="textarea" rows="4" value=${text} disabled=${answered} onInput=${(e) => setText(e.currentTarget.value)} placeholder="Typ exact wat er op de console verschijnt…"></textarea>
    </div>` : html`<div>
      <input class="input" value=${text} disabled=${answered} onInput=${(e) => setText(e.currentTarget.value)} onKeyDown=${(e) => e.key === 'Enter' && canSubmit && check()} placeholder="Jouw antwoord" />
    </div>`}

    ${!answered ? html`<div class="row" style="margin-top:12px">
      <${Button} variant="primary" icon="check" disabled=${!canSubmit} loading=${busy} onClick=${check}>Controleer</${Button}>
      ${feedback && !feedback.correct ? html`<${Button} variant="ghost" icon="eye" onClick=${reveal}>Toon het antwoord</${Button}>` : null}
      ${attempts === 0 ? html`<span class="faint xs">Juist bij de eerste poging: +10 XP</span>` : null}
    </div>` : null}

    ${feedback && !feedback.correct && !feedback.revealed ? html`<${Callout} tone="warn" title="Nog niet juist.">Kijk nog eens goed naar de code en probeer opnieuw. Denk regel per regel: welke waarde heeft elke variabele?</${Callout}>` : null}

    ${answered && !feedback.restored ? html`<div class="predict-explain fade-in">
      ${feedback.correct ? html`<${Callout} tone="success" icon="checkCircle" title="Juist!">Goed voorspeld.</${Callout}>`
        : html`<${Callout} tone="info" title="Het juiste antwoord:"><span class="mono">${feedback.correctAnswer}</span></${Callout}>`}
      ${feedback.actualOutput != null ? html`<div class="io-block"><span class="io-label">Echte output (zelf uitgevoerd)</span><pre>${feedback.actualOutput || '(geen output)'}</pre></div>` : null}
      ${feedback.explanation ? html`<div class="explain"><h4><${Icon} name="lightbulb" size="16" /> Uitleg</h4><${Markdown} text=${feedback.explanation} /></div>` : null}
    </div>` : null}
    ${answered && feedback.restored ? html`<p class="faint small"><${Icon} name="check" size="14" /> Je beantwoordde deze vraag al.</p>` : null}
  </div>`;
}
