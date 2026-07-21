# Global Xenite Resource Streaming Architecture
## Cursor Implementation Specification for Routes to Glory

### Purpose

Replace the current Douglas-centered xenite loading model with a global, deterministic, streamed resource system that:

- Produces xenite deposits near the player anywhere on Earth.
- Loads only resources within a bounded radius of the player.
- Produces the same deposits every time a geographic area is revisited.
- Preserves claimed, depleted, discovered, and other mutable deposit state.
- Works with Cesium origin shifting and globe anchors.
- Supports offline travel and delayed synchronization.
- Avoids pre-generating or storing deposits for the entire Earth.

The recommended model is:

> **Procedural deterministic placement + geographic cell streaming + sparse persistent overrides.**

Cesium continues to stream terrain. Unity independently streams gameplay resources around the player.

---

# 1. Architectural Decision

Do **not** create all xenite deposits at world load.

Do **not** store every possible xenite deposit in a global database.

Do **not** base random placement on Unity world-space coordinates, because Cesium origin shifting changes those coordinates.

Instead:

1. Divide Earth into stable geographic cells.
2. Determine which cells surround the player's WGS84 position.
3. Generate each cell's candidate deposits from a deterministic seed.
4. Instantiate only deposits within the active streaming radius.
5. Remove or pool deposits when their cells leave the active radius.
6. Persist only mutable state keyed by a stable deposit ID.
7. Regenerate the same base deposits when the player returns and reapply their persisted state.

This gives the illusion that xenite exists globally while paying runtime cost only for the player's current vicinity.

---

# 2. Relationship to the Existing Cesium Architecture

The current project already establishes the correct separation:

- Cesium owns Earth coordinates, terrain streaming, elevation, georeferencing, and origin shifts.
- Unity owns xenite prefabs, gameplay state, route overlays, visual effects, and interaction.

The resource streamer must therefore operate primarily in **latitude/longitude**, not Unity `Vector3` space.

Each active deposit should use:

- A stable WGS84 latitude and longitude.
- A terrain-resolved height.
- A `CesiumGlobeAnchor`.
- A stable deposit identifier independent of the current Unity origin.

Recommended hierarchy:

```text
RTG Georeference
├── RTG Terrain
├── RTG Player
└── RTG Streamed World Content
    ├── Cell_<cellId>
    │   ├── Xenite_<depositId>
    │   └── Xenite_<depositId>
    └── Cell_<cellId>
```

A `CesiumOriginShift` may recenter the Unity coordinate system while travelling. Anchored deposits should remain geographically fixed because their authoritative location is cartographic/ECEF, not their current local transform.

---

# 3. Geographic Partitioning

## 3.1 Recommended production model: H3 cells

Use an Earth-wide hierarchical hexagonal index such as **H3** for logical resource partitioning.

Advantages:

- Stable cell IDs worldwide.
- Efficient neighbor/ring queries.
- Similar cell sizes at a given resolution.
- No special longitude wraparound logic in gameplay code.
- No rectangular clustering artifacts.
- Easy backend indexing and sharding.
- Hierarchical parent/child cells for future zoom or density systems.

Use an H3-compatible C# library that supports the Unity target runtime and IL2CPP. Wrap it behind an application-owned interface so the package can be replaced later.

```csharp
public interface IGeoCellIndex
{
    string GetCell(double latitude, double longitude, int resolution);
    IReadOnlyCollection<string> GetCellsWithinGridDistance(
        string centerCell,
        int gridDistance);
    GeoCoordinate GetCellCenter(string cellId);
    IReadOnlyList<GeoCoordinate> GetCellBoundary(string cellId);
}
```

Do not expose the third-party H3 types throughout gameplay code.

## 3.2 Initial target resolution

Start with cells roughly **1 to 3 km across**.

The exact H3 resolution should be selected after measuring:

- Player travel speed.
- Desired deposit density.
- Visible horizon distance.
- Terrain height-query cost.
- Number of simultaneous GameObjects affordable on target phones.

A practical starting configuration:

```text
Logical cell diameter:       approximately 1.5–2.5 km
Active spawn radius:         3–5 km
Preload radius:              5–8 km
Retention radius:            7–10 km
Target active deposits:      20–80
Maximum active deposits:     120
```

Use separate radii to prevent loading/unloading thrash:

```text
Preload radius > visible radius
Retention radius > preload radius
```

## 3.3 POC fallback

If introducing H3 immediately is undesirable, implement a latitude/longitude grid behind the same `IGeoCellIndex` interface.

