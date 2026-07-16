# DXLog KST Chat Bridge – AirScout v1.8

## v1.8 home-centred map zoom

- Zoom +, Zoom -, and mouse-wheel zoom now always centre the KST map on the operator/home locator.
- The map can still be dragged for temporary inspection, but the next zoom step returns the centre to home.
- The Fit button continues to fit all currently listed stations.
- Retains the compact AS column, selected AirScout path/aircraft overlay, and automatic station scanning.

# DXLog KST Chat Bridge – AirScout v1.7

## v1.7 narrower AS column

- Reduces the **AS** station-list column to a fixed compact width suitable for `NOW`, `Xm`, `-`, or blank values.
- Gives the recovered width to the **Name** column rather than leaving unused space.
- Retains the v1.6 selected-path and aircraft overlay, automatic AirScout scanning, and all previous fixes.

# DXLog KST Chat Bridge – AirScout v1.6

## v1.6 KST map path and aircraft overlay

- Clicking a KST callsign now keeps that station as the selected path on the bridge's own map.
- The map draws a great-circle line from the configured own QTH locator to the selected station locator.
- The bridge reads AirScout's local aircraft JSON output from `http://127.0.0.1:9880/planes.json`.
- Aircraft returned by AirScout for the selected path are matched to their live latitude, longitude, heading and altitude and drawn on the KST map.
- Aircraft labels show the identifier, `NOW` / minutes to opportunity, and altitude in thousands of feet.
- Aircraft colours distinguish immediate, near-term and later opportunities.
- A **Show AirScout path and aircraft** checkbox is included on the map.
- Setup now includes both the AirScout UDP port (default `9872`) and HTTP port (default `9880`).
- Automatic AS-column scanning continues in the background and cannot overwrite the path displayed on the KST map.
- v1.5 message-header correction and all previous bridge features are retained.

### AirScout requirement for the map overlay

In AirScout, **Options → Network → Activate Network Server** must be enabled. Keep the UDP server on `9872` and the HTTP server on `9880`, unless matching custom ports are entered in the bridge Setup. AirScout documents its aircraft-position output at `/planes.json`.

# DXLog KST Chat Bridge – AirScout v1.5

## v1.5 display correction

- Removes the large white blocks at the right of the two message-list headers.
- These blocks were unused owner-drawn header space, not functional scroll bars.
- The Message column now fills the available width while retaining a two-pixel border margin.
- AirScout v1.4 automatic station scanning and all existing bridge features are unchanged.

# DXLog KST Chat Bridge - AirScout build

This build keeps the existing DXLog/KST bridge features and adds first-pass AirScout integration.

## Existing features retained

- DXLog-style KST station and message lists.
- CQ button and directed **To call** messaging.
- M1-M4 directed macros.
- 10 second ON4KST `/SH US` station refresh.
- Immediate KST/worked-status refresh after a QSO is logged in DXLog.
- Station map and selected-station conversation pane.
- PSTRotator control, including the existing Ctrl+F12 action.
- Window position, size and colour persistence.

## AirScout integration

The bridge talks directly to AirScout using the established Win-Test-style UDP protocol.

- Default UDP port: **9872**.
- Protocol source/destination names: **KST** -> **AS**.
- A new **AS** column is shown in the KST station list.
- Selecting a KST station sends its current path to AirScout.
- Changing the focused DXLog radio/frequency refreshes the selected AirScout path.
- The selected path is refreshed every 15 seconds.
- AirScout `ASNEAREST` replies are shown as:
  - `NOW` - a reported aircraft opportunity is current/immediate.
  - `Xm` - best reported opportunity in X minutes.
  - `-` - AirScout replied but reported no suitable aircraft within the display window.
- Hovering a station row shows the best aircraft details: aircraft identifier, category, opportunity time, potential and intersection QRB.
- Right-click a station and choose **Show path in AirScout** to send `ASSHOWPATH` as well as the normal path update.
- The bottom-right status shows **AirScout: Off**, **Listening**, **OK** or **Error**.

The displayed aircraft is selected by highest AirScout potential, then the shortest intersection QRB, matching the established client behaviour. Opportunities more than 30 minutes away are not shown in the compact AS column.

## Setup

1. Start AirScout and make sure its compatible UDP/Win-Test network interface is enabled on the machine/network.
2. Open **Setup** in the DXLog KST Chat Bridge.
3. Enter a valid **User / call** and **QTH locator**.
4. Tick **Enable AirScout UDP integration**.
5. Leave the UDP port at **9872** unless your AirScout configuration uses a different port.
6. Click **OK**.
7. Select a KST station that has a valid locator while DXLog has a valid focused radio frequency.

The status should initially show **AirScout: Listening** and change to **AirScout: OK** when a valid `ASNEAREST` reply is received.

## Build

Build as an **x86 .NET Framework 4.8 class library** with the normal DXLog references available:

- `C:\Program Files (x86)\DXLog.net\DXLog.net.exe`
- `C:\Program Files (x86)\DXLog.net\DXLogDAL.dll`

Copy the resulting DLL from either:

- `bin\x86\Debug\DXLogKstBridge.dll`
- `bin\x86\Release\DXLogKstBridge.dll`

to:

`%appdata%\DXLog.net\CustomForms`

Restart DXLog or reload the custom form after replacing the DLL.

## Notes

- AirScout is disabled by default so existing users are unaffected until it is enabled in Setup.
- The AS column needs both stations' valid Maidenhead locators and a valid current DXLog radio frequency.
- This implementation independently implements the AirScout-compatible UDP message framing and does not require AirScout DLL references.

## v1.2 protocol fix

Corrected the AirScout legacy Win-Test UDP wire framing used by `ASSETPATH`, `ASSHOWPATH`, and `ASNEAREST`:

- data is now quoted on the wire;
- outgoing commands use the AirScout/Win-Test `?` + checksum convention;
- incoming replies treat the final byte as the checksum and tolerate an optional trailing NUL.

This fixes v1.1 remaining at `AirScout: Listening` even when the AirScout Network Server was enabled on UDP 9872.

## AirScout v1.3 diagnostic/frequency fix
- Maps the live DXLog VFO to AirScout's canonical band frequencies (e.g. any 140-150 MHz VFO value is sent as 1440000 in 100 Hz units), matching the established wtKST integration.
- The AirScout status now changes from `Listening` to `Waiting <CALL>` as soon as a valid ASSETPATH query is transmitted.
- The bridge status line reports the exact call, locator and AirScout QRG sent for easier diagnosis.

## AirScout v1.4 automatic station scan

The AS column is now populated automatically for the full current KST station list.

- The bridge builds a queue of all listed KST stations that have valid locators.
- It sends one `ASSETPATH` query at a time and waits for that station's `ASNEAREST` reply before moving on.
- A two-second per-path timeout prevents one bad path from stalling the scan.
- The complete list is rescanned after a 20-second pause, so aircraft opportunities stay current without flooding AirScout.
- The AirScout status can show scan progress such as `AirScout: OK 12/34`.
- Changing the DXLog band clears the old band-specific AS results and starts a fresh full scan automatically.
- The **AS** column can now be clicked to sort opportunities with `NOW` first, then `1m`, `2m`, etc.; `-` and blank entries sort last.
- Selecting a station still performs an immediate query, and **Show path in AirScout** remains available on the station right-click menu.
