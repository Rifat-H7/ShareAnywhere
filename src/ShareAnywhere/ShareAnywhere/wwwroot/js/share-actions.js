const copyAnnouncement = document.createElement("p");
copyAnnouncement.className = "sr-only";
copyAnnouncement.setAttribute("role", "status");
document.body.append(copyAnnouncement);

document.addEventListener("click", async (event) => {
    const button = event.target.closest("button[data-copy-target]");
    const target = button ? document.getElementById(button.dataset.copyTarget) : null;
    if (!target) {
        return;
    }

    const originalLabel = button.dataset.copyLabel ??= button.textContent.trim();
    try {
        await navigator.clipboard.writeText(target.textContent);
        button.textContent = "Copied";
        copyAnnouncement.textContent = "Copied to clipboard.";
    } catch {
        const selection = window.getSelection();
        const range = document.createRange();
        range.selectNodeContents(target);
        selection.removeAllRanges();
        selection.addRange(range);
        button.textContent = "Selected";
        copyAnnouncement.textContent = "Selected for manual copying.";
    }
    clearTimeout(button.copyTimer);
    button.copyTimer = setTimeout(() => { button.textContent = originalLabel; }, 1800);
});

function updateShareQr(details, output, url) {
    details.open = false;
    details.hidden = !window.QRCode;
    output.replaceChildren();
    details.ontoggle = () => {
        if (details.open && !output.hasChildNodes()) {
            try {
                new QRCode(output, { text: url, width: 144, height: 144 });
                const image = output.querySelector("img");
                if (image) {
                    image.alt = "QR code for the share link";
                }
            } catch {
                output.textContent = "QR code unavailable.";
            }
        }
    };
}

function formatShareExpiry(expiresAt) {
    return new Date(expiresAt).toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" });
}