Never use raw decimal string truncation as the permanent architecture. At minimum:

- Normalize longitude into `[-180, 180)`.
- Clamp latitude.
- Account for longitudinal distance shrinking toward the poles.
- Use integer cell coordinates.
- Test the International Date Line.
- Test high latitudes.

The fallback should be replaceable without changing resource generation or persistence.

---

# 4. Deterministic Deposit Generation

## 4.1 World seed

Every world must have an immutable resource seed:

```csharp
public readonly record struct WorldGenerationConfig(
    string WorldId,
    ulong WorldSeed,
    int GeneratorVersion,
    int CellResolution);
```

The seed should be generated server-side when the world is created and must never change for that world.

## 4.2 Cell seed

Derive a cell seed from:

- World seed.
- Cell ID.
- Resource type.
- Generator version.

Example conceptual input:

```text
SHA-256(
    worldSeed |
    generatorVersion |
    cellId |
    "xenite"
)
```

Convert the required bytes to a deterministic PRNG seed.

Do not use:

- `UnityEngine.Random`.
- `System.Random` without an explicitly controlled algorithm.
- `GetHashCode()`, because its behavior is not a durable persistence contract.
- Current date/time.
- Device-specific entropy.

Implement a small fixed deterministic PRNG, such as PCG32 or Xoshiro, in project source so generation remains reproducible across devices and releases.

```csharp
public interface IDeterministicRandom
{
    uint NextUInt();
    double NextDouble01();
    int NextInt(int minInclusive, int maxExclusive);
}
```

## 4.3 Stable base generation

For each cell:

1. Calculate a deterministic density score.
2. Select zero or more candidate deposit points.
3. Apply deterministic minimum-spacing rejection.
4. Assign deposit properties.
5. Return lightweight definitions, not GameObjects.

```csharp
public sealed record XeniteDepositDefinition(
    string DepositId,
    string CellId,
    int CandidateIndex,
    double Latitude,
    double Longitude,
    XeniteRarity Rarity,
    float BaseQuantity,
    float VisualScale,
    int GeneratorVersion);
```

The stable ID should be derived from:

```text
worldId + generatorVersion + cellId + candidateIndex + resourceType
```

Example:

```text
xen:v1:<worldId>:<cellId>:<candidateIndex>
```

A hashed compact form may be used in production, but retain enough fields in logs to diagnose generation errors.

## 4.4 Candidate positioning

Generate points inside the cell polygon, not around the player's current position.

Possible implementation:

1. Get the cell boundary.
2. Generate deterministic points in a local tangent plane centered on the cell.
3. Reject points outside the polygon.
4. Convert accepted points back to WGS84.
5. Enforce minimum spacing between candidates.

This is important: deposits must belong to the cell itself. The player merely causes the cell to load.

## 4.5 Density

Use a two-stage deterministic model:

```text
Cell abundance:
    determines whether the cell has 0, 1, 2, ... deposits

Deposit attributes:
    determines rarity, quantity, scale, cluster type, and appearance
```

Example starting distribution:

```text
35% of cells: 0 deposits
40% of cells: 1 deposit
20% of cells: 2 deposits
5% of cells: 3–4 deposits
```

Tune by measured cell area and desired encounter frequency.

Avoid guaranteeing a deposit in every cell. Sparse and clustered placement will feel more natural.

## 4.6 Optional biome weighting

The first implementation should be geographically uniform except for deterministic noise.

A later version may weight spawn probability using:

- Alien biome classification.
- Elevation.
- Slope.
- Distance from water.
- Echo Site influence.
- Fictional regional abundance fields.

Treat these as deterministic inputs. Do not make generation depend on whether terrain happened to finish loading first.

---

# 5. Generator Versioning

Procedural generation becomes part of saved-game compatibility.

Any algorithm change can move deposits. Therefore, include `GeneratorVersion` in:

- Cell cache keys.
- Deposit IDs.
- Backend state.
- Client save records.
- Diagnostics.

Recommended policy:

```text
Version 1 worlds continue using generator version 1.
New worlds may use generator version 2.
Existing claimed deposits never silently move.
```

Do not overwrite the generation version of an existing world.

If a future migration is required, build an explicit migration process rather than changing the seed formula.

---

# 6. Runtime Streaming System

Create a dedicated service:

```csharp
public sealed class RtgXeniteStreamingService : MonoBehaviour
{
    // Observes authoritative player WGS84 position.
    // Computes desired geographic cells.
    // Loads, generates, resolves, and spawns deposits.
    // Unloads or pools cells outside retention range.
}
```

## 6.1 Streaming states

