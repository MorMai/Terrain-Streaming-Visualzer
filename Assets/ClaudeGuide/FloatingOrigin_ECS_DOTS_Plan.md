# Floating Origin in ECS / DOTS — Implementation Plan

> A plan for porting the floating-origin (origin-rebasing) system from the GameObject/MonoBehaviour
> visualizer to a Unity **ECS / DOTS** game. The *core idea* is identical — keep rendered floats
> small, bank the big numbers into a high-precision offset — but the mechanics change a lot:
> data-oriented layout, no per-object `Update`, and a parallel Burst job for the rebase.

---

## 0. The idea in one sentence

Render the world relative to a periodically-reset origin while a high-precision `double` offset
preserves the true position — keeping `float` positions small where precision matters and pushing
the "big numbers" into a value that is never rendered.

---

## 1. Mapping from the MonoBehaviour version

| MonoBehaviour version (current project) | ECS / DOTS equivalent |
|---|---|
| `FloatingOrigin` MonoBehaviour (`Update`) | `WorldOrigin` singleton + `RebaseSystem` (`ISystem`, Burst) |
| `OffsetX/Y` (double) | `WorldOrigin.Offset` (`double3`) on a singleton entity |
| `transform.position` (rendered) | `LocalTransform.Position` (`float3`) |
| `PlayerController.AbsolutePosition` | `LocalTransform.Position + (float3)WorldOrigin.Offset`, or a `double3` on key entities |
| "shift player + camera + cells" each rebase | one parallel `IJobEntity` over all rebaseable root entities |
| `CurrentChunk` from absolute position | streaming system reads absolute position |
| `ChunkCenterRendered()` (= absolute − offset) | same math in the streaming/placement system |
| `[DefaultExecutionOrder(100)]` ordering | `[UpdateBefore(typeof(TransformSystemGroup))]` |

**The discipline is the same:** *simulation reads absolute, rendering reads `LocalTransform`.*

---

## 2. Data (components)

```csharp
using Unity.Entities;
using Unity.Mathematics;

// Singleton – global bookkeeping.
public struct WorldOrigin : IComponentData
{
    public double3 Offset;     // absolute = rendered + Offset
    public float   Threshold;  // rebase when focus exceeds this (DOTS scales, so go large: e.g. 1000)
    public int     RebaseCount;
    public bool    Enabled;
}

// Tag: "shift me when the world rebases". Add to root-space entities (no Parent).
public struct WorldRebaseable : IComponentData {}

// Tag: the entity whose distance from origin drives rebasing (player/camera).
public struct RebaseFocus : IComponentData {}

// Optional: full-precision true position for gameplay that needs it (streaming, etc).
public struct AbsolutePosition : IComponentData { public double3 Value; }
```

`LocalTransform.Position` stays `float3` (rendered). Only `Offset` / `AbsolutePosition` are `double3`.
Burst handles `double3` math fine.

---

## 3. Systems & update order

One detection-and-apply system runs **early in the frame**, before `TransformSystemGroup`
(so `LocalToWorld` reflects the shift) and before the physics group.

```
SimulationSystemGroup
 ├─ RebaseSystem          [UpdateBefore(typeof(TransformSystemGroup))]
 ├─ ... gameplay / streaming systems ...
 ├─ TransformSystemGroup     (builds LocalToWorld from LocalTransform)
 └─ PhysicsSystemGroup
```

`RebaseSystem` logic each frame:
1. Read the `RebaseFocus` entity's `LocalTransform.Position`.
2. If `Enabled` and `length(pos.xy) > Threshold` → compute `shift` (snap to focus, or to a sector multiple).
3. `Offset += (double3)shift; RebaseCount++`.
4. Schedule a parallel job subtracting `shift` from every rebaseable root.

Because we subtract `shift` from rendered positions **and** add the same `shift` to `Offset`,
`absolute = rendered + Offset` is invariant — the true position never moves.

---

## 4. The rebase job — where DOTS shines

The MonoBehaviour version shifted ~3 transforms. In DOTS this can be **hundreds of thousands**
of entities, but the shift is embarrassingly parallel, Burst-compiled, and cache-friendly:

```csharp
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[WithAll(typeof(WorldRebaseable))]
[WithNone(typeof(Parent))]                 // only roots; children inherit via TransformSystemGroup
public partial struct RebaseJob : IJobEntity
{
    public float3 Shift;
    void Execute(ref LocalTransform t) => t.Position -= Shift;
}
```

