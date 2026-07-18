# Deploying Routes to Glory (SPanel)

Target: **https://8082ventures.com/rtg/**

SPanel hosts the **static PWA** in your web root and runs the **Node API** via **NodeJS Manager** (not cPanel). MySQL tables live in your existing database (e.g. `ventures_rtg_test`).

## Public API URL (Unity / mobile)

| Client | API base URL |
|---|---|
| **Phone / tablet (off-LAN)** | **`https://8082ventures.com/rtg_api/api`** |
| Unity Editor (`pnpm dev`) | `http://localhost:3001/api` |
| Same Wi‑Fi LAN field test | `http://<Mac-LAN-IP>:3001/api` |
| Local API over the internet | HTTPS URL from `pnpm dev:tunnel` + `/api` |

**Verify production:**

```bash
curl https://8082ventures.com/rtg_api/health
# → {"ok":true,"database":true}

curl https://8082ventures.com/rtg_api/api/config/public

# After picking a real world id + empire id from /worlds/saved:
WID=<world-uuid>
EID=<empire-uuid>
curl -sS -w "\nHTTP:%{http_code}\n" \
  "https://8082ventures.com/rtg_api/api/worlds/$WID/map" | head -c 200
# → 200 + settlements/routes/resources JSON

curl -sS -w "\nHTTP:%{http_code}\n" \
  "https://8082ventures.com/rtg_api/api/worlds/$WID/missions?empireId=$EID"
# → 200 + currentMission/objective (NOT Fastify "Route GET:/api/worlds/.../missions not found")
```