Track each cell through an explicit state machine:

```csharp
public enum StreamedCellState
{
    Unloaded,
    Queued,
    Generating,
    LoadingPersistentState,
    ResolvingTerrain,
    ReadyToSpawn,
    Active,
    Retained,
    Unloading,
    Failed
}
```

Store runtime state separately from deterministic definitions:

```csharp
public sealed class XeniteCellRuntime
{
    public string CellId;
    public StreamedCellState State;
    public CancellationTokenSource Cancellation;
    public IReadOnlyList<XeniteDepositDefinition> Definitions;
    public Dictionary<string, XeniteDepositView> SpawnedViews;
    public double LastDistanceMeters;
    public DateTime LastAccessUtc;
}
```

## 6.2 Update trigger

Do not recompute the streaming set every frame.

Reevaluate when either:

- The player enters a different logical cell.
- The player moves more than a threshold, such as 250–500 meters.
- A time fallback expires, such as every 2–5 seconds.
- Teleport/debug travel occurs.
- The app resumes.

```csharp
bool ShouldRefresh(
    GeoCoordinate current,
    GeoCoordinate previousRefresh,
    string currentCell,
    string previousCell);
```

## 6.3 Desired cell bands

Maintain three sets:

```text
Spawn set:
    Cells whose deposits may be visibly instantiated.

Preload set:
    Cells generated and state-loaded before entering spawn range.

Retention set:
    Recently nearby cells retained to prevent rapid unload/reload.
```

Example:

```csharp
public sealed class XeniteStreamingSettings : ScriptableObject
{
    public int CellResolution;
    public double SpawnRadiusMeters = 4000;
    public double PreloadRadiusMeters = 6500;
    public double RetentionRadiusMeters = 8500;
    public int MaxActiveDeposits = 120;
    public int MaxConcurrentCellLoads = 2;
    public int MaxTerrainQueriesPerFrame = 4;
    public float RefreshDistanceMeters = 300;
    public float RefreshIntervalSeconds = 3;
}
```

## 6.4 Priority queue

Prioritize work by:

1. Distance to player.
2. Whether the cell intersects the forward travel corridor.
3. Whether it is inside spawn range.
4. Whether cached data exists.
5. Time waiting.

For road travel, prefetch cells ahead of the player using recent velocity:

```text
predicted position = current position + velocity × lookahead seconds
```

Use prediction only for preload priority. Never use prediction to determine permanent deposit coordinates.

## 6.5 Cancellation

Every asynchronous cell operation must be cancellable.

If a user travels rapidly, old terrain queries or backend requests should not create resources hundreds of miles behind the player.

On unload or reprioritization:

```csharp
cell.Cancellation.Cancel();
```

Before committing results, verify:

```csharp
if (!desiredCells.Contains(cellId))
    return;
```

---

# 7. Terrain Height Resolution

Deterministic generation supplies latitude and longitude. Terrain height is a presentation attachment step.

Pipeline:

```text
Generate WGS84 candidates
    ↓
Load persistent mutation state
    ↓
Cull permanently unavailable candidates
    ↓
Request terrain height
    ↓
Instantiate or reuse prefab
    ↓
Set CesiumGlobeAnchor longitude/latitude/height
```

## 7.1 Height query budget

Do not resolve every candidate in a single frame.

Use a queue with a frame budget:

```text
Mobile starting point: 2–4 height queries initiated per frame
```

Prioritize nearest deposits first.

## 7.2 Placeholder policy

Two acceptable approaches:

### Preferred

Do not show the deposit until terrain height has resolved.

### Optional

Spawn a low-cost distant indicator at approximate ellipsoid height, then replace or move it once terrain height resolves.

Do not place full deposit meshes at `height = 0` and let them visibly jump through terrain.

## 7.3 Height cache

Cache resolved heights by:

```text
generatorVersion + depositId + terrainDatasetVersion
```

The local cache may be reused across sessions.

Height is not necessarily authoritative gameplay state. It may be recalculated if the terrain provider changes.

## 7.4 Invalid terrain locations

After terrain information is available, reject or adjust deposits that are:

- Under water, if xenite should be land-only.
- On slopes above a configured threshold.
- Embedded in cliffs.
- Too near a road or Echo Site.
- Outside supported terrain coverage.

The correction must be deterministic.

Recommended method:

1. Generate several ordered fallback points per candidate.
2. Evaluate them in deterministic order.
3. Select the first valid point.
4. If none are valid, suppress the deposit.

Do not choose a random replacement at runtime.

---

# 8. Rendering and Object Lifecycle

