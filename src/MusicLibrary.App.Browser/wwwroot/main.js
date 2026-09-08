import { dotnet } from './_framework/dotnet.js'

const isBrowser = typeof window !== 'undefined';
if (!isBrowser) throw new Error('Expected to be running in a browser');

const avaloniaHost = document.getElementById('out');
avaloniaHost.addEventListener('pointerdown', event => {
    if (event.pointerType !== 'touch') return;

    const input = avaloniaHost.querySelector('.avalonia-input-element');
    if (!input) return;

    input.style.display = 'block';
    input.focus({ preventScroll: true });
}, { capture: true });

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);