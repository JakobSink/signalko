/* ============================================================
   Signalko — i18n (Internationalisation)
   Supported: sl (Slovenian, default), en (English)
   ============================================================ */

const SK_I18N = {
  sl: {
    // Navigation
    'nav.overview':        'Pregled',
    'nav.loans':           'Izposoja',
    'nav.assets':          'Sredstva',
    'nav.tags':            'RFID Tagi',
    'nav.presence':        'Prisotnost',
    'nav.presence_admin':  'Prisotnost — nadzor',
    'nav.users':           'Uporabniki',
    'nav.roles':           'Vloge',
    'nav.license':         'Licenca',
    'nav.readers':         'Čitalci',
    'nav.antennas':        'Antene',
    'nav.zones':           'Cone',
    'nav.profile':         'Profil',
    'nav.logout':          'Odjava',
    // Sections
    'section.user':        'Uporabnik',
    'section.admin':       'Admin',
    'section.config':      'Konfiguracija',
    // Login
    'login.tab.login':     'Prijava',
    'login.tab.register':  'Registracija',
    'login.email':         'Email',
    'login.password':      'Geslo',
    'login.confirm_pass':  'Ponovi geslo',
    'login.name':          'Ime',
    'login.surname':       'Priimek',
    'login.license_key':   'Licenčni ključ',
    'login.submit':        'Prijavi se',
    'login.register_btn':  'Registriraj se',
    // Pages — titles
    'page.assets':         'Sredstva',
    'page.assets.sub':     'Pregled in upravljanje inventarja',
    'page.loans':          'Izposoja',
    'page.loans.sub':      'Evidenca izposoj in vrnitev',
    'page.tags':           'RFID Tagi',
    'page.tags.sub':       'Pregled RFID tagov in dodelitev',
    'page.presence':       'Prisotnost',
    'page.presence.sub':   'Vaša evidenca prisotnosti',
    'page.presenceadmin':  'Prisotnost — nadzor',
    'page.presenceadmin.sub': 'Admin pregled prisotnosti',
    'page.users':          'Uporabniki',
    'page.users.sub':      'Upravljanje uporabnikov',
    'page.roles':          'Vloge in pravice',
    'page.roles.sub':      'Upravljanje uporabniških vlog in njihovih dovoljenj',
    'page.license':        'Licenca',
    'page.license.sub':    'Licenčni ključ in omejitve',
    'page.readers':        'Čitalci',
    'page.readers.sub':    'Konfiguracija RFID čitalcev',
    'page.antennas':       'Antene',
    'page.antennas.sub':   'Konfiguracija anten',
    'page.zones':          'Cone',
    'page.zones.sub':      'Upravljanje con',
    'page.profile':        'Moj profil',
    'page.profile.sub':    'Osebni podatki in nastavitve',
    // Common buttons
    'btn.save':            'Shrani',
    'btn.cancel':          'Prekliči',
    'btn.delete':          'Izbriši',
    'btn.add':             'Dodaj',
    'btn.edit':            'Uredi',
    'btn.close':           'Zapri',
    'btn.search':          'Iskanje',
    'btn.export':          'Izvozi',
    'btn.import':          'Uvozi',
    'btn.refresh':         'Osveži',
    // Common labels
    'lbl.name':            'Ime',
    'lbl.surname':         'Priimek',
    'lbl.email':           'Email',
    'lbl.role':            'Vloga',
    'lbl.status':          'Status',
    'lbl.actions':         'Akcije',
    'lbl.date':            'Datum',
    'lbl.yes':             'Da',
    'lbl.no':              'Ne',
    'lbl.active':          'Aktiven',
    'lbl.inactive':        'Neaktiven',
    'lbl.loading':         'Nalaganje…',
    'lbl.no_data':         'Ni podatkov.',
    'lbl.language':        'Jezik',
    // Profile
    'profile.language':    'Jezik vmesnika',
    'profile.lang.sl':     'Slovenščina',
    'profile.lang.en':     'English',
  },

  en: {
    // Navigation
    'nav.overview':        'Overview',
    'nav.loans':           'Loans',
    'nav.assets':          'Assets',
    'nav.tags':            'RFID Tags',
    'nav.presence':        'Presence',
    'nav.presence_admin':  'Presence — Admin',
    'nav.users':           'Users',
    'nav.roles':           'Roles',
    'nav.license':         'License',
    'nav.readers':         'Readers',
    'nav.antennas':        'Antennas',
    'nav.zones':           'Zones',
    'nav.profile':         'Profile',
    'nav.logout':          'Logout',
    // Sections
    'section.user':        'User',
    'section.admin':       'Admin',
    'section.config':      'Configuration',
    // Login
    'login.tab.login':     'Sign In',
    'login.tab.register':  'Register',
    'login.email':         'Email',
    'login.password':      'Password',
    'login.confirm_pass':  'Confirm password',
    'login.name':          'First name',
    'login.surname':       'Last name',
    'login.license_key':   'License key',
    'login.submit':        'Sign in',
    'login.register_btn':  'Register',
    // Pages — titles
    'page.assets':         'Assets',
    'page.assets.sub':     'Inventory overview and management',
    'page.loans':          'Loans',
    'page.loans.sub':      'Loan and return records',
    'page.tags':           'RFID Tags',
    'page.tags.sub':       'RFID tag overview and assignment',
    'page.presence':       'Presence',
    'page.presence.sub':   'Your presence records',
    'page.presenceadmin':  'Presence — Admin',
    'page.presenceadmin.sub': 'Admin presence overview',
    'page.users':          'Users',
    'page.users.sub':      'User management',
    'page.roles':          'Roles & Permissions',
    'page.roles.sub':      'Manage user roles and their permissions',
    'page.license':        'License',
    'page.license.sub':    'License key and limits',
    'page.readers':        'Readers',
    'page.readers.sub':    'RFID reader configuration',
    'page.antennas':       'Antennas',
    'page.antennas.sub':   'Antenna configuration',
    'page.zones':          'Zones',
    'page.zones.sub':      'Zone management',
    'page.profile':        'My Profile',
    'page.profile.sub':    'Personal details and settings',
    // Common buttons
    'btn.save':            'Save',
    'btn.cancel':          'Cancel',
    'btn.delete':          'Delete',
    'btn.add':             'Add',
    'btn.edit':            'Edit',
    'btn.close':           'Close',
    'btn.search':          'Search',
    'btn.export':          'Export',
    'btn.import':          'Import',
    'btn.refresh':         'Refresh',
    // Common labels
    'lbl.name':            'Name',
    'lbl.surname':         'Surname',
    'lbl.email':           'Email',
    'lbl.role':            'Role',
    'lbl.status':          'Status',
    'lbl.actions':         'Actions',
    'lbl.date':            'Date',
    'lbl.yes':             'Yes',
    'lbl.no':              'No',
    'lbl.active':          'Active',
    'lbl.inactive':        'Inactive',
    'lbl.loading':         'Loading…',
    'lbl.no_data':         'No data.',
    'lbl.language':        'Language',
    // Profile
    'profile.language':    'Interface language',
    'profile.lang.sl':     'Slovenian',
    'profile.lang.en':     'English',
  }
};

/* Get current language from localStorage */
function skGetLang() {
  try {
    const u = JSON.parse(localStorage.getItem('sk_user') || 'null');
    const lang = u?.language || u?.Language || 'sl';
    return SK_I18N[lang] ? lang : 'sl';
  } catch { return 'sl'; }
}

/* Translate a key */
function skT(key) {
  const lang = skGetLang();
  return SK_I18N[lang]?.[key] ?? SK_I18N['sl']?.[key] ?? key;
}

/* Apply data-i18n translations to the document */
function skApplyI18n() {
  document.querySelectorAll('[data-i18n]').forEach(el => {
    const key = el.getAttribute('data-i18n');
    const val = skT(key);
    if (val) el.textContent = val;
  });
  document.querySelectorAll('[data-i18n-placeholder]').forEach(el => {
    const key = el.getAttribute('data-i18n-placeholder');
    const val = skT(key);
    if (val) el.placeholder = val;
  });
  document.querySelectorAll('[data-i18n-title]').forEach(el => {
    const key = el.getAttribute('data-i18n-title');
    const val = skT(key);
    if (val) el.title = val;
  });
}

document.addEventListener('DOMContentLoaded', skApplyI18n);
