const fileToggle = document.getElementById("fileToggle");
const textToggle = document.getElementById("textToggle");
const fileCard = document.getElementById("fileUploadCard");
const textCard = document.getElementById("textUploadCard");
const fileForm = document.getElementById("fileMetadataForm");
const filePicker = document.getElementById("filePicker");
const dragDropArea = document.getElementById("dragDropArea");
const fileInput = document.getElementById("uploadedFile");
const fileInfo = document.getElementById("fileInfo");
const changeFile = document.getElementById("changeFile");
const fileActions = document.getElementById("fileActions");
const fileMessage = document.getElementById("fileMessage");
const relayInfo = document.getElementById("relayInfo");
const relayStatus = document.getElementById("relayStatus");
const downloadLink = document.getElementById("downloadLink");
const linkControls = document.getElementById("linkControls");
const linkRemaining = document.getElementById("linkRemaining");
const nearbyStatus = document.getElementById("nearbyStatus");
const nearbyHint = document.getElementById("nearbyHint");
const nearbyMessage = document.getElementById("nearbyMessage");
const peerList = document.getElementById("peerList");
const incomingPanel = document.getElementById("incomingPanel");
const incomingTransfers = document.getElementById("incomingTransfers");
const incomingAnnouncement = document.getElementById("incomingAnnouncement");
const deviceSettings = document.getElementById("deviceSettings");
const deviceNameInput = document.getElementById("deviceNameInput");
const deviceNameLabel = document.getElementById("deviceNameLabel");
const reconnectNearby = document.getElementById("reconnectNearby");
const endpoints = fileForm.dataset;
const peers = new Map();
const offers = new Map();
const incomingOffers = new Map();
let nearbyConnection;
let currentShare;
let deviceName = `Device ${Math.floor(1000 + Math.random() * 9000)}`;
let deviceId = Array.from(crypto.getRandomValues(new Uint8Array(16)), byte =>
    byte.toString(16).padStart(2, "0")).join("");

try {
    deviceName = localStorage.getItem("shareAnywhereDeviceName") || deviceName;
    const savedId = localStorage.getItem("shareAnywhereDeviceId");
    if (savedId && /^[a-f0-9]{32}$/i.test(savedId)) {
        deviceId = savedId;
    } else {
        localStorage.setItem("shareAnywhereDeviceId", deviceId);
    }
} catch {
    // Use the page's generated device ID when browser storage is unavailable.
}

deviceNameInput.value = deviceName;
deviceNameLabel.textContent = deviceName;

function setMode(textMode) {
    fileToggle.setAttribute("aria-pressed", String(!textMode));
    textToggle.setAttribute("aria-pressed", String(textMode));
    fileCard.hidden = textMode;
    textCard.hidden = !textMode;
    updateNearbyAvailability();
}

fileToggle.addEventListener("click", () => setMode(false));
textToggle.addEventListener("click", () => setMode(true));
setMode(textCard.dataset.hasError === "true");

dragDropArea.addEventListener("click", () => fileInput.click());
changeFile.addEventListener("click", () => fileInput.click());
filePicker.addEventListener("dragover", (event) => {
    event.preventDefault();
    filePicker.classList.add("dragover");
});
filePicker.addEventListener("dragleave", (event) => {
    if (!(event.relatedTarget instanceof Node) || !filePicker.contains(event.relatedTarget)) {
        filePicker.classList.remove("dragover");
    }
});
filePicker.addEventListener("drop", (event) => {
    event.preventDefault();
    filePicker.classList.remove("dragover");
    const [file] = event.dataTransfer.files;
    if (file) {
        const selection = new DataTransfer();
        selection.items.add(file);
        fileInput.files = selection.files;
        updateFileDisplay();
    }
});
fileInput.addEventListener("change", updateFileDisplay);

for (const type of ["dragover", "drop"]) {
    document.addEventListener(type, (event) => {
        if (event.dataTransfer?.types.includes("Files")) {
            event.preventDefault();
        }
    });
}

function updateFileDisplay() {
    const [file] = fileInput.files;
    const moveFocus = file && document.activeElement === dragDropArea;
    dragDropArea.hidden = Boolean(file);
    document.getElementById("fileLimit").hidden = Boolean(file);
    fileInfo.hidden = !file;
    fileActions.hidden = !file;
    document.getElementById("selectedFileName").textContent = file?.name ?? "";
    document.getElementById("selectedFileSize").textContent = file ? formatFileSize(file.size) : "";
    fileMessage.textContent = "";
    if (moveFocus) {
        changeFile.focus();
    }
    updateNearbyAvailability();
}

