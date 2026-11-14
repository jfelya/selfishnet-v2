# SelfishNet v2 — Live LAN / Adapter-Loss Test Plan

> **Safety first:** run only on an isolated test LAN you own. Do not disable the
> adapter that carries your internet connection. Use a dedicated USB Wi-Fi
> adapter or a test PC/VM for the disconnect tests.

## 1. Controlled LAN shaping

1. Use an isolated router/AP with a test PC and at least two client devices.
2. Start the x64 exe; select the test adapter; confirm Npcap is detected.
3. Test each engine (Legacy and Modern):
   - Set a download cap and transfer a large file to a client.
   - Set an upload cap and transfer from the client.
   - Enable **Block** and confirm traffic stops.
   - Clear the cap/Block and confirm traffic resumes.
4. Press **Stop** and confirm the clients recover.
5. Record the client ARP entries before and after the test.

**Pass:** caps/block take effect, Stop restores connectivity, no crash.

## 2. Adapter loss / Wi-Fi change

Use a **dedicated USB Wi-Fi adapter**, not the PC's primary connection.

1. Start shaping on the USB adapter.
2. Disable the adapter in Device Manager, or run (as admin, targeting the USB
   adapter only):
   ```powershell
   Disable-NetAdapter -Name "<USB adapter name>" -Confirm:$false
   ```
3. Confirm:
   - Shaping stops.
   - A friendly “adapter disconnected” message appears.
   - Old LAN rows clear.
   - The app prompts for the new adapter.
   - It does **not** auto-start shaping.
4. Re-enable the adapter, select it, and start shaping again.

**Pass:** no crash, no stale “Shaping” state, no automatic shaping on the wrong
network.

## 3. Real roaming (optional)

Use two APs on the same or different SSIDs. Start shaping, move the client
between APs, and confirm the app detects an IP/gateway change and follows the
same recovery path.

## Record

- App version / exe hash.
- Adapter name, IP, gateway.
- Client device and test file.
- Before/after ARP output.
- Screenshots of the error/recovery prompt.
- Any failed step and what the UI showed.

Do not run these steps on a production network.
