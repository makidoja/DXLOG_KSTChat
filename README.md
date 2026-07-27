# DXLog KST Chat Bridge with AirScout

**Version 2.4.8 — DXLog.net custom form for ON4KST, AirScout and DXLog rotator control**

<img width="1195" height="571" alt="image" src="https://github.com/user-attachments/assets/3710b697-7908-43cb-be6e-572356030a27" />
/>


<img width="1920" height="1032" alt="image" src="https://github.com/user-attachments/assets/60e69401-749e-4d0d-9c24-7c0d39896375" />

## What is new in v2.5.0

- **Uniform main toolbar:** corrected the clipped **Distance** caption and aligned Setup, Map, Distance, AS, Auto sort, Need, Room and the connection controls on one consistent row.
- **Uniform Map toolbar:** renamed the map checkboxes to **Rotator**, **Show AS** and **AS Trails**, with equal-width control cells and consistent spacing.
- **Adjustable Name column:** the station **Name** column is now the only resizable contact-list column. Its selected width is saved and restored.
- **Fixed compact data columns:** Call, Loc, QTF, QRB, AS, Active, Msg and worked-band columns retain predictable fixed widths.
- **Clear QRB units:** the heading is now **QRB km** and each row contains only the numeric distance, saving horizontal space.
- **Full station popup:** hovering over a Name cell displays the complete decoded name together with the station locator, recent activity, worked state/bands, notes and AirScout details.

## What is new in v2.4.7

- Reworked the Map toolbar into fixed, non-shrinking command columns.
- Added a flexible spacer before **Close**, keeping the button pinned to the right without covering **Show AirScout path and aircraft**.
- Increased the Map window default and minimum width so all checkbox captions remain visible.
- The map canvas and status line now span the revised nine-column toolbar layout.

## What is new in v2.4.6

- **Protected minimum window width:** the KST Chat Bridge can no longer be resized so narrowly that Auto sort/Need cover the Room selector or connection buttons. Saved narrow layouts are expanded safely when restored.
- **Comprehensive interface tooltips:** added help text to the main toolbar, station and message areas, message controls, status lines, Setup dialog, macro editor and Map window.
- **Clear map-control help:** the map tooltips now distinguish **Show AirScout path and aircraft** from **Aircraft trails**, explaining that trails can be hidden without hiding the aircraft or stopping the feed.
- **Map minimum width:** prevents the Map toolbar controls and captions from being clipped into one another.

- Corrected clipping in the top toolbar so **Distance**, **AS**, **Auto sort**, **Need**, and **Room** all display in full.
- Rebalanced the fixed toolbar column widths without changing the overall window width or moving the Room/connection controls.

- **Aligned main toolbar:** Setup, Map, Distance, AS, Auto sort, Need, Room, Connect and Disconnect now share one consistent vertical baseline using normal DXLog/WinForms control heights.
- **Clear Auto sort label:** the previously abbreviated **Aut** option is now labelled **Auto sort**. When enabled, NOW and approaching AirScout opportunities are kept at the top of the station list.
- **Aligned message controls:** the CQ button, To field, message entry box, Send button and M1–M4 buttons now share one vertical baseline.
- **Matching To/message fields:** the To target is now displayed in a read-only text box matching the message entry field, eliminating the previous border and height mismatch.

- **Unread directed-message indicator:** the new **Msg** column shows an orange unread count for directed KST messages. Selecting that station or its message clears the count.
- **Per-band macro profiles:** M1–M4 are now stored separately for General, 50/70 MHz, 144/432 MHz, 1296 MHz, Microwave and EME operation. The active profile follows the current DXLog band/room automatically.
- **Need filter:** a new **Need** option combines with the existing Distance and AS filters to show stations not yet worked on the current DXLog band. Watched stations remain visible.
- **Prepare contact:** right-click a station and choose **Prepare contact** to place its call/locator into DXLog, open and update the map, request its AirScout path and focus the message field.
- **Persistent station notes:** add, edit or clear a note from the station context menu. Notes are retained between sessions and shown in the station tooltip.
- **AirScout feed health:** while the map is open, the status reports when the live aircraft HTTP feed has become stale and retains the last good aircraft data through temporary failures.
- **UI-latency diagnostics:** the performance window now reports current and maximum UI delay alongside KST, AirScout and map timings.
- **Rotator sequencing fix:** the DXLog callsign and locator are now allowed to finish updating before the bridge triggers DXLog's Ctrl+F12 rotator command. This restores reliable rotator control from the map.
- **Explicit rotator command:** right-click a station and choose **Turn rotator to N°** to enter the station into DXLog and turn the antenna using DXLog's normal rotator command.
- **DXLog compatibility:** the bridge now searches for compatible short-path rotator handler names and retains keyboard/direct-azimuth fallbacks.
- **Authoritative KST locator:** after DXLog performs its callsign lookup, the bridge overwrites the Loc/Grid/QRA field with the latest locator advertised by that station on KST.
- **Rotator on station selection:** a deliberate left-click on a KST station and **Prepare contact** now turn the rotator when **Turn rotator on click** is enabled in the Map window.
- **Single prepared station:** preparing another contact clears the previous highlighted station, leaving only the newly prepared station selected.
- **Persistent rotator preference:** the Map window's **Turn rotator on click** setting is retained between sessions.

