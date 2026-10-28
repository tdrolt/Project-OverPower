#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Overpower.Web
{
    /// <summary>
    /// Text kept in the browser tab's sessionStorage: one value per key, private to the tab, kept across a reload of that tab, gone
    /// when the tab closes. Only the web player has it (BrowserTabStore.jslib); in the Editor and desktop builds Get returns null and
    /// Set/Remove do nothing.
    /// </summary>
    public static class BrowserTabStore
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern string OverPowerTabGet(string key);
        [DllImport("__Internal")] private static extern void OverPowerTabSet(string key, string value);
        [DllImport("__Internal")] private static extern void OverPowerTabRemove(string key);

        public static string Get(string key) => OverPowerTabGet(key);
        public static void Set(string key, string value) => OverPowerTabSet(key, value ?? "");
        public static void Remove(string key) => OverPowerTabRemove(key);
#else
        public static string Get(string key) => null;
        public static void Set(string key, string value) { }
        public static void Remove(string key) { }
#endif
    }
}