## 8.1 Separate data from views

The deposit definition and persistent state must exist independently of the Unity GameObject.

```csharp
public sealed record XeniteDepositState(
    string DepositId,
    XeniteOwnership Ownership,
    string ClaimedByPlayerId,
    float RemainingQuantity,
    bool Discovered,
    long Revision,
    DateTime UpdatedAtUtc);
```

```csharp
public sealed class XeniteDepositView : MonoBehaviour
{
    public string DepositId { get; private set; }
    public CesiumForUnity.CesiumGlobeAnchor GlobeAnchor { get; private set; }

    public void Bind(
        XeniteDepositDefinition definition,
        XeniteDepositState state,
        double terrainHeight);

    public void Unbind();
}
```

## 8.2 Pooling

Use object pooling for:

- Deposit meshes.
- Glow/VFX.
- Interaction colliders.
- Labels or icons.

Destroying and recreating resource prefabs during driving can produce garbage-collection spikes.

```csharp
public interface IXeniteViewPool
{
    XeniteDepositView Rent(XeniteRarity rarity);
    void Return(XeniteDepositView view);
}
```

## 8.3 Distance-based presentation

Use multiple presentation bands:

```text
Near:
    Full mesh, glow, collider, interaction logic.

Medium:
    Simplified mesh or lower LOD, limited VFX.

Far:
    Billboard, beacon, or map marker only.

Outside spawn radius:
    No view.
```

Do not keep colliders or particle systems active for deposits that cannot be interacted with.

## 8.4 Cesium anchors

Every spawned deposit should be globe-anchored.

Set its authoritative cartographic coordinates through `CesiumGlobeAnchor`.

Avoid making the streamed cell root's local transform authoritative. The root is organizational only.

---

# 9. Persistent State Model

The deterministic generator answers:

```text
What deposit would exist here in an untouched world?
```

Persistence answers:

```text
What has happened to that deposit?
```

Only mutations need storage.

Examples:

- Claimed owner.
- Claim timestamp.
- Remaining quantity.
- Depleted state.
- Discovered state.
- Respawn timestamp.
- Upgrade state.
- Server revision.

## 9.1 Sparse override storage

Backend table example:

```sql
CREATE TABLE XeniteDepositState
(
    WorldId              VARCHAR(64)  NOT NULL,
    DepositId            VARCHAR(128) NOT NULL,
    GeneratorVersion     INT          NOT NULL,
    CellId               VARCHAR(32)  NOT NULL,
    ClaimedByPlayerId    VARCHAR(64)  NULL,
    OwnershipStatus      SMALLINT     NOT NULL,
    RemainingQuantity    REAL         NOT NULL,
    Discovered           BOOLEAN      NOT NULL,
    RespawnAtUtc         TIMESTAMP    NULL,
    Revision             BIGINT       NOT NULL,
    UpdatedAtUtc         TIMESTAMP    NOT NULL,

    PRIMARY KEY (WorldId, DepositId)
);

CREATE INDEX IX_XeniteDepositState_Cell
ON XeniteDepositState(WorldId, GeneratorVersion, CellId);
```

Rows should be created only when state differs from the generated default, unless audit requirements dictate otherwise.

This keeps storage proportional to player activity rather than Earth surface area.

## 9.2 Cell state endpoint

Recommended API:

```http
POST /api/worlds/{worldId}/xenite/cells/state
```

Request:

```json
{
  "generatorVersion": 1,
  "cellIds": [
    "cell-a",
    "cell-b",
    "cell-c"
  ],
  "knownRevisions": {
    "cell-a": 410,
    "cell-b": 77
  }
}
```

Response:

```json
{
  "worldId": "world-123",
  "generatorVersion": 1,
  "cells": [
    {
      "cellId": "cell-a",
      "revision": 411,
      "states": [
        {
          "depositId": "xen:v1:world-123:cell-a:0",
          "claimedByPlayerId": "player-9",
          "ownershipStatus": "Claimed",
          "remainingQuantity": 72.5,
          "discovered": true,
          "revision": 19
        }
      ]
    }
  ]
}
```

The server does not need to return unchanged default deposits.

## 9.3 Claim endpoint

```http
POST /api/worlds/{worldId}/xenite/{depositId}/claim
```

Request should include:

- Player ID from authentication.
- Expected revision.
- Client observation timestamp.
- Player WGS84 position.
- Optional device operation ID for idempotency.

The server should:

1. Reconstruct or validate that the deterministic deposit ID is valid.
2. Verify the player's proximity.
3. Apply concurrency control.
4. Return the authoritative state.
5. Reject duplicate or competing claims safely.

