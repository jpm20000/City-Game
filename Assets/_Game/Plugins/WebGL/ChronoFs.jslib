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

  // M28b: hands the player a file (a Blob behind a temporary <a download>).
  ChronoDownload: function (namePtr, textPtr) {
    var name = UTF8ToString(namePtr), text = UTF8ToString(textPtr);
    var url = URL.createObjectURL(new Blob([text], { type: 'application/json' }));
    var link = document.createElement('a');
    link.href = url;
    link.download = name;
    link.style.display = 'none';
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    setTimeout(function () { URL.revokeObjectURL(url); }, 2000);
  },

  // M28b: opens the browser's file picker; the chosen file comes back as SendMessage(gameObject, method,
  // "<file name>\n<text>"). Nothing is sent when the player cancels.
  ChronoPickFile: function (objPtr, methodPtr) {
    var target = UTF8ToString(objPtr), method = UTF8ToString(methodPtr);
    var send = typeof SendMessage === 'function' ? SendMessage : Module.SendMessage;
    var input = document.createElement('input');
    input.type = 'file';
    input.accept = '.json,application/json';
    input.style.display = 'none';
    input.onchange = function () {
      var file = input.files && input.files[0];
      if (input.parentNode) input.parentNode.removeChild(input);
      if (!file) return;
      var reader = new FileReader();
      reader.onload = function () { send(target, method, file.name + '\n' + reader.result); };
      reader.readAsText(file);
    };
    document.body.appendChild(input);
    input.click();
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
