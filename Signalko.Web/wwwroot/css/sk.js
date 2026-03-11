/* ============================================================
   Signalko — Shared JS Utilities
   Include this on every page before page-specific scripts.
   ============================================================ */

/* ── Auth helpers ────────────────────────────────────────── */
function skIsLoggedIn() {
  return !!(localStorage.getItem('token') || localStorage.getItem('sk_token'));
}

function skCurrentUser() {
  try { return JSON.parse(localStorage.getItem('sk_user') || 'null'); } catch { return null; }
}

function skIsAdmin() {
  const u = skCurrentUser();
  if (!u) return false;
  // Check cached role name/id (refreshed on every page load via sidebar)
  if (u.role === 'Admin' || u.Role === 'Admin') return true;
  if (u.roleName === 'Admin' || u.RoleName === 'Admin') return true;
  if (u.roleId == 1 || u.RoleId == 1) return true;
  return false;
}

/* ── Permission helpers ──────────────────────────────────── */
function skGetPerms() {
  try { return JSON.parse(localStorage.getItem('sk_user_perms') || 'null'); } catch { return null; }
}

// Check if current user has a specific permission.
// Admins always return true (they have all permissions).
function skHasPerm(code) {
  if (skIsAdmin()) return true;
  const perms = skGetPerms();
  if (!Array.isArray(perms)) return false;
  return perms.includes(code);
}

function skHasAnyPerm(...codes) {
  return codes.some(c => skHasPerm(c));
}

// Fetch fresh permissions from server and update localStorage cache.
// Called automatically by sidebar; call manually if needed before rendering.
async function skRefreshPerms() {
  const token = localStorage.getItem('token') || localStorage.getItem('sk_token');
  if (!token) { localStorage.removeItem('sk_user_perms'); return []; }
  try {
    const res = await fetch('/api/Role/my-permissions', {
      headers: { 'Authorization': `Bearer ${token}`, 'Accept': 'application/json' }
    });
    if (!res.ok) return skGetPerms() || [];
    const perms = await res.json();
    if (Array.isArray(perms)) localStorage.setItem('sk_user_perms', JSON.stringify(perms));
    return Array.isArray(perms) ? perms : [];
  } catch { return skGetPerms() || []; }
}

/* ── Auth guard (optional — call on pages that need login) ── */
function skRequireLogin() {
  if (!skIsLoggedIn()) location.href = '/login.html';
}
function skRequireAdmin() {
  if (!skIsAdmin()) location.href = '/index.html';
}

// Require a specific permission — redirect if missing
function skRequirePerm(code) {
  skRefreshPerms().then(perms => {
    const isAdmin = skIsAdmin();
    if (!isAdmin && !perms.includes(code)) location.href = '/index.html';
  });
}

/* ── HTTP ───────────────────────────────────────────────────── */
async function skApi(method, path, body) {
  try {
    const token = localStorage.getItem('token') || localStorage.getItem('sk_token');
    const headers = { 'Content-Type': 'application/json', 'Accept': 'application/json' };
    if (token) headers['Authorization'] = `Bearer ${token}`;
    const res  = await fetch(path, {
      method,
      headers,
      body: body ? JSON.stringify(body) : undefined,
    });
    const text = await res.text();
    let data = null;
    try { data = text ? JSON.parse(text) : null; } catch {}
    return { ok: res.ok, status: res.status, data };
  } catch (e) {
    return { ok: false, status: 0, data: { message: e.message } };
  }
}
const skGet  = p     => skApi('GET',    p);
const skPost = (p,b) => skApi('POST',   p, b);
const skPut  = (p,b) => skApi('PUT',    p, b);
const skDel  = p     => skApi('DELETE', p);

/* ── Toast ──────────────────────────────────────────────────── */
function skToast(msg, type = 'success') {
  const colors = {
    success: 'var(--grn)',
    error:   'var(--red)',
    warning: 'var(--org)',
    info:    'var(--acc)',
  };
  let container = document.getElementById('sk-toasts');
  if (!container) {
    container = document.createElement('div');
    container.id = 'sk-toasts';
    document.body.appendChild(container);
  }
  const div = document.createElement('div');
  div.className = 'sk-toast';
  div.style.borderLeftColor = colors[type] || colors.success;
  div.innerHTML = msg;
  container.appendChild(div);
  setTimeout(() => { div.style.transition = 'opacity .3s'; div.style.opacity = '0'; }, 3200);
  setTimeout(() => div.remove(), 3600);
}

/* ── Modal helpers ──────────────────────────────────────────── */
function skModal(id) { document.getElementById(id).style.display = 'flex'; }
function skModalClose(id) { document.getElementById(id).style.display = 'none'; }

/* ── Formatting ─────────────────────────────────────────────── */
function skFmt(dt) {
  if (!dt) return '—';
  return new Date(dt).toLocaleString('sl-SI', {
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit',
  });
}
function skFmtDate(dt) {
  if (!dt) return '—';
  return new Date(dt).toLocaleDateString('sl-SI', { day:'2-digit', month:'2-digit', year:'numeric' });
}
function skInitials(name, surname) {
  return [(name||'')[0], (surname||'')[0]].filter(Boolean).join('').toUpperCase() || '?';
}
function skUserName(u) {
  if (!u) return '—';
  return `${u.Name||u.name||''} ${u.Surname||u.surname||''}`.trim() || `#${u.Id||u.id}`;
}

/* ── Sidebar loader ─────────────────────────────────────────── */
function skLoadSidebar() {
  const host = document.getElementById('sidebarHost');
  if (!host) return;
  fetch('/partials/sidebar.html')
    .then(r => r.text())
    .then(html => {
      host.innerHTML = '';
      const frag = document.createRange().createContextualFragment(html);
      host.appendChild(frag);
    })
    .catch(() => {});
}

/* ── Spinner helper ─────────────────────────────────────────── */
function skSpinner() {
  return '<div class="sk-spin sk-spin-sm"></div>';
}

/* ── Set button loading state ───────────────────────────────── */
function skBtnLoading(btn, loading, label) {
  btn.disabled = loading;
  btn.innerHTML = loading ? skSpinner() : label;
}

document.addEventListener('DOMContentLoaded', skLoadSidebar);
