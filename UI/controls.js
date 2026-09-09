/* ============================================================
   Enhanced select + date picker.

   Both keep the original native element as the source of truth and
   dispatch a real `change`, so everything in app.js that reads .value,
   assigns .value or listens for change keeps working untouched.
   Opt out per element with data-no-enhance.
   ============================================================ */
(function () {
  'use strict';

  var CHEV = '<svg class="ic" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 9l6 6 6-6"/></svg>';
  var TICK = '<svg class="ic xc-tick" viewBox="0 0 24 24" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg>';
  var valueProp = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
  var selValueProp = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value');

  function place(pop, anchor) {
    var r = anchor.getBoundingClientRect();
    var h = pop.offsetHeight;
    var below = window.innerHeight - r.bottom;
    var up = below < h + 8 && r.top > below;
    pop.style.left = Math.max(8, Math.min(r.left, window.innerWidth - pop.offsetWidth - 8)) + 'px';
    pop.style.top = (up ? Math.max(8, r.top - h - 6) : r.bottom + 6) + 'px';
    pop.style.minWidth = r.width + 'px';
  }

  /* ── select ─────────────────────────────────────────────── */
  function enhanceSelect(sel) {
    if (sel.dataset.xsReady || sel.multiple || sel.hasAttribute('data-no-enhance')) return;
    sel.dataset.xsReady = '1';

    var wrap = document.createElement('div');
    wrap.className = 'xs';
    if (sel.getAttribute('style')) { wrap.setAttribute('style', sel.getAttribute('style')); sel.removeAttribute('style'); }
    sel.parentNode.insertBefore(wrap, sel);
    wrap.appendChild(sel);

    var trigger = document.createElement('button');
    trigger.type = 'button';
    trigger.className = 'xs-trigger';
    trigger.setAttribute('aria-haspopup', 'listbox');
    trigger.setAttribute('aria-expanded', 'false');
    wrap.appendChild(trigger);

    var pop = null, opts = [], active = -1;

    function label() {
      var o = sel.options[sel.selectedIndex];
      var txt = o ? o.textContent.trim() : '';
      trigger.innerHTML = '<span class="xs-label' + (txt ? '' : ' is-ph') + '"></span>' + CHEV;
      trigger.querySelector('.xs-label').textContent = txt || 'Select…';
      trigger.disabled = sel.disabled;
      wrap.classList.toggle('is-disabled', sel.disabled);
    }

    function close() {
      if (!pop) return;
      pop.remove(); pop = null; active = -1;
      trigger.setAttribute('aria-expanded', 'false');
      document.removeEventListener('mousedown', onOutside, true);
      window.removeEventListener('resize', close);
      window.removeEventListener('scroll', onScroll, true);
    }

    function onOutside(e) { if (pop && !pop.contains(e.target) && e.target !== trigger) close(); }
    // Capturing, so it also fires for scrolls inside the list itself; those must not close it.
    function onScroll(e) { if (pop && pop.contains(e.target)) return; close(); }

    function paint(filter) {
      var list = pop.querySelector('.xs-list');
      var q = (filter || '').toLowerCase();
      list.innerHTML = '';
      opts = [];
      var lastGroup = null;
      Array.prototype.forEach.call(sel.options, function (o) {
        if (q && o.textContent.toLowerCase().indexOf(q) === -1) return;
        var g = o.parentNode.tagName === 'OPTGROUP' ? o.parentNode.label : null;
        if (g && g !== lastGroup) {
          list.insertAdjacentHTML('beforeend', '<div class="xs-grp"></div>');
          list.lastChild.textContent = g;
          lastGroup = g;
        }
        var row = document.createElement('div');
        row.className = 'xs-opt' + (o.disabled ? ' is-disabled' : '');
        row.setAttribute('role', 'option');
        row.setAttribute('aria-selected', o.selected ? 'true' : 'false');
        var span = document.createElement('span');
        span.textContent = o.textContent.trim();
        row.appendChild(span);
        row.insertAdjacentHTML('beforeend', TICK);
        row.addEventListener('mousedown', function (e) {
          e.preventDefault();
          if (o.disabled) return;
          commit(o);
        });
        list.appendChild(row);
        if (!o.disabled) opts.push({ el: row, opt: o });
      });
      pop.querySelector('.xs-none').style.display = opts.length ? 'none' : 'block';
      active = opts.findIndex(function (x) { return x.opt.selected; });
      mark();
    }

    function mark() {
      opts.forEach(function (x, i) { x.el.classList.toggle('is-active', i === active); });
      if (active > -1) opts[active].el.scrollIntoView({ block: 'nearest' });
    }

    function commit(o) {
      sel.value = o.value;
      sel.dispatchEvent(new Event('input', { bubbles: true }));
      sel.dispatchEvent(new Event('change', { bubbles: true }));
      label();
      close();
      trigger.focus();
    }

    function open() {
      if (pop || sel.disabled) return;
      pop = document.createElement('div');
      pop.className = 'xc-pop xs-pop';
      pop.setAttribute('role', 'listbox');
      var searchable = sel.options.length > 8;
      pop.innerHTML =
        (searchable ? '<div class="xs-search"><input type="text" placeholder="Search…" aria-label="Filter options"></div>' : '') +
        '<div class="xs-list"></div><div class="xs-none">No matches</div>';
      document.body.appendChild(pop);
      paint('');
      place(pop, trigger);
      pop.addEventListener('keydown', onKey);
      trigger.setAttribute('aria-expanded', 'true');
      document.addEventListener('mousedown', onOutside, true);
      window.addEventListener('resize', close);
      window.addEventListener('scroll', onScroll, true);
      var s = pop.querySelector('.xs-search input');
      if (s) { s.addEventListener('input', function () { paint(s.value); }); s.focus(); }
    }

    trigger.addEventListener('click', function () { pop ? close() : open(); });
    trigger.addEventListener('keydown', function (e) {
      if (!pop && (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); open(); }
    });

    function onKey(e) {
      if (!pop) return;
      if (e.key === 'Escape') { e.preventDefault(); close(); trigger.focus(); }
      else if (e.key === 'ArrowDown') { e.preventDefault(); active = Math.min(active + 1, opts.length - 1); mark(); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); active = Math.max(active - 1, 0); mark(); }
      else if (e.key === 'Home') { e.preventDefault(); active = 0; mark(); }
      else if (e.key === 'End') { e.preventDefault(); active = opts.length - 1; mark(); }
      else if (e.key === 'Enter' && active > -1) { e.preventDefault(); commit(opts[active].opt); }
    }
    // Bound to the popup too: it lives on document.body, so keys pressed inside
    // the search box never bubble through the wrapper.
    wrap.addEventListener('keydown', onKey);

    sel.addEventListener('change', label);
    // app.js rebuilds option lists and assigns .value directly, neither of which notifies us.
    new MutationObserver(label).observe(sel, { childList: true, subtree: true, attributes: true, attributeFilter: ['disabled'] });
    Object.defineProperty(sel, 'value', {
      configurable: true,
      get: function () { return selValueProp.get.call(this); },
      set: function (v) { selValueProp.set.call(this, v); label(); }
    });

    label();
  }

  /* ── date picker ────────────────────────────────────────── */
  var MONTHS = ['January','February','March','April','May','June','July','August','September','October','November','December'];
  var DOW = ['Su','Mo','Tu','We','Th','Fr','Sa'];

  function iso(d) {
    return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
  }
  function parse(v) {
    var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(v || '');
    return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null;
  }

  function enhanceDate(input) {
    if (input.dataset.xdReady || input.hasAttribute('data-no-enhance')) return;
    input.dataset.xdReady = '1';

    var wrap = document.createElement('div');
    wrap.className = 'xd';
    if (input.getAttribute('style')) { wrap.setAttribute('style', input.getAttribute('style')); input.removeAttribute('style'); }
    input.parentNode.insertBefore(wrap, input);
    wrap.appendChild(input);

    var btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'xd-btn';
    btn.setAttribute('aria-label', 'Open calendar');
    btn.innerHTML = '<svg class="ic" viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg>';
    wrap.appendChild(btn);

    var pop = null, view = null, focusDay = null;

    function close() {
      if (!pop) return;
      pop.remove(); pop = null;
      btn.setAttribute('aria-expanded', 'false');
      document.removeEventListener('mousedown', onOutside, true);
      window.removeEventListener('resize', close);
      window.removeEventListener('scroll', onScroll, true);
    }
    function onOutside(e) { if (pop && !pop.contains(e.target) && e.target !== btn) close(); }
    function onScroll(e) { if (pop && pop.contains(e.target)) return; close(); }

    function limits() {
      return { min: parse(input.getAttribute('min')), max: parse(input.getAttribute('max')) };
    }
    function blocked(d) {
      var l = limits();
      return (l.min && d < l.min) || (l.max && d > l.max);
    }

    function commit(d) {
      input.value = iso(d);
      input.dispatchEvent(new Event('input', { bubbles: true }));
      input.dispatchEvent(new Event('change', { bubbles: true }));
      close();
      input.focus();
    }

    function draw() {
      var sel = parse(input.value);
      var today = new Date(); today.setHours(0, 0, 0, 0);
      var first = new Date(view.getFullYear(), view.getMonth(), 1);
      var start = new Date(first); start.setDate(1 - first.getDay());

      var cells = '';
      for (var i = 0; i < 42; i++) {
        var d = new Date(start); d.setDate(start.getDate() + i);
        var cls = 'xd-day';
        if (d.getMonth() !== view.getMonth()) cls += ' is-out';
        if (sel && d.getTime() === sel.getTime()) cls += ' is-sel';
        if (d.getTime() === today.getTime()) cls += ' is-today';
        if (blocked(d)) cls += ' is-off';
        if (focusDay && d.getTime() === focusDay.getTime()) cls += ' is-focus';
        cells += '<button type="button" class="' + cls + '" data-d="' + iso(d) + '"' +
                 (blocked(d) ? ' disabled' : '') + ' tabindex="-1">' + d.getDate() + '</button>';
      }

      pop.querySelector('.xd-title').textContent = MONTHS[view.getMonth()] + ' ' + view.getFullYear();
      pop.querySelector('.xd-grid').innerHTML = cells;
    }

    function shift(months) {
      view = new Date(view.getFullYear(), view.getMonth() + months, 1);
      draw();
    }

    function open() {
      if (pop || input.disabled || input.readOnly) return;
      view = parse(input.value) || new Date();
      view = new Date(view.getFullYear(), view.getMonth(), 1);
      focusDay = parse(input.value) || new Date();
      focusDay.setHours(0, 0, 0, 0);

      pop = document.createElement('div');
      pop.className = 'xc-pop xd-pop';
      pop.tabIndex = -1;
      pop.innerHTML =
        '<div class="xd-head">' +
          '<button type="button" class="xd-nav" data-m="-1" aria-label="Previous month"><svg class="ic" viewBox="0 0 24 24"><path d="M15 18l-6-6 6-6"/></svg></button>' +
          '<div class="xd-title" aria-live="polite"></div>' +
          '<button type="button" class="xd-nav" data-m="1" aria-label="Next month"><svg class="ic" viewBox="0 0 24 24"><path d="M9 6l6 6-6 6"/></svg></button>' +
        '</div>' +
        '<div class="xd-dow">' + DOW.map(function (d) { return '<span>' + d + '</span>'; }).join('') + '</div>' +
        '<div class="xd-grid" role="grid"></div>' +
        '<div class="xd-foot">' +
          '<button type="button" class="xd-link" data-today>Today</button>' +
          '<button type="button" class="xd-link" data-clear>Clear</button>' +
        '</div>';
      document.body.appendChild(pop);
      draw();
      place(pop, wrap);
      pop.addEventListener('keydown', onDateKey);
      btn.setAttribute('aria-expanded', 'true');
      document.addEventListener('mousedown', onOutside, true);
      window.addEventListener('resize', close);
      window.addEventListener('scroll', onScroll, true);
      pop.focus();

      pop.addEventListener('mousedown', function (e) {
        var nav = e.target.closest('.xd-nav');
        var day = e.target.closest('.xd-day');
        e.preventDefault();
        if (nav) return shift(+nav.dataset.m);
        if (e.target.closest('[data-today]')) { var t = new Date(); t.setHours(0,0,0,0); if (!blocked(t)) commit(t); return; }
        if (e.target.closest('[data-clear]')) { input.value = ''; input.dispatchEvent(new Event('change', { bubbles: true })); close(); return; }
        if (day && !day.disabled) commit(parse(day.dataset.d));
      });
    }

    wrap.addEventListener('keydown', onDateKey);

    function onDateKey(e) {
      if (!pop) return;
      var step = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }[e.key];
      if (step) {
        e.preventDefault();
        focusDay = new Date(focusDay.getFullYear(), focusDay.getMonth(), focusDay.getDate() + step);
        view = new Date(focusDay.getFullYear(), focusDay.getMonth(), 1);
        draw();
      } else if (e.key === 'PageUp') { e.preventDefault(); shift(-1); }
      else if (e.key === 'PageDown') { e.preventDefault(); shift(1); }
      else if (e.key === 'Enter' && focusDay && !blocked(focusDay)) { e.preventDefault(); commit(focusDay); }
      else if (e.key === 'Escape') { e.preventDefault(); close(); input.focus(); }
    }

    btn.addEventListener('click', function () { pop ? close() : open(); });
    Object.defineProperty(input, 'value', {
      configurable: true,
      get: function () { return valueProp.get.call(this); },
      set: function (v) { valueProp.set.call(this, v); if (pop) { view = parse(v) || new Date(); draw(); } }
    });
  }

  /* ── wiring ─────────────────────────────────────────────── */
  function scan(root) {
    (root || document).querySelectorAll('select').forEach(enhanceSelect);
    (root || document).querySelectorAll('input[type="date"]').forEach(enhanceDate);
  }

  document.addEventListener('DOMContentLoaded', function () { scan(); });
  if (document.readyState !== 'loading') scan();

  new MutationObserver(function (recs) {
    recs.forEach(function (r) {
      Array.prototype.forEach.call(r.addedNodes, function (n) {
        if (n.nodeType !== 1) return;
        if (n.matches('select')) enhanceSelect(n);
        else if (n.matches('input[type="date"]')) enhanceDate(n);
        else scan(n);
      });
    });
  }).observe(document.documentElement, { childList: true, subtree: true });
})();
