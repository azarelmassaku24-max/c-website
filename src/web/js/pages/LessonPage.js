import { html, useEffect, useState } from '../lib/preact.js';
import { Icon } from '../components/Icon.js';
import { Badge, Button, Callout, ErrorBox, LinkButton, Progress, Spinner, Modal } from '../components/ui.js';
import { Markdown } from '../components/Markdown.js';
import { PredictQuestion } from '../features/lesson/PredictQuestion.js';
import { Workbench } from '../features/lab/Workbench.js';
import { FeedbackView } from '../features/lab/FeedbackPanels.js';
import { curriculumService } from '../services/curriculumService.js';
import { codeService } from '../services/codeService.js';
import { useProgress } from '../hooks/useProgress.js';
import { engine } from '../progress/engine.js';
import { lessonStatus, lockReason } from '../progress/unlocks.js';
import { practiceFor } from '../progress/recommend.js';
import { navigate } from '../app/router.js';

const STEPS = [
  { id: 'begrijpen', label: 'Begrijpen', icon: 'book' },
  { id: 'voorbeeld', label: 'Voorbeeld', icon: 'code' },
  { id: 'voorspellen', label: 'Voorspellen', icon: 'eye' },
  { id: 'programmeren', label: 'Programmeren', icon: 'terminal' },
  { id: 'controleren', label: 'Controleren', icon: 'checkCircle' },
  { id: 'meesterschap', label: 'Meesterschap', icon: 'crown' },
];

// Laatste beoordelingen in deze sessie (voor de stap 'Controleren').
const lastResults = new Map();

export function LessonPage({ id }) {
  const p = useProgress();
  const c = curriculumService.get();
  const summary = c.lessons.get(id);
  const [detail, setDetail] = useState(null);
  const [error, setError] = useState(null);

  useEffect(() => {
    setDetail(null);
    curriculumService.lesson(id).then(setDetail, setError);
  }, [id]);

  if (!summary) return html`<div class="page"><${ErrorBox} error=${{ message: 'Deze les bestaat niet (meer).' }} /></div>`;
  const status = lessonStatus(p, c, summary);
  if (status === 'locked') return html`<${Locked} lesson=${summary} reason=${lockReason(p, c, summary)} />`;
  if (error) return html`<div class="page"><${ErrorBox} error=${error} /></div>`;
  if (!detail) return html`<div class="page"><${Spinner} label="Les laden…" /></div>`;

  if (summary.kind === 'boss' || summary.kind === 'project') return html`<${PhasedLesson} lesson=${summary} detail=${detail} />`;
  if (summary.kind === 'challenges') return html`<${ChallengeSet} lesson=${summary} detail=${detail} />`;
  return html`<${LessonPlayer} lesson=${summary} detail=${detail} />`;
}

function Locked({ lesson, reason }) {
  return html`<div class="page">
    <div class="locked-screen card">
      <div class="icon-tile muted" style="width:56px;height:56px"><${Icon} name="lock" size="26" /></div>
      <h2>${lesson.title}</h2>
      <p class="muted">${reason}</p>
      <div class="row" style="justify-content:center">
        <${LinkButton} href="#/academy" icon="graduation">Naar de Academy</${LinkButton}>
        <${LinkButton} href="#/instellingen" variant="ghost">Vrije modus inschakelen</${LinkButton}>
      </div>
    </div>
  </div>`;
}

