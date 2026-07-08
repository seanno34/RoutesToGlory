# Dev Environment & Tooling — Routes to Glory (native client)

Setup guide for developing the planned **Unity (C#) + Cesium** native client alongside the existing **Node/TypeScript** backend and the **styled-tile** pipeline.

**IDE decision:** **JetBrains Rider (primary) + Cursor (AI pair)**, both open on the same project on disk.
**Also read:** [ENGINE_EVALUATION.md](ENGINE_EVALUATION.md) (why Unity + Cesium) · [ROADMAP.md](ROADMAP.md) · [DEPLOY.md](DEPLOY.md)

---

## Architecture recap (what runs where)

| Component | Tech | Repo location | Tooling |
|---|---|---|---|
| Game server (authoritative) | Node + Fastify + MySQL | `apps/api`, `packages/shared` | Cursor |
| Native client (POC) | Unity 6 LTS + C# + Cesium | `apps/unity-poc` | Rider + Cursor |
| Styled-tile pipeline | Planetiler + TileServer GL + Maputnik | new (e.g. `infra/tiles`) | Cursor + Docker |
| Existing web PWA | React + Mapbox | `apps/web` | Cursor |

All game rules stay server-side; the Unity client is a rich view + GPS input over the same API. See [ENGINE_EVALUATION.md](ENGINE_EVALUATION.md).

---

## IDE setup: Rider + Cursor

**Why this combo:** Rider is the best Unity/C# IDE on macOS (bundled Unity plugin, play-mode debugging, profiler, shader syntax, engine-aware refactors) and is free for non-commercial use. Cursor stays as the AI editor for the same files and owns the Node backend + tile pipeline + Docker/shell work. They coexist because they sync through the filesystem.

> Important: **Do not install Microsoft's C# Dev Kit / C# / Unity extensions in Cursor.** They are license-locked to Microsoft editors and are actively blocked in Cursor forks. Cursor handles C# via its own/OpenVSX language support; Rider does the heavy Unity C# lifting.

### Rider
1. Install **JetBrains Rider** (via [JetBrains Toolbox](https://www.jetbrains.com/toolbox-app/) is easiest for updates).
2. On first launch, choose **Non-commercial use** and sign in with a JetBrains account (1-year renewable free license).
3. The **Unity Support** plugin is bundled — no manual install.
4. In Unity: install the **JetBrains Rider Editor** package (Package Manager), then **Edit → Preferences → External Tools → External Script Editor → Rider**.
5. Verify: right-click in Unity Project view → **Open C# Project** launches Rider connected to Unity (Unity icon shows connection status).

### Cursor (same project, AI pair)
1. Open the **same project folder** in Cursor.
2. For C# language features in Cursor, use OpenVSX extensions only:
   - **DotRush** — C# language server (works in Cursor).
   - **DotNet.Meteor** — optional, mobile build/deploy/debug.
3. Optional Unity integration so Unity can target Cursor too: the community **`com.boxqkrtm.ide.cursor`** Unity package (csproj generation, install auto-discovery).
4. Use Cursor as the primary editor for `apps/api`, `packages/shared`, `apps/web`, and `infra/tiles`.

---

## Prerequisites checklist

Grouped by area — install what each area needs.

### Unity client
- [x] **Unity Hub**
- [x] **Unity 6.3 LTS** (`6000.3.19f1`) editor (created from the **Universal 3D / URP** template — labeled "Universal 3D (SRP)" in some Hub versions; URP *is* an SRP. Cesium requires URP or HDRP; the built-in renderer will not render Cesium tiles)

> ⚠️ **Do NOT use Unity 6.5 / tech-stream (6000.5+).** Cesium for Unity 1.24 uses editor `TreeView` APIs that Unity marked obsolete-as-**error** (CS0619) in 6000.5, so Cesium fails to compile and its `Window → Cesium` menu never appears. Unity 6.3 LTS (6000.3) only emits harmless deprecation **warnings** and compiles fine. Cesium officially supports Unity 2022 LTS and Unity 6. Also: pick the editor version and install all packages **before** doing other work — switching editor versions mid-project leaves stale `Library/`/`packages-lock.json` and 6.5-only modules (e.g. `com.unity.modules.physicscore2d`) that break package resolution.
- [ ] Unity Hub module: **Android Build Support** (+ OpenJDK, Android SDK, Android NDK)
- [ ] Unity Hub module: **iOS Build Support**
- [ ] **Cesium for Unity** package (scoped registry — see below)
- [ ] **Cesium ion** account (free tier) for streaming terrain/assets
- [ ] Unity packages: **Shader Graph**, **Input System**

### C# IDE
- [ ] **JetBrains Rider** (non-commercial license)
- [ ] **JetBrains Rider Editor** Unity package
- [ ] **Cursor** + **DotRush** (OpenVSX); optional **DotNet.Meteor**, `com.boxqkrtm.ide.cursor`

### iOS build/deploy (macOS)
- [ ] **Xcode** (Unity emits an Xcode project to compile/sign)
- [ ] **Apple Developer Program** membership ($99/yr)
- [ ] A physical **iPhone** (GPS/AR can't be validated well in the simulator)

### Android build/deploy
- [ ] SDK/NDK/JDK (installed via Unity Hub modules)
- [ ] A physical **Android device**
- [ ] **Google Play Console** account ($25 one-time)
- [ ] Player settings for Cesium: **IL2CPP**, **ARM64** (not ARMv7), Internet Access = **Require**

### Version control
- [x] **Git** + **Git LFS** (essential — Unity assets are large/binary)
- [x] Unity `.gitignore` (root `.gitignore`, scoped to `apps/unity-poc/`)
- [x] `.gitattributes` (root) — Git LFS filters + Unity YAML merge rules
- [ ] **Unity Smart Merge (UnityYAMLMerge)** configured for scene/prefab merges (see note below)

#### Git LFS install (as actually done on this machine)

Homebrew was **not** present, so Git LFS was installed from the standalone release rather than `brew install git-lfs`:

```bash
cd /tmp
curl -L -o git-lfs.zip https://github.com/git-lfs/git-lfs/releases/download/v3.7.1/git-lfs-darwin-amd64-v3.7.1.zip
unzip -o git-lfs.zip
mkdir -p ~/.local/bin
cp git-lfs-*/git-lfs ~/.local/bin/
chmod +x ~/.local/bin/git-lfs
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.bash_profile   # bash login shell
export PATH="$HOME/.local/bin:$PATH"
git lfs install        # writes ~/.gitconfig filters; no sudo needed
git lfs version        # expect git-lfs/3.7.x
```

Notes:
- **Intel Mac** → `darwin-amd64`; Apple Silicon → `darwin-arm64`. macOS releases are `.zip` (not `.tar.gz`).
- The binary lives in `~/.local/bin` and is on PATH via `~/.bash_profile`. **Any terminal that runs `git push`/`git add` on this repo must have `~/.local/bin` on PATH**, or LFS filters fail with `git-lfs: command not found`.
- If you later install Homebrew, `brew install git-lfs` is the simpler long-term route.

### Backend (existing — no change)
- [ ] **Node 20+**, **pnpm**, **MySQL**

### Styled-tile pipeline
- [ ] **Docker** + **Docker Compose**
- [ ] **JDK 21** (or run Planetiler via Docker)
- [ ] **Planetiler**, **TileServer GL**, **Maputnik**
- [ ] An OSM extract from **Geofabrik**
- [ ] Object storage / CDN (**S3** or **Cloudflare R2**) for serving tiles

---

## Step-by-step: Cesium for Unity

Cesium ships native binaries, so it **cannot** be installed via a git URL or OpenUPM. Use the scoped registry:

1. **Edit → Project Settings → Package Manager**.
2. Add a **Scoped Registry**:
   - Name: `Cesium`
   - URL: `https://unity.pkg.cesium.com`
   - Scope(s): `com.cesium.unity`
3. **Window → Package Manager → Packages: My Registries → Cesium for Unity → Install**.
4. Create/sign in to **Cesium ion**, generate a token, and add it in the Cesium panel to stream terrain/assets.

For the alien-world look, follow the layer stack in [ENGINE_EVALUATION.md](ENGINE_EVALUATION.md): Cesium World Terrain + a custom stylized raster overlay (`CesiumUrlTemplateRasterOverlay`) + fog/claims as cartographic polygons / shader masks.

---

## Step-by-step: styled-tile pipeline (local)

Prototype quickly on **Stadia Maps + Stamen** (ready XYZ raster, no pipeline). For the production custom alien style:

1. **Author** the style in **Maputnik** (MapLibre GL style JSON — portable, no lock-in).
2. **Generate data:** run **Planetiler** on a Geofabrik OSM extract to produce MBTiles/PMTiles.
3. **Serve raster:** run **TileServer GL** (Docker) to render vector tiles + style into raster XYZ tiles.
4. Point Cesium's `CesiumUrlTemplateRasterOverlay` at the TileServer GL XYZ URL (Web Mercator; prefer 512px @2x).

Managed escape hatch: **MapTiler Cloud** (hosted styles + raster tiles) if you don't want to run a tile server. Full tool comparison in [ENGINE_EVALUATION.md](ENGINE_EVALUATION.md#styled-tile-tooling).

---

## Accounts & recurring costs

| Item | When needed | Cost |
|---|---|---|
| Apple Developer Program | iOS builds/distribution | $99/yr |
| Google Play Console | Android distribution | $25 one-time |
| JetBrains Rider | C#/Unity IDE | Free (non-commercial) |
| Cursor | AI editor | Existing plan |
| Cesium ion | Terrain/asset streaming | Free tier → usage |
| Unity | Engine | Free until revenue thresholds (rev-share/runtime terms at scale) |
| Tile hosting (self-host CDN or MapTiler Cloud) | Styled tiles in production | CDN/egress or MapTiler usage |

---

## Gotchas / notes

- **C# Dev Kit will not work in Cursor** — use Rider for Unity C#, DotRush in Cursor. This is a licensing + technical block, not a config issue.
- **Cesium needs URP/HDRP** — starting from the plain 3D (built-in) template will render nothing.
- **Cesium install** is scoped-registry only (native code); git-URL/OpenUPM installs fail with compilation errors.
- **Git LFS before first asset commit** — retrofitting LFS after large binaries land in history is painful.
- **Test GPS on real devices** — simulators/emulators don't reproduce real-world GPS behavior; this is the whole point of going native.
- **Rider + Cursor on the same files** — fine; just avoid running conflicting formatters. Let one own formatting settings.