function updateNearbyAvailability() {
    const canSend = !fileCard.hidden && fileInput.files.length > 0;
    nearbyHint.hidden = canSend;
    for (const button of peerList.querySelectorAll("button")) {
        button.disabled = !canSend || button.dataset.sending === "true";
    }
}

function formatFileSize(bytes) {
    if (bytes < 1024) {
        return `${bytes} B`;
    }

    const units = ["B", "KB", "MB", "GB", "TB"];
    const index = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
    return `${Number((bytes / (1024 ** index)).toFixed(2))} ${units[index]}`;
}

fileForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    const [file] = fileInput.files;
    const submit = fileForm.querySelector('[type="submit"]');
    submit.disabled = true;
    setMessage(fileMessage, "Creating link…");
    try {
        const limit = Number(document.getElementById("deleteAfterCount").value);
        const share = await registerShare(file, limit, false);
        currentShare = share;
        const url = new URL(share.downloadLink, location.href).href;
        downloadLink.href = url;
        downloadLink.textContent = url;
        relayInfo.hidden = false;
        linkControls.hidden = false;
        document.getElementById("sharedFileName").textContent = file.name;
        document.getElementById("shareCode").textContent = share.code;
        document.getElementById("linkExpiry").textContent = `Expires at ${formatShareExpiry(share.expiresAt)}`;
        setRemainingDownloads(limit);
        setMessage(relayStatus, "Waiting for a download");
        fileMessage.textContent = "";
        updateShareQr(document.getElementById("linkQr"), document.getElementById("relayQrCode"), url);
        void pollShare(share);
    } catch (error) {
        setMessage(fileMessage, error.message, true);
    } finally {
        submit.disabled = false;
    }
});

async function registerShare(file, limit, nearbyOnly) {
    if (!file) {
        throw new Error("Choose a file first.");
    }

    if (file.size > Number(endpoints.maxFileBytes)) {
        throw new Error("Choose a file of 1 GiB or less.");
    }

    if (!Number.isInteger(limit) || limit < 1 || limit > Number(endpoints.maxUses)) {
        throw new Error("Enter a download limit between 1 and 100.");
    }

    const response = await fetch(endpoints.register, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            fileName: file.name,
            fileSize: file.size,
            contentType: file.type || "application/octet-stream",
            deleteAfterCount: limit,
            nearbyOnly
        })
    });
    if (!response.ok) {
        throw new Error(response.status === 429 ? "Share limit reached. Try again later." : "Could not create the share.");
    }

    return { ...await response.json(), file, nearbyOnly, stopped: false };
}

function setMessage(element, message, error = false) {
    element.textContent = message;
    element.classList.toggle("error", error);
}

function setRemainingDownloads(remaining) {
    linkRemaining.textContent = `${remaining} download${remaining === 1 ? "" : "s"} left`;
}

function shareStatus(share, message, error = false) {
    if (share.nearbyOnly) {
        setMessage(nearbyMessage, message, error);
    } else if (share === currentShare) {
        setMessage(relayStatus, message, error);
    }
}

function stopShare(share) {
    share.stopped = true;
    if (share.offerId) {
        offers.delete(share.offerId);
    }

    if (share === currentShare) {
        linkControls.hidden = true;
        linkRemaining.textContent = "Link closed";
        downloadLink.removeAttribute("href");
    }
}

async function pollShare(share) {
    if (share.stopped) {
        return;
    }

    const auth = { Authorization: `Bearer ${share.senderToken}` };
    try {
        const response = await fetch(endpoints.poll, { headers: auth });
        if (share.stopped) {
            return;
        }

        if (response.status === 404) {
            shareStatus(share, `Share for ${share.file.name} expired`);
            stopShare(share);
            return;
        }

        if (!response.ok) {
            throw new Error("Could not check for a receiver.");
        }

        const pending = await response.json();
        if (pending.hasPending) {
            await sendFile(share, pending.transferId, auth);
        }
    } catch {
        shareStatus(share, `Connection interrupted for ${share.file.name}. Retrying…`, true);
    }

    if (!share.stopped) {
        setTimeout(() => void pollShare(share), 1500);
    }
}

async function sendFile(share, transferId, headers) {
    shareStatus(share, `Sending ${share.file.name}…`);
    const url = new URL(endpoints.relay, location.href);
    url.searchParams.set("transferId", transferId);
    const response = await fetch(url, { method: "POST", headers, body: share.file });
    if (!response.ok) {
        shareStatus(share, `Transfer interrupted for ${share.file.name}. The receiver can retry.`, true);
        return;
    }

    const { remainingDownloads } = await response.json();
    if (remainingDownloads === 0) {
        shareStatus(share, `Sent ${share.file.name}`);
        stopShare(share);
    } else {
        if (share === currentShare) {
            setRemainingDownloads(remainingDownloads);
        }
        shareStatus(share, "Download finished. Waiting for the next recipient.");
    }
}

