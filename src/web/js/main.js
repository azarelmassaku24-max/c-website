import { html, render } from './lib/preact.js';
import { App } from './app/App.js';
import { store } from './progress/store.js';
import { applyTheme } from './progress/settings.js';
import { curriculumService } from './services/curriculumService.js';
import { engine } from './progress/engine.js';

async function boot() {
  applyTheme();
  const root = document.getElementById('app');
  try {
    await Promise.all([curriculumService.load(), store.load()]);
    engine.refreshAchievements();
    render(html`<${App} />`, root);
  } catch (e) {
    console.error(e);
    root.innerHTML = `<div class="boot"><div class="boot-logo"><img src="/img/logo.svg" width="44" height="44" alt=""></div>
      <h2>C# Quest kon niet starten</h2><p class="muted">${String(e.message || e).replace(/</g, '&lt;')}</p>
      <p class="muted small">Start de server met <code>dotnet run</code> in de map <code>src/server</code> en herlaad deze pagina.</p>
      <button class="btn btn-primary" onclick="location.reload()">Opnieuw proberen</button></div>`;
  }
}

matchMedia('(prefers-color-scheme: light)').addEventListener('change', applyTheme);
boot();
