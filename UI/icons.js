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
    scale:'<path d="M12 3v18M7 21h10"/><path d="M5 7h14"/><path d="M5 7 2.5 13h5zM19 7l-2.5 6h5z"/>',
    trash:'<path d="M4 7h16M10 4h4M9 7l.7 13h4.6L15 7"/><path d="M10 11v6M14 11v6"/>',
    pencil:'<path d="M4 20h4L19.5 8.5a2.1 2.1 0 0 0-3-3L5 17z"/><path d="M14.5 5.5l3 3"/>',
    save:'<path d="M5 3h11l3 3v15H5z"/><path d="M8 3v6h7V3M8 21v-7h8v7"/>',
    search:'<circle cx="11" cy="11" r="7"/><path d="M21 21l-4-4"/>',
    eye:'<path d="M2 12s3.6-6.5 10-6.5S22 12 22 12s-3.6 6.5-10 6.5S2 12 2 12z"/><circle cx="12" cy="12" r="2.7"/>',
    card:'<rect x="2" y="5" width="20" height="14" rx="2.5"/><path d="M2 10h20M6 15h4"/>',
    clip:'<path d="M20 11l-8.5 8.5a5 5 0 0 1-7-7L13 4a3.4 3.4 0 0 1 4.8 4.8l-8.5 8.5a1.8 1.8 0 0 1-2.5-2.5l7.9-7.9"/>',
    download:'<path d="M12 3v12"/><path d="M8 11l4 4 4-4"/><path d="M4 19h16"/>',
    building:'<path d="M4 21V4a1 1 0 0 1 1-1h9a1 1 0 0 1 1 1v17"/><path d="M15 9h4a1 1 0 0 1 1 1v11M2 21h20"/><path d="M7 7h2M7 11h2M7 15h2"/>',
    calendar:'<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/>',
    user:'<circle cx="12" cy="8" r="3.4"/><path d="M4.5 20a7.5 7.5 0 0 1 15 0"/>',
    key:'<circle cx="8" cy="14" r="4"/><path d="M11 11 21 1M18 4l2 2M15 7l2 2"/>',
    lock:'<rect x="4" y="10" width="16" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/>',
    unlock:'<rect x="4" y="10" width="16" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 7.5-2"/>',
    phone:'<rect x="6" y="2" width="12" height="20" rx="2.5"/><path d="M11 18h2"/>',
    mail:'<rect x="2.5" y="5" width="19" height="14" rx="2"/><path d="M3 7l9 6 9-6"/>',
    pin:'<path d="M12 21s7-6.2 7-11a7 7 0 1 0-14 0c0 4.8 7 11 7 11z"/><circle cx="12" cy="10" r="2.6"/>',
    target:'<circle cx="12" cy="12" r="8.5"/><circle cx="12" cy="12" r="4.5"/><circle cx="12" cy="12" r="1"/>',
    folder:'<path d="M3 7a2 2 0 0 1 2-2h4l2 2.5h8a2 2 0 0 1 2 2V18a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
    palette:'<path d="M12 3a9 9 0 1 0 0 18c1.4 0 2-1 2-2 0-1.6 1-2 2.2-2H18a3.5 3.5 0 0 0 3.5-3.5C21.5 7.5 17.2 3 12 3z"/><circle cx="7.5" cy="11" r="1"/><circle cx="10" cy="7.5" r="1"/><circle cx="14.5" cy="7.5" r="1"/>',
    camera:'<path d="M3 8.5A2 2 0 0 1 5 6.5h2.2l1.3-2h7l1.3 2H19a2 2 0 0 1 2 2V18a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><circle cx="12" cy="13" r="3.4"/>',
    image:'<rect x="3" y="4" width="18" height="16" rx="2"/><circle cx="8.5" cy="9.5" r="1.6"/><path d="M4 18l5.5-5.5L14 17l3-3 3 3"/>',
    plus:'<path d="M12 5v14M5 12h14"/>',
    minus:'<path d="M5 12h14"/>',
    close:'<path d="M6 6l12 12M18 6L6 18"/>',
    play:'<path d="M7 4.5v15l13-7.5z"/>',
    pause:'<path d="M8 4.5v15M16 4.5v15"/>',
    undo:'<path d="M4 10h11a5 5 0 0 1 0 10h-3"/><path d="M8 6l-4 4 4 4"/>',
    moon:'<path d="M20 14.5A8.5 8.5 0 0 1 9.5 4 8.5 8.5 0 1 0 20 14.5z"/>',
    sun:'<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M2 12h2M20 12h2M5 5l1.5 1.5M17.5 17.5 19 19M19 5l-1.5 1.5M6.5 17.5 5 19"/>',
    menu:'<path d="M4 7h16M4 12h16M4 17h16"/>',
    megaphone:'<path d="M4 10v4a1 1 0 0 0 1 1h3l7 4V5L8 9H5a1 1 0 0 0-1 1z"/><path d="M18.5 9.5a3.5 3.5 0 0 1 0 5"/>',
    ledger:'<path d="M5 3h13a1 1 0 0 1 1 1v17H6a1 1 0 0 1-1-1z"/><path d="M9 3v18M12 8h4M12 12h4"/>',
    flask:'<path d="M9 3h6M10 3v6L4.5 19a1.5 1.5 0 0 0 1.3 2h12.4a1.5 1.5 0 0 0 1.3-2L14 9V3"/><path d="M7.5 15h9"/>',
    recycle:'<path d="M7 19h10M12 4l3 5M12 4 9 9"/><path d="M5 14l2.5 5M19 14l-2.5 5"/>',
    file:'<path d="M14 3H7a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1V7z"/><path d="M14 3v4h4"/>'
  };

  var BY_EMOJI = {
    '📊':'dashboard', '💰':'rupee', '✅':'check', '🧾':'receipt', '💵':'cash',
    '📈':'trend', '⚡':'bolt', '📦':'box', '🏦':'bank', '💸':'refund',
    '🧮':'calculator', '👥':'users', '🛠':'wrench', '🚪':'door', '📋':'clipboard',
    '📤':'upload', '🔔':'bell', '📜':'scroll', '⚙':'gear', '🔐':'shield', '🏠':'home',
    '⏳':'hourglass', '👛':'wallet', '⚖':'scale',
    '🗑':'trash', '✏':'pencil', '✎':'pencil', '💾':'save', '🔍':'search',
    '👁':'eye', '💳':'card', '📎':'clip', '⬇':'download', '📥':'download',
    '⬆':'upload', '🏢':'building', '📅':'calendar', '📆':'calendar', '🗓':'calendar',
    '👤':'user', '🔑':'key', '🔒':'lock', '🔓':'unlock', '📱':'phone', '📞':'phone',
    '✉':'mail', '📍':'pin', '🎯':'target', '📂':'folder', '🗂':'folder',
    '🎨':'palette', '📷':'camera', '🖼':'image', '➕':'plus', '➖':'minus',
    '✖':'close', '❌':'close', '▶':'play', '⏸':'pause', '↩':'undo',
    '🌙':'moon', '☀':'sun', '☰':'menu', '📢':'megaphone', '📒':'ledger',
    '🧪':'flask', '♻':'recycle', '📄':'file', '📝':'file', '✔':'check', '🔧':'wrench'
  };

  // Emoji that carry meaning through colour or tone rather than shape, plus
  // anything that is content rather than chrome. Converting these would lose
  // information, so they are left alone.
  var KEEP = /[\u{1F7E0}-\u{1F7E5}\u{26AA}\u{26AB}\u{1F534}\u{1F535}\u{1F389}\u{26A0}\u{1F468}\u{1F469}\u{1F467}\u{1F466}\u{1F697}\u{1F44B}\u{00A9}]/u;

  // Only chrome is rewritten. Table cells and free text can contain emoji a
  // resident typed, and those must survive untouched.
  var CHROME = 'button,h1,h2,h3,h4,h5,label,th,.nav-sec,.card-hdr,.pill,.chip,.ic-btn,.btn,.empty';

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

  var PICTO = /\p{Extended_Pictographic}\uFE0F?/gu;

  function svgFor(name) {
    var s = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    s.setAttribute('class', 'ic ic-inline');
    s.setAttribute('aria-hidden', 'true');
    s.setAttribute('focusable', 'false');
    var u = document.createElementNS('http://www.w3.org/2000/svg', 'use');
    u.setAttribute('href', '#i-' + name);
    s.appendChild(u);
    return s;
  }

  // Rewrites emoji that sit inline with a label, e.g. "💾 Save Settings".
  function swapInline(el) {
    if (el.dataset.iconText) return;
    el.dataset.iconText = '1';

    var walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT, null);
    var nodes = [];
    while (walker.nextNode()) nodes.push(walker.currentNode);

    nodes.forEach(function (node) {
      var text = node.nodeValue;
      PICTO.lastIndex = 0;
      if (!PICTO.test(text)) return;
      PICTO.lastIndex = 0;

      var frag = document.createDocumentFragment();
      var last = 0, m, changed = false;
      while ((m = PICTO.exec(text)) !== null) {
        var raw = m[0];
        var name = KEEP.test(raw) ? null : BY_EMOJI[key(raw)];
        if (!name) continue;
        if (m.index > last) frag.appendChild(document.createTextNode(text.slice(last, m.index)));
        frag.appendChild(svgFor(name));
        last = m.index + raw.length;
        changed = true;
      }
      if (!changed) return;
      if (last < text.length) frag.appendChild(document.createTextNode(text.slice(last)));
      node.parentNode.replaceChild(frag, node);
    });
  }

  function scan(root) {
    root = root || document;
    root.querySelectorAll('.ni,.kpi-icon').forEach(swap);
    root.querySelectorAll(CHROME).forEach(swapInline);
  }

  function init() { sprite(); scan(); }

  document.addEventListener('DOMContentLoaded', init);
  if (document.readyState !== 'loading') init();

  new MutationObserver(function (recs) {
    recs.forEach(function (r) {
      Array.prototype.forEach.call(r.addedNodes, function (n) {
        if (n.nodeType !== 1) return;
        if (n.classList && (n.classList.contains('ni') || n.classList.contains('kpi-icon'))) swap(n);
        if (n.matches && n.matches(CHROME)) swapInline(n);
        scan(n);
      });
    });
  }).observe(document.documentElement, { childList: true, subtree: true });
})();
