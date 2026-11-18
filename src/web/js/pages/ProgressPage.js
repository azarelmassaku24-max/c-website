import { html, useState } from '../lib/preact.js';
import { Icon } from '../components/Icon.js';
import { Badge, Card, LinkButton, Progress, Ring, Segmented } from '../components/ui.js';
import { useProgress } from '../hooks/useProgress.js';
import { curriculumService } from '../services/curriculumService.js';
import { levelInfo, rankFor } from '../progress/xp.js';
import { summary, weakSkills, topErrors, today, addDays } from '../progress/stats.js';
import { levelProgress } from '../progress/unlocks.js';
import { practiceFor } from '../progress/recommend.js';

export function ProgressPage() {
  const p = useProgress();
  const c = curriculumService.get();
  const s = summary(p);
  const info = levelInfo(p.xp);
  const weak = weakSkills(p, c, 6);
  const errors = topErrors(p, 10);
  const [sort, setSort] = useState('mastery');
  const skills = c.skills.map((sk) => ({ ...sk, st: p.skills[sk.id] || { mastery: 0, attempts: 0, passes: 0, failures: 0 } }))
    .filter((sk) => sk.st.attempts > 0 || sort === 'all')
    .sort((a, b) => sort === 'mastery' ? b.st.mastery - a.st.mastery : 0);

  return html`<div class="page">
    <div class="page-header">
      <div>
        <div class="eyebrow">Progress</div>
        <h1>Jouw leervoortgang</h1>
        <p>Wat je beheerst, wat nog moeilijk gaat en welke fouten je het vaakst maakt. Gebaseerd op je echte pogingen.</p>
      </div>
    </div>

    <div class="grid grid-4">
      <${Stat} icon="zap" tone="warning" value=${p.xp.toLocaleString('nl-BE')} label="XP" sub=${`Level ${info.level} · ${rankFor(info.level, c.ranks)}`} />
      <${Stat} icon="checkCircle" tone="success" value=${s.passed} label="Opgeloste opdrachten" sub=${`${s.attempts} pogingen in totaal`} />
      <${Stat} icon="target" value=${s.avgAttempts ? s.avgAttempts.toFixed(1).replace('.', ',') : '–'} label="Gem. pogingen per opdracht" sub=${`${s.hints} hints · ${s.solutions} oplossingen bekeken`} />
      <${Stat} icon="eye" tone="info" value=${s.predictions ? Math.round(s.predictionAccuracy * 100) + '%' : '–'} label="Voorspellingen juist (1e poging)" sub=${`${s.predictions} beantwoord`} />
    </div>

    <div class="grid grid-2" style="margin-top:16px">
      <${Card} title="Activiteit (laatste 12 weken)" icon="calendar">
        <${Heatmap} p=${p} />
        <div class="row between small muted" style="margin-top:8px"><span><${Icon} name="flame" size="14" /> ${s.streak} dagen op rij</span><span>${s.lessonsCompleted} lessen voltooid</span></div>
      </${Card}>
      <${Card} title="Voortgang per level" icon="graduation">
        <div class="stack-sm">${c.levels.map((l) => {
          const lp = levelProgress(p, l);
          return html`<a class="level-progress-row" href=${`#/academy/${l.id}`}>
            <span class="lp-num" style=${{ background: l.color }}>${l.kind === 'level' ? l.number : '★'}</span>
            <span class="grow small truncate">${l.title}</span>
            <span style="width:90px"><${Progress} value=${lp.pct} tone=${lp.pct === 100 ? 'success' : ''} size="thin" /></span>
            <span class="xs faint mono" style="width:34px;text-align:right">${lp.pct}%</span>
          </a>`;
        })}</div>
      </${Card}>
    </div>

    <div class="grid grid-2" style="margin-top:16px">
      <${Card} title="Moeilijke onderwerpen" icon="brain" sub="Lage beheersing na meerdere pogingen. Het systeem biedt hier extra oefeningen aan.">
        ${weak.length ? weak.map((w) => {
          const ex = practiceFor(p, c, w.id, 1)[0];
          return html`<div class="list-item">
            <div class="grow"><strong class="small">${w.skill?.title || w.id}</strong>
              <div class="faint xs">${w.failures} mislukt van ${w.attempts} pogingen · ${Math.round(w.mastery * 100)}% beheersing</div></div>
            ${ex ? html`<${LinkButton} size="sm" href=${`#/lab/${ex.id}`} icon="code">Oefen</${LinkButton}>` : null}
          </div>`;
        }) : html`<p class="muted small">Geen zwakke onderwerpen gevonden.</p>`}
      </${Card}>
      <${Card} title="Veelgemaakte fouten" icon="bug" sub="Fouten die je meermaals maakte. Herken je het patroon?">
        ${errors.length ? html`<table class="error-table"><tbody>${errors.map((e) => html`<tr>
          <td><span class="count-pill">${e.count}×</span></td>
          <td><div class="small">${e.title || e.code}</div><div class="faint xs mono">${e.code.startsWith('CS') ? e.code : e.code.replace(/^System\./, '')}</div></td>
          <td>${e.code.startsWith('CS') ? html`<a class="xs" href=${`#/kennisbank?q=${e.code}`}>uitleg</a>` : null}</td>
        </tr>`)}</tbody></table>` : html`<p class="muted small">Nog geen fouten geregistreerd.</p>`}
      </${Card}>
    </div>

    <div class="section-head">
      <h2>Beheersing per vaardigheid</h2>
      <${Segmented} value=${sort} onChange=${setSort} options=${[{ value: 'mastery', label: 'Geoefend' }, { value: 'all', label: 'Alle vaardigheden' }]} />
    </div>
    <div class="card">
      ${skills.length ? html`<div class="skill-bars">${skills.map((sk) => html`<div class="skill-bar-row">
        <span class="small truncate">${sk.title}</span>
        <${Progress} value=${sk.st.mastery * 100} tone=${sk.st.mastery >= 0.8 ? 'success' : sk.st.mastery < 0.4 && sk.st.attempts >= 2 ? 'warning' : ''} />
        <span class="xs mono faint">${Math.round(sk.st.mastery * 100)}%</span>
      </div>`)}</div>` : html`<p class="muted small">Begin met oefenen om je beheersing te zien.</p>`}
    </div>
  </div>`;
}

function Stat({ icon, tone, value, label, sub }) {
  return html`<div class="card stat-card">
    <div class=${`icon-tile ${tone || ''}`}><${Icon} name=${icon} /></div>
    <div class="stat"><span class="stat-value">${value}</span><span class="stat-label">${label}</span></div>
    ${sub ? html`<div class="faint xs">${sub}</div>` : null}
  </div>`;
}

function Heatmap({ p }) {
  const weeks = 12;
  const end = today();
  const startOffset = weeks * 7 - 1;
  const cells = [];
  let max = 1;
  for (let i = startOffset; i >= 0; i--) {
    const k = addDays(end, -i);
    const xp = p.activity[k]?.xp || 0;
    max = Math.max(max, xp);
    cells.push({ k, xp });
  }
  return html`<div class="heatmap" role="img" aria-label="Activiteit per dag">
    ${cells.map((cell) => {
      const level = cell.xp === 0 ? 0 : Math.min(4, Math.ceil((cell.xp / max) * 4));
      return html`<span class=${`hm l${level}`} title=${`${cell.k}: ${cell.xp} XP`}></span>`;
    })}
  </div>`;
}
