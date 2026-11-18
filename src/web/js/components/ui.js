import { html, useEffect, useRef, useState } from '../lib/preact.js';
import { Icon } from './Icon.js';

export function Button({ variant, size, icon, iconRight, loading, children, class: cls = '', ...rest }) {
  const classes = ['btn', variant && `btn-${variant}`, size && `btn-${size}`, !children && icon && 'btn-icon', cls].filter(Boolean).join(' ');
  return html`<button type="button" class=${classes} disabled=${rest.disabled || loading} ...${rest}>
    ${loading ? html`<span class="spinner"></span>` : icon ? html`<${Icon} name=${icon} />` : null}
    ${children}
    ${iconRight ? html`<${Icon} name=${iconRight} />` : null}
  </button>`;
}

export function LinkButton({ href, variant, size, icon, iconRight, children, class: cls = '' }) {
  const classes = ['btn', variant && `btn-${variant}`, size && `btn-${size}`, cls].filter(Boolean).join(' ');
  return html`<a href=${href} class=${classes}>
    ${icon ? html`<${Icon} name=${icon} />` : null}${children}${iconRight ? html`<${Icon} name=${iconRight} />` : null}
  </a>`;
}

export function Card({ title, icon, sub, actions, children, class: cls = '', onClick, ...rest }) {
  return html`<section class=${`card ${onClick ? 'interactive' : ''} ${cls}`} onClick=${onClick} ...${rest}>
    ${title || actions ? html`<div class="card-header">
      <div>
        ${title ? html`<h3 class="card-title">${icon ? html`<${Icon} name=${icon} />` : null}${title}</h3>` : null}
        ${sub ? html`<p class="card-sub">${sub}</p>` : null}
      </div>
      ${actions ? html`<div class="row">${actions}</div>` : null}
    </div>` : null}
    ${children}
  </section>`;
}

export function Badge({ tone, icon, children, title }) {
  return html`<span class=${`badge ${tone ? 'badge-' + tone : ''}`} title=${title}>${icon ? html`<${Icon} name=${icon} />` : null}${children}</span>`;
}

export function Progress({ value, tone, size, label }) {
  const pct = Math.max(0, Math.min(100, value || 0));
  return html`<div class=${`progress ${tone || ''} ${size || ''}`} role="progressbar" aria-valuenow=${Math.round(pct)} aria-valuemin="0" aria-valuemax="100" aria-label=${label}>
    <span style=${{ width: pct + '%' }}></span>
  </div>`;
}

export function Ring({ value, size = 64, stroke = 6, color = 'var(--accent)', label, children }) {
  const r = (size - stroke) / 2;
  const c = 2 * Math.PI * r;
  const pct = Math.max(0, Math.min(100, value || 0));
  return html`<div class="ring" style=${{ width: size + 'px', height: size + 'px' }}>
    <svg width=${size} height=${size} aria-hidden="true">
      <circle cx=${size / 2} cy=${size / 2} r=${r} fill="none" stroke="var(--surface-3)" stroke-width=${stroke} />
      <circle cx=${size / 2} cy=${size / 2} r=${r} fill="none" stroke=${color} stroke-width=${stroke} stroke-linecap="round"
        stroke-dasharray=${c} stroke-dashoffset=${c - (pct / 100) * c} style="transition: stroke-dashoffset .6s var(--ease)" />
    </svg>
    <span class="ring-label">${children ?? label ?? Math.round(pct) + '%'}</span>
  </div>`;
}

export function Spinner({ label }) {
  return html`<div class="row muted small"><span class="spinner"></span>${label}</div>`;
}

export function Empty({ icon = 'sparkles', title, children }) {
  return html`<div class="empty"><${Icon} name=${icon} /><h3>${title}</h3>${children ? html`<div>${children}</div>` : null}</div>`;
}

export function Tabs({ tabs, active, onChange, class: cls = '' }) {
  return html`<div class=${`tabs ${cls}`} role="tablist">
    ${tabs.map((t) => html`<button type="button" role="tab" aria-selected=${active === t.id} class=${`tab ${active === t.id ? 'active' : ''}`} onClick=${() => onChange(t.id)}>
      ${t.icon ? html`<${Icon} name=${t.icon} />` : null}${t.label}
      ${t.count != null ? html`<span class=${`count ${t.tone || ''}`}>${t.count}</span>` : null}
    </button>`)}
  </div>`;
}

export function Segmented({ options, value, onChange }) {
  return html`<div class="segmented" role="radiogroup">
    ${options.map((o) => html`<button type="button" role="radio" aria-checked=${value === o.value} class=${value === o.value ? 'active' : ''} onClick=${() => onChange(o.value)}>${o.label}</button>`)}
  </div>`;
}

export function Switch({ checked, onChange, label }) {
  return html`<label class="switch" title=${label}>
    <input type="checkbox" checked=${checked} onChange=${(e) => onChange(e.currentTarget.checked)} aria-label=${label} />
    <span></span>
  </label>`;
}

export function Modal({ title, onClose, children, footer, wide }) {
  const ref = useRef(null);
  useEffect(() => {
    const onKey = (e) => { if (e.key === 'Escape') onClose?.(); };
    addEventListener('keydown', onKey);
    ref.current?.querySelector('button, [href], input, textarea, select')?.focus();
    return () => removeEventListener('keydown', onKey);
  }, []);
  return html`<div class="modal-backdrop" onMouseDown=${(e) => { if (e.target === e.currentTarget) onClose?.(); }}>
    <div class=${`modal ${wide ? 'wide' : ''}`} role="dialog" aria-modal="true" aria-label=${title} ref=${ref}>
      <div class="modal-header">
        <h2>${title}</h2>
        ${onClose ? html`<${Button} variant="ghost" icon="x" onClick=${onClose} aria-label="Sluiten" />` : null}
      </div>
      ${children}
      ${footer ? html`<div class="modal-footer">${footer}</div>` : null}
    </div>
  </div>`;
}

export function Callout({ tone = 'info', icon, title, children }) {
  const icons = { info: 'info', tip: 'lightbulb', warn: 'alert', danger: 'xCircle', success: 'checkCircle' };
  return html`<div class=${`callout ${tone}`}>
    <${Icon} name=${icon || icons[tone] || 'info'} />
    <div>${title ? html`<strong>${title}</strong> ` : null}${children}</div>
  </div>`;
}

export function Difficulty({ value }) {
  return html`<span class="difficulty" title=${`Moeilijkheid ${value}/5`} aria-label=${`Moeilijkheid ${value} van 5`}>
    ${[1, 2, 3, 4, 5].map((i) => html`<i class=${i <= value ? 'on' : ''}></i>`)}
  </span>`;
}

export function useAsync(fn, deps) {
  const [state, setState] = useState({ loading: true, data: null, error: null });
  useEffect(() => {
    let alive = true;
    setState((s) => ({ ...s, loading: true, error: null }));
    Promise.resolve().then(fn).then(
      (data) => alive && setState({ loading: false, data, error: null }),
      (error) => alive && setState({ loading: false, data: null, error }),
    );
    return () => { alive = false; };
  }, deps);
  return state;
}

export function ErrorBox({ error, retry }) {
  return html`<div class="callout danger"><${Icon} name="alert" />
    <div><strong>Er ging iets mis.</strong> ${error?.message || String(error)} ${retry ? html`<${Button} size="sm" onClick=${retry}>Opnieuw</${Button}>` : null}</div>
  </div>`;
}
