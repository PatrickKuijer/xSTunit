// The page half of the WebView2 channel the WPF host
// (ResultsToolWindowControl.xaml.cs) talks to, and the renderer that turns a
// run result into #tree's rows.
//
// Inbound, the host calls four globals via ExecuteScriptAsync:
// xstunitBeginRun(), xstunitRenderResult(result), xstunitSetRunning(bool) and
// xstunitSetStatus(state, text). The result argument arrives as a literal JS
// expression, not a string to JSON.parse. Its wire shape is camelCase over
// TestRunner/XstunitModels.cs: { suites: [{ name, filePath, error,
// durationMs, callStack, tests: [{ name, passed, failures, durationMs }] }],
// passed, failed, exitCode, error }.
//
// Outbound, window.chrome.webview.postMessage carries two payload shapes: the
// bare strings 'run' and 'stop', and JSON envelopes ({ type: 'openFile',
// filePath } and { type: 'rerunFailed' }). The host tells the two apart by
// whether TryGetWebMessageAsString succeeds, so any message carrying data has
// to stay an envelope.
//
// Run state is host-driven throughout: the run button's label, #prog and
// #runningState change only when the host reports that a run actually started
// or ended, never optimistically on click, so they cannot desync from whether
// a xstunit process is really running. Every render replaces #tree's rows
// outright; there is no partial-update path.
(function () {
  'use strict';

  var treeEl = document.getElementById('tree');
  var emptyStateEl = document.getElementById('emptyState');
  var runningStateEl = document.getElementById('runningState');
  var runButton = document.getElementById('runButton');
  var rerunFailedButton = document.getElementById('rerunFailedButton');
  var progEl = document.getElementById('prog');
  var filterInput = document.getElementById('filterInput');
  var statusSeg = document.getElementById('statusSeg');
  var statusTextEl = document.getElementById('statusText');
  var statusDotEl = document.querySelector('.tw-statusbar .dot');

  // #rerunFailedButton is disabled for two independent reasons, and neither
  // alone is the whole rule: a passing run must disable it whatever the run
  // state, and a run in flight must disable it whatever the last result said.
  // One action is live at a time, the same rule #runButton/#prog follow.
  var hasFailures = false;
  var isRunning = false;

  // The failure-message format FB_TestSuite.Fail bakes in
  // (src/xStunit.Runner/TcUnitStub/FB_TestSuite.cs):
  //   FAILED TEST '<name>', EXP: <expected>, ACT: <actual>[, MSG: <message>]
  // The wire shape carries that as one free-text line, not structured
  // expected/actual fields, so this regex is how the .assert block recovers
  // them. A message in some other shape still renders, just as plain text.
  var FAILURE_PATTERN = /^FAILED TEST '[^']*', EXP: (.*?), ACT: (.*?)(?:, MSG: (.*))?$/;

  function el(tag, className) {
    var e = document.createElement(tag);
    if (className) {
      e.className = className;
    }
    return e;
  }

  function textEl(tag, className, value) {
    var e = el(tag, className);
    e.textContent = value;
    return e;
  }

  // No-op with no filePath (nothing to navigate to) or outside the WebView2
  // host, where nothing is listening on the other end of postMessage.
  function postOpenFile(filePath) {
    if (!filePath || !(window.chrome && window.chrome.webview)) {
      return;
    }
    window.chrome.webview.postMessage({ type: 'openFile', filePath: filePath });
  }

  // The shared "open" step a row's dblclick handler and the Enter key both
  // call, reading the filePath the row already carries on its own dataset
  // rather than re-deriving it from the suite/test data a second time.
  function openNodeFile(node) {
    if (node) {
      postOpenFile(node.dataset.filePath);
    }
  }

  // No "skipped" concept exists in the interpreter or the CLI: a test's status
  // is a plain bool. test.skipped is read defensively so this keeps working
  // unchanged if one ever ships, without inventing a status the data cannot
  // produce.
  function statusOf(test) {
    if (test && test.skipped) {
      return 'skip';
    }
    return test && test.passed ? 'pass' : 'fail';
  }

  // Returns null for anything that is not a genuine numeric duration -- the
  // "didn't run" case, which callers use to decide whether to draw a
  // .node-dur element at all -- rather than rendering "NaN ms".
  function formatDurationMs(durationMs) {
    if (typeof durationMs !== 'number' || !isFinite(durationMs)) {
      return null;
    }
    return durationMs + ' ms';
  }

  // Named so #rerunFailedButton's enable rule and renderSuite's glyph/status
  // choice cannot drift apart on what counts as a failed suite.
  function suiteFailed(suite) {
    if (suite && suite.error) {
      return true;
    }
    var tests = (suite && suite.tests) || [];
    return tests.some(function (t) { return statusOf(t) === 'fail'; });
  }

  function updateRerunFailedButton() {
    if (rerunFailedButton) {
      rerunFailedButton.disabled = !hasFailures || isRunning;
    }
  }

  function glyphFor(status) {
    switch (status) {
      case 'pass': return '✓';
      case 'fail': return '✗';
      case 'skip': return '–';
      default: return '●';
    }
  }

  function countSkipped(result) {
    var n = 0;
    var suites = (result && result.suites) || [];
    suites.forEach(function (suite) {
      var tests = (suite && suite.tests) || [];
      tests.forEach(function (test) {
        if (statusOf(test) === 'skip') {
          n++;
        }
      });
    });
    return n;
  }

  function setCount(id, kind, count) {
    var badge = document.getElementById(id);
    if (!badge) {
      return;
    }
    badge.className = 'count ' + (count > 0 ? kind : 'zero');
    var n = badge.querySelector('.n');
    if (n) {
      n.textContent = String(count);
    }
  }

  function updateCounts(result) {
    setCount('countPass', 'pass', (result && result.passed) || 0);
    setCount('countFail', 'fail', (result && result.failed) || 0);
    setCount('countSkip', 'skip', countSkipped(result));
  }

  // A failures[] entry is an object, never a bare string: the host renders
  // only JSON it has already deserialized into XstunitFailure. Its other keys
  // (kind, expected, actual, pou, method, bodyLine, ...) are for
  // non-interactive consumers; `message` alone is what the .assert block
  // parses.
  function failureText(failure) {
    return (failure && failure.message) || '';
  }

  function buildAssertBlock(message) {
    var div = el('div', 'assert');
    var match = FAILURE_PATTERN.exec(message || '');

    if (!match) {
      div.textContent = message || '';
      return div;
    }

    var expected = match[1];
    var actual = match[2];
    var extraMessage = match[3];

    div.appendChild(textEl('b', null, 'expected'));
    div.appendChild(document.createTextNode(' ' + expected + '  '));
    div.appendChild(textEl('b', null, 'actual'));
    div.appendChild(document.createTextNode(' ' + actual));

    if (extraMessage) {
      div.appendChild(document.createElement('br'));
      div.appendChild(document.createTextNode(extraMessage));
    }

    return div;
  }

  // "PouTypeName.MethodName(line)" when a method is known, "PouTypeName(line)"
  // for a suite-body or bare-FB frame, whose methodName is null by the wire
  // shape's own convention. The line preferred is bodyLine, the
  // XAE-implementation-editor-relative one, not the raw .TcPOU XML line --
  // the console output never labels a frame with the latter, so neither does
  // this. Falls back to the raw line, then drops the suffix entirely, as each
  // becomes unavailable.
  function callStackFrameLabel(frame) {
    var pou = (frame && frame.pouTypeName) || '?';
    var name = (frame && frame.methodName) ? pou + '.' + frame.methodName : pou;
    var line = (frame && typeof frame.bodyLine === 'number') ? frame.bodyLine
      : (frame && typeof frame.line === 'number') ? frame.line : null;
    return line === null ? name : name + '(' + line + ')';
  }

  // The expandable "call stack" section inside a failed-to-load suite's
  // banner, one row per suite.callStack frame. Frames stay in the CLI's own
  // order, innermost first, so callStack[0] describes the same fault as
  // suite.error. Returns null when the suite carries no frames -- the case
  // for a load-level failure that never entered an interpreted ST body -- so
  // the caller appends nothing rather than an empty section.
  function buildCallStackSection(suite) {
    var frames = (suite && suite.callStack) || [];
    if (frames.length === 0) {
      return null;
    }

    var section = el('div', 'callstack');
    var toggle = textEl('div', 'callstack-toggle', '▶ call stack (' + frames.length + ' frames)');
    var list = el('div', 'callstack-frames');
    list.hidden = true;

    var expanded = false;
    toggle.addEventListener('click', function () {
      expanded = !expanded;
      toggle.textContent = (expanded ? '▼' : '▶') + ' call stack (' + frames.length + ' frames)';
      list.hidden = !expanded;
    });

    frames.forEach(function (frame) {
      var frameRow = textEl('div', 'callstack-frame', callStackFrameLabel(frame));
      // A frame carries POU, method and line but no filePath, so the suite's
      // own file is the only navigation target this data can support.
      if (suite && suite.filePath) {
        frameRow.classList.add('has-open');
        frameRow.addEventListener('click', function () {
          postOpenFile(suite.filePath);
        });
      }
      list.appendChild(frameRow);
    });

    section.appendChild(toggle);
    section.appendChild(list);
    return section;
  }

  // A suite that failed to load reads as its own banner rather than being
  // swallowed as an empty suite. suite.error is the CLI's caught exception
  // message (unresolved type, parse error, and so on): free text, not HTML,
  // so it goes in via textContent -- there is no reliable way to locate the
  // inline <code> fragment the mockup draws.
  function buildBanner(suite) {
    var div = el('div', 'banner');
    div.appendChild(textEl('strong', null, 'Suite failed to load'));
    div.appendChild(document.createTextNode(((suite && suite.error) || '') + ' No tests were run.'));

    var callStackSection = buildCallStackSection(suite);
    if (callStackSection) {
      div.appendChild(callStackSection);
    }

    return div;
  }

  // suiteFilePath is the parent suite's filePath, or falsy: a leaf test has no
  // file of its own, so it inherits the suite's to have anywhere to navigate.
  function buildTestNode(test, suiteFilePath) {
    var status = statusOf(test);
    var classes = 'node depth1';
    if (status === 'fail' || status === 'skip') {
      classes += ' ' + status;
    }

    var node = el('div', classes);
    node.appendChild(textEl('span', 'glyph ' + status, glyphFor(status)));
    node.appendChild(textEl('span', 'node-name', (test && test.name) || ''));

    // A test that did not run gets no .node-dur slot at all rather than an
    // empty or placeholder one. A skipped test is that case by definition; a
    // pass/fail test always carries a durationMs, so formatDurationMs
    // returning null there means an unexpected payload, where omitting the
    // slot is still the right answer.
    var testDur = formatDurationMs(test && test.durationMs);
    if (status !== 'skip' && testDur !== null) {
      node.appendChild(textEl('span', 'node-dur', testDur));
    }

    // Only a failed test is worth a navigation target, and it gets no
    // .node-open arrow: the arrow opens the suite's file, not the test's own,
    // so drawing it on this row would be a false affordance. The pointer
    // cursor and the dblclick handler stay -- double-click still opens the
    // suite's file.
    if (status === 'fail' && suiteFilePath) {
      node.classList.add('has-open');
      node.dataset.filePath = suiteFilePath;
      node.addEventListener('dblclick', function () {
        openNodeFile(node);
      });
    }

    return node;
  }

  // Returns the flat sequence of rows to append to .tree: the suite header
  // node, then either a failed-to-load banner or the suite's test rows and
  // their assert blocks. The tree's DOM has no per-suite wrapper element, so
  // expand/collapse toggles `hidden` on each row directly rather than on a
  // container, and applyTextFilter has to regroup the sequence by shape.
  function renderSuite(suite) {
    var rows = [];
    var tests = (suite && suite.tests) || [];
    var hasError = !!(suite && suite.error);
    var anyFail = !hasError && tests.some(function (t) { return statusOf(t) === 'fail'; });
    var suiteStatus = hasError ? 'fail' : (anyFail ? 'fail' : 'pass');

    var suiteNode = el('div', 'node');
    var twisty = textEl('span', 'twisty', '▼');
    suiteNode.appendChild(twisty);
    suiteNode.appendChild(textEl('span', 'glyph ' + suiteStatus, glyphFor(suiteStatus)));
    suiteNode.appendChild(textEl('span', 'node-name', (suite && suite.name) || ''));

    // A suite that failed to load never ran, so the same "didn't run" rule as
    // a skipped test applies: no .node-dur slot at all. The CLI agrees,
    // emitting a null durationMs for exactly this case.
    var suiteDur = formatDurationMs(suite && suite.durationMs);
    if (!hasError && suiteDur !== null) {
      suiteNode.appendChild(textEl('span', 'node-dur', suiteDur));
    }

    if (suite && suite.filePath) {
      suiteNode.appendChild(textEl('span', 'node-open', '↗'));
      suiteNode.classList.add('has-open');
      suiteNode.dataset.filePath = suite.filePath;
      suiteNode.addEventListener('dblclick', function () {
        openNodeFile(suiteNode);
      });
    }

    rows.push(suiteNode);

    // The path gets its own row below the suite name rather than sitting
    // inline beside it, where a long absolute path overlaps the name. It joins
    // childRows so it collapses with the suite instead of always costing a
    // line.
    var childRows = [];
    if (suite && suite.filePath) {
      var srcRow = el('div', 'node-src-row');
      srcRow.appendChild(textEl('span', 'node-src', suite.filePath));
      rows.push(srcRow);
      childRows.push(srcRow);
    }

    if (hasError) {
      var bannerRow = buildBanner(suite);
      rows.push(bannerRow);
      childRows.push(bannerRow);

      // A failed-to-load suite collapses like any other, on the same twisty
      // wiring a passing suite gets below -- it just gates the path row and
      // banner instead of test/assert rows.
      var errorExpanded = true;
      function toggleErrorExpand() {
        errorExpanded = !errorExpanded;
        twisty.textContent = errorExpanded ? '▼' : '▶';
        childRows.forEach(function (rowEl) {
          rowEl.hidden = !errorExpanded;
        });
      }
      twisty.addEventListener('click', toggleErrorExpand);
      suiteNode.toggleExpand = toggleErrorExpand;

      return rows;
    }

    var suiteFilePath = suite && suite.filePath;
    tests.forEach(function (test) {
      var testNode = buildTestNode(test, suiteFilePath);
      rows.push(testNode);
      childRows.push(testNode);

      if (statusOf(test) === 'fail') {
        var failures = (test && test.failures) || [];
        failures.forEach(function (failure) {
          var assertNode = buildAssertBlock(failureText(failure));
          rows.push(assertNode);
          childRows.push(assertNode);
        });
      }
    });

    // Named and stashed on suiteNode rather than passed inline to
    // addEventListener, so the Space-key handler can call the identical
    // function a twisty click does instead of duplicating expand/collapse.
    var expanded = true;
    function toggleExpand() {
      expanded = !expanded;
      twisty.textContent = expanded ? '▼' : '▶';
      childRows.forEach(function (rowEl) {
        rowEl.hidden = !expanded;
      });
    }
    twisty.addEventListener('click', toggleExpand);
    suiteNode.toggleExpand = toggleExpand;

    return rows;
  }

  function normalize(text) {
    return (text || '').toLowerCase();
  }

  function nameOf(rowEl) {
    var nameEl = rowEl && rowEl.querySelector('.node-name');
    return normalize(nameEl && nameEl.textContent);
  }

  // The segmented control's current value: 'fail', 'skip', or null for "All".
  // Held here rather than read back off #tree's filter-fail/filter-skip class,
  // since setStatusFilter is the one place that already knows it.
  var statusFilter = null;

  // Filters the already-rendered .tree by name, toggling `hidden` per row
  // rather than touching the DOM structure or re-rendering from JSON. It runs
  // on every keystroke undebounced: these trees are a handful of rows.
  //
  // Because .tree is a flat row sequence, the walk has to regroup rows into
  // suite-sized units by their classes to decide visibility. A suite header
  // (and its banner) stays visible when its own name matches or any of its
  // tests do, so a matching leaf never loses its suite context and a matching
  // suite shows all its tests.
  //
  // This composes with the segmented status control (results.css's
  // .tree.filter-fail/-skip) only by both being independently necessary for a
  // row to be visible; no interaction between the two is coded anywhere.
  function applyTextFilter() {
    if (!treeEl) {
      return;
    }

    var query = normalize(filterInput && filterInput.value);
    var rows = Array.prototype.slice.call(treeEl.children);
    var i = 0;

    while (i < rows.length) {
      var suiteRow = rows[i];
      i++;

      if (!suiteRow.classList || !suiteRow.classList.contains('node') || suiteRow.classList.contains('depth1')) {
        // Not a suite header, which renderSuite's output shape rules out --
        // skip rather than misclassify the row or loop forever on it.
        continue;
      }

      var suiteMatches = query === '' || nameOf(suiteRow).indexOf(query) !== -1;

      var srcRow = null;
      if (i < rows.length && rows[i].classList.contains('node-src-row')) {
        srcRow = rows[i];
        i++;
      }

      var testEntries = [];
      var anyTestMatches = false;

      while (i < rows.length && rows[i].classList.contains('depth1')) {
        var testRow = rows[i];
        i++;
        var testMatches = query === '' || nameOf(testRow).indexOf(query) !== -1;
        if (testMatches) {
          anyTestMatches = true;
        }

        var assertRows = [];
        while (i < rows.length && rows[i].classList.contains('assert')) {
          assertRows.push(rows[i]);
          i++;
        }

        testEntries.push({ row: testRow, matches: testMatches, asserts: assertRows });
      }

      var bannerRow = null;
      if (i < rows.length && rows[i].classList.contains('banner')) {
        bannerRow = rows[i];
        i++;
      }

      // results.css's status rules prune only .node.depth1 rows, so that a
      // matching leaf keeps its suite context -- which on its own would leave
      // an all-passing suite showing an empty header under "Failed". A suite
      // earns visibility under an active status filter only if it actually
      // holds a row of that status: a load error or a failing test for
      // "fail", a skipped test for "skip".
      var suiteHasError = !!bannerRow;
      var statusOk = true;
      if (statusFilter === 'fail') {
        statusOk = suiteHasError || testEntries.some(function (e) { return e.row.classList.contains('fail'); });
      } else if (statusFilter === 'skip') {
        statusOk = testEntries.some(function (e) { return e.row.classList.contains('skip'); });
      }

      var suiteVisible = (suiteMatches || anyTestMatches) && statusOk;
      suiteRow.hidden = !suiteVisible;
      if (srcRow) {
        srcRow.hidden = !suiteVisible;
      }
      if (bannerRow) {
        bannerRow.hidden = !suiteVisible;
      }

      testEntries.forEach(function (entry) {
        var testVisible = suiteMatches || entry.matches;
        entry.row.hidden = !testVisible;
        entry.asserts.forEach(function (assertRow) {
          assertRow.hidden = !testVisible;
        });
      });
    }
  }

  // The All/Failed/Skipped control hides nothing itself: it only toggles a
  // class on #tree, and results.css's "status segmented control" selectors do
  // the hide/show. Nothing has to be redone on a later render, since #tree
  // itself survives every render -- only its children are replaced.
  function setStatusFilter(status) {
    statusFilter = (status === 'fail' || status === 'skip') ? status : null;
    if (!treeEl) {
      return;
    }
    treeEl.classList.remove('filter-fail', 'filter-skip');
    if (statusFilter) {
      treeEl.classList.add('filter-' + statusFilter);
    }
    // Suite header visibility depends on statusFilter too, and a segment click
    // is the one path that changes it without going through the text-input or
    // render paths that already rerun the filter.
    applyTextFilter();
  }

  var selectedRow = null;

  // The rows keyboard nav may land on: every .node actually rendered visible.
  // offsetParent is the test rather than a hand-rolled check of `hidden` plus
  // the segmented control's class, which would mean keeping a second copy of
  // results.css's hide rules in sync here. It reads null for both filtering
  // mechanisms without this file knowing which one applied.
  function visibleRows() {
    if (!treeEl) {
      return [];
    }
    var rows = Array.prototype.slice.call(treeEl.querySelectorAll('.node'));
    return rows.filter(function (row) {
      return !row.hidden && row.offsetParent !== null;
    });
  }

  function setSelectedRow(row) {
    if (selectedRow) {
      selectedRow.classList.remove('selected');
    }
    selectedRow = row || null;
    if (selectedRow) {
      selectedRow.classList.add('selected');
      if (typeof selectedRow.scrollIntoView === 'function') {
        selectedRow.scrollIntoView({ block: 'nearest' });
      }
    }
  }

  // direction is +1 for ArrowDown, -1 for ArrowUp. Clamps at the ends rather
  // than wrapping. A selection that is not in the current visible set -- never
  // set, or filtered or rerendered away since -- restarts at the first visible
  // row either way, rather than guessing an offset from a stale position.
  function moveSelection(direction) {
    var rows = visibleRows();
    if (rows.length === 0) {
      setSelectedRow(null);
      return;
    }

    var currentIndex = selectedRow ? rows.indexOf(selectedRow) : -1;
    var nextIndex;
    if (currentIndex === -1) {
      nextIndex = 0;
    } else {
      nextIndex = currentIndex + direction;
      if (nextIndex < 0) {
        nextIndex = 0;
      } else if (nextIndex >= rows.length) {
        nextIndex = rows.length - 1;
      }
    }

    setSelectedRow(rows[nextIndex]);
  }

  // #tree is the one tabbable element on the page (tabindex="0" in
  // results.html), carrying a roving selection tracked in JS rather than real
  // per-row DOM focus. Enter and Space act only where they would do something
  // -- an open target, a toggleExpand -- mirroring how the equivalent mouse
  // paths are themselves wired only onto rows that support them.
  if (treeEl) {
    treeEl.addEventListener('keydown', function (event) {
      switch (event.key) {
        case 'ArrowDown':
          event.preventDefault();
          moveSelection(1);
          break;
        case 'ArrowUp':
          event.preventDefault();
          moveSelection(-1);
          break;
        case 'Enter':
          if (selectedRow && selectedRow.classList.contains('has-open')) {
            event.preventDefault();
            openNodeFile(selectedRow);
          }
          break;
        case ' ':
        case 'Spacebar': // legacy IE/Edge key name for the space bar
          if (selectedRow && typeof selectedRow.toggleExpand === 'function') {
            event.preventDefault();
            selectedRow.toggleExpand();
          }
          break;
        default:
          break;
      }
    });
  }

  if (filterInput) {
    filterInput.addEventListener('input', applyTextFilter);
  }

  // A segment carries two attributes on purpose: data-v drives vsix-shell.css's
  // active-button highlight, data-status is read only here, so the filter never
  // depends on visual state.
  if (statusSeg) {
    var segButtons = Array.prototype.slice.call(statusSeg.querySelectorAll('button'));
    segButtons.forEach(function (btn) {
      btn.addEventListener('click', function () {
        segButtons.forEach(function (b) { b.classList.remove('active'); });
        btn.classList.add('active');
        setStatusFilter(btn.getAttribute('data-status'));
      });
    });
  }

  window.xstunitRenderResult = function (result) {
    if (!treeEl) {
      return;
    }

    while (treeEl.firstChild) {
      treeEl.removeChild(treeEl.firstChild);
    }

    // The rows above are gone, so any held selection is now a detached node.
    setSelectedRow(null);

    var suites = (result && result.suites) || [];
    suites.forEach(function (suite) {
      renderSuite(suite).forEach(function (rowEl) {
        treeEl.appendChild(rowEl);
      });
    });

    updateCounts(result || {});

    hasFailures = suites.some(suiteFailed);
    updateRerunFailedButton();

    // The status filter's class rides on #tree, which survives this render, so
    // only the text filter's per-row hidden state needs recomputing against
    // the fresh rows -- a filter typed before a rerun still applies after it.
    applyTextFilter();

    if (runningStateEl) {
      runningStateEl.hidden = true;
    }
    if (emptyStateEl) {
      emptyStateEl.hidden = true;
    }
    treeEl.hidden = false;
  };

  // Called right before a run's CLI process starts, so the panel can never
  // show a prior run's rows alongside a new run in flight -- a stale tree left
  // up for the duration of a run is indistinguishable from a fresh one.
  // hasFailures resets with the counts: nothing is on screen for
  // #rerunFailedButton to rerun until the next render sets it again.
  window.xstunitBeginRun = function () {
    if (!treeEl) {
      return;
    }

    while (treeEl.firstChild) {
      treeEl.removeChild(treeEl.firstChild);
    }
    setSelectedRow(null);

    updateCounts({});
    hasFailures = false;
    updateRerunFailedButton();

    if (emptyStateEl) {
      emptyStateEl.hidden = true;
    }
    treeEl.hidden = true;
    if (runningStateEl) {
      runningStateEl.hidden = false;
    }
  };

  // Drives the .tw-statusbar dot and #statusText. state is 'running',
  // 'stopped' or 'error', each colored by the matching class in results.css;
  // anything else, 'ready' included, leaves the dot in its plain class-less
  // state, which is already the ready color in markup.
  window.xstunitSetStatus = function (state, text) {
    if (statusDotEl) {
      statusDotEl.classList.remove('running', 'stopped', 'error');
      if (state === 'running' || state === 'stopped' || state === 'error') {
        statusDotEl.classList.add(state);
      }
    }
    if (statusTextEl) {
      statusTextEl.textContent = text || '';
    }
  };

  window.xstunitSetRunning = function (running) {
    isRunning = !!running;

    if (runButton) {
      runButton.textContent = running ? 'Stop' : 'Run tests';
      runButton.classList.toggle('stop-btn', !!running);
    }
    if (progEl) {
      progEl.hidden = !running;
    }

    // A run that ends without ever rendering -- Stop, or a host-level error --
    // would otherwise freeze the "Running..." placeholder on screen for a run
    // that is no longer running. #emptyState is the honest replacement, since
    // xstunitBeginRun already cleared the tree and there is nothing to show. A
    // run that did render hid the placeholder itself, so this is a no-op then.
    if (!running && runningStateEl && !runningStateEl.hidden) {
      runningStateEl.hidden = true;
      if (emptyStateEl) {
        emptyStateEl.hidden = false;
      }
    }

    updateRerunFailedButton();
  };

  if (runButton) {
    runButton.addEventListener('click', function () {
      if (!(window.chrome && window.chrome.webview)) {
        // Opened outside the VS WebView2 host, e.g. straight in a browser for
        // a visual check: there is nothing to post to, and nothing would ever
        // call xstunitSetRunning back to undo an optimistic label flip.
        return;
      }
      var running = runButton.classList.contains('stop-btn');
      window.chrome.webview.postMessage(running ? 'stop' : 'run');
    });
  }

  // The rerun request carries no payload: the host, not this page, holds the
  // list of suites that failed last run. The `disabled` attribute is the only
  // guard needed, since a disabled button fires no click events.
  if (rerunFailedButton) {
    rerunFailedButton.addEventListener('click', function () {
      if (!(window.chrome && window.chrome.webview)) {
        return;
      }
      window.chrome.webview.postMessage({ type: 'rerunFailed' });
    });
  }
})();
