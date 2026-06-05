# Floating Origin & Terrain Streaming — Corrected Design (v2)

**Project:** Airship Kingdom Ablaze
**Status:** Validated rewrite — replaces `floating_origin_terrain_streaming_design.md` (v1)
**Authority:** Consistent with `# World Engine  Floating Origin Arc.txt` (v1.7) and validated against the codebase. See `floating_origin_terrain_streaming_RECONCILED.md` for the line-by-line change log.

> **Validated facts this design is built on**
> - ECS/DOTS exists **only** in the battle-simulation layer (`Scripts\Gameplay\Battle\Simulation\` — Ordnance, Turret). The world/streaming layer is **not** ECS.
> - The world/origin/streaming driver is a **MonoBehaviour** (`ChunkStreamer`), per World Engine v1.7.
> - Chunk content streams via **Addressables** (already used in the project), **not** procedural generation.
> - The world is a **finite 300×300 km theater**, target **120 FPS**.

---

## 1. Problem Statement

Unity transforms are single-precision `float`. Precision degrades past ~5 km from origin and becomes visually objectionable past ~10 km, producing mesh/particle jitter and physics drift. Across a 300×300 km theater the player would routinely sit tens of kilometres from origin.

Separately, the full theater cannot be resident at once — terrain, sky islands, cloud layers, and horizon detail must stream in and out as the player travels, while keeping draw calls and memory bounded.

---

## 2. Goals

| Goal | Mechanism |
|------|-----------|
| Eliminate float jitter anywhere in the 300×300 km theater | Floating-origin recentering (horizontal) |
| Keep the player visually near physical origin | Periodic reset to `(0, y, 0)` |
| Preserve true position at full precision | `double3` `TrueWorldPos` / `CumulativeOffset` |
| Stream content without hitches | Async **Addressables** load/release on Chebyshev rings |
| Bound draw calls at the horizon | Low-poly **Proxy ring** (SRP Batcher + GPU Instancing) |
| Keep the ECS battle sim oblivious to world scale | Battle layer always runs near origin; subscribes to shift |
| Designer-configurable | `World_Cfg` / `ChunkDef` ScriptableObjects |

---

## 3. Architecture Overview

The macro world layer **owns** the origin shift and streaming. The ECS battle layer and all non-ECS subscribers **react** to it via the `*_Sig` event bus.

```
[ChunkStreamer  (MonoBehaviour)]  ── owns origin shift + chunk streaming
   │  reads PlayerTransform, World_Cfg, WorldState_Var
   │  enforces TravelAltitude clamp (Y)
   │
   │  when |horizontal player pos| > ThresholdDistance:
   │     delta = (pos.x, 0, pos.z)
   │     CumulativeOffset += delta ; TrueWorldPos updated ; ShiftCounter++
   │     shift own world roots + all HD/Proxy chunk roots by −delta
   │     raise onOriginShifted_Sig(delta)  ───────────────┐
   │                                                       ▼
   │                                          [Subscribers]
   │                                          • ECS battle layer → Burst offsets LocalTransform.Position by −delta
   │                                          • Camera rig, Audio listener, Particles, VFX (Obvious Soap bus)
   │                                          • Travel-Mode ship sim + AirshipCore visual-proxy pool
   │
   └─ Chebyshev ring evaluation each frame:
        HighDetailRadius → Addressables load HD_Chunk_X_Y
        ProxyRingDepth   → Addressables load batched Proxy prefabs
        leaving rings    → Addressables release
        raises onChunkLoaded_Sig / onChunkUnloaded_Sig
```

---

## 4. Floating Origin

### 4.1 Data

| Name | Type | Convention | Description |
|------|------|-----------|-------------|
| `World_Cfg` | ScriptableObject | `*_Cfg` | `TheaterSize` (300 km), `ChunkSize` (5000 m), `HighDetailRadius`, `ProxyRingDepth`, `TravelAltitude`, `ThresholdDistance` (2000 m) |
| `WorldState_Var` | runtime ScriptableObject | `*_Var` | `TrueWorldPos` (double3), `CumulativeOffset` (double3), `ChunkIndex` (Vector2Int), `ShiftCounter` (int) |
| `onOriginShifted_Sig` | ScriptableEvent | `*_Sig` | Broadcast `delta` to all subscribers after a shift |

### 4.2 Trigger Logic

- Each frame `ChunkStreamer` reads the **horizontal** magnitude of `PlayerTransform` relative to physical origin: `length(pos.x, pos.z)`.
- If it exceeds `ThresholdDistance` (default **2000 m**), a shift is performed.
- **Optional hysteresis** (not in v1.7; tune if oscillation appears): require `> ThresholdDistance + band` (band ≤ 200 m) to re-trigger near the boundary.
- Y is **not** part of the trigger — altitude is governed by the `TravelAltitude` clamp.

### 4.3 Shift Execution

1. `delta = float3(pos.x, 0, pos.z)` — horizontal only; **Y is preserved** (`(0, y, 0)` recenter).
2. `CumulativeOffset += delta` (accumulated as `double3` for full precision); recompute `TrueWorldPos`; `ShiftCounter++`.
3. Shift the streamer's own world roots and **all loaded HD/Proxy chunk roots** by `−delta`.
4. Raise `onOriginShifted_Sig(delta)`.
5. Subscribers self-correct:
   - **ECS battle layer** schedules a Burst pass offsetting every `LocalTransform.Position` by `−delta`, capturing `delta` as an immutable job parameter.
   - **Non-ECS** (camera rig, audio listener, particles, VFX modules) shift their own transforms.
   - **Travel-Mode** ship simulation + `AirshipCore` visual proxies shift.

### 4.4 Ordering Guarantee

The shift must be **fully applied across every layer before the camera renders**, or a one-frame position pop appears. Perform the shift + broadcast at end-of-frame / before render so ECS transforms, MonoBehaviour transforms, and Addressable chunk roots are all consistent in the same frame.

### 4.5 Physics

If Unity Physics runs in the battle layer, rebuild/offset its broadphase on shift. The high **2000 m** threshold keeps shifts rare, so this cost is infrequent. (Confirm whether the macro layer uses physics at all — if battle-only, this is scoped to ECS.)

---

## 5. Terrain Streaming

### 5.1 Chunk Coordinate System — 2D, precision-immune

Chunks use a uniform **5000 m** horizontal grid addressed by **`Vector2Int ChunkIndex`** (X, Z). Integer addressing never accumulates float error, so it is correct anywhere in the theater. Altitude is handled by the `TravelAltitude` clamp, not a chunk axis — so streaming is 2D, not 3D.

```
ChunkIndex     = floor( horizontal(TrueWorldPos) / ChunkSize )         // Vector2Int, clamped to TheaterSize
chunkWorldPos  = double3(ChunkIndex.x * ChunkSize, y, ChunkIndex.y * ChunkSize)
chunkRenderPos = float3( chunkWorldPos - CumulativeOffset )             // small magnitude → float-safe
```

### 5.2 Detail Tiers (Chebyshev rings)

| Tier | Selector | Content |
|------|----------|---------|
| **High-Detail** | `Chebyshev(ChunkIndex, playerChunk) <= HighDetailRadius` (radius 1 → 3×3) | Collision, high-poly meshes, complex shaders — `HD_Chunk_X_Y` Addressables |
| **Proxy ring** | `<= HighDetailRadius + ProxyRingDepth` | Low-poly, shared-material horizon — batched via SRP Batcher + GPU Instancing |
| **Unloaded** | beyond the proxy ring | Released from memory |

### 5.3 Data

| Name | Type | Convention | Description |
|------|------|-----------|-------------|
| `ChunkDef` | ScriptableObject | `*_Def` | Per-chunk Addressable key + LOD/content tier metadata (no procedural noise params) |
| `ChunkState` | streamer-side enum | — | `Unloaded \| Loading \| Loaded \| Unloading` |
| `onChunkLoaded_Sig` | ScriptableEvent | `*_Sig` | Fired on transition to Loaded |
| `onChunkUnloaded_Sig` | ScriptableEvent | `*_Sig` | Fired on transition to Unloaded |

### 5.4 Chunk State Machine (Addressables)

```
Unloaded ──[enters ring]──► Loading ──[handle Completed]──► Loaded
                               │                              │
Unloaded ◄──[release done]── (release) ◄──[leaves ring]──────┘
                               ▲
Loading ──[leaves ring before load finishes]──► (release in-flight handle) ──► Unloaded
```

| Transition | Trigger | Action |
|-----------|---------|--------|
| `Unloaded → Loading` | Chunk enters desired ring | `Addressables.InstantiateAsync(key)`; store `AsyncOperationHandle`; capture `CumulativeOffset` snapshot; place root at `chunkRenderPos` |
| `Loading → Loaded` | Handle `Completed` | Re-place root using current `CumulativeOffset`; raise `onChunkLoaded_Sig` |
| `Loaded → Unloading` | Chunk leaves ring | `Addressables.ReleaseInstance(...)` |
| `Unloading → Unloaded` | Release complete | Drop registry entry; raise `onChunkUnloaded_Sig` |
| `Loading → Unloaded` | Leaves ring before load finishes | Release the in-flight handle; discard |

### 5.5 Offset Snapshot

When a load starts, snapshot `CumulativeOffset`. If an origin shift fires mid-load, the chunk is positioned with the **current** offset on completion (step `Loading → Loaded`), so it can never be misplaced by a shift that happened during the async load. In-flight loads are otherwise unaffected — addressing is integer-based.

---

## 6. System Interaction

### 6.1 Origin Shift During Active Streaming
- **In-flight Addressable loads:** safe — integer addressing is shift-immune; final placement uses current `CumulativeOffset`.
- **Loaded HD/Proxy chunks:** their roots are shifted by `−delta` in the same frame as the origin shift (§4.3 step 3).
- **Ring evaluation:** operates in integer `ChunkIndex` space → unaffected.

### 6.2 Data Flow
```
World_Cfg / ChunkDef (designer, Google-Sheets-syncable)
        │
        ▼
ChunkStreamer ──► Addressables.InstantiateAsync(HD_Chunk_X_Y / Proxy)
        │                         │
        │                         ▼
        │                 Loaded chunk root (positioned at chunkRenderPos)
        │                         │
        └──► onChunkLoaded_Sig ───┴──► AudioService, VFX modules
```

---

## 7. Adjacent System — Travel Mode Ecosystem

Per World Engine v1.7 §5: 1,000–5,000 background vessels simulated as lightweight `WorldShipData` structs (DOD / Burst) in absolute space. When one enters the player's visibility bubble, a pooled `AirshipCore` prefab (100-entry visual-proxy registry) is claimed and driven from the struct; on exit it is recycled. This system also subscribes to `onOriginShifted_Sig` so active visual proxies shift with everything else.

---

## 8. Sovereign Jump (Fast Travel)

`LocationRegistry_SO` maps string IDs to absolute `double3` coordinates. A jump sets `TrueWorldPos` to the target, recomputes `CumulativeOffset` so the player lands near physical origin, forces a `ChunkIndex` recompute, and lets `ChunkStreamer` stream the destination rings.

---

## 9. Key Decisions & Tradeoffs

| Decision | Choice | Tradeoff |
|---------|--------|---------|
| Shift driver | MonoBehaviour `ChunkStreamer` | Matches v1.7 + codebase; ECS battle layer subscribes rather than owns |
| Shift threshold | 2000 m | Rare physics rebuilds; well inside float-safe zone |
| Recenter axes | Horizontal only `(0, y, 0)` | Altitude stays under `TravelAltitude` clamp; avoids vertical popping |
| Chunk size | 5000 m uniform | Stable grid alignment; LOD changes by ring, not by size |
| Chunk addressing | `Vector2Int` 2D | Precision-immune; altitude clamp removes need for 3D streaming |
| Ring shape | Chebyshev (square) | Matches HD/Proxy ring counts; no corner over-load |
| Content source | Addressables prefabs | Uses existing pipeline; authored art; no runtime mesh gen |
| Horizon | Proxy ring + SRP Batcher + GPU Instancing | Bounds draw calls at distance |
| World extent | Finite 300×300 km | Bounded memory/addressing; clamp `ChunkIndex` to `TheaterSize` |

---

## 10. Integration Points

| Existing | Integration |
|----------|------------|
| ECS battle sim (Ordnance/Turret) | Unchanged logic; subscribes to `onOriginShifted_Sig` to offset `LocalTransform.Position` |
| `BattleManager` | Re-anchors spawn positions on shift |
| VFX modules (20+) | Subscribe to `onOriginShifted_Sig` via Obvious Soap bus |
| `ServiceLocator` / DI | `ChunkStreamer` registered on the persistent controller |
| `*_Cfg` / `*_Var` / `*_Def` / `*_Sig` conventions | `World_Cfg`, `WorldState_Var`, `ChunkDef`, the `*_Sig` events |
| Addressables | HD/Proxy chunk groups keyed by grid coord |
| Google Sheets pipeline | `World_Cfg` / `ChunkDef` parameters sheet-sourced |

---

## 11. Out of Scope

- Procedural noise / runtime mesh generation (deferred to content authoring only).
- LOD mesh authoring strategy beyond the HD/Proxy split.
- Network sync of chunk/origin state (multiplayer).
- Save/load of destructible/modified chunks.

---

## 12. To Confirm With a Human

1. Hysteresis band — keep it (≤200 m), or rely on the 2000 m threshold alone?
2. Does the macro/world layer run Unity Physics, or is physics battle-layer-only?
3. Keep per-chunk altitude/content tiers as `ChunkDef` metadata, or drop given the `TravelAltitude` clamp?
4. Confirm procedural noise is authoring-time only (no runtime generation).
