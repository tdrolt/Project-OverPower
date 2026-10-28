// sessionStorage for BrowserTabStore.cs. Storage can be blocked (privacy settings, some embeds): every call then reads as empty.
mergeInto(LibraryManager.library, {
  OverPowerTabGet: function (key) {
    var value = null;
    try { value = window.sessionStorage.getItem(UTF8ToString(key)); } catch (e) {}
    if (value === null) return null;
    var size = lengthBytesUTF8(value) + 1;
    var buffer = _malloc(size);
    stringToUTF8(value, buffer, size);
    return buffer;
  },
  OverPowerTabSet: function (key, value) {
    try { window.sessionStorage.setItem(UTF8ToString(key), UTF8ToString(value)); } catch (e) {}
  },
  OverPowerTabRemove: function (key) {
    try { window.sessionStorage.removeItem(UTF8ToString(key)); } catch (e) {}
  }
});
