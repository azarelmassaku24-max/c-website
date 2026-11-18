import { html } from '../lib/preact.js';
import { Workbench } from '../features/lab/Workbench.js';
import { curriculumService } from '../services/curriculumService.js';
import { useProgress } from '../hooks/useProgress.js';
import { lessonStatus, isLevelUnlocked } from '../progress/unlocks.js';
import { LinkButton } from '../components/ui.js';
import { Icon } from '../components/Icon.js';

/** Code Lab: vrije programmeeromgeving, of één specifieke opdracht (#/lab/<id>). */
export function CodeLab({ exerciseId, daily }) {
  const p = useProgress();
  const c = curriculumService.get();
  let initial = null;
  if (!exerciseId) {
    try {
      const imported = JSON.parse(sessionStorage.getItem('csq.lab.import') || 'null');
      if (imported) { initial = imported; sessionStorage.removeItem('csq.lab.import'); }
    } catch { /* */ }
  }
  if (exerciseId) {
    const ex = c.exercises.get(exerciseId);
    if (!ex) return html`<div class="page"><h1>Opdracht niet gevonden</h1><a href="#/lab">Naar het vrije Code Lab</a></div>`;
    const lesson = ex.lesson;
    const open = isLevelUnlocked(p, c, lesson.level) && (lesson.kind === 'challenges' || lesson.kind === 'verdieping' || lessonStatus(p, c, lesson) !== 'locked');
    if (!open) return html`<div class="page"><div class="locked-screen card">
      <div class="icon-tile muted" style="width:56px;height:56px"><${Icon} name="lock" size="26" /></div>
      <h2>${ex.title}</h2><p class="muted">Deze opdracht hoort bij een les die nog vergrendeld is.</p>
      <${LinkButton} href="#/academy" icon="graduation">Naar de Academy</${LinkButton}></div></div>`;
  }
  return html`<div class="lab-page"><${Workbench} exerciseId=${exerciseId || null} daily=${daily} initial=${initial} /></div>`;
}
