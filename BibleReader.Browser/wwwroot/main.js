import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

// The files of the app's config folder, kept in the browser's IndexedDB (see BrowserStorage.cs).
const db = await new Promise((resolve, reject) => {
    const request = indexedDB.open('CrossBible', 1);
    request.onupgradeneeded = () => request.result.createObjectStore('files');
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
});
const store = (mode, action) => new Promise((resolve, reject) => {
    const transaction = db.transaction('files', mode);
    const request = action(transaction.objectStore('files'));
    transaction.oncomplete = () => resolve(request.result);
    transaction.onerror = () => reject(transaction.error);
});
globalThis.bibleStorage = {
    listKeys: async () => JSON.stringify(await store('readonly', s => s.getAllKeys())),
    getFile: async key => {
        const data = await store('readonly', s => s.get(key));
        if (!data) return null;
        return await new Promise(resolve => {
            const reader = new FileReader();
            reader.onload = () => resolve(reader.result.split(',')[1]);
            reader.readAsDataURL(new Blob([data]));
        });
    },
    putFile: (key, data) => store('readwrite', s => s.put(data.slice(), key)),
    deleteFile: key => store('readwrite', s => s.delete(key)),
};

// The percentage of the files of the app that are downloaded (the splash screen shows it, see index.html).
const percent = document.getElementById('loading-percent');
const showProgress = (loaded, total) => { if (percent && total > 0) percent.textContent = `${Math.round(100 * loaded / total)}%`; };

const dotnetRuntime = await dotnet
    .withModuleConfig({ onDownloadResourceProgress: showProgress })
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

if (percent) percent.textContent = 'Starting…';
const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
