'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { metadata, writeMetadata } = require('../../.github/scripts/write-dev-meta.cjs');

test('manual package retains plugin identity, actual version and automatic future updates', () => {
    const catalogPath = path.join(__dirname, '../../manifest.json');
    const before = fs.readFileSync(catalogPath, 'utf8');
    const result = metadata();
    assert.equal(result.guid, JSON.parse(before)[0].guid);
    assert.equal(result.targetAbi, '12.0.0.0');
    const project = fs.readFileSync(path.join(__dirname, '../../src/Jellyfin.Plugin.LiveTvGroups/Jellyfin.Plugin.LiveTvGroups.csproj'), 'utf8');
    const expectedVersion = project.match(/<Version>([^<]+)<\/Version>/)[1];
    assert.equal(result.version, expectedVersion);
    if (project.includes('-beta</InformationalVersion>')) assert.ok(result.changelog.startsWith('[BETA]'));
    else if (project.includes('-dev')) assert.ok(result.changelog.startsWith('[DEV]'));
    else assert.ok(!/^\[(BETA|DEV)\]/.test(result.changelog));
    assert.equal(result.autoUpdate, true);
    assert.equal(result.status, 'Active');
    assert.deepEqual(result.assemblies, ['Jellyfin.Plugin.LiveTvGroups.dll']);
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'ltvg-package-'));
    try {
        writeMetadata(directory);
        assert.deepEqual(JSON.parse(fs.readFileSync(path.join(directory, 'meta.json'), 'utf8')), result);
        assert.equal(fs.readFileSync(catalogPath, 'utf8'), before);
    } finally {
        assert.ok(path.resolve(directory).startsWith(path.resolve(os.tmpdir())+path.sep+'ltvg-package-'));
        fs.rmSync(directory, { recursive: true });
    }
});
