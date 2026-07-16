# DXLog KST Chat Bridge with AirScout

**Version 1.9 — DXLog.net custom form for ON4KST, AirScout and DXLog rotator control**

This custom form combines the ON4KST chat service with DXLog.net. It displays the current KST station list and chat messages, inserts selected callsigns and locators into DXLog, sends directed messages and macros, calculates QRB/QTF, controls the DXLog rotator command, and uses AirScout to show aircraft-scatter opportunities automatically.

The bridge is supplied as source code and builds as an **x86 .NET Framework 4.8 class library**.

---

## Main features

- ON4KST classic telnet connection and room selection.
- Station list with callsign, name, locator, QTF, QRB and AirScout opportunity.
- General CQ and directed KST messaging.
- Four editable message macros using live DXLog frequency, band and mode.
- Double-click a station to enter its callsign and locator into DXLog.
- Automatic refresh of the KST station list every 10 seconds.
- Immediate worked-status and station-list refresh after a QSO is logged.
- Selected-station conversation panel.
- OpenStreetMap station map with pan, zoom and station selection.
- Optional DXLog rotator command when a station is selected on the map.
- Automatic AirScout scan of every KST station with a valid locator.
- Sortable **AS** column showing `NOW`, `Xm`, `-`, or blank.
- Selected AirScout path and matched aircraft drawn on the KST map.
- Window position, size, colours and macros saved between sessions.

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

The room can also be changed later with the **Room** button. If connected, the bridge reconnects automatically using the new room.

## KST room numbers

```text
1   50 MHz
2   144/432 MHz
3   1296 MHz
4   2.3/3.4 GHz
5   5.7/10 GHz
6   24 GHz and up
7   EME
8   MS
9   144/432 MHz IARU R3
10  2000–630 m
11  WARC 30/17/12 m
12  28 MHz
13  40 MHz
```

---

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

# 15. Version 1.9 notes

Version 1.9 adds automatic decoding of HTML character entities in KST station names. Values such as `&#9889;`, `&#8482;` and `&amp;` now display as their intended characters instead of raw entity text.

# 16. Version 1.8 notes

Version 1.8 includes:

- Automatic full-room AirScout scanning.
- Compact sortable AS column.
- Selected path and aircraft overlay on the KST map.
- AirScout UDP and HTTP port settings.
- Five-second aircraft-map refresh.
- Message-header white-block correction.
- Home-centred button and mouse-wheel zoom.
- Map dragging with the next zoom returning to the home station.
- Ten-second KST station refresh and QSO-triggered refresh.
- Existing CQ, directed messaging, macros, map and rotator functions.

---

## Files in this source package

```text
DXLogKstBridge.cs       complete bridge source code
DXLogKstBridge.csproj   Visual Studio/.NET Framework project
README.md               this guide
```
