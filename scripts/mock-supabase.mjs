// A stand-in for the Supabase Auth + PostgREST endpoints the app uses, for
// testing accounts, sync and the forum without a real project:
//   node scripts/mock-supabase.mjs   (listens on :54321; the sign-in code is 123456,
//   and mod@example.com is a moderator), then build with
//   VITE_SUPABASE_URL=http://localhost:54321 VITE_SUPABASE_ANON_KEY=test npm run build
// It follows the
// same rules schema.sql enforces (owner-only libraries, author names from
// profiles, moderator-only status changes). For local testing only.
import http from 'node:http';
import { randomUUID } from 'node:crypto';
const users = {};      // email -> { id, email }
const tokens = {};     // access token -> user id
const refresh = {};    // refresh token -> user id
const profiles = {};   // id -> { id, display_name, is_admin }
const libraries = {};  // user_id -> { data, updated_at }
let threads = [];      // rows
let posts = [];
let reports = [];
const log = [];
const ADMIN_EMAIL = 'mod@example.com';

function send(res, status, body) {
  res.writeHead(status, {
    'Content-Type': 'application/json',
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'apikey, authorization, content-type, prefer',
    'Access-Control-Allow-Methods': 'GET,POST,PATCH,DELETE,OPTIONS'
  });
  res.end(body === undefined ? '' : JSON.stringify(body));
}
function issue(uid, email) {
  const a = 'at-' + randomUUID(), r = 'rt-' + randomUUID();
  tokens[a] = uid; refresh[r] = uid;
  return { access_token: a, refresh_token: r, expires_in: 3600, token_type: 'bearer', user: { id: uid, email } };
}
function who(req) {
  const h = req.headers.authorization || '';
  return tokens[h.replace('Bearer ', '')] || null;
}
function filters(q) {
  const f = [];
  for (const [k, v] of q) {
    if (['select', 'order', 'limit', 'on_conflict'].includes(k)) continue;
    const [op, ...rest] = v.split('.'); const val = rest.join('.');
    f.push(row => op === 'eq' ? String(row[k]) === val : op === 'neq' ? String(row[k]) !== val : true);
  }
  return f;
}
const nameOf = uid => (profiles[uid] && profiles[uid].display_name) || 'A reader';
const isMod = uid => !!(profiles[uid] && profiles[uid].is_admin);

