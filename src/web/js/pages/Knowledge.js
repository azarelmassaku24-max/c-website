import { html, useMemo, useState } from '../lib/preact.js';
import { Icon } from '../components/Icon.js';
import { Badge, ErrorBox, Spinner, useAsync, Empty } from '../components/ui.js';
import { Markdown } from '../components/Markdown.js';
import { curriculumService } from '../services/curriculumService.js';

/** Kennisbank: begrippen, syntax, referentievoorbeelden en alle foutmeldingen met uitleg. */
export function Knowledge({ query }) {
  const { loading, data, error } = useAsync(() => curriculumService.knowledge(), []);
  const [q, setQ] = useState(query || '');
  const [category, setCategory] = useState('');
  const [openId, setOpenId] = useState(null);

  const entries = useMemo(() => {
    if (!data) return [];
    const errorEntries = data.errors.map((e) => ({
      id: 'err-' + e.code, title: `${e.code} — ${e.message}`, category: 'Foutmeldingen', tags: [e.code],
      body: `**Wat betekent dit?** ${e.explanation}\n\n**Hoe los je het op?** ${e.tip}`,
    }));
    return [...data.entries, ...errorEntries];
  }, [data]);

  const categories = useMemo(() => [...new Set(entries.map((e) => e.category))], [entries]);
  const needle = q.trim().toLowerCase();
  const filtered = entries.filter((e) => (!category || e.category === category)
    && (!needle || e.title.toLowerCase().includes(needle) || e.body.toLowerCase().includes(needle) || e.tags?.some((t) => t.toLowerCase().includes(needle))));

  return html`<div class="page">
    <div class="page-header">
      <div>
        <div class="eyebrow">Knowledge Base</div>
        <h1>Begrippen, syntax en voorbeelden</h1>
        <p>Een naslagwerk bij de cursus. Elk codevoorbeeld kan je meteen uitvoeren of stap voor stap volgen.</p>
      </div>
    </div>
    <div class="kb-toolbar">
      <div class="search">
        <${Icon} name="search" size="16" />
        <input class="input" value=${q} onInput=${(e) => setQ(e.currentTarget.value)} placeholder="Zoek op begrip, syntax of foutcode (bv. foreach, TryParse, CS0103)…" aria-label="Zoeken" />
      </div>
      <div class="chip-row">
        <button class=${`chip ${category === '' ? 'active' : ''}`} onClick=${() => setCategory('')}>Alles</button>
        ${categories.map((cat) => html`<button class=${`chip ${category === cat ? 'active' : ''}`} onClick=${() => setCategory(cat)}>${cat}</button>`)}
      </div>
    </div>
    ${loading ? html`<${Spinner} label="Laden…" />` : error ? html`<${ErrorBox} error=${error} />` : filtered.length === 0 ? html`<${Empty} icon="search" title="Niets gevonden">Probeer een andere zoekterm.</${Empty}>` : html`
      <div class="kb-list">${filtered.map((e) => html`<article class=${`kb-item ${openId === e.id || needle ? 'open' : ''}`}>
        <button class="kb-head" onClick=${() => setOpenId(openId === e.id ? null : e.id)}>
          <${Icon} name=${e.category === 'Foutmeldingen' ? 'bug' : e.category === 'Syntax' ? 'code' : 'book'} size="16" />
          <span class="grow">${e.title}</span>
          <${Badge}>${e.category}</${Badge}>
        </button>
        ${openId === e.id || (needle && filtered.length <= 8) ? html`<div class="kb-body"><${Markdown} text=${e.body} /></div>` : null}
      </article>`)}</div>`}
  </div>`;
}
