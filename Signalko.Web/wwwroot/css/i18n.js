/* ============================================================
   Signalko — i18n
   Language files: /lang/{code}.json
   Add a new language: create /lang/xx.json + add option to profile.html
   ============================================================ */

let _skLang = {};

function skGetLang() {
  try {
    const u = JSON.parse(localStorage.getItem('sk_user') || 'null');
    return u?.language || u?.Language || 'sl';
  } catch { return 'sl'; }
}

/* Resolves when the language JSON is loaded */
const SK_I18N_READY = (async () => {
  const code = skGetLang();
  try {
    const res = await fetch(`/lang/${code}.json?v=1.0.3`);
    if (!res.ok) throw new Error();
    _skLang = await res.json();
  } catch {
    // fallback: try Slovenian
    try {
      const res = await fetch('/lang/sl.json?v=1.0.3');
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
