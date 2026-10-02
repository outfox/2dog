import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import test, { before } from 'node:test';

const run = promisify(execFile);
const project = fileURLToPath(new URL('../../tests/browser-exceptions/browser-exceptions.csproj', import.meta.url));
const assembly = fileURLToPath(new URL('../../tests/browser-exceptions/bin/Release/net10.0/browser-exceptions.dll', import.meta.url));
const config = fileURLToPath(new URL('../../nuget.config', import.meta.url));
before(async () => {
    await run('dotnet', ['build', project, '-c', 'Release', '--configfile', config, '-v:quiet'], { timeout: 60000 });
});
const probe = (mode) => run('dotnet', [assembly, mode], { timeout: 15000 });

test('forgotten async failures report while the task is retained, without garbage collection', async () => {
    const { stdout, stderr } = await probe('forgotten');
    assert.match(stdout, /PROBE_COMPLETE/);
    assert.equal(stderr.match(/2dog: Fire-and-forget task exception/g)?.length, 1);
    assert.match(stderr, /InvalidOperationException: forgotten-task/);
    assert.match(stderr, /inner-failure/);
    assert.match(stderr, /FailAsync/);
});

test('already-faulted tasks report all failures immediately', async () => {
    const { stdout, stderr } = await probe('forgotten-completed');
    assert.match(stdout, /PROBE_COMPLETE/);
    assert.equal(stderr.match(/2dog: Fire-and-forget task exception/g)?.length, 1);
    assert.match(stderr, /completed-task/);
    assert.match(stderr, /second-failure/);
});

test('task-source failures report even though no exception was thrown and the captured context never pumps', async () => {
    const { stdout, stderr } = await probe('forgotten-source');
    assert.match(stdout, /PROBE_COMPLETE/);
    assert.equal(stderr.match(/2dog: Fire-and-forget task exception/g)?.length, 1);
    assert.match(stderr, /source-task/);
    assert.match(stderr, /inner-failure/);
});

test('forgotten successful and cancelled tasks stay quiet', async () => {
    const { stdout, stderr } = await probe('forgotten-quiet');
    assert.match(stdout, /PROBE_COMPLETE/);
    assert.equal(stderr, '');
});

test('both value-task overloads report failures', async () => {
    const { stdout, stderr } = await probe('forgotten-value-task');
    assert.match(stdout, /PROBE_COMPLETE/);
    assert.equal(stderr.match(/2dog: Fire-and-forget task exception/g)?.length, 2);
    assert.match(stderr, /InvalidOperationException: value-task/);
    assert.match(stderr, /InvalidOperationException: generic-value-task/);
});

test('abandoned async tasks report the complete exception exactly once', async () => {
    const { stdout, stderr } = await probe('unobserved');
    assert.match(stdout, /PROBE_COMPLETE/);
    assert.equal(stderr.match(/2dog: Unobserved task exception/g)?.length, 1);
    assert.match(stderr, /InvalidOperationException: abandoned-task/);
    assert.match(stderr, /inner-failure/);
    assert.match(stderr, /FailAsync/);
});

test('async-void failures report before runtime termination', async () => {
    await assert.rejects(probe('unhandled'), (error) => {
        assert.notEqual(error.code, 0);
        assert.match(error.stderr, /2dog: Unhandled managed exception \(runtime terminating\)/);
        assert.match(error.stderr, /InvalidOperationException: async-void-failure/);
        assert.match(error.stderr, /inner-failure/);
        assert.match(error.stderr, /ThrowAsyncVoid/);
        return true;
    });
});

test('first-chance reporting exposes retained task failures and can be disabled', async () => {
    const { stdout, stderr } = await probe('first-chance');
    assert.match(stdout, /PROBE_COMPLETE/);
    assert.equal(stderr.match(/2dog: First-chance managed exception/g)?.length, 1);
    assert.match(stderr, /InvalidOperationException: retained-task/);
    assert.match(stderr, /inner-failure/);
    assert.match(stderr, /FailAsync/);
    assert.doesNotMatch(stderr, /caught-after-disable/);
});

test('caught and awaited failures stay quiet by default', async () => {
    const { stdout, stderr } = await probe('caught');
    assert.match(stdout, /PROBE_COMPLETE/);
    assert.equal(stderr, '');
});

test('a throwing diagnostic writer cannot recurse or replace the original failure', async () => {
    const { stdout, stderr } = await probe('broken-stderr');
    assert.match(stdout, /PROBE_COMPLETE/);
    assert.equal(stderr, '');
});
