using System.Collections.Generic;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// Sovereign Jump (fast-travel) table from the v2 design §8: maps string IDs to absolute
    /// (full-precision) horizontal coordinates. A jump sets the true position to the target and
    /// recomputes the floating-origin offset so the player lands near physical origin, then lets the
    /// streamer pull in the destination rings.
    /// </summary>
    [CreateAssetMenu(menuName = "World Engine/LocationRegistry_SO", fileName = "LocationRegistry_SO")]
    public class LocationRegistry_SO : ScriptableObject
    {
        [System.Serializable]
        public struct Location
        {
            public string id;
            [Tooltip("Absolute horizontal position (full precision).")]
            public double x;
            public double y;
        }

        public List<Location> locations = new();

        /// <summary>Look up an absolute target by id. Returns false if the id is unknown.</summary>
        public bool TryGet(string id, out Vector2 absolute)
        {
            foreach (var loc in locations)
            {
                if (loc.id == id)
                {
                    absolute = new Vector2((float)loc.x, (float)loc.y);
                    return true;
                }
            }
            absolute = Vector2.zero;
            return false;
        }
    }
}
