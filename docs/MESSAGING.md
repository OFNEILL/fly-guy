# Fly messages

The Windows app can receive owner-written messages as a speech bubble beside its fly. A separate .NET relay holds the user list and routes messages to individual recipients. Receiving is opt-in; **Disconnect** stops it. The Mac frontend does not yet receive messages.

## Local setup

From the repository root, run this in PowerShell and leave it running:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Start-Messaging.ps1
```

The relay listens only at `http://127.0.0.1:5187`. The script creates a random admin key, protected with Windows DPAPI under your Windows account. It is saved at `%LOCALAPPDATA%\FlyGuy\relay-owner.json`. It is never bundled with the desktop app or printed to the console. Another Windows account cannot decrypt this file.

Launch the updated Windows app:

```powershell
dotnet run --project FlyGuy/FlyGuy.Desktop -c Release
```

1. Open **Fly messages** in the inspector. In **Receive messages**, choose a display name, **Connect / receive messages**. That copy of the app now appears in your user list.
2. Open the **Admin** tab, click **Use local owner key**, then **Sign in as admin**. The list refreshes every ten seconds and shows names, a short user ID, online/offline status and queued messages.
3. Select a user, type up to 500 characters, and click **Send as the fly**. The recipient's fly displays the text in a bubble for twelve seconds. Bubbles follow the fly, stay inside its monitor, and do not take keyboard focus or intercept clicks.
4. **Sign out** removes the admin session from the panel. Closing the panel also signs out. The admin key is not saved with recipient settings.

No one else can receive messages from this localhost setup on their own computer. It is ready for local testing; sharing the app alone does not connect other people to it.

## Connecting other computers later

### Browser message desk

Run `powershell -ExecutionPolicy Bypass -File scripts/Start-Messaging.ps1 -CopyOwnerKey`, then open **http://127.0.0.1:5187** in your browser. Paste the copied owner key to unlock the desk, then clear your clipboard and clipboard history if enabled. Select a recipient and send a message. The list refreshes every ten seconds and includes online status and queued message counts. Sign out or reload to clear the key from page memory; the page does not save it in browser storage. Only paste the owner key into a trusted relay page.

This is a local interface for the existing relay. It does not make localhost accessible from another computer. For someone on another network, the relay still needs a reachable HTTPS address as described below, and both machines need internet access. Wi-Fi specifically is not required; Ethernet works too. Recipients must enable receiving in the app, which explains that the owner writes messages. The desk confirms queue acceptance, not reading or guaranteed delivery.

Host `FlyGuy.Relay` on a server behind an HTTPS reverse proxy, with a persistent private data directory. Provide these environment variables to the relay process:

| Variable | Purpose |
| --- | --- |
| `FLYGUY_ADMIN_KEY` | Owner-only random secret, at least 32 characters. Keep it to yourself. |

| `FLYGUY_DATA_PATH` | Absolute filename for relay state in a persistent private directory. |

Bind the relay to loopback behind the proxy, for example:

```powershell
dotnet run --project FlyGuy/FlyGuy.Relay -c Release -- --urls http://127.0.0.1:5187
```

Recipients enter the HTTPS relay address, their chosen display name in **Fly messages**, then connect. You enter the same HTTPS address and your admin key in the Admin tab. The recipient app refuses non-local HTTP addresses; do not disable certificate checking. **Use local owner key** only applies to the local demo, not a remote relay.

Only possession of the admin key authorizes listing users or sending messages. Knowing the URL or one's own recipient token does not grant those permissions. Keep the owner Windows profile private and do not share its owner-key file. To rotate server admin key, replace them and restart the relay. Already enrolled recipient tokens continue to work. Remote hosting and deployment are not configured by local setup.

## Delivery and stored data

- Clients poll their own inbox every five seconds when idle, or after the current twelve-second bubble. Network failures retry automatically. The admin panel reports messages as queued, rather than claiming they have been read.
- Each client receives a unique random token. Windows protects the saved token with DPAPI for the current Windows user. An enabled saved connection resumes on startup. Disconnecting preserves credentials but disables automatic receiving.
- The server stores display names, recipient IDs, hashed recipient tokens and queued plaintext message contents. It does not collect Windows account names, screenshots, fly vision or neural state. No admin key is stored in the server state file.
- Offline messages survive relay restarts, expire after 24 hours and are capped at 50 per recipient. Acknowledged messages are removed. A recipient can neither read nor acknowledge another user's messages. Clients suppress repeated delivery of the last displayed message if an acknowledgement needs retrying.
- User records persist, so offline users remain selectable. Presence expires after 45 seconds without polling. Reconnecting reuses saved recipient credentials. A first connection registers without an invitation key. Display names are labels, not verified identities; the short ID distinguishes duplicates.
- Messages are plain text, with no HTML, scripts or commands. API body size and request rates are bounded. The receiving setup explains that the relay owner writes the fly's messages.

## Checks

```powershell
dotnet run --project tests/FlyGuy.Messaging.Checks -c Release
dotnet run --project tests/FlyGuy.Checks -c Release
```

The messaging checks run an isolated local HTTP relay and test authorization, recipient isolation, real send/poll/ack delivery, offline persistence, queue limits, presence expiry and invalid requests.
