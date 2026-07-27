// TcXunit-1tt.2: static results tree render (node anatomy, banner, assert
// detail). Builds the .tree DOM from a TcxunitRunResult (see
// src/TcXunit.Vsix/TestRunner/TcxunitModels.cs for the C# shape; wire JSON is
// camelCase: { suites: [{ name, filePath, error, tests: [{ name, passed,
// failures }] }], passed, failed, exitCode, error } per
// docs/design-system.html section 8's "Data" rule).
//
// window.tcxunitRenderResult(result) is called by
// ResultsToolWindowControl.xaml.cs's StartRunAsync via ExecuteScriptAsync,
// passing the CLI's own JSON output as a literal JS expression (not a string
// to JSON.parse -- see BuildRenderResultScript in that file).
//
// TcXunit-1tt.3 adds the other half of the run lifecycle: #runButton posts
// 'run'/'stop' strings to the WPF host over window.chrome.webview.postMessage
// (CoreWebView2.WebMessageReceived on the host side), and the host calls
// window.tcxunitSetRunning(bool) back in once the run actually starts/ends --
// the button's label/class and #prog's visibility are host-driven, not
// optimistically flipped on click, so they can never desync from whether a
// tcxunit process is actually running.
//
// TcXunit-1tt.4 adds click-to-navigate: a suite row's .node-open (and, per
// the epic's explicit design decision, a failed leaf test row's -- it has no
// file of its own, so it inherits its parent suite's filePath) posts
// {type:'openFile', filePath} to the WPF host over the same
// window.chrome.webview.postMessage channel TcXunit-1tt.3 uses for 'run'/
// 'stop', on double-click (design-system.html section 5: "the double-click
// target hint" / section 8's Data rule: "filePath is what
// ItemOperations.OpenFile receives on double-click"). Passing/skipped leaf
// rows never get a .node-open span (see buildTestNode below), so they have
// nothing to wire a handler onto -- satisfies "do not attempt navigation"
// without a separate guard.
//
// TcXunit-1tt.5 adds the filter box (#filterInput, a .field per
// design-system.html section 5) and the All/Failed/Skipped segmented control
// (#statusSeg, a .seg). These are two independent, composable mechanisms, not
// one:
//   - The segmented control toggles a "filter-fail"/"filter-skip" class on
//     #tree itself (see setStatusFilter below) -- results.css keys its
//     .tree.filter-fail/.tree.filter-skip selectors off that class and does
//     the actual hide/show in CSS, per the epic's own wording ("the
//     segmented control toggles a status class on .tree"). No JS walk of the
//     tree needed for it, and nothing to redo on a later render -- the class
//     lives on #tree, which a rerun never replaces (only its children).
//   - The text filter (applyTextFilter) is pure per-row `hidden` toggling
//     over the already-rendered DOM, re-run on every keystroke -- no
//     debounce; these trees are a handful of suites/tests, not worth the
//     complexity. It groups the tree's flat row sequence back into
//     suite/test/assert units (mirroring renderSuite's own output shape) to
//     decide, per row, whether the suite name or the row's own name matches.
// Both apply to the same rows at once (a status-filtered-out row's CSS
// display:none and a text-filtered-out row's hidden attribute are
// independent effects -- a row needs neither to be visible).
//
// Not implemented here (separate sibling tickets under TcXunit-1tt):
// rerun-failed, keyboard nav, real .node-dur values (always "--" for a row
// that ran; omitted for a row that didn't -- see design-system.html section
// 5's node-anatomy table), per-node "currently executing" state (the CLI
// emits one JSON blob at the end of a run, not an incremental stream, so
// there is no data to know which suite is currently executing -- only that a
// run is or isn't in flight). Plain script (no ES modules) since this page
// has exactly one small render concern, unlike TcAgent's chat.html.
(function () {
  'use strict';

  var treeEl = document.getElementById('tree');
  var emptyStateEl = document.getElementById('emptyState');
  var runButton = document.getElementById('runButton');
  var progEl = document.getElementById('prog');
  var filterInput = document.getElementById('filterInput');
  var statusSeg = document.getElementById('statusSeg');

  // FB_TestSuite.Fail()'s baked failure-message format (see
  // src/TcXunit.Runner/TcUnitStub/FB_TestSuite.cs):
  //   FAILED TEST '<name>', EXP: <expected>, ACT: <actual>[, MSG: <message>]
  // TcxunitTestResult.Failures is free text, not structured expected/actual
  // fields -- this regex is how the .assert block recovers them. A failure
  // message that doesn't match (e.g. some future/foreign shape) still
  // renders, just as plain text instead of bolded expected/actual.
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

  // Click-to-navigate (TcXunit-1tt.4): posts a JSON envelope, not a plain
  // string -- unlike 'run'/'stop' (TcXunit-1tt.3) this needs to carry data.
  // OnWebMessageReceived (ResultsToolWindowControl.xaml.cs) distinguishes the
  // two by trying TryGetWebMessageAsString first and falling back to parsing
  // WebMessageAsJson, mirroring the envelope shape TcAgentPlugin's
  // Browser_WebMessageReceived already uses for its own object messages.
  // No-op with no filePath (nothing to navigate to) or outside the WebView2
  // host (same guard #runButton's click handler uses below).
  function postOpenFile(filePath) {
    if (!filePath || !(window.chrome && window.chrome.webview)) {
      return;
    }
    window.chrome.webview.postMessage({ type: 'openFile', filePath: filePath });
  }

  // No "skipped" concept exists in the interpreter/CLI today (TestResult.Passed
  // is a plain bool) -- test.skipped is read defensively so this keeps working
  // unchanged if that ever ships, without inventing a status the data can't
  // produce yet.
  function statusOf(test) {
    if (test && test.skipped) {
      return 'skip';
    }
    return test && test.passed ? 'pass' : 'fail';
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

  // Renders one failure message as an .assert block. Bolds expected/actual
  // when the message matches FB_TestSuite's known format; otherwise falls
  // back to the raw text -- "with expected/actual if present in the JSON"
  // per TcXunit-1tt.2's acceptance criteria, not a guarantee every failure
  // parses that way.
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

  // Suite-failed-to-load banner: distinct from a swallowed empty suite, per
  // TcXunit-1tt.2's acceptance criteria. suite.error is CliRunner's caught
  // exception message (unresolved type, parse error, etc.) -- free text, not
  // HTML, so it's set via textContent even though the mockup shows a <code>
  // fragment inline; this data has no reliable way to locate that substring.
  function buildBanner(errorMessage) {
    var div = el('div', 'banner');
    div.appendChild(textEl('strong', null, 'Suite failed to load'));
    div.appendChild(document.createTextNode((errorMessage || '') + ' No tests were run.'));
    return div;
  }

  // suiteFilePath is the parent suite's filePath (or falsy) -- a failed leaf
  // test has no file of its own (out of scope per the epic: "no per-test file
  // granularity exists"), so it inherits the suite's for navigation purposes.
  function buildTestNode(test, suiteFilePath) {
    var status = statusOf(test);
    var classes = 'node depth1';
    if (status === 'fail' || status === 'skip') {
      classes += ' ' + status;
    }

    var node = el('div', classes);
    node.appendChild(textEl('span', 'glyph ' + status, glyphFor(status)));
    node.appendChild(textEl('span', 'node-name', (test && test.name) || ''));

    // .node-dur: "Omitted entirely when a test didn't run" (design-system.html
    // section 5) -- a skipped test never ran, so it gets no slot at all, not a
    // "--" placeholder. A pass/fail test did run; its duration just isn't
    // wired yet (TcXunit-1tt.6), hence the placeholder.
    if (status !== 'skip') {
      node.appendChild(textEl('span', 'node-dur', '--'));
    }

    // node-open (click-to-navigate affordance, TcXunit-1tt.4): only failed
    // tests inherit their suite's filePath as a navigation target per the
    // epic's design decision, so only failed rows get the hover affordance --
    // and only those with an actual filePath to navigate to get the
    // double-click handler wired (defensive: postOpenFile no-ops without one
    // regardless, but 'has-open' should only claim the affordance is live
    // when it is).
    if (status === 'fail') {
      node.appendChild(textEl('span', 'node-open', '↗'));

      if (suiteFilePath) {
        node.classList.add('has-open');
        node.addEventListener('dblclick', function () {
          postOpenFile(suiteFilePath);
        });
      }
    }

    return node;
  }

  // Builds one suite's full row set: the suite header node, plus either a
  // failed-to-load banner or its test rows (+ any assert blocks). Returns
  // { rows: Element[] } where rows is the flat sequence to append to .tree --
  // the tree's DOM has no per-suite wrapper element (matches
  // docs/design-system.html's markup exactly), so expand/collapse below
  // works by toggling `hidden` on each row directly rather than a container.
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

    if (suite && suite.filePath) {
      suiteNode.appendChild(textEl('span', 'node-src', suite.filePath));
    }

    // A suite that failed to load never ran -- same "didn't run" rule as a
    // skipped test, so no .node-dur slot at all (matches the mockup's
    // failed-to-load suite row, which also omits it).
    if (!hasError) {
      suiteNode.appendChild(textEl('span', 'node-dur', '--'));
    }

    if (suite && suite.filePath) {
      suiteNode.appendChild(textEl('span', 'node-open', '↗'));
      suiteNode.classList.add('has-open');
      suiteNode.addEventListener('dblclick', function () {
        postOpenFile(suite.filePath);
      });
    }

    rows.push(suiteNode);

    if (hasError) {
      // Nothing to expand/collapse -- draw the twisty in its "closed" resting
      // state and leave it non-interactive. The banner is never gated behind
      // it; a broken suite is not something a user should have to expand to
      // discover.
      twisty.textContent = '▶';
      rows.push(buildBanner(suite.error));
      return rows;
    }

    var childRows = [];
    var suiteFilePath = suite && suite.filePath;
    tests.forEach(function (test) {
      var testNode = buildTestNode(test, suiteFilePath);
      rows.push(testNode);
      childRows.push(testNode);

      if (statusOf(test) === 'fail') {
        var failures = (test && test.failures) || [];
        failures.forEach(function (message) {
          var assertNode = buildAssertBlock(message);
          rows.push(assertNode);
          childRows.push(assertNode);
        });
      }
    });

    var expanded = true;
    twisty.addEventListener('click', function () {
      expanded = !expanded;
      twisty.textContent = expanded ? '▼' : '▶';
      childRows.forEach(function (rowEl) {
        rowEl.hidden = !expanded;
      });
    });

    return rows;
  }

  function normalize(text) {
    return (text || '').toLowerCase();
  }

  function nameOf(rowEl) {
    var nameEl = rowEl && rowEl.querySelector('.node-name');
    return normalize(nameEl && nameEl.textContent);
  }

  // TcXunit-1tt.5: text filter over the already-rendered .tree. Walks the
  // flat row sequence (.tree has no per-suite wrapper element -- see
  // renderSuite's own comment on why) back into suite-sized groups, then
  // toggles `hidden` per row rather than touching the DOM structure or
  // re-rendering from JSON. A suite header row (and its .banner, if any)
  // stays visible whenever its own name matches OR any of its tests do --
  // "narrows the visible tree to matching suite/test names" without losing a
  // matching leaf's suite context; a suite whose name matches shows all of
  // its tests too, same reasoning in the other direction. Composes with the
  // segmented status control (results.css's .tree.filter-fail/-skip) purely
  // by both being independently necessary for visibility -- no interaction
  // between the two is coded here.
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
        // Not a suite header (shouldn't happen given renderSuite's output
        // shape) -- skip rather than misclassify or loop forever.
        continue;
      }

      var suiteMatches = query === '' || nameOf(suiteRow).indexOf(query) !== -1;
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

      var suiteVisible = suiteMatches || anyTestMatches;
      suiteRow.hidden = !suiteVisible;
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

  // TcXunit-1tt.5: All/Failed/Skipped segmented control. Pure class-toggle on
  // #tree -- results.css's selectors do the actual hide/show (see that
  // file's "status segmented control" block) -- plus the .seg's own
  // active-button bookkeeping (data-v="plain" is what vsix-shell.css's
  // existing `.seg button.active[data-v]` rule keys its highlight off; the
  // separate data-status attribute is only for this handler to read).
  function setStatusFilter(status) {
    if (!treeEl) {
      return;
    }
    treeEl.classList.remove('filter-fail', 'filter-skip');
    if (status === 'fail' || status === 'skip') {
      treeEl.classList.add('filter-' + status);
    }
  }

  if (filterInput) {
    filterInput.addEventListener('input', applyTextFilter);
  }

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

  // Global entry point -- see the file banner above for who calls this and how.
  window.tcxunitRenderResult = function (result) {
    if (!treeEl) {
      return;
    }

    while (treeEl.firstChild) {
      treeEl.removeChild(treeEl.firstChild);
    }

    var suites = (result && result.suites) || [];
    suites.forEach(function (suite) {
      renderSuite(suite).forEach(function (rowEl) {
        treeEl.appendChild(rowEl);
      });
    });

    updateCounts(result || {});

    // TcXunit-1tt.5: a rerun replaces #tree's children (above) but never
    // #tree itself, so the segmented control's "filter-fail"/"filter-skip"
    // class survives automatically -- only the text filter's per-row hidden
    // state needs recomputing against the freshly-built rows, so a filter
    // typed before a rerun still applies to the new results.
    applyTextFilter();

    // First render this session swaps the empty state out for the tree
    // permanently -- a later rerun replaces #tree's contents in place (above)
    // rather than ever reverting to #emptyState, per TcXunit-1tt.3's
    // acceptance criteria ("results already rendered from a prior run stay
    // visible/updating during a subsequent run").
    if (emptyStateEl && !emptyStateEl.hidden) {
      emptyStateEl.hidden = true;
      treeEl.hidden = false;
    }
  };

  // Global entry point -- called by ResultsToolWindowControl.xaml.cs's
  // StartRunAsync/StopRun (via PushSetRunning) once a run has actually
  // started or actually ended, so this is always a true reflection of
  // whether a tcxunit child process is running, never an optimistic guess
  // made on click.
  window.tcxunitSetRunning = function (running) {
    if (runButton) {
      runButton.textContent = running ? 'Stop' : 'Run tests';
      runButton.classList.toggle('stop-btn', !!running);
    }
    if (progEl) {
      progEl.hidden = !running;
    }
  };

  if (runButton) {
    runButton.addEventListener('click', function () {
      if (!(window.chrome && window.chrome.webview)) {
        // Not hosted inside the VS WebView2 control (e.g. opened directly in
        // a browser for a quick visual check) -- nothing to post to, and
        // nothing would ever call tcxunitSetRunning back, so there is
        // nothing safe to do here.
        return;
      }
      var running = runButton.classList.contains('stop-btn');
      window.chrome.webview.postMessage(running ? 'stop' : 'run');
    });
  }
})();
