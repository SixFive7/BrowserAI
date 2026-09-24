<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Windows toasts -- what one can show, and what it silently will not

What the Windows notification surface does with a toast this product would raise,
measured by raising real ones. It is here because the answer changed a design: the
update toast was drawn with five buttons, and five buttons cannot be read.

**Versions.** Windows **11 Pro 10.0.26200**, x64, at **3840x2160**. Raised through
`Windows.UI.Notifications.ToastNotificationManager` from PowerShell 7 under the
shell's own application id. Renderings, XML and rig:
[`docs/design/toast-2026-09-24`](../../docs/design/toast-2026-09-24/README.md).

## Every button is laid out in ONE row, and the row never wraps -- measured 2026-09-24

`[STABLE]` on the platform, `[MACHINE]` for the pixel scale.

⭐ **Five labelled buttons truncate to their first word.** A toast offering
*Review the open sessions*, *Ask again in 10 minutes*, *Ask again in 6 hours*,
*Not before the next reboot* and *Skip this version* rendered as **Revie**,
**Ask**, **Ask**, **Not**, **Skip** -- each button's leading emoji drawn as a
small icon above the word. Windows divides the available width by the number of
buttons and clips; it does not wrap to a second row, it does not shrink the font,
and **nothing reports that it happened**. The toast is valid, it is shown, and it
communicates nothing.

**Two buttons fit a real label** at this width: *Review 27 sessions* and *Ask me
later* both rendered whole. Three was not measured.

⭐ **The way to offer more than two choices is a selection input.** An
`<input type="selection">` is laid out **full width**, above the buttons, with its
own title, and an action bound to it with `hint-inputId` receives the chosen value.
That is how four snooze choices and two buttons fit in one toast, and it is what
the update toast uses.

## Three smaller properties, measured the same day

- **The identity line belongs to the application id, not to the XML.** A toast
  raised under the shell's id reads *Windows PowerShell* with PowerShell's icon.
  A `<text placement="attribution">` is a **second**, separate line under the body,
  and both are shown -- so a mock-up raised from a script shows the wrong identity
  above the right attribution, and the rendering is not wrong, it is somebody
  else's. `[STABLE]`
- **Button icons are all or none.** Windows draws an icon on a button only when
  every button in the toast declares one, each a 16 px white PNG on transparency.
  Adding one to a single button silently draws none. `[STABLE]`
- **`scenario="reminder"` is what keeps a toast on screen** until it is acted on
  instead of expiring into the Action Center, which is the behaviour an update
  prompt wants. `[STABLE]`

⚠️ **What is NOT established here is what any of this looks like at another scale
factor or on another Windows build.** Every rendering was taken on one display at
one resolution. The one-row property is a layout rule and is not expected to move;
*how many* buttons fit a label before clipping obviously does.

**Re-establish it** with `show.ps1` from the design directory, handing it each of
the three XML files in turn. It writes nothing, installs nothing and clears only
the calling shell's own toast history.
