import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { once } from "node:events";
import { access, mkdir, mkdtemp, readdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { after, before, test } from "node:test";
import { setTimeout as delay } from "node:timers/promises";
import { fileURLToPath } from "node:url";

const app = fileURLToPath(new URL("../ShareAnywhere/", import.meta.url));
const assembly = join(app, "bin", "Debug", "net8.0", "ShareAnywhere.dll");
const temp = resolve(tmpdir());
let contentRoot;
let snippets;
let processExit;
let server;
let origin;
let logs = "";

before(async () => {
    await access(assembly);
    contentRoot = await mkdtemp(join(temp, "shareanywhere-smoke-"));
    snippets = join(contentRoot, "App_Data", "Snippets");
    await mkdir(snippets, { recursive: true });
    await writeFile(join(snippets, "snippet-orphan.dat"), "expired test fixture");
    server = spawn("dotnet", [
        assembly, "--urls", "http://127.0.0.1:0",
        "--contentRoot", contentRoot, "--webroot", join(app, "wwwroot"),
        "--Logging:EventLog:LogLevel:Default", "None"
    ], { windowsHide: true });
    processExit = once(server, "exit");
    await new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error(`Server did not start. ${logs}`)), 8000);
        server.once("error", reject);
        server.once("exit", () => {
            clearTimeout(timer);
            reject(new Error(logs));
        });
        server.stderr.on("data", output => { logs += output; });
        server.stdout.on("data", output => {
            logs += output;
            const address = logs.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/);
            if (address) {
                clearTimeout(timer);
                origin = address[1];
                resolve();
            }
        });
    });
});

after(async () => {
    if (server?.pid && server.exitCode === null) {
        server.kill();
        await processExit;
    }
    if (contentRoot) {
        assert.equal(resolve(contentRoot, ".."), temp);
        await rm(contentRoot, { recursive: true, force: true });
    }
});

async function request(path, init) {
    try {
        return await fetch(new URL(path, origin), { signal: AbortSignal.timeout(6000), ...init });
    } catch (error) {
        if (error.name === "AbortError") {
            throw error;
        }
        throw new Error(`Request failed for ${path}\n${logs.slice(-7000)}`, { cause: error });
    }
}

async function register(bytes, extra = {}) {
    const response = await request("/File/RegisterFile", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            fileName: "smoke.txt",
            contentType: "text/plain",
            fileSize: bytes.length,
            deleteAfterCount: 1,
            ...extra
        })
    });
    assert.equal(response.status, 200, await response.clone().text());
    return response.json();
}

async function pending(share, check = value => value) {
    for (let attempt = 0; attempt < 40; attempt++) {
        const response = await request("/File/PollTransfer", {
            headers: { Authorization: `Bearer ${share.senderToken}` }
        });
        assert.equal(response.status, 200);
        const data = await response.json();
        if (check(data.hasPending)) {
            return data.transferId;
        }
        await delay(25);
    }
    assert.fail("The transfer did not reach the expected state.");
}

function upload(share, transferId, bytes) {
    return request(`/File/RelayUploadStream?transferId=${transferId}`, {
        method: "POST",
        headers: { Authorization: `Bearer ${share.senderToken}` },
        body: bytes
    });
}

async function checkRelay(share, receiver, bytes, transferId) {
    const sender = upload(share, transferId, bytes);
    const download = await receiver;
    assert.equal(download.status, 200);
    assert.deepEqual(Buffer.from(await download.arrayBuffer()), bytes);
    const uploaded = await sender;
    assert.equal(uploaded.status, 200);
    assert.deepEqual(await uploaded.json(), { relayed: true, remainingDownloads: 0 });
}

function formToken(html) {
    const token = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];
    assert.ok(token, "Expected a form token.");
    return token;
}

function cookie(response) {
    const value = response.headers.getSetCookie()[0];
    assert.ok(value, "Expected a form cookie.");
    return value.split(";")[0];
}

async function connectNearby(name, context, deviceId) {
    const url = new URL("/nearbyHub", origin);
    url.protocol = "ws:";
    url.searchParams.set("name", name);
    if (deviceId) {
        url.searchParams.set("deviceId", deviceId);
    }
    const socket = new WebSocket(url);
    context.after(() => socket.close());
    const messages = [];
    socket.addEventListener("message", ({ data }) => {
        for (const frame of data.split("\u001e").filter(Boolean)) {
            messages.push(JSON.parse(frame));
        }
    });
    await once(socket, "open", { signal: AbortSignal.timeout(3000) });
    socket.send('{"protocol":"json","version":1}\u001e');

    return async (target) => {
        for (let attempt = 0; attempt < 80; attempt++) {
            const message = messages.find(item => item.target === target);
            if (message) {
                return message.arguments[0];
            }
            await delay(25);
        }
        assert.fail(`Nearby did not send ${target}.`);
    };
}

