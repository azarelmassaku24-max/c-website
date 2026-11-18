import { html, useMemo, useState } from '../lib/preact.js';
import { Icon } from '../components/Icon.js';
import { Badge, LinkButton, Progress } from '../components/ui.js';
import { useProgress } from '../hooks/useProgress.js';
import { curriculumService } from '../services/curriculumService.js';
import { skillStatus, lessonStatus } from '../progress/unlocks.js';
import { practiceFor } from '../progress/recommend.js';

const STATUS_LABEL = { mastered: 'Beheerst', learning: 'Bezig', available: 'Beschikbaar', locked: 'Vergrendeld' };

/** Visuele skill tree: vaardigheden en hun afhankelijkheden. */
export function LearningPath() {
  const p = useProgress();
  const c = curriculumService.get();
  const [selected, setSelected] = useState(null);
  const skills = c.skills;
  const W = 140, H = 64, GX = 190, GY = 104, PAD = 30;
  const layout = useMemo(() => skills.map((s) => ({ ...s, px: PAD + s.x * GX, py: PAD + s.y * GY })), [skills]);
  const byId = new Map(layout.map((s) => [s.id, s]));
  if (!layout.length) return html`<div class="page"><h1>Skill tree</h1><p class="muted">Er zijn nog geen vaardigheden gedefinieerd in curriculum.json.</p></div>`;
  const width = Math.max(...layout.map((s) => s.px)) + W + PAD;
  const height = Math.max(...layout.map((s) => s.py)) + H + PAD;
  const status = (s) => skillStatus(p, c, s);
  const sel = selected ? byId.get(selected) : null;
  const counts = layout.reduce((acc, s) => { acc[status(s)] = (acc[status(s)] || 0) + 1; return acc; }, {});

  return html`<div class="page wide">
    <div class="page-header">
      <div>
        <div class="eyebrow">Learning Path</div>
        <h1>Skill tree</h1>
        <p>Elke vaardigheid bouwt verder op eerdere vaardigheden. Een vaardigheid is <strong>beheerst</strong> wanneer je ze meermaals zelfstandig correct toepaste.</p>
      </div>
      <div class="row wrap">
        <${Badge} tone="success" icon="star">${counts.mastered || 0} beheerst</${Badge}>
        <${Badge} tone="accent" icon="play">${counts.learning || 0} bezig</${Badge}>
        <${Badge} icon="lock">${counts.locked || 0} vergrendeld</${Badge}>
      </div>
    </div>
    <div class="skilltree-wrap">
      <div class="skilltree-scroll">
        <svg class="skilltree" width=${width} height=${height} viewBox=${`0 0 ${width} ${height}`} role="img" aria-label="Skill tree">
          <defs>
            <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
              <path d="M0 0 L10 5 L0 10 z" fill="var(--border-strong)" />
            </marker>
          </defs>
          ${layout.flatMap((s) => (s.requires || []).map((r) => {
            const from = byId.get(r);
            if (!from) return null;
            const x1 = from.px + W, y1 = from.py + H / 2, x2 = s.px, y2 = s.py + H / 2;
            const mx = (x1 + x2) / 2;
            const active = status(from) === 'mastered' || status(from) === 'learning';
            return html`<path class=${`edge ${active ? 'active' : ''}`} d=${`M${x1} ${y1} C${mx} ${y1}, ${mx} ${y2}, ${x2} ${y2}`} marker-end="url(#arrow)" />`;
          }))}
          ${layout.map((s) => {
            const st = status(s);
            const m = p.skills[s.id]?.mastery || 0;
            return html`<g class=${`node ${st} ${selected === s.id ? 'selected' : ''}`} transform=${`translate(${s.px},${s.py})`} onClick=${() => setSelected(s.id)} tabindex="0"
              onKeyDown=${(e) => e.key === 'Enter' && setSelected(s.id)} role="button" aria-label=${`${s.title}: ${STATUS_LABEL[st]}`}>
              <rect width=${W} height=${H} rx="12" />
              <text x="12" y="25" class="node-title">${s.title.length > 18 ? s.title.slice(0, 17) + '…' : s.title}</text>
              <text x="12" y="44" class="node-sub">${STATUS_LABEL[st]}${st !== 'locked' ? ` · ${Math.round(m * 100)}%` : ''}</text>
              <rect x="12" y="52" width=${W - 24} height="4" rx="2" class="node-bar-bg" />
              <rect x="12" y="52" width=${(W - 24) * m} height="4" rx="2" class="node-bar" />
            </g>`;
          })}
        </svg>
      </div>
      <aside class="skill-panel card">
        ${sel ? html`<${SkillDetail} skill=${sel} status=${status(sel)} />` : html`<div class="empty"><${Icon} name="map" /><h3>Kies een vaardigheid</h3><p>Klik op een blok in de skill tree voor details, je beheersing en de bijhorende lessen.</p></div>`}
      </aside>
    </div>
  </div>`;
}

function SkillDetail({ skill, status }) {
  const p = useProgress();
  const c = curriculumService.get();
  const s = p.skills[skill.id] || {};
  const lessons = c.orderedLessons.filter((l) => l.skills.includes(skill.id));
  const practice = status !== 'locked' ? practiceFor(p, c, skill.id, 3) : [];
  const level = c.levelMap.get(skill.level);
  return html`<div class="stack">
    <div>
      <div class="eyebrow">Level ${level?.number ?? ''} · ${level?.title ?? ''}</div>
      <h2 style="margin:0">${skill.title}</h2>
      ${skill.description ? html`<p class="muted small">${skill.description}</p>` : null}
    </div>
    <div>
      <div class="row between small"><span>Beheersing</span><strong>${Math.round((s.mastery || 0) * 100)}%</strong></div>
      <${Progress} value=${(s.mastery || 0) * 100} tone=${status === 'mastered' ? 'success' : ''} />
      <div class="faint xs" style="margin-top:4px">${s.passes || 0} keer geslaagd · ${s.failures || 0} mislukte pogingen</div>
    </div>
    ${skill.requires?.length ? html`<div><h4>Vereist</h4><div class="chip-row">${skill.requires.map((r) => html`<${Badge}>${c.skillMap.get(r)?.title || r}</${Badge}>`)}</div></div>` : null}
    <div>
      <h4>Lessen</h4>
      ${lessons.length ? html`<div class="list">${lessons.map((l) => {
        const st = lessonStatus(p, c, l);
        return html`<a class="list-item clickable" href=${st === 'locked' ? null : `#/les/${l.id}`}>
          <${Icon} name=${st === 'completed' ? 'checkCircle' : st === 'locked' ? 'lock' : 'book'} size="16" />
          <span class="grow small">${l.title}</span>
        </a>`;
      })}</div>` : html`<p class="faint small">Geen lessen gekoppeld.</p>`}
    </div>
    ${practice.length ? html`<div><h4>Oefen deze vaardigheid</h4>
      ${practice.map((e) => html`<${LinkButton} size="sm" href=${`#/lab/${e.id}`} icon="code" class="practice-btn">${e.title}</${LinkButton}>`)}
    </div>` : null}
  </div>`;
}
