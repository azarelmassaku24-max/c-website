import { codeService } from '../../services/codeService.js';
import { settings } from '../../progress/settings.js';

/**
 * Laadt Monaco Editor (lokaal meegeleverd) en registreert C#-ondersteuning:
 * thema's, Roslyn-completion, hover en snippets. Monaco wordt maar één keer geladen.
 */
let loading = null;

/** model-uri -> functie die de huidige projectbestanden teruggeeft (voor IntelliSense over meerdere bestanden). */
export const modelOwners = new Map();

export function loadMonaco() {
  if (loading) return loading;
  loading = new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = '/vendor/monaco/vs/loader.js';
    script.onload = () => {
      window.require.config({ paths: { vs: '/vendor/monaco/vs' } });
      window.require(['vs/editor/editor.main'], () => {
        const monaco = window.monaco;
        defineThemes(monaco);
        monaco.editor.setTheme(monacoTheme());
        registerCSharp(monaco);
        // Thema volgen wanneer de gebruiker van donker naar licht schakelt (ook voor ingekleurde codeblokken).
        new MutationObserver(() => monaco.editor.setTheme(monacoTheme()))
          .observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
        resolve(monaco);
      }, reject);
    };
    script.onerror = () => reject(new Error('Monaco Editor kon niet geladen worden.'));
    document.head.appendChild(script);
  });
  return loading;
}

export function monacoTheme() {
  const t = document.documentElement.dataset.theme;
  return t === 'light' ? 'quest-light' : 'quest-dark';
}

function defineThemes(monaco) {
  monaco.editor.defineTheme('quest-dark', {
    base: 'vs-dark',
    inherit: true,
    rules: [
      { token: 'comment', foreground: '6b7489', fontStyle: 'italic' },
      { token: 'keyword', foreground: 'c4a7ff' },
      { token: 'string', foreground: '8fe3b6' },
      { token: 'number', foreground: 'ffcb6b' },
      { token: 'type', foreground: '7dcfff' },
      { token: 'identifier', foreground: 'e8ebf3' },
      { token: 'delimiter', foreground: '9aa3b8' },
    ],
    colors: {
      'editor.background': '#0d1016',
      'editor.foreground': '#e8ebf3',
      'editorLineNumber.foreground': '#454d61',
      'editorLineNumber.activeForeground': '#9aa3b8',
      'editor.lineHighlightBackground': '#151a24',
      'editor.selectionBackground': '#3a3166',
      'editorCursor.foreground': '#b8a6ff',
      'editorIndentGuide.background1': '#1c2130',
      'editorGutter.background': '#0d1016',
      'editorWidget.background': '#151922',
      'editorSuggestWidget.background': '#151922',
      'editorSuggestWidget.selectedBackground': '#262b3b',
      'editorHoverWidget.background': '#151922',
      'scrollbarSlider.background': '#ffffff14',
    },
  });
  monaco.editor.defineTheme('quest-light', {
    base: 'vs',
    inherit: true,
    rules: [
      { token: 'comment', foreground: '858ca0', fontStyle: 'italic' },
      { token: 'keyword', foreground: '6d4cff' },
      { token: 'string', foreground: '0a7d4f' },
      { token: 'number', foreground: 'b45309' },
      { token: 'type', foreground: '0b6bcb' },
    ],
    colors: {
      'editor.background': '#fbfbfd',
      'editor.lineHighlightBackground': '#f1f2f7',
      'editorLineNumber.foreground': '#b5bac8',
      'editorGutter.background': '#fbfbfd',
    },
  });
}

const KIND_MAP = (monaco) => ({
  Class: monaco.languages.CompletionItemKind.Class,
  Structure: monaco.languages.CompletionItemKind.Struct,
  Interface: monaco.languages.CompletionItemKind.Interface,
  Enum: monaco.languages.CompletionItemKind.Enum,
  EnumMember: monaco.languages.CompletionItemKind.EnumMember,
  Method: monaco.languages.CompletionItemKind.Method,
  ExtensionMethod: monaco.languages.CompletionItemKind.Method,
  Property: monaco.languages.CompletionItemKind.Property,
  Field: monaco.languages.CompletionItemKind.Field,
  Local: monaco.languages.CompletionItemKind.Variable,
  Parameter: monaco.languages.CompletionItemKind.Variable,
  Keyword: monaco.languages.CompletionItemKind.Keyword,
  Namespace: monaco.languages.CompletionItemKind.Module,
  Constant: monaco.languages.CompletionItemKind.Constant,
  Delegate: monaco.languages.CompletionItemKind.Function,
  Event: monaco.languages.CompletionItemKind.Event,
  TypeParameter: monaco.languages.CompletionItemKind.TypeParameter,
});