function setConnectionState(text, online = false) {
    nearbyStatus.classList.toggle("online", online);
    nearbyStatus.querySelector("span:last-child").textContent = text;
}

reconnectNearby.addEventListener("click", startNearbyDiscovery);

async function startNearbyDiscovery() {
    if (!window.signalR) {
        setConnectionState("Offline");
        nearbyMessage.textContent = "Nearby discovery is unavailable.";
        return;
    }

    nearbyConnection ??= createNearbyConnection();
    if (nearbyConnection.state !== signalR.HubConnectionState.Disconnected) {
        return;
    }

    reconnectNearby.hidden = true;
    nearbyMessage.textContent = "";
    setConnectionState("Connecting");
    try {
        await nearbyConnection.start();
        await updateDeviceName();
    } catch {
        showDiscoveryRetry();
    }
}

function createNearbyConnection() {
    const hubUrl = new URL(endpoints.hub, location.href);
    hubUrl.searchParams.set("name", deviceName);
    hubUrl.searchParams.set("deviceId", deviceId);
    const connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl.href)
        .withAutomaticReconnect()
        .build();

    connection.on("PeerRegistered", () => setConnectionState("Online", true));
    connection.on("PeerList", (items) => {
        peers.clear();
        items.forEach(peer => peers.set(peer.peerId, peer));
        renderPeers();
    });
    const updatePeer = (peer) => {
        peers.set(peer.peerId, peer);
        renderPeers();
    };
    connection.on("PeerJoined", updatePeer);
    connection.on("PeerUpdated", updatePeer);
    connection.on("PeerLeft", (peerId) => {
        peers.delete(peerId);
        renderPeers();
    });
    connection.on("IncomingTransfer", renderIncomingTransfer);
    connection.on("ReceiveStatus", ({ offerId, state }) => incomingOffers.get(offerId)?.(state));
    connection.on("TransferResponse", ({ offerId, receiverName, accepted, fileName }) => {
        const share = offers.get(offerId);
        if (share && !accepted) {
            stopShare(share);
        }
        setMessage(nearbyMessage, `${receiverName} ${accepted ? "accepted" : "declined"} ${fileName}`);
    });
    connection.onreconnecting(() => {
        setConnectionState("Reconnecting");
        peers.clear();
        renderPeers();
    });
    connection.onreconnected(() => void updateDeviceName());
    connection.onclose(showDiscoveryRetry);
    return connection;
}

function showDiscoveryRetry() {
    setConnectionState("Offline");
    peers.clear();
    renderPeers();
    reconnectNearby.hidden = false;
}

document.getElementById("deviceNameForm").addEventListener("submit", (event) => {
    event.preventDefault();
    void saveCustomDeviceName();
});

deviceSettings.addEventListener("toggle", () => {
    if (deviceSettings.open) {
        deviceNameInput.select();
    }
});

deviceSettings.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && deviceSettings.open) {
        deviceSettings.open = false;
        deviceSettings.querySelector("summary").focus();
    }
});

async function saveCustomDeviceName() {
    deviceName = deviceNameInput.value.trim().slice(0, 40) || deviceName;
    deviceNameInput.value = deviceName;
    deviceNameLabel.textContent = deviceName;
    deviceSettings.open = false;
    deviceSettings.querySelector("summary").focus();
    try {
        localStorage.setItem("shareAnywhereDeviceName", deviceName);
    } catch {
        // The updated name still applies to this page.
    }

    await updateDeviceName();
}

async function updateDeviceName() {
    if (nearbyConnection?.state !== "Connected") {
        return;
    }

    try {
        await nearbyConnection.invoke("UpdateName", deviceName);
    } catch {
        setMessage(nearbyMessage, "Could not update your device name.", true);
    }
}

function renderPeers() {
    peerList.replaceChildren();
    if (peers.size === 0) {
        peerList.append(makeElement("p", "empty", "No nearby devices yet."));
        return;
    }

    for (const peer of peers.values()) {
        const row = makeElement("div", "peer-row");
        const main = makeElement("div", "peer-main");
        const avatar = makeElement("span", "peer-avatar", Array.from(peer.displayName.trim())[0]?.toUpperCase() || "?");
        avatar.setAttribute("aria-hidden", "true");
        const button = makeElement("button", "button secondary", "Send");
        button.type = "button";
        button.setAttribute("aria-label", `Send to ${peer.displayName}`);
        button.addEventListener("click", () => void sendToPeer(peer, button));
        main.append(avatar, makeElement("div", "peer-name", peer.displayName), button);
        row.append(main);
        peerList.append(row);
    }
    updateNearbyAvailability();
}

