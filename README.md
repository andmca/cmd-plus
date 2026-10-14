# CMD+

A tiny tabbed window for `cmd.exe`, made for Windows Server 2019, where Windows Terminal isn't supported. It also has a one-click launcher for GitHub Copilot CLI and copy/paste that works while Copilot is running.

Each tab is a real cmd window, so everything behaves exactly like normal cmd. It's a handful of C# files with no dependencies beyond the .NET Framework already included in Windows.

## Run it

**Option 1: download.** Grab `CMDPlus.exe` from the [Releases](../../releases) page and run it. Nothing to install.

The exe isn't code-signed, so Windows may show a SmartScreen warning the first time. Choose **More info → Run anyway**, or build it yourself (option 2).

**Option 2: build it yourself.** Clone or download this repo and double-click `build.cmd`. It produces `CMDPlus.exe` in the same folder using the C# compiler that ships with Windows, so you don't need Visual Studio or an SDK.

## Shortcuts

| Keys | Action |
| --- | --- |
| Ctrl+T or **+** after the tabs | New cmd tab |
| Ctrl+Shift+T | New Copilot tab |
| Drag with the mouse | Select text |
| Ctrl+Shift+C | Copy the selected text (**Ctrl+C** and **Enter** also copy while text is selected; **Esc** clears the selection) |
| Ctrl+Shift+V | Paste |
| Ctrl+W, a tab's **X**, or middle-click it | Close tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |

The toolbar and the tabs have buttons for these too. The shortcuts only apply while the CMD+ window is focused.

Closing a tab, or the whole window, asks you to confirm first, with an extra warning if a Copilot session would be ended. Typing `exit` in a tab closes it without asking.

If you run CMD+ as administrator, every tab runs as administrator too, and the window title shows **CMD+ (Administrator)**.

## Copilot

**+ Copilot** opens a tab that runs `copilot`, so the Copilot CLI needs to be installed and on your PATH. It starts in the last folder you used. The arrow next to the button picks a different folder, which is then remembered. When Copilot exits, the tab drops back to a normal cmd prompt.

**Paste** types the clipboard into the tab as if you'd typed it, so it works inside Copilot even if right-click paste doesn't. When pasting several lines into Copilot, CMD+ asks whether to join them into one line, since a line break can send the prompt early.

## Requirements

Windows with .NET Framework 4.x, which is built into Windows Server 2016 and later and Windows 10 and later.

## Publishing a release (maintainers)

Every push builds `CMDPlus.exe` with GitHub Actions. To publish a release with the exe attached, either:

- on GitHub, go to **Releases → Draft a new release**, create a tag such as `v1.0.0`, and publish it; or
- push a version tag: `git tag v1.0.0` then `git push origin v1.0.0`.

`CMDPlus.exe` is attached to the release a couple of minutes later.
