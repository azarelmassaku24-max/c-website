import { html, useState } from '../lib/preact.js';
import { Icon } from '../components/Icon.js';
import { Button, Card, Segmented, Switch, Modal, useAsync, Callout } from '../components/ui.js';
import { toast } from '../components/Notifications.js';
import { useProgress, useSettings } from '../hooks/useProgress.js';
import { settings } from '../progress/settings.js';
import { store } from '../progress/store.js';
import { engine } from '../progress/engine.js';
import { curriculumService } from '../services/curriculumService.js';

export function SettingsPage() {
  const s = useSettings();
  const p = useProgress();
  const [name, setName] = useState(p.profile.name);
  const [confirmReset, setConfirmReset] = useState(false);
  const system = useAsync(() => curriculumService.system(), []);
  const status = store.status();

  function exportProgress() {
    const blob = new Blob([JSON.stringify(p, null, 2)], { type: 'application/json' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `csharp-quest-voortgang-${new Date().toISOString().slice(0, 10)}.json`;
    a.click();
    URL.revokeObjectURL(a.href);
  }

  function importProgress(e) {
    const file = e.currentTarget.files?.[0];
    if (!file) return;
    file.text().then((text) => {
      try {
        const data = JSON.parse(text);
        if (typeof data !== 'object' || data.version == null) throw new Error('Geen geldig voortgangsbestand.');
        store.replace(data);
        engine.refreshAchievements();
        toast('Voortgang geïmporteerd', 'Je voortgang werd hersteld.', 'success', 'check');
      } catch (err) { toast('Import mislukt', err.message, 'danger', 'alert'); }
    });
  }

  const row = (label, help, control) => html`<div class="setting-row"><div><div class="setting-label">${label}</div>${help ? html`<div class="setting-help">${help}</div>` : null}</div><div>${control}</div></div>`;

  return html`<div class="page">
    <div class="page-header"><div><div class="eyebrow">Settings</div><h1>Instellingen</h1><p>Pas de leeromgeving aan en beheer je voortgang.</p></div></div>
    <div class="grid grid-2">
      <${Card} title="Profiel" icon="users">
        <div class="field">
          <label for="name">Naam</label>
          <div class="row"><input id="name" class="input" value=${name} maxLength="40" onInput=${(e) => setName(e.currentTarget.value)} />
          <${Button} onClick=${() => { store.update((x) => { x.profile.name = name.trim() || 'Leerling'; }); toast('Opgeslagen', 'Je naam werd aangepast.', 'success', 'check'); }}>Opslaan</${Button}></div>
        </div>
      </${Card}>
      <${Card} title="Weergave" icon="sun">
        ${row('Thema', null, html`<${Segmented} value=${s.theme} onChange=${(v) => settings.set({ theme: v })} options=${[{ value: 'dark', label: 'Donker' }, { value: 'light', label: 'Licht' }, { value: 'system', label: 'Systeem' }]} />`)}
        ${row('Minder animaties', 'Schakelt overgangen en confetti uit.', html`<${Switch} checked=${s.reducedMotion} onChange=${(v) => settings.set({ reducedMotion: v })} label="Minder animaties" />`)}
      </${Card}>
      <${Card} title="Code-editor" icon="code">
        ${row('Lettergrootte', `${s.editorFontSize}px`, html`<div class="row"><${Button} size="sm" icon="minus" onClick=${() => settings.set({ editorFontSize: Math.max(11, s.editorFontSize - 1) })} aria-label="Kleiner" /><${Button} size="sm" icon="plus" onClick=${() => settings.set({ editorFontSize: Math.min(22, s.editorFontSize + 1) })} aria-label="Groter" /></div>`)}
        ${row('Minimap', 'Overzicht van je code rechts in de editor.', html`<${Switch} checked=${s.minimap} onChange=${(v) => settings.set({ minimap: v })} label="Minimap" />`)}
        ${row('Live foutcontrole', 'Toon compileerfouten terwijl je typt.', html`<${Switch} checked=${s.liveDiagnostics} onChange=${(v) => settings.set({ liveDiagnostics: v })} label="Live foutcontrole" />`)}
        ${row('IntelliSense', 'Suggesties en uitleg bij het typen (via de echte C#-compiler).', html`<${Switch} checked=${s.intellisense} onChange=${(v) => settings.set({ intellisense: v })} label="IntelliSense" />`)}
        ${row('Getalnotatie', 'Bepaalt hoe kommagetallen getoond en ingelezen worden (de cursus gebruikt de Belgische notatie: 3,5).', html`<${Segmented} value=${s.culture} onChange=${(v) => settings.set({ culture: v })} options=${[{ value: 'nl-BE', label: '3,5 (nl-BE)' }, { value: 'en-US', label: '3.5 (en-US)' }]} />`)}
      </${Card}>
      <${Card} title="Leertraject" icon="map">
        ${row('Vrije modus', 'Ontgrendel alle levels en lessen (bv. als je een deel van de cursus al kent). XP verdien je nog steeds alleen door opdrachten op te lossen.', html`<${Switch} checked=${s.freeMode} onChange=${(v) => settings.set({ freeMode: v })} label="Vrije modus" />`)}
      </${Card}>
      <${Card} title="Voortgang" icon="database" sub=${status.serverAvailable ? 'Opgeslagen op de server (data/progress) en in deze browser.' : 'Server niet bereikbaar: voortgang wordt tijdelijk enkel in deze browser bewaard.'}>
        <div class="row wrap">
          <${Button} icon="download" onClick=${exportProgress}>Exporteren</${Button}>
          <label class="btn"><${Icon} name="upload" />Importeren<input type="file" accept="application/json" style="display:none" onChange=${importProgress} /></label>
          <${Button} variant="danger" icon="trash" onClick=${() => setConfirmReset(true)}>Voortgang wissen</${Button}>
        </div>
      </${Card}>
      <${Card} title="Uitvoeromgeving" icon="shield">
        ${system.data ? html`<div class="small stack-sm">
          <div><strong>.NET runtime:</strong> ${system.data.runtime} · <strong>Roslyn:</strong> ${system.data.compiler}</div>
          <div><strong>Besturingssysteem:</strong> ${system.data.os}</div>
          <div><strong>Isolatie:</strong> ${system.data.isolation}</div>
        </div>` : system.loading ? html`<span class="muted small">Laden…</span>` : html`<${Callout} tone="danger">Server niet bereikbaar.</${Callout}>`}
      </${Card}>
    </div>
    ${confirmReset ? html`<${Modal} title="Voortgang wissen?" onClose=${() => setConfirmReset(false)} footer=${html`
      <${Button} onClick=${() => setConfirmReset(false)}>Annuleren</${Button}>
      <${Button} variant="danger" onClick=${async () => { await store.reset(); setConfirmReset(false); toast('Voortgang gewist', 'Je begint opnieuw.', 'info', 'rotate'); }}>Ja, alles wissen</${Button}>`}>
      <p>Al je XP, voltooide lessen, opgeslagen code en badges worden gewist. Tip: exporteer eerst je voortgang als back-up. (Op de server blijft een kopie bewaard als <code>.reset-…</code>-bestand.)</p>
    </${Modal}>` : null}
  </div>`;
}
