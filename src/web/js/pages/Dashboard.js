import { html, useEffect } from '../lib/preact.js';
import { Icon } from '../components/Icon.js';
import { Badge, Card, LinkButton, Progress, Ring } from '../components/ui.js';
import { AchievementBadge } from '../components/Notifications.js';
import { useProgress } from '../hooks/useProgress.js';
import { curriculumService } from '../services/curriculumService.js';
import { levelInfo, rankFor } from '../progress/xp.js';
import { summary, currentStreak, dueReviews, weakSkills, today, addDays } from '../progress/stats.js';
import { nextMission, ensureDaily, practiceFor } from '../progress/recommend.js';
import { levelProgress, isLevelUnlocked } from '../progress/unlocks.js';
import { ACHIEVEMENTS } from '../achievements/definitions.js';

const STEP_NAMES = ['Begrijpen', 'Voorbeeld', 'Voorspellen', 'Programmeren', 'Controleren', 'Meesterschap'];

export function Dashboard() {
  const p = useProgress();
  const c = curriculumService.get();
  useEffect(() => { ensureDaily(c); }, []);
  const info = levelInfo(p.xp);
  const stats = summary(p);
  const mission = nextMission(p, c);
  const reviews = dueReviews(p, c);
  const weak = weakSkills(p, c, 4);
  const daily = p.daily?.date === today() ? p.daily : null;
  const recentAch = Object.entries(p.achievements).sort((a, b) => b[1].unlockedAt.localeCompare(a[1].unlockedAt)).slice(0, 4)
    .map(([id]) => ACHIEVEMENTS.find((a) => a.id === id)).filter(Boolean);
  const hour = new Date().getHours();
  const greeting = hour < 6 ? 'Goedenacht' : hour < 12 ? 'Goedemorgen' : hour < 18 ? 'Goedemiddag' : 'Goedenavond';
  const currentLevel = mission?.lesson.level || c.levels.find((l) => l.kind === 'level' && isLevelUnlocked(p, c, l));
  const lp = currentLevel ? levelProgress(p, currentLevel) : null;

  return html`<div class="page">
    <div class="page-header">
      <div>
        <div class="eyebrow">${greeting}, ${p.profile.name}</div>
        <h1>${mission ? 'Klaar voor je volgende missie?' : 'Je hebt het volledige leerpad voltooid!'}</h1>
        <p>Rang: <strong>${rankFor(info.level, c.ranks)}</strong> · ${stats.passed} oefeningen opgelost · ${stats.lessonsCompleted} lessen voltooid</p>
      </div>
      <${LinkButton} href="#/lab" icon="code">Vrij Code Lab</${LinkButton}>
    </div>

    <div class="grid dash-grid">
      <section class="card glow mission-card span-2">
        ${mission ? html`
          <div class="row between wrap">
            <div class="eyebrow"><${Icon} name="target" size="13" /> Volgende missie · Level ${mission.lesson.level.number}</div>
            ${mission.lesson.kind === 'boss' ? html`<${Badge} tone="danger" icon="sword">Boss fight</${Badge}>` : mission.lesson.kind === 'project' ? html`<${Badge} tone="success" icon="users">Project</${Badge}>` : null}
          </div>
          <h2 class="mission-title">${mission.lesson.title}</h2>
          <p class="muted">${mission.lesson.summary}</p>
          ${mission.lesson.kind === 'lesson' ? html`<div class="mission-steps">
            ${STEP_NAMES.map((s, i) => html`<span class=${`mini-step ${i < mission.step ? 'done' : i === mission.step ? 'current' : ''}`}>${s}</span>`)}
          </div>` : null}
          <div class="row wrap" style="margin-top:16px">
            <${LinkButton} variant="primary" size="lg" href=${`#/les/${mission.lesson.id}`} iconRight="arrowRight">${mission.status === 'in-progress' ? 'Verdergaan' : 'Starten'}</${LinkButton}>
            <span class="muted small"><${Icon} name="clock" size="13" /> ±${mission.lesson.minutes} min${mission.lesson.source ? ' · ' + mission.lesson.source : ''}</span>
          </div>` : html`<h2>Proficiat!</h2><p class="muted">Herhaal onderwerpen via Challenges of bouw iets eigens in het Code Lab.</p>`}
      </section>

      <${Card} title="Jouw niveau" icon="zap">
        <div class="row" style="gap:18px">
          <${Ring} value=${info.pct} size=${84} stroke=${8}><span class="ring-big">${info.level}</span></${Ring}>
          <div class="stack-sm">
            <div class="stat"><span class="stat-value xp-text">${p.xp.toLocaleString('nl-BE')}</span><span class="stat-label">XP totaal</span></div>
            <span class="small muted">${(info.needed - info.inLevel).toLocaleString('nl-BE')} XP tot level ${info.level + 1}</span>
          </div>
        </div>
        <div class="row between" style="margin-top:14px">
          <span class="small"><${Icon} name="flame" size="14" class="streak-ic" /> <strong>${currentStreak(p)}</strong> dagen op rij</span>
          <${WeekStrip} p=${p} />
        </div>
      </${Card}>

      <${Card} title="Dagelijkse uitdaging" icon="calendar" sub="3 opdrachten op maat: een herhaling, een zwak punt en iets nieuws. Bonus: +50 XP.">
        ${daily && daily.ids.length ? html`<div class="list">${daily.ids.map((id) => {
          const ex = c.exercises.get(id);
          if (!ex) return null;
          const done = daily.done.includes(id);
          return html`<a class="list-item clickable" href=${`#/lab/${id}?daily=1`}>
            <div class=${`icon-tile ${done ? 'success' : ''}`}><${Icon} name=${done ? 'check' : 'code'} /></div>
            <div class="grow"><div class="truncate"><strong>${ex.title}</strong></div><div class="faint xs">Level ${ex.lesson.level.number} · ${ex.lesson.title}</div></div>
            <${Badge} tone="xp">${ex.xp} XP</${Badge}>
          </a>`;
        })}</div>
        <${Progress} value=${(daily.done.length / daily.ids.length) * 100} tone="success" label="Dagelijkse uitdaging" />`
        : html`<p class="muted small">Start je eerste les om dagelijkse uitdagingen vrij te spelen.</p>`}
      </${Card}>

      <${Card} title="Herhalen (spaced repetition)" icon="repeat" sub="Opdrachten die je eerder oploste komen terug op het juiste moment, zodat je ze niet vergeet.">
        ${reviews.length ? html`<div class="list">${reviews.slice(0, 4).map((r) => html`<a class="list-item clickable" href=${`#/lab/${r.id}`}>
          <div class="icon-tile info"><${Icon} name="repeat" /></div>
          <div class="grow truncate"><strong>${r.exercise.title}</strong><div class="faint xs">Gepland voor ${r.due === today() ? 'vandaag' : r.due}</div></div>
        </a>`)}</div>
        ${reviews.length > 4 ? html`<a class="small" href="#/uitdagingen">+${reviews.length - 4} meer</a>` : null}`
        : html`<p class="muted small"><${Icon} name="checkCircle" size="14" class="success-text" /> Niets te herhalen vandaag.</p>`}
      </${Card}>

      <${Card} title="Moeilijke onderwerpen" icon="brain" sub="Op basis van je pogingen en fouten. Hier ligt je grootste winst.">
        ${weak.length ? html`<div class="list">${weak.map((w) => {
          const ex = practiceFor(p, c, w.id, 1)[0];
          return html`<div class="list-item">
            <div class="grow"><strong>${w.skill?.title || w.id}</strong>
              <div class="row" style="gap:8px"><${Progress} value=${w.mastery * 100} tone="warning" size="thin" /><span class="faint xs">${Math.round(w.mastery * 100)}%</span></div></div>
            ${ex ? html`<${LinkButton} size="sm" href=${`#/lab/${ex.id}`}>Oefen</${LinkButton}>` : null}
          </div>`;
        })}</div>` : html`<p class="muted small">Nog geen zwakke punten gedetecteerd. Het systeem leert je kennen naarmate je oefent.</p>`}
      </${Card}>

      ${currentLevel ? html`<${Card} title=${`Level ${currentLevel.number}: ${currentLevel.title}`} icon="graduation" sub=${currentLevel.source}>
        <div class="row" style="gap:16px">
          <${Ring} value=${lp.pct} size=${64}>${lp.pct}%</${Ring}>
          <div class="small muted">${lp.done} van ${lp.total} onderdelen voltooid${currentLevel.lessons.some((l) => l.kind === 'boss') ? ', inclusief de boss fight.' : '.'}</div>
        </div>
        <div style="margin-top:12px"><${LinkButton} size="sm" href=${`#/academy/${currentLevel.id}`} iconRight="arrowRight">Bekijk level</${LinkButton}></div>
      </${Card}>` : null}

      <${Card} title="Recente prestaties" icon="trophy" actions=${html`<a class="small" href="#/prestaties">Alles</a>`}>
        ${recentAch.length ? html`<div class="ach-row">${recentAch.map((a) => html`<div class="ach-mini" title=${a.description}><${AchievementBadge} achievement=${a} unlocked size=${44} /><span class="xs">${a.title}</span></div>`)}</div>`
          : html`<p class="muted small">Los je eerste oefening op voor je eerste badge.</p>`}
      </${Card}>
    </div>
  </div>`;
}

function WeekStrip({ p }) {
  const days = [];
  for (let i = 6; i >= 0; i--) {
    const k = addDays(today(), -i);
    const a = p.activity[k];
    days.push({ k, active: a && (a.xp > 0 || a.attempts > 0), label: ['zo', 'ma', 'di', 'wo', 'do', 'vr', 'za'][new Date(k).getDay()] });
  }
  return html`<div class="week-strip">${days.map((d) => html`<span class=${d.active ? 'on' : ''} title=${d.k}>${d.label}</span>`)}</div>`;
}