```csharp
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TransformSystemGroup))]
public partial struct RebaseSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<WorldOrigin>();
        state.RequireForUpdate<RebaseFocus>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var originRW = SystemAPI.GetSingletonRW<WorldOrigin>();
        ref var origin = ref originRW.ValueRW;
        if (!origin.Enabled) return;

        // Focus position (single entity).
        float3 focus = float3.zero;
        foreach (var t in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<RebaseFocus>())
            focus = t.ValueRO.Position;

        if (math.abs(focus.x) < origin.Threshold && math.abs(focus.y) < origin.Threshold)
            return;

        float3 shift = new float3(focus.x, focus.y, 0f); // bring focus back to rendered origin
        origin.Offset += new double3(shift.x, shift.y, shift.z);
        origin.RebaseCount++;

        state.Dependency = new RebaseJob { Shift = shift }.ScheduleParallel(state.Dependency);
        // Camera (managed) is shifted in a separate SystemBase, or is itself a rebaseable entity.
    }
}
```

Only shift **root** entities (`WithNone<Parent>`): children are relative to their parent and come
along for free once `TransformSystemGroup` recomputes `LocalToWorld`.

---

## 5. Subsystem integration

- **Rendering (Entities Graphics):** free. `LocalToWorld` derives from `LocalTransform`; shift the
  position and rendering follows. No change needed.
- **Unity Physics:** rebase *before* the physics build step (the ordering above does this).
  `LocalTransform` positions shift; `PhysicsVelocity` is a delta so it is untouched; contacts/joints
  are relative. Tag **static colliders** `WorldRebaseable` too, or they detach from the world.
- **Camera:** usually still a managed GameObject. Drive it from an entity's `LocalToWorld` via a
  managed `SystemBase`, or shift it in that system. If it is an entity, tag it `WorldRebaseable`.
- **Streaming:** unchanged in spirit. Compute `chunkCoord = floor(absolute / chunkSize)` from
  `LocalTransform.Position + Offset`, and place streamed content at `absoluteCenter - Offset`
  (the DOTS analog of `ChunkCenterRendered`). Invariant under rebasing.

---

## 6. The big DOTS-specific decision: NetCode / determinism

If you use **Unity NetCode**, floating origin must be a **client-only presentation concern** —
it must never touch ghost-synchronized or predicted state, or clients desync.

- **Simulation / ghost transforms = absolute** (often quantized integers for determinism).
  Authoritative and identical on every client.
- A **client-only presentation pass** computes the render `LocalTransform = absolute - clientOffset`,
  with each client free to rebase independently.

This cleanly decouples a `Simulated`/absolute transform from the render `LocalTransform` — which is
the same "two coordinate spaces" split we already have, just promoted to two explicit components
instead of one transform plus an offset.

For **single-player** DOTS you can skip this and rebase `LocalTransform` directly, as above.

---

## 7. Gotchas

- **Only shift roots** (`WithNone<Parent>`) — shifting a child double-applies via its parent.
- **World-space trails / particles / line renderers** smear on rebase; switch them to local space or
  clear them on the rebase frame (the same caveat as the `LineRenderer` sight cone in the MonoBehaviour version).
- **Anything caching a world position across frames** (a stored target `float3`) must be stored in
  absolute space, or refreshed after a rebase.
- **Double precision lives only on `Offset`** (and any `AbsolutePosition`); `LocalTransform` stays `float`.
- **Sectored extension** for planetary scales: store position as `int3 sector + float3 local` instead
  of one `double3` — same principle, more headroom, and integer sectors are deterministic (netcode-friendly).
- **Timing matters:** if the rebase runs *after* something reads positions for that frame, you get a
  one-frame jump. Keep it before `TransformSystemGroup` and the physics group.

---

## 8. Suggested implementation phases

1. `WorldOrigin` singleton + `WorldRebaseable` / `RebaseFocus` tags; bake them onto authoring GameObjects
   (an `IComponentData` baker per authoring component).
2. `RebaseSystem` with the Burst `IJobEntity` shift, ordered before `TransformSystemGroup`. Single-player first.
3. Debug readouts (render pos / absolute pos / rebase count) via UI Toolkit or a hybrid HUD — the same
   readouts as the MonoBehaviour visualizer.
4. Wire streaming to read absolute position; verify chunk coordinates are invariant across rebases.
5. Physics pass: tag static colliders, confirm ordering runs before the physics group.
6. If networked: split into absolute (ghost/simulated) vs render `LocalTransform`, and move rebasing
   into a client-only presentation system.

---

## 9. Summary

In ECS the **trigger and bookkeeping stay tiny** (one singleton, one detection system), while the
**apply step becomes a parallel Burst job** that scales to entity counts a MonoBehaviour world could
never touch. The only genuinely new design question is the **netcode split**, which is just the
"rendered vs absolute" idea made explicit as two components. Everything else is a near-direct
translation of the system already running in this project's visualizer.
