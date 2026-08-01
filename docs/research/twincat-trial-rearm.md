# TwinCAT 3 seven-day trial: is there a supported unattended re-arm?

- Issue: `xstunit-229.22` (part of epic `xstunit-229`; related to `xstunit-229.20`,
  the Grade B nightly verdict-diff pilot, and `xstunit-229.24`, the dongle
  station).
- Date: 2026-08-01.
- **STATUS: findings, not yet ratified.** One question, one verdict; nothing here
  needs a decision round beyond accepting or rejecting the verdict.
- Method: **web-primary-sourced.** Every substantive claim is cited to a
  Beckhoff-published URL (`infosys.beckhoff.com` or `download.beckhoff.com`) and
  was fetched, not recalled. Nothing in this document rests on forum posts,
  vendor blogs, or resellers — three such pages surfaced during searching and
  none was used. Where Beckhoff is *silent* rather than explicit, that is called
  out in the text as silence.
- **Scope discipline.** The question asked is only whether Beckhoff *supports*
  unattended trial re-arm. This document does not investigate, describe, or
  evaluate unsupported workarounds, and deliberately contains no analysis of the
  security-code mechanism beyond what Beckhoff itself publishes about it.
- **Cite convention.** InfoSys page IDs are stable but the page *content* is
  versioned; the licensing manual read here is version 1.20.7, dated 2025-09-16
  (<https://infosys.beckhoff.com/content/1033/tc3_licensing/index.html>). Quoted
  sentences are verbatim from the English (1033) content set.

---

## Verdict

**No — Beckhoff documents no unattended or scriptable path to create or re-arm a
TwinCAT 3 seven-day trial license; the only published procedure is an
interactive one in TwinCAT XAE that requires a human to read a security code out
of a dialog and type it back in.**

Of the three possible landings the ticket names — (a) no supported path exists,
(b) a supported path exists, (c) the docs are silent — this is **(a), arrived at
by enumeration rather than by prohibition.** That distinction matters enough to
state plainly, so §4 does. No Beckhoff page says "trial license creation may not
be automated." What the documentation does instead is (i) publish exactly one
procedure for creating a trial, whose title is literally "Creating trial licenses
**manually**" and whose steps include a human-in-the-loop verification step, and
(ii) enumerate the licensing surface of every automation product Beckhoff ships —
Automation Interface, ITcSysManager, the ADS PowerShell module, the OEM
command-line signing tool — none of which exposes trial creation. This is not the
same evidentiary situation as silence: the adjacent, contrasting case (OEM
application licenses) *is* documented as automatable, with a named executable and
a command-line syntax block (§3.4). Where Beckhoff intends a licensing operation
to be scriptable, it says so and gives you the exe. For the trial, it does not.

Confidence: **high** on the verdict, **moderate** on completeness of the
enumeration (see §5).

---

## 1. What the trial actually is, per Beckhoff

The load-bearing facts, all verbatim from
<https://infosys.beckhoff.com/content/1033/tc3_licensing/921947147.html>
("TwinCAT 3 test licenses"):

> "TwinCAT 3 test licenses can be activated as often as required in the TwinCAT 3
> development environment (XAE) for a period of 7 days."

> "TwinCAT 3 test licenses cannot be generated with the TwinCAT 3 Runtime (XAR).
> They cannot be generated for TwinCAT 3 license dongles either, but only for the
> set target system (IPC or engineering computer)."

> "A trial license (7-day test version) cannot be enabled for a TwinCAT 3 license
> dongle."

> "An internet connection is not required."

The engineering-product page restates the repeatability in marketing terms — "a
7-day trial license for TwinCAT 3 products, which can be renewed over and over
again"
(<https://infosys.beckhoff.com/content/1033/tc3_licensing/179888523.html>) — and
the license-types page adds the XAE dependency: "The creation of a trial license
requires the TwinCAT 3 development environment. It is not possible to create a
trial license in the TwinCAT 3 Runtime."
(<https://infosys.beckhoff.com/content/1033/tc3_licensing/955881355.html>).

Four consequences fall out of those four sentences, and they are the ones that
matter to `xstunit-229.20`:

1. **Repetition is explicitly blessed.** "As often as required", "renewed over
   and over again". There is no documented cap, no cooldown, and no
   account/registration gate. Re-arming weekly forever is a *supported* use of
   the product, not an abuse of it.
2. **It is offline.** No internet connection means no license-server round trip
   to script against, and also no external dependency that could fail at 02:00.
3. **XAE must be on the box.** A trial cannot be created from a runtime-only
   machine, so the pilot station cannot be reduced to XAR.
4. **Trial and dongle are disjoint mechanisms.** A trial can never live on a
   dongle. The two routes to a licensed run do not compose or hand off to each
   other — see §6.

## 2. The documented creation procedure, and where the human sits in it

The canonical procedure is
<https://infosys.beckhoff.com/content/1033/tc3_licensing/3510308491.html>,
titled **"Creating trial licenses manually"**. Its steps: select the target
system in the XAE Base toolbar (`<Local>` or a remote computer); double-click
**License** in the System subtree; open the **Manage Licenses** tab; optionally
tick *Ignore Project Licenses* to pick licenses by hand; open the **Order
Information (Runtime)** tab; click **7 Days Trial License…**.

Then, verbatim:

> "A dialog box opens, prompting you to enter the security code displayed in the
> dialog."

> "Enter the code exactly as it is displayed and click on OK."

The identical two sentences appear in the per-function licensing chapter that
Beckhoff replicates across the TFxxxx manuals — e.g. TF5130
(<https://infosys.beckhoff.com/content/1033/tf5130_tc3_unival_plc/262609675.html>)
and TF6280
(<https://infosys.beckhoff.com/content/1033/tf6280_tc3_ethernetipslave/262609675.html>).
So this is not an artifact of one page: the human verification step is the
documented behaviour of the feature everywhere Beckhoff describes it.

Two corrections to the ticket's stated prior belief, neither of which changes the
verdict:

- **The "five-character" detail is not documented by Beckhoff.** Every page
  found says "the security code displayed in the dialog" and specifies no length.
  The prior belief is probably right from observation, but it is not a
  documented fact and nothing here rests on it.
- **The re-arm is not a separate flow from the first arm.** There is no
  "extend"/"renew" path in the docs at all; re-arming *is* clicking **7 Days
  Trial License…** again, which is the same procedure and therefore the same
  security-code step. So the answer to "is the code required on every re-arm?" is
  yes-by-construction: there is only one documented flow and it contains the step.

## 3. The automation surfaces, enumerated

This section is the actual evidence for the verdict. The claim is not "we looked
and didn't find it" but "we enumerated each product Beckhoff ships for
automating TwinCAT, read its licensing chapter, and the trial is in none of
them."

### 3.1 Automation Interface — the `License` tree item

The Automation Interface's licensing coverage is two pages, and both were read.

**"Configuration of licensing hardware"**
(<https://infosys.beckhoff.com/content/1033/tc3_automationinterface/4554517771.html>)
covers discovering and selecting licensing *hardware* (e.g. an EL6070 terminal)
via `LookupTreeItem("TIRC^License")`, `ProduceXml()` and `CreateChild()`.
Requires TwinCAT v3.1.4022.4. No trial-license call.

**"Activating license response files"**
(<https://infosys.beckhoff.com/content/1033/tc3_automationinterface/4554565515.html>)
is the one page where the AI *writes* to the licensing subsystem, and it does so
by pushing an XML command block into the `License` node via `ConsumeXml()`. The
documented command set is exactly one command:

```xml
<TreeItem>
  <ItemName>License</ItemName>
  <PathName>TIRC^License</PathName>
  <ItemType>59</ItemType>
  <LicenseDef>
    <Commands>
      <ActivateResponseFile>
        <Path>...</Path>
        <OemGuid>...</OemGuid>
      </ActivateResponseFile>
    </Commands>
  </LicenseDef>
</TreeItem>
```

`ActivateResponseFile` **installs a `.tclrs` file that already exists**. It does
not mint one. There is no `CreateTrialLicense`, no `GenerateTrialLicense`, no
sibling command of any kind in the documented `<Commands>` block. This is the
strongest single piece of negative evidence in the document, because it is an
*enumeration of a command surface*, not an absence of a page: Beckhoff published
the list of things you may command the `License` node to do, and trial creation
is not on it.

### 3.2 `ITcSysManager`

<https://infosys.beckhoff.com/content/1033/tc3_automationinterface/242753675.html>
documents `ITcSysManager` (`NewConfiguration`, `OpenConfiguration`,
`SaveConfiguration`, `ActivateConfiguration`, `LookupTreeItem`,
`StartRestartTwinCAT`, `IsTwinCATStarted`, `LinkVariables`, `UnlinkVariables`),
`ITcSysManager2` (`SetTargetNetId`, `GetTargetNetId`, `GetLastErrorMessages`) and
`ITcSysManager3` (`LookupTreeItemById`, `ProduceMappingInfo`, `ClearMappingInfo`).
**No licensing member on any of them.** Licensing reaches the AI only through the
tree item of §3.1.

### 3.3 ADS PowerShell module (`TcXaeMgmt`, part of TE1000)

Beckhoff's own PowerShell module
(<https://infosys.beckhoff.com/content/1033/tc3_ads_ps_tcxaemgmt/11221779979.html>;
published by Beckhoff Automation GmbH at
<https://www.powershellgallery.com/packages/TcXaeMgmt/7.0.41>) exposes
**`Get-TcLicense`** for browsing license information. That is a read. There is no
`New-TcLicense`, `Add-TcLicense`, or trial-creation cmdlet in the documented
surface. The module's own documented discovery command is
`Get-Command -Module TcXaeMgmt -CommandType Function`, which is worth running
locally as the cheap confirmation step noted in §5.

### 3.4 Command-line licensing tooling — the contrast case

Beckhoff *does* ship and document a command-line licensing tool, which is why
this is (a)-by-enumeration and not (c)-silence. "Automated creation via a command
line tool"
(<https://infosys.beckhoff.com/content/1033/tc3_security_management/7888497419.html>)
documents `TcSignTool.exe`:

```
tcsigntool licsign /f certificatefile [/p password] [/i issueTime] [/d validDays] [/q] licfile1 [licfile2]
```

with a `/q` quiet flag and `0`/`1` exit codes — i.e. a tool shaped for exactly
the unattended use the ticket is asking about. But it signs **OEM application
license** requests (`.tclrq` → `.tclrs`) using an OEM certificate. It is not a
TwinCAT-product-license tool and it has nothing to do with the seven-day trial.

The inference: Beckhoff's house style, when a licensing operation is meant to be
automated, is to publish an executable with a syntax block, a quiet flag and exit
codes. That style is applied to OEM signing and to nothing else in the licensing
docs. The trial's documented interface is a dialog box.

### 3.5 Is a headless/CI story documented at all?

No — and the Automation Interface FAQ says something stronger that constrains any
CI design here regardless of licensing
(<https://infosys.beckhoff.com/content/1033/tc3_automationinterface/242685835.html>):

> "To execute Automation Interface code, TwinCAT XAE (Engineering) is needed"

because the AI drives the Visual Studio COM object. The FAQ contains no entry on
build servers, continuous integration, headless operation, or licensing. The
licensing manual's own "Quick start"
(<https://infosys.beckhoff.com/content/1033/tc3_licensing/63050397061788427.html>)
presents exactly two routes onto a system — dongles pre-activated by Beckhoff in
production, and the manual request-file/response-file round trip through
`tclicense@beckhoff.com` (<https://infosys.beckhoff.com/content/1033/tc3_licensing/920379275.html>)
— and even notes that "Automatic recognition and integration of TwinCAT 3 license
dongles is not yet available in the current TwinCAT version." Beckhoff's
published automation ambition for licensing is modest and does not include the
trial.

## 4. Why this is (a) and not (c), stated carefully

The honest form of the finding is: **no Beckhoff document prohibits automating
trial creation, and no Beckhoff document supports it.** Treating that as (c)
"silence" would be wrong for three reasons.

1. **Silence about a feature is not the same as silence about a surface.** §3.1
   is an enumerated command list. A feature absent from a published enumeration
   of a command surface is a documented absence, not an undocumented presence.
2. **The one documented procedure is titled "manually" and contains a step whose
   only function is to require a person.** A security code you must read off the
   screen and retype is not incidental UI friction; it is the mechanism by which
   the vendor makes the operation attended. Automating around it would be
   defeating a control, not using an undocumented feature — which puts it outside
   "supported" by definition, and outside this document's scope by instruction.
3. **The contrast case exists and is documented (§3.4).** If Beckhoff's docs were
   simply thin on automation across the board, silence would be uninformative.
   They are not: the OEM path gets an exe, a syntax line and exit codes.

What would flip this to (b) is narrow and checkable: a documented `<Commands>`
entry on the `License` tree item that creates a trial, a `TcXaeMgmt` cmdlet that
does, or a Beckhoff KB article describing a supported unattended re-arm. None was
found.

**Practical reading for the purchasing decision: the seven-day trial is a
supported way to run TwinCAT indefinitely, but only with a human touching it
once a week. It is not a supported way to run TwinCAT unattended indefinitely.**

## 5. Confidence, and what was not checked

Confidence in the verdict is **high**. Confidence that the enumeration in §3 is
*exhaustive* is **moderate**, bounded by three known gaps:

- The full TwinCAT 3 Licensing manual PDF
  (<https://download.beckhoff.com/download/document/automation/twincat3/Licensing_EN.pdf>,
  v1.20.7) could not be retrieved — it exceeds the fetch size limit and the CDN
  returned 403 to a direct download. The InfoSys HTML pages read here are the
  same content set, but a full-text search of the PDF for "script", "command
  line" and "automat" was not performed and is the single cheapest way to raise
  confidence to near-certain.
- `Get-Command -Module TcXaeMgmt` was not run on this workstation. §3.3 rests on
  Beckhoff's published module description rather than the installed module's
  actual cmdlet list. This is a one-line local check on a box that already has
  TwinCAT installed.
- Beckhoff's customer-portal knowledge base (behind login) was not searched; only
  public InfoSys and beckhoff.com were. A supported path documented *only* in a
  gated KB article would have been missed. This is judged unlikely — a supported
  automation path for a headline free feature would normally appear in InfoSys —
  but it is not excluded.

Also explicitly not established, because the docs do not say: whether a trial can
be re-armed *before* the current one expires (i.e. whether the seven days can be
refreshed on day 5, or only after lapse). If the pilot wants a safety margin
rather than a hard weekly deadline, that is an empirical question for night one,
not a documented one.

## 6. What this means for the Grade B pilot (`xstunit-229.20`)

`xstunit-229.20` already assumes "the trial expires after seven days and
re-arming needs a human at the license dialog". **That assumption is confirmed,
and the pilot's design does not need to change.** What the research adds is the
shape of the constraint, which is better than feared in one direction and harder
in another.

**Better than feared: the weekly babysit is cheap and unlimited.** "As often as
required", "renewed over and over again", no internet, no account, no cap
(§1). The human cost of the pilot is one person, one XAE dialog, once a week, for
as long as anyone cares to keep it running. Nothing forces the pilot week to be
exactly one week — a two- or three-week measurement campaign is a re-click, not a
purchase. For a pilot whose success bar is *measurement viability* rather than
divergence count, that is entirely adequate: the six unattended nights the ticket
plans for fit inside one arming, and the diff harness gets exercised
unattended exactly as designed.

**Harder than feared, and this is the load-bearing consequence: the trial can
never become the unattended mechanism, no matter how long the pilot runs.**
Because trials cannot be generated by XAR and cannot be enabled for a dongle
(§1), the trial route and the dongle route are not two points on a continuum
where the trial gradually converts into permanence. They are disjoint. There is
no incremental step — no partial purchase, no cheaper intermediate SKU discovered
by this research — between "a human clicks weekly" and "buy the dongle". The
decision `xstunit-229.24` frames is therefore binary, and this research removes
the only hypothesis that could have softened it.

Concretely, for the pilot:

- **Keep XAE on the pilot box.** A trial cannot be created from a runtime-only
  machine (§1), so the "just leave XAR running" simplification is unavailable.
- **The re-arm is local to the target system selected in XAE.** The procedure's
  first step is choosing `<Local>` or a remote computer (§2), so one human at one
  XAE could in principle re-arm several runtime targets in one sitting. Not
  relevant to a single-box pilot; relevant if Grade B ever fans out.
- **Schedule the babysit as a hard weekly commitment, not a best-effort one.** A
  missed re-arm does not degrade the run, it stops it, and the nights lost are
  not recoverable retroactively.
- **Do not spend engineering effort on unattended re-arm.** There is nothing
  supported to build against, and this document's scope explicitly excludes the
  alternatives. Any effort budgeted for "make the trial unattended" should be
  reallocated to the divergence-report stability work that is the pilot's actual
  success bar.
- **`xstunit-229.24` (dongle station) is confirmed as the only route to
  continuous Grade B**, and its priority should be read as a function of how much
  the project wants nightly conformance *after* the pilot proves the harness — not
  as something the pilot itself might obviate.

---

## Sources

All fetched 2026-08-01. Beckhoff-published only.

- TwinCAT 3 test licenses — <https://infosys.beckhoff.com/content/1033/tc3_licensing/921947147.html>
- Creating trial licenses manually — <https://infosys.beckhoff.com/content/1033/tc3_licensing/3510308491.html>
- Special TwinCAT 3 license types — <https://infosys.beckhoff.com/content/1033/tc3_licensing/955881355.html>
- TwinCAT 3 Engineering (licensing) — <https://infosys.beckhoff.com/content/1033/tc3_licensing/179888523.html>
- Licensing process — <https://infosys.beckhoff.com/content/1033/tc3_licensing/920379275.html>
- Quick start (licensing) — <https://infosys.beckhoff.com/content/1033/tc3_licensing/63050397061788427.html>
- Licensing manual index (v1.20.7, 2025-09-16) — <https://infosys.beckhoff.com/content/1033/tc3_licensing/index.html>
- Licensing the 7-day test version of a TwinCAT 3 Function (TF5130) — <https://infosys.beckhoff.com/content/1033/tf5130_tc3_unival_plc/262609675.html>
- Licensing (TF6280) — <https://infosys.beckhoff.com/content/1033/tf6280_tc3_ethernetipslave/262609675.html>
- AI: Configuration of licensing hardware — <https://infosys.beckhoff.com/content/1033/tc3_automationinterface/4554517771.html>
- AI: Activating license response files — <https://infosys.beckhoff.com/content/1033/tc3_automationinterface/4554565515.html>
- AI: ITcSysManager — <https://infosys.beckhoff.com/content/1033/tc3_automationinterface/242753675.html>
- AI: Frequently Asked Questions — <https://infosys.beckhoff.com/content/1033/tc3_automationinterface/242685835.html>
- TE1000 ADS PowerShell module: About TcXaeMgmt — <https://infosys.beckhoff.com/content/1033/tc3_ads_ps_tcxaemgmt/11221779979.html>
- TcXaeMgmt on PowerShell Gallery (publisher: Beckhoff Automation GmbH) — <https://www.powershellgallery.com/packages/TcXaeMgmt/7.0.41>
- Security Management: Automated creation via a command line tool (`TcSignTool.exe`) — <https://infosys.beckhoff.com/content/1033/tc3_security_management/7888497419.html>
