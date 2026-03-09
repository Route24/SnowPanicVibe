using System.Collections.Generic;
using UnityEngine;

/// <summary>Detached雪の登録・解除。屋根上停止強制消去の対象管理。</summary>
public static class DetachedSnowRegistry
{
    static readonly List<MvpSnowChunkMotion> _chunks = new List<MvpSnowChunkMotion>();
    static readonly List<SnowPackFallingPiece> _falling = new List<SnowPackFallingPiece>();

    public static void RegisterChunk(MvpSnowChunkMotion c)
    {
        if (c == null || _chunks.Contains(c)) return;
        _chunks.Add(c);
    }

    public static void UnregisterChunk(MvpSnowChunkMotion c)
    {
        if (c == null) return;
        _chunks.Remove(c);
    }

    public static void RegisterFalling(SnowPackFallingPiece f)
    {
        if (f == null || _falling.Contains(f)) return;
        _falling.Add(f);
    }

    public static void UnregisterFalling(SnowPackFallingPiece f)
    {
        if (f == null) return;
        _falling.Remove(f);
    }

    public static IReadOnlyList<MvpSnowChunkMotion> Chunks => _chunks;
    public static IReadOnlyList<SnowPackFallingPiece> Falling => _falling;
}