**Stale API symptom:** login + saved list + map work, but Mission HUD shows
`Route GET:/api/worlds/<guid>/missions… not found`. That means production Node is an
older build missing `missionRoutes`. Rebuild and redeploy the API (see
[Updating after a release](#updating-after-a-release)).

Note: **`/rtg/api`** may return Apache **403** on this host (mod_proxy blocked in `.htaccess`). Use **`/rtg_api`** (PHP proxy in `deploy/spanel/rtg_api/`) for public clients. CORS is open (`origin: true` on Fastify + `Access-Control-Allow-Origin: *` on the PHP proxy). HTTPS satisfies iOS ATS / Android cleartext rules for production.

---

## What goes where

| Piece | Type | Server location |
|---|---|---|
| **Web PWA** | Static HTML/JS/CSS | `public_html/rtg/` (public) |
| **API** | Node.js process | **Outside** `public_html`, e.g. `/home/USERNAME/rtg-api/` |
| **`.env` (API)** | Secrets | Next to API `dist/` — **never** in `public_html` |
| **MySQL** | Database | Existing MySQL on server; no file upload |

---

## 1. MySQL tables

**SQL file (reference):**

```
apps/api/migrations/001_initial.sql
```

**Apply via migration runner** (recommended — from your laptop or SSH):

```bash
cd /path/to/routestoglory
pnpm db:migrate
```

Uses `MYSQL_*` from the root `.env`. Or run `001_initial.sql` manually in SPanel’s **phpMyAdmin** / **MySQL Databases** against `ventures_rtg_test`.

---

## 2. Build locally

```bash
cd /path/to/routestoglory

# apps/web/.env — production values for the PWA build:
#   VITE_API_BASE=/rtg/api
#   VITE_MAPBOX_TOKEN=pk.xxxx

pnpm --filter @empire/shared build
pnpm --filter @empire/web build
pnpm --filter @empire/api build
```

Outputs:

- `apps/web/dist/` → upload to web server
- `apps/api/dist/` → upload to Node app folder

---

## 3. Deploy the web PWA (static files)

Using SPanel **File Manager** or FTP/SFTP, upload **everything inside** `apps/web/dist/` to:

```
public_html/rtg/
```

Expected files:

```
public_html/rtg/
├── index.html
├── assets/
├── sw.js
├── workbox-*.js
├── registerSW.js
└── manifest.webmanifest
```

**SPA fallback:** requests to `/rtg/anything` must serve `index.html`. SPanel/Apache usually needs a `.htaccess` in `public_html/rtg/`:

```apache
RewriteEngine On
RewriteBase /rtg/
RewriteRule ^index\.html$ - [L]
RewriteCond %{REQUEST_FILENAME} !-f
RewriteCond %{REQUEST_FILENAME} !-d
RewriteRule . /rtg/index.html [L]
```

Test: **https://8082ventures.com/rtg/** should load the app shell (map needs Mapbox token + API).

---

## 4. Deploy the Node API (SPanel NodeJS Manager)

The API is **not** uploaded to `public_html`. It runs as a background Node process managed by SPanel.

### 4a. Upload API files

Create a folder **outside** the web root, e.g.:

```
/home/USERNAME/rtg-api/
```

Upload (FTP / File Manager / SFTP):

| Local path | Server path |
|---|---|
| `apps/api/dist/` | `rtg-api/dist/` |
| `apps/api/index.js` | `rtg_api/index.js` |
| `apps/api/app.yml` | `rtg_api/app.yml` ← **required by SPanel before deploy** |
| `apps/api/migrations/` | `rtg_api/migrations/` |
| `apps/api/package.json` | `rtg-api/package.json` |
| `packages/shared/dist/` | `rtg_api/vendor/@empire/shared/dist/` (or `node_modules/@empire/shared/`) |
| `packages/shared/package.json` | `rtg_api/vendor/@empire/shared/package.json` |

ScalaHosting/SPanel may require **`vendor/@empire/shared/`** instead of `node_modules/` — match whatever support configured on your server.

Copy `apps/api/.env.example` → `rtg-api/.env` and fill in MySQL credentials + `PORT=3001`.

**Or** upload the whole repo via Git/SSH and build on the server:

```bash
cd ~/rtg-api-src
pnpm install
pnpm --filter @empire/shared build
pnpm --filter @empire/api build
cp apps/api/.env.example apps/api/.env   # edit credentials
```

Then use `apps/api/` as the application root in NodeJS Manager.

### 4b. Install dependencies on the server

**SSH** into the server (SPanel → **SSH Access**), then:

```bash
cd ~/rtg-api
npm install --omit=dev
```

If you deployed the full monorepo:

```bash
cd ~/rtg-api-src
pnpm install --prod
```

SPanel may also offer **Actions → npm install** on deployed Node apps (varies by SPanel version).

### 4c. Register the app in SPanel

1. Log in to **SPanel**
2. Open **NodeJS Manager** (under the user interface / developer tools)
3. Click **Deploy a New App**
4. Set:

| Field | Value |
|---|---|
| **Application root** | `rtg-api` (relative to home) — **not** `rtg-api/app.yml` |
| **Startup / entry file** | `index.js` — **not** `app.yml` |
| **Port** | `3001` (must be 3000–3500) |
| **Application URL** | `8082ventures.com/rtg-api` |

**Common error:** `.../rtg-api/app.yml/app.yml does not exist` — see [Troubleshooting: app.yml](#troubleshooting-spanel-appyml-error) below.

5. Click **Deploy**
6. Use **Actions → Restart** after code updates
7. Use **Actions → View Logs** if the app fails to start

The start command should run `npm start` → `node dist/index.js` (defined in `apps/api/package.json`).

### 4d. Proxy `/rtg/api` to Node

The PWA calls **`/rtg/api`**. Apache must forward that to your Node process on port 3001.

Add to **`public_html/.htaccess`** (or a vhost include SPanel supports):

```apache
RewriteEngine On

# Routes to Glory API → Node on port 3001
RewriteRule ^rtg/api/?(.*)$ http://127.0.0.1:3001/api/$1 [P,L]
```

**Alternative:** if SPanel NodeJS Manager lets you bind the app URL directly (e.g. `8082ventures.com/rtg/api`), use that instead of manual `.htaccess`.

After proxying, test:

```bash
curl https://8082ventures.com/rtg/api/health
# → {"ok":true,"database":true}
```

If that returns **403**, use the working public path instead (already deployed on 8082ventures):

```bash
curl https://8082ventures.com/rtg_api/health
curl https://8082ventures.com/rtg_api/api/config/public
```

Unity / mobile should use **`https://8082ventures.com/rtg_api/api`** as the API base (see [Public API URL](#public-api-url-unity--mobile) above).

---

## 5. Environment variables (API)

File: **`rtg-api/.env`** (same folder as `dist/`, not in `public_html`):

```bash
MYSQL_HOST=localhost
MYSQL_USER=your_user
MYSQL_PASSWORD=your_password
MYSQL_DATABASE=ventures_rtg_test
MYSQL_PORT=3306
PORT=3001
```

Run migrations once on the server (SSH):

```bash
cd ~/rtg-api
node dist/scripts/migrate.js
```

---

## 6. Checklist

- [ ] MySQL tables created (`pnpm db:migrate` or `001_initial.sql`)
- [ ] `apps/web/dist/` → `public_html/rtg/`
- [ ] `VITE_API_BASE=/rtg/api` used at **build** time
- [ ] API uploaded to `~/rtg-api/` (outside `public_html`)
- [ ] `rtg-api/.env` with MySQL creds
- [ ] SPanel **NodeJS Manager** → app deployed on port 3001
- [ ] `.htaccess` proxies `/rtg/api` → `127.0.0.1:3001`
- [ ] `curl …/rtg/api/health` returns OK
- [ ] **https://8082ventures.com/rtg/** loads the map

---

### Apache 403 on `/rtg_api/*` subpaths

If `curl https://8082ventures.com/rtg_api/` returns Fastify JSON but `/rtg_api/health` returns Apache **403**, the host blocks `[P]` proxy rules in `.htaccess`.

**Fix A — PHP proxy (works on PHP hosts like 8082ventures):**

Upload `deploy/spanel/rtg_api/` to **`public_html/rtg_api/`**:

- `.htaccess`
- `proxy.php`

Then test:

```bash
curl https://8082ventures.com/rtg_api/health
curl https://8082ventures.com/rtg_api/api/config/public
```

**Fix B — ask support** to add vhost `ProxyPass /rtg_api/ http://127.0.0.1:3001/` (mod_proxy, not `.htaccess`).

---

### SPanel `app.yml` error

Error: `Application at /home/ventures/rtg-api/app.yml/app.yml does not exist`

SPanel is looking for `{root}/app.yml` but your **Application root is set to `rtg-api/app.yml`** instead of `rtg-api`. It doubles the filename.

**Fix (required):**

1. NodeJS Manager → **Undeploy** the app completely (not just Restart)
2. SSH or File Manager — check for a mistaken **folder** named `app.yml`:
   ```bash
   ls -la /home/ventures/rtg-api/
   ```
   - If `app.yml` is a **directory**, delete it: `rm -rf /home/ventures/rtg-api/app.yml`
3. Upload `apps/api/app.yml` as a **file** at `/home/ventures/rtg-api/app.yml`
4. **Deploy a New App** with:

| Field | Value |
|---|---|
| Application root | `rtg-api` |
| Startup file | `index.js` |
| Port | `3001` |
| URL | `8082ventures.com/rtg-api` |

**Do not** put `app.yml` in the Application root or Startup file fields.

**If SPanel still fails** — bypass NodeJS Manager and run via SSH:

```bash
cd /home/ventures/rtg-api
npm install --omit=dev
PORT=3001 node index.js
```

Keep it running with PM2 if available: `pm2 start index.js --name rtg-api`. The `.htaccess` proxy to port 3001 still applies.

---

| Problem | What to check |
|---|---|
| App won’t start | **Actions → View Logs** in NodeJS Manager |
| `MODULE_NOT_FOUND` | Run `npm install` in the app root; ensure `@empire/shared` is under `node_modules/` |
| API 502 / connection refused | App running? Port 3001? Proxy rule correct? |
| DB errors | `.env` MySQL creds; run `node dist/scripts/migrate.js` |
| Map loads, no data | API proxy; browser devtools → Network tab on `/rtg/api/...` |
| PWA 404 on refresh | Add SPA `.htaccess` rewrite in `public_html/rtg/` |

---

## Updating after a release

Preferred (self-contained upload matching ScalaHosting layout):

```bash
cd /path/to/routestoglory
./scripts/bundle-rtg-api.sh
# → deploy/rtg_api_bundle/  (includes dist/routes/missions.js + migration 005)
```

Then on the server (FTP / File Manager / SFTP):

1. Upload **contents** of `deploy/rtg_api_bundle/` into `/home/ventures/rtg_api/`
   (replace `dist/`, `index.js`, `migrations/`, `package.json`, `vendor/`, `node_modules/` as needed)
2. **Keep** the existing server `.env` (do not overwrite MySQL secrets)
3. SPanel **NodeJS Manager** → **Actions → Restart** (migrations run on boot; `005_empire_missions.sql` creates `empire_missions` if missing)
4. Re-verify missions (see curls above) — must be **200**, not Fastify route-not-found

Manual alternative (no bundle script):

1. `pnpm --filter @empire/shared build && pnpm --filter @empire/api build`
2. Upload new `apps/web/dist/` → `public_html/rtg/` (PWA only)
3. Upload new `apps/api/dist/` → `rtg-api/dist/` (must include `routes/missions.js`)
4. Upload new `apps/api/migrations/` if new SQL files were added
5. SPanel NodeJS Manager → **Actions → Restart**