## 9.4 Optimistic concurrency

Claims must be server-authoritative in multiplayer.

Use a revision or ETag-style check:

```text
Update only if current revision == expected revision.
```

A client may show a pending visual state, but it must reconcile with the server response.

---

# 10. Local Offline Persistence

The road-trip requirement implies intermittent connectivity.

Maintain a local database, preferably SQLite, containing:

```text
Cell state cache
Resolved height cache
Pending claim/mutation operations
Last server revision per cell
Generator configuration
```

Suggested tables:

```sql
CachedCellState
CachedDepositState
CachedDepositHeight
PendingWorldOperation
WorldGenerationMetadata
```

## 10.1 Offline read behavior

When offline:

1. Generate deterministic cell contents locally.
2. Apply locally cached server mutations.
3. Apply pending local operations.
4. Display the resulting state.
5. Mark unconfirmed claims as pending.

The user should still see resources throughout a disconnected road trip.

## 10.2 Offline write behavior

Queue an idempotent operation:

```json
{
  "operationId": "uuid",
  "type": "ClaimXenite",
  "worldId": "world-123",
  "depositId": "xen:v1:world-123:cell-a:0",
  "playerId": "player-9",
  "observedRevision": 18,
  "latitude": 42.123,
  "longitude": -106.456,
  "occurredAtUtc": "2026-07-20T20:00:00Z"
}
```

On reconnect:

1. Upload operations in order.
2. Use `operationId` for idempotency.
3. Accept or reject each operation server-side.
4. Reconcile local state.
5. Surface conflicts gracefully.

For a competitive shared world, an offline claim cannot be guaranteed until server confirmation.

For a solo or player-private world, the local device may be authoritative and sync later.

---

# 11. Server Validation Without a Global Deposit Table

The server should share the same deterministic generation library or equivalent specification.

Given:

- World seed.
- Generator version.
- Cell ID.
- Candidate index.

The server can reconstruct the deposit and verify:

- The ID is valid.
- Its expected coordinates.
- Its base quantity and type.
- Whether the player was close enough to interact.

Recommended approach:

```text
Shared pure .NET generation assembly
    referenced by Unity client
    referenced by backend tests/server where compatible
```

If backend and Unity cannot share the exact binary, maintain golden test vectors.

Example golden vector:

```json
{
  "worldSeed": 918273645,
  "generatorVersion": 1,
  "cellId": "example-cell",
  "expectedDeposits": [
    {
      "depositId": "...",
      "latitude": 42.1234567,
      "longitude": -105.7654321,
      "rarity": "Common",
      "baseQuantity": 100
    }
  ]
}
```

Both implementations must produce the same values within defined numeric tolerances.

---

# 12. Caching

Use layered caches.

## Memory cache

Contains currently active and retained cells.

## Local disk cache

Contains:

- Generated definitions.
- Last known mutation states.
- Resolved terrain heights.
- Cell revisions.

## Backend cache

Optionally cache state responses by:

```text
worldId + generatorVersion + cellId + cellRevision
```

Generated base definitions generally do not need backend storage.

## Cache invalidation

Invalidate when:

- Generator version changes for a new world.
- Terrain dataset version changes, for height only.
- Cell revision advances.
- Deposit visual asset version changes, for views only.

Do not invalidate deterministic coordinates because a prefab or shader changes.

---

# 13. Proposed Unity Components

Create the following files.

```text
Assets/Scripts/Game/WorldStreaming/
    GeoCoordinate.cs
    IGeoCellIndex.cs
    H3GeoCellIndex.cs
    RtgXeniteStreamingService.cs
    XeniteStreamingSettings.cs
    XeniteCellRuntime.cs
    StreamedCellState.cs
    XeniteCellPriorityQueue.cs

Assets/Scripts/Game/WorldGeneration/
    WorldGenerationConfig.cs
    XeniteDepositDefinition.cs
    XeniteDepositGenerator.cs
    DeterministicHash.cs
    Pcg32Random.cs
    XeniteGeneratorVersion1.cs

Assets/Scripts/Game/Xenite/
    XeniteDepositState.cs
    XeniteDepositView.cs
    XeniteViewPool.cs
    XeniteTerrainPlacementService.cs
    XeniteInteractionController.cs

Assets/Scripts/Game/Persistence/
    IXeniteStateRepository.cs
    LocalXeniteStateRepository.cs
    RemoteXeniteStateRepository.cs
    CompositeXeniteStateRepository.cs
    PendingWorldOperationQueue.cs

Assets/Scripts/Game/Diagnostics/
    XeniteStreamingDebugOverlay.cs
```

