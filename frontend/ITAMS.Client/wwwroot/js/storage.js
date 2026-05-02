window.itamsStorage = {
  get: function (key) {
    const raw = window.localStorage.getItem(key);
    return raw ? JSON.parse(raw) : null;
  },
  set: function (key, value) {
    window.localStorage.setItem(key, JSON.stringify(value));
  },
  remove: function (key) {
    window.localStorage.removeItem(key);
  }
};
