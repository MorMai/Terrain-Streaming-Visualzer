# Software Requirements Specification (SRS) — Prompt

## Project: 2D Bird's-Eye Level Streaming Visualizer (Unity)

> **How to use this document:** This is a bare-bones SRS written as a build prompt. Hand it to a coding agent or use it as your own spec. It defines a minimal, top-down 2D Unity scene whose only job is to *visualize* a chunk-streaming system: a player circle, a cone-of-sight line renderer, and a colored grid that flips chunks to green as they "load." The streaming logic starts as a simple 3×3 window but is architected so the loading strategy can be swapped without touching the visualization.

---

## 1. Introduction

### 1.1 Purpose
Build the smallest possible Unity project that makes a level-streaming algorithm *visible and debuggable* from a top-down 2D view, before any real world content, art, or 3D streaming exists. The visualizer is a diagnostic tool, not a game.

### 1.2 Scope
**In scope**
- Top-down 2D orthographic view.
- A player represented as a circle that moves around the grid.
- A "cone/frustum of sight" drawn with a `LineRenderer` originating at the player.
- A grid of chunks rendered as cells.
- Per-chunk load state with color feedback (e.g. unloaded → loading → green when loaded).
- A simple **3×3** streaming window centered on the player's current chunk.
- A clean interface boundary so alternative streaming strategies can be plugged in later.

**Out of scope (v1)**
- Real asset/scene loading, addressables, or async I/O of actual content (simulated only).
- 3D rendering, lighting, physics beyond simple movement.
- Persistence, save/load, networking, audio, UI menus.

### 1.3 Definitions
| Term | Meaning |
|------|---------|
| **Chunk** | One grid cell; the atomic unit of streaming. Identified by integer coords `(cx, cy)`. |
| **Streaming window** | The set of chunks the strategy wants loaded *right now* (3×3 around the player in v1). |
| **Load state** | Lifecycle of a chunk: `Unloaded`, `Loading`, `Loaded` (and optionally `Unloading`). |
| **Cone of sight** | A wedge/frustum drawn from the player showing facing + view range. |
| **Strategy** | A swappable component that decides *which* chunks should be loaded. |

### 1.4 References
- Unity `LineRenderer`, `Camera` (orthographic), `Gizmos` documentation.
- Target Unity LTS (e.g. 2022 LTS or later) — specify the version you intend to use.

### 1.5 Overview
Section 2 describes the system at a high level; Section 3 gives the functional and non-functional requirements; Section 4 gives the component/architecture breakdown and the extension seams that satisfy the scalability requirement.

---

## 2. Overall Description

### 2.1 Product Perspective
A single Unity scene running a small set of MonoBehaviours. The world is an infinite-feeling integer grid; only a finite window is ever "loaded." Visualization and streaming logic are deliberately decoupled: the grid renderer subscribes to chunk-state events and never knows *why* a chunk changed state.

### 2.2 Core User Story
> As a developer, I move the player around a 2D grid and watch chunks light up green as they enter the streaming window, so I can verify my streaming logic and later swap in a smarter strategy without rewriting the view.

### 2.3 Assumptions & Constraints
- Square chunks of uniform world size (e.g. `chunkSize = 1` world unit, configurable).
- Orthographic camera, fixed top-down.
- "Loading" is **simulated** via a coroutine/timer delay; no real asset load in v1.
- Single player, single strategy active at a time.

---

## 3. Specific Requirements

### 3.1 Functional Requirements

**FR-1 — Player representation**
- Render the player as a filled circle (sprite or runtime mesh) at a world position.
- Support movement via input (WASD/arrow keys or mouse-follow); configurable speed.
- Expose `CurrentChunk` derived from world position: `cx = floor(x / chunkSize)`, `cy = floor(y / chunkSize)`.

**FR-2 — Cone / frustum of sight**
- Draw a wedge from the player using a `LineRenderer` (outline) showing facing direction, configurable **FOV angle** and **view distance**.
- The cone rotates to match the player's facing/movement direction.
- Cone is purely visual in v1 (no occlusion), but expose `IsChunkInSight(cx, cy)` so a future strategy could stream by visibility.

**FR-3 — Grid rendering**
- Render a grid of chunk cells around the player (at least the streaming window plus a margin for context).
- Each cell shows its load state through color:
  - `Unloaded` → neutral/gray (or outline only)
  - `Loading` → transitional (e.g. yellow/amber)
  - `Loaded` → **green**
- Optional: display chunk coords as labels for debugging.

