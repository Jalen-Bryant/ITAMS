window.itamsLayout = (() => {
  const observers = new Map();

  function createId() {
    if (window.crypto && typeof window.crypto.randomUUID === "function") {
      return window.crypto.randomUUID();
    }

    return `topbar-${Date.now()}-${Math.random().toString(16).slice(2)}`;
  }

  function buildObserver(topbar, sentinel) {
    const topValue = window.getComputedStyle(topbar).top;
    const topOffset = Number.parseFloat(topValue);
    const rootOffset = Number.isFinite(topOffset) ? -Math.max(topOffset, 0) : 0;

    const observer = new IntersectionObserver(
      ([entry]) => {
        topbar.classList.toggle("is-compact", !entry.isIntersecting);
      },
      {
        root: null,
        threshold: 0,
        rootMargin: `${rootOffset}px 0px 0px 0px`
      });

    observer.observe(sentinel);
    return observer;
  }

  function observeTopbar(topbar, sentinel) {
    if (!topbar || !sentinel || typeof window.IntersectionObserver === "undefined") {
      return "";
    }

    const id = createId();
    const record = {
      observer: null,
      resizeHandler: null,
      sentinel,
      topbar
    };

    const refreshObserver = () => {
      if (record.observer) {
        record.observer.disconnect();
      }

      record.observer = buildObserver(topbar, sentinel);
    };

    record.resizeHandler = () => refreshObserver();
    window.addEventListener("resize", record.resizeHandler, { passive: true });
    refreshObserver();
    topbar.classList.remove("is-compact");
    observers.set(id, record);
    return id;
  }

  function disposeTopbar(id) {
    const record = observers.get(id);
    if (!record) {
      return;
    }

    if (record.observer) {
      record.observer.disconnect();
    }

    if (record.resizeHandler) {
      window.removeEventListener("resize", record.resizeHandler);
    }

    record.topbar.classList.remove("is-compact");
    observers.delete(id);
  }

  return {
    observeTopbar,
    disposeTopbar
  };
})();
