'use strict';
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
function metadata() {
    const catalog = JSON.parse(fs.readFileSync(path.join(root, 'manifest.json'), 'utf8'))[0];
    const project = fs.readFileSync(path.join(root, 'src/Jellyfin.Plugin.LiveTvGroups/Jellyfin.Plugin.LiveTvGroups.csproj'), 'utf8');
    const version = project.match(/<Version>([^<]+)<\/Version>/)?.[1];
    if (!/^\d+\.\d+\.\d+\.\d+$/.test(version || '')) throw new Error('Expected a four-part plugin version.');
    return {
        category: catalog.category, changelog: '[DEV] Remote Live TV branch test build.',
        description: catalog.description, guid: catalog.guid, name: catalog.name,
        overview: catalog.overview, owner: catalog.owner, targetAbi: catalog.versions[0].targetAbi,
        version, status: 'Active', autoUpdate: true, assemblies: ['Jellyfin.Plugin.LiveTvGroups.dll']
    };
}
function writeMetadata(directory) {
    const result = metadata();
    fs.writeFileSync(path.join(directory, 'meta.json'), JSON.stringify(result, null, 2)+'\n');
    return result;
}
if (require.main === module) writeMetadata(process.argv[2] || path.join(root, 'out'));
module.exports = { metadata, writeMetadata };
