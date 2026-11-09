// Light/dark is shared with the other NaguSuite tools through the cookie naguya_theme. No cookie = follow the system.
(() => {
  const m = document.cookie.match(/(?:^|;\s*)naguya_theme=(light|dark)\b/);
  if (m) document.documentElement.dataset.theme = m[1];
  window.toggleTheme = () => {
    const root = document.documentElement;
    const dark = root.dataset.theme ? root.dataset.theme === 'dark' : matchMedia('(prefers-color-scheme: dark)').matches;
    root.dataset.theme = dark ? 'light' : 'dark';
    const domain = location.hostname.endsWith('naguya.tech') ? '; Domain=naguya.tech' : '';
    document.cookie = `naguya_theme=${root.dataset.theme}; Path=/; Max-Age=31536000; SameSite=Lax${domain}`;
  };
})();
