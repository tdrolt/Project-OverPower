namespace Overpower.Vision
{
    /// <summary>
    /// The Player Property that tells every client a player is holding the Scope (so their sight is the narrow long beam).
    /// Written by the owner only, when it changes; no RPC. A missing key reads as not scoped.
    /// </summary>
    public static class ScopeSightProperty
    {
        public const string Key = "vScp";

        /// <summary>Reads a player's property value, never throwing: a missing key or a value of the wrong type is not scoped.</summary>
        public static bool Read(object raw) => raw is bool scoped && scoped;

        /// <summary>Write only on change.</summary>
        public static bool ShouldPublish(bool lastPublished, bool now) => lastPublished != now;
    }
}