This custom form combines the ON4KST chat service with DXLog.net. It displays the current KST station list and chat messages, inserts selected callsigns and locators into DXLog, sends directed messages and macros, calculates QRB/QTF, controls the DXLog rotator command, and uses AirScout to show aircraft-scatter opportunities automatically.

The bridge is supplied as source code and builds as an **x86 .NET Framework 4.8 class library**.

---

## Main features

- ON4KST classic telnet connection and room selection.
- Station list with callsign, name, QRA, QTF, QRB, graphical AirScout opportunity, last KST activity, unread directed-message count and selectable DXLog worked-band columns.
- Optional **Need** filter for stations not yet worked on the current band.
- Per-band M1–M4 macro profiles selected automatically from the current operating band.
- Persistent station notes and one-click **Prepare contact** workflow.
- General CQ and directed KST messaging.
- Four editable message macros using live DXLog frequency, band and mode.
- Double-click a station to enter its callsign and locator into DXLog.
- Automatic refresh of the KST station list every 10 seconds.
- Immediate worked-status and station-list refresh after a QSO is logged.
- Selected-station conversation panel.
- OpenStreetMap station map with pan, zoom and station selection.
- Optional DXLog rotator command when a station is selected on the map.
- Automatic AirScout scan of every KST station with a valid locator.
- Graphical **AS** column using a green blob for **NOW**, an orange blob plus minutes for an approaching aircraft, `-` for no suitable aircraft, or blank before a result is available.
- Optional automatic AS sorting and filters for **All**, **NOW**, or opportunities within 5, 10 or 20 minutes.
- Persistent station watchlist. Watched calls are marked with a gold star, stay visible through filters and are scanned first by AirScout.
- Optional audible/balloon AirScout alerts when a station crosses the configured opportunity threshold.
- Selected AirScout path and matched aircraft drawn on the KST map.
- Optional 90-second aircraft trails and watched-station highlighting on the map.
- Rich station tooltips with AirScout aircraft, opportunity, potential, intersection QRB, altitude, speed and track where available.
- Right-click the status line for lightweight performance diagnostics.
- DXLog colour/options menu is restricted to the green title bar; station, message, macro, map and status areas keep their own context actions.
- Window position, size, colours and macros saved between sessions.

---

## AirScout opportunity controls

The top bar includes an **AS** filter and an **Auto sort** checkbox:

- **All** — show every station allowed by the distance filter.
- **NOW** — show only current AirScout opportunities.
- **≤5m / ≤10m / ≤20m** — show stations with an opportunity inside that time.
- **Auto sort** — continually keeps NOW and approaching stations at the top.

Right-click a station and choose **Add to watchlist**. Watched stations:

- remain visible even when outside the current distance or AS filter;
- are marked with a gold star in the list and map;
- are queried first in each AirScout scan;
- remain watched after restarting DXLog.

AirScout alerts and aircraft trails are configured in **Setup → AirScout**.

### Additional macro tokens

Alongside `{CALL}`, `{MYCALL}`, `{FREQ}`, `{FREQMHZ}`, `{BAND}` and `{MODE}`, the bridge supports:

- `{LOC}` — selected station locator
- `{MYLOC}` — home locator
- `{QTF}` — bearing in degrees
- `{QRB}` — distance in kilometres
- `{AS}` — `NOW` or predicted minutes such as `5m`
- `{AIRCRAFT}` — best AirScout aircraft identifier
- `{ASMIN}` — predicted minutes as a number

