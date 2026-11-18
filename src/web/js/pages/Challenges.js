import { html, useEffect, useState } from '../lib/preact.js';
import { Icon } from '../components/Icon.js';
import { Badge, Card, Difficulty, Empty, LinkButton, Segmented } from '../components/ui.js';
import { useProgress } from '../hooks/useProgress.js';
import { curriculumService } from '../services/curriculumService.js';
import { dueReviews, today, weakSkills } from '../progress/stats.js';
import { ensureDaily, availableExercises, practiceFor, targetDifficulty } from '../progress/recommend.js';

export function Challenges({ skill: initialSkill }) {
  const p = useProgress();
  const c = curriculumService.get();
  const [skill, setSkill] = useState(initialSkill || '');
  const [show, setShow] = useState('open');
  useEffect(() => { ensureDaily(c); }, []);
  const daily = p.daily?.date === today() ? p.daily : null;
  const reviews = dueReviews(p, c);
  const weak = weakSkills(p, c, 6);
  const pool = availableExercises(p, c)
    .filter((e) => e.role !== 'boss')
    .filter((e) => !skill || e.skills.includes(skill))
    .filter((e) => show === 'all' || (show === 'open' ? p.exercises[e.id]?.status !== 'passed' : p.exercises[e.id]?.status === 'passed'));
  const skillsAvailable = [...new Set(availableExercises(p, c).flatMap((e) => e.skills))].map((s) => c.skillMap.get(s) || { id: s, title: s });
  const recommended = skill ? practiceFor(p, c, skill, 4) : [];

  return html`<div class="page">
    <div class="page-header">
      <div>
        <div class="eyebrow">Challenges</div>
        <h1>Oefenen, herhalen en uitdagen</h1>
        <p>Herhaal wat je geleerd hebt op het juiste moment (spaced repetition), werk aan je zwakke punten en kies extra uitdagingen per onderwerp.</p>
      </div>
    </div>
    <div class="grid grid-3">
      <${Card} title="Dagelijkse uitdaging" icon="calendar" sub=${daily ? `${daily.done.length}/${daily.ids.length} voltooid${daily.bonusAwarded ? ' · bonus behaald!' : ' · +50 XP bonus'}` : ''}>
        ${daily?.ids.length ? daily.ids.map((id) => {
          const ex = c.exercises.get(id);
          return ex ? html`<${ExerciseRow} ex=${ex} p=${p} href=${`#/lab/${id}?daily=1`} done=${daily.done.includes(id)} />` : null;
        }) : html`<p class="muted small">Nog niet beschikbaar.</p>`}
      </${Card}>
      <${Card} title=${`Herhalingen (${reviews.length})`} icon="repeat" sub="Je geheugen versterkt het meest als je herhaalt net voor je iets vergeet.">
        ${reviews.length ? reviews.slice(0, 6).map((r) => html`<${ExerciseRow} ex=${r.exercise} p=${p} href=${`#/lab/${r.id}`} />`) : html`<p class="muted small">Geen herhalingen gepland voor vandaag.</p>`}
      </${Card}>
      <${Card} title="Werk aan je zwakke punten" icon="brain">
        ${weak.length ? weak.map((w) => html`<button class="list-item clickable weak-skill" onClick=${() => setSkill(w.id)}>
          <span class="grow"><strong class="small">${w.skill?.title || w.id}</strong><span class="faint xs"> · ${Math.round(w.mastery * 100)}% beheersing</span></span>
          <${Icon} name="chevronRight" size="15" />
        </button>`) : html`<p class="muted small">Nog geen zwakke punten — goed bezig!</p>`}
      </${Card}>
    </div>

    <div class="section-head">
      <h2>Oefenpool</h2>
      <div class="row wrap">
        <select class="select" style="width:auto" value=${skill} onChange=${(e) => setSkill(e.currentTarget.value)}>
          <option value="">Alle onderwerpen</option>
          ${skillsAvailable.map((s) => html`<option value=${s.id}>${s.title}</option>`)}
        </select>
        <${Segmented} value=${show} onChange=${setShow} options=${[{ value: 'open', label: 'Open' }, { value: 'done', label: 'Opgelost' }, { value: 'all', label: 'Alles' }]} />
      </div>
    </div>
    ${skill && recommended.length ? html`<div class="callout tip"><${Icon} name="sparkles" /><div><strong>Aanbevolen voor jou</strong> (moeilijkheid rond ${targetDifficulty(p, skill)}/5 op basis van je beheersing):
      ${recommended.map((e, i) => html`${i ? ' · ' : ' '}<a href=${`#/lab/${e.id}`}>${e.title}</a>`)}</div></div>` : null}
    ${pool.length ? html`<div class="grid grid-auto">${pool.map((ex) => html`<a class="card interactive exercise-card" href=${`#/lab/${ex.id}`}>
      <div class="row between">
        <${Badge} tone=${ex.role === 'uitdaging' ? 'accent' : ex.role === 'meesterschap' ? 'warning' : 'info'}>${{ uitdaging: 'Uitdaging', meesterschap: 'Meesterschap', oefenen: 'Oefening', project: 'Project' }[ex.role] || ex.role}</${Badge}>
        ${p.exercises[ex.id]?.status === 'passed' ? html`<${Icon} name="checkCircle" size="18" class="success-text" />` : html`<${Difficulty} value=${ex.difficulty} />`}
      </div>
      <h4 class="exercise-title">${ex.title}</h4>
      <p class="faint xs">Level ${ex.lesson.level.number} · ${ex.lesson.title}</p>
      <div class="row between"><span class="xs muted">${ex.source || ''}</span><${Badge} tone="xp">${ex.xp} XP</${Badge}></div>
    </a>`)}</div>` : html`<${Empty} icon="target" title="Geen opdrachten in deze selectie">Ontgrendel meer levels in de Academy of kies een ander filter.</${Empty}>`}
  </div>`;
}

function ExerciseRow({ ex, p, href, done }) {
  const passed = done ?? p.exercises[ex.id]?.status === 'passed';
  return html`<a class="list-item clickable" href=${href}>
    <div class=${`icon-tile ${passed ? 'success' : ''}`}><${Icon} name=${passed ? 'check' : 'code'} /></div>
    <div class="grow"><div class="truncate small"><strong>${ex.title}</strong></div><div class="faint xs">${ex.lesson.title}</div></div>
    <${Badge} tone="xp">${ex.xp}</${Badge}>
  </a>`;
}
