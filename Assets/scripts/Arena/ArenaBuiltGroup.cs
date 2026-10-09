using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>
    /// Marks a group under Source (Boundry, Blocks, Barriers) as one the arena builder made and may freely replace.
    /// Anything under Source WITHOUT this marker is somebody else's - a hand-placed piece, or old art not yet moved
    /// aside - and the builder refuses to touch Source at all while any of that is present, rather than risk
    /// deleting it: the builder never deletes anything it doesn't own (Decisions 4 and 13).
    /// </summary>
    public sealed class ArenaBuiltGroup : MonoBehaviour
    {
    }
}
