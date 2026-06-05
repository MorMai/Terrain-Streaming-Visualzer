using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Designer-configurable world constants (the <c>*_Cfg</c> convention from the
    /// "Floating Origin &amp; Terrain Streaming" v2 design). One immutable asset, sheet-syncable.
    ///
    /// Scale note: the meter-valued fields (<see cref="TheaterSize"/>, <see cref="ChunkSize"/>,
    /// <see cref="ThresholdDistance"/>, <see cref="TravelAltitude"/>) carry the real
    /// "Airship Kingdom Ablaze" targets so this asset documents the production design. The 2D
    /// visualizer runs at an arbitrary scale (its live <c>PlayerController.chunkSize</c> /
    /// <c>FloatingOrigin.threshold</c> may differ); the unit-independent ring counts
    /// (<see cref="HighDetailRadius"/>, <see cref="ProxyRingDepth"/>) are what actually drive
    /// streaming and are correct at any scale.
    /// </summary>
    [CreateAssetMenu(menuName = "World Engine/World_Cfg", fileName = "World_Cfg")]
    public class World_Cfg : ScriptableObject
    {
        [Header("Theater")]
        [Tooltip("Finite world extent (metres). Design target: 300 km.")]
        public float TheaterSize = 300000f;

        [Tooltip("Uniform horizontal chunk grid size (metres). Design target: 5000 m.")]
        public float ChunkSize = 5000f;

        [Header("Detail rings (Chebyshev)")]
        [Tooltip("High-detail radius in chunks. 1 => 3x3 HD window around the player.")]
        [Min(0)] public int HighDetailRadius = 1;

        [Tooltip("Additional low-poly proxy rings beyond the HD window.")]
        [Min(0)] public int ProxyRingDepth = 2;

        [Header("Floating origin")]
        [Tooltip("Horizontal distance from physical origin (metres) that triggers a recenter. Design target: 2000 m.")]
        [Min(1f)] public float ThresholdDistance = 2000f;

        [Tooltip("Optional hysteresis band (metres) added to the threshold to stop oscillation at the boundary. 0 = off.")]
        [Min(0f)] public float HysteresisBand = 0f;

        [Header("Travel")]
        [Tooltip("Altitude clamp (metres). Streaming is 2D; altitude is governed here, not by a chunk axis.")]
        public float TravelAltitude = 1500f;

        /// <summary>Total Chebyshev radius that still streams content (HD + proxy rings).</summary>
        public int OuterRingRadius => HighDetailRadius + ProxyRingDepth;
    }
}
