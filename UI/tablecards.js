/* Labels each cell with its column heading so tables can be restacked as cards
   on narrow screens. The app has ~49 tables with different columns, so the
   labels are read from each table's own <thead> rather than hardcoded. */
(function () {
  'use strict';

  function label(table) {
    var heads = table.querySelectorAll('thead th');
    if (!heads.length) return;
    var names = Array.prototype.map.call(heads, function (th) {
      return th.textContent.replace(/[\u2191\u2193\u2195]/g, '').trim();
    });

    table.querySelectorAll('tbody tr').forEach(function (tr) {
      var cells = tr.children;
      // Empty-state and grouping rows span the table and must stay as they are.
      if (cells.length === 1 && cells[0].hasAttribute('colspan')) {
        tr.dataset.tcSpan = '1';
        return;
      }
      for (var i = 0; i < cells.length; i++) {
        var name = names[i] || '';
        if (name && !cells[i].dataset.tcLabel) cells[i].dataset.tcLabel = name;
      }
    });
  }

  function scan(root) {
    (root || document).querySelectorAll('table').forEach(label);
  }

  function init() { scan(); }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();

  // Tables are re-rendered wholesale on nearly every filter change.
  var queued = false;
  new MutationObserver(function () {
    if (queued) return;
    queued = true;
    requestAnimationFrame(function () { queued = false; scan(); });
  }).observe(document.documentElement, { childList: true, subtree: true });
})();