Modify carefully:

```text
Assets/Scripts/Game/RtgEchoSiteLoader.cs
Assets/Scripts/Game/RtgPlayerLocation.cs
Assets/Scripts/Game/RtgTerrainHeight*.cs
Assets/Scripts/Editor/RtgMapBuilder.cs
```

The existing `RtgEchoSiteLoader` should stop treating xenite as a single complete static list loaded with the world map.

Echo Sites and xenite may use different loading strategies:

```text
Echo Sites:
    backend-authored/static map entities.

Xenite:
    deterministic streamed entities plus sparse mutations.
```

Do not force both through the same data path merely because both are map markers.

---

# 14. Core Interfaces

```csharp
public interface IXeniteDepositGenerator
{
    IReadOnlyList<XeniteDepositDefinition> GenerateCell(
        WorldGenerationConfig config,
        string cellId);
}
```

```csharp
public interface IXeniteStateRepository
{
    Task<IReadOnlyDictionary<string, XeniteDepositState>> GetStatesForCellsAsync(
        string worldId,
        int generatorVersion,
        IReadOnlyCollection<string> cellIds,
        CancellationToken cancellationToken);

    Task<XeniteDepositState> ClaimAsync(
        ClaimXeniteCommand command,
        CancellationToken cancellationToken);
}
```

```csharp
public interface IXeniteTerrainPlacementService
{
    Task<XeniteTerrainPlacement> ResolveAsync(
        XeniteDepositDefinition definition,
        CancellationToken cancellationToken);
}
```

```csharp
public interface IPlayerGeoPositionSource
{
    bool TryGetCurrentPosition(out GeoPositionSample sample);
    event Action<GeoPositionSample> PositionUpdated;
}
```

The streamer should depend on these interfaces rather than directly reading GPS, HTTP, SQLite, or Cesium internals.

---

# 15. Streaming Algorithm

Pseudocode:

```csharp
async Task RefreshStreamingSetAsync(GeoCoordinate playerPosition)
{
    string centerCell = cellIndex.GetCell(
        playerPosition.Latitude,
        playerPosition.Longitude,
        settings.CellResolution);

    CellBands bands = cellBandCalculator.Calculate(
        playerPosition,
        centerCell,
        settings);

    foreach (string cellId in bands.PreloadCells)
    {
        if (!cells.ContainsKey(cellId))
            QueueCellLoad(cellId, playerPosition);
    }

    foreach (XeniteCellRuntime cell in cells.Values)
    {
        if (bands.SpawnCells.Contains(cell.CellId))
        {
            EnsureViewsActive(cell);
        }
        else if (bands.RetentionCells.Contains(cell.CellId))
        {
            RetainOrHideViews(cell);
        }
        else
        {
            QueueUnload(cell);
        }
    }

    EnforceGlobalBudgets(playerPosition);
}
```

Cell loading:

```csharp
async Task LoadCellAsync(
    XeniteCellRuntime cell,
    CancellationToken cancellationToken)
{
    cell.State = StreamedCellState.Generating;

    IReadOnlyList<XeniteDepositDefinition> definitions =
        generator.GenerateCell(worldConfig, cell.CellId);

    cell.State = StreamedCellState.LoadingPersistentState;

    IReadOnlyDictionary<string, XeniteDepositState> states =
        await stateRepository.GetStatesForCellsAsync(
            worldConfig.WorldId,
            worldConfig.GeneratorVersion,
            new[] { cell.CellId },
            cancellationToken);

    var visibleDefinitions = ApplyStateAndCull(definitions, states);

    cell.State = StreamedCellState.ResolvingTerrain;

    foreach (var definition in visibleDefinitions.OrderByDistanceToPlayer())
    {
        cancellationToken.ThrowIfCancellationRequested();

        XeniteTerrainPlacement placement =
            await terrainPlacement.ResolveAsync(
                definition,
                cancellationToken);

        cell.AddPreparedDeposit(definition, states, placement);
    }

    cell.State = StreamedCellState.ReadyToSpawn;
    SpawnPreparedDepositsWithinBudget(cell);
    cell.State = StreamedCellState.Active;
}
```

In production, batch cell-state requests and terrain queries rather than awaiting each sequentially.

---

# 16. Fast Travel and Long Road Trips

## Normal travel

For walking or driving:

- Entering new cells triggers preloading.
- Cells behind the player remain briefly retained.
- Object pooling avoids churn.
- Deposits appear before entering interaction range.