async function sendToPeer(peer, button) {
    const [file] = fileInput.files;
    button.disabled = true;
    button.dataset.sending = "true";
    try {
        if (nearbyConnection?.state !== "Connected") {
            throw new Error("Nearby discovery is disconnected.");
        }
        const share = await registerShare(file, 1, true);
        share.offerId = await nearbyConnection.invoke("SendTransferOffer", peer.peerId, share.senderToken);
        offers.set(share.offerId, share);
        setMessage(nearbyMessage, `Waiting for ${peer.displayName} to accept ${file.name}`);
        void pollShare(share);
    } catch (error) {
        setMessage(nearbyMessage, error.message, true);
    } finally {
        delete button.dataset.sending;
        updateNearbyAvailability();
    }
}

function renderIncomingTransfer(offer) {
    if (incomingOffers.has(offer.offerId)) {
        return;
    }

    const row = makeElement("div", "incoming-row");
    const copy = makeElement("div", "incoming-copy");
    const status = makeElement("p", "message transfer-status");
    status.setAttribute("role", "status");
    const actions = makeElement("div", "incoming-actions");
    const accept = makeElement("button", "button primary", "Accept");
    const decline = makeElement("button", "button secondary", "Decline");
    const dismiss = makeElement("button", "button secondary", "Dismiss");
    dismiss.hidden = true;
    let downloadUrl;
    let frame;
    let completed = false;

    function removeOffer() {
        frame?.remove();
        row.remove();
        incomingOffers.delete(offer.offerId);
        incomingPanel.hidden = incomingOffers.size === 0;
    }

    function showReceiveState(state) {
        if (state === "receiving") {
            setMessage(status, "Receiving file…");
        } else if (state === "complete") {
            completed = true;
            accept.hidden = true;
            dismiss.hidden = false;
            setMessage(status, "Download sent to your browser.");
        } else if (!completed) {
            accept.textContent = "Retry download";
            accept.disabled = false;
            dismiss.hidden = false;
            setMessage(status, "The download could not finish. You can retry.", true);
        }
    }

    async function respond(accepted) {
        accept.disabled = true;
        decline.disabled = true;
        try {
            if (!downloadUrl) {
                downloadUrl = await nearbyConnection.invoke("SendTransferResponse", offer.offerId, accepted);
            }
            if (accepted && downloadUrl) {
                decline.hidden = true;
                dismiss.hidden = true;
                accept.textContent = "Downloading…";
                setMessage(status, "Waiting for the sender…");
                frame?.remove();
                const nextFrame = document.createElement("iframe");
                nextFrame.hidden = true;
                nextFrame.title = "File download";
                nextFrame.src = downloadUrl;
                // A normal frame load means the server returned a page instead of an attachment.
                nextFrame.addEventListener("load", () => {
                    if (frame === nextFrame && nextFrame.contentDocument?.URL !== "about:blank") {
                        showReceiveState("failed");
                    }
                }, { once: true });
                frame = nextFrame;
                row.append(frame);
            } else if (!accepted) {
                removeOffer();
            } else {
                throw new Error("Missing download URL");
            }
        } catch {
            accept.disabled = false;
            decline.disabled = false;
            setMessage(status, "Could not respond. The offer may have expired.", true);
            dismiss.hidden = false;
        }
    }

    accept.type = decline.type = dismiss.type = "button";
    accept.addEventListener("click", () => void respond(true));
    decline.addEventListener("click", () => void respond(false));
    dismiss.addEventListener("click", removeOffer);
    actions.append(accept, decline, dismiss);
    copy.append(
        makeElement("p", "incoming-file", offer.fileName),
        makeElement("p", "muted", `From ${offer.senderName} · ${formatFileSize(offer.fileSize)}`),
        status
    );
    row.append(copy, actions);
    incomingOffers.set(offer.offerId, showReceiveState);
    incomingTransfers.prepend(row);
    incomingPanel.hidden = false;
    incomingAnnouncement.textContent = `Incoming file from ${offer.senderName}: ${offer.fileName}`;
}

function makeElement(tag, className, text = "") {
    const element = document.createElement(tag);
    element.className = className;
    element.textContent = text;
    return element;
}

void startNearbyDiscovery();
