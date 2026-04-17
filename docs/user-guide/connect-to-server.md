# Connect to ChartHub Server

Both the Desktop and Android apps connect to a user-deployed **ChartHub Server** instance. There is no peer-to-peer pairing between Desktop and Android — each client authenticates independently with the server using Google OAuth and a server-issued JWT.

---

## Prerequisites

- A running ChartHub Server instance (see [Server Setup](../self-hosting/server-setup.md))
- Your Google account must be in the server's allowlist (`CHARTHUB_SERVER_ALLOWED_EMAIL_0` / `CHARTHUB_SERVER_ALLOWED_EMAIL_1` etc.)
- The server URL (FQDN or local IP, e.g. `https://charthub.yourdomain.com` or `http://192.168.1.10:5180`)

---

## Setup Steps (Desktop and Android — same process)

1. Open the **Settings** view in ChartHub.
2. Enter your ChartHub Server URL in the **Server URL** field.
3. Tap or click **Sign in with Google**.
4. Complete the Google OAuth flow in your browser. ChartHub uses PKCE — no client secret is stored.
5. ChartHub sends the Google ID token to `POST /api/v1/auth/exchange` on your server.
6. The server validates the token, checks your email against the allowlist, and returns a signed JWT.
7. ChartHub stores the JWT and uses it for all subsequent API calls.

After a successful sign-in, all features that require server connectivity become active.

---

## Authentication Details

| Step | Detail |
|---|---|
| OAuth method | Google PKCE (no client secret) |
| Token exchange | `POST /api/v1/auth/exchange` — sends Google ID token, receives server JWT |
| JWT usage | All `/api/v1/*` endpoints require `Authorization: Bearer <token>` |
| Token expiry | Controlled by `Auth.AccessTokenMinutes` in server config |
| Allowlist | Server validates your Google email against the configured allowed emails |

If your email is not on the allowlist, the exchange returns `403 Forbidden`.

---

## Troubleshooting

### "403 Forbidden after sign-in"
Your Google account email is not in the server's allowlist. Add it via `CHARTHUB_SERVER_ALLOWED_EMAIL_N` in your server environment and restart.

### "Could not reach server"
- Confirm the server URL is correct and the server is running.
- If using a local IP, ensure both devices are on the same network.
- Check firewall rules allow the server port from the client device.

### "Token expired" / re-auth prompts
The JWT has expired. Sign in again via Settings. Token lifetime is configured with `Auth.AccessTokenMinutes` on the server.