test("starts cleanly and serves the bundled browser scripts", async () => {
    assert.deepEqual(await readdir(snippets), []);
    for (const path of ["/", "/js/site.js", "/lib/qrcodejs/qrcode.min.js", "/lib/signalr/signalr.min.js"]) {
        const response = await request(path);
        assert.equal(response.status, 200, path);
        await response.arrayBuffer();
    }
});

test("Nearby discovers visitors on the same network automatically", async (context) => {
    const deviceId = "8423ecab1a70499a8b18efa4b796a059";
    const first = await connectNearby("First device", context, deviceId);
    const firstPeer = await first("PeerRegistered");
    assert.deepEqual(await first("PeerList"), []);

    const retry = await connectNearby("First device after reconnect", context, deviceId);
    const retryPeer = await retry("PeerRegistered");
    assert.notEqual(retryPeer.peerId, firstPeer.peerId);
    assert.deepEqual(await retry("PeerList"), []);

    const second = await connectNearby("First device", context);
    const secondPeer = await second("PeerRegistered");
    const listedPeers = await second("PeerList");
    assert.ok(listedPeers.some(peer => peer.peerId === firstPeer.peerId));
    assert.deepEqual(await first("PeerJoined"), secondPeer);
    assert.deepEqual(await retry("PeerJoined"), secondPeer);
});

test("one-use links reserve a download and deliver the original payload", async () => {
    const bytes = Buffer.from("share-anywhere-smoke".repeat(8192));
    const share = await register(bytes);
    assert.match(share.code, /^[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{8}$/);
    assert.equal(share.downloadLink, `/${share.code}`);
    const receiver = request(`/${share.code.toLowerCase()}`);
    const transferId = await pending(share);
    assert.equal((await request(share.downloadLink)).status, 404);
    await checkRelay(share, receiver, bytes, transferId);
    assert.equal((await request(share.downloadLink)).status, 404);
});

test("a disconnected receiver releases its download reservation", async () => {
    const bytes = Buffer.from("retryable transfer");
    const share = await register(bytes);
    const abort = new AbortController();
    const interrupted = request(share.downloadLink, { signal: abort.signal });
    const interruptedResult = assert.rejects(interrupted, { name: "AbortError" });
    await pending(share);
    abort.abort();
    await interruptedResult;
    await pending(share, hasPending => !hasPending);
    const receiver = request(share.downloadLink);
    await checkRelay(share, receiver, bytes, await pending(share));
});

test("a size mismatch fails and leaves the share available for retry", async () => {
    const bytes = Buffer.from("expected payload");
    const share = await register(bytes);
    const receiver = request(share.downloadLink);
    assert.equal((await upload(share, await pending(share), Buffer.from("short"))).status, 400);
    assert.equal((await receiver).status, 502);
    const retry = request(share.downloadLink);
    await checkRelay(share, retry, bytes, await pending(share));
});

test("empty files can complete successfully", async () => {
    const bytes = Buffer.alloc(0);
    const share = await register(bytes);
    const receiver = request(share.downloadLink);
    await checkRelay(share, receiver, bytes, await pending(share));
});

test("text previews preserve a single view and only one reveal succeeds", async () => {
    const message = "synthetic private text";
    const home = await request("/");
    const created = await request("/File/UploadText", {
        method: "POST",
        redirect: "manual",
        headers: { Cookie: cookie(home) },
        body: new URLSearchParams({
            __RequestVerificationToken: formToken(await home.text()),
            textContent: message,
            deleteAfterCount: "1"
        })
    });
    assert.equal(created.status, 302, await created.clone().text());
    const code = new URL(created.headers.get("location"), origin).searchParams.get("code");
    assert.match(code, /^[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{8}$/);
    assert.equal((await readdir(snippets)).length, 1);

    const preview = await request(`/${code}`);
    const formCookie = cookie(preview);
    const html = await preview.text();
    assert.match(html, /Reveal text/);
    assert.ok(!html.includes(message));
    assert.equal((await request(`/${code}`)).status, 200);
    const reveal = () => request("/File/RevealText", {
        method: "POST",
        headers: { Cookie: formCookie },
        body: new URLSearchParams({ __RequestVerificationToken: formToken(html), code })
    });
    const results = await Promise.all([reveal(), reveal()]);
    assert.deepEqual(results.map(result => result.status).sort(), [200, 404]);
    const revealed = results.find(result => result.ok);
    assert.equal(revealed.headers.get("cache-control"), "no-store");
    assert.match(await revealed.text(), /synthetic private text/);
    assert.deepEqual(await readdir(snippets), []);
});