function LessonHeader({ lesson, children }) {
  return html`<div class="lesson-header">
    <div class="lesson-header-inner">
      <a class="back-link" href=${`#/academy/${lesson.levelId}`}><${Icon} name="arrowLeft" size="15" /> Level ${lesson.level.number}: ${lesson.level.title}</a>
      <div class="row between wrap">
        <div>
          <h1 class="lesson-title">${lesson.kind === 'verdieping' ? html`<span class="badge badge-info" style="vertical-align:middle;margin-right:8px">Verdieping</span>` : null}${lesson.title}</h1>
          <div class="row wrap small muted" style="gap:10px">
            ${lesson.source ? html`<span class="source-tag"><${Icon} name="book" size="13" /> ${lesson.source}</span>` : null}
            <span><${Icon} name="clock" size="13" /> ±${lesson.minutes} min</span>
            ${lesson.skills.map((s) => html`<${Badge}>${curriculumService.get().skillMap.get(s)?.title || s}</${Badge}>`)}
          </div>
        </div>
      </div>
      ${children}
    </div>
  </div>`;
}

function LessonPlayer({ lesson, detail }) {
  const p = useProgress();
  const lp = p.lessons[lesson.id] || {};
  const practice = lesson.exercises.filter((e) => e.role === 'oefenen');
  const mastery = lesson.exercises.filter((e) => e.role === 'meesterschap');
  const [step, setStep] = useState(() => Math.min(lp.step || 0, 5));
  const [practiceIndex, setPracticeIndex] = useState(() => Math.max(0, practice.findIndex((e) => p.exercises[e.id]?.status !== 'passed')));
  const completed = !!lp.completedAt;

  useEffect(() => { engine.startLesson(lesson.id); }, [lesson.id]);
  useEffect(() => { engine.setStep(lesson.id, step); }, [step]);

  const predictionsDone = detail.predictions.every((q) => lp.predictions?.[q.id]?.correct || lp.predictions?.[q.id]?.revealed);
  const practiceDone = practice.every((e) => p.exercises[e.id]?.status === 'passed');
  const masteryDone = mastery.every((e) => p.exercises[e.id]?.status === 'passed');
  const stepDone = [true, true, predictionsDone, practiceDone, !!lp.reviewed || !practice.length, masteryDone];
  const maxReachable = (() => { let m = 0; while (m < 5 && stepDone[m]) m++; return Math.max(m, lp.maxStep || 0); })();

  const next = () => setStep(Math.min(5, step + 1));
  const content = {
    0: html`<${Markdown} text=${detail.understand} />`,
    1: html`<${Markdown} text=${detail.example || '_Geen voorbeeld._'} />`,
    2: html`<${PredictStep} lesson=${lesson} detail=${detail} />`,
    3: practice.length ? html`<${PracticeStep} lesson=${lesson} exercises=${practice} index=${practiceIndex} setIndex=${setPracticeIndex} />` : html`<p class="muted">Deze les heeft geen aparte oefenopdracht.</p>`,
    4: html`<${ReviewStep} lesson=${lesson} exercises=${practice} />`,
    5: mastery.length ? html`<${MasteryStep} lesson=${lesson} exercise=${mastery[0]} />` : html`<p class="muted">Geen meesterschapsopdracht.</p>`,
  }[step];

  const isWorkbenchStep = (step === 3 && practice.length) || (step === 5 && mastery.length);
  const nextLesson = completed ? findNext(lesson) : null;

  return html`<div class="lesson">
    <${LessonHeader} lesson=${lesson}>
      <ol class="stepper" aria-label="Leerstappen">
        ${STEPS.map((s, i) => {
          const done = (i < step && stepDone[i]) || (i === 5 && masteryDone);
          return html`<li class=${`step ${i === step ? 'current' : ''} ${done ? 'done' : ''} ${i > maxReachable ? 'locked' : ''}`}>
          <button onClick=${() => i <= maxReachable && setStep(i)} disabled=${i > maxReachable} aria-current=${i === step ? 'step' : undefined}>
            <span class="step-icon"><${Icon} name=${i > maxReachable ? 'lock' : done ? 'check' : s.icon} size="15" /></span>
            <span class="step-label"><span class="step-num">Stap ${i + 1}</span>${s.label}</span>
          </button>
        </li>`;
        })}
      </ol>
    </${LessonHeader}>

    ${completed ? html`<div class="lesson-complete-bar"><${Icon} name="trophy" size="16" /> Les voltooid!
      ${nextLesson ? html`<${LinkButton} size="sm" variant="primary" href=${`#/les/${nextLesson.id}`} iconRight="arrowRight">Volgende: ${nextLesson.title}</${LinkButton}>` : null}</div>` : null}

    <div class=${isWorkbenchStep ? 'lesson-body wide' : 'lesson-body'}>
      ${step === 0 && lesson.supplement ? html`<${Callout} tone="info" icon="info" title="Over deze uitleg:">${lesson.supplement}</${Callout}>` : null}
      ${content}
      <div class="lesson-nav">
        <${Button} variant="ghost" icon="arrowLeft" disabled=${step === 0} onClick=${() => setStep(step - 1)}>Vorige</${Button}>
        ${step < 5 ? html`<${Button} variant="primary" iconRight="arrowRight" disabled=${!stepDone[step]} onClick=${() => { if (step === 4) engine.markReviewed(lesson.id); next(); }}
          title=${!stepDone[step] ? (step === 2 ? 'Beantwoord eerst alle vragen' : 'Los eerst de opdracht op') : ''}>
          ${step === 2 && !predictionsDone ? 'Beantwoord eerst alle vragen' : step === 3 && !practiceDone ? 'Los eerst de oefening op' : `Verder: ${STEPS[step + 1].label}`}
        </${Button}>` : completed && nextLesson ? html`<${LinkButton} variant="primary" href=${`#/les/${nextLesson.id}`} iconRight="arrowRight">Volgende les</${LinkButton}>` : null}
      </div>
    </div>
  </div>`;
}

function findNext(lesson) {
  const c = curriculumService.get();
  const list = c.orderedLessons.filter((l) => ['lesson', 'boss', 'project'].includes(l.kind));
  const i = list.findIndex((l) => l.id === lesson.id);
  return i >= 0 ? list[i + 1] : null;
}

function PredictStep({ lesson, detail }) {
  const p = useProgress();
  if (!detail.predictions.length) return html`<p class="muted">Geen voorspelvragen in deze les.</p>`;
  const lp = p.lessons[lesson.id] || {};
  const done = detail.predictions.filter((q) => lp.predictions?.[q.id]?.correct || lp.predictions?.[q.id]?.revealed).length;
  return html`<div class="stack">
    <div class="predict-intro">
      <h2>Voorspel eerst, dan pas uitvoeren</h2>
      <p class="muted">Programmeurs lezen code en voorspellen wat ze doet. Beantwoord elke vraag <strong>voordat</strong> je de uitleg ziet. Het juiste antwoord bij vragen over output wordt bepaald door de code echt uit te voeren.</p>
      <${Progress} value=${(done / detail.predictions.length) * 100} label="Beantwoorde vragen" />
    </div>
    ${detail.predictions.map((q, i) => html`<${PredictQuestion} key=${q.id} lesson=${lesson} q=${q} index=${i} total=${detail.predictions.length} />`)}
  </div>`;
}

function PracticeStep({ lesson, exercises, index, setIndex }) {
  const p = useProgress();
  const c = curriculumService.get();
  const ex = exercises[index] || exercises[0];
  const st = p.exercises[ex.id] || {};
  const struggling = (st.attempts || 0) >= 3 && st.status !== 'passed';
  const extra = struggling ? practiceFor(p, c, ex.skills[0], 3).filter((e) => e.id !== ex.id && e.lesson.id !== lesson.id) : [];
  return html`<div class="stack">
    ${exercises.length > 1 ? html`<div class="row wrap">
      ${exercises.map((e, i) => html`<button class=${`chip ${i === index ? 'active' : ''}`} onClick=${() => setIndex(i)}>
        ${p.exercises[e.id]?.status === 'passed' ? html`<${Icon} name="check" size="13" />` : html`<span class="faint">${i + 1}</span>`} ${e.title}</button>`)}
    </div>` : null}
    ${struggling ? html`<${Callout} tone="tip" title="Dit lijkt lastig — dat is normaal!">
      Je probeerde dit al ${st.attempts} keer. Bekijk de feedback in het tabblad <strong>Feedback</strong>, gebruik een hint of de <strong>Debugger</strong>.
      ${extra.length ? html`<div style="margin-top:6px">Of oefen eerst met: ${extra.map((e, i) => html`${i ? ', ' : ''}<a href=${`#/lab/${e.id}`}>${e.title}</a>`)}</div>` : null}
    </${Callout}>` : null}
    <${Workbench} exerciseId=${ex.id} embedded onGraded=${(res) => {
      lastResults.set(ex.id, res);
      if (res.passed) {
        const nextIdx = exercises.findIndex((e, i) => i > index && p.exercises[e.id]?.status !== 'passed');
        if (nextIdx > 0) setTimeout(() => setIndex(nextIdx), 1200);
      }
    }} />
  </div>`;
}

function ReviewStep({ lesson, exercises }) {
  const p = useProgress();
  const [results, setResults] = useState(() => Object.fromEntries(exercises.map((e) => [e.id, lastResults.get(e.id)])));
  const [busy, setBusy] = useState(null);
  const [solution, setSolution] = useState(null);

  async function regrade(ex) {
    const files = p.exercises[ex.id]?.files;
    if (!files) return;
    setBusy(ex.id);
    try {
      const res = await codeService.grade(ex.id, files);
      lastResults.set(ex.id, res);
      engine.recordGrade(ex, res, files);
      setResults((r) => ({ ...r, [ex.id]: res }));
    } finally { setBusy(null); }
  }

  if (!exercises.length) return html`<p class="muted">Geen oefeningen om te controleren.</p>`;
  return html`<div class="stack">
    <div>
      <h2>Controleer je oplossing kritisch</h2>
      <p class="muted">Werkende code is pas het begin. Bekijk hieronder de code review: zijn er nog structuurproblemen, onnodige code of verbeterpunten? Een verbeterde versie zonder opmerkingen levert extra XP op.</p>
    </div>
    ${exercises.map((ex) => {
      const res = results[ex.id];
      const st = p.exercises[ex.id] || {};
      return html`<div class="card">
        <div class="card-header">
          <h3 class="card-title"><${Icon} name="code" />${ex.title}</h3>
          <div class="row">
            <${Button} size="sm" icon="rotate" loading=${busy === ex.id} onClick=${() => regrade(ex)}>${res ? 'Opnieuw beoordelen' : 'Beoordeling tonen'}</${Button}>
            ${st.status === 'passed' ? html`<${Button} size="sm" icon="eye" onClick=${async () => setSolution(await curriculumService.solution(ex.id))}>Vergelijk met modeloplossing</${Button}>` : null}
          </div>
        </div>
        ${res ? html`<${FeedbackView} result=${res} />` : html`<p class="muted small">${st.lastSummary ? `Laatste resultaat: ${st.lastSummary.passed ? 'geslaagd' : 'niet geslaagd'}, ${st.lastSummary.quality} verbeterpunt(en).` : 'Nog geen beoordeling.'}</p>`}
      </div>`;
    })}
    <div class="card">
      <h3 class="card-title"><${Icon} name="brain" /> Reflectie</h3>
      <ul class="reflect">
        <li>Kan je voor elke regel van je oplossing uitleggen wat hij doet?</li>
        <li>Wat gebeurt er als de gebruiker onverwachte invoer geeft (0, een negatief getal, tekst)?</li>
        <li>Zijn je variabelennamen duidelijk voor iemand anders (camelCasing, betekenisvol)?</li>
        <li>Staat er code dubbel die je kan vereenvoudigen?</li>
      </ul>
    </div>
    ${solution ? html`<${Modal} title="Modeloplossing ter vergelijking" wide onClose=${() => setSolution(null)}>
      <${Callout} tone="info">Je loste deze opdracht al op. Vergelijk: welke keuzes maakte jij anders? Er zijn vaak meerdere goede oplossingen.</${Callout}>
      ${solution.files.map((f) => html`<${Markdown} text=${'```csharp titel="' + f.path + '"\n' + f.content.trimEnd() + '\n```'} />`)}
      ${solution.explanation ? html`<${Markdown} text=${solution.explanation} />` : null}
    </${Modal}>` : null}
  </div>`;
}

function MasteryStep({ lesson, exercise }) {
  const p = useProgress();
  const passed = p.exercises[exercise.id]?.status === 'passed';
  return html`<div class="stack">
    <div class="mastery-intro">
      <div class="icon-tile warning"><${Icon} name="crown" /></div>
      <div>
        <h2>Meesterschap</h2>
        <p class="muted">Een moeilijkere opdracht waarin je het nieuwe concept combineert met eerdere leerstof. Slaag je, dan is de les voltooid en wordt het volgende onderdeel vrijgespeeld.</p>
      </div>
      ${passed ? html`<${Badge} tone="success" icon="check">Behaald</${Badge}>` : null}
    </div>
    <${Workbench} exerciseId=${exercise.id} embedded onGraded=${(res) => lastResults.set(exercise.id, res)} />
  </div>`;
}

/** Boss fights en het personenproject: meerdere fases na elkaar. */
function PhasedLesson({ lesson, detail }) {
  const p = useProgress();
  const phases = lesson.exercises;
  const firstOpen = phases.findIndex((e) => p.exercises[e.id]?.status !== 'passed');
  const [index, setIndex] = useState(firstOpen < 0 ? 0 : firstOpen);
  const [briefing, setBriefing] = useState(() => !p.lessons[lesson.id]?.startedAt);
  const passedCount = phases.filter((e) => p.exercises[e.id]?.status === 'passed').length;
  const hp = Math.round(100 - (passedCount / phases.length) * 100);
  const isBoss = lesson.kind === 'boss';
  useEffect(() => { engine.startLesson(lesson.id); }, [lesson.id]);
  const current = phases[index];
  const unlocked = (i) => i === 0 || p.exercises[phases[i - 1].id]?.status === 'passed' || p.exercises[phases[i].id]?.status === 'passed';
  const prevFiles = index > 0 ? p.exercises[phases[index - 1].id]?.files : null;
  const [seed, setSeed] = useState(0);

  return html`<div class="lesson">
    <${LessonHeader} lesson=${lesson}>
      <div class=${`boss-bar ${isBoss ? '' : 'project'}`}>
        <div class="boss-avatar"><${Icon} name=${isBoss ? 'skull' : 'users'} size="26" /></div>
        <div class="grow">
          <div class="row between"><strong>${isBoss ? 'Boss HP' : 'Projectvoortgang'}</strong><span class="mono small">${isBoss ? `${hp}%` : `${passedCount}/${phases.length} fases`}</span></div>
          <div class=${`progress thick ${isBoss ? 'danger' : 'success'}`}><span style=${{ width: (isBoss ? hp : (passedCount / phases.length) * 100) + '%' }}></span></div>
        </div>
        <${Button} size="sm" variant="ghost" icon="book" onClick=${() => setBriefing(true)}>${isBoss ? 'Briefing' : 'Projectomschrijving'}</${Button}>
      </div>
      <div class="phase-tabs">
        ${phases.map((e, i) => html`<button class=${`phase ${i === index ? 'active' : ''} ${p.exercises[e.id]?.status === 'passed' ? 'done' : ''}`} disabled=${!unlocked(i)} onClick=${() => setIndex(i)}>
          <${Icon} name=${p.exercises[e.id]?.status === 'passed' ? 'check' : unlocked(i) ? (isBoss ? 'sword' : 'code') : 'lock'} size="14" />
          <span>${e.phase || `Fase ${i + 1}`}</span>
        </button>`)}
      </div>
    </${LessonHeader}>
    <div class="lesson-body wide">
      ${prevFiles && !p.exercises[current.id]?.files ? html`<${Callout} tone="tip" title="Verder bouwen?">
        Je kan deze fase starten vanaf je eigen code van de vorige fase.
        <${Button} size="sm" icon="git" onClick=${() => { engine.saveDraft(current.id, prevFiles); setSeed(seed + 1); }}>Neem mijn code van de vorige fase over</${Button}>
      </${Callout}>` : null}
      <${Workbench} key=${current.id + seed} exerciseId=${current.id} embedded onGraded=${(res) => {
        if (res.passed && index + 1 < phases.length) setTimeout(() => setIndex(index + 1), 1500);
      }} />
    </div>
    ${briefing ? html`<${Modal} title=${lesson.title} wide onClose=${() => setBriefing(false)} footer=${html`<${Button} variant="primary" icon=${isBoss ? 'sword' : 'play'} onClick=${() => setBriefing(false)}>${isBoss ? 'Start het gevecht' : 'Start het project'}</${Button}>`}>
      ${lesson.story ? html`<p class="boss-story">${lesson.story}</p>` : null}
      <${Markdown} text=${detail.understand} />
      <h4>Fases</h4>
      <ol>${phases.map((e) => html`<li><strong>${e.phase || e.title}</strong> — ${e.title}</li>`)}</ol>
    </${Modal}>` : null}
  </div>`;
}

/** Een verzameling extra oefeningen (vrij te kiezen). */
function ChallengeSet({ lesson, detail }) {
  const p = useProgress();
  return html`<div class="lesson">
    <${LessonHeader} lesson=${lesson} />
    <div class="lesson-body">
      <${Markdown} text=${detail.understand} />
      <div class="grid grid-auto" style="margin-top:16px">
        ${lesson.exercises.map((e) => html`<a class="card interactive exercise-card" href=${`#/lab/${e.id}`}>
          <div class="row between"><${Badge} tone="xp" icon="zap">${e.xp} XP</${Badge}>${p.exercises[e.id]?.status === 'passed' ? html`<${Badge} tone="success" icon="check">Geslaagd</${Badge}>` : null}</div>
          <h4 style="margin:10px 0 4px">${e.title}</h4>
          <p class="faint xs">${e.source || ''}</p>
        </a>`)}
      </div>
    </div>
  </div>`;
}
