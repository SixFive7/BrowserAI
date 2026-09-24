<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# The update toast -- 2026-09-24

The three toasts drawn for the moment a staged update is blocked by live
sessions, and the rig that rendered them. **v3 is the shape
[the decision](../../../DECISIONS.md#the-update-lane-the-sessions-that-hold-it-and-the-second-client)
records**; v1 and v2 are kept because the first one is what proved the platform
limit and the second is the step between them.

⚠️ **Nothing here is built.** The toast and the sessions page it opens are the
next version's work, listed in [`TODO.md`](../../../TODO.md). These are
renderings, taken by handing Windows the XML beside each one.

## The limit v1 found, which is why there are three

⭐ **Windows lays every toast button in ONE row and never wraps.** v1 offered
five labelled buttons -- review, ask in 10 minutes, ask in 6 hours, not until a
reboot, skip this version -- and Windows truncated each label to its first word:
*Revie*, *Ask*, *Ask*, *Not*, *Skip*, with each button's emoji rendered as a small
icon above the word. **Five labelled buttons cannot communicate anything**, and no
amount of shorter wording fixes it: the row is divided by the number of buttons.

v2 and v3 move the four snooze choices into a **selection input** -- a dropdown,
which Windows lays out full width -- and keep two buttons: *Review N sessions*,
green, and *Ask me later*, which reads the dropdown. The body's own click does
what the green button does.

| File | What it is |
|---|---|
| [`v1.png`](v1.png) / [`v1.xml`](v1.xml) | Five labelled buttons, truncated by Windows to their first words |
| [`v2.png`](v2.png) / [`v2.xml`](v2.xml) | The snooze choices as a selection input, two buttons |
| [`v3.png`](v3.png) / [`v3.xml`](v3.xml) | **The chosen shape.** Same as v2 with the final wording |
| [`show.ps1`](show.ps1) | Raises one of the three by name, through the Windows notification API |

## What the renderings do not show, and it matters twice

⚠️ **The identity line reads *Windows PowerShell*, with PowerShell's icon**,
because `show.ps1` raises the toast under the shell's own application id. Under
the configuration app's identity it would read **BrowserAI** with the product's
icon. The `BrowserAI` line visible under the body is a different thing -- an
explicit `placement="attribution"` text in the XML -- and both would be present in
the real one.

⚠️ **Button icons are all or none.** Windows draws an icon on a button only when
every button in the toast has one, and each must be a 16 px white PNG on
transparency. v3 names none, which is why none is drawn; adding one to the green
button alone would silently draw none at all.

⚠️ **The dropdown's default in these three files is *In 10 minutes*, and the
decision is six hours.** The XML carries `defaultInput="10m"` in v2 and v3, which
is what the renderings show; the decision the maintainer took names **6 hours** as
the default the X takes. A rendering is not a specification, and this line exists
so that nobody implements the toast by copying the attribute.

**All three renderings were taken at 3840x2160**, which is the maintainer's own
display, and all three lay out cleanly there. No other scale factor was measured.

⚠️ **The three images are CROPPED to the toast, and the originals are not kept.**
`show.ps1` captures the bottom-right 900x700 of the whole screen, so each capture
carried a large area of whatever was on the maintainer's desktop behind it --
an unrelated project's editor window. The crops are lossless, contain the whole
toast including its identity line and its shadow, and are the only copy; what was
removed is background and no part of it was ever the subject.

## Re-rendering one

```
powershell -File show.ps1 -XmlPath v3.xml -Shot v3.png
```

*Corrected 2026-09-24 (previously `pwsh -File show.ps1 -XmlPath v3.xml -Shot v3.png`)*:
`pwsh`, PowerShell 7.6.6, cannot load the WinRT toast type at all and stops at
the script's first line that names it, while Windows PowerShell 5.1,
`powershell`, loads it -- measured 2026-09-24 by loading the type in each shell
and raising nothing ([kb](../../../kb/windows/notifications.md)). `show.ps1`
passes Windows PowerShell's own app id, which is the identity line the images
show, so it only ever ran under that shell.

It clears the shell's own toast history for that application id, shows the toast,
waits 2.5 s and captures the bottom-right 900x700 of the primary screen -- which is
why every image here is that size and in that corner. It writes nothing but the
image it is given, and installs nothing. The version number and the session counts
in the body are literal text in the XML, so no reading of the real numbers is
needed to judge the layout.
