import { html, useEffect, useState } from '../lib/preact.js';
import { Icon } from './Icon.js';
import { Button, Modal } from './ui.js';
import { events } from '../progress/events.js';
import { TIER_COLORS } from '../achievements/definitions.js';

let toastId = 0;
const toastListeners = new Set();

/** Toon een korte melding. tone: success | danger | xp | achievement | info */
export function toast(title, body, tone = 'info', icon) {
  const t = { id: ++toastId, title, body, tone, icon };
  toastListeners.forEach((l) => l(t));
}

export function confetti() {
  if (document.documentElement.dataset.motion === 'reduced') return;
  const root = document.createElement('div');
  root.className = 'confetti';
  const colors = ['#8b6cff', '#3fa9ff', '#34d399', '#fbbf24', '#fb7185'];
  for (let i = 0; i < 90; i++) {
    const p = document.createElement('i');
    p.style.left = Math.random() * 100 + 'vw';
    p.style.background = colors[i % colors.length];
    p.style.animationDelay = Math.random() * 0.5 + 's';
    p.style.animationDuration = 1.2 + Math.random() * 1.2 + 's';
    p.style.transform = `rotate(${Math.random() * 360}deg)`;
    root.appendChild(p);
  }
  document.body.appendChild(root);
  setTimeout(() => root.remove(), 3200);
}

export function floatXp(amount, x, y) {
  const el = document.createElement('div');
  el.className = 'xp-float';
  el.textContent = `+${amount} XP`;
  el.style.left = (x ?? innerWidth / 2) + 'px';
  el.style.top = (y ?? innerHeight / 2) + 'px';
  document.body.appendChild(el);
  setTimeout(() => el.remove(), 1300);
}

export function Notifications() {
  const [toasts, setToasts] = useState([]);
  const [celebration, setCelebration] = useState(null);

  useEffect(() => {
    const add = (t) => {
      setToasts((list) => [...list.slice(-4), t]);
      setTimeout(() => setToasts((list) => list.filter((x) => x.id !== t.id)), t.tone === 'achievement' ? 6000 : 3800);
    };
    toastListeners.add(add);
    const unsub = events.subscribe((e) => {
      switch (e.type) {
        case 'xp': toast(`+${e.amount} XP`, e.reason, 'xp', 'zap'); break;
        case 'achievement': toast(`Prestatie ontgrendeld: ${e.achievement.title}`, e.achievement.description, 'achievement', e.achievement.icon); break;
        case 'info': toast('Goed om te weten', e.message, 'info', 'info'); break;
        case 'levelUp': setCelebration({ kind: 'level', level: e.level }); confetti(); break;
        case 'lessonComplete': setCelebration((c) => c || { kind: 'lesson', lesson: e.lesson }); confetti(); break;
        case 'bossDefeated': setCelebration({ kind: 'boss', lesson: e.lesson }); confetti(); break;
      }
    });
    return () => { toastListeners.delete(add); unsub(); };
  }, []);

  return html`
    <div class="toasts" aria-live="polite">
      ${toasts.map((t) => html`<div class=${`toast ${t.tone}`} key=${t.id}>
        <${Icon} name=${t.icon || (t.tone === 'danger' ? 'alert' : t.tone === 'success' ? 'checkCircle' : 'info')} />
        <div><div class="toast-title">${t.title}</div>${t.body ? html`<div class="toast-body">${t.body}</div>` : null}</div>
      </div>`)}
    </div>
    ${celebration ? html`<${Celebration} data=${celebration} onClose=${() => setCelebration(null)} />` : null}
  `;
}

function Celebration({ data, onClose }) {
  const title = data.kind === 'level' ? `Level ${data.level} bereikt!` : data.kind === 'boss' ? 'Boss verslagen!' : 'Les voltooid!';
  const text = data.kind === 'level'
    ? 'Je ervaring groeit. Blijf oefenen om nieuwe rangen te bereiken.'
    : data.kind === 'boss'
      ? `Je versloeg “${data.lesson.title}”. Het volgende level is ontgrendeld.`
      : `Je beheerst “${data.lesson.title}”. De volgende les is ontgrendeld.`;
  return html`<${Modal} title=${title} onClose=${onClose} footer=${html`<${Button} variant="primary" onClick=${onClose}>Verder</${Button}>`}>
    <div class="celebrate">
      <div class="celebrate-icon"><${Icon} name=${data.kind === 'level' ? 'zap' : data.kind === 'boss' ? 'crown' : 'trophy'} size="40" /></div>
      <p>${text}</p>
    </div>
  </${Modal}>`;
}

export function AchievementBadge({ achievement, unlocked, size = 56 }) {
  const color = TIER_COLORS[achievement.tier] || 'var(--accent)';
  return html`<div class=${`achievement-badge ${unlocked ? 'unlocked' : ''}`} style=${{ '--tier': color, width: size + 'px', height: size + 'px' }}>
    <${Icon} name=${unlocked ? achievement.icon : 'lock'} size=${Math.round(size * 0.42)} />
  </div>`;
}
