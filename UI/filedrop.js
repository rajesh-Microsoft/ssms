/* Upgrades plain <input type="file"> into a drag-and-drop picker.
   Purely additive: the original input stays in the DOM with the same id, so existing
   code that reads .files or assigns .value keeps working untouched. */
(function () {
  'use strict';

  var ACCEPT_LABELS = [
    [/^image\//, 'Images'],
    [/pdf$/, 'PDF'],
    [/(csv|ms-excel|spreadsheetml)/, 'Spreadsheets']
  ];

  function iconFor(accept) {
    if (/^image\//.test(accept)) return '📷';
    if (/(csv|excel|spreadsheetml)/.test(accept)) return '📊';
    if (/pdf/.test(accept)) return '📄';
    return '📎';
  }

  function hintFor(input) {
    var accept = input.getAttribute('accept');
    if (!accept) return 'Any file type';
    var seen = [];
    accept.split(',').forEach(function (raw) {
      var token = raw.trim().toLowerCase();
      ACCEPT_LABELS.forEach(function (pair) {
        if (pair[0].test(token) && seen.indexOf(pair[1]) === -1) seen.push(pair[1]);
      });
    });
    return (seen.length ? seen.join(' or ') : 'Any file type') +
      (input.multiple ? ' · you can pick several' : '');
  }

  function prettySize(bytes) {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return Math.round(bytes / 1024) + ' KB';
    return (bytes / 1024 / 1024).toFixed(1) + ' MB';
  }

  // Dropping and removing both need to rewrite the selection, which is only
  // possible by handing the input a fresh FileList built from a DataTransfer.
  function assign(input, files) {
    var dt = new DataTransfer();
    files.forEach(function (f) { dt.items.add(f); });
    input.files = dt.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));
  }

  function enhance(input) {
    if (input.dataset.fdReady || input.hidden || input.closest('.fd')) return;
    input.dataset.fdReady = '1';

    var wrap = document.createElement('div');
    wrap.className = 'fd';
    wrap.tabIndex = 0;
    wrap.setAttribute('role', 'button');
    wrap.setAttribute('aria-label', 'Choose a file');
    if (input.getAttribute('style')) {
      wrap.setAttribute('style', input.getAttribute('style'));
      input.removeAttribute('style');
    }

    input.parentNode.insertBefore(wrap, input);
    wrap.appendChild(input);

    var verb = input.multiple ? 'files' : 'a file';
    var accept = (input.getAttribute('accept') || '').toLowerCase();
    // capture= opens the camera on a phone, where "drag it here" means nothing.
    var title = input.hasAttribute('capture')
      ? '<b>Take a photo</b> or choose one'
      : '<b>Choose ' + verb + '</b> or drag ' + (input.multiple ? 'them' : 'it') + ' here';

    wrap.insertAdjacentHTML('beforeend',
      '<div class="fd-cue">' +
        '<span class="fd-ico">' + iconFor(accept) + '</span>' +
        '<span class="fd-copy">' +
          '<span class="fd-title">' + title + '</span>' +
          '<span class="fd-hint">' + hintFor(input) + '</span>' +
        '</span>' +
      '</div>' +
      '<ul class="fd-files"></ul>');

    var list = wrap.querySelector('.fd-files');

    function render() {
      var files = input.files ? Array.prototype.slice.call(input.files) : [];
      list.innerHTML = files.map(function (f, i) {
        return '<li class="fd-file">' +
          '<span class="fd-name"></span>' +
          '<span class="fd-size">' + prettySize(f.size) + '</span>' +
          '<button type="button" class="fd-x" data-i="' + i + '" title="Remove">×</button>' +
        '</li>';
      }).join('');
      // Filenames are user-supplied, so they are set as text rather than markup.
      list.querySelectorAll('.fd-name').forEach(function (el, i) {
        el.textContent = files[i].name;
      });
      wrap.setAttribute('aria-label',
        files.length ? files.length + ' file(s) selected' : 'Choose a file');
    }

    input.addEventListener('change', render);

    // Several callers clear the picker with .value = '', which fires no event.
    var proto = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
    Object.defineProperty(input, 'value', {
      configurable: true,
      get: function () { return proto.get.call(this); },
      set: function (v) { proto.set.call(this, v); render(); }
    });

    wrap.addEventListener('click', function (e) {
      var x = e.target.closest('.fd-x');
      if (x) {
        var keep = Array.prototype.slice.call(input.files);
        keep.splice(parseInt(x.dataset.i, 10), 1);
        assign(input, keep);
        return;
      }
      if (e.target !== input) input.click();
    });

    wrap.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); input.click(); }
    });

    ['dragenter', 'dragover'].forEach(function (evt) {
      wrap.addEventListener(evt, function (e) {
        e.preventDefault(); wrap.classList.add('is-drag');
      });
    });
    ['dragleave', 'drop'].forEach(function (evt) {
      wrap.addEventListener(evt, function (e) {
        if (evt === 'dragleave' && wrap.contains(e.relatedTarget)) return;
        wrap.classList.remove('is-drag');
      });
    });

    wrap.addEventListener('drop', function (e) {
      e.preventDefault();
      var dropped = Array.prototype.slice.call(e.dataTransfer.files);
      if (!dropped.length) return;
      assign(input, input.multiple
        ? Array.prototype.slice.call(input.files).concat(dropped)
        : [dropped[0]]);
    });

    render();
  }

  function scan() {
    document.querySelectorAll('input[type="file"]').forEach(enhance);
  }

  document.addEventListener('DOMContentLoaded', scan);
  if (document.readyState !== 'loading') scan();

  // Tabs load their markup from partials well after DOMContentLoaded. Only the added
  // subtrees are searched, because the tables here re-render on almost every action.
  new MutationObserver(function (records) {
    records.forEach(function (r) {
      Array.prototype.forEach.call(r.addedNodes, function (node) {
        if (node.nodeType !== 1) return;
        if (node.matches('input[type="file"]')) enhance(node);
        else node.querySelectorAll('input[type="file"]').forEach(enhance);
      });
    });
  }).observe(document.documentElement, { childList: true, subtree: true });
})();
