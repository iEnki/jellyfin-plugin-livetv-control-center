const {test,before,after}=require('node:test');
const assert=require('node:assert/strict');
const http=require('node:http');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('playwright');
let browser,server,url;
const root='11111111111111111111111111111111',crime='22222222222222222222222222222222',sport='33333333333333333333333333333333';
const a='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',b='bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',c='cccccccccccccccccccccccccccccccc';
function fixture(){return {canRecord:true,jellyfinTargets:true,wholphinTargets:true,access:{Mode:'personal',CanManage:true,IsAdministrator:false},adminUsers:[{Id:'user',Name:'Robert',IsAdministrator:true,HasLiveTvAccess:true},{Id:'other',Name:'Normaler Benutzer',IsAdministrator:false,HasLiveTvAccess:true}],policies:{},groups:[{Id:crime,Name:'Crime',ChannelCount:2,HasCustomImage:false,ArtworkRevision:0},{Id:sport,Name:'Sport',ChannelCount:2,HasCustomImage:false,ArtworkRevision:0}],refs:{[crime]:[a,b],[sport]:[a,c]},prefs:{HiddenGroupIds:[],DefaultGroupId:crime,DefaultView:'guide',Zoom:5,RememberLastView:false,LastGroupId:null,LastView:'guide'},players:[{DeviceId:'living-tv',Name:'Wohnzimmer Fire TV',Client:'Jellyfin Android TV',UsesAndroidTvDiscoveryFallback:true}],nativeScopes:{},nativeFailure:false,remotePlays:[],remoteFailure:false,preferenceFailure:false,requests:[],fail:false,delay:false};}
const channels=[{Id:a,Name:'RTL Crime',ChannelNumber:'127'},{Id:b,Name:'Investigation',ChannelNumber:'7'},{Id:c,Name:'Sport',ChannelNumber:'58'}];
function localizationScript(){return 'window.LiveTvGroupsTranslations='+fs.readFileSync(path.join(__dirname,'../../src/Jellyfin.Plugin.LiveTvGroups/Localization/strings.json'),'utf8')+';\n'+fs.readFileSync(path.join(__dirname,'../../src/Jellyfin.Plugin.LiveTvGroups/Web/localization.js'),'utf8');}
let fixtures=new Map();
const shell=`<!doctype html><html><head><meta charset="utf-8"><title>Jellyfin groups regression</title><link rel="stylesheet" href="/LiveTvGroups/client.css"><style>body{background:#101010;color:#eee;font:16px Arial;margin:0}header{padding:20px}.page{padding:20px}.hide{display:none}</style></head><body><header><a href="#/list?parentId=${root}&serverId=server">Live-TV Control Center</a> <a href="#/livetv">Live TV</a> <a href="#/userpluginsettings.html?pageUrl=/LiveTvGroups/page.html">Plugin Pages</a></header><div class="page homePage hide"><div id="homeTab"><div class="homeSectionsContainer"><div class="verticalSection"><h2>My Media</h2><div class="itemsContainer" id="my-media">
<div class="card" id="home-live"><a href="#/livetv?serverId=server">Live TV</a></div>
<div class="card" id="home-groups"><a href="#/list?parentId=${root}&serverId=server">Groups</a></div>
<div class="card" id="home-recordings"><a href="#/livetv?tab=3&serverId=server">Recordings</a></div>
</div></div></div></div></div><div class="page libraryPage"><div class="native-content">Original content</div></div><script>
window.ApiClient={accessToken:()=>new URLSearchParams(location.search).get('fixture'),getCurrentUserId:()=> 'user',serverId:()=> 'server',deviceId:()=> 'device',getUrl:(p,q)=> '/'+p+(q?'?'+new URLSearchParams(q):''),getScaledImageUrl:()=>'',getJSON:async u=>{const r=await fetch(u,{headers:{Authorization:'MediaBrowser Token="'+new URLSearchParams(location.search).get('fixture')+'"'}});return r.json();},ajax:async()=>[]};
function native(){document.querySelector('.homePage').classList.toggle('hide',!location.hash.startsWith('#/home'));const own=location.hash.includes('parentId=${root}');document.querySelector('.libraryPage').classList.toggle('hide',location.hash.startsWith('#/details')||location.hash.startsWith('#/home'));const target=document.querySelector('.native-content');target.textContent=location.hash.startsWith('#/details')?'Details':own?'Original group folder':'Original Live TV: all 432 channels';if(location.hash.startsWith('#/livetv'))fetch('/LiveTv/Channels');if(location.hash.startsWith('#/userpluginsettings'))fetch('/LiveTvGroups/page.html').then(r=>r.text()).then(html=>target.append(document.createRange().createContextualFragment(html)));document.dispatchEvent(new Event('viewshow'));}window.addEventListener('hashchange',native);native();</script><script src="/LiveTvGroups/client.js"></script></body></html>`;
before(async()=>{
 server=http.createServer(async(req,res)=>{const u=new URL(req.url,'http://local');const key=(req.headers.authorization||'').match(/Token="([^"]+)"/)?.[1];const fixtureId=u.searchParams.get('fixture');const f=fixtures.get(key||fixtureId||u.searchParams.get('api_key'));const send=(value,status=200)=>{res.writeHead(status,{'Content-Type':'application/json'});res.end(status===204?'':JSON.stringify(value));};
 if(u.pathname.endsWith('localization.js')){res.setHeader('Content-Type','application/javascript');res.end(localizationScript());return;}
 if(u.pathname.endsWith('client.js')||u.pathname.endsWith('channel-access.js')||u.pathname.endsWith('client.css')){res.setHeader('Content-Type',u.pathname.endsWith('.js')?'application/javascript':'text/css');res.end((u.pathname.endsWith('client.js')?localizationScript():'')+fs.readFileSync(path.join(__dirname,'../../src/Jellyfin.Plugin.LiveTvGroups/Web',path.basename(u.pathname))));return;}
 if(u.pathname.endsWith('/page.html')){res.setHeader('Content-Type','text/html; charset=utf-8');res.end(fs.readFileSync(path.join(__dirname,'../../src/Jellyfin.Plugin.LiveTvGroups/Web/page.html')));return;}
 if(u.pathname==='/dashboard'){
  const bootstrap='<script>window.ApiClient={getCurrentUserId:()=>"user",accessToken:()=>new URLSearchParams(location.search).get("fixture"),getUrl:p=>"/"+p,getJSON:async p=>{const r=await fetch(p,{headers:{Authorization:\'MediaBrowser Token="\'+ApiClient.accessToken()+\'"\'}});if(!r.ok)throw Error("Unavailable");return r.json();},getPluginConfiguration:async()=>({AppGuideTimeZone:"Europe/Vienna",EnableWebIntegration:true,EnableAppChannel:true,EnablePlaylistSync:false,HideOriginalLiveTvHomeEntry:'+JSON.stringify(f?.hideHome===true)+',EnableJellyfinTvTargets:'+JSON.stringify(f?.jellyfinTargets!==false)+',EnableWholphinTargets:'+JSON.stringify(f?.wholphinTargets!==false)+'}),updatePluginConfiguration:async (pluginId,config)=>{window.dashboardConfiguration=config;return{};}};window.Dashboard={showLoadingMsg:()=>{},hideLoadingMsg:()=>{},alert:message=>{window.dashboardError=message;},processPluginConfigurationUpdateResult:()=>{window.dashboardSaved=true;}};window.addEventListener("load",()=>document.querySelector("#LiveTvGroupsConfigPage").dispatchEvent(new Event("pageshow")));</script>';
  res.setHeader('Content-Type','text/html');res.end(fs.readFileSync(path.join(__dirname,'../../src/Jellyfin.Plugin.LiveTvGroups/Configuration/configPage.html'),'utf8').replace('<html>','<html lang="'+(u.searchParams.get('language')||'de-DE')+'">').replace('</head>',bootstrap+'</head>'));return;
 }
 if(u.pathname==='/'){res.setHeader('Content-Type','text/html; charset=utf-8');res.end(shell.replace('<html>','<html lang="'+(u.searchParams.get('language')||'de-DE')+'">')); return;}
 if(u.pathname==='/LiveTv/Channels'){send({Items:Array.from({length:432},(_,i)=>({Id:i}))});return;}

 if(!f){send({},401);return;}f.requests.push(req.method+' '+u.pathname);let data='';for await(const chunk of req)data+=chunk;const body=data?(req.headers['content-type']||'').startsWith('application/json')?JSON.parse(data):data:null;
 const program=u.pathname.match(/^\/LiveTv\/Programs\/([^/]+)$/);
 if(program){const ch=channels.find(c=>c.Id===program[1]);if(!ch){send({},404);return;}send({Id:ch.Id,Type:'Program',Name:ch.Name+' program',Overview:'Program description',ChannelId:ch.Id,ChannelName:ch.Name,IsSeries:true,StartDate:new Date(Date.now()+3600000).toISOString(),EndDate:new Date(Date.now()+7200000).toISOString(),TimerId:f.timer?.ProgramId===ch.Id?f.timer.Id:null,SeriesTimerId:f.seriesTimer?.ProgramId===ch.Id?f.seriesTimer.Id:null,Status:f.timer?'New':null});return;}
 if(u.pathname==='/LiveTv/Timers/Defaults'){const programId=u.searchParams.get('programId');send({ProgramId:programId,ChannelId:programId,PrePaddingSeconds:60,PostPaddingSeconds:120,RecordAnyChannel:true,RecordAnyTime:false,RecordNewOnly:false,SkipEpisodesInLibrary:false,KeepUpTo:0});return;}
 const timer=u.pathname.match(/^\/LiveTv\/(SeriesTimers|Timers)(?:\/([^/]+))?$/);
 if(timer){if(req.method!=='GET'&&!f.canRecord){send({},403);return;}const name=timer[1]==='SeriesTimers'?'seriesTimer':'timer';if(req.method==='GET'){send(f[name]||{},f[name]?200:404);return;}if(req.method==='DELETE'){f[name]=null;send(null,204);return;}f[name]={...body,Id:timer[2]||name+'-1'};send(null,204);return;}
 if(u.pathname==='/LiveTvGroups/Access'){send(f.access);return;}
 if(u.pathname.startsWith('/LiveTvGroups/Administration/ChannelAccess')){
  if(!f.access.IsAdministrator){send({},403);return;}
  f.channelAccess??={Revision:0,Enabled:false,Rules:[],Assignments:[],Users:f.adminUsers,Channels:channels.map(c=>({ItemId:c.Id,Name:c.Name,Number:c.ChannelNumber,ServiceName:'IPTV',ExternalId:c.Id})),References:[],Recordings:[{Id:'recording',Name:'Old recording'}]};
  if(u.pathname.endsWith('/Preview')){if(f.previewFailure){send({},503);return;}if(f.previewDelay)await new Promise(r=>setTimeout(r,200));const selected=body.PreviewUserId;send({Channels:f.channelAccess.Channels.map(c=>{const rules=body.Enabled?body.Rules.filter(r=>r.Channels.some(x=>x.ItemId===c.ItemId)&&(r.DeniedUserIds.includes(selected)||!r.VisibleToAllUsers&&!r.AllowedUserIds.includes(selected))).map(r=>r.Name):[];return {Id:c.ItemId,Name:c.Name,Number:c.Number,ServiceName:c.ServiceName,Allowed:rules.length===0,Rules:rules,JellyfinAllowed:true};}),Users:f.adminUsers.map(u=>({...u,DeniedCount:0})),ActivePlayback:[{DeviceName:'TV',Name:'RTL Crime',WillStop:body.Enabled&&body.Rules.length>0}]});return;}
  if(req.method==='PUT'){if(f.channelConflict){send({},409);return;}f.savedChannelAccess=body;Object.assign(f.channelAccess,{Revision:f.channelAccess.Revision+1,Enabled:body.Enabled,Rules:body.Rules,Assignments:body.Recordings});}
  send(f.channelAccess);return;
 }

 if(u.pathname==='/LiveTvGroups/Administration'){
  if(!f.access.IsAdministrator){send({},403);return;}
  if(req.method==='PUT'){f.access.Mode=body.Mode;f.access.CanManage=true;f.imported=body.ImportPersonalGroups;send(null,204);}
  else send({Configuration:{Mode:f.access.Mode,Groups:f.groups.map(g=>({...g,VisibleToAllUsers:true,AllowedUserIds:[],DeniedUserIds:[],...f.policies[g.Id]}))},Users:f.adminUsers});return;
 }
 const policy=u.pathname.match(/^\/LiveTvGroups\/Administration\/Groups\/([^/]+)\/Access$/);
 if(policy){if(!f.access.IsAdministrator){send({},403);return;}f.policies[policy[1]]=body;send(null,204);return;}
 if(u.pathname.endsWith('/Entry')){const value={ChannelId:f.channelAccess===false?null:root,Version:'0.3.2.2',DisplayName:f.displayName,HideOriginalLiveTvHomeEntry:f.hideHome===true&&f.channelAccess!==false,JellyfinTvTargetsEnabled:f.jellyfinTargets,WholphinTargetsEnabled:f.wholphinTargets,CanRecord:f.canRecord};if(f.entryDelay)await new Promise(r=>setTimeout(r,f.entryDelay));send(value,f.entryStatus||200);return;}
 if(u.pathname==='/LiveTvGroups/Players'){send(f.players.filter(p=>f.jellyfinTargets&&f.wholphinTargets || f.jellyfinTargets&&/^android tv$|^jellyfin (android tv|for android tv)/i.test(p.Client) || f.wholphinTargets&&/^wholphin(?: \(debug\))?$/i.test(p.Client)));return;}
 if(u.pathname==='/LiveTvGroups/Players/Preference'){
  if(f.preferenceFailure){send('Preference unavailable',500);return;}
  const target=f.players.find(p=>p.DeviceId===body.DeviceId && (f.jellyfinTargets&&f.wholphinTargets || f.jellyfinTargets&&/^android tv$|^jellyfin (android tv|for android tv)/i.test(p.Client) || f.wholphinTargets&&/^wholphin(?: \(debug\))?$/i.test(p.Client)));
  if(body.DeviceId && !target){send('Target unavailable or not allowed',409);return;}
  f.prefs.PreferredTargetDeviceId=body.DeviceId;
  f.prefs.PreferredTargetDeviceName=target?.Name||null;send(null,204);return;
 }
 const native=u.pathname.match(/^\/LiveTvGroups\/NativeGuide\/Devices\/([^/]+)$/);
 if(native){if(f.nativeFailure){send('TV scope unavailable',409);return;}const device=decodeURIComponent(native[1]);if(req.method==='DELETE')delete f.nativeScopes[device];else {f.nativeRequests??=[];f.nativeRequests.push(body);f.nativeScopes[device]=body.AllVisibleGroups ? {AllVisibleGroups:true} : body.GroupId;}send(null,204);return;}
 const remote=u.pathname.match(/^\/LiveTvGroups\/Players\/([^/]+)\/Play\/([^/]+)$/);
 if(remote){f.remotePlays.push({deviceId:decodeURIComponent(remote[1]),channelId:remote[2]});send(f.remoteFailure?'TV unavailable':null,f.remoteFailure?409:204);return;}
 if(u.pathname.endsWith('/Preferences')){if(req.method==='PUT'){f.prefs=body;send(null,204);}else send(f.prefs);return;}
 if(u.pathname.endsWith('/AvailableChannels')){send({Items:channels});return;}
 if(u.pathname==='/LiveTvGroups/Groups'){if(req.method==='POST'){const g={Id:'44444444444444444444444444444444',Name:body.Name,ChannelCount:0,HasCustomImage:false,ArtworkRevision:0};f.groups.push(g);f.refs[g.Id]=[];send(g);}else send(f.groups);return;}
 const artwork=u.pathname.match(new RegExp('^/LiveTvGroups/Groups/([^/]+)/Image$'));
 if(artwork){const g=f.groups.find(group=>group.Id===artwork[1]);if(!g){send({},404);return;}if(req.method==='GET'){res.writeHead(200,{'Content-Type':'image/png'});res.end(fs.readFileSync(path.join(__dirname,'../../assets/Live-TV_Group.png')));return;}if(req.method==='PUT'){g.HasCustomImage=true;g.ArtworkRevision++;f.imageUpload={type:req.headers['content-type'],bytes:Buffer.byteLength(data)};send(g);return;}g.HasCustomImage=false;g.ArtworkRevision++;f.imageReset=true;send(null,204);return;}
 const match=u.pathname.match(/\/Groups\/([^/]+)(?:\/(Channels|Order))?$/);
 if(match){const gid=match[1],g=f.groups.find(g=>g.Id===gid);if(req.method==='DELETE'){f.groups=f.groups.filter(g=>g.Id!==gid);send(null,204);}else if(match[2]==='Channels'){if(req.method==='GET'){send(f.channelSelectionFailure?'Group selection unavailable':{Items:(f.refs[gid]||[]).map(id=>channels.find(c=>c.Id===id)).filter(Boolean)},f.channelSelectionFailure?500:200);}else{f.refs[gid]=body;g.ChannelCount=body.length;send(null,204);}}else if(g){g.Name=body.Name;send(null,204);}else send({},404);return;}
 const ids=[...new Set(u.searchParams.getAll('groupIds').flatMap(g=>f.refs[g]||[]))],scope=ids.map(id=>channels.find(c=>c.Id===id)).filter(Boolean);
 if(u.pathname.endsWith('/Channels')){send({Items:scope});return;}
 if(u.pathname.endsWith('/Guide')){f.lastGuide={start:u.searchParams.get('start'),end:u.searchParams.get('end')};if(f.fail){f.fail=false;send('Guide unavailable',500);return;}if(f.delay&&u.searchParams.getAll('groupIds').includes(sport))await new Promise(r=>setTimeout(r,300));const start=u.searchParams.get('start'),end=u.searchParams.get('end');send({Start:start,End:end,Channels:scope,Programs:scope.filter(c=>c.Id!==b).map((c,i)=>({Id:c.Id,ChannelId:c.Id,Name:c.Name+' program',EpisodeTitle:'Episode',StartDate:new Date(Date.parse(start)-6*3600000).toISOString(),EndDate:new Date(Date.parse(end)+6*3600000).toISOString()})),MissingChannelCount:0,InvalidProgramCount:0});return;}
 send({},404);
 });await new Promise(r=>server.listen(0,'127.0.0.1',r));url='http://127.0.0.1:'+server.address().port;
 browser=await chromium.launch({headless:true,...(process.env.LTVG_BROWSER_CHANNEL?{channel:process.env.LTVG_BROWSER_CHANNEL}:{})});
});
after(async()=>{await browser?.close();server?.close();});
async function open(t,viewport={width:1440,height:1000},at=null,language='de-DE'){const key=Math.random().toString(36).slice(2),f=fixture();fixtures.set(key,f);const context=await browser.newContext({viewport,timezoneId:'Europe/Vienna'});const page=await context.newPage();if(at)await page.clock.install({time:new Date(at)});const errors=[];page.on('pageerror',e=>errors.push(e.message));page.on('console',m=>{if(m.type()==='error'&&!m.text().includes('Failed to load resource'))errors.push(m.text());});t.after(async()=>{if(t.passed===false)console.log(await page.locator('#ltvg-page').innerText().catch(()=>''));await context.close();fixtures.delete(key);assert.deepEqual(errors,[]);});await page.goto(url+'/?fixture='+key+'&language='+language+'#/list?parentId='+root+'&serverId=server');await page.locator('.ltvg-guide-prog').first().waitFor();if(process.env.LTVG_QA_DIR)await page.screenshot({path:path.join(process.env.LTVG_QA_DIR,viewport.width<500?'groups-mobile.png':'groups-desktop.png')});return {page,f};}
test('own guide, details and return preserve group and scroll; original Live TV stays intact',async t=>{const {page,f}=await open(t);assert.equal(await page.locator('.ltvg-guide-row').count(),3);await page.locator('.ltvg-guide-scroll').evaluate(e=>e.scrollLeft=450);await page.locator('.ltvg-guide-caption').first().click();await page.getByRole('dialog').getByRole('button',{name:'Jellyfin-Details öffnen'}).click();await page.waitForURL(/#\/details\?/) ;await page.locator('#ltvg-page').waitFor({state:'detached'});await page.goBack();await page.locator('.ltvg-guide-prog').first().waitFor();assert.match(page.url(),/group=/);assert.equal(await page.locator('.ltvg-guide-scroll').evaluate(e=>e.scrollLeft),450);await page.getByRole('link',{name:'Live TV',exact:true}).click();await page.locator('#ltvg-page').waitFor({state:'detached'});assert.equal(await page.locator('[data-ltvg-host]').count(),0);assert.match(await page.locator('.native-content').innerText(),/all 432/);assert.equal(await page.locator('#ltvg-open-button').count(),0);assert.ok(f.requests.every(r=>!r.includes('GuideFilter')));});
test('program dialog schedules, updates and cancels an individual recording',async t=>{
 const {page,f}=await open(t);
 await page.locator('.ltvg-guide-prog').first().click();
 const dialog=page.getByRole('dialog');
 if(process.env.LTVG_QA_DIR)await page.screenshot({path:path.join(process.env.LTVG_QA_DIR,'recording-dialog.png')});
 await dialog.getByLabel('Früher beginnen (Minuten)').fill('5');
 await dialog.getByLabel('Später beenden (Minuten)').fill('10');
 await dialog.getByRole('button',{name:'Aufnahme planen'}).click();
 await dialog.waitFor({state:'detached'});
 await page.getByRole('status').filter({hasText:'Aufnahme geplant.'}).waitFor();
 assert.equal(f.timer.ProgramId,a);assert.equal(f.timer.ChannelId,a);
 assert.equal(f.timer.PrePaddingSeconds,300);assert.equal(f.timer.PostPaddingSeconds,600);
 assert.ok(f.requests.includes('POST /LiveTv/Timers'));
 await page.locator('.ltvg-guide-prog').first().click();
 await dialog.getByLabel('Früher beginnen (Minuten)').fill('7');
 await dialog.getByRole('button',{name:'Aufnahmeeinstellungen speichern'}).click();
 await dialog.waitFor({state:'detached'});
 await page.getByRole('status').filter({hasText:'Aufnahmeeinstellungen gespeichert.'}).waitFor();
 assert.equal(f.timer.PrePaddingSeconds,420);
 assert.ok(f.requests.includes('POST /LiveTv/Timers/timer-1'));
 await page.locator('.ltvg-guide-prog').first().click();
 page.once('dialog',confirm=>confirm.accept());
 await dialog.getByRole('button',{name:'Aufnahme abbrechen'}).click();
 await dialog.waitFor({state:'detached'});
 await page.getByRole('status').filter({hasText:'Aufnahme abgebrochen.'}).waitFor();
 assert.equal(f.timer,null);
 assert.ok(f.requests.includes('DELETE /LiveTv/Timers/timer-1'));
});

test('series recordings keep the chosen Jellyfin timer settings',async t=>{
 const {page,f}=await open(t);
 await page.locator('.ltvg-guide-prog').first().click();
 const dialog=page.getByRole('dialog');
 await dialog.getByLabel('Aufnahmeart').selectOption('series');
 await dialog.getByLabel('Nur neue Folgen').check();
 await dialog.getByLabel('Jede Sendezeit').check();
 await dialog.getByLabel('Jeder Sender').check();
 await dialog.getByLabel('Höchstens behalten').fill('8');
 await dialog.getByRole('button',{name:'Aufnahme planen'}).click();
 await dialog.waitFor({state:'detached'});
 assert.equal(f.seriesTimer.RecordNewOnly,true);
 assert.equal(f.seriesTimer.RecordAnyTime,true);
 assert.equal(f.seriesTimer.RecordAnyChannel,true);
 assert.equal(f.seriesTimer.KeepUpTo,8);
 assert.ok(f.requests.includes('POST /LiveTv/SeriesTimers'));
 await page.locator('.ltvg-guide-prog').first().click();
 await dialog.getByLabel('Aufnahmeart').waitFor();
 assert.equal(await dialog.getByLabel('Aufnahmeart').inputValue(),'series');
 await dialog.getByLabel('Höchstens behalten').fill('5');
 await dialog.getByRole('button',{name:'Aufnahmeeinstellungen speichern'}).click();
 await dialog.waitFor({state:'detached'});
 assert.equal(f.seriesTimer.KeepUpTo,5);
 assert.ok(f.requests.includes('POST /LiveTv/SeriesTimers/seriesTimer-1'));
});

test('a permission change rejects recording without closing the dialog',async t=>{
 const {page,f}=await open(t);
 await page.locator('.ltvg-guide-prog').first().click();
 const dialog=page.getByRole('dialog');
 f.canRecord=false;
 await dialog.getByRole('button',{name:'Aufnahme planen'}).click();
 await dialog.getByRole('alert').filter({hasText:'Jellyfin-Berechtigung'}).waitFor();
 assert.equal(f.timer,undefined);
 assert.equal(await dialog.count(),1);
});

test('users without recording management see no timer action',async t=>{
 const {page,f}=await open(t);f.canRecord=false;
 await page.reload();await page.locator('.ltvg-guide-prog').first().click();
 const dialog=page.getByRole('dialog');
 await dialog.getByRole('status').filter({hasText:'Jellyfin-Berechtigung'}).waitFor();
 assert.equal(await dialog.getByRole('button',{name:'Aufnahme planen'}).count(),0);
 assert.ok(!f.requests.some(request=>request.startsWith('POST /LiveTv/Timers')));
});

test('English recording controls remain usable on a phone',async t=>{
 const {page}=await open(t,{width:390,height:844},null,'en-US');
 await page.locator('.ltvg-guide-prog').first().click();
 const dialog=page.getByRole('dialog');
 await dialog.getByRole('button',{name:'Schedule recording'}).waitFor();
 const fits=await dialog.evaluate(element=>{const rect=element.getBoundingClientRect();return rect.left>=0&&rect.right<=innerWidth&&rect.top>=0&&rect.bottom<=innerHeight;});
 assert.equal(fits,true);
 await dialog.getByRole('button',{name:'Close'}).click();
 assert.equal(await dialog.count(),0);
});

test('visible groups and zoom persist; union deduplicates channels',async t=>{const {page,f}=await open(t);await page.getByLabel('Gruppe',{exact:true}).selectOption('all');await page.waitForFunction(()=>document.querySelectorAll('.ltvg-guide-row').length===4);await page.getByRole('button',{name:'Einstellungen',exact:true}).click();await page.locator('[data-group="'+sport+'"]').uncheck();await page.locator('[name="zoom"]').selectOption('8');await page.getByRole('button',{name:'Speichern',exact:true}).click();await page.locator('#ltvg-modal').waitFor({state:'detached'});assert.deepEqual(f.prefs.HiddenGroupIds,[sport]);assert.equal(f.prefs.Zoom,8);assert.equal(await page.getByLabel('Gruppe',{exact:true}).locator('option').count(),2);await page.reload();await page.locator('.ltvg-guide-prog').first().waitFor();assert.equal(await page.getByLabel('Zoom',{exact:true}).inputValue(),'8');});
test('sender search hides nonmatches and selected-only filter works',async t=>{const {page}=await open(t);await page.getByLabel('Ansicht',{exact:true}).selectOption('channels');await page.getByRole('button',{name:'Sender auswählen',exact:true}).click();await page.getByRole('button',{name:'Speichern',exact:true}).waitFor({state:'visible'});await page.getByRole('searchbox').fill('Crime');await page.waitForFunction(()=>document.querySelector('.ltvg-picker-count').textContent.includes('1 Treffer'));assert.equal(await page.locator('.ltvg-picker .ltvg-picker-row:visible').count(),1);await page.getByRole('searchbox').fill('');await page.getByLabel('Nur ausgewählte Sender').check();assert.equal(await page.locator('.ltvg-picker .ltvg-picker-row:visible').count(),2);await page.getByRole('button',{name:'Abbrechen'}).click();});
test('latest group wins delayed requests, failure can be retried',async t=>{const {page,f}=await open(t);f.delay=true;await page.getByLabel('Gruppe',{exact:true}).selectOption(sport);await page.getByLabel('Gruppe',{exact:true}).selectOption(crime);await page.waitForTimeout(450);assert.equal(await page.locator('.ltvg-guide-row').count(),3);f.fail=true;await page.getByRole('button',{name:'Aktualisieren',exact:true}).click();await page.locator('.ltvg-error:not([hidden])').waitFor();await page.getByRole('button',{name:'Erneut versuchen'}).click();await page.locator('.ltvg-error').waitFor({state:'hidden'});await page.locator('.ltvg-guide-prog').first().waitFor();});
test('mobile scroll keeps program labels readable; date navigation works',async t=>{const {page}=await open(t,{width:390,height:844});await page.locator('.ltvg-guide-scroll').evaluate(e=>e.scrollLeft=600);await page.waitForTimeout(50);const visible=await page.locator('.ltvg-guide-caption').first().evaluate(e=>{const c=e.getBoundingClientRect(),p=e.closest('.ltvg-guide-scroll').getBoundingClientRect();return c.left>=p.left&&c.left<p.right&&c.width>80;});assert.equal(visible,true);assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.getByRole('button',{name:'Heute Abend',exact:true}).click();await page.waitForFunction(()=>document.querySelector('[data-control="time"]')?.value==='18:00');await page.getByRole('button',{name:'Jetzt',exact:true}).click();await page.locator('.ltvg-guide-prog').first().waitFor();assert.ok(!page.url().includes('start='));});
test('create, select senders, rename, reorder and delete group',async t=>{const {page,f}=await open(t);await page.getByRole('button',{name:'Gruppen verwalten',exact:true}).click();await page.getByRole('button',{name:'Neue Gruppe',exact:true}).click();await page.getByLabel('Name',{exact:true}).fill('Test');await page.getByRole('button',{name:'Speichern',exact:true}).click();await page.getByRole('button',{name:'Sender auswählen',exact:true}).click();await page.locator('.ltvg-picker input[value="'+a+'"]').check();await page.locator('.ltvg-picker input[value="'+c+'"]').check();await page.getByRole('button',{name:'Speichern',exact:true}).click();await page.waitForFunction(()=>document.querySelectorAll('.ltvg-channel-card').length===2);await page.getByRole('button',{name:'Reihenfolge ändern'}).click();await page.getByRole('button',{name:'Sender nach oben'}).nth(1).click();await page.waitForFunction(()=>document.querySelector('.ltvg-channel-card').dataset.id===''+ 'cccccccccccccccccccccccccccccccc');assert.equal(f.refs['44444444444444444444444444444444'][0],c);await page.getByRole('button',{name:'Gruppen verwalten',exact:true}).click();const card=page.locator('.ltvg-group-card').filter({hasText:'Test'});await card.getByRole('button',{name:'Umbenennen'}).click();await page.getByLabel('Name',{exact:true}).fill('Renamed');await page.getByRole('button',{name:'Speichern',exact:true}).click();await page.locator('.ltvg-group-card').filter({hasText:'Renamed'}).getByRole('button',{name:'Löschen',exact:true}).click();await page.locator('#ltvg-modal').getByRole('button',{name:'Löschen',exact:true}).click();await page.waitForFunction(()=>document.querySelectorAll('.ltvg-group-card').length===2);assert.equal(f.groups.length,2);});

test('group artwork can be previewed, uploaded and reset with localized controls',async t=>{
 const {page,f}=await open(t);
 await page.getByRole('button',{name:'Gruppen verwalten',exact:true}).click();
 const card=page.locator('.ltvg-group-card').filter({hasText:'Crime'});
 await card.locator('.ltvg-group-image').waitFor();
 assert.match(await card.locator('.ltvg-group-image').getAttribute('src'),/revision=0/);
 assert.equal(await card.getByRole('button',{name:'Standardbild verwenden'}).isDisabled(),true);
 await card.getByRole('button',{name:'Bild ändern'}).click();
 const input=page.getByLabel('Bild auswählen');
 await input.setInputFiles(path.join(__dirname,'../../assets/Live-TV_Group.png'));
 assert.match(await page.locator('.ltvg-image-preview').getAttribute('src'),/^blob:/);
 await page.getByRole('button',{name:'Hochladen',exact:true}).click();
 await page.locator('#ltvg-modal').waitFor({state:'detached'});
 await page.waitForFunction(()=>document.querySelector('.ltvg-group-card .ltvg-group-image')?.src.includes('revision=1'));
 assert.equal(f.imageUpload.type,'image/png');
 assert.ok(f.imageUpload.bytes>0);
 assert.equal(await card.getByRole('button',{name:'Standardbild verwenden'}).isDisabled(),false);
 await card.getByRole('button',{name:'Standardbild verwenden'}).click();
 await page.waitForFunction(()=>document.querySelector('.ltvg-group-card .ltvg-group-image')?.src.includes('revision=2'));
 assert.equal(f.imageReset,true);
 assert.equal(f.groups[0].HasCustomImage,false);
});
test('calendar navigation crosses DST correctly; programs use current time after future guide',async t=>{
 const {page,f}=await open(t,{width:1440,height:1000},'2026-10-24T16:00:00Z');
 await page.getByRole('button',{name:'Heute Abend',exact:true}).click();
 await page.waitForFunction(()=>document.querySelector('[data-control="time"]')?.value==='18:00');
 const saturday=Date.parse(f.lastGuide.start);
 await page.locator('[data-action="day"]').nth(1).click();
 await page.waitForFunction(()=>document.querySelector('[data-control="date"]')?.value==='2026-10-25');
 assert.equal((Date.parse(f.lastGuide.start)-saturday)/3600000,25);
 await page.getByLabel('Ansicht',{exact:true}).selectOption('programs');
 await page.locator('.ltvg-program-rail').first().waitFor();
 assert.ok(Math.abs(Date.parse(f.lastGuide.start)-Date.parse('2026-10-24T16:00:00Z'))<3600000);
 assert.ok(f.requests.every(r=>!r.includes('/LiveTv/Programs')));
});

test('optional Plugin Pages fragment opens the same independent groups route',async t=>{
 const {page}=await open(t);
 await page.getByRole('link',{name:'Plugin Pages',exact:true}).click();
 await page.waitForURL(/#\/list\?parentId=/);
 await page.locator('.ltvg-guide-prog').first().waitFor();
 assert.match(page.url(),/liveTvGroups=1/);
 assert.equal(await page.locator('[data-ltvg-host]').count(),1);
});

test('remote target persists after reload; sender click sends TV command and keeps EPG open',async t=>{
 const {page,f}=await open(t);
 await page.getByLabel('Abspielen auf',{exact:true}).selectOption('living-tv');
 await page.waitForFunction(()=>!document.querySelector('[data-control="player"]').disabled);
 assert.equal(f.prefs.PreferredTargetDeviceId,'living-tv');
 assert.equal(f.prefs.PreferredTargetDeviceName,'Wohnzimmer Fire TV');
 await page.reload();await page.locator('.ltvg-guide-prog').first().waitFor();
 assert.equal(await page.getByLabel('Abspielen auf',{exact:true}).inputValue(),'living-tv');
 await page.locator('[data-action="play"]').first().click();
 await page.getByRole('status').filter({hasText:'Wiedergabebefehl an Wohnzimmer Fire TV gesendet.'}).waitFor();
 assert.deepEqual(f.remotePlays,[{deviceId:'living-tv',channelId:a}]);
 assert.ok(!page.url().includes('#/details'));
 assert.ok(!f.requests.some(r=>r.includes('/Sessions')));
});

test('offline remembered TV stays visible but cannot play or fall back to phone',async t=>{
 const {page,f}=await open(t);
 await page.getByLabel('Abspielen auf',{exact:true}).selectOption('living-tv');
 await page.waitForFunction(()=>!document.querySelector('[data-control="player"]').disabled);
 f.players=[];await page.getByRole('button',{name:'Geräte aktualisieren',exact:true}).click();
 await page.getByRole('status').filter({hasText:'Zielgerät nicht verfügbar'}).waitFor();
 assert.equal(await page.getByLabel('Abspielen auf',{exact:true}).inputValue(),'living-tv');
 assert.match(await page.getByLabel('Abspielen auf',{exact:true}).locator('option:checked').innerText(),/nicht verfügbar/);
 f.remoteFailure=true;await page.locator('[data-action="play"]').first().click();
 await page.getByRole('status').filter({hasText:'Bitte ein verfügbares TV-Gerät auswählen.'}).waitFor();
 assert.ok(!page.url().includes('#/details'));assert.equal(f.remotePlays.length,0);
 assert.equal(await page.locator('#ltvg-page').count(),1);
 await page.getByLabel('Abspielen auf',{exact:true}).selectOption('');
 await page.waitForFunction(()=>!document.querySelector('[data-control="player"]').disabled);
 assert.equal(f.prefs.PreferredTargetDeviceId,null);
 await page.locator('[data-action="play"]').first().click();await page.waitForURL(/#\/details\?/);
 assert.equal(f.remotePlays.length,0);
});

test('target preference save failure restores previous device',async t=>{
 const {page,f}=await open(t);
 f.preferenceFailure=true;
 await page.getByLabel('Abspielen auf',{exact:true}).selectOption('living-tv');
 await page.locator('.ltvg-error:not([hidden])').waitFor();
 assert.equal(await page.getByLabel('Abspielen auf',{exact:true}).inputValue(),'');
 assert.ok(!f.prefs.PreferredTargetDeviceId);
});

test('mobile TV selector remains usable and fits the viewport',async t=>{
 const {page}=await open(t,{width:390,height:844});
 await page.getByLabel('Abspielen auf',{exact:true}).selectOption('living-tv');
 await page.waitForFunction(()=>!document.querySelector('[data-control="player"]').disabled);
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 assert.equal(await page.title(),'Jellyfin groups regression');
 if(process.env.LTVG_QA_DIR)await page.screenshot({path:path.join(process.env.LTVG_QA_DIR,'remote-mobile.png')});
});

test('ordinary shared-mode user has a working guide and TV controls without editing tools',async t=>{
 const {page,f}=await open(t);f.access={Mode:'shared',CanManage:false,IsAdministrator:false};
 await page.reload();await page.locator('.ltvg-guide-prog').first().waitFor();
 assert.equal(await page.getByRole('button',{name:'Gruppen verwalten',exact:true}).count(),0);
 assert.equal(await page.getByLabel('Ansicht',{exact:true}).locator('option[value="manage"]').count(),0);
 await page.getByLabel('Ansicht',{exact:true}).selectOption('channels');await page.locator('.ltvg-channel-card').first().waitFor();
 assert.equal(await page.getByRole('button',{name:'Sender auswählen',exact:true}).count(),0);
 await page.getByRole('button',{name:'Fernsehprogramm',exact:true}).click();await page.locator('.ltvg-guide-prog').first().waitFor();
 assert.equal(await page.getByLabel('Ansicht',{exact:true}).inputValue(),'guide');
 await page.getByLabel('Abspielen auf',{exact:true}).selectOption('living-tv');
 await page.waitForFunction(()=>!document.querySelector('[data-control="player"]').disabled);
 await page.locator('[data-action="play"]').first().click();
 await page.getByRole('status').filter({hasText:'Wiedergabebefehl an Wohnzimmer Fire TV gesendet.'}).waitFor();
 assert.equal(f.remotePlays.length,1);
 if(process.env.LTVG_QA_DIR)await page.screenshot({path:path.join(process.env.LTVG_QA_DIR,'shared-user-guide.png')});
});
test('admin changes mode and saves per-group user exclusions or selected-user access',async t=>{
 const {page,f}=await open(t,{width:390,height:844});f.access.IsAdministrator=true;await page.reload();await page.locator('.ltvg-guide-prog').first().waitFor();
 await page.getByRole('button',{name:'Verwaltung',exact:true}).click();
 await page.getByLabel('Betriebsart',{exact:true}).selectOption('shared');
 await page.getByLabel('Meine persönlichen Gruppen in zentrale Gruppen kopieren').check();
 await page.getByRole('button',{name:'Speichern',exact:true}).click();await page.locator('#ltvg-modal').waitFor({state:'detached'});assert.equal(f.access.Mode,'shared');assert.equal(f.imported,true);
 await page.getByRole('button',{name:'Gruppen verwalten',exact:true}).click();
 await page.locator('.ltvg-group-card').filter({hasText:'Crime'}).getByRole('button',{name:'Benutzerfreigabe'}).click();
 await page.locator('[data-user="other"]').check();
 if(process.env.LTVG_QA_DIR)await page.screenshot({path:path.join(process.env.LTVG_QA_DIR,'admin-group-access-mobile.png')});
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 await page.getByRole('button',{name:'Speichern',exact:true}).click();await page.locator('#ltvg-modal').waitFor({state:'detached'});
 assert.deepEqual(f.policies[crime],{VisibleToAllUsers:true,AllowedUserIds:[],DeniedUserIds:['other']});
 await page.locator('.ltvg-group-card').filter({hasText:'Crime'}).getByRole('button',{name:'Benutzerfreigabe'}).click();
 await page.getByLabel('Sichtbarkeit',{exact:true}).selectOption('selected');await page.locator('[data-user="other"]').check();
 await page.getByRole('button',{name:'Speichern',exact:true}).click();await page.locator('#ltvg-modal').waitFor({state:'detached'});
 assert.deepEqual(f.policies[crime],{VisibleToAllUsers:false,AllowedUserIds:['other'],DeniedUserIds:[]});
});
test('shared-mode empty group list does not offer a create action to normal users',async t=>{
 const {page,f}=await open(t);f.access={Mode:'shared',CanManage:false,IsAdministrator:false};f.groups=[];
 await page.reload();await page.getByText('Keine sichtbaren Gruppen.',{exact:false}).waitFor();
 assert.equal(await page.getByRole('button',{name:'Neue Gruppe',exact:true}).count(),0);
});

test('dashboard mode selection loads and saves central mode without erasing personal groups',async t=>{
 const {page,f}=await open(t);f.access.IsAdministrator=true;
 await page.goto(page.url().replace('/?','/dashboard?').split('#')[0]);
 await page.waitForFunction(()=>!document.querySelector('#GroupMode').disabled);
 assert.equal(await page.getByLabel('Gruppenverwaltung',{exact:true}).inputValue(),'personal');
 await page.getByLabel('Gruppenverwaltung',{exact:true}).selectOption('shared');
 await page.getByLabel('Meine persönlichen Gruppen in zentrale Gruppen kopieren').check();
 await page.getByRole('button',{name:'Speichern',exact:true}).click();
 await page.waitForFunction(()=>window.dashboardSaved);
 assert.equal(f.access.Mode,'shared');assert.equal(f.imported,true);
 assert.equal(f.groups.length,2);assert.equal(await page.evaluate(()=>window.dashboardError),undefined);
});

test('refresh reloads mode and visible group permissions on an already open page',async t=>{
 const {page,f}=await open(t);
 f.access={Mode:'shared',CanManage:false,IsAdministrator:false};f.groups=f.groups.filter(g=>g.Id===sport);
 await page.getByRole('button',{name:'Aktualisieren',exact:true}).click();
 await page.waitForFunction(()=>document.querySelector('[data-control="group"] option[value="22222222222222222222222222222222"]')===null);
 assert.equal(await page.getByRole('button',{name:'Gruppen verwalten',exact:true}).count(),0);
 assert.equal(await page.getByLabel('Gruppe',{exact:true}).inputValue(),'all');
 assert.equal(await page.getByLabel('Gruppe',{exact:true}).locator('option').count(),2);
});


test('English Jellyfin language localizes the guide, management and remote playback',async t=>{
 const {page,f}=await open(t,{width:1440,height:1000},null,'en-US');
 assert.equal(await page.getByRole('heading',{name:'Live-TV Control Center',exact:true}).count(),1);
 await page.getByLabel('View',{exact:true}).selectOption('channels');
 await page.getByRole('button',{name:'Choose channels',exact:true}).click();
 await page.getByPlaceholder('Search channels…').waitFor();
 await page.getByRole('button',{name:'Cancel',exact:true}).click();
 await page.getByLabel('Play on',{exact:true}).selectOption('living-tv');
 await page.waitForFunction(()=>!document.querySelector('[data-control="player"]').disabled);
 await page.locator('[data-action="play"]').first().click();
 await page.getByRole('status').filter({hasText:'Playback command sent to Wohnzimmer Fire TV.'}).waitFor();
 await page.getByRole('button',{name:'Manage groups',exact:true}).click();
 await page.getByRole('button',{name:'New group',exact:true}).waitFor();
 assert.equal(f.remotePlays.length,1);
});

test('custom display name is escaped and user content is not translated',async t=>{
 const {page,f}=await open(t,{width:1440,height:1000},null,'en-GB');
 f.displayName='Family <TV> & Guide';f.groups[0].Name='Einstellungen';await page.reload();
 await page.getByRole('heading',{name:'Family <TV> & Guide',exact:true}).waitFor();
 assert.equal(await page.locator('h1 tv').count(),0);
 assert.equal(await page.getByLabel('Group',{exact:true}).locator('option').filter({hasText:'Einstellungen'}).count(),1);
});

test('unsupported display languages fall back to English',async t=>{
 const {page}=await open(t,{width:390,height:844},null,'fr-FR');
 await page.getByRole('heading',{name:'Live-TV Control Center',exact:true}).waitFor();
 await page.getByRole('button',{name:'Settings',exact:true}).click();
 await page.getByRole('heading',{name:'Group settings',exact:true}).waitFor();
 await page.getByRole('button',{name:'Cancel',exact:true}).click();
});


test('administrator can save a custom display name and restore the automatic default',async t=>{
 const {page,f}=await open(t,{width:1440,height:1000},null,'en-US');f.access.IsAdministrator=true;
 await page.goto(page.url().replace('/?','/dashboard?').split('#')[0]);
 await page.waitForFunction(()=>!document.querySelector('#GroupMode').disabled);
 await page.getByLabel('Display name',{exact:true}).fill('Family TV');
 await page.getByRole('button',{name:'Save',exact:true}).click();
 await page.waitForFunction(()=>window.dashboardConfiguration?.DisplayName==='Family TV');
 assert.equal(await page.getByLabel('Display name',{exact:true}).getAttribute('placeholder'),'Live-TV Control Center');
 await page.getByLabel('Display name',{exact:true}).fill('');
 await page.getByRole('button',{name:'Save',exact:true}).click();
 await page.waitForFunction(()=>window.dashboardConfiguration?.DisplayName===null);
 assert.equal(await page.evaluate(()=>window.dashboardError),undefined);
});

test('changing Jellyfin display language refreshes the plugin without changing group data',async t=>{
 const {page,f}=await open(t);
 await page.evaluate(()=>document.documentElement.lang='en-US');
 await page.getByRole('heading',{name:'Live-TV Control Center',exact:true}).waitFor();
 await page.getByLabel('View',{exact:true}).selectOption('channels');
 await page.getByRole('button',{name:'Choose channels',exact:true}).waitFor();
 assert.equal(f.groups[0].Name,'Crime');
 await page.evaluate(()=>document.documentElement.lang='de-DE');
 await page.getByRole('heading',{name:'Live-TV Control Center',exact:true}).waitFor();
 await page.getByLabel('Ansicht',{exact:true}).waitFor();
});

async function openHome(t, options={}) {
 const key=Math.random().toString(36).slice(2),f=Object.assign(fixture(),options);fixtures.set(key,f);
 const context=await browser.newContext({viewport:options.viewport||{width:1440,height:900}}),page=await context.newPage();
 const errors=[];page.on('pageerror',e=>errors.push(e.message));page.on('console',m=>{if(m.type()==='error'&&!m.text().includes('Failed to load resource'))errors.push(m.text());});
 t.after(async()=>{await context.close();fixtures.delete(key);assert.deepEqual(errors,[]);});
 await page.goto(url+'/?fixture='+key+'&language='+(options.language||'en-US')+'#/home');
 await page.waitForFunction(()=>document.querySelector('#home-groups a').getAttribute('href').includes('liveTvGroups=1'));
 return {page,f};
}

test('home option defaults off and leaves ordinary Live TV, recordings and groups visible',async t=>{
 const {page}=await openHome(t);assert.equal(await page.title(),'Jellyfin groups regression');
 assert.ok(page.url().endsWith('#/home'));await page.locator('#home-live').waitFor({state:'visible'});
 await page.locator('#home-groups').waitFor({state:'visible'});await page.locator('#home-recordings').waitFor({state:'visible'});
 assert.equal(await page.locator('[data-ltvg-hidden-home-entry]').count(),0);
});

test('enabled home option hides only original tile and grouped entry still opens EPG and TV remote',async t=>{
 const {page,f}=await openHome(t,{hideHome:true,displayName:'Family TV'});
 await page.locator('#home-live').waitFor({state:'hidden'});await page.locator('#home-recordings').waitFor({state:'visible'});
 await page.locator('header a[href="#/livetv"]').waitFor({state:'visible'});
 if(process.env.LTVG_QA_DIR)await page.screenshot({path:path.join(process.env.LTVG_QA_DIR,'home-groups-desktop.png')});
 await page.locator('#home-groups a').click();await page.locator('.ltvg-guide-prog').first().waitFor();
 assert.equal(await page.locator('#ltvg-page h1').innerText(),'Family TV');
 await page.getByLabel('Play on',{exact:true}).selectOption('living-tv');
 await page.waitForFunction(()=>!document.querySelector('[data-control="player"]').disabled);
 await page.locator('[data-action="play"]').first().click();await page.getByRole('status').filter({hasText:'Playback command sent to Wohnzimmer Fire TV.'}).waitFor();
 await page.locator('[data-action="play"]').nth(1).click();await page.waitForFunction(()=>!document.querySelector('[data-action="play"]').disabled);
 assert.equal(f.remotePlays.length,2);assert.notEqual(f.remotePlays[0].channelId,f.remotePlays[1].channelId);
 await page.locator('header a[href="#/livetv"]').click();await page.locator('#ltvg-page').waitFor({state:'detached'});
 assert.match(await page.locator('.native-content').innerText(),/all 432/);
});

test('mobile library buttons use routes regardless of German labels and restore when groups disappear',async t=>{
 const {page}=await openHome(t,{hideHome:true,language:'de-DE',viewport:{width:390,height:844}});
 await page.locator('#home-live').waitFor({state:'hidden'});
 await page.locator('#my-media').evaluate((element,root)=>{element.innerHTML='<a class="homeLibraryButton" id="home-live" href="#/livetv?serverId=server">Fernsehen</a><a class="homeLibraryButton" id="home-groups" href="#/list?parentId='+root+'&serverId=server">Eigener Name</a>';},root);
 await page.locator('#home-live').waitFor({state:'hidden'});await page.locator('#home-groups').waitFor({state:'visible'});
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 if(process.env.LTVG_QA_DIR)await page.screenshot({path:path.join(process.env.LTVG_QA_DIR,'home-groups-mobile.png')});
 await page.locator('#home-groups').evaluate(e=>e.hidden=true);await page.locator('#home-live').waitFor({state:'visible'});
 await page.locator('#home-groups').evaluate(e=>e.hidden=false);await page.locator('#home-live').waitFor({state:'hidden'});
 await page.locator('#home-groups').evaluate(e=>e.remove());await page.locator('#home-live').waitFor({state:'visible'});
});

test('home option survives rerenders and restores cached home cards outside the home page',async t=>{
 const {page}=await openHome(t,{hideHome:true});await page.locator('#home-live').waitFor({state:'hidden'});
 await page.locator('#my-media').evaluate(e=>e.innerHTML=e.innerHTML.replace(' data-ltvg-hidden-home-entry=""',''));
 await page.locator('#home-live').waitFor({state:'hidden'});
 await page.locator('header a[href="#/livetv"]').click();await page.waitForFunction(()=>!document.querySelector('#home-live').hasAttribute('data-ltvg-hidden-home-entry'));
 await page.evaluate(()=>location.hash='#/home');await page.locator('#home-live').waitFor({state:'hidden'});
 await page.locator('#my-media').evaluate(e=>e.querySelector('#home-groups').remove());await page.locator('#home-live').waitFor({state:'visible'});
});
test('Entry access denial and request failures keep original Live TV visible after reload',async t=>{
 const {page,f}=await openHome(t,{hideHome:true});await page.locator('#home-live').waitFor({state:'hidden'});
 for(const options of [{channelAccess:false},{channelAccess:true,entryStatus:403},{entryStatus:500}]) {
  Object.assign(f,options);const response=page.waitForResponse(r=>r.url().endsWith('/LiveTvGroups/Entry'));
  await page.reload();await response;await page.locator('#home-live').waitFor({state:'visible'});
  assert.equal(await page.locator('[data-ltvg-hidden-home-entry]').count(),0);
 }
});

test('switching users restores the home entry and ignores a delayed response from the previous user',async t=>{
 const {page,f}=await openHome(t,{hideHome:true});await page.locator('#home-live').waitFor({state:'hidden'});
 f.entryDelay=250;
 const [oldRequest]=await Promise.all([page.waitForRequest(r=>r.url().endsWith('/LiveTvGroups/Entry')),page.evaluate(()=>document.documentElement.lang='de-DE')]);
 f.entryDelay=0;f.channelAccess=false;
 const deniedResponse=page.waitForResponse(r=>r.url().endsWith('/LiveTvGroups/Entry'));
 await page.evaluate(()=>{window.ApiClient.getCurrentUserId=()=> 'other';document.dispatchEvent(new Event('viewshow'));});
 await deniedResponse;await page.locator('#home-live').waitFor({state:'visible'});
 await (await oldRequest.response()).finished();
 assert.equal(await page.locator('[data-ltvg-hidden-home-entry]').count(),0);
 await page.evaluate(()=>{window.ApiClient.getCurrentUserId=()=> null;document.dispatchEvent(new Event('viewshow'));});
 await page.locator('#home-live').waitFor({state:'visible'});
});

test('administrator can load, enable and disable the localized home option without altering other settings',async t=>{
 const {page,f}=await openHome(t,{language:'en-US'});f.access.IsAdministrator=true;
 await page.goto(page.url().replace('/?','/dashboard?').split('#')[0]);await page.locator('#GroupMode:not([disabled])').waitFor();
 const checkbox=page.getByLabel('Hide the original Live TV entry on the web home screen',{exact:true});assert.equal(await checkbox.isChecked(),false);
 await checkbox.check();await page.getByRole('button',{name:'Save',exact:true}).click();
 await page.waitForFunction(()=>window.dashboardConfiguration?.HideOriginalLiveTvHomeEntry===true);
 assert.equal(await page.evaluate(()=>window.dashboardConfiguration.EnableAppChannel),true);
 f.hideHome=true;await page.reload();await page.locator('#GroupMode:not([disabled])').waitFor();assert.equal(await checkbox.isChecked(),true);
 await checkbox.uncheck();await page.getByRole('button',{name:'Save',exact:true}).click();
 await page.waitForFunction(()=>window.dashboardConfiguration?.HideOriginalLiveTvHomeEntry===false);
 await page.goto(page.url().replace('language=en-US','language=de-DE'));await page.locator('#GroupMode:not([disabled])').waitFor();
 await page.getByLabel('Normalen Live-TV-Eintrag auf der Web-Startseite ausblenden',{exact:true}).waitFor();
 assert.equal(await page.evaluate(()=>window.dashboardError),undefined);
});
test('existing group cards edit the clicked personal group on mobile with saved order and no cross-group changes',async t=>{
 const {page,f}=await open(t,{width:390,height:844});f.refs[sport]=[c,a];
 await page.getByRole('button',{name:'Gruppen verwalten',exact:true}).click();
 const sportCard=page.locator('.ltvg-group-card[data-id="'+sport+'"]');
 await sportCard.getByRole('button',{name:'Sender bearbeiten',exact:true}).click();
 const modal=page.locator('#ltvg-modal');await modal.getByRole('button',{name:'Speichern',exact:true}).waitFor({state:'visible'});
 await page.waitForFunction(()=>!document.querySelector('#ltvg-modal [data-action="save"]').disabled);
 assert.equal(await modal.locator('input[value="'+c+'"]').isChecked(),true);
 assert.equal(await modal.locator('input[value="'+a+'"]').isChecked(),true);
 assert.equal(await modal.locator('input[value="'+b+'"]').isChecked(),false);
 assert.match(await modal.getByRole('heading').innerText(),/Sport/);
 assert.equal(await page.getByLabel('Gruppe',{exact:true}).inputValue(),crime);
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 if(process.env.LTVG_QA_DIR)await page.screenshot({path:path.join(process.env.LTVG_QA_DIR,'edit-group-channels-mobile.png')});
 await modal.locator('input[value="'+b+'"]').check();await modal.getByRole('button',{name:'Speichern',exact:true}).click();await modal.waitFor({state:'detached'});
 assert.deepEqual(f.refs[sport],[c,a,b]);assert.deepEqual(f.refs[crime],[a,b]);
 await sportCard.getByText('3 Sender',{exact:true}).waitFor();
 await sportCard.getByRole('button',{name:'Sender bearbeiten',exact:true}).click();
 await page.waitForFunction(()=>!document.querySelector('#ltvg-modal [data-action="save"]').disabled);
 await modal.locator('input[value="'+a+'"]').uncheck();await modal.getByRole('button',{name:'Speichern',exact:true}).click();await modal.waitFor({state:'detached'});
 assert.deepEqual(f.refs[sport],[c,b]);assert.deepEqual(f.refs[crime],[a,b]);
});

test('central administrator edits existing group cards in English while ordinary shared users have no editing controls',async t=>{
 const {page,f}=await open(t,{width:1440,height:1000},null,'en-US');f.access={Mode:'shared',CanManage:true,IsAdministrator:true};
 await page.reload();await page.locator('.ltvg-guide-prog').first().waitFor();
 await page.getByRole('button',{name:'Manage groups',exact:true}).click();
 const card=page.locator('.ltvg-group-card[data-id="'+crime+'"]');await card.getByRole('button',{name:'Edit channels',exact:true}).click();
 await page.waitForFunction(()=>!document.querySelector('#ltvg-modal [data-action="save"]').disabled);
 await page.locator('#ltvg-modal input[value="'+b+'"]').uncheck();await page.locator('#ltvg-modal').getByRole('button',{name:'Save',exact:true}).click();await page.locator('#ltvg-modal').waitFor({state:'detached'});
 assert.deepEqual(f.refs[crime],[a]);assert.deepEqual(f.refs[sport],[a,c]);
 if(process.env.LTVG_QA_DIR)await page.screenshot({path:path.join(process.env.LTVG_QA_DIR,'edit-group-cards-desktop.png')});
 f.access={Mode:'shared',CanManage:false,IsAdministrator:false};await page.reload();await page.locator('.ltvg-guide-prog').first().waitFor();
 assert.equal(await page.getByRole('button',{name:'Edit channels',exact:true}).count(),0);
 assert.equal(await page.locator('#ltvg-page [data-action="edit-channels"]').count(),0);
});

test('failed existing selection load disables saving and cancel never changes saved group channels',async t=>{
 const {page,f}=await open(t);await page.getByRole('button',{name:'Gruppen verwalten',exact:true}).click();f.channelSelectionFailure=true;
 await page.locator('.ltvg-group-card[data-id="'+sport+'"]').getByRole('button',{name:'Sender bearbeiten',exact:true}).click();
 const modal=page.locator('#ltvg-modal');await modal.getByRole('alert').waitFor();assert.equal(await modal.getByRole('button',{name:'Speichern',exact:true}).isDisabled(),true);
 await modal.getByRole('button',{name:'Abbrechen',exact:true}).click();await modal.waitFor({state:'detached'});assert.deepEqual(f.refs[sport],[a,c]);
 f.channelSelectionFailure=false;await page.locator('.ltvg-group-card[data-id="'+sport+'"]').getByRole('button',{name:'Sender bearbeiten',exact:true}).click();
 await page.waitForFunction(()=>!document.querySelector('#ltvg-modal [data-action="save"]').disabled);
 await modal.locator('input[value="'+a+'"]').uncheck();await modal.getByRole('button',{name:'Abbrechen',exact:true}).click();await modal.waitFor({state:'detached'});
 assert.deepEqual(f.refs[sport],[a,c]);assert.ok(!f.requests.includes('PUT /LiveTvGroups/Groups/'+sport+'/Channels'));
});

test('English channel rules select filtered channels, preview denials, save, rename and remove',async t=>{
 const {page,f}=await open(t,{width:1440,height:1000},null,'en-US');f.access.IsAdministrator=true;await page.reload();await page.getByRole('button',{name:'Channel access',exact:true}).click();
 const dialog=page.locator('#ltvg-acl');await dialog.getByLabel('Enable central channel access',{exact:true}).check();await dialog.getByRole('button',{name:'New rule',exact:true}).click();
 await dialog.getByLabel('Rule name',{exact:true}).fill('Adult');assert.equal(await dialog.getByLabel('Visibility policy',{exact:true}).inputValue(),'selected');
 await dialog.getByLabel('Search channels by name, number or source',{exact:true}).fill('127');await dialog.getByRole('button',{name:'Select filtered channels',exact:true}).click();
 assert.equal(await dialog.locator('[data-channel]:checked').count(),1);await dialog.getByRole('button',{name:'Apply rule to draft',exact:true}).click();await dialog.locator('#acl-save:not(:disabled)').waitFor();
 assert.match(await dialog.locator('#acl-preview').innerText(),/RTL Crime · IPTV · Blocked · Adult/);assert.match(await dialog.locator('#acl-preview').innerText(),/Will stop/);
 if(process.env.LTVG_QA_DIR)await page.screenshot({path:path.join(process.env.LTVG_QA_DIR,'channel-access-english-desktop.png')});
 await dialog.getByRole('button',{name:'Save channel access',exact:true}).click();await dialog.waitFor({state:'detached'});
 assert.equal(f.savedChannelAccess.Enabled,true);assert.equal(f.savedChannelAccess.Rules[0].Name,'Adult');assert.deepEqual(f.savedChannelAccess.Rules[0].Channels.map(c=>c.ItemId),[a]);
 await page.getByRole('button',{name:'Channel access',exact:true}).click();await dialog.getByRole('button',{name:'Edit',exact:true}).click();await dialog.getByLabel('Rule name',{exact:true}).fill('Restricted');await dialog.getByLabel('Visibility policy',{exact:true}).selectOption('except');await dialog.locator('[data-user="other"]').check();await dialog.getByRole('button',{name:'Apply rule to draft',exact:true}).click();await dialog.locator('#acl-save:not(:disabled)').waitFor();await dialog.getByRole('button',{name:'Save channel access',exact:true}).click();await dialog.waitFor({state:'detached'});
 assert.equal(f.savedChannelAccess.Rules[0].Name,'Restricted');assert.deepEqual(f.savedChannelAccess.Rules[0].DeniedUserIds,['other']);
 await page.getByRole('button',{name:'Channel access',exact:true}).click();await dialog.getByRole('button',{name:'Delete',exact:true}).click();assert.equal(await dialog.locator('#acl-save').isDisabled(),true);await dialog.getByRole('button',{name:'Preview changes',exact:true}).click();await dialog.locator('#acl-save:not(:disabled)').waitFor();await dialog.getByRole('button',{name:'Save channel access',exact:true}).click();await dialog.waitFor({state:'detached'});assert.deepEqual(f.savedChannelAccess.Rules,[]);
});

test('German mobile dashboard opens shared rule editor and manually assigns old recordings',async t=>{
 const {page,f}=await open(t,{width:390,height:844});f.access.IsAdministrator=true;await page.goto(page.url().replace('/?','/dashboard?').split('#')[0]);await page.locator('#GroupMode:not([disabled])').waitFor();await page.getByRole('button',{name:'Senderrechte',exact:true}).click();
 const dialog=page.locator('#ltvg-acl');await dialog.getByRole('heading',{name:'Benutzervorschau',exact:true}).waitFor();await dialog.getByLabel('Aufnahme',{exact:true}).selectOption('recording');await dialog.getByLabel('Sender',{exact:true}).selectOption(a);await dialog.getByRole('button',{name:'Aufnahme zuordnen',exact:true}).click();
 await dialog.getByRole('button',{name:'Änderungen prüfen',exact:true}).click();await dialog.locator('#acl-save:not(:disabled)').waitFor();assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 if(process.env.LTVG_QA_DIR)await page.screenshot({path:path.join(process.env.LTVG_QA_DIR,'channel-access-german-mobile.png')});
 await dialog.getByRole('button',{name:'Senderrechte speichern',exact:true}).click();await dialog.waitFor({state:'detached'});assert.equal(f.savedChannelAccess.Recordings[0].Channel.ItemId,a);assert.equal(await page.evaluate(()=>window.dashboardError),undefined);
});

test('channel preview failure and revision conflict prevent unsafe saves; ordinary users have no admin control',async t=>{
 const {page,f}=await open(t,{width:1440,height:1000},null,'en-US');assert.equal(await page.getByRole('button',{name:'Channel access',exact:true}).count(),0);f.access.IsAdministrator=true;f.previewFailure=true;await page.reload();await page.getByRole('button',{name:'Channel access',exact:true}).click();const dialog=page.locator('#ltvg-acl');await dialog.getByRole('alert').filter({hasText:'503'}).waitFor();assert.equal(await dialog.locator('#acl-save').isDisabled(),true);
 f.previewFailure=false;await dialog.getByRole('button',{name:'Preview changes',exact:true}).click();await dialog.locator('#acl-save:not(:disabled)').waitFor();f.channelConflict=true;await dialog.locator('#acl-save').click();await dialog.getByRole('alert').filter({hasText:'Reload before saving'}).waitFor();assert.equal(await dialog.locator('#acl-save').isDisabled(),true);assert.equal(f.savedChannelAccess,undefined);
 f.previewDelay=true;const response=page.waitForResponse(r=>r.url().endsWith('/ChannelAccess/Preview'));await dialog.getByRole('button',{name:'Preview changes',exact:true}).click();await page.evaluate(()=>{ApiClient.getCurrentUserId=()=> 'other';});await response;await dialog.waitFor({state:'detached'});
});

test('missing rule references can be repaired; disabling keeps rules after preview and save',async t=>{
 const {page,f}=await open(t,{width:390,height:844},null,'en-US');f.access.IsAdministrator=true;await page.reload();await page.getByRole('button',{name:'Channel access',exact:true}).click();await page.locator('#ltvg-acl').getByRole('button',{name:'Close',exact:true}).click();
 const old={ItemId:'old-channel',Name:'Retired',Number:'9',ServiceName:'IPTV',ExternalId:'old'};
 f.channelAccess.Enabled=true;f.channelAccess.Rules=[{Id:'rule',Name:'Adult',VisibleToAllUsers:false,AllowedUserIds:[],DeniedUserIds:[],Channels:[old]}];f.channelAccess.References=[{RuleId:'rule',Index:0,Name:'Retired',Status:'missing'}];
 await page.getByRole('button',{name:'Channel access',exact:true}).click();const dialog=page.locator('#ltvg-acl');await dialog.getByRole('button',{name:'Edit',exact:true}).click();await dialog.locator('[data-repair="0"]').selectOption(a);await dialog.getByLabel('Search channels by name, number or source',{exact:true}).fill('IPTV');
 if(process.env.LTVG_QA_DIR){await dialog.locator('.acl-dialog').evaluate(e=>e.scrollTop=0);await page.screenshot({path:path.join(process.env.LTVG_QA_DIR,'channel-rule-editor-mobile.png')});}
 await dialog.getByRole('button',{name:'Apply rule to draft',exact:true}).click();await dialog.locator('#acl-save:not(:disabled)').waitFor();await dialog.getByLabel('Enable central channel access',{exact:true}).uncheck();assert.equal(await dialog.locator('#acl-save').isDisabled(),true);await dialog.getByRole('button',{name:'Preview changes',exact:true}).click();await dialog.locator('#acl-save:not(:disabled)').waitFor();assert.match(await dialog.locator('#acl-preview').innerText(),/3 Allowed · 0 Blocked/);
 await dialog.getByRole('button',{name:'Save channel access',exact:true}).click();await dialog.waitFor({state:'detached'});assert.equal(f.savedChannelAccess.Enabled,false);assert.equal(f.savedChannelAccess.Rules[0].Name,'Adult');assert.deepEqual(f.savedChannelAccess.Rules[0].Channels.map(c=>c.ItemId),[a]);
});

test('native TV group apply disables offline guide changes while preserving web playback',async t=>{
 const {page,f}=await open(t,{width:390,height:844});
 assert.equal(await page.getByRole('button',{name:'Gruppe am TV verwenden',exact:true}).isDisabled(),true);
 await page.getByLabel('Abspielen auf',{exact:true}).selectOption('living-tv');
 await page.waitForFunction(()=>!document.querySelector('[data-control="player"]').disabled);
 await page.getByRole('button',{name:'Gruppe am TV verwenden',exact:true}).click();
 await page.getByRole('status').filter({hasText:'Gruppe am TV aktiviert'}).waitFor();
 assert.equal(f.nativeScopes['living-tv'],crime);assert.equal(f.remotePlays.length,0);
 assert.equal(await page.locator('.ltvg-guide-row').count(),3);
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 if(process.env.LTVG_QA_DIR)await page.screenshot({path:path.join(process.env.LTVG_QA_DIR,'native-guide-mobile.png')});
 await page.getByLabel('Gruppe',{exact:true}).selectOption('all');
 assert.equal(await page.getByRole('button',{name:'Gruppe am TV verwenden',exact:true}).isDisabled(),false);
 f.players=[];await page.getByRole('button',{name:'Geräte aktualisieren',exact:true}).click();
 await page.getByRole('status').filter({hasText:'Zielgerät nicht verfügbar'}).waitFor();
 assert.equal(await page.getByRole('button',{name:'Alle Sender am TV',exact:true}).isDisabled(),true);
 assert.equal(f.nativeScopes['living-tv'],crime);assert.equal(f.remotePlays.length,0);
});

test('failed native scope update is visible and does not change guide or playback',async t=>{
 const {page,f}=await open(t);await page.getByLabel('Abspielen auf',{exact:true}).selectOption('living-tv');
 await page.waitForFunction(()=>!document.querySelector('[data-control="player"]').disabled);
 f.nativeFailure=true;await page.getByRole('button',{name:'Gruppe am TV verwenden',exact:true}).click();
 await page.locator('.ltvg-error:not([hidden])').waitFor();
 assert.equal(f.nativeScopes['living-tv'],undefined);assert.equal(f.remotePlays.length,0);
 assert.equal(await page.locator('.ltvg-guide-row').count(),3);
 assert.equal(await page.getByLabel('Abspielen auf',{exact:true}).isDisabled(),false);
});

for (const clientName of ['Android TV','Jellyfin for Android TV']) test('official TV client '+clientName+' supports target playback and native guide activation',async t=>{
 const {page,f}=await open(t);f.players[0].Client=clientName;
 await page.getByRole('button',{name:'Geräte aktualisieren',exact:true}).click();
 await page.getByLabel('Abspielen auf',{exact:true}).selectOption('living-tv');
 await page.waitForFunction(()=>!document.querySelector('[data-control="player"]').disabled);
 const apply=page.getByRole('button',{name:'Gruppe am TV verwenden',exact:true});
 assert.equal(await apply.isDisabled(),false);await apply.click();
 await page.getByRole('status').filter({hasText:'Gruppe am TV aktiviert'}).waitFor();
 assert.equal(f.nativeScopes['living-tv'],crime);
 await page.locator('[data-action="play"]').first().click();
 await page.getByRole('status').filter({hasText:'Wiedergabebefehl an Wohnzimmer Fire TV gesendet.'}).waitFor();
 assert.equal(f.remotePlays.length,1);
 assert.ok(!(await page.getByLabel('Abspielen auf',{exact:true}).innerText()).includes('nicht verfügbar'));
});


 test('all visible groups can be applied to the TV and offline reset stays disabled',async t=>{
 const {page,f}=await open(t,{width:390,height:844});f.prefs.HiddenGroupIds=[sport];await page.reload();
 await page.getByLabel('Abspielen auf',{exact:true}).selectOption('living-tv');
 await page.waitForFunction(()=>!document.querySelector('[data-control="player"]').disabled);
 await page.getByLabel('Gruppe',{exact:true}).selectOption('all');
 const apply=page.getByRole('button',{name:'Gruppe am TV verwenden',exact:true});assert.equal(await apply.isDisabled(),false);await apply.click();
 await page.getByRole('status').filter({hasText:'Alle sichtbaren Gruppen am TV aktiviert'}).waitFor();
 assert.deepEqual(f.nativeScopes['living-tv'],{AllVisibleGroups:true});
 assert.deepEqual(f.nativeRequests.at(-1),{AllVisibleGroups:true});assert.equal(f.remotePlays.length,0);
 assert.equal(await page.locator('.ltvg-guide-row').count(),3);
 if(process.env.LTVG_QA_DIR)await page.screenshot({path:path.join(process.env.LTVG_QA_DIR,'native-guide-visible-groups-mobile.png')});
 await page.getByLabel('Gruppe',{exact:true}).selectOption(crime);await apply.click();
 await page.getByRole('status').filter({hasText:'Gruppe am TV aktiviert.'}).waitFor();assert.equal(f.nativeScopes['living-tv'],crime);
 await page.getByLabel('Gruppe',{exact:true}).selectOption('all');await apply.click();
 await page.getByRole('status').filter({hasText:'Alle sichtbaren Gruppen am TV aktiviert'}).waitFor();
 f.players=[];await page.getByRole('button',{name:'Geräte aktualisieren',exact:true}).click();
 await page.getByRole('status').filter({hasText:'Zielgerät nicht verfügbar'}).waitFor();assert.equal(await apply.isDisabled(),true);
 assert.equal(await page.getByRole('button',{name:'Alle Sender am TV',exact:true}).isDisabled(),true);
 assert.deepEqual(f.nativeScopes['living-tv'],{AllVisibleGroups:true});
 });

 test('all-visible native guide action is disabled with no visible groups and failures preserve the prior scope',async t=>{
 const {page,f}=await open(t);await page.getByLabel('Abspielen auf',{exact:true}).selectOption('living-tv');await page.waitForFunction(()=>!document.querySelector('[data-control="player"]').disabled);
 await page.getByRole('button',{name:'Gruppe am TV verwenden',exact:true}).click();await page.getByRole('status').filter({hasText:'Gruppe am TV aktiviert.'}).waitFor();
 await page.getByLabel('Gruppe',{exact:true}).selectOption('all');f.nativeFailure=true;
 await page.getByRole('button',{name:'Gruppe am TV verwenden',exact:true}).click();await page.locator('.ltvg-error:not([hidden])').waitFor();assert.equal(f.nativeScopes['living-tv'],crime);
 f.prefs.HiddenGroupIds=[crime,sport];await page.reload();await page.getByRole('button',{name:'Gruppe am TV verwenden',exact:true}).waitFor();
 assert.equal(await page.getByRole('button',{name:'Gruppe am TV verwenden',exact:true}).isDisabled(),true);
 });

 test('Wholphin target can apply and reset a group with an honest guide hint',async t=>{
 const {page,f}=await open(t);f.players[0].Client='Wholphin';
 await page.getByRole('button',{name:'Geräte aktualisieren',exact:true}).click();
 await page.getByLabel('Abspielen auf',{exact:true}).selectOption('living-tv');
 await page.waitForFunction(()=>!document.querySelector('[data-control="player"]').disabled);
 await page.locator('.ltvg-player-hint').getByText(/Wholphin am TV geöffnet lassen/).waitFor();
 const apply=page.getByRole('button',{name:'Gruppe am TV verwenden',exact:true});
 assert.equal(await apply.isDisabled(),false);
 await apply.click();await page.getByRole('status').filter({hasText:'Gruppe am TV aktiviert.'}).waitFor();
 assert.equal(f.nativeScopes['living-tv'],crime);assert.equal(f.remotePlays.length,0);
 await page.getByRole('button',{name:'Alle Sender am TV',exact:true}).click();
 await page.getByRole('status').filter({hasText:'Alle Sender am TV wiederhergestellt'}).waitFor();
 assert.equal(f.nativeScopes['living-tv'],undefined);
 });

 test('English all-visible group activation sends an explicit union and localized confirmation',async t=>{
 const {page,f}=await open(t,{width:1440,height:1000},null,'en-US');await page.getByLabel('Play on',{exact:true}).selectOption('living-tv');await page.waitForFunction(()=>!document.querySelector('[data-control="player"]').disabled);
 await page.getByLabel('Group',{exact:true}).selectOption('all');await page.getByRole('button',{name:'Use group on TV',exact:true}).click();
 await page.getByRole('status').filter({hasText:'All visible groups applied on the TV.'}).waitFor();assert.deepEqual(f.nativeScopes['living-tv'],{AllVisibleGroups:true});
 });


test('administrator can choose TV target apps and cannot disable both',async t=>{
 const {page,f}=await open(t,{width:1440,height:1000},null,'en-US');f.access.IsAdministrator=true;
 await page.goto(page.url().replace('/?','/dashboard?').split('#')[0]);await page.locator('#GroupMode:not([disabled])').waitFor();
 const jellyfin=page.getByLabel('Jellyfin Android TV / Fire TV',{exact:true}),wholphin=page.getByLabel('Wholphin',{exact:true});
 assert.equal(await jellyfin.isChecked(),true);assert.equal(await wholphin.isChecked(),true);
 await jellyfin.uncheck();await wholphin.uncheck();await page.getByRole('button',{name:'Save',exact:true}).click();
 await page.waitForFunction(()=>window.dashboardError==='Enable at least one TV target app.');
 assert.equal(await page.evaluate(()=>window.dashboardConfiguration),undefined);
 await wholphin.check();await page.getByRole('button',{name:'Save',exact:true}).click();
 await page.waitForFunction(()=>window.dashboardConfiguration?.EnableWholphinTargets===true);
 assert.equal(await page.evaluate(()=>window.dashboardConfiguration.EnableJellyfinTvTargets),false);
});

test('single enabled Wholphin TV is selected automatically and official app is hidden',async t=>{
 const {page,f}=await open(t);f.jellyfinTargets=false;f.players.push({DeviceId:'bedroom-tv',Name:'Schlafzimmer Wholphin',Client:'Wholphin',UsesAndroidTvDiscoveryFallback:false});
 await page.reload();await page.waitForFunction(()=>document.querySelector('[data-control="player"]')?.value==='bedroom-tv');
 assert.equal(f.prefs.PreferredTargetDeviceId,'bedroom-tv');
 assert.equal(await page.getByLabel('Abspielen auf',{exact:true}).locator('option').count(),2);
 await page.getByRole('button',{name:'Gruppe am TV verwenden',exact:true}).click();
 await page.getByRole('status').filter({hasText:'Gruppe am TV aktiviert.'}).waitFor();
 assert.equal(f.nativeScopes['bedroom-tv'],crime);
});

test('multiple Wholphin TVs keep selection visible and remember an available device',async t=>{
 const {page,f}=await open(t);f.jellyfinTargets=false;
 f.players=[{DeviceId:'living-wholphin',Name:'Wohnzimmer',Client:'Wholphin'},{DeviceId:'bedroom-wholphin',Name:'Schlafzimmer',Client:'Wholphin'}];
 await page.reload();await page.locator('option[value="bedroom-wholphin"]').waitFor({state:'attached'});
 const selector=page.getByLabel('Abspielen auf',{exact:true});assert.equal(await selector.inputValue(),'');
 assert.equal(await page.getByRole('button',{name:'Gruppe am TV verwenden',exact:true}).isDisabled(),true);
 await selector.selectOption('bedroom-wholphin');await page.waitForFunction(()=>!document.querySelector('[data-control="player"]').disabled);
 assert.equal(f.prefs.PreferredTargetDeviceId,'bedroom-wholphin');
 await page.reload();await page.locator('option[value="living-wholphin"]').waitFor({state:'attached'});assert.equal(await selector.inputValue(),'bedroom-wholphin');
 f.players=f.players.filter(p=>p.DeviceId!=='bedroom-wholphin');await page.getByRole('button',{name:'Geräte aktualisieren',exact:true}).click();
 await page.waitForFunction(()=>document.querySelector('[data-control="player"]')?.value==='living-wholphin');
 assert.equal(f.prefs.PreferredTargetDeviceId,'living-wholphin');
});

test('multiple TVs require choice when remembered device is hidden or unavailable',async t=>{
 const {page,f}=await open(t);f.jellyfinTargets=false;
 f.prefs.PreferredTargetDeviceId='living-tv';f.prefs.PreferredTargetDeviceName='Wohnzimmer Fire TV';
 f.players=[f.players[0],{DeviceId:'wholphin-one',Name:'Schlafzimmer',Client:'Wholphin'},{DeviceId:'wholphin-two',Name:'Wohnzimmer',Client:'Wholphin'}];
 await page.reload();await page.locator('option[value="wholphin-two"]').waitFor({state:'attached'});
 assert.equal(await page.getByLabel('Abspielen auf',{exact:true}).inputValue(),'');
 assert.equal(await page.locator('option[value="living-tv"]').count(),0);
 assert.equal(await page.getByRole('button',{name:'Gruppe am TV verwenden',exact:true}).isDisabled(),true);
 assert.equal(await page.getByRole('button',{name:'Alle Sender am TV',exact:true}).isDisabled(),true);
 await page.locator('[data-action="play"]').first().click();
 await page.getByRole('status').filter({hasText:'Bitte ein verfügbares TV-Gerät auswählen.'}).waitFor();
 assert.equal(f.remotePlays.length,0);assert.equal(f.nativeScopes['living-tv'],undefined);
});

test('official-only settings hide Wholphin while both apps retain the existing selector',async t=>{
 const {page,f}=await open(t);f.players.push({DeviceId:'bedroom-tv',Name:'Schlafzimmer',Client:'Wholphin'});
 await page.getByRole('button',{name:'Geräte aktualisieren',exact:true}).click();
 await page.locator('option[value="bedroom-tv"]').waitFor({state:'attached'});
 assert.equal(await page.getByLabel('Abspielen auf',{exact:true}).inputValue(),'');
 f.wholphinTargets=false;await page.reload();await page.locator('option[value="living-tv"]').waitFor({state:'attached'});
 assert.equal(await page.locator('option[value="bedroom-tv"]').count(),0);
 await page.waitForFunction(()=>document.querySelector('[data-control="player"]')?.value==='living-tv');
});
