import { html } from '../lib/preact.js';
import { Badge } from '../components/ui.js';
import { AchievementBadge } from '../components/Notifications.js';
import { useProgress } from '../hooks/useProgress.js';
import { ACHIEVEMENTS } from '../achievements/definitions.js';

const TIERS = ['brons', 'zilver', 'goud', 'platina'];

export function AchievementsPage() {
  const p = useProgress();
  const unlocked = ACHIEVEMENTS.filter((a) => p.achievements[a.id]).length;
  return html`<div class="page">
    <div class="page-header">
      <div>
        <div class="eyebrow">Achievements</div>
        <h1>Badges en prestaties</h1>
        <p>Prestaties belonen echt leergedrag: zelfstandig oplossen, fouten debuggen, voorspellen, herhalen en doorzetten.</p>
      </div>
      <${Badge} tone="xp" icon="trophy">${unlocked}/${ACHIEVEMENTS.length} behaald</${Badge}>
    </div>
    ${TIERS.map((tier) => {
      const list = ACHIEVEMENTS.filter((a) => a.tier === tier);
      if (!list.length) return null;
      return html`<section class="ach-section">
        <h2 class="ach-tier">${tier[0].toUpperCase() + tier.slice(1)}</h2>
        <div class="ach-grid">${list.map((a) => {
          const got = p.achievements[a.id];
          return html`<div class=${`ach-card ${got ? 'unlocked' : ''}`}>
            <${AchievementBadge} achievement=${a} unlocked=${!!got} />
            <div>
              <div class="ach-title">${a.title}</div>
              <div class="ach-desc">${a.description}</div>
              ${got ? html`<div class="faint xs">Behaald op ${new Date(got.unlockedAt).toLocaleDateString('nl-BE')}</div>` : null}
            </div>
          </div>`;
        })}</div>
      </section>`;
    })}
  </div>`;
}