**FR-4 — Chunk load lifecycle**
- A `Chunk` has state, coords, and a simulated load duration.
- Transition `Unloaded → Loading → Loaded` over a configurable delay (coroutine).
- On leaving the window, transition `Loaded → Unloaded` (optionally via `Unloading`).
- Emit an event on every state change for the renderer to consume.

**FR-5 — 3×3 streaming window (default strategy)**
- Compute the desired set as the 3×3 block of chunks centered on `CurrentChunk`.
- When the player crosses a chunk boundary, recompute: request load for newly-entered chunks, request unload for chunks that fell out of the window.
- No redundant reloads: a chunk already `Loaded`/`Loading` is not re-requested.

**FR-6 — Strategy abstraction (scalability)**
- Define an interface, e.g. `IStreamingStrategy { IEnumerable<ChunkCoord> GetDesiredChunks(ChunkCoord playerChunk, Context ctx); }`.
- The 3×3 system is one concrete implementation (`GridWindowStrategy`).
- The streaming manager consumes *only* the interface, diffing desired-vs-current sets to drive loads/unloads — so swapping in radius-based, sight-cone-based, predictive/velocity-based, or priority-queued strategies requires no manager or renderer changes.

### 3.2 Non-Functional Requirements
- **NFR-1 Modularity:** visualization, streaming manager, and strategy are separate components communicating via events/interfaces.
- **NFR-2 Configurability:** chunk size, grid extent, window size, FOV, view distance, load delay, and colors exposed in the Inspector.
- **NFR-3 Performance:** no per-frame allocations in the streaming loop; only recompute on chunk-boundary crossing, not every frame.
- **NFR-4 Determinism/Debuggability:** state changes logged and/or shown via Gizmos so behavior is inspectable in the editor.
- **NFR-5 Minimalism:** no external packages required for v1 beyond a Unity LTS install.

---

## 4. System Architecture

### 4.1 Components
| Component | Responsibility |
|-----------|----------------|
| `PlayerController` | Movement, facing, exposes `WorldPos` + `CurrentChunk`. |
| `SightCone` | Builds/updates the `LineRenderer` wedge; `IsChunkInSight()`. |
| `ChunkGridRenderer` | Spawns/colors cell visuals; subscribes to chunk state-change events. |
| `StreamingManager` | Detects chunk crossings, queries the active `IStreamingStrategy`, diffs desired vs. loaded, drives load/unload. |
| `Chunk` (data) | Coords, state, simulated load coroutine, state-change event. |
| `IStreamingStrategy` | Pluggable "which chunks should be loaded" decision. |
| `GridWindowStrategy` | Default 3×3 implementation. |

### 4.2 Data Flow
1. `PlayerController` updates position → `StreamingManager` checks if `CurrentChunk` changed.
2. On change, `StreamingManager` asks active strategy for desired chunk set.
3. Manager diffs against currently loaded/loading set → issues load requests (new) and unload requests (dropped).
4. Each affected `Chunk` runs its lifecycle and fires a state-change event.
5. `ChunkGridRenderer` recolors the corresponding cell (green when `Loaded`).

### 4.3 Extension Seams (satisfies scalability requirement)
- **New strategy:** implement `IStreamingStrategy`, assign it on `StreamingManager` — done.
- **Real loading later:** replace the `Chunk` simulated-delay coroutine with async asset/scene loading (e.g. Addressables) behind the same state machine; renderer is unaffected.
- **Sight-driven streaming:** a strategy can call `SightCone.IsChunkInSight()` to load only visible chunks.
- **Larger/variable windows:** parameterize window radius or supply a different strategy; the 3×3 is just `radius = 1`.

---

## 5. Acceptance Criteria (v1 "done")
- [ ] Player circle moves smoothly in a top-down 2D view.
- [ ] A `LineRenderer` cone follows the player's facing with configurable FOV/range.
- [ ] A grid is visible; cells reflect load state by color and turn **green** when loaded.
- [ ] Moving across chunk boundaries loads the new 3×3 window and unloads what falls outside it, with a visible loading transition.
- [ ] Swapping `GridWindowStrategy` for a stub alternate strategy changes which chunks load **without editing** `StreamingManager` or `ChunkGridRenderer`.

---

## 6. Suggested Build Order
1. Orthographic camera + player circle + movement.
2. World→chunk coordinate mapping and `CurrentChunk`.
3. Grid renderer with static cell colors.
4. `Chunk` state machine with simulated load delay + events; wire renderer to events.
5. `StreamingManager` + `IStreamingStrategy` + `GridWindowStrategy` (3×3); chunk-crossing detection and diff-based load/unload.
6. `SightCone` LineRenderer + `IsChunkInSight()`.
7. Expose all tunables to Inspector; add Gizmos/logging.
