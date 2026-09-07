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