## 500-mile road trip

The system does not retain the whole trip in memory.

At any moment it keeps only:

- Current active cells.
- Nearby preload cells.
- A small retention ring.
- Cached disk data from prior cells.

As the user moves:

```text
old cells → unload/pool
new cells → deterministic generate
state overrides → load from cache/API
terrain heights → resolve
views → spawn
```

## Return home

The Douglas-area cell IDs are the same as before.

The generator recreates the same base deposits because:

- World seed is unchanged.
- Generator version is unchanged.
- Cell IDs are unchanged.
- Candidate indices are unchanged.

The local/backend state repository reapplies claimed or depleted state using the same stable deposit IDs.

No home-specific seed data is required.

## Teleport/debug movement

If movement exceeds a threshold, such as 25 km between samples:

1. Cancel pending cell work.
2. Immediately pool all nonessential views.
3. Recenter streaming on the new location.
4. Load nearest cells first.
5. Avoid trying to stream every intermediate cell.

Real GPS traces used for route creation may still contain the journey, but resource streaming should respond only to the current vicinity and short forward prediction.

---

# 17. Performance Budgets

Starting mobile budgets:

```text
Streaming-set refresh:        <= once per 2–5 seconds or cell transition
Concurrent cell pipelines:    2
Terrain requests started:     2–4 per frame
Full deposit meshes:          20–40
Total active deposit views:   <= 120
Interactive colliders:        nearest 10–25
Particle/glow effects:        nearest 10–20
Disk cache target:            configurable, e.g. 100–250 MB
```

Measure on the lowest supported iPhone and Android device.

Avoid:

- One `Update()` method per distant deposit.
- Per-frame distance checks on hundreds of objects.
- Per-deposit HTTP calls.
- Instantiating prefabs during large bursts.
- Synchronous file/database access on the main thread.
- Unity physics for far-away markers.

Use centralized batched updates.

---

# 18. Security and Anti-Cheat

A client can generate deposits, but a multiplayer server should validate mutations.

Server validation should include:

- Valid world and generation version.
- Valid deterministic deposit ID.
- Deposit coordinate reconstruction.
- Maximum claim distance from trusted or plausibility-checked player location.
- Deposit is not already claimed or depleted.
- Expected revision.
- Rate limits.
- Idempotent operation ID.
- Plausible travel speed between interactions.

Do not trust client-supplied quantity, rarity, or coordinates as authoritative.

---

# 19. Debugging Tools

Create an in-game debug overlay showing:

```text
Current latitude/longitude
Current cell ID
Active/preload/retention cell counts
Queued cell jobs
Active deposit count
Pooled deposit count
Terrain query queue length
State cache hits/misses
API latency
Offline operation count
World seed
Generator version
```

Add optional gizmos:

- Active cell boundaries.
- Preload cell boundaries.
- Retention cell boundaries.
- Candidate points.
- Rejected terrain points.
- Deposit IDs.
- Player prediction corridor.

Add developer commands:

```text
Teleport to latitude/longitude
Clear local xenite cache
Force offline mode
Force cell reload
Dump generated cell JSON
Claim nearest deposit
Reset local pending operations
Compare generated cell against golden vector
```

---

# 20. Testing Requirements

## Determinism tests

Given the same:

- World seed.
- Generator version.
- Cell ID.

The generator must produce byte-for-byte stable IDs and numerically stable coordinates.

Test across:

- Editor.
- iOS IL2CPP.
- Android IL2CPP.
- Backend implementation.

## Geographic tests

Test:

- Douglas, Wyoming.
- Equator.
- Northern and southern hemispheres.
- International Date Line.
- Near-polar supported latitudes.
- Cell boundaries.
- Long-distance teleport.

## Streaming tests

Verify:

- No duplicate deposits across neighboring cells.
- No rapid thrashing at cell boundaries.
- Cancellation prevents stale spawns.
- Maximum active-object budget is respected.
- Return to a prior location restores identical IDs and positions.
- Origin shift does not move deposits geographically.
- App resume rebuilds the correct streaming set.

## Persistence tests

Verify:

1. Spawn a deposit.
2. Claim it.
3. Unload the cell.
4. Restart the app.
5. Return to the cell.
6. Confirm the same deposit remains claimed.

Also test:

- Offline claim accepted later.
- Offline claim rejected due to another player.
- Duplicate operation submission.
- Stale revision.
- Cache eviction and reload.
- Generator-version mismatch.

---

# 21. Migration From the Current POC

Implement in phases.