Example:

```text
PSE SKED {FREQ} {MODE} QTF {QTF} AS {AS} {AIRCRAFT}
```

### Performance diagnostics

Right-click the bottom status line to view the latest KST refresh, AirScout reply, aircraft-feed and map-render timings. This is intended for contest testing and checking that the bridge is not causing long UI stalls.

---

## Requirements

### For normal use

- Windows 10 or Windows 11.
- DXLog.net installed in its standard 32-bit program folder.
- A valid ON4KST account and password.
- Internet access for ON4KST and OpenStreetMap tiles.

### For AirScout features

- AirScout running on the same PC.
- A working aircraft feed configured in AirScout.
- AirScout Network Server enabled on UDP port `9872` and HTTP port `9880`.

### For building the DLL

- Visual Studio 2022.
- .NET Framework 4.8 Developer Pack.
- DXLog.net installed so the project can reference:

```text
C:\Program Files (x86)\DXLog.net\DXLog.net.exe
C:\Program Files (x86)\DXLog.net\DXLogDAL.dll
```

---

# 1. Build the bridge

1. Extract the ZIP file to a normal writable folder.
2. Open `DXLogKstBridge.csproj` in Visual Studio 2022.
3. Select **Release** and **x86** in the Visual Studio toolbar.
4. Check that both DXLog references load without warning:
   - `DXLog.net`
   - `DXLogDAL`
5. Select **Build → Build Solution**.

The DLL will be created at:

```text
bin\x86\Release\DXLogKstBridge.dll
```

For a diagnostic build, use **Debug | x86**. Its output is:

```text
bin\x86\Debug\DXLogKstBridge.dll
```

## Reference errors

If Visual Studio cannot find the DXLog assemblies, remove and re-add the two references from the actual DXLog installation folder. The project must remain an **x86** build because DXLog.net is a 32-bit application.

---

# 2. Install the bridge in DXLog

1. Close DXLog.net completely.
2. Create the following folder if it does not already exist:

```text
%APPDATA%\DXLog.net\CustomForms
```

3. Copy `DXLogKstBridge.dll` into that folder.
4. Start DXLog.net.
5. Open the custom form from the DXLog **Custom** menu using **KST Chat Bridge**.

When updating an existing version, close DXLog before replacing the DLL. Keeping a copy of the previous working DLL is recommended.

---

# 3. Configure ON4KST

Open the bridge and click **Setup**.

Use these normal values:

```text
Host:       www.on4kst.info
Port:       23000
Room:       2              (144/432 MHz)
User/call:  your callsign
Password:   your ON4KST password
Name:       your name
QTH locator: your Maidenhead locator
```

The QTH locator is important. It is used for:

- QRB and QTF calculations.
- AirScout path calculations.
- Home-station position on the KST map.
- Home-centred map zoom.
- Rotator bearing calculations.

Click **OK**, then click **Connect**.

The room can also be changed later with the **Room** dropdown. If connected, the bridge reconnects automatically using the new room.

## KST room numbers

```text
1   50/70 MHz
2   144/432 MHz
3   Microwave
4   EME
5   LowBand
6   50/70 MHz R3
7   50/70 MHz R2
8   144/432 MHz R2
9   144/432 MHz R3
10  kHz (2000–630 m)
11  WARC (30/17/12 m)
12  28 MHz
13  40 MHz
```

---


## Worked-band columns

Open **Setup** and use **Worked-band columns** to choose which bands are shown in the station list. Available columns are:

```text
50, 70, 144, 432, 1296, 2320, 3400, 5760, 10G, 24G, 47G, 76G
```

A green tick means that callsign has been worked on that band in the current DXLog log. Unselected bands are hidden, which keeps the station list compact. The full worked-band summary remains available in the station tooltip.

By default, new installations show the **144** and **432** columns. Existing settings are retained in:

```text
%APPDATA%\DXLog.net\KstChatBridgeTelnet.ini
```

# 4. Configure an aircraft feed in AirScout

AirScout must show live aircraft on its own map before the bridge can produce useful AS results.

## Tested OpenSky configuration

In AirScout open:

**Options → Planes**

Choose:

```text
[WebFeed] OpenSky
```

Open **Settings** and set the URL to:

```text
https://opensky-network.org:443/api/states/all
```

A cycle of around `90` seconds is suitable for initial use.

