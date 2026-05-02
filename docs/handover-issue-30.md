# Issue #30 — Context Handover for Planning Session

**Date gathered:** 2026-05-02  
**Source branch:** `main`  
**Purpose:** Inputs for the planning chat designing a fix for incorrect chat-list/conversation assignment in wide-mode and RTL layouts.

---

## 1. Current Divider-Detection Signal

`WhatsAppRegionDetector.Detect()` uses a **column-wise per-row edge-strength scan with cross-row consistency voting** — there is no UIA, no single-line Canny edge, and no third-party CV library involved.

**What it does, in plain language:**

1. For every column `x` in the "plausible" horizontal range (10 %–90 % of the frame width), count how many sampled rows (one sample every 10 px) show a strong horizontal edge at that column. "Strong" means the sum of absolute B/G/R channel differences between pixel `x-1` and pixel `x` exceeds a threshold of 30.
2. The column with the highest such row-count that also meets a 70 % consistency floor (`≥ 70 %` of all sampled rows must show the edge) is declared the divider.
3. If no column meets that threshold within the plausible range, the detector checks outside the range and returns a specific failure reason ("divider out of plausible range" vs. "no strong edge found").

**What is cheaply available as a side effect:**

The inner loop (`CountConsistentRows`) produces, for the winning column `bestX`, a per-column row-count — i.e., how many rows have a strong *vertical* edge at that x-coordinate. That is the only column-level statistic currently stored. No per-region brightness average, no horizontal-edge count, and no pixel-variance map is computed.

The `ColumnDelta` helper at `WhatsAppRegionDetector.cs:96–104` computes the single-pixel B+G+R delta between adjacent columns. It is the lowest-level primitive currently used and is inlined inside the row-sampling loop. Any new heuristic that samples vertical strips down each pane (to count *horizontal* row-to-row edges) would need a parallel helper that computes deltas between rows `y-1` and `y` at a given column.

---

## 2. Current Side-Assignment Heuristic

**The current rule is purely positional — it is NOT a "narrower side = chat list" width comparison.**

After the divider column `bestX` is found, the assignment is:

```csharp
// WhatsApp Desktop is LTR: the chat list is always the left panel.
var chatListRect     = new Rect(0,      0, bestX,              frameHeight);
var conversationRect = new Rect(bestX,  0, frameWidth - bestX, frameHeight);
```

*(`WhatsAppRegionDetector.cs:71–73`)*

The left panel is unconditionally labelled `ChatListRect`. There is no width comparison anywhere in the file. Test 2 (`Detect_DividerRightOfCenter_ChatListIsAlwaysLeftPanel`) explicitly asserts this: when the divider is at `x=980` out of a 1280 px frame, the 980 px-wide left panel is still `ChatListRect`. The test comment reads: *"WhatsApp is LTR so the left panel is always the chat list, regardless of which side is narrower."*

**Note for the planning chat:** STATE.md describes the heuristic as "narrower side = chat list." That description is inaccurate relative to the code that actually shipped. The code uses left = chat list — which is a strictly positional rule. The two rules differ: the positional rule is correct for LTR wide-mode (where the chat list is on the left even if it happens to be the wider panel), but wrong for RTL (where the chat list is on the right). A width-based rule would be wrong in both wide-mode LTR and RTL, whereas the positional rule is wrong only in RTL.

---

## 3. UIA Exploration Status

**UIA was fully explored and confirmed non-viable.** A dedicated recon tool (`tools/UiaDump/`) was built, run against six WhatsApp layout variants (default/narrow/wide × chat-open/empty), and the tree was logged to disk.

**Finding:** WhatsApp Desktop (WinUI 3 shell) presents the following UIA tree:

```
(Window) Name='WhatsApp' ClassName='WinUIDesktopWin32WindowClass'
  (Pane)  Name='Non Client Input Sink Window' ClassName='InputNonClientPointerSource'
  (Pane)  Name='AppWindow Custom Title Bar'   ClassName='ReunionWindowingCaptionControls'
    (Button) Minimize / Maximize / Close
  (Pane)  ClassName='Microsoft.UI.Content.DesktopChildSiteBridge'
    (Pane)  ClassName='InputSiteWindowClass'  [Offscreen=True]
      (TitleBar) AutomationId='TitleBar'
      (Pane)  Name='' AutomationId='WebView' ClassName='Microsoft.UI.Xaml.Controls.WebView2'
  (TitleBar) Name='WhatsApp' AutomationId='TitleBar'
    (MenuBar) ...
```

