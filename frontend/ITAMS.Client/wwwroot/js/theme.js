window.itamsTheme = (() => {
  const storageKey = "itams.theme";
  const defaultTheme = "dark";
  const supportedThemes = new Set(["dark", "light"]);
  const themeColors = {
    dark: "#0d1a1f",
    light: "#f4f8f9"
  };

  function normalizeTheme(theme) {
    return supportedThemes.has(theme) ? theme : defaultTheme;
  }

  function setThemeColor(theme) {
    const metaTheme = document.querySelector('meta[name="theme-color"]');
    if (!metaTheme) {
      return;
    }

    metaTheme.setAttribute("content", themeColors[theme] ?? themeColors[defaultTheme]);
  }

  function readStoredTheme() {
    try {
      return normalizeTheme(window.localStorage.getItem(storageKey));
    } catch {
      return defaultTheme;
    }
  }

  function applyTheme(theme, persist) {
    const normalizedTheme = normalizeTheme(theme);
    document.documentElement.dataset.theme = normalizedTheme;
    setThemeColor(normalizedTheme);

    if (persist) {
      try {
        window.localStorage.setItem(storageKey, normalizedTheme);
      } catch {
      }
    }

    return normalizedTheme;
  }

  function prefersReducedMotion() {
    return window.matchMedia &&
      typeof window.matchMedia === "function" &&
      window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  }

  async function transitionTheme(theme) {
    const normalizedTheme = normalizeTheme(theme);
    if (normalizeTheme(document.documentElement.dataset.theme) === normalizedTheme) {
      return normalizedTheme;
    }

    if (typeof document.startViewTransition !== "function" || prefersReducedMotion()) {
      return applyTheme(normalizedTheme, true);
    }

    const transition = document.startViewTransition(() => {
      applyTheme(normalizedTheme, true);
    });

    try {
      await transition.finished;
    } catch {
    }

    return normalizedTheme;
  }

  applyTheme(readStoredTheme(), false);

  return {
    get() {
      return normalizeTheme(document.documentElement.dataset.theme);
    },
    async set(theme) {
      return transitionTheme(theme);
    }
  };
})();
