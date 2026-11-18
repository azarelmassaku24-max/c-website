import { html } from '../lib/preact.js';
import { Icon } from '../components/Icon.js';
import { Badge, LinkButton, Progress, Empty } from '../components/ui.js';
import { useProgress } from '../hooks/useProgress.js';
import { curriculumService } from '../services/curriculumService.js';
import { lessonStatus, lockReason } from '../progress/unlocks.js';

export function BossFights() {
  const p = useProgress();
  const c = curriculumService.get();
  const bosses = c.orderedLessons.filter((l) => l.kind === 'boss' || l.kind === 'project');
  const defeated = bosses.filter((b) => lessonStatus(p, c, b) === 'completed').length;

  return html`<div class="page">
    <div class="page-header">
      <div>
        <div class="eyebrow">Boss Fights</div>
        <h1>Grote programmeeruitdagingen</h1>
        <p>Aan het einde van elk level wacht een boss: een groter project waarin je alles van dat level (en eerder) combineert. Versla de boss om het volgende level te ontgrendelen.</p>
      </div>
      <${Badge} tone="danger" icon="sword">${defeated}/${bosses.length} verslagen</${Badge}>
    </div>
    ${bosses.length === 0 ? html`<${Empty} icon="sword" title="Nog geen bosses" />` : html`<div class="boss-grid">
      ${bosses.map((b) => {
        const st = lessonStatus(p, c, b);
        const passed = b.exercises.filter((e) => p.exercises[e.id]?.status === 'passed').length;
        const hp = b.exercises.length ? 100 - Math.round((passed / b.exercises.length) * 100) : 100;
        const xp = b.exercises.reduce((s, e) => s + e.xp, 0) + (b.kind === 'boss' ? 150 : 200);
        return html`<article class=${`boss-card ${st}`} style=${{ '--level-color': b.level.color }}>
          <div class="boss-card-top">
            <div class="boss-icon"><${Icon} name=${st === 'locked' ? 'lock' : st === 'completed' ? 'crown' : b.kind === 'project' ? 'users' : 'skull'} size="26" /></div>
            <div class="grow">
              <div class="eyebrow">${b.kind === 'project' ? 'Eindproject' : `Level ${b.level.number}`} · ${b.level.title}</div>
              <h3>${b.title}</h3>
            </div>
          </div>
          <p class="muted small">${b.story || b.summary}</p>
          <div class="row between xs"><span>${b.kind === 'project' ? 'Voortgang' : 'Boss HP'}</span><span class="mono">${b.kind === 'project' ? `${passed}/${b.exercises.length}` : hp + '%'}</span></div>
          <${Progress} value=${b.kind === 'project' ? (passed / Math.max(1, b.exercises.length)) * 100 : hp} tone=${b.kind === 'project' ? 'success' : 'danger'} />
          <div class="row wrap" style="gap:6px;margin:10px 0">
            <${Badge} icon="layers">${b.exercises.length} fases</${Badge}>
            <${Badge} tone="xp" icon="zap">${xp} XP</${Badge}>
            ${b.skills.slice(0, 3).map((s) => html`<${Badge}>${c.skillMap.get(s)?.title || s}</${Badge}>`)}
          </div>
          ${st === 'locked'
            ? html`<p class="faint xs"><${Icon} name="lock" size="12" /> ${lockReason(p, c, b)}</p>`
            : html`<${LinkButton} variant=${st === 'completed' ? undefined : 'primary'} href=${`#/les/${b.id}`} icon=${st === 'completed' ? 'eye' : 'sword'}>${st === 'completed' ? 'Bekijk' : st === 'in-progress' ? 'Verder vechten' : 'Uitdagen'}</${LinkButton}>`}
        </article>`;
      })}
    </div>`}
  </div>`;
}
