/* Derives a readable brand ramp from whatever colour a society picked.

   Societies set their own Primary Color, so the palette cannot be authored by
   hand: a mid blue needs white text, a pale yellow needs dark text, and a fixed
   "darken by 8%" satisfies neither. Everything here is measured instead. */
(function () {
  'use strict';

  var AA = 4.5;

  function rgb(hex) {
    hex = String(hex || '').trim().replace(/^#/, '');
    if (hex.length === 3) hex = hex.replace(/./g, '$&$&');
    if (!/^[0-9a-f]{6}$/i.test(hex)) return null;
    var n = parseInt(hex, 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
  }
  function hex(c) {
    return '#' + c.map(function (v) {
      return Math.round(Math.max(0, Math.min(255, v))).toString(16).padStart(2, '0');
    }).join('');
  }
  function lum(c) {
    return c.map(function (v) {
      v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
    }).reduce(function (a, x, i) { return a + [0.2126, 0.7152, 0.0722][i] * x; }, 0);
  }
  function ratio(a, b) {
    var l1 = lum(a), l2 = lum(b), hi = Math.max(l1, l2), lo = Math.min(l1, l2);
    return (hi + 0.05) / (lo + 0.05);
  }
  function mix(c, target, amount) {
    return c.map(function (v, i) { return v + (target[i] - v) * amount; });
  }

  var WHITE = [255, 255, 255], BLACK = [16, 24, 40];
  var lastKey = '';

  // Step towards a target until the pair clears AA, or give up and return the last try.
  function shiftUntil(base, target, against) {
    for (var i = 0; i <= 20; i++) {
      var c = mix(base, target, i / 20);
      if (ratio(c, against) >= AA) return c;
    }
    return mix(base, target, 1);
  }

  function apply() {
    var root = document.documentElement;
    var accent = rgb(getComputedStyle(root).getPropertyValue('--accent'));
    if (!accent) return;

    var dark = document.body.classList.contains('dark');
    // apply() writes to the style attribute it is observing, so without this it
    // would retrigger itself on every run.
    var key = accent.join(',') + '|' + dark;
    if (key === lastKey) return;
    lastKey = key;

    // Fill + its label: keep the society's colour if either white or dark text
    // reads on it; only darken when neither does.
    var solid = accent, on = WHITE;
    if (ratio(accent, WHITE) < AA) {
      if (ratio(accent, BLACK) >= AA) on = BLACK;
      else { solid = shiftUntil(accent, BLACK, WHITE); on = WHITE; }
    }

    var weak = mix(accent, dark ? [14, 17, 23] : WHITE, dark ? .82 : .91);
    // A stronger tint for selected states, which need to be spotted at a glance.
    var tint = mix(accent, dark ? [14, 17, 23] : WHITE, dark ? .70 : .82);
    // Measured against the strongest tint brand text ever sits on, so it holds
    // up on the plain surface and on both tints.
    var text = ratio(accent, tint) >= AA
      ? accent
      : shiftUntil(accent, dark ? WHITE : BLACK, tint);

    root.style.setProperty('--brand-solid', hex(solid));
    root.style.setProperty('--brand-on', hex(on));
    root.style.setProperty('--brand-press', hex(mix(solid, BLACK, .18)));
    root.style.setProperty('--brand-text', hex(text));
    root.style.setProperty('--brand-weak', hex(weak));
    root.style.setProperty('--brand-tint', hex(tint));
    root.style.setProperty('--brand-line', hex(mix(accent, dark ? [14, 17, 23] : WHITE, dark ? .6 : .72)));
  }

  window.smmsApplyBrand = function () { lastKey = ''; apply(); };

  function init() {
    apply();
    // applySettings() writes --accent inline once the society's settings arrive.
    new MutationObserver(apply).observe(document.documentElement, { attributes: true, attributeFilter: ['style'] });
    new MutationObserver(apply).observe(document.body, { attributes: true, attributeFilter: ['class'] });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