const SNIPPETS = [
  ['cw', 'Console.WriteLine(…)', 'Console.WriteLine($1);'],
  ['crl', 'Console.ReadLine()', 'Console.ReadLine()'],
  ['main', 'static void Main', 'static void Main(string[] args) {\n\t$0\n}'],
  ['for', 'for-loop', 'for (int ${1:i} = 0; ${1:i} < ${2:lengte}; ${1:i}++) {\n\t$0\n}'],
  ['forr', 'for-loop achterwaarts', 'for (int ${1:i} = ${2:lengte} - 1; ${1:i} >= 0; ${1:i}--) {\n\t$0\n}'],
  ['foreach', 'foreach-loop', 'foreach (${1:var} ${2:element} in ${3:collectie}) {\n\t$0\n}'],
  ['while', 'while-loop', 'while (${1:voorwaarde}) {\n\t$0\n}'],
  ['do', 'do while-loop', 'do {\n\t$0\n} while (${1:voorwaarde});'],
  ['if', 'if', 'if (${1:voorwaarde}) {\n\t$0\n}'],
  ['ifelse', 'if … else', 'if (${1:voorwaarde}) {\n\t$2\n} else {\n\t$0\n}'],
  ['tryparse', 'int.TryParse invoer', 'int ${1:getal};\nwhile (!int.TryParse(Console.ReadLine(), out ${1:getal})) {\n\tConsole.Write("Ongeldige invoer, probeer opnieuw: ");\n}'],
  ['class', 'class', 'class ${1:Naam} {\n\t$0\n}'],
  ['prop', 'auto-implemented property', 'public ${1:string} ${2:Naam} { get; set; }'],
  ['ctor', 'constructor', 'public ${1:Klasse}() {\n\t$0\n}'],
  ['try', 'try … catch', 'try {\n\t$1\n} catch (${2:Exception} ex) {\n\t$0\n}'],
  ['using', 'using StreamReader', 'using (StreamReader reader = new StreamReader(${1:pad})) {\n\t$0\n}'],
  ['switch', 'switch', 'switch (${1:waarde}) {\n\tcase ${2:1}:\n\t\t$0\n\t\tbreak;\n\tdefault:\n\t\tbreak;\n}'],
];

function registerCSharp(monaco) {
  const kinds = KIND_MAP(monaco);
  monaco.languages.registerCompletionItemProvider('csharp', {
    triggerCharacters: ['.'],
    async provideCompletionItems(model, position, context, token) {
      const word = model.getWordUntilPosition(position);
      const range = new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn);
      const suggestions = [];
      const lineText = model.getLineContent(position.lineNumber).slice(0, position.column - 1);
      const afterDot = /\.\w*$/.test(lineText);
      if (!afterDot) {
        for (const [label, detail, insert] of SNIPPETS) {
          suggestions.push({
            label, detail: 'snippet · ' + detail, kind: monaco.languages.CompletionItemKind.Snippet,
            insertText: insert, insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet, range, sortText: '~' + label,
          });
        }
      }
      const owner = modelOwners.get(model.uri.toString());
      if (owner && settings.get().intellisense) {
        const controller = new AbortController();
        token.onCancellationRequested?.(() => controller.abort());
        try {
          const { files, path } = owner();
          const res = await codeService.complete(files, path, position.lineNumber, position.column, controller.signal);
          for (const item of res.items || []) {
            suggestions.push({
              label: item.label,
              kind: kinds[item.kind] ?? monaco.languages.CompletionItemKind.Text,
              detail: item.detail || item.kind,
              insertText: item.label.replace(/<>$/, ''),
              filterText: item.filterText,
              sortText: item.sortText,
              preselect: item.preselect,
              range,
            });
          }
        } catch { /* geen server of geannuleerd: alleen snippets */ }
      }
      return { suggestions };
    },
  });

  monaco.languages.registerHoverProvider('csharp', {
    async provideHover(model, position, token) {
      const owner = modelOwners.get(model.uri.toString());
      if (!owner || !settings.get().intellisense) return null;
      const word = model.getWordAtPosition(position);
      if (!word) return null;
      const controller = new AbortController();
      token.onCancellationRequested?.(() => controller.abort());
      try {
        const { files, path } = owner();
        const res = await codeService.hover(files, path, position.lineNumber, position.column, controller.signal);
        if (!res?.markdown) return null;
        return {
          range: new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn),
          contents: [{ value: res.markdown }],
        };
      } catch { return null; }
    },
  });
}

/** Kleurt code in (voor codeblokken in lessen) zonder een volledige editor. */
export async function colorize(code, language = 'csharp') {
  const monaco = await loadMonaco();
  return monaco.editor.colorize(code, language, { tabSize: 4 });
}