For anonymous testing, leave these fields blank:

```text
OAuthClientID
OAuthSecret
```

The explicit `:443` is important with affected AirScout 1.4.x OpenSky plugins. Without it, the plugin may rewrite the working hostname to `api.opensky-network.org`, which can produce:

```text
Could not establish trust relationship for the SSL/TLS secure channel
```

After restarting AirScout, reopen the OpenSky settings and confirm that the URL still contains `:443`.

## Other aircraft feeds

The bridge does not depend directly on OpenSky. Any AirScout-compatible feed is suitable, including a local receiver, Virtual Radar Server, RTL1090 or another supported web feed. AirScout only needs to have current aircraft positions available internally.

---

# 5. Enable the AirScout Network Server

In AirScout open:

**Options → Network**

Enable:

```text
Activate Network Server
```

Use:

```text
AirScout UDP Server Name: AS
AirScout UDP Server Port: 9872
AirScout HTTP Server Port: 9880
```

Allow AirScout through Windows Firewall when prompted. Private-network access is sufficient when AirScout and DXLog are on the same PC.

The two interfaces have different jobs:

- **UDP 9872** — path queries and `ASNEAREST` opportunity replies.
- **HTTP 9880** — live aircraft positions from `/planes.json` for the KST map overlay.

---

# 6. Enable AirScout in the KST bridge

Open **Setup** in the KST Chat Bridge and enable:

```text
Enable AirScout UDP integration
UDP:  9872
HTTP: 9880
```

The station list uses these AirScout indicators:

- **Green blob** — the best aircraft opportunity is available now.
- **Orange blob plus `Xm`** — the best aircraft is approaching in the displayed number of minutes.
- **`-`** — AirScout replied but reported no suitable aircraft.
- Blank — that station has not yet been scanned.

Matched aircraft on the KST map use the same green/orange status colours. AirScout remains the prediction and aircraft-data engine; the bridge handles the DXLog/KST presentation and workflow.

The bridge status at the bottom-right can show:

- **AirScout: Off** — disabled in bridge Setup.
- **AirScout: Listening** — UDP listener is active, but no valid query/reply has completed yet.
- **AirScout: Waiting CALL** — a query has been sent and the bridge is waiting for AirScout.
- **AirScout: OK** — valid replies are being received.
- **AirScout: OK n/total** — automatic station scan is in progress.
- **AirScout: Error** — the UDP listener or AirScout setup failed.

A valid own callsign, own locator, KST station locator and DXLog radio frequency are all required for a path query.

---

# 7. Understanding the station list

The station list contains:

```text
Call | Name | Loc | QTF | QRB | AS
```

The **AS** values mean:

- `NOW` — AirScout reports an immediate/current opportunity.
- `5m` — the best reported opportunity is approximately five minutes away.
- `-` — AirScout replied, but no suitable aircraft was reported within the display window.
- blank — that station has not yet been queried, lacks a valid locator, or AirScout is unavailable.

Click the **AS** column header to sort the list with `NOW` first, followed by the lowest minute values. This makes it easy to choose a station whose aircraft-scatter opportunity is approaching.

Hover over a station row to see additional AirScout information, including:

- Aircraft identifier.
- Aircraft category.
- Minutes to opportunity.
- AirScout potential.
- Intersection QRB.

## Automatic scanning

The bridge automatically scans every current KST station that has a valid locator.

- One path is queried at a time.
- A path is skipped after a two-second timeout if no reply arrives.
- After a complete scan, the bridge waits 20 seconds and starts again.
- Changing the active DXLog band clears the old AS results and starts a new band-specific scan.
- The KST station list itself refreshes every 10 seconds.

With a large room, the first full pass takes longer because every valid station has to be queried.

---

# 8. Selecting stations and using DXLog

### Single-click a station

- Selects that callsign.
- Displays messages to/from that station in the lower message panel.
- Performs an immediate AirScout query for the selected path.
- Updates the selected path shown on the KST map.

### Double-click a station

- Inserts the callsign into the current DXLog entry line.
- Supplies the KST locator when DXLog does not already have a locator.

DXLog database information takes priority over KST locator information where available.

### Right-click a station

The context menu provides:

- Put the callsign into DXLog.
- Message the station.
- Copy the callsign.
- Send a custom message.
- **Show path in AirScout**.

Use **Show path in AirScout** when you specifically want AirScout’s own window to display that path.

