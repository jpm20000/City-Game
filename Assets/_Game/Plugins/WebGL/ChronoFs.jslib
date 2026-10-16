// M28: browser storage for the WebGL build. Unity keeps persistentDataPath (the saves, PlayerPrefs) in an in-memory
// file system backed by IndexedDB; nothing reaches IndexedDB until FS.syncfs(false) runs, and the page that hosts the
// game may not set config.autoSyncPersistentDataPath. The game flushes after every change itself (WebStorage.cs).
mergeInto(LibraryManager.library, {
  // One sync in flight; a request while it runs queues exactly one more (the files may have changed again).
  ChronoSyncFs: function () {
    if (typeof FS === 'undefined' || !FS.syncfs) return;
    var state = Module.chronoFs || (Module.chronoFs = { busy: false, queued: false });
    if (state.busy) { state.queued = true; return; }
    state.busy = true;
    FS.syncfs(false, function (err) {
      if (err) console.warn('Chronopolis: could not write to browser storage', err);
      state.busy = false;
      if (state.queued) { state.queued = false; _ChronoSyncFs(); }
    });
  },

  // A last flush when the tab is hidden or closed (it cannot be awaited, so it is a safety net only).
  ChronoInstallUnloadSync: function () {
    if (typeof window === 'undefined' || Module.chronoUnloadHooked) return;
    Module.chronoUnloadHooked = true;
    var flush = function () { _ChronoSyncFs(); };
    document.addEventListener('visibilitychange', function () { if (document.visibilityState === 'hidden') flush(); });
    window.addEventListener('pagehide', flush);
  },
});
