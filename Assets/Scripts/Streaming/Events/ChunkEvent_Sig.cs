using System;
using UnityEngine;

namespace LevelStreaming
{
    /// <summary>
    /// ScriptableObject event channel (the <c>*_Sig</c> convention) broadcasting a chunk lifecycle
    /// transition. Used for <c>onChunkLoaded_Sig</c> / <c>onChunkUnloaded_Sig</c> so services such as
    /// audio and VFX can react to streaming without referencing the streamer.
    /// </summary>
    [CreateAssetMenu(menuName = "World Engine/Signals/onChunk_Sig", fileName = "onChunk_Sig")]
    public class ChunkEvent_Sig : ScriptableObject
    {
        private event Action<ChunkCoord> _listeners;

        public void Register(Action<ChunkCoord> listener) => _listeners += listener;
        public void Unregister(Action<ChunkCoord> listener) => _listeners -= listener;

        public void Raise(ChunkCoord coord) => _listeners?.Invoke(coord);
    }
}
