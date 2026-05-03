window.itamsSessionStorage = {
  get: function (key) {
    const raw = window.sessionStorage.getItem(key);
    return raw ? JSON.parse(raw) : null;
  },
  set: function (key, value) {
    window.sessionStorage.setItem(key, JSON.stringify(value));
  },
  remove: function (key) {
    window.sessionStorage.removeItem(key);
  }
};

try {
  window.localStorage.removeItem("itams.auth.session");
} catch {
}