http.createServer(async (req, res) => {
  if (req.method === 'OPTIONS') return send(res, 204);
  const url = new URL(req.url, 'http://x');
  let body = '';
  for await (const c of req) body += c;
  const json = body ? JSON.parse(body) : null;
  const uid = who(req);
  log.push(req.method + ' ' + url.pathname + url.search);
  const p = url.pathname;
  if (p === '/__log') return send(res, 200, { log, threads, posts, libraries, profiles, reports, users });
  // Supabase's answer when too many emails were sent: try it with this address
  if (p === '/auth/v1/otp' && json.email === 'ratelimit@example.com') return send(res, 429, { code: 429, error_code: 'over_email_send_rate_limit', msg: 'email rate limit exceeded' });
  if (p === '/auth/v1/otp') { if (!users[json.email]) users[json.email] = { id: randomUUID(), email: json.email }; if (json.email === ADMIN_EMAIL) profiles[users[json.email].id] = { id: users[json.email].id, display_name: 'Moderator', is_admin: true }; return send(res, 200, {}); }
  if (p === '/auth/v1/verify') {
    const u = users[json.email];
    if (!u || json.token !== '123456') return send(res, 403, { error_description: 'Token has expired or is invalid' });
    return send(res, 200, issue(u.id, u.email));
  }
  if (p === '/auth/v1/token') {
    const id = refresh[json.refresh_token]; if (!id) return send(res, 400, { error_description: 'Invalid Refresh Token' });
    const email = Object.values(users).find(u => u.id === id).email;
    return send(res, 200, issue(id, email));
  }
  if (p === '/auth/v1/user') { if (!uid) return send(res, 401, { msg: 'invalid JWT' }); const u = Object.values(users).find(u => u.id === uid); return send(res, 200, { id: uid, email: u.email }); }
  if (p === '/auth/v1/logout') return send(res, 204);
  const table = p.replace('/rest/v1/', '');
  const f = filters(url.searchParams);
  const match = row => f.every(fn => fn(row));
  if (table === 'profiles') {
    if (req.method === 'GET') return send(res, 200, Object.values(profiles).filter(match));
    if (req.method === 'POST') { if (!uid || json.id !== uid) return send(res, 403, { message: 'new row violates row-level security policy' }); const old = profiles[uid] || { is_admin: false };
      profiles[uid] = { ...old, id: uid, display_name: json.display_name };
      threads.forEach(t => { if (t.author_id === uid) t.author_name = json.display_name; }); posts.forEach(t => { if (t.author_id === uid) t.author_name = json.display_name; });
      return send(res, 201); }
  }
  if (table === 'libraries') {
    if (!uid) return send(res, 200, []);
    if (req.method === 'GET') return send(res, 200, libraries[uid] ? [libraries[uid]] : []);
    if (req.method === 'POST') { if (json.user_id !== uid) return send(res, 403, { message: 'rls' }); libraries[uid] = { user_id: uid, data: json.data, updated_at: new Date().toISOString() }; return send(res, 201); }
  }
  if (table === 'forum_threads') {
    if (req.method === 'GET') { let rows = threads.filter(match).sort((a, b) => b.last_post_at.localeCompare(a.last_post_at)); return send(res, 200, rows); }
    if (req.method === 'POST') { if (!uid || json.author_id !== uid) return send(res, 401, { message: 'rls' });
      const now = new Date().toISOString();
      const row = { id: randomUUID(), category: json.category, title: json.title, body: json.body, work: json.work || '', ref: json.ref || '', status: json.category === 'bugs' ? 'open' : '', author_id: uid, author_name: nameOf(uid), reply_count: 0, created_at: now, last_post_at: now };
      threads.push(row); return send(res, 201, [row]); }
    if (req.method === 'PATCH') { if (!isMod(uid)) return send(res, 200, []); threads.filter(match).forEach(t => t.status = json.status); return send(res, 204); }
    if (req.method === 'DELETE') { const del = threads.filter(match).filter(t => t.author_id === uid || isMod(uid)).map(t => t.id); threads = threads.filter(t => !del.includes(t.id)); posts = posts.filter(x => !del.includes(x.thread_id)); return send(res, 204); }
  }
  if (table === 'forum_posts') {
    if (req.method === 'GET') return send(res, 200, posts.filter(match).sort((a, b) => a.created_at.localeCompare(b.created_at)));
    if (req.method === 'POST') { if (!uid || json.author_id !== uid) return send(res, 401, { message: 'rls' });
      const now = new Date().toISOString();
      posts.push({ id: randomUUID(), thread_id: json.thread_id, body: json.body, author_id: uid, author_name: nameOf(uid), created_at: now });
      const t = threads.find(t => t.id === json.thread_id); if (t) { t.reply_count++; t.last_post_at = now; }
      return send(res, 201); }
    if (req.method === 'DELETE') { const del = posts.filter(match).filter(x => x.author_id === uid || isMod(uid)); posts = posts.filter(x => !del.includes(x)); del.forEach(x => { const t = threads.find(t => t.id === x.thread_id); if (t) t.reply_count--; }); return send(res, 204); }
  }
  if (table === 'forum_reports') {
    if (req.method === 'GET') return send(res, 200, isMod(uid) ? reports.filter(match).sort((a, b) => b.created_at.localeCompare(a.created_at)) : []);
    if (req.method === 'POST') { if (!uid || json.reporter_id !== uid) return send(res, 401, { message: 'rls' });
      if (reports.some(r => r.reporter_id === uid && r.thread_id === json.thread_id && (r.post_id || '') === (json.post_id || ''))) return send(res, 409, { message: 'duplicate key value violates unique constraint "forum_reports_once"' });
      reports.push({ id: randomUUID(), thread_id: json.thread_id, post_id: json.post_id || null, reason: json.reason, note: json.note || '', thread_title: json.thread_title || '', excerpt: json.excerpt || '', reporter_id: uid, status: 'open', created_at: new Date().toISOString() });
      return send(res, 201); }
    if (req.method === 'PATCH') { if (!isMod(uid)) return send(res, 200, []); reports.filter(match).forEach(r => r.status = json.status); return send(res, 204); }
  }
  // deleting an account cascades to everything it owns, as in schema.sql
  if (p === '/rest/v1/rpc/delete_my_account' && req.method === 'POST') {
    if (!uid) return send(res, 401, { message: 'Sign in first.' });
    const email = Object.keys(users).find(e => users[e].id === uid); if (email) delete users[email];
    for (const t of Object.keys(tokens)) if (tokens[t] === uid) delete tokens[t];
    for (const t of Object.keys(refresh)) if (refresh[t] === uid) delete refresh[t];
    delete profiles[uid]; delete libraries[uid];
    const gone = threads.filter(t => t.author_id === uid).map(t => t.id);
    threads = threads.filter(t => t.author_id !== uid);
    posts.filter(x => x.author_id === uid && !gone.includes(x.thread_id)).forEach(x => { const t = threads.find(t => t.id === x.thread_id); if (t) t.reply_count--; });
    posts = posts.filter(x => x.author_id !== uid && !gone.includes(x.thread_id));
    reports = reports.filter(r => r.reporter_id !== uid && !gone.includes(r.thread_id));
    return send(res, 204);
  }
  send(res, 404, { message: 'no route ' + p });
}).listen(54321, () => console.log('mock on 54321'));