---

# 9. Messages, CQ and macros

## CQ

Click **CQ** to send a general room message. This clears the directed-station selection for messaging.

## To call

Click **To call** to compose a directed message to the selected callsign.

## Macros

Click **Edit macros** to configure M1–M4. The default macros are:

```text
M1  PSE SKED {FREQ} {MODE}
M2  QRV {FREQ} {MODE}?
M3  I CALL YOU {FREQ} {MODE}
M4  TU 73
```

Supported replacements are:

```text
{CALL}      selected station callsign
{MYCALL}    your configured callsign
{FREQ}      DXLog frequency in plain kHz, for example 144750
{FREQMHZ}   frequency in MHz, for example 144.75MHz
{BAND}      active DXLog band
{MODE}      active DXLog mode
```

Macros are directed to the currently selected station.

---

# 10. KST map and aircraft overlay

Click **Map** in the bridge.

The map displays:

- Your configured home station.
- Current KST users with valid locators.
- The selected station.
- The great-circle path from home to the selected station.
- AirScout aircraft matched to the selected path.

## Map controls

- **Refresh** — immediately reload stations and current aircraft data.
- **Zoom +** — zoom in, centred on the home station.
- **Zoom -** — zoom out, centred on the home station.
- **Mouse wheel** — zoom in/out, centred on the home station.
- **Fit** — fit the home station and all listed stations into view.
- **Drag map** — temporarily pan the map. The next zoom action recentres on home.
- **Turn rotator on click** — selecting a map station also triggers DXLog’s rotator command.
- **Show AirScout path and aircraft** — enables or hides the selected path/aircraft overlay.

The map checks AirScout’s local `/planes.json` output every **5 seconds**. Actual aircraft movement is limited by the update interval of the aircraft feed configured in AirScout. For example, a 90-second OpenSky cycle will cause aircraft positions to update in larger steps even though the bridge checks every five seconds.

OpenStreetMap tiles are cached at:

```text
%APPDATA%\DXLog.net\KstMapTiles
```

---

# 11. Rotator control

The bridge uses DXLog’s existing short-path rotator command, equivalent to **Ctrl+F12**.

Before using it:

1. Configure the rotator normally in DXLog.
2. Confirm that Ctrl+F12 turns the antenna to the callsign in the current entry line.
3. Open the KST map.
4. Leave **Turn rotator on click** enabled.
5. Click a station on the map.

The bridge first puts the callsign/locator into DXLog, then triggers the DXLog rotator command after a short delay.

If the rotator does not move, test Ctrl+F12 directly in DXLog before troubleshooting the bridge.

---

# 12. Refresh intervals

```text
KST station list:                 every 10 seconds
AirScout full AS rescan pause:    20 seconds after a completed pass
AirScout per-path timeout:        2 seconds
KST map refresh:                  every 5 seconds
AirScout /planes.json check:      at most every 5 seconds
Selected path query:              immediate when selected
QSO logged refresh:               immediate/short delayed refresh
```

The aircraft feed’s own cycle remains the limiting factor for new aircraft positions.

---

# 13. Saved settings

The bridge stores its configuration at:

```text
%APPDATA%\DXLog.net\KstChatBridgeTelnet.ini
```

This includes:

- ON4KST host, port, room, callsign and password.
- Name and home locator.
- AirScout enable state and ports.
- Macros.
- Window position and size.
- Display colours and title-bar colour.

**Security note:** the ON4KST password is stored in this local INI file. Do not share the file, and protect access to the Windows account.

To reset all bridge settings, close DXLog and rename or delete `KstChatBridgeTelnet.ini`.

---

# 14. Troubleshooting

## The bridge does not appear in DXLog

- Confirm the DLL is in `%APPDATA%\DXLog.net\CustomForms`.
- Confirm the project was built as **x86** and targets **.NET Framework 4.8**.
- Close and restart DXLog after replacing the DLL.

## KST does not connect

- Check the ON4KST callsign and password.
- Confirm host `www.on4kst.info` and port `23000`.
- Check Windows Firewall and internet access.
- Try a different KST room only after normal login is confirmed.

## QTF and QRB are blank or incorrect

- Enter a valid own Maidenhead locator in bridge Setup.
- Confirm the remote KST station has a valid locator.

## AirScout remains Off

Enable **AirScout UDP integration** in bridge Setup.

## AirScout remains Listening

