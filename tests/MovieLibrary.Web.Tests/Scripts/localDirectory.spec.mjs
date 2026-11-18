// Runs the REAL localDirectory.js against a fake directory handle. Usage: node localDirectory.spec.mjs <path to localDirectory.js>
//
// No seam is needed: the module sets its private `root` from window.showDirectoryPicker() in grant(), so the fake
// handle goes in through the one door production uses. There is deliberately no indexedDB here, which makes
// remember() throw inside grant(), and grant() must keep the folder anyway.
import { pathToFileURL } from "node:url";
import { resolve } from "node:path";

const target = process.argv[2];
if (!target) { console.error("usage: node localDirectory.spec.mjs <path to localDirectory.js>"); process.exit(2); }

function domError(name) { const e = new Error(name); e.name = name; return e; }

// An in-memory directory. Failures are injected by name. A file with an unclosed, unaborted stream is locked and
// cannot be removed, which is what makes the abort in the script observable.
function fakeDirectory(initial = {}, fail = {}) {
    const files = new Map(Object.entries(initial));
    const locked = new Set();
    const aborted = [];

    const handle = name => ({
        async getFile() {
            if (fail.read === name) throw domError("NotAllowedError");
            return { text: async () => files.get(name) };
        },
        async createWritable() {
            locked.add(name);
            let pending = "";
            return {
                async write(chunk) {
                    if (fail.write === name) throw domError("QuotaExceededError");
                    pending += chunk;
                },
                async close() { files.set(name, pending); locked.delete(name); },
                async abort() { aborted.push(name); locked.delete(name); },
            };
        },
        async move(to) {
            if (fail.move) throw domError("InvalidStateError");
            files.set(to, files.get(name));
            files.delete(name);
        },
    });

    const dir = {
        name: "fake-repo",
        async getFileHandle(name, options) {
            if (fail.lookup === name) throw domError("SecurityError");
            if (!files.has(name)) {
                if (!options?.create) throw domError("NotFoundError");
                files.set(name, "");
            }
            return handle(name);
        },
        async removeEntry(name) {
            if (locked.has(name)) throw domError("InvalidModificationError");
            if (!files.delete(name)) throw domError("NotFoundError");
        },
    };
    return { dir, files, aborted };
}

let module;
async function open(initial, fail) {
    const fake = fakeDirectory(initial, fail);
    globalThis.window = { showDirectoryPicker: async () => fake.dir };
    const granted = await module.grant();
    if (granted !== "fake-repo") throw new Error(`grant() did not keep the handle despite no indexedDB; it answered ${JSON.stringify(granted)}`);
    return fake;
}

function expect(actual, wanted, what) {
    if (JSON.stringify(actual) !== JSON.stringify(wanted))
        throw new Error(`${what}: expected ${JSON.stringify(wanted)} but got ${JSON.stringify(actual)}`);
}

const cases = [
    ["a target that cannot be read (not absent) stops the write, with no .tmp, no .bak and the target untouched", async () => {
        const { files } = await open({ "s.json": "old" }, { read: "s.json" });
        expect(await module.writeText("s.json", "new"), false, "writeText result");
        expect([...files.keys()], ["s.json"], "files");
        expect(files.get("s.json"), "old", "target");
    }],
    ["a target whose lookup fails with something other than NotFoundError also stops the write", async () => {
        const { files } = await open({ "s.json": "old" }, { lookup: "s.json" });
        expect(await module.writeText("s.json", "new"), false, "writeText result");
        expect([...files.keys()], ["s.json"], "files");
        expect(files.get("s.json"), "old", "target");
    }],
    ["an absent target is written, and no .bak is made", async () => {
        const { files } = await open({}, {});
        expect(await module.writeText("s.json", "new"), true, "writeText result");
        expect([...files.keys()], ["s.json"], "files");
        expect(files.get("s.json"), "new", "target");
    }],
    ["a readable target is backed up to .bak with its old content, and holds the new text", async () => {
        const { files } = await open({ "s.json": "old" }, {});
        expect(await module.writeText("s.json", "new"), true, "writeText result");
        expect(files.get("s.json.bak"), "old", ".bak");
        expect(files.get("s.json"), "new", "target");
        expect(files.has("s.json.tmp"), false, ".tmp left behind");
    }],
    ["a failed temporary write leaves the target unchanged, aborts the stream and removes the .tmp", async () => {
        const { files, aborted } = await open({ "s.json": "old" }, { write: "s.json.tmp" });
        expect(await module.writeText("s.json", "new"), false, "writeText result");
        expect(files.get("s.json"), "old", "target");
        expect(files.has("s.json.tmp"), false, ".tmp left behind");
        expect(aborted, ["s.json.tmp"], "aborted streams");
    }],
    ["a failed rename leaves the target unchanged and no .tmp behind", async () => {
        const { files } = await open({ "s.json": "old" }, { move: true });
        expect(await module.writeText("s.json", "new"), false, "writeText result");
        expect(files.get("s.json"), "old", "target");
        expect(files.has("s.json.tmp"), false, ".tmp left behind");
    }],
];

try {
    module = await import(pathToFileURL(resolve(target)).href);
    for (const [name, run] of cases) {
        await run();
        console.log(`ok - ${name}`);
    }
    console.log(`${cases.length} cases passed`);
} catch (error) {
    console.error(`FAILED - ${error.message}`);
    process.exit(1);
}
