// TcXunit-1tt.2: static results tree render (node anatomy, banner, assert
// detail). Builds the .tree DOM from a TcxunitRunResult (see
// src/TcXunit.Vsix/TestRunner/TcxunitModels.cs for the C# shape; wire JSON is
// camelCase: { suites: [{ name, filePath, error, tests: [{ name, passed,
// failures }] }], passed, failed, exitCode, error } per
// docs/design-system.html section 8's "Data" rule).
//
// window.tcxunitRenderResult(result) is called by
// ResultsToolWindowControl.xaml.cs's RunButton_Click via ExecuteScriptAsync,
// passing the CLI's own JSON output as a literal JS expression (not a string
// to JSON.parse -- see BuildRenderResultScript in that file).
//
// Not implemented here (separate sibling tickets under TcXunit-1tt): filter
// box, All/Failed/Skipped segmented control, click-to-navigate (.node-open
// is visual only), rerun-failed, keyboard nav, real .node-dur values (always
// "--" for a row that ran; omitted for a row that didn't -- see
// design-system.html section 5's node-anatomy table). Plain script (no ES
// modules) since this page has exactly one small render concern, unlike
// TcAgent's chat.html.
(function () {
  'use strict';

  var treeEl = document.getElementById('tree');

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

  function buildTestNode(test) {
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
    // epic's design decision, so only failed rows get the hover affordance.
    if (status === 'fail') {
      node.appendChild(textEl('span', 'node-open', '↗'));
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
    tests.forEach(function (test) {
      var testNode = buildTestNode(test);
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
  };
})();
