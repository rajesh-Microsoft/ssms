/* ============================================================
   Confirm dialog + focus management for the existing modals.

   smmsConfirm() returns a Promise<boolean>, so callers must await it —
   window.confirm() blocks the thread and this cannot.
   ============================================================ */
(function () {
  'use strict';

  var FOCUSABLE = 'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),' +
                  'textarea:not([disabled]),[tabindex]:not([tabindex="-1"])';

  function trap(box, e) {
    if (e.key !== 'Tab') return;
    var f = Array.prototype.filter.call(box.querySelectorAll(FOCUSABLE), function (el) {
      return el.offsetParent !== null || el === document.activeElement;
    });
    if (!f.length) return;
    var first = f[0], last = f[f.length - 1];
    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  }

  var ICONS = {
    danger: '<svg viewBox="0 0 24 24"><path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"/><path d="M12 9v4M12 17h.01"/></svg>',
    question: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/><path d="M9.5 9.5a2.5 2.5 0 1 1 3.2 2.4c-.6.2-.7.6-.7 1.1v.5M12 17h.01"/></svg>'
  };

  window.smmsConfirm = function (message, opts) {
    opts = opts || {};
    var danger = opts.danger !== false;   // most callers are deletions

    return new Promise(function (resolve) {
      var opener = document.activeElement;

      var overlay = document.createElement('div');
      overlay.className = 'dlg-overlay';
      overlay.innerHTML =
        '<div class="dlg" role="alertdialog" aria-modal="true" aria-labelledby="dlg-t" aria-describedby="dlg-d">' +
          '<div class="dlg-top">' +
            '<span class="dlg-ic ' + (danger ? 'is-danger' : 'is-ask') + '">' + (danger ? ICONS.danger : ICONS.question) + '</span>' +
            '<div class="dlg-copy">' +
              '<h3 class="dlg-title" id="dlg-t"></h3>' +
              '<p class="dlg-msg" id="dlg-d"></p>' +
            '</div>' +
          '</div>' +
          '<div class="dlg-actions">' +
            '<button type="button" class="btn btn-ghost dlg-no"></button>' +
            '<button type="button" class="btn ' + (danger ? 'btn-danger' : 'btn-primary') + ' dlg-yes"></button>' +
          '</div>' +
        '</div>';
      document.body.appendChild(overlay);

      var box = overlay.querySelector('.dlg');
      box.querySelector('.dlg-title').textContent = opts.title || (danger ? 'Are you sure?' : 'Please confirm');
      box.querySelector('.dlg-msg').textContent = message || '';
      var no = box.querySelector('.dlg-no'), yes = box.querySelector('.dlg-yes');
      no.textContent = opts.cancelText || 'Cancel';
      yes.textContent = opts.confirmText || (danger ? 'Delete' : 'Confirm');

      function done(result) {
        overlay.classList.add('is-out');
        setTimeout(function () { overlay.remove(); }, 120);
        document.removeEventListener('keydown', onKey, true);
        if (opener && opener.focus) opener.focus();
        resolve(result);
      }
      function onKey(e) {
        if (e.key === 'Escape') { e.preventDefault(); done(false); }
        else trap(box, e);
      }

      no.addEventListener('click', function () { done(false); });
      yes.addEventListener('click', function () { done(true); });
      overlay.addEventListener('mousedown', function (e) { if (e.target === overlay) done(false); });
      document.addEventListener('keydown', onKey, true);

      // Cancel is focused so Enter cannot destroy something by reflex.
      no.focus();
    });
  };

  /* ── focus handling for the app's existing modals ───────── */
  var openBoxes = [];

  function onModalKey(e) {
    var top = openBoxes[openBoxes.length - 1];
    if (!top) return;
    if (e.key === 'Escape') {
      var close = top.querySelector('.modal-close');
      if (close) { e.preventDefault(); close.click(); }
    } else {
      trap(top, e);
    }
  }

  function watch(overlay) {
    if (overlay.dataset.dlgWatched) return;
    overlay.dataset.dlgWatched = '1';
    new MutationObserver(function () {
      var open = overlay.classList.contains('open');
      var box = overlay.querySelector('.modal') || overlay;
      var i = openBoxes.indexOf(box);
      if (open && i === -1) {
        openBoxes.push(box);
        overlay._opener = document.activeElement;
        var first = box.querySelector('input:not([type=hidden]):not([disabled]),select,textarea,.xs-trigger');
        if (first) setTimeout(function () { first.focus(); }, 40);
      } else if (!open && i > -1) {
        openBoxes.splice(i, 1);
        if (overlay._opener && overlay._opener.focus) overlay._opener.focus();
      }
    }).observe(overlay, { attributes: true, attributeFilter: ['class'] });
  }

  function scan() { document.querySelectorAll('.modal-overlay').forEach(watch); }

  function init() {
    scan();
    document.addEventListener('keydown', onModalKey, true);
  }
  document.addEventListener('DOMContentLoaded', init);
  if (document.readyState !== 'loading') init();

  new MutationObserver(function (recs) {
    recs.forEach(function (r) {
      Array.prototype.forEach.call(r.addedNodes, function (n) {
        if (n.nodeType !== 1) return;
        if (n.classList && n.classList.contains('modal-overlay')) watch(n);
        else if (n.querySelectorAll) n.querySelectorAll('.modal-overlay').forEach(watch);
      });
    });
  }).observe(document.documentElement, { childList: true, subtree: true });
})();
