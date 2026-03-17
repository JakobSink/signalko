/* ============================================================
   Signalko — i18n
   Language files: /lang/{code}.json
   Add a new language: create /lang/xx.json + add option to profile.html
   ============================================================ */

let _skLang = {};

const SK_SUPPORTED_LANGS = ['sl', 'en', 'de', 'sr'];

function skGetLang() {
  try {
    const u = JSON.parse(localStorage.getItem('sk_user') || 'null');
    const lang = u?.language || u?.Language || 'en';
    return SK_SUPPORTED_LANGS.includes(lang) ? lang : 'en';
  } catch { return 'en'; }
}

/* Resolves when the language JSON is loaded */
const SK_I18N_READY = (async () => {
  const code = skGetLang();
  try {
    const res = await fetch(`/lang/${code}.json?v=1.0.5`);
    if (!res.ok) throw new Error();
    _skLang = await res.json();
  } catch {
    // fallback: try English
    try {
      const res = await fetch('/lang/en.json?v=1.0.5');
      _skLang = await res.json();
    } catch { _skLang = {}; }
  }
})();

/* Translate a key — sync, use after awaiting SK_I18N_READY */
function skT(key) {
  return _skLang[key] ?? key;
}

/* Apply all data-i18n* attributes — awaits loading first */
async function skApplyI18n() {
  await SK_I18N_READY;
  document.querySelectorAll('[data-i18n]').forEach(el => {
    const val = skT(el.getAttribute('data-i18n'));
    if (val) el.textContent = val;
  });
  document.querySelectorAll('[data-i18n-placeholder]').forEach(el => {
    const val = skT(el.getAttribute('data-i18n-placeholder'));
    if (val) el.placeholder = val;
  });
  document.querySelectorAll('[data-i18n-title]').forEach(el => {
    const val = skT(el.getAttribute('data-i18n-title'));
    if (val) el.title = val;
  });
}

document.addEventListener('DOMContentLoaded', skApplyI18n);
