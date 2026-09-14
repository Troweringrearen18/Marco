# Marco

A desktop utility for **Windows 10/11** that forces windowed games into **borderless fullscreen**, without injecting any code into the game, without telemetry and without an installer: **a single portable `.exe`**.

A working replacement for *Borderless Gaming* (Steam), which stopped working properly after several updates. It's called **Marco** because that's exactly what it removes.

> Windows 10/11 · .NET 8 · WinForms · GPL v3 *(see [License](#license))*

<!-- TODO: screenshot of the main window (drag an image here, e.g. docs/screenshot.png) -->

---

## What it does

### Manual borderless, right away
- Lists every titled window: **process + title + status pill** ("borderless" in the accent color, "admin" in orange for elevated processes).
- **Contextual** main button (applies or undoes depending on the window state) and **double-click** to apply instantly.
- Undoing restores the window **exactly** to its original size, position and styles — even if borderless was applied by another process, or Marco was restarted in between (state is persisted to disk).

### Library + watcher (2 s)
- Mark a process as a **favourite** and the watcher applies borderless to it **automatically** when detected.
- **Re-applies** if the game regains its border (some do it when changing scene or resolution).
- **Respects** a manual undo while that window is still alive, **skips** elevated processes and **stops retrying** after repeated failures.

### Per-favourite options
Contextual "Options" button → themed dialog, per favourite:
- **Sizing mode**: stretch to monitor · remove borders only (no move, no resize) · custom size and position (auto-centred when no position is given).
- **Target monitor** by `DeviceName` (stable across reboots; if the monitor is gone, it falls back to the nearest one).
- **Always on top** (topmost, undone on restore).
- **Mute audio in the background** (WASAPI per pid, implemented by hand with no NuGet).
- **Delay in seconds** before the first apply (for games that reconfigure themselves on startup).

Watcher, CLI, button/double-click and hotkey all apply **the same** options.

### System tray
- **Left click** opens the window; menu with *Show*, *Pause automatic* (session only; pausing also unmutes) and *Exit*.
- Toggles for **start with Windows**, **start minimized** and **close = minimize**.
- **Single instance**: a second launch wakes the existing window instead of opening another one.

### Customisable global hotkeys
- Defaults: **Ctrl+Alt+B** (borderless/undo on the active window) and **Ctrl+Alt+L** (lock/release the mouse via `ClipCursor`; re-imposed while that window stays in the foreground and released when the window dies or Marco exits).
- **Remappable** from Settings: click the combo → the next key press becomes the new shortcut (Esc cancels). If the combo doesn't parse or another app already owns it, it falls back to the default with a balloon notification.

### Exclusions, help and more
- **Hide selected** removes a process from the list ("Show hidden (N)" brings it back). The watcher is unaffected.
- **Help** via the "?" button in the title bar: windowed mode, tricks for games without a windowed option, compatibility, etc.
- **30 languages**, English by default, switchable live (see [Languages](#languages)).
- **UI** with custom chrome (Lossless Scaling style): resizable, maximise that respects the taskbar, smooth per-pixel scrolling list, automatic light/dark theme based on background luminance, **4 customisable colors** (background / buttons-bars / accent / text) + rounded corners, integrated settings panel, persistent window size and transient notices in the title bar.

---

## CLI mode

Made for scripts and for testing without touching the GUI (the CLI is exempt from the single-instance lock):

```powershell
$exe = 'src\bin\Debug\net8.0-windows\Marco.exe'

& $exe --apply notepad     # borderless on every window of that process
& $exe --restore notepad   # undo (works even if the apply came from another process)
```

- Prints one line per window processed and applies the **favourite's options** if the process is in the library.
- **Exit codes**: `0` ok · `1` some operation failed · `2` no windows for that process.

---

## Install and usage

1. Download **`Marco.exe`** (in the root of this repo or under *Releases*).
2. Run it. It installs nothing, writes nothing outside `%APPDATA%\Marco\` and never touches the network.
3. Put the game in **windowed mode**, select it in the list and press the main button (or double-click).
4. Optional: "Add to library" so the watcher does it automatically from now on.

> **SmartScreen**: the exe is unsigned, so on other people's PCs Windows will warn the first time (normal for indie tools). *More info → Run anyway*.

### Old games with no windowed option in their menu

A common case (and the target audience for this tool): the game only starts in fullscreen and its video menu offers no "window" option. Marco **cannot** do anything with exclusive fullscreen — first you have to force windowed mode from the outside, in this order:

1. **Alt+Enter** inside the game (many DX9/DXGI titles support it even if the menu doesn't mention it).
2. **Launch parameters** (in Steam: Properties → Launch Options): `-window`, `-windowed`, `-w`, `-popupwindow` and `-screen-fullscreen 0` (the last two are Unity).
3. **Edit the game's config/registry**: look the game up on [PCGamingWiki](https://www.pcgamingwiki.com/), section *Video* → *Windowed*.

Verified case: **Space Marine Anniversary Edition** → `-window` in Steam. Careful: in that game the flag **breaks textures** (a game quirk). Always try **without tricks first**.

### The game runs as administrator (and Marco doesn't)

If every attempt to touch the window returns `ERROR_ACCESS_DENIED` (even moving it 1 px), the game is running at high integrity and **UIPI** is blocking the changes. Marco detects this and marks the row with "admin".

The usual cause is the `RUNASADMIN` flag in `HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers` on the game's exe (set by hand or by fix guides). **Better fix**: remove `RUNASADMIN` from that key (keeping other flags such as `WIN7RTM`) rather than elevating Marco. Elevating Marco just for this breaks the rest of the experience (tray, autostart, hotkeys) and isn't needed for most games.

---

## Build from source

Requirements: **.NET SDK 8** (tested with 8.0.419) on Windows.

```powershell
dotnet run --project src     # build + run the GUI
dotnet build src             # build only
```

### Portable publish (single exe)

```powershell
dotnet publish src -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Produces a **single self-contained `Marco.exe` (~68 MB)** (no runtime installation required). In Release the csproj's `PublishDir` drops it **in the root of the repo** (git-ignored), without a `.pdb`.

The version lives **only** in `<Version>` of `src/Marco.csproj`; the UI reads it from `Application.ProductVersion` and shows it as `v1.0` in the bottom-right corner.

---

## Architecture

| Component | What it does |
|---|---|
| `Program.cs` | Entry point: PerMonitorV2 DPI, CLI branch, single instance (`Local\Marco.InstanciaUnica`) and tray startup (`--bandeja`). |
| `WindowEnumerator.cs` | `EnumWindows` + `IsWindowVisible` + `GetWindowText`, shell/UWP/*cloaked* filtering and elevation detection via **token** (`TokenIntegrityLevel`). |
| `BorderlessService.cs` | The core: strip styles + position + topmost, and restore from `estado.json`. |
| `ConfigStore.cs` | `config.json` in `%APPDATA%\Marco\` (colors, library, hidden list, hotkeys, language…) with migration of the old format. |
| `AudioService.cs` | Hand-rolled WASAPI per pid (COM interfaces truncated but in exact vtable order): mute/unmute background audio. |
| `Textos.cs` | 30 translation dictionaries + English fallback (no `.resx`, no dependencies). |
| `MainForm.cs` | Main form: custom chrome, window list, watcher, tray, settings, dialogs and hotkeys. |

> Source code, comments and string keys are in Spanish (the author's working language) — that's intentional and consistent throughout.

### How borderless is applied (Win32)

**Stripping borders** — on `GWL_STYLE` it clears `WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU`, and on `GWL_EXSTYLE` it clears `WS_EX_DLGMODALFRAME | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE`. It uses `GetWindowLongPtr`/`SetWindowLongPtr` (the non-`Ptr` variants truncate on x64).

**Stretching to the monitor** — `MonitorFromWindow` + `GetMonitorInfo`, using **`rcMonitor`** (not `rcWork`: we want to cover the taskbar too). Then `SetWindowPos(..., SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOOWNERZORDER)` — **without `SWP_FRAMECHANGED` the border stays painted** even though the style is already gone.

### Known pitfalls (already handled, documented in case you touch the code)

1. **DPI**: without `ApplicationHighDpiMode = PerMonitorV2` in the csproj, at 125/150 % scaling the coordinates arrive virtualised and the window doesn't cover the monitor. This is *the* classic bug.
2. **UIPI/elevation**: against an elevated game, `SetWindowLongPtr` sometimes fails **silently**; you must check the return value and `GetLastError`.
3. **UWP/Store** (`ApplicationFrameWindow`): unsupported — they're filtered out of the list instead of half-failing.
4. **Exclusive fullscreen**: if the game isn't in windowed mode there's nothing to do (not a bug; see the tricks above).
5. **Restoring**: you must save `GWL_STYLE`, `GWL_EXSTYLE` and the `RECT` **before** touching anything, and put all three back. `WS_EX_TOPMOST` is not removed by restoring the saved exstyle: you must finish with `HWND_NOTOPMOST`.
6. **Games that re-impose their styles** on scene or resolution changes: the watcher covers those.

### User data

Everything lives in `%APPDATA%\Marco\`: `config.json` (preferences and library) and `estado.json` (original styles and `RECT` per window, so it can restore). Marco has **no network, no telemetry and no installer**; it automatically migrates the folder from the old name (`%APPDATA%\SinBordes`).

---

## Languages

30 languages, switchable live from Settings (English by default; English fallback when a key is missing):

English · Deutsch · Русский · Français · 简体中文 · Español (España) · 한국어 · Polski · 繁體中文 · 日本語 · Українська · Italiano · العربية · Български · Čeština · Dansk · Nederlands · Suomi · Ελληνικά · Magyar · Bahasa Indonesia · Norsk · Português (Brasil) · Português (Portugal) · Română · Español (Latinoamérica) · Svenska · ไทย · Türkçe · Tiếng Việt

The translations are our own (not copied from other projects). The less common ones would welcome a review by native speakers.

---

## Project status

**v1.0.0** — complete and distributable.

**Known outstanding items** (nothing blocks day-to-day use):
- Test **background muting** and **mouse locking inside a real game** (verified with a harness and Notepad, not yet in-game).
- Review the Help/Options dialog layout at **≥ 125 % scaling**.
- Notify in the UI when the **hotkey fallback** kicks in at startup (today it silently falls back to the default).
- Optional code signing to remove the SmartScreen warning.
- Rename the repo folder `SinBordes` → `Marco`.

**Deliberately out of scope**:
- **Linux**: on Wayland the compositor won't let an external app manipulate other windows; on X11 it would be 100 % different code with no way to test it. Besides, Proton and Linux WMs already offer native borderless.
- **Code injection / graphics proxies**: everything is done with public Win32 APIs from outside the game process (same approach as classic Borderless Gaming), which is what keeps it anticheat-friendly.
- **GPU effects** (FSR, upscaling, 390+ shaders): that's Lossless Scaling's niche and precisely what broke Borderless Gaming 1.x.

---

## License

GPL v3 — see [LICENSE](LICENSE) for details.

Marco is **original code written from scratch**. [Borderless-Gaming](https://github.com/Codeusa/Borderless-Gaming) and its classic version [andrewmd5/Borderless-Gaming](https://github.com/andrewmd5/Borderless-Gaming) (both GPL-2.0) were used **only as a behaviour reference** for edge cases: **no code or translations were copied** from them.