## Phase 1: local deterministic streaming

- Add `IGeoCellIndex`.
- Add world seed and generator version.
- Generate xenite by nearby cell.
- Remove Douglas-only xenite filtering.
- Spawn with `CesiumGlobeAnchor`.
- Add unload/pooling.
- Store claimed state locally.

Acceptance:

- Teleporting between Douglas and another state produces local deposits.
- Returning to Douglas reproduces the original deposits.
- Restarting preserves locally claimed state.

## Phase 2: backend state overrides

- Add cell state batch endpoint.
- Add claim endpoint.
- Add optimistic concurrency.
- Store sparse mutations.
- Cache cell revisions.

Acceptance:

- Two clients see the same deposits.
- Claims propagate and survive reload.
- Conflicting claims resolve server-side.

## Phase 3: offline operations

- Add SQLite repository.
- Add pending operation queue.
- Add idempotent synchronization.
- Add pending/confirmed/rejected visuals.

Acceptance:

- A disconnected road trip continues generating resources.
- Cached areas restore without a network.
- Reconnection reconciles mutations.

## Phase 4: biome and terrain suitability

- Add slope/water checks.
- Add deterministic fallback points.
- Add biome-weighted density.
- Add height cache versioning.

## Phase 5: production optimization

- Tune cell size and radii.
- Batch terrain/state work.
- Add LOD and GPU-friendly glow.
- Profile IL2CPP builds.
- Add cache pruning.
- Add telemetry.

---

# 22. Explicit Changes to Existing Behavior

Remove this POC behavior for production:

```text
If player GPS is far from Douglas:
    park the pin at the play area so xenite remains on-screen.
```

Replace it with:

```text
Manual GPS:
    player and streaming center follow actual WGS84 position.

Auto Pilot:
    player and streaming center follow simulated WGS84 position.

Editor:
    streaming center follows configured test coordinates or debug camera.
```

The xenite source should no longer be a complete resource array from `sample-world-map.json`.

The sample map may retain hand-authored xenite only as:

- Tutorial deposits.
- Developer test fixtures.
- Explicit landmarks.

Mark these as `AuthoredDepositDefinition` and merge them with procedural definitions by stable ID. Do not let authored and procedural deposits overlap unintentionally.

---

# 23. Recommended Final Data Flow

```text
GPS / Auto Pilot WGS84 sample
        │
        ▼
RtgXeniteStreamingService
        │
        ├── Determine center geographic cell
        ├── Calculate spawn/preload/retention cells
        ├── Prioritize cells by distance and heading
        │
        ▼
XeniteDepositGenerator
        │
        └── Stable definitions from:
            world seed + generator version + cell ID
        │
        ▼
CompositeXeniteStateRepository
        │
        ├── Local cache
        ├── Pending offline mutations
        └── Remote sparse state overrides
        │
        ▼
XeniteTerrainPlacementService
        │
        └── Terrain height + deterministic suitability correction
        │
        ▼
XeniteViewPool
        │
        └── Globe-anchored Unity views near player
        │
        ▼
Claim interaction
        │
        ├── Immediate pending local state
        ├── Idempotent server operation
        └── Authoritative reconciliation
```

---

# 24. Cursor Implementation Directives

Cursor should follow these constraints:

1. Preserve the Cesium-versus-Unity responsibility split.
2. Keep WGS84 coordinates and stable IDs authoritative.
3. Never derive persistence keys from Unity transforms.
4. Never generate the entire planet.
5. Never perform a network request per deposit.
6. Batch mutation-state retrieval by cell.
7. Make generation pure and deterministic.
8. Version the generator from its first release.
9. Use cancellation tokens for all asynchronous cell work.
10. Use object pooling.
11. Centralize update loops and frame budgets.
12. Keep third-party geographic indexing behind `IGeoCellIndex`.
13. Preserve offline operation.
14. Make server claims authoritative for multiplayer.
15. Add tests before replacing the existing xenite loader.

---

# 25. Definition of Done

The implementation is complete when:

- A player can launch near Douglas and see nearby xenite.
- The same world seed produces the same deposits on two devices.
- Moving or teleporting hundreds of miles loads new nearby deposits without loading intervening regions.
- The number of loaded objects remains bounded.
- Returning to Douglas restores the exact prior deposit positions.
- Claimed deposits retain their claimed status after unload, restart, and return.
- Cesium origin shifting does not alter resource identity or geographic placement.
- The app remains usable without connectivity.
- Multiplayer claim conflicts are reconciled by the backend.
- Generator changes cannot silently relocate deposits in existing worlds.
