// English and German. The language choice is shared with the other NaguSuite tools through the cookie naguya_lang.
window.I18N = (() => {
  const de = {
    theme: 'Hell / dunkel',
    title: 'Deine Worte, so getippt, wie du sie gemeint hast.',
    lead: 'TypeMend repariert Tippfehler und sonst nichts. Bedeutung, Slang und Ton bleiben, und was du gesagt hast, wird nie geglättet.',
    langNote: 'Gemacht für Englisch. Bei anderen Sprachen hilft es auch, aber mit Grenzen: Deutsche Schimpfwörter und Wörter mit ä, ö, ü oder ß bleiben, wie du sie getippt hast, andere Wörter werden aber eventuell zu englischen "repariert". Bitte prüfe das Ergebnis.',
    typedLabel: 'Was du getippt hast',
    repair: 'Reparieren',
    keepTitle: 'Wörter, die sich nie ändern sollen',
    keepHint: 'Namen und Spielbegriffe, eins pro Zeile. Sie bleiben genau so, wie du sie getippt hast, wenn sie so geschrieben sind. Wird nur in diesem Browser gespeichert.',
    loading: 'Das Reparatur-Modell wird vorbereitet...',
    readyHint: 'Enter repariert, Umschalt+Enter macht eine neue Zeile.',
    working: 'Wird repariert...',
    failed: 'Das Modell konnte nicht geladen werden. Bitte lade die Seite neu.',
    repairFailed: 'Das hat nicht geklappt. Dein Text ist unverändert.',
    youTyped: 'Du hast getippt',
    repaired: 'Repariert (du kannst das ändern)',
    guesses: 'Markierte Wörter sind Vermutungen. Klicke eins an, um es zu korrigieren.',
    fixHint: 'Klicken zum Korrigieren',
    fixSuggestions: 'Vorschläge',
    fixLoading: 'Suche...',
    fixNone: 'Keine anderen Vorschläge',
    fixTyped: 'So, wie du es getippt hast',
    fixKeep: 'Behalten:',
    copy: 'Reparatur kopieren',
    copyBoth: 'Reparatur und Original kopieren',
    copied: 'Kopiert.',
    copyFailed: 'Kopieren ging nicht. Bitte markiere den Text von Hand.',
    typedPrefix: 'getippt',
    foot1: 'Läuft auf deinem eigenen Gerät. Nichts wird hochgeladen.',
    foot2: 'Keine Konten, kein Tracking.',
    foot3: 'Lizenzen und Danksagungen',
    foot4: 'NaguSuite: mehr kostenlose Tools',
  };
  const en = {
    readyHint: 'Enter repairs, Shift+Enter starts a new line.',
    working: 'Repairing...',
    failed: 'The model could not load. Please reload the page.',
    repairFailed: 'That did not work. Your text is unchanged.',
    copied: 'Copied.',
    copyFailed: 'Copying did not work. Please select the text by hand.',
    typedPrefix: 'typed',
    fixHint: 'Click to fix',
    fixSuggestions: 'Suggestions',
    fixLoading: 'Looking...',
    fixNone: 'No other suggestions',
    fixTyped: 'As you typed it',
    fixKeep: 'Keep',
  };
  const read = () => {
    const m = document.cookie.match(/(?:^|;\s*)naguya_lang=(en|de)\b/);
    if (m) return m[1];
    try { const s = localStorage.getItem('naguya_lang'); if (s === 'en' || s === 'de') return s; } catch (_) {}
    return (navigator.language || '').toLowerCase().startsWith('de') ? 'de' : 'en';
  };
  let lang = read();
  const originals = new Map();
  const t = (key) => (lang === 'de' && de[key]) || en[key] || originals.get(key) || key;
  const apply = () => {
    document.documentElement.lang = lang;
    document.querySelectorAll('[data-i18n]').forEach((el) => {
      const key = el.dataset.i18n;
      if (!originals.has(key)) originals.set(key, el.textContent);
      el.textContent = t(key);
    });
    document.getElementById('lang').textContent = lang === 'de' ? 'English' : 'Deutsch';
  };
  const toggle = () => {
    lang = lang === 'de' ? 'en' : 'de';
    try { localStorage.setItem('naguya_lang', lang); } catch (_) {}
    const domain = location.hostname.endsWith('naguya.tech') ? '; Domain=naguya.tech' : '';
    document.cookie = `naguya_lang=${lang}; Path=/; Max-Age=31536000; SameSite=Lax${domain}`;
    apply();
  };
  apply();
  return { t, toggle, apply };
})();
