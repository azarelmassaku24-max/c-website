import { html, useEffect, useState } from '../lib/preact.js';
import { Icon } from '../components/Icon.js';
import { Badge, Progress, LinkButton, Segmented } from '../components/ui.js';
import { useProgress } from '../hooks/useProgress.js';
import { curriculumService } from '../services/curriculumService.js';
import { lessonStatus, lockReason, levelProgress, isLevelUnlocked, isLevelComplete } from '../progress/unlocks.js';

const KIND_ICON = { lesson: 'book', boss: 'sword', project: 'users', challenges: 'target', verdieping: 'sparkles' };
const STATUS_ICON = { completed: 'checkCircle', 'in-progress': 'play', available: 'circle', locked: 'lock' };

export function Academy({ focus }) {
  const p = useProgress();
  const c = curriculumService.get();
  const [filter, setFilter] = useState('all');
  const [open, setOpen] = useState(() => new Set(focus ? [focus] : c.levels.filter((l) => isLevelUnlocked(p, c, l) && !isLevelComplete(p, l)).slice(0, 1).map((l) => l.id)));
  useEffect(() => { if (focus) { setOpen((o) => new Set([...o, focus])); setTimeout(() => document.getElementById('lvl-' + focus)?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 50); } }, [focus]);

  const levels = c.levels.filter((l) => filter === 'all' || (filter === 'regulier' ? l.kind === 'level' : l.kind !== 'level'));
  const toggle = (id) => { const s = new Set(open); s.has(id) ? s.delete(id) : s.add(id); setOpen(s); };
  const totalLessons = c.orderedLessons.length;
  const totalExercises = c.exercises.size;

  return html`<div class="page">
    <div class="page-header">
      <div>
        <div class="eyebrow">Academy</div>
        <h1>Alle modules en lessen</h1>
        <p>${c.levels.filter((l) => l.kind === 'level').length} levels · ${totalLessons} lessen · ${totalExercises} programmeeropdrachten. De volgorde en terminologie volgen je cursus “Programmeren Basis”.</p>
      </div>
      <${Segmented} value=${filter} onChange=${setFilter} options=${[{ value: 'all', label: 'Alles' }, { value: 'regulier', label: 'Leertraject' }, { value: 'extra', label: 'Project & verdieping' }]} />
    </div>
    ${c.problems?.length ? html`<div class="callout warn"><${Icon} name="alert" /><div><strong>Let op:</strong> ${c.problems.length} probleem/problemen in de leerinhoud. Voer <code>dotnet run -- verify</code> uit voor details.</div></div>` : null}
    <div class="stack">
      ${levels.map((level) => {
        const unlocked = isLevelUnlocked(p, c, level);
        const prog = levelProgress(p, level);
        const isOpen = open.has(level.id);
        return html`<section class=${`level-card ${unlocked ? '' : 'locked'} ${prog.pct === 100 ? 'complete' : ''}`} id=${'lvl-' + level.id} style=${{ '--level-color': level.color }}>
          <button class="level-head" onClick=${() => toggle(level.id)} aria-expanded=${isOpen}>
            <div class="level-num">${level.kind === 'level' ? level.number : html`<${Icon} name=${level.kind === 'project' ? 'users' : 'sparkles'} />`}</div>
            <div class="grow">
              <div class="row wrap" style="gap:8px">
                <h2 class="level-title">${level.title}</h2>
                ${level.kind === 'verdieping' ? html`<${Badge} tone="info">Verdieping</${Badge}>` : level.kind === 'project' ? html`<${Badge} tone="success">Doorlopend project</${Badge}>` : null}
                ${!unlocked ? html`<${Badge} icon="lock">Vergrendeld</${Badge}>` : prog.pct === 100 ? html`<${Badge} tone="success" icon="check">Voltooid</${Badge}>` : null}
              </div>
              <div class="muted small">${level.subtitle}${level.source ? ' · ' + level.source : ''}</div>
            </div>
            <div class="level-meta hide-sm">
              <div class="small"><strong>${prog.done}</strong>/${prog.total}</div>
              <div style="width:110px"><${Progress} value=${prog.pct} tone=${prog.pct === 100 ? 'success' : ''} size="thin" /></div>
            </div>
            <${Icon} name=${isOpen ? 'chevronDown' : 'chevronRight'} />
          </button>
          ${isOpen ? html`<div class="level-body fade-in">
            ${level.description ? html`<p class="muted">${level.description}</p>` : null}
            ${level.note ? html`<div class="callout info"><${Icon} name="info" /><div>${level.note}</div></div>` : null}
            ${!unlocked ? html`<p class="small warning-text"><${Icon} name="lock" size="14" /> Voltooi eerst ${level.requires.map((r) => c.levelMap.get(r)?.title).join(' en ')}.</p>` : null}
            <ol class="lesson-list">
              ${level.lessons.map((lesson, i) => {
                const st = lessonStatus(p, c, lesson);
                const passedEx = lesson.exercises.filter((e) => p.exercises[e.id]?.status === 'passed').length;
                return html`<li class=${`lesson-row ${st} kind-${lesson.kind}`}>
                  <a href=${st === 'locked' ? null : `#/les/${lesson.id}`} title=${st === 'locked' ? lockReason(p, c, lesson) : ''} aria-disabled=${st === 'locked'}>
                    <span class="lesson-status"><${Icon} name=${st === 'locked' ? 'lock' : lesson.kind !== 'lesson' && st !== 'completed' ? KIND_ICON[lesson.kind] : STATUS_ICON[st]} size="17" /></span>
                    <span class="grow">
                      <span class="lesson-name">${lesson.kind === 'boss' ? 'Boss: ' : ''}${lesson.title}</span>
                      <span class="lesson-sub">${lesson.summary}</span>
                    </span>
                    <span class="lesson-tags hide-sm">
                      ${lesson.source ? html`<span class="source-tag">${lesson.source}</span>` : null}
                      <span class="faint xs">${passedEx}/${lesson.exercises.length} opdrachten</span>
                    </span>
                  </a>
                </li>`;
              })}
            </ol>
          </div>` : null}
        </section>`;
      })}
    </div>
  </div>`;
}
