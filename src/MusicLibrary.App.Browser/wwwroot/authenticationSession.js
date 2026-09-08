const sessionKey = 'music-library.authentication';

export function loadAuthenticationSession() {
    return globalThis.localStorage.getItem(sessionKey);
}

export function saveAuthenticationSession(value) {
    if (value === null) {
        globalThis.localStorage.removeItem(sessionKey);
        return;
    }

    globalThis.localStorage.setItem(sessionKey, value);
}

export function downloadFile(content, contentType, fileName) {
    const url = globalThis.URL.createObjectURL(new Blob([new Uint8Array(content)], { type: contentType }));
    const anchor = globalThis.document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    globalThis.document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    globalThis.setTimeout(() => globalThis.URL.revokeObjectURL(url), 0);
}
