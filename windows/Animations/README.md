# Hatch mascot animations

The default mascot uses generated WinUI 3 Composition visuals. Only the selected state's visual is created. These sources require no runtime JSON parsing and support Native AOT.

| State | Trigger | Playback |
| --- | --- | --- |
| Idle | Normal desktop use | Loop |
| Greeting | First reveal, opening Quick Add, or waking from idle sleep | Once, then return to context; typing interrupts it |
| Thinking | A nonblank draft in Quick Add or the active main task page, or an open planning tip | Once, then hold the planning pose; subsequent keystrokes do not restart it |
| Celebrate | Capturing a task in either form, completing a task locally, or finishing My Day | Once, then return to context |
| Focus | Running focus session | Restrained loop |
| Sleep | Paused focus session or existing 30-second inactivity interval | Once, then still |
| Drag | Moving the mascot | Loop until release or capture loss |

Planning and celebration include the blue paperclip helper arm. Its pencil/checklist and star stay within the 256-pixel composition. Custom JSON animations remain whole-file overrides. Hidden mascots pause playback; mute and Windows animation preferences select a still frame. The existing fullscreen policy is unchanged.

Draft thinking ends when the form is cleared or dismissed, or the main task page is hidden, left or deactivated. An empty Quick Add form returns to idle after its greeting. Drag takes precedence over reactions; celebration briefly takes precedence over a draft, then restores the remaining context. Drafts take precedence over a background focus session. The existing inactivity timer stays stopped while a draft is active. All changes follow existing interaction events, without additional polling.

`Source/` contains the editable Lottie JSON files. `Generated/` contains C# output from LottieGen 8.2.250604 for WinUI 3, with partial classes for CsWinRT's Native AOT vtable generation. Use `../scripts/GenerateMascot.ps1` to regenerate them with a matching LottieGen executable.
