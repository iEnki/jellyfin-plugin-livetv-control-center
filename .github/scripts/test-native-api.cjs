// Test actual server controllers from the exact source used by Jellyfin.Controller 12.0.0.
const {spawnSync}=require('node:child_process');
const fs=require('node:fs');const os=require('node:os');const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const revision='6c073e19ddf604b2369c638716164fdab4c952dc';
function run(command,args,cwd=root,env=process.env){const p=spawnSync(command,args,{cwd,env,stdio:'inherit',shell:false});if(p.error)throw p.error;if(p.status!==0)process.exit(p.status||1);}
let api=process.env.JELLYFIN_NATIVE_API;
if(!api){
 const project=fs.readFileSync(path.join(root,'src/Jellyfin.Plugin.LiveTvGroups/Jellyfin.Plugin.LiveTvGroups.csproj'),'utf8');
 if(!project.includes('Include="Jellyfin.Controller" Version="12.0.0"'))throw Error('Update the pinned native API revision when changing the supported server.');
 const source=fs.mkdtempSync(path.join(os.tmpdir(),'ltvg-native-api-'));
 run('git',['init','--quiet',source]);run('git',['remote','add','origin','https://github.com/jellyfin/jellyfin.git'],source);
 run('git',['fetch','--quiet','--depth=1','origin',revision],source);run('git',['checkout','--quiet','--detach','FETCH_HEAD'],source);
 const output=path.join(source,'api-bin');
 run('dotnet',['build',path.join(source,'Jellyfin.Api/Jellyfin.Api.csproj'),'--configuration','Release','--output',output,'-p:CopyLocalLockFileAssemblies=true','-p:UseSharedCompilation=false','--verbosity','quiet']);
 api=path.join(output,'Jellyfin.Api.dll');
}
api=path.resolve(api);
if(!fs.existsSync(api))throw Error('Native API assembly missing: '+api);
run('dotnet',['test','tests/Jellyfin.Plugin.LiveTvGroups.Tests','--configuration','Release','--filter','FullyQualifiedName~ChannelAccessNativeApiTests'],root,{...process.env,JELLYFIN_NATIVE_API:api});
if(process.env.GITHUB_ENV)fs.appendFileSync(process.env.GITHUB_ENV,'JELLYFIN_NATIVE_API='+api+os.EOL);