- Select a station with a valid locator.
- Confirm DXLog has a valid active radio frequency.
- Confirm AirScout Network Server is enabled.
- Confirm UDP port `9872` matches in both programs.

## AirScout shows Waiting but never OK

- Check AirScout UDP Server Name is `AS`.
- Check UDP port `9872`.
- Allow AirScout and DXLog through Windows Firewall.
- Confirm AirScout itself can calculate the selected path.

## AS cells contain only `-`

Communication is working, but AirScout is not reporting a suitable aircraft for those paths. Confirm live aircraft are visible in AirScout and that the correct band is active in DXLog.

## Aircraft do not appear on the KST map

- Select a station whose AS result has an aircraft.
- Enable **Show AirScout path and aircraft**.
- Confirm AirScout HTTP server port is `9880`.
- Confirm `http://127.0.0.1:9880/planes.json` is available locally.
- Confirm AirScout has a working live aircraft feed.

## OpenSky gives an SSL/TLS trust error

Use this exact AirScout OpenSky URL:

```text
https://opensky-network.org:443/api/states/all
```

The explicit `:443` prevents affected plugin versions from changing it to the failing `api.opensky-network.org` hostname. Check the setting again after restarting AirScout.

## Map is blank

- Check internet access for OpenStreetMap tiles.
- Click **Refresh** or **Fit**.
- Check the tile-cache folder is writable.
- Temporarily remove old cached tiles from `%APPDATA%\DXLog.net\KstMapTiles` if required.

## Rotator does not turn

- Confirm the rotator works from DXLog itself.
- Confirm Ctrl+F12 works with the active entry line.
- Confirm the selected station has a valid locator.
- Confirm **Turn rotator on click** is enabled.

---

---

# 15. Version 2.1 release notes

Version 2.1 is the first formal release of the redesigned DXLog KST Chat Bridge with integrated AirScout support. It promotes the tested v2.0 beta series and includes the following major improvements:

- DXLog-style setup and macro dialogs with centred **OK / Cancel** controls.
- Descriptive KST room dropdown and distance filtering from **All** to **0–2000 km**.
- Stable 10-second KST user refresh without blanking the current station list.
- Visible CQ/directed-message entry with keyboard routing that prevents DXLog from stealing typed text.
- Four right-click editable macros with expanded-message tooltips.
- **Active** and **Worked** station-list columns.
- Automatic AirScout scanning that completes each queue before accepting a replacement list.
- Band-change refresh of the KST list, map, worked state, selected path and AirScout queue.
- Cached, double-buffered map rendering with smoother resize and drag behaviour.
- Active stations shown green, inactive stations yellow and the selected station red.
- Collision-aware station and aircraft labels.
- A single dedicated home-station marker and label.
- HTML entity decoding in station names.
- Assembly and file version set to **2.1.3.0**.

The historical worked-band index uses best-effort reflection against the installed DXLog version. Newly logged QSOs are tracked directly. Operators should still verify operation with their normal contest, DVK and station configuration before a major event.

## Files in this source package

```text
DXLogKstBridge.cs       complete bridge source code
DXLogKstBridge.csproj   Visual Studio/.NET Framework project
README.md               installation and operating guide
RELEASE-NOTES-v2.1.md   concise release summary
```


## Version 2.1.1 maintenance update

- Fixed colour choices briefly applying and then reverting to the previous saved colour.
- Fixed **Reset default colours** so all defaults are applied immediately and saved.
- Enabled colour-change persistence regardless of which DXLog constructor opens the custom form.


## Version 2.1.3 maintenance update

- Updated the KST room names and ordering to match the current KSTChat v1.30.2 room menu.
- Replaced the obsolete separate microwave-band room labels with the current combined **Microwave** room.
- Added the current Region 2 and Region 3 room labels for 50/70 MHz and 144/432 MHz.

## Version 2.1.2 maintenance update

- Closing or hiding the main KST Chat Bridge window now also closes the associated Map window.
- The Map refresh timer and map resources are therefore disposed immediately with the bridge window.

## v2.4.1 maintenance fixes

- Incoming messages no longer change the selected station, map trace or AirScout path.
- Prepare Contact updates AirScout silently without raising the AirScout window.
- Prepare Contact now inserts the callsign and KST locator into the active DXLog entry line after the context menu closes.
- Use **Show path in AirScout** when you deliberately want AirScout brought to the foreground.
