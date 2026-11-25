export function get(key) { try { return localStorage.getItem(key) || ""; } catch { return ""; } }
export function set(key, value) { try { if (value) localStorage.setItem(key, value); else localStorage.removeItem(key); } catch {} }
export function clearAccount(account) { try { const prefix = "rpg-draft:" + account + ":"; Object.keys(localStorage).filter(k => k.startsWith(prefix)).forEach(k => localStorage.removeItem(k)); } catch {} }
