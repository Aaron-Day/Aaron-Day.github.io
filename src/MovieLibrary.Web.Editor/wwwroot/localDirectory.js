// The editor's one piece of JavaScript, because the File System Access API has no .NET surface.
// Nothing here throws across the interop boundary: an exception reaching Blazor surfaces #blazor-error-ui,
// which has an assertive role of its own, and one event must not produce two spoken statements. The handle
// lives in IndexedDB because a directory handle is structured-cloneable and localStorage is not, and
// permission is re-confirmed every session, because a stored handle is not a stored grant.

const DB = "movielibrary-editor";
const STORE = "handles";
const KEY = "root";

function openDb() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(DB, 1);
        request.onupgradeneeded = () => request.result.createObjectStore(STORE);
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

function transact(db, mode, work) {
    return new Promise((resolve, reject) => {
        const request = work(db.transaction(STORE, mode).objectStore(STORE));
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

async function remember(handle) {
    const db = await openDb();
    try { await transact(db, "readwrite", store => store.put(handle, KEY)); } finally { db.close(); }
}

async function recall() {
    const db = await openDb();
    try { return await transact(db, "readonly", store => store.get(KEY)); } finally { db.close(); }
}

let root = null;

export function supported() {
    return typeof window !== "undefined" && "showDirectoryPicker" in window;
}

export async function grant() {
    if (!supported()) return null;

    try {
        root = await window.showDirectoryPicker({ mode: "readwrite", id: "movielibrary-root" });
        await remember(root);
        return root.name;
    } catch {
        // AbortError when the picker is dismissed, which is an ordinary outcome rather than a failure.
        root = null;
        return null;
    }
}

export async function reconfirm() {
    if (!supported()) return null;

    try {
        const stored = root ?? await recall();
        if (!stored) return null;

        const options = { mode: "readwrite" };
        let state = await stored.queryPermission(options);
        if (state === "prompt") state = await stored.requestPermission(options);
        if (state !== "granted") return null;

        root = stored;
        return stored.name;
    } catch {
        return null;
    }
}

// Three answers, not two, because "I could not read it" must never look like "it is not there". The first has
// to stop a write; the second is the ordinary state of a repository that has no store yet.
async function peek(name) {
    if (!root) return { read: false, missing: false };

    try {
        const handle = await root.getFileHandle(name);
        return { read: true, missing: false, text: await (await handle.getFile()).text() };
    } catch (error) {
        return { read: false, missing: error?.name === "NotFoundError" };
    }
}

// Best effort and silent: a leftover temporary file is untidy rather than harmful, and failing to clear it
// must not turn one failure into two.
async function discard(name) {
    if (!root) return;

    try { await root.removeEntry(name); } catch { /* it was already not there, or cannot be removed */ }
}

export async function readText(name) {
    const seen = await peek(name);
    return seen.read ? seen.text : null;
}

// Whole-file and atomic from a reader's point of view: the backup first, then a temporary file in the same
// directory, then a rename over the target.
export async function writeText(name, text) {
    if (!root) return false;

    try {
        const seen = await peek(name);

        // A read that failed for any reason OTHER than the file being absent stops the write here. Carrying on
        // would overwrite a file we could not copy, and an overwrite with no backup is not reversible, which is
        // the one thing a save must never be. Absence is different: there is nothing to back up and nothing to
        // lose.
        if (!seen.read && !seen.missing) return false;

        if (seen.read) {
            const backup = await root.getFileHandle(name + ".bak", { create: true });
            const out = await backup.createWritable();
            await out.write(seen.text);
            await out.close();
        }

        const temporary = await root.getFileHandle(name + ".tmp", { create: true });
        const writable = await temporary.createWritable();
        await writable.write(text);
        await writable.close();
        await temporary.move(name);
        return true;
    } catch {
        await discard(name + ".tmp");
        return false;
    }
}

export function download(name, text) {
    const url = URL.createObjectURL(new Blob([text], { type: "application/json" }));
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = name;
    anchor.click();
    URL.revokeObjectURL(url);
}
