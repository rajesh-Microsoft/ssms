/* Inline SVG icon set. Replaces the emoji in nav items with one coherent
   stroke set at a consistent weight and size.

   Emoji are matched rather than the markup being rewritten, so the 36 nav
   entries (admin + member) and any added later are all covered. Anything
   unmapped simply keeps its emoji. */
(function () {
  'use strict';

  var P = {
    dashboard:'<rect x="3" y="3" width="7" height="9" rx="1.5"/><rect x="14" y="3" width="7" height="5" rx="1.5"/><rect x="14" y="12" width="7" height="9" rx="1.5"/><rect x="3" y="16" width="7" height="5" rx="1.5"/>',
    rupee:'<circle cx="12" cy="12" r="9"/><path d="M9 8h6M9 11h6M14.5 8c0 2-1.5 3-3.5 3H9l4.5 5"/>',
    check:'<circle cx="12" cy="12" r="9"/><path d="M8 12.5l2.5 2.5L16 9.5"/>',
    receipt:'<path d="M5 3h14v18l-2.5-1.7L14 21l-2-1.6L10 21l-2.5-1.7L5 21z"/><path d="M9 8h6M9 12h4"/>',
    cash:'<rect x="2" y="6" width="20" height="12" rx="2"/><circle cx="12" cy="12" r="2.6"/><path d="M6 12h.01M18 12h.01"/>',
    trend:'<path d="M3 17l6-6 4 4 8-8"/><path d="M15 7h6v6"/>',
    bolt:'<path d="M13 2 4 14h7l-1 8 9-12h-7z"/>',
    box:'<path d="M21 8v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8"/><path d="M2 8l3-4h14l3 4H2z"/><path d="M10 12h4"/>',
    bank:'<path d="M3 21h18M5 21V9l7-5 7 5v12"/><path d="M9 21v-6h6v6"/>',
    refund:'<path d="M3 10h13a4 4 0 0 1 0 8h-3"/><path d="M7 6l-4 4 4 4"/>',
    calculator:'<rect x="4" y="2" width="16" height="20" rx="2"/><path d="M8 6h8M8 11h.01M12 11h.01M16 11h.01M8 15h.01M12 15h.01M16 15h.01M8 19h8"/>',
    users:'<circle cx="9" cy="8" r="3.2"/><path d="M2.5 20a6.5 6.5 0 0 1 13 0"/><path d="M17 8.5a3 3 0 0 1 0 5M19 6a6 6 0 0 1 0 10"/>',
    wrench:'<path d="M14.7 6.3a4 4 0 0 1-5.4 5.4L4 17v3h3l5.3-5.3a4 4 0 0 1 5.4-5.4z"/>',
    door:'<path d="M3 21V5a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v16"/><path d="M16 8h3a2 2 0 0 1 2 2v11M2 21h20M12 12h.01"/>',
    clipboard:'<rect x="5" y="4" width="14" height="17" rx="2"/><path d="M9 4V3a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v1"/><path d="M9 11h6M9 15h4"/>',
    upload:'<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><path d="M7 9l5-5 5 5M12 4v12"/>',
    bell:'<path d="M18 8a6 6 0 1 0-12 0c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.7 21a2 2 0 0 1-3.4 0"/>',
    scroll:'<path d="M6 3h11a2 2 0 0 1 2 2v13a3 3 0 0 0 3 3H7a3 3 0 0 1-3-3V5a2 2 0 0 1 2-2z"/><path d="M9 8h7M9 12h7M9 16h4"/>',
    gear:'<circle cx="12" cy="12" r="3"/><path d="M19.1 12.9a7 7 0 0 0 0-1.9l2-1.5a.5.5 0 0 0 .1-.7l-1.9-3.3a.5.5 0 0 0-.6-.2l-2.4 1a7 7 0 0 0-1.6-1l-.4-2.5a.5.5 0 0 0-.5-.4h-3.8a.5.5 0 0 0-.5.4l-.4 2.5a7 7 0 0 0-1.6 1l-2.4-1a.5.5 0 0 0-.6.2L2.7 8.8a.5.5 0 0 0 .1.7l2 1.5a7 7 0 0 0 0 1.9l-2 1.5a.5.5 0 0 0-.1.7l1.9 3.3c.1.2.4.3.6.2l2.4-1a7 7 0 0 0 1.6 1l.4 2.5c0 .2.2.4.5.4h3.8c.3 0 .5-.2.5-.4l.4-2.5a7 7 0 0 0 1.6-1l2.4 1c.2.1.5 0 .6-.2l1.9-3.3a.5.5 0 0 0-.1-.7z"/>',
    shield:'<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><path d="M9 12l2 2 4-4"/>',
    home:'<path d="M3 10.5 12 3l9 7.5"/><path d="M5 9.5V20a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V9.5"/><path d="M9.5 21v-6h5v6"/>',
    hourglass:'<path d="M7 3h10M7 21h10"/><path d="M7 3v3.5a5 5 0 0 0 5 5 5 5 0 0 0 5-5V3"/><path d="M7 21v-3.5a5 5 0 0 1 5-5 5 5 0 0 1 5 5V21"/>',
    wallet:'<path d="M3 7a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2"/><path d="M3 7v10a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2H5"/><circle cx="16.5" cy="13" r="1.2"/>',
    scale:'<path d="M12 3v18M7 21h10"/><path d="M5 7h14"/><path d="M5 7 2.5 13h5zM19 7l-2.5 6h5z"/>'
  };

  var BY_EMOJI = {
    '📊':'dashboard', '💰':'rupee', '✅':'check', '🧾':'receipt', '💵':'cash',
    '📈':'trend', '⚡':'bolt', '📦':'box', '🏦':'bank', '💸':'refund',
    '🧮':'calculator', '👥':'users', '🛠':'wrench', '🚪':'door', '📋':'clipboard',
    '📤':'upload', '🔔':'bell', '📜':'scroll', '⚙':'gear', '🔐':'shield', '🏠':'home',
    '⏳':'hourglass', '👛':'wallet', '⚖':'scale'
  };

  function sprite() {
    if (document.getElementById('smms-icons')) return;
    var svg = '<svg id="smms-icons" aria-hidden="true" style="position:absolute;width:0;height:0;overflow:hidden">';
    for (var k in P) svg += '<symbol id="i-' + k + '" viewBox="0 0 24 24">' + P[k] + '</symbol>';
    document.body.insertAdjacentHTML('afterbegin', svg + '</svg>');
  }

  // Emoji arrive with variation selectors and skin-tone joiners; strip them before matching.
  function key(text) {
    return (text || '').replace(/[\uFE00-\uFE0F\u200D]/g, '').trim();
  }

  function swap(el) {
    if (el.dataset.iconDone) return;
    var name = BY_EMOJI[key(el.textContent)];
    if (!name) return;
    el.dataset.iconDone = '1';
    el.innerHTML = '<svg class="ic" aria-hidden="true" focusable="false"><use href="#i-' + name + '"/></svg>';
  }

  function scan(root) {
    (root || document).querySelectorAll('.ni,.kpi-icon').forEach(swap);
  }

  function init() { sprite(); scan(); }

  document.addEventListener('DOMContentLoaded', init);
  if (document.readyState !== 'loading') init();

  new MutationObserver(function (recs) {
    recs.forEach(function (r) {
      Array.prototype.forEach.call(r.addedNodes, function (n) {
        if (n.nodeType !== 1) return;
        if (n.classList && (n.classList.contains('ni') || n.classList.contains('kpi-icon'))) swap(n);
        else scan(n);
      });
    });
  }).observe(document.documentElement, { childList: true, subtree: true });
})();
