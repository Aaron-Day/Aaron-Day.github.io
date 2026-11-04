// A just-enough browser for the inline boot watchdog in index.html: a window with spec-style listener dispatch
// (capture versus bubble, at-target), elements with a style.display that notifies MutationObservers, and
// observers whose records are delivered on flush() the way a microtask would deliver them.
// The watchdog source is the real one, read from index.html. Nothing here knows how it works inside.
function makeEnv(code, options) {
  options = options || {};
  var listeners = [];
  var observers = [];
  var displayWrites = [];          // every write to bar.style.display, in order, with who made it
  var who = 'script';

  var win = {
    addEventListener: function (type, fn, cap) {
      cap = cap === true;
      for (var i = 0; i < listeners.length; i++)
        if (listeners[i].type === type && listeners[i].fn === fn && listeners[i].cap === cap) return;
      listeners.push({ type: type, fn: fn, cap: cap });
    },
    removeEventListener: function (type, fn, cap) {
      cap = cap === true;
      for (var i = 0; i < listeners.length; i++)
        if (listeners[i].type === type && listeners[i].fn === fn && listeners[i].cap === cap) { listeners.splice(i, 1); return; }
    },
    // An event on window itself runs capture and bubble listeners. A non-bubbling event on an element
    // (a script that failed to load) only reaches capture listeners on window.
    fire: function (type, ev, onWindow) {
      var snapshot = listeners.slice();
      for (var i = 0; i < snapshot.length; i++) {
        var l = snapshot[i];
        if (l.type !== type || listeners.indexOf(l) < 0) continue;
        if (!onWindow && !l.cap) continue;
        l.fn(ev);
      }
    }
  };

  function queue(target, record) {
    observers.forEach(function (o) {
      if (!o.active || o.target !== target) return;
      if (record.type === 'attributes' && !(o.options.attributes && (!o.options.attributeFilter || o.options.attributeFilter.indexOf(record.attributeName) >= 0))) return;
      if (record.type === 'childList' && !o.options.childList) return;
      o.records.push(record);
    });
  }

  function flush() {
    for (var round = 0; round < 10; round++) {
      var any = false;
      observers.slice().forEach(function (o) {
        if (o.active && o.records.length) { var r = o.records; o.records = []; any = true; o.callback(r, o); }
      });
      if (!any) return;
    }
  }

  function MutationObserverStub(callback) {
    this.callback = callback; this.active = false; this.records = [];
  }
  MutationObserverStub.prototype.observe = function (target, options) {
    this.target = target; this.options = options; this.active = true; observers.push(this);
  };
  MutationObserverStub.prototype.disconnect = function () {
    this.active = false; this.records = [];
  };

  var log = [];                    // ordered notes about changes to #boot-status
  function element(id, init) {
    var display = '';
    var e = {
      id: id, className: '', attrs: {}, removed: false, onclick: null, kids: {}, tagName: 'DIV',
      setAttribute: function (n, v) { this.attrs[n] = String(v); },
      removeAttribute: function (n) { delete this.attrs[n]; if (id === 'boot-status') log.push('removeAttribute:' + n); },
      getAttribute: function (n) { return n in this.attrs ? this.attrs[n] : null; },
      querySelector: function (sel) { return this.kids[sel] || null; },
      focus: function () { env.activeElement = this; }
    };
    Object.defineProperty(e, 'style', { value: Object.defineProperty({}, 'display', {
      get: function () { return display; },
      set: function (v) {
        display = v;
        if (id === 'blazor-error-ui') displayWrites.push(who + ':' + v);
        queue(e, { type: 'attributes', attributeName: 'style' });
      } }) });
    if (id === 'boot-status') {
      var text = 'Loading library';
      Object.defineProperty(e, 'textContent', { get: function () { return text; }, set: function (v) { text = v; log.push('textContent'); } });
    }
    return Object.assign(e, init);
  }

  var app = element('app');
  var spinner = element('spinner'), spinnerText = element('spinnerText');
  var status = element('boot-status', { className: 'visually-hidden', attrs: { role: 'status' } });
  var reloadBtn = element('reload'), dismissBtn = element('dismiss');
  var bar = element('blazor-error-ui', { kids: { '.reload': reloadBtn, '.dismiss': dismissBtn } });
  var byId = { 'app': app, 'boot-status': status, 'blazor-error-ui': bar };
  var reloads = 0;

  var doc = {
    getElementById: function (id) { var e = byId[id]; return e && !e.removed ? e : null; },
    querySelectorAll: function () { return [spinner, spinnerText].filter(function (e) { return !e.removed; }); }
  };

  var env = {
    win: win, bar: bar, status: status, spinner: spinner, spinnerText: spinnerText, reloadBtn: reloadBtn, dismissBtn: dismissBtn,
    activeElement: null, flush: flush, log: log, displayWrites: displayWrites,
    reloads: function () { return reloads; },
    listeners: function () { return listeners.map(function (l) { return l.type + (l.cap ? '(capture)' : ''); }).sort(); },
    activeObservers: function () { return observers.filter(function (o) { return o.active; }).length; },
    // Blazor attaching its root component: the children of #app are replaced.
    bootSucceeds: function () {
      [spinner, spinnerText, status].forEach(function (e) { e.removed = true; });
      queue(app, { type: 'childList' });
    },
    // What the runtime does on its own error path: reveal the bar and wire its own handlers.
    runtimeReveals: function () {
      who = 'runtime';
      bar.style.display = 'block';
      reloadBtn.onclick = function () { reloads++; };
      dismissBtn.onclick = function () { bar.style.display = 'none'; };
      who = 'script';
    }
  };

  var Observer = options.noMutationObserver ? undefined : MutationObserverStub;
  new Function('window', 'document', 'location', 'MutationObserver', code)(win, doc, { reload: function () { reloads++; } }, Observer);
  return env;
}

// The observable result of a scenario, as plain data.
function state(env) {
  return {
    bar: env.bar.style.display,
    spinner: env.spinner.style.display,
    spinnerText: env.spinnerText.style.display,
    role: env.status.getAttribute('role'),
    cls: env.status.className,
    tabindex: env.status.getAttribute('tabindex'),
    text: env.status.textContent,
    listeners: env.listeners(),
    observers: env.activeObservers(),
    reloadWired: typeof env.reloadBtn.onclick,
    dismissWired: typeof env.dismissBtn.onclick
  };
}