*(Excerpt from `uia-dump-default-empty.log`, captured 2026-05-02 04:21:54)*

The `WebView2` pane is the leaf from UIA's perspective. The tree is **identical in structure** across all six dumps — no chat-list pane, no conversation pane, no AutomationId or Name property distinguishes left from right or identifies any chat content element. The tree is identical in the wide layout (Bounds change from 1278-wide to 1855-wide, but no new elements appear):

```
(Pane) Name='' AutomationId='WebView' ClassName='Microsoft.UI.Xaml.Controls.WebView2'
       Bounds=(32,81,1855,763)
```

*(Excerpt from `uia-dump-wide-empty.log`, captured 2026-05-02 04:25:47)*

**Conclusion:** WhatsApp Desktop's chat content (chat list, conversation, all panes below the title bar) is rendered entirely inside the WebView2 and is invisible to UIA. There are no `AutomationId`, `Name`, or `ClassName` properties that identify either pane. The original v0.2.0 plan of "UIA primary, CV fallback" was invalidated by this finding; the detector is now CV-only.

---

## 4. Existing Test Coverage

All 7 tests are in `tests/Gausslite.Core.Tests/Detection/WhatsAppRegionDetectorTests.cs`.

| # | Test name | What it asserts |
|---|-----------|-----------------|
| 1 | `Detect_DividerAt300_Returns_CorrectRects` | LTR layout with divider at x=300: chat list rect = (0,0,300,800), conversation rect = (300,0,980,800). |
| 2 | `Detect_DividerRightOfCenter_ChatListIsAlwaysLeftPanel` | Divider right of centre (x=980/1280): the wider left panel is still labelled chat list — positional rule, not width rule. |
| 3 | `Detect_UniformFrame_Fails_NoStrongEdge` | A frame of uniform colour produces no edge; result is `Succeeded=false`, reason contains "no strong edge". |
| 4 | `Detect_DividerTooCloseToEdge_Fails_OutOfPlausibleRange` | Edge at x=20 is outside the 10–90 % plausible range; result is `Succeeded=false`, reason contains "out of plausible range". |
| 5 | `Detect_FrameTooSmall_Fails` | Frame smaller than 200×200 px returns `Succeeded=false`, reason contains "frame too small". |
| 6 | `Detect_StrideLargerThanMinimum_ReadsCorrectly` | GPU row-pitch padding (stride=4096, width=800) does not corrupt pixel reads; correct split at x=250 is returned. |
| 7 | `Detect_ThreeRowConsensus_IgnoresTransientHighlight` | A bright yellow highlight spanning columns 600–610 at a single row does not beat the full-height divider at x=300. |

**Coverage gaps a new heuristic must fill:**

- RTL: chat list on the right side (no test exists for this case).
- Wide mode with open conversation where the conversation pane is wider than the chat list (all existing tests have a narrow left panel or a symmetric frame; none explicitly verify side assignment under a reversed-width scenario).
- A test where horizontal-edge density differs between the two panes (the proposed fix mechanism — no such synthetic frame exists yet).

---

## 5. RegionDump Artifacts

All six PNGs are in the repository root (not committed to a subfolder). They were produced by `tools/RegionDump/RegionDump.exe <label>`.

| File | Layout | Chat list location | Notes |
|------|--------|--------------------|-------|
| `region-dump-default-raw.png` | Default LTR (~1278 px wide) | Left panel, divider ≈ x 490 | No conversation open; right pane shows "Send document / Add contact / Ask Meta AI" placeholder icons. |
| `region-dump-default-annotated.png` | Same | Green "CHAT LIST" box left; red "CONVERSATION" box right | Annotation is correct for this layout. |
| `region-dump-narrow-raw.png` | Narrow LTR (~930 px wide) | Left panel, divider ≈ x 295 | Chat list squeezed; contact names and timestamps truncated. |
| `region-dump-narrow-annotated.png` | Same | Green left; red right | Annotation correct. |
| `region-dump-wide-raw.png` | Wide LTR (~1847 px wide) | Left panel, divider ≈ x 640 | Chat list is WIDER than in default/narrow. Conversation pane is also wider but still on the right. |
| `region-dump-wide-annotated.png` | Same | Green left; red right | Annotation correct for this LTR wide layout. |

