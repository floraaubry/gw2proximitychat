# GW2 Proximity Chat

Blish HUD module for proximity-based voice chat: reads MumbleLink position/instance
data, sends it to a relay server, and mixes in nearby players' voices attenuated by
in-game distance. Two parts:

- **`GW2ProximityChat.csproj`** (net472) — the Blish HUD module: MumbleLink read-out,
  mic capture/Opus encode, Opus decode/mix/playback, and a tabbed settings/debug
  window (see UI section below).
- **`GW2ProximityChat.Server/`** — the relay server: a small Python `asyncio`/
  `websockets` app that groups clients by `(MapId, InstanceKey)`, broadcasts each
  group's position roster, and relays Opus audio frames between members of the same
  group. Deliberately dependency-light (one PyPI package) for easy Linux hosting.

## Protocol

Single WebSocket connection per client to `ws://<host>:<port>/`. JSON text frames
carry state/roster (property names are exact PascalCase — the client serializes with
.NET's `JavaScriptSerializer`, not a camelCase-by-default library, so `server.py`
reads/writes the same PascalCase keys rather than Python's usual snake_case/camelCase).
Binary frames carry raw Opus: client→server frames are the bare payload; server→client
frames are prefixed with `[1 byte id length][id bytes][opus payload]` so the client
knows which peer to attribute decoded audio to.

Instance filtering is a hard cutoff enforced by server-side group membership — a
client never receives state or audio for a different `(MapId, InstanceKey)`, rather
than receiving it and computing a numeric gain of zero.

**Auth and server identity**: the server has a name (`--name`, shown to clients
regardless of auth outcome) and an optional password (`--password`; omit it and
anyone can connect, same as before this existed). On every connection the server
immediately sends `{"Type":"hello","ServerName":"..."}`, unauthenticated -- so the
client can display the name even if the connection is about to be rejected. The
client's `StateMessage` carries a `Password` field on every send; the server only
actually checks it on the first state message of a connection (the one that
establishes identity) via plain equality against `--password`, and only when a
password was configured at all. A mismatch gets `{"Type":"auth_failed","Reason":
"Invalid password"}` followed by the server closing the connection; since there's no
auto-reconnect at all (see below), this just means the corner menu's "Connect" item
sits there until the user fixes the password and either applies it in the settings
window or clicks Connect again. This is a plaintext shared secret over an
unencrypted `ws://` connection -- adequate for keeping randoms off a small
home-hosted friends server, not a real auth system.

**No auto-connect**: the module never connects to the relay on its own -- not on
module load, not on a timer, not after an unexpected drop. `ProximityService.Tick`
used to run a background reconnect-every-5s loop; that's gone entirely. Connecting
is exclusively user-initiated, via the corner icon's menu (see UI) or the settings
window's Apply button -- and Apply only reconnects if a connection was already
active/attempted (including a prior auth failure), not from a cold state, so editing
settings before ever connecting doesn't itself trigger a connect.

**Connection stability**: `ClientWebSocket` only supports one send in flight at a
time, but state (10Hz) and audio (up to ~50Hz while talking) were originally sent
from independent fire-and-forget calls with no mutual exclusion between them — and
separately, .NET Framework's `ClientWebSocket` sends its own automatic keep-alive
PING on the same socket outside of app code entirely. Either one racing against an
app-level send is enough to knock the socket into an unusable "Aborted" state, which
is what periodic "Lost connection" drops with no actual network cause were. Fixed by
serializing every send (state, audio, keep-alive) through one lock in `RelayClient`,
disabling the client's automatic keep-alive (`KeepAliveInterval = TimeSpan.Zero`) and
the server's (`ping_interval=None`), and adding an explicit app-level keep-alive
(`ProximityService` sends a no-op `{"Type":"ping"}` every 15s whenever MumbleLink
isn't available and state isn't already flowing) so an otherwise-idle connection
still produces traffic and doesn't get dropped by a NAT/router in between.

## UI

**Corner icon**: left-clicking it does *not* toggle the settings window any more --
it opens a `ContextMenuStrip` (`GW2ProximityChatModule.BuildCornerMenuItems`,
rebuilt fresh every time the menu opens so it never shows stale state), in order:
Server name and Status (both non-interactive, `Enabled = false` -- just display),
Connect/Disconnect (single item, label and action flip based on
`ProximityService.IsConnected`), Mute/Unmute Microphone, Mute/Unmute Output (new --
`AudioService.OutputMuted`, independent of the Output Volume slider so unmuting
restores whatever volume was set rather than needing to remember it), and Open
Settings Window.

The settings window itself is still reachable via `Ctrl+Alt+M` (default,
rebindable) or Blish HUD's own "Manage Modules" settings area for this module --
which would otherwise show up completely empty (everything's hidden, see below), so
`GW2ProximityChatModule` overrides `Module.GetSettingsView()` to show a single,
centered "Open GW2 Proximity Chat Settings" button there instead of the default
auto-generated (and in this case empty) settings list. Every actual setting is
driven by that custom window rather than Blish HUD's native settings menu, because
the native settings UI can't do a dynamic device dropdown and always puts a slider
on every `int`.

Both tabs are laid out as bordered, titled category groups (`FlowPanel` inherits
`Panel.Title`/`ShowBorder` -- same look as `TurtleMyWaypointWindow`'s per-region
panels) with `ControlPadding`/`OuterControlPadding` for breathing room between rows,
inside a `CanScroll` container sized to the tab's content area so a long settings
list scrolls instead of overflowing the window. Widths are explicit pixel math
throughout (category panels, rows, and every control inside them), the same approach
`TurtleMyWaypointWindow`'s `CoreTyriaView`/`MapRowBuilder` use, rather than leaving
things to auto-size -- `panelWidth = contentWidth - PanelMargin`, and each row's
control fills whatever's left after its label. The scroll container's height also
adds a fixed `ExtraHeight` (60px) on top of the tab's nominal content height.

The Input/Output Device rows are stacked (label above, dropdown below at full
category width) rather than side-by-side -- real device names are long enough to
get clipped next to a label at half the row width. The mic level meter has a caption
explaining what it shows: the fill (green -> yellow -> orange/red) is how loud the
mic currently is; the **cyan** vertical line is the Noise Gate threshold from the
slider above it -- in Voice Activity mode, only audio louder than that line
transmits. (It used to be a red line, which read as "too loud" instead of "this is
the gate" since the fill itself also turns red at high volume -- changed to cyan to
stop overloading red with two different meanings.) Same window
chrome (dimensions, background texture) as this user's other Blish HUD modules
(WIMPSNA, TurtleMyWaypoint) -- `TabbedWindow2`, `ref/window_bg.png` copied from
WIMPSNA (it's generic art, not project-specific), no emblem since none of the
existing ones fit a voice-chat module.

**General tab**: a Relay Server group showing live connection status and the
server's name, host/port/password textboxes (password is plaintext -- Blish HUD has
no password-masked field) + an Apply button; microphone enable checkbox; Activation
Mode dropdown (Push to Talk / Voice Activity); rebindable keybindings via
`KeybindingAssigner` for Push-to-Talk (hold), toggle mic enabled, and toggle this
window; input volume, output volume, and noise gate sliders (`TrackBar`,
percent-based); a live mic level meter (`MicLevelMeter` -- Blish HUD has no native
progress bar control, so it's two nested `Panel`s) with a marker showing where the
noise gate threshold sits, so gate/volume can be calibrated by ear+eye while
talking, whether or not the mic is actually enabled; input/output device dropdowns
(real `Dropdown` controls populated from `WaveIn`/`WaveOut` device names, "System
Default" = Sound Mapper/-1 first, with full untruncated names -- `AudioDevices.cs`
cross-references the WinMM device list's 31-char-truncated names against
`NAudio.CoreAudioApi.MMDeviceEnumerator`'s full ones by prefix match).

**Debug tab**: unchanged from before -- raw MumbleLink fields, connection status,
peer roster with computed distance/gain/pan.

All keybindings set `BlockSequenceFromGw2` (consumed by the module, never reaches the
GW2 client). Push-to-Talk is polled via `KeyBinding.IsTriggering` every tick (true
while physically held, not just a one-shot press event) rather than a toggle.

Connection lifecycle (connecting, connected, lost connection with the reason,
reconnect attempts) is logged through Blish HUD's own logger (`Blish_HUD.Logger`,
NLog-backed) at Info/Warn level from `ProximityService`, so a flaky relay connection
shows up in Blish HUD's log file/console rather than only being visible by having the
debug window open at the exact moment it drops.

Every setting (including keybindings) lives in a `settings.AddSubCollection(...)`
with `renderInUi` left at its default `false`, so none of it appears in Blish HUD's
own settings menu -- persistence without the native UI.

## Running the server

```
cd GW2ProximityChat.Server
pip install -r requirements.txt
python server.py                  # listens on 0.0.0.0:5847
python server.py 6000              # or pass a port explicitly
python server.py --host 127.0.0.1 6000
python server.py -v                # verbose: log every state/audio frame, not just events
```

For a real multi-machine test, forward that port through your router/firewall and
point the module's "Relay Server Host" setting at your public IP/DNS name.

By default the server logs one line per connect/disconnect, per client identified
(name/map/instance), per group change, and per "started talking" transition (plus a
periodic frame/byte count summary for anyone actively sending audio) — enough to
confirm data is actually flowing without being swamped. `-v` additionally logs every
individual state and audio frame plus the WebSocket handshake, which is a lot of
output but useful for tracing a specific bug.

## Status

All milestones from the original build order are implemented: MumbleLink read-out,
server skeleton, position sync, instance filtering, mic/speaker capture via NAudio,
Opus via Concentus, audio relay, and client-side per-peer gain application. Verified
so far:

- The client module builds clean against Blish HUD 1.3.0 (net472).
- `server.py` runs clean under the installed `websockets` package, and its roster
  broadcast, hard instance-cutoff filtering, audio-frame sender tagging, and
  disconnect cleanup were all exercised with a scripted 3-client WebSocket test (not
  checked in — scratch-only) before and after porting it from the original C#
  implementation, with identical results both times.

User has been live-testing the module against Blish HUD directly (not just this
environment's builds), which is how the truncated-device-name, mic-level-meter, and
layout-height issues got caught and fixed. Still not yet verified:

- Actual voice audio quality/latency end-to-end.
- Whether `ServerAddress`/`ShardId` really are stable across two real clients in the
  same vs. different map instances (the debug window's raw section is exactly the
  tool for checking that).
- The pan sign in `GainCalculator.ComputePan` — the axis convention (X=right, Y=up,
  Z=front) is from Mumble's documented positional-audio spec, but the left/right sign
  hasn't been confirmed against real gameplay. If a peer's voice pans to the wrong
  ear, flip the sign of `right` in `GainCalculator.ComputePan`.
- Whether proximity-based gain is actually audible in-game (the wiring is confirmed
  correct by reading the code -- `ProximityService.RecomputeGains` ->
  `AudioService.SetPeerGain` -> `PeerAudioTrack.Read` -- but hasn't been confirmed by
  ear).
- The new auth/server-name flow (`hello`/`auth_failed`/`Password`) against the real
  Blish HUD client -- only verified so far via `server.py` itself (a scripted mock
  WebSocket client hit both the wrong-password-rejected and correct-password-accepted
  paths and got exactly the expected messages back) and a clean C# build; the
  `RelayClient`/`ProximityService` handling of `hello`/`auth_failed` hasn't been
  exercised against a live server from inside Blish HUD yet.
- The corner-icon `ContextMenuStrip` -- API (`AddMenuItem`/item `Click`/`Enabled`)
  was confirmed by reading the actual Blish HUD source, and it's part of a clean
  build, but never seen rendered; check that non-interactive items (Server/Status)
  actually render as inert text rather than clickable-but-no-op, and that the menu
  positions sensibly near the corner icon.
