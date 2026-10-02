import { dotnet } from './_framework/dotnet.js';

const runtime = await dotnet.create();
await runtime.runMain(runtime.getConfig().mainAssemblyName,
    [new URLSearchParams(location.search).get('mode') ?? 'caught']);
document.documentElement.dataset.probe = 'complete';
