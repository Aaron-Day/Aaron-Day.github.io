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
    } catch {
        // AbortError when the picker is dismissed, which is an ordinary outcome rather than a failure.
        root = null;
        return null;
    }

    // Remembering the handle is a convenience for next session; having it now is the point. A failed IndexedDB
    // write must not discard a folder the owner has just chosen and granted.
    try { await remember(root); } catch { /* the grant stands for this session; it is just not kept */ }

    return root.name;
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

// Query-only twin of reconfirm(), for page load. A picker or a prompt fires on a user gesture and never on load,
// and Chromium rejects requestPermission without transient activation, so reconfirm() on load reports "no
// folder" on every fresh load even with a grant standing. This answers the name only when permission already
// is "granted", sets root so a later read has a handle, and never calls requestPermission or the picker.
export async function status() {
    if (!supported()) return null;

    try {
        const stored = root ?? await recall();
        if (!stored) return null;

        if (await stored.queryPermission({ mode: "readwrite" }) !== "granted") return null;

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

// Best effort and silent, like discard: a stream that was never closed or aborted can leave its file locked,
// which would make discarding the temporary file fail and can make the next save fail until the page reloads.
async function abort(stream) {
    try { await stream?.abort(); } catch { /* already closed, or cannot be aborted */ }
}

// Whole-file and atomic from a reader's point of view: the backup first, then a temporary file in the same
// directory, then a rename over the target.
//
// The backup is a copy of the previous CONTENT as text, not of its bytes: Blob.text() decodes as UTF-8, which
// strips a byte-order mark and replaces invalid sequences. For a JSON file this repository wrote itself that is
// harmless, and nothing reads the .bak back programmatically, but it is a property of the backup and not a
// byte-for-byte copy.
export async function writeText(name, text) {
    if (!root) return false;

    let open = null;

    try {
        const seen = await peek(name);

        // A read that failed for any reason OTHER than the file being absent stops the write here. Carrying on
        // would overwrite a file we could not copy, and an overwrite with no backup is not reversible, which is
        // the one thing a save must never be. Absence is different: there is nothing to back up and nothing to
        // lose.
        if (!seen.read && !seen.missing) return false;

        if (seen.read) {
            const backup = await root.getFileHandle(name + ".bak", { create: true });
            open = await backup.createWritable();
            await open.write(seen.text);
            await open.close();
            open = null;
        }

        const temporary = await root.getFileHandle(name + ".tmp", { create: true });
        open = await temporary.createWritable();
        await open.write(text);
        await open.close();
        open = null;
        await temporary.move(name);
        return true;
    } catch {
        await abort(open);
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
