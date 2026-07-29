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
// TcXunit-1tt.6 wires real .node-dur values from durationMs (added to the
// CLI's --format json output by the companion TcXunit-6fb.1/.2 tickets):
// suite.durationMs and test.durationMs are milliseconds (a plain number for a
// test -- every test entry in the JSON ran; a nullable number for a suite,
// null when the suite failed to load). formatDurationMs renders "<n> ms" per
// design-system.html section 5's populated-tree example (12 ms, 3 ms, 31 ms,
// ...) -- the doc's node-anatomy table gives no unit-switching threshold (no
// example duration is anywhere near 1000ms), so this does not invent one.
// "Omitted entirely when a test didn't run" (same table) is what governs
// whether a .node-dur element exists at all -- a skipped test or a
// failed-to-load suite gets no slot, exactly as before this ticket; what
// changes here is only what fills the slot when one is drawn.
//
// TcXunit-1tt.8 adds #rerunFailedButton (a .ghost-btn per design-system.html
// section 5's populated-tree example). It carries no suite-name data of its
// own -- the WPF host, not this page, tracks which suites failed in the last
// run (ResultsToolWindowControl.xaml.cs's _lastFailedSuiteNames) -- so a
// click just posts the JSON envelope {type:'rerunFailed'}, the same
// window.chrome.webview.postMessage channel TcXunit-1tt.4's openFile uses,
// and the host does the rest (re-invokes tcxunit with --suite <name> per
// failed suite, then pushes a normal window.tcxunitRenderResult(...) back in
// -- REPLACING #tree exactly like any other run, never merging into it,
// since there is no separate "partial render" code path here at all). This
// page's only two jobs are (1) posting the click and (2) keeping the
// button's `disabled` attribute correct -- per the mockup's
// ".ghost-btn[disabled]" state, disabled whenever the last render had zero
// failures OR a run is currently in flight (updateRerunFailedButton, wired
// into both tcxunitRenderResult and tcxunitSetRunning below).
//
// TcXunit-1tt.7 adds keyboard nav: #tree carries tabindex="0" (results.html)
// as the one tabbable element for a "roving selection" over its .node rows
// -- Up/Down move a tracked selectedRow (.node.selected, results.css) through
// the currently *visible* rows (visibleRows() below skips both TcXunit-1tt.5
// mechanisms -- the text filter's `hidden` attribute and the segmented
// control's CSS-class-driven display:none -- via offsetParent rather than
// duplicating either rule here), Enter opens the selected row's file by
// calling the same openNodeFile() a dblclick uses (TcXunit-1tt.4), and Space
// toggles a selected suite row's twisty by calling the same toggleExpand()
// a twisty click uses (TcXunit-1tt.2). No Left/Right binding is added --
// docs/design-system.html section 8's A11y rule only specifies arrows/Enter/
// Space.
//
// TcXunit-qjt supersedes TcXunit-1tt.3's "results already rendered from a
// prior run stay visible/updating during a subsequent run" decision, per
// explicit user feedback: a stale tree left on screen for the full duration
// of a new run turned out to be indistinguishable from a fresh one. Two new
// global entry points -- window.tcxunitBeginRun() (clears #tree/counts and
// swaps in #runningState, called by StartRunAsync right before its CLI
// process starts) and window.tcxunitSetStatus(state, text) (drives the
// .tw-statusbar dot/#statusText, called alongside tcxunitSetRunning at every
// one of StartRunAsync's status-line branches) -- plus #runningState itself
// (results.html) are this ticket's additions; see their own comments below
// for how they fit into the existing render/running-state plumbing.
//
// Not implemented here: per-node "currently executing" state (the CLI emits
// one JSON blob at the end of a run, not an incremental stream, so there is
// no data to know which suite is currently executing -- only that a run is
// or isn't in flight; #runningState is a single generic placeholder, not a
// per-suite one). Plain script (no ES modules) since this page has exactly
// one small render concern, unlike TcAgent's chat.html.
//
// TcXunit-9fs adds an expandable "call stack" section to a failed-to-load
// suite's banner (buildBanner/buildCallStackSection below), rendering
// suite.callStack -- additive JSON the CLI has emitted since TcXunit-7s6,
// one frame ({ pouTypeName, methodName, line, bodyLine }) per level of the
// interpreted call chain, innermost first. Frames carry no filePath of their
// own (only the suite does), so a frame click opens the suite's file --
// the only navigation target this data can support -- same as the suite
// row's own dblclick.
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

  // TcXunit-1tt.8: the two independent reasons #rerunFailedButton can be
  // disabled -- "last render had zero failures" (hasFailures, set by
  // tcxunitRenderResult) and "a run is currently in flight" (isRunning, set
  // by tcxunitSetRunning). Neither alone is the whole rule: a passing run
  // must disable it regardless of run state, and a run in flight must
  // disable it regardless of the previous result (one action live at a
  // time, same reasoning #runButton/#prog already follow).
  var hasFailures = false;
  var isRunning = false;

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

  // TcXunit-1tt.7: the shared "open" step a row's dblclick handler and the
  // Enter key both call -- reads the filePath a node's dblclick wiring
  // already stashed on its own dataset (see buildTestNode/renderSuite below)
  // rather than each caller re-deriving it from the suite/test data a second
  // time. No-op for a row with no filePath (e.g. a passing/skipped test, or
  // Enter pressed with nothing selected) -- postOpenFile already guards that
  // case, so nothing further is needed here.
  function openNodeFile(node) {
    if (node) {
      postOpenFile(node.dataset.filePath);
    }
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

  // Formats a durationMs number as the .node-dur slot's text, per
  // design-system.html section 5's populated-tree example ("12 ms", "3 ms",
  // "31 ms"). Returns null for anything that isn't a genuine numeric
  // duration (missing/null -- the "didn't run" case, which callers use to
  // decide whether to draw the .node-dur element at all) rather than
  // rendering "NaN ms" or similar.
  function formatDurationMs(durationMs) {
    if (typeof durationMs !== 'number' || !isFinite(durationMs)) {
      return null;
    }
    return durationMs + ' ms';
  }

  // TcXunit-1tt.8: whether a suite counts as "failed" for #rerunFailedButton's
  // enable/disable rule -- exactly the same test renderSuite below uses to
  // pick a suite row's glyph/status class (hasError || anyFail), pulled out
  // to a named function since both this file's own render path and the
  // button-state check need the identical definition of "failed".
  function suiteFailed(suite) {
    if (suite && suite.error) {
      return true;
    }
    var tests = (suite && suite.tests) || [];
    return tests.some(function (t) { return statusOf(t) === 'fail'; });
  }

  // Applies the combined disabled rule (see hasFailures/isRunning's own
  // comment above) to #rerunFailedButton. Called after every render and
  // every running-state change so the two independently-updated flags never
  // leave the button in a stale state.
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

  // Renders one failure message as an .assert block. Bolds expected/actual
  // when the message matches FB_TestSuite's known format; otherwise falls
  // back to the raw text -- "with expected/actual if present in the JSON"
  // per TcXunit-1tt.2's acceptance criteria, not a guarantee every failure
  // parses that way.
  // TcXunit-3tx.2: failures[] entries became objects ({ message, kind,
  // expected, actual, assert, pou, method, bodyLine, ... }) instead of bare
  // strings. `message` is the identical formatted line the string used to be,
  // so this renderer needs nothing else. No string fallback: the page only ever
  // renders JSON that TcxunitProcessRunner already deserialized into
  // TcxunitFailure, so a bare-string payload could not reach here anyway.
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

  // One callStack[] frame's label: "PouTypeName.MethodName(line)" when a
  // method is known (an assert/method-level frame), or just "PouTypeName(line)"
  // for a suite-body/bare-FB frame (methodName null, per CallStackFrameReport's
  // own convention) -- mirrors the console output's own frame formatting
  // (AssertSite.LocationWithLine, TcXunit-7s6's console path): the
  // XAE-implementation-editor-relative bodyLine, not the raw .TcPOU XML
  // line -- console output never uses the latter for a frame, so this
  // doesn't either. Falls back to the raw line if bodyLine is unknown, then
  // omits the "(...)" suffix entirely if neither is known.
  function callStackFrameLabel(frame) {
    var pou = (frame && frame.pouTypeName) || '?';
    var name = (frame && frame.methodName) ? pou + '.' + frame.methodName : pou;
    var line = (frame && typeof frame.bodyLine === 'number') ? frame.bodyLine
      : (frame && typeof frame.line === 'number') ? frame.line : null;
    return line === null ? name : name + '(' + line + ')';
  }

  // TcXunit-9fs: the expandable "call stack" section inside a failed-to-load
  // suite's banner, one row per suite.callStack frame (innermost first, per
  // the CLI's own ordering -- CallStack[0] describes the same fault as
  // suite.error/fileLine, so no re-sorting happens here). Only drawn when the
  // CLI actually emitted frames (null/empty for a load-level failure that
  // never entered an interpreted ST body) -- returns null in that case so the
  // caller can skip appending anything.
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
      // No per-frame filePath in the wire shape (TcXunit-7s6's
      // CallStackFrameReport carries POU/method/line only) -- every frame
      // opens the suite's own file, same as the suite row's dblclick, which
      // is the only navigation target this data can support.
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

  // Suite-failed-to-load banner: distinct from a swallowed empty suite, per
  // TcXunit-1tt.2's acceptance criteria. suite.error is CliRunner's caught
  // exception message (unresolved type, parse error, etc.) -- free text, not
  // HTML, so it's set via textContent even though the mockup shows a <code>
  // fragment inline; this data has no reliable way to locate that substring.
  // TcXunit-9fs: also appends an expandable call-stack section (see
  // buildCallStackSection) when the CLI emitted one for this suite.
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
    // section 5) -- a skipped test never ran, so it gets no slot at all. A
    // pass/fail test did run and the CLI always emits a non-negative
    // durationMs for it (TcXunit-6fb.1); formatDurationMs returning null here
    // would mean an unexpected payload shape, in which case omitting the slot
    // is still the right call (no fabricated placeholder).
    var testDur = formatDurationMs(test && test.durationMs);
    if (status !== 'skip' && testDur !== null) {
      node.appendChild(textEl('span', 'node-dur', testDur));
    }

    // Click-to-navigate (TcXunit-1tt.4): only failed tests inherit their
    // suite's filePath as a navigation target. No .node-open arrow here --
    // feedback: the arrow only ever actually opened via the suite header row,
    // never from the test row itself, so drawing it on this row was a false
    // affordance. 'has-open' (cursor: pointer, results.css) + the dblclick
    // handler stay -- double-click still opens the suite's file.
    if (status === 'fail' && suiteFilePath) {
      node.classList.add('has-open');
      node.dataset.filePath = suiteFilePath;
      node.addEventListener('dblclick', function () {
        openNodeFile(node);
      });
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

    // A suite that failed to load never ran -- same "didn't run" rule as a
    // skipped test, so no .node-dur slot at all (matches the mockup's
    // failed-to-load suite row, which also omits it; the CLI backs this up by
    // emitting durationMs: null for exactly this case, TcXunit-6fb.2).
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

    // Path row: a suite row's own name can be arbitrarily overlapped by a
    // long absolute filePath if drawn inline (feedback: "the FB_CounterTest
    // is overlapped by the path") -- drawn as its own row below the name
    // instead, folded into childRows/toggleExpand below so it collapses with
    // the rest of the suite rather than always taking up a line.
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

      // Feedback: a failed-to-load suite couldn't be collapsed into its
      // parent result -- give it the same twisty/toggleExpand wiring a
      // normal suite gets below, gating the path row + banner instead of
      // test/assert rows.
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

    // TcXunit-1tt.7: named (not an inline closure passed straight to
    // addEventListener) and stashed on suiteNode itself so the Space-key
    // handler below can call the identical function a twisty click uses --
    // "reuse, don't duplicate" for expand/collapse. The hasError branch above
    // returns before this point with its own toggleErrorExpand wired instead
    // (same shape, gating the path row + banner rather than test/assert rows).
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
  // Segmented control's current value ('fail'/'skip'/null for "All"), read by
  // applyTextFilter's suite-visibility check below. Kept as its own var
  // (rather than re-reading #tree's filter-fail/filter-skip class back out)
  // since setStatusFilter is the single place that already knows it.
  var statusFilter = null;

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

      // Feedback: selecting "Failed" in the segmented control hid failing
      // rows' non-matching siblings but left the suite header itself (and a
      // passing suite's now-childless header) visible -- results.css's
      // .tree.filter-fail/.filter-skip rules deliberately only prune
      // .node.depth1 rows for suite context, but that leaves an
      // all-passing suite showing an empty header under "Failed". A suite
      // only earns visibility under an active status filter if it actually
      // has a row of that status: a load error or a failing test for
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

  // TcXunit-1tt.5: All/Failed/Skipped segmented control. Pure class-toggle on
  // #tree -- results.css's selectors do the actual hide/show (see that
  // file's "status segmented control" block) -- plus the .seg's own
  // active-button bookkeeping (data-v="plain" is what vsix-shell.css's
  // existing `.seg button.active[data-v]` rule keys its highlight off; the
  // separate data-status attribute is only for this handler to read).
  function setStatusFilter(status) {
    statusFilter = (status === 'fail' || status === 'skip') ? status : null;
    if (!treeEl) {
      return;
    }
    treeEl.classList.remove('filter-fail', 'filter-skip');
    if (statusFilter) {
      treeEl.classList.add('filter-' + statusFilter);
    }
    // Suite headers' own visibility depends on statusFilter too (see
    // applyTextFilter's suiteHasError/statusOk check) -- rerun it here since
    // this is the one path (segment click) that changes statusFilter without
    // also going through the text-input or render paths that already call it.
    applyTextFilter();
  }

  // TcXunit-1tt.7: keyboard nav's selection state. Tracks the currently
  // selected .node element, or null when nothing is selected (fresh page,
  // just after a render, or the previous selection scrolled out of the
  // visible set -- see moveSelection below, which treats all three the
  // same way).
  var selectedRow = null;

  // The rows keyboard nav is allowed to land on: every .node currently
  // rendered as actually visible. Deliberately uses offsetParent rather than
  // re-deriving visibility from `hidden`/the segmented control's filter-fail/
  // filter-skip class by hand -- that would mean keeping a second copy of
  // results.css's hide rules in sync here. offsetParent is null for both
  // TcXunit-1tt.5 mechanisms (the text filter's `hidden` attribute, via
  // results.css's `.node[hidden] { display: none; }`, and the segmented
  // control's CSS-class-driven display:none) without this file needing to
  // know which one applies.
  function visibleRows() {
    if (!treeEl) {
      return [];
    }
    var rows = Array.prototype.slice.call(treeEl.querySelectorAll('.node'));
    return rows.filter(function (row) {
      return !row.hidden && row.offsetParent !== null;
    });
  }

  // Applies/clears .node.selected (results.css) and keeps selectedRow in
  // sync. Passing null clears the selection entirely (used on every render,
  // since a rerun/rerun-failed replaces #tree's rows -- see
  // tcxunitRenderResult below -- and a stale element reference would only
  // ever be wrong).
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

  // Moves the selection through visibleRows(): direction is +1 (ArrowDown) or
  // -1 (ArrowUp). Clamps at the ends rather than wrapping -- design-system.
  // html's A11y rule doesn't specify wraparound, and clamping is the more
  // common convention for this kind of list. If selectedRow isn't found in
  // the current visible set (nothing selected yet, or it was filtered/
  // rerendered away since), this falls back to the first visible row for
  // either direction rather than guessing an offset from a stale position.
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

  // #tree is the sole tabbable element this feature adds (tabindex="0" in
  // results.html) -- a "roving selection" tracked here in JS, not real
  // per-row DOM focus. Enter/Space only act when they'd do something (an
  // open target / a toggleExpand function present, respectively), mirroring
  // how the equivalent mouse paths (dblclick / twisty click) are themselves
  // only wired onto rows that support them -- see buildTestNode/renderSuite.
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

    // TcXunit-1tt.7: every render (a normal run or a rerun-failed) replaces
    // #tree's rows outright, so any previous selection is a stale element
    // reference the moment this runs -- clear it rather than leave
    // selectedRow pointing at a detached node.
    setSelectedRow(null);

    var suites = (result && result.suites) || [];
    suites.forEach(function (suite) {
      renderSuite(suite).forEach(function (rowEl) {
        treeEl.appendChild(rowEl);
      });
    });

    updateCounts(result || {});

    // TcXunit-1tt.8: recompute #rerunFailedButton's "last run had zero
    // failures" half of its disabled rule from this render's own suites list
    // -- covers a normal run, a rerun-failed run (button correctly goes back
    // to disabled once the previously-failed suites all pass), and the
    // suite-load-failure case (a suite with an .error banner counts via
    // suiteFailed, same as any other failure).
    hasFailures = suites.some(suiteFailed);
    updateRerunFailedButton();

    // TcXunit-1tt.5: a rerun replaces #tree's children (above) but never
    // #tree itself, so the segmented control's "filter-fail"/"filter-skip"
    // class survives automatically -- only the text filter's per-row hidden
    // state needs recomputing against the freshly-built rows, so a filter
    // typed before a rerun still applies to the new results.
    applyTextFilter();

    // TcXunit-qjt: every completed run swaps #runningState (shown for the
    // run's duration by tcxunitBeginRun below) and #emptyState (the
    // pre-first-run copy) back out for #tree, unconditionally -- a rerun no
    // longer needs the "already showing the tree" guard TcXunit-1tt.3 used to
    // have here, since tcxunitBeginRun now always hides the tree at the start
    // of every run.
    if (runningStateEl) {
      runningStateEl.hidden = true;
    }
    if (emptyStateEl) {
      emptyStateEl.hidden = true;
    }
    treeEl.hidden = false;
  };

  // TcXunit-qjt: clears #tree's rows/counts and swaps in #runningState, in
  // #emptyState's/#tree's place, right before a run's CLI process starts
  // (ResultsToolWindowControl.xaml.cs's StartRunAsync calls this ahead of
  // window.tcxunitSetRunning(true) below) -- so the panel can never show a
  // mix of a prior run's stale rows and a new run in flight. hasFailures
  // resets to false along with the counts: there is no result on screen for
  // #rerunFailedButton to rerun until the next tcxunitRenderResult call sets
  // it again (isRunning already disables the button for the run's own
  // duration -- see updateRerunFailedButton).
  window.tcxunitBeginRun = function () {
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

  // TcXunit-qjt: drives the .tw-statusbar dot/#statusText pushed in by
  // ResultsToolWindowControl.xaml.cs's new PushStatus, mirroring
  // StartRunAsync's own status-line branches (running while the CLI process
  // is in flight, then ready/stopped/error once it ends). state is one of
  // 'running'/'stopped'/'error' (colored via the matching class, see
  // results.css) or anything else (including 'ready') for the default green
  // dot -- results.css only defines the three non-default classes since
  // "ready" is the dot's plain, class-less state already in markup.
  window.tcxunitSetStatus = function (state, text) {
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

  // Global entry point -- called by ResultsToolWindowControl.xaml.cs's
  // StartRunAsync/StopRun (via PushSetRunning) once a run has actually
  // started or actually ended, so this is always a true reflection of
  // whether a tcxunit child process is running, never an optimistic guess
  // made on click.
  window.tcxunitSetRunning = function (running) {
    isRunning = !!running;

    if (runButton) {
      runButton.textContent = running ? 'Stop' : 'Run tests';
      runButton.classList.toggle('stop-btn', !!running);
    }
    if (progEl) {
      progEl.hidden = !running;
    }

    // TcXunit-qjt: a run that ends without ever calling tcxunitRenderResult
    // (Stop, or a host-level error -- both skip straight to the finally
    // block that calls this with running=false) leaves #runningState still
    // showing; left alone that would freeze a "Running..." placeholder on
    // screen for a run that is no longer running. Reverting to #emptyState
    // is correct either way here since tcxunitBeginRun already cleared
    // #tree -- there is nothing to show. A run that DID render already
    // hid #runningState itself (see tcxunitRenderResult above), so this is a
    // no-op in that case.
    if (!running && runningStateEl && !runningStateEl.hidden) {
      runningStateEl.hidden = true;
      if (emptyStateEl) {
        emptyStateEl.hidden = false;
      }
    }

    // TcXunit-1tt.8: a run in flight (whether started from #runButton or
    // #rerunFailedButton itself) disables #rerunFailedButton regardless of
    // the previous result -- one action live at a time, same reasoning
    // #runButton/#prog already follow.
    updateRerunFailedButton();
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

  // TcXunit-1tt.8: posts the JSON envelope (mirrors postOpenFile's shape,
  // TcXunit-1tt.4) requesting a rerun of just the last run's failed suites.
  // No payload of its own -- see this file's top-of-file banner comment for
  // why the host, not this page, is the one holding the suite-name list.
  // The `disabled` attribute (kept correct by updateRerunFailedButton) is
  // the only guard needed here; a disabled button doesn't fire click events,
  // so there's nothing further to check before posting.
  if (rerunFailedButton) {
    rerunFailedButton.addEventListener('click', function () {
      if (!(window.chrome && window.chrome.webview)) {
        return;
      }
      window.chrome.webview.postMessage({ type: 'rerunFailed' });
    });
  }
})();