**Wide-mode observation:** In the wide-annotated image the green "CHAT LIST" label covers a ~640 px panel on the left and the red "CONVERSATION" label covers the remaining ~1207 px on the right. The positional (left = chat list) heuristic gives the correct assignment here, because no conversation is actively open — the conversation pane shows the placeholder empty state.

**Flag — the screenshot does NOT demonstrate the failure case described in issue #30.** The issue says "if the conversation is narrowed (split-screen with another window) the chat list becomes the wider pane and the labels invert." The existing wide-raw PNG was captured without an active conversation open, so the conversation pane is the empty-state placeholder. To demonstrate the failure in a real wide-window + open-conversation scenario, a fresh capture would be needed. Under RTL WhatsApp the failure is structural and would appear regardless of window width.

---

## 6. Issue #30 Current Text

Retrieved via `gh issue view 30 --json title,body,comments,state` on 2026-05-02. No comments have been added since the original description.

---

**Title:** Region detection: incorrect chat-list/conversation assignment in wide-mode and RTL layouts  
**State:** OPEN  
**URL:** https://github.com/mohamedasem318/Gausslite/issues/30

---

> ## Summary
>
> WhatsAppRegionDetector and RegionDump currently assume left-panel-is-chat-list, which is wrong in two real-world cases:
>
> 1. **Wide-mode WhatsApp:** the chat list has a max width (~400px) but the conversation pane can stretch much wider. In typical wide-mode usage the heuristic happens to work, but if the conversation is narrowed (split-screen with another window) the chat list becomes the wider pane and the labels invert.
>
> 2. **RTL languages (Arabic, Hebrew):** the chat list is on the right side. RegionDump's annotation code is hardcoded LTR and would show 'CHAT LIST' over the conversation pane in RTL.
>
> ## Why this isn't a bug today
>
> v0.2.0 ships region-aware blur scaffolding with a known limitation: detection assumes English LTR WhatsApp at default/narrow widths. Whole-window blur (the privacy primitive) is unaffected by this issue.
>
> ## Proposed fix (post-v0.2.0)
>
> Replace any width or position-based heuristic with **horizontal-edge density analysis**:
>
> 1. After finding the vertical divider, sample a thin vertical strip down the middle of each pane.
> 2. Walk each strip top-to-bottom, counting strong horizontal edges (per-row delta exceeds threshold).
> 3. The pane with significantly more evenly-spaced horizontal edges is the chat list — chat-list rows are ~72px tall and uniform; conversation messages are irregular.
> 4. Assign ChatListRect / ConversationRect based on edge-density winner, not position or width.
>
> Locale-independent, width-independent, theme-independent.
>
> ## Testing requirements when implemented
>
> - Synthetic frame test: high-density horizontal-edge strip on left -> chat list on left
> - Synthetic frame test: high-density horizontal-edge strip on right -> chat list on right (RTL case)
> - Smoke test on real Arabic WhatsApp via WhatsApp's in-app language setting
> - Smoke test on wide-mode where conversation is narrower than chat list
>
> ## Origin
>
> Identified during v0.2.0 region-aware blur development. Smoke-tested with RegionDump in default LTR English WhatsApp and worked correctly there. Wide-mode and RTL counter-examples surfaced after the smoke test.
>
> ## Track context
>
> This is a polish issue. v1.0 IDD architecture may make this issue largely irrelevant (different privacy model, region detection becomes optional UX rather than core feature).

---

## 7. Open Observations

One discrepancy worth flagging: STATE.md and issue #30 both describe the current heuristic as "narrower side = chat list," but the actual code uses a strictly positional rule — left panel is always chat list, with no width comparison at all. This matters for the fix design: the positional rule is already correct for LTR wide-mode (chat list is on the left even when it is the wider panel), so there is no LTR wide-mode bug in the *current code*. The actual failing case is RTL only, where the chat list is structurally on the right side. The issue description's wide-mode scenario ("split-screen narrows the conversation pane until the chat list is wider") would only be a real failure if the original width-based heuristic were in place — under the current positional rule, that scenario still assigns correctly because position does not change. The planning chat should clarify this before deciding how broad the fix scope needs to be (RTL-only vs. RTL + pathological wide-mode).
