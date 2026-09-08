const sessionKey = 'music-library.authentication';
let playingAudio = null;
let playingAudioUrl = null;

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

export async function playFile(content, contentType) {
    if (playingAudio !== null) {
        playingAudio.pause();
    }
    if (playingAudioUrl !== null) {
        globalThis.URL.revokeObjectURL(playingAudioUrl);
    }

    playingAudioUrl = globalThis.URL.createObjectURL(new Blob([new Uint8Array(content)], { type: contentType }));
    playingAudio = new Audio(playingAudioUrl);
    playingAudio.addEventListener('ended', clearPlayingAudio, { once: true });
    playingAudio.addEventListener('error', clearPlayingAudio, { once: true });
    try {
        await playingAudio.play();
    } catch (error) {
        clearPlayingAudio();
        throw error;
    }
}

export async function listenLive(streamUrl) {
    if (playingAudio !== null) {
        playingAudio.pause();
    }
    if (playingAudioUrl !== null) {
        globalThis.URL.revokeObjectURL(playingAudioUrl);
    }

    playingAudioUrl = null;
    playingAudio = new Audio(streamUrl);
    playingAudio.addEventListener('error', clearPlayingAudio, { once: true });
    try {
        await playingAudio.play();
    } catch (error) {
        clearPlayingAudio();
        throw error;
    }
}

export async function toggleFilePlayback() {
    if (playingAudio === null) {
        return -1;
    }
    if (playingAudio.paused) {
        await playingAudio.play();
        return 1;
    }

    playingAudio.pause();
    return 0;
}

function clearPlayingAudio() {
    if (playingAudioUrl !== null) {
        globalThis.URL.revokeObjectURL(playingAudioUrl);
    }
    playingAudio = null;
    playingAudioUrl = null;
}
