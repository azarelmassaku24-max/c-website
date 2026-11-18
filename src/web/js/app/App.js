import { html, useEffect, useState } from '../lib/preact.js';
import { Icon } from '../components/Icon.js';
import { Notifications } from '../components/Notifications.js';
import { useRoute } from './router.js';
import { useProgress } from '../hooks/useProgress.js';
import { levelInfo, rankFor } from '../progress/xp.js';
import { currentStreak, dueReviews } from '../progress/stats.js';
import { curriculumService } from '../services/curriculumService.js';
import { Dashboard } from '../pages/Dashboard.js';
import { LearningPath } from '../pages/LearningPath.js';
import { Academy } from '../pages/Academy.js';
import { LessonPage } from '../pages/LessonPage.js';
import { CodeLab } from '../pages/CodeLab.js';
import { Challenges } from '../pages/Challenges.js';
import { BossFights } from '../pages/BossFights.js';
import { ProgressPage } from '../pages/ProgressPage.js';
import { AchievementsPage } from '../pages/AchievementsPage.js';
import { Knowledge } from '../pages/Knowledge.js';
import { SettingsPage } from '../pages/SettingsPage.js';

const NAV = [
  { section: 'Leren', items: [
    { path: '/', label: 'Dashboard', icon: 'home' },
    { path: '/pad', label: 'Learning Path', icon: 'map' },
    { path: '/academy', label: 'Academy', icon: 'graduation' },
    { path: '/lab', label: 'Code Lab', icon: 'code' },
  ] },
  { section: 'Spelen', items: [
    { path: '/uitdagingen', label: 'Challenges', icon: 'target', badge: 'reviews' },
    { path: '/bosses', label: 'Boss Fights', icon: 'sword' },
    { path: '/prestaties', label: 'Achievements', icon: 'trophy' },
  ] },
  { section: 'Jij', items: [
    { path: '/voortgang', label: 'Progress', icon: 'chart' },
    { path: '/kennisbank', label: 'Knowledge Base', icon: 'library' },
    { path: '/instellingen', label: 'Settings', icon: 'settings' },
  ] },
];

function routeToPage(route) {
  const [first, second] = route.parts;
  switch (first) {
    case undefined: return { page: html`<${Dashboard} />`, crumb: 'Dashboard', nav: '/' };
    case 'pad': return { page: html`<${LearningPath} />`, crumb: 'Learning Path', nav: '/pad' };
    case 'academy': return { page: html`<${Academy} focus=${second} />`, crumb: 'Academy', nav: '/academy' };
    case 'les': return { page: html`<${LessonPage} id=${second} step=${route.query.get('stap')} />`, crumb: 'Les', nav: '/academy', full: true };
    case 'lab': return { page: html`<${CodeLab} exerciseId=${second} daily=${route.query.get('daily') === '1'} />`, crumb: 'Code Lab', nav: '/lab', full: true };
    case 'uitdagingen': return { page: html`<${Challenges} skill=${second} />`, crumb: 'Challenges', nav: '/uitdagingen' };
    case 'bosses': return { page: html`<${BossFights} />`, crumb: 'Boss Fights', nav: '/bosses' };
    case 'voortgang': return { page: html`<${ProgressPage} />`, crumb: 'Progress', nav: '/voortgang' };
    case 'prestaties': return { page: html`<${AchievementsPage} />`, crumb: 'Achievements', nav: '/prestaties' };
    case 'kennisbank': return { page: html`<${Knowledge} query=${route.query.get('q')} />`, crumb: 'Knowledge Base', nav: '/kennisbank' };
    case 'instellingen': return { page: html`<${SettingsPage} />`, crumb: 'Settings', nav: '/instellingen' };
    default: return { page: html`<div class="page"><h1>Pagina niet gevonden</h1><p><a href="#/">Terug naar het dashboard</a></p></div>`, crumb: 'Niet gevonden', nav: '' };
  }
}

export function App() {
  const route = useRoute();
  const p = useProgress();
  const c = curriculumService.get();
  const [open, setOpen] = useState(false);
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem('csq.sidebar') === 'collapsed');
  const { page, crumb, nav, full } = routeToPage(route);
  const info = levelInfo(p.xp);
  const rank = rankFor(info.level, c?.ranks);
  const streak = currentStreak(p);
  const reviews = c ? dueReviews(p, c).length : 0;

  useEffect(() => setOpen(false), [route.path]);
  useEffect(() => { try { localStorage.setItem('csq.sidebar', collapsed ? 'collapsed' : 'open'); } catch { /* */ } }, [collapsed]);

  return html`<div class=${`shell ${collapsed ? 'collapsed' : ''}`}>
    <nav class=${`sidebar ${open ? 'open' : ''}`} aria-label="Hoofdnavigatie">
      <a class="brand" href="#/"><img src="/img/logo.svg" alt="" /><span>C# Quest<small>Programming Academy</small></span></a>
      ${NAV.map((s) => html`<div class="nav-section">
        <div class="nav-label">${s.section}</div>
        ${s.items.map((item) => html`<a class=${`nav-link ${nav === item.path ? 'active' : ''}`} href=${'#' + item.path} title=${item.label}>
          <${Icon} name=${item.icon} /><span>${item.label}</span>
          ${item.badge === 'reviews' && reviews > 0 ? html`<span class="pill" title="Herhalingen klaar">${reviews}</span>` : null}
        </a>`)}
      </div>`)}
      <a class="sidebar-player" href="#/voortgang" title="Jouw voortgang">
        <div class="avatar">${(p.profile.name || 'L')[0].toUpperCase()}</div>
        <div class="meta">
          <div class="name truncate">${p.profile.name}</div>
          <div class="rank">Lvl ${info.level} · ${rank}</div>
          <div class="progress thin" style="margin-top:5px"><span style=${{ width: info.pct + '%' }}></span></div>
        </div>
      </a>
    </nav>
    ${open ? html`<div class="scrim" onClick=${() => setOpen(false)}></div>` : null}
    <div class="main">
      <header class="topbar">
        <button class="btn btn-ghost btn-icon menu-btn" onClick=${() => setOpen(true)} aria-label="Menu openen"><${Icon} name="menu" /></button>
        <button class="btn btn-ghost btn-icon btn-sm hide-sm" onClick=${() => setCollapsed(!collapsed)} aria-label="Zijbalk in-/uitklappen" title="Zijbalk in-/uitklappen"><${Icon} name="sidebar" /></button>
        <div class="crumbs"><span class="current">${crumb}</span></div>
        <div class="spacer"></div>
        <span class="topbar-stat streak" title="Dagen op rij geleerd"><${Icon} name="flame" />${streak}</span>
        <span class="topbar-stat xp" title="Ervaringspunten"><${Icon} name="zap" />${p.xp.toLocaleString('nl-BE')}</span>
        <span class="topbar-stat level" title=${`${info.inLevel}/${info.needed} XP tot level ${info.level + 1}`}>Lvl ${info.level}</span>
      </header>
      <main class=${full ? 'main-full' : ''} id="main">${page}</main>
    </div>
    <${Notifications} />
  </div>`;
}
