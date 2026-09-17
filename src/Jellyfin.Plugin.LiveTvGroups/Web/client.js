/* Live-TV Groups: independent user page for Jellyfin Web 12.0. */
(function () {
    'use strict';
    if (window.__liveTvGroupsLoaded) return;
    window.__liveTvGroupsLoaded = true;
    const PAGE = 'ltvg-page', MODAL = 'ltvg-modal';
    const views = { programs: 'Programme', guide: 'Fernsehprogramm', channels: 'Sender', manage: 'Gruppen verwalten' };
    let entry = null, userKey = '', entryPending = false, scheduled = false, host = null, timer = null;
    let access = { Mode:'personal', CanManage:false, IsAdministrator:false };
    let groups = [], prefs = null, active = false, request = 0, abort = null, lastRoute = '';
    let state = { group: 'all', view: 'guide', start: null, reorder: false };
    let loadedChannels = [], guideData = null, tick = 0, restoreFocus = null;
    const positions = new Map();
    let players = [], playerRequest = 0, playerBusy = false, playerMessage = '', playbackBusy = false;
    let preferenceWrites = Promise.resolve();
    function savePreferences(value = prefs) {
        const snapshot = JSON.parse(JSON.stringify(value)), expected = userKey;
        const write = preferenceWrites.catch(() => {}).then(() => {
            if (expected !== userKey) return;
            return api('PUT','Preferences',snapshot);
        });
        preferenceWrites = write; return write;
    }
    const client = () => window.ApiClient;
    const id = value => String(value || '').replace(/-/g, '').toLowerCase();
    const esc = value => String(value == null ? '' : value).replace(/[&<>"']/g, c => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[c]));
    const time = value => new Date(value).toLocaleTimeString([], { hour:'2-digit', minute:'2-digit' });
    const dayLabel = value => new Date(value).toLocaleDateString([], { weekday:'short', day:'2-digit', month:'2-digit' });
    const page = () => document.getElementById(PAGE);
    const visible = () => groups.filter(g => !prefs.HiddenGroupIds.some(hidden => id(hidden) === id(g.Id)));
    const selected = () => state.group === 'all' ? visible() : groups.filter(g => id(g.Id) === id(state.group));
    const scopeQuery = () => selected().map(g => 'groupIds=' + encodeURIComponent(g.Id)).join('&');
    const button = (action, text, extra = '', cls = '') => '<button type="button" class="ltvg-btn '+cls+'" data-action="'+action+'" '+extra+'>'+text+'</button>';
    const image = (item, height = 160) => item.ImageTags && item.ImageTags.Primary
        ? client().getScaledImageUrl(item.Id, { type:'Primary', maxHeight:height, tag:item.ImageTags.Primary }) : '';
    function logo(item) { const url = image(item); return url ? '<img alt="" loading="lazy" src="'+esc(url)+'">' : '<span class="material-icons" aria-hidden="true">live_tv</span>'; }
    async function api(method, path, body, signal) {
        const response = await fetch(client().getUrl('LiveTvGroups/' + path), {
            method, signal, headers: { Authorization:'MediaBrowser Token="'+client().accessToken()+'"', 'Content-Type':'application/json' },
            body:body === undefined ? undefined : JSON.stringify(body)
        });
        if (!response.ok) {
            const text = await response.text();
            throw new Error(response.status === 401 ? 'Bitte erneut anmelden.' : response.status === 403 ? 'Kein Zugriff auf Live-TV.' : text || 'Anfrage fehlgeschlagen (HTTP '+response.status+').');
        }
        return response.status === 204 ? null : response.json();
    }
    function error(e) {
        if (!active || e.name === 'AbortError') return;
        const target = page()?.querySelector('.ltvg-error');
        if (target) { target.hidden = false; target.querySelector('span').textContent = e.message || String(e); }
    }
    function styles() {
        if (document.getElementById('ltvg-styles')) return;
        const link = document.createElement('link'); link.id = 'ltvg-styles'; link.rel = 'stylesheet';
        link.href = client().getUrl('LiveTvGroups/client.css?v=' + encodeURIComponent(entry?.Version || '0.3.1.2'));
        document.head.appendChild(link);
    }
    function ownRoute() {
        const route = new URL(window.location.hash.slice(1), window.location.origin);
        return route.pathname === '/list' && entry && id(route.searchParams.get('parentId')) === id(entry.ChannelId);
    }
    function routeUrl() { return '#/list?parentId='+encodeURIComponent(entry.ChannelId)+'&serverId='+encodeURIComponent(client().serverId())+'&liveTvGroups=1'; }
    function readRoute() {
        const params = new URL(window.location.hash.slice(1), window.location.origin).searchParams;
        const remembered = prefs.RememberLastView;
        state.group = params.get('group') || (remembered ? prefs.LastGroupId : prefs.DefaultGroupId) || 'all';
        const chosen = params.get('view') || (remembered ? prefs.LastView : prefs.DefaultView);
        state.view = views[chosen] && (chosen !== 'manage' || access.CanManage) ? chosen : 'guide';
        const start = params.get('start'); state.start = start && Number.isFinite(Date.parse(start)) ? Date.parse(start) : null;
        state.reorder = false;
        if (state.group !== 'all' && !visible().some(g => id(g.Id) === id(state.group))) state.group = 'all';
    }
    function writeRoute() {
        if (!ownRoute()) return;
        const params = new URLSearchParams({ parentId:entry.ChannelId, serverId:client().serverId(), liveTvGroups:'1', group:state.group, view:state.view });
        if (state.start !== null) params.set('start', new Date(state.start).toISOString());
        const hash = '#/list?' + params;
        history.replaceState(history.state, '', hash); lastRoute = hash;
    }
    function positionKey() { return state.group+'|'+state.view+'|'+(state.start || 'now'); }
    function capture() {
        if (!page()?.getClientRects().length) return;
        const scroller = page()?.querySelector('.ltvg-guide-scroll');
        positions.set(positionKey(), { x:scroller?.scrollLeft || 0, y:scroller?.scrollTop || 0, pageY:window.scrollY });
        const focused = document.activeElement;
        restoreFocus = page()?.contains(focused) && focused.dataset.focus ? focused.dataset.focus : null;
    }
    function restore() {
        const pos = positions.get(positionKey()); const scroller = page()?.querySelector('.ltvg-guide-scroll');
        if (pos) { if (scroller) { scroller.scrollLeft = pos.x; scroller.scrollTop = pos.y; } window.scrollTo(0,pos.pageY); }
        if (restoreFocus) { const target = Array.from(page()?.querySelectorAll('[data-focus]') || []).find(e => e.dataset.focus === restoreFocus); target?.focus({preventScroll:true}); }
        keepLabelsVisible();
    }
    function stop() {
        active = false; request++; playerRequest++; abort?.abort(); clearInterval(timer); timer = null; closeModal();
        if (host) { host.removeAttribute('data-ltvg-host'); host = null; }
        page()?.remove(); guideData = null; lastRoute = '';
    }
    function playerOptions() {
        const target = prefs?.PreferredTargetDeviceId || '';
        const available = players.some(p => p.DeviceId === target);
        return '<option value="">Dieses Gerät</option>'
            + (target && !available ? '<option value="'+esc(target)+'">'+esc(prefs.PreferredTargetDeviceName || 'TV')+' · nicht verfügbar</option>' : '')
            + players.map(p => '<option value="'+esc(p.DeviceId)+'">'+esc(p.Name)+' ('+esc(p.Client)+')</option>').join('');
    }
    function updatePlayers() {
        const selector = page()?.querySelector('[data-control="player"]');
        if (!selector || !prefs) return;
        selector.innerHTML = playerOptions();
        selector.value = prefs.PreferredTargetDeviceId || '';
        selector.disabled = playerBusy || playbackBusy;
        page().querySelector('.ltvg-player-status').textContent = playerMessage;
    }
    async function refreshPlayers() {
        const seq = ++playerRequest, expected = userKey;
        try {
            const result = await api('GET','Players');
            if (!active || seq !== playerRequest || expected !== userKey) return;
            players = result.filter(p => p.DeviceId !== client().deviceId());
            playerMessage = prefs?.PreferredTargetDeviceId && !players.some(p => p.DeviceId === prefs.PreferredTargetDeviceId)
                ? 'Zielgerät nicht verfügbar. Jellyfin am TV öffnen und Geräte aktualisieren.' : '';
            updatePlayers();
        } catch(e) {
            if (!active || seq !== playerRequest || expected !== userKey) return;
            players = []; playerMessage = 'Geräte konnten nicht geladen werden. '+e.message; updatePlayers();
        }
    }
    async function choosePlayer(selector) {
        if (playerBusy || playbackBusy) { updatePlayers(); return; }
        const expected = userKey, deviceId = selector.value || null;
        const target = players.find(p => p.DeviceId === deviceId);
        playerBusy = true; playerMessage = 'Speichert Zielgerät…'; updatePlayers();
        try {
            await api('PUT','Players/Preference',{ DeviceId:deviceId });
            if (expected !== userKey || !active) return;
            prefs.PreferredTargetDeviceId = deviceId;
            prefs.PreferredTargetDeviceName = target?.Name || null;
            playerMessage = '';
        } catch(e) {
            if (expected === userKey && active) { playerMessage = 'Zielgerät konnte nicht gespeichert werden.'; error(e); }
        } finally {
            if (expected === userKey) { playerBusy = false; updatePlayers(); }
        }
    }
    async function playRemote(channelId) {
        if (playerBusy || playbackBusy) return;
        const expected = userKey, deviceId = prefs.PreferredTargetDeviceId;
        const targetName = prefs.PreferredTargetDeviceName || 'TV';
        playbackBusy = true; playerMessage = 'Sendet Wiedergabebefehl…'; updatePlayers();
        page()?.querySelector('.ltvg-error')?.setAttribute('hidden','');
        try {
            // Resolve DeviceId again on the server for every click; never cache or persist SessionId.
            await api('POST','Players/'+encodeURIComponent(deviceId)+'/Play/'+encodeURIComponent(channelId));
            if (expected === userKey && active) playerMessage = 'Wiedergabebefehl an '+targetName+' gesendet.';
        } catch(e) {
            if (expected === userKey && active) { playerMessage = 'Wiedergabebefehl fehlgeschlagen.'; error(e); }
        } finally {
            if (expected === userKey) { playbackBusy = false; updatePlayers(); }
        }
    }
    function chrome() {
        const root = page(); if (!root || !prefs) return;
        root.innerHTML = '<div class="ltvg-header"><h1>Live-TV Gruppen</h1><div class="ltvg-actions">'
            +(access.IsAdministrator ? button('administration','Verwaltung') : '')+button('guide','Fernsehprogramm')+button('settings','<span class="material-icons" aria-hidden="true">settings</span><span>Einstellungen</span>')+'</div></div>'
            +'<div class="ltvg-toolbar"><label class="ltvg-view-label"><span class="ltvg-sr">Ansicht</span><select data-control="view" aria-label="Ansicht" class="ltvg-view">'
            +Object.keys(views).filter(v => v !== 'manage' || access.CanManage).map(v => '<option value="'+v+'"'+(v===state.view?' selected':'')+'>'+views[v]+'</option>').join('')+'</select></label>'
            +'<label><span class="ltvg-sr">Gruppe</span><select class="ltvg-input" data-control="group" aria-label="Gruppe"><option value="all">Alle sichtbaren Gruppen</option>'
            +visible().map(g=>'<option value="'+esc(g.Id)+'"'+(id(g.Id)===id(state.group)?' selected':'')+'>'+esc(g.Name)+'</option>').join('')+'</select></label>'
            +'<div class="ltvg-actions ltvg-end">'+button('refresh','Aktualisieren')+(access.CanManage ? button('manage','Gruppen verwalten') : '')+'</div></div>'
            +'<div class="ltvg-player-toolbar"><label>Abspielen auf <select class="ltvg-input" data-control="player" aria-label="Abspielen auf">'+playerOptions()+'</select></label>'
            +button('players-refresh','Geräte aktualisieren')
            +'<span class="ltvg-player-status" role="status" aria-live="polite">'+esc(playerMessage)+'</span></div>'
            +'<p class="ltvg-player-hint">Am TV muss Jellyfin im Vordergrund mit internem Player geöffnet sein.</p>'
            +'<div class="ltvg-error" role="alert" hidden><span></span>'+button('refresh','Erneut versuchen')+'</div>'
            +'<div class="ltvg-content" aria-live="polite"></div>';
        root.querySelector('[data-control="group"]').value = state.group;
        updatePlayers();
    }
    function content(html) { const target = page()?.querySelector('.ltvg-content'); if (target) target.innerHTML = html; }
    function remember() {
        if (!prefs.RememberLastView || state.view === 'manage') return;
        prefs.LastView = state.view; prefs.LastGroupId = state.group === 'all' ? null : state.group;
        savePreferences().catch(error);
    }
    async function boot() {
        const seq = ++request; abort?.abort(); abort = new AbortController();
        try {
            const results = await Promise.all([api('GET','Groups',undefined,abort.signal), api('GET','Preferences',undefined,abort.signal), api('GET','Access',undefined,abort.signal)]);
            if (!active || seq !== request) return;
            groups = results[0]; prefs = results[1]; access = results[2]; readRoute(); chrome(); writeRoute(); refreshPlayers(); await render();
        } catch(e) { if (seq===request) { if (!prefs) { page().innerHTML='<h1>Live-TV Gruppen</h1><div class="ltvg-error" role="alert"><span></span>'+button('refresh','Erneut versuchen')+'</div>'; } error(e); } }
    }
    async function render(keep = false) {
        if (!active || !prefs) return;
        if (keep) capture(); else { clearInterval(timer); timer = null; chrome(); }
        const seq = ++request; abort?.abort(); abort = new AbortController(); const signal = abort.signal;
        if (state.view === 'manage' && !access.CanManage) state.view = 'guide';
        if (state.view === 'manage') { renderGroups(); return; }
        if (!selected().length) { content('<p class="ltvg-empty">Keine sichtbaren Gruppen. Prüfe deine Gruppeneinstellungen oder wende dich an den Administrator.</p>'+(access.CanManage ? button('create','Neue Gruppe') : '')); return; }
        if (!keep) content('<p class="ltvg-empty" role="status">Lädt…</p>');
        try {
            if (state.view === 'channels') {
                const result = await api('GET','Channels?'+scopeQuery(),undefined,signal);
                if (!active || seq!==request) return;
                loadedChannels = result.Items || []; renderChannels();
            } else {
                let from = state.view === 'programs' || state.start === null ? Date.now() : state.start;
                const rounded = new Date(from); rounded.setSeconds(0,0); rounded.setMinutes(rounded.getMinutes()-rounded.getMinutes()%30); from = rounded.getTime();
                const result = await api('GET','Guide?'+scopeQuery()+'&start='+encodeURIComponent(new Date(from).toISOString())+'&end='+encodeURIComponent(new Date(from+6*3600000).toISOString()),undefined,signal);
                if (!active || seq!==request) return;
                guideData = result; loadedChannels = result.Channels || [];
                state.view==='guide' ? renderGuide(result) : renderPrograms(result);
            }
            restore(); clearInterval(timer); tick = 0;
            timer = setInterval(() => { if (!active) return; tick++; refreshPlayers(); if (tick%5===0) render(true); else if (state.view==='guide') updateNow(); },60000);
        } catch(e) { if (seq===request) { if (!keep) content('<p class="ltvg-empty">Daten konnten nicht geladen werden.</p>'); error(e); } }
    }
    function renderGroups() {
        content('<div class="ltvg-section-heading"><h2>Sendergruppen</h2>'+button('create','Neue Gruppe','','ltvg-primary')+'</div>'
            +'<div class="ltvg-grid" data-list="groups">'+groups.map(g=>'<article class="ltvg-card ltvg-group-card" draggable="true" data-id="'+esc(g.Id)+'">'
                +button('open','<strong>'+esc(g.Name)+'</strong><span>'+g.ChannelCount+' Sender'+(prefs.HiddenGroupIds.some(hidden=>id(hidden)===id(g.Id))?' · Ausgeblendet':'')+'</span>','data-id="'+esc(g.Id)+'"','ltvg-card-main')
                +'<div class="ltvg-card-tools">'+button('rename','Umbenennen','data-id="'+esc(g.Id)+'"')+button('delete','Löschen','data-id="'+esc(g.Id)+'"')
                +(access.Mode === 'shared' && access.IsAdministrator ? button('group-access','Benutzerfreigabe','data-id="'+esc(g.Id)+'"') : '')
                +button('group-up','↑','data-id="'+esc(g.Id)+'" aria-label="'+esc(g.Name)+' nach oben"')+button('group-down','↓','data-id="'+esc(g.Id)+'" aria-label="'+esc(g.Name)+' nach unten"')+'</div></article>').join('')+'</div>'
            +(!groups.length?'<p class="ltvg-empty">Noch keine Gruppen.</p>':''));
        dragSort(page().querySelector('[data-list="groups"]'), async ids=>{ await api('PUT','Groups/Order',ids); await reloadGroups(); });
    }
    function channelCard(channel) {
        const program = channel.CurrentProgram;
        return '<article class="ltvg-channel-card" data-id="'+esc(channel.Id)+'"'+(state.reorder?' draggable="true"':'')+'>'
            +button('play','<span class="ltvg-logo">'+logo(channel)+'</span><span class="ltvg-card-title">'+esc(channel.ChannelNumber ? channel.ChannelNumber+' '+channel.Name : channel.Name)+'</span><span class="ltvg-card-sub">'+(program?esc(program.Name)+'<br>'+time(program.StartDate)+' – '+time(program.EndDate):'Keine Programmdaten')+'</span>', 'data-id="'+esc(channel.Id)+'" '+(state.reorder?'disabled':''),'ltvg-media-card')
            +(state.reorder?'<div class="ltvg-card-tools">'+button('channel-up','↑','data-id="'+esc(channel.Id)+'" aria-label="Sender nach oben"')+button('channel-down','↓','data-id="'+esc(channel.Id)+'" aria-label="Sender nach unten"')+'</div>':'')+'</article>';
    }
    function renderChannels() {
        const editable = access.CanManage && state.group !== 'all';
        content('<div class="ltvg-section-heading"><span>'+loadedChannels.length+' Sender</span><div class="ltvg-actions">'
            +(editable?button('edit-channels','Sender auswählen','','ltvg-primary')+button('reorder',state.reorder?'Fertig':'Reihenfolge ändern'):'')+'</div></div>'
            +'<div class="ltvg-grid ltvg-channel-grid" data-list="channels">'+loadedChannels.map(channelCard).join('')+'</div>'
            +(!loadedChannels.length?'<p class="ltvg-empty">Diese Gruppe enthält keine verfügbaren Sender.</p>':''));
        if (state.reorder) dragSort(page().querySelector('[data-list="channels"]'), async ids=>{ await api('PUT','Groups/'+state.group+'/Channels',ids); await render(true); });
    }
    function programCard(program, channel) {
        const url = image(program) || image(channel || {});
        return button('details','<span class="ltvg-program-image">'+(url?'<img loading="lazy" alt="" src="'+esc(url)+'">':logo(channel||{}))+'</span>'
            +'<span class="ltvg-card-title">'+esc(program.Name)+'</span><span class="ltvg-card-sub">'+esc(program.EpisodeTitle || channel?.Name || '')+'<br>'+time(program.StartDate)+' – '+time(program.EndDate)+'</span>', 'data-id="'+esc(program.Id)+'"','ltvg-media-card');
    }
    function renderPrograms(data) {
        const now = Date.now(), programs = data.Programs || [], channelMap = new Map(loadedChannels.map(c=>[id(c.Id),c]));
        const current = programs.filter(p=>Date.parse(p.StartDate)<=now && Date.parse(p.EndDate)>now);
        const next = loadedChannels.map(c=>programs.find(p=>id(p.ChannelId)===id(c.Id) && Date.parse(p.StartDate)>now)).filter(Boolean);
        const nextIds = new Set(next.map(p=>p.Id)); const later = programs.filter(p=>Date.parse(p.StartDate)>now && !nextIds.has(p.Id));
        content([['Gerade läuft',current],['Als Nächstes',next],['Später',later]].map(([title,items])=>'<section class="ltvg-program-section"><h2>'+title+'</h2>'
            +(items.length?'<div class="ltvg-program-rail">'+items.map(p=>programCard(p,channelMap.get(id(p.ChannelId)))).join('')+'</div>':'<p class="ltvg-empty">Keine Programmdaten in diesem Zeitraum.</p>')+'</section>').join('')+diagnostics(data));
    }
    function diagnostics(data) {
        return (data.MissingChannelCount?'<p class="ltvg-hint">'+data.MissingChannelCount+' gespeicherte Sender sind derzeit nicht verfügbar. Prüfe die Senderauswahl.</p>':'')
            +(data.InvalidProgramCount?'<p class="ltvg-hint">'+data.InvalidProgramCount+' unvollständige Programme konnten nicht dargestellt werden.</p>':'');
    }
    function renderGuide(data) {
        const start = Date.parse(data.Start), end = Date.parse(data.End), width = (end-start)/60000*prefs.Zoom;
        const today = new Date(); today.setHours(0,0,0,0);
        let days = ''; for(let n=0;n<7;n++) { const date=new Date(today);date.setDate(date.getDate()+n);
            days+=button('day','<span>'+date.toLocaleDateString([],{weekday:'short'})+'</span><span>'+date.getDate()+'</span>','data-date="'+date.getTime()+'" aria-label="'+esc(dayLabel(date))+'" aria-pressed="'+(new Date(start).toDateString()===date.toDateString())+'"','ltvg-day'); }
        let html='<nav class="ltvg-days" aria-label="Programmtag">'+days+'</nav><div class="ltvg-guide-toolbar">'
            +button('earlier','‹ früher')+button('now','Jetzt')+button('evening','Heute Abend')+button('later','später ›')
            +'<label>Datum <input type="date" class="ltvg-input" data-control="date" value="'+localDate(new Date(start))+'"></label>'
            +'<label>Uhrzeit <input type="time" class="ltvg-input" data-control="time" value="'+new Date(start).toTimeString().slice(0,5)+'"></label>'
            +'<label>Zoom <select class="ltvg-input" data-control="zoom" aria-label="Zoom">'+[3,5,8].map(z=>'<option value="'+z+'"'+(prefs.Zoom===z?' selected':'')+'>'+({3:'Kompakt',5:'Normal',8:'Groß'}[z])+'</option>').join('')+'</select></label>'
            +'<span class="ltvg-guide-range">'+esc(dayLabel(start))+' '+time(start)+' – '+time(end)+'</span></div>';
        if (!loadedChannels.length) { content(html+'<p class="ltvg-empty">Diese Gruppe enthält keine verfügbaren Sender.</p>'+diagnostics(data)); return; }
        const byChannel = new Map(); (data.Programs||[]).forEach(p=>{const key=id(p.ChannelId);if(!byChannel.has(key))byChannel.set(key,[]);byChannel.get(key).push(p);});
        html+='<div class="ltvg-guide-scroll" tabindex="0" aria-label="Programmführer"><div class="ltvg-guide" style="width:calc(var(--ltvg-channel-width) + '+width+'px)">'
            +'<div class="ltvg-guide-row ltvg-guide-timeline"><div class="ltvg-guide-ch"></div><div class="ltvg-guide-cells" style="width:'+width+'px">';
        for(let minute=0;minute<(end-start)/60000;minute+=30) html+='<span class="ltvg-guide-slot" style="left:'+(minute*prefs.Zoom)+'px;width:'+(30*prefs.Zoom)+'px">'+time(start+minute*60000)+'</span>';
        html+='</div></div>';
        loadedChannels.forEach(c=>{
            html+='<div class="ltvg-guide-row">'+button('play','<span class="ltvg-number">'+esc(c.ChannelNumber||'')+'</span><span class="ltvg-guide-logo">'+logo(c)+'</span>','data-id="'+esc(c.Id)+'" title="'+esc(c.Name)+' abspielen" aria-label="'+esc(c.Name)+' abspielen"','ltvg-guide-ch')+'<div class="ltvg-guide-cells" style="width:'+width+'px">';
            const programs = byChannel.get(id(c.Id)) || []; let count=0;
            programs.forEach(p=>{const from=Math.max(Date.parse(p.StartDate),start),to=Math.min(Date.parse(p.EndDate),end);if(!(to>from))return;count++;
                const left=(from-start)/60000*prefs.Zoom, size=Math.max(2,(to-from)/60000*prefs.Zoom-2);
                html+=button('details','<span class="ltvg-guide-caption"><span class="ltvg-guide-title">'+(p.TimerId?'● ':'')+esc(p.Name)+'</span><span class="ltvg-guide-sub">'+esc(p.EpisodeTitle||'')+'</span><span class="ltvg-guide-sub">'+time(p.StartDate)+' – '+time(p.EndDate)+'</span></span>',
                    'data-id="'+esc(p.Id)+'" data-focus="program-'+esc(p.Id)+'" data-left="'+left+'" data-width="'+size+'" data-from="'+Date.parse(p.StartDate)+'" data-to="'+Date.parse(p.EndDate)+'" style="left:'+left+'px;width:'+size+'px" title="'+esc(p.Name+(p.EpisodeTitle?' – '+p.EpisodeTitle:''))+' ('+time(p.StartDate)+' – '+time(p.EndDate)+')"','ltvg-guide-prog');
            });
            if (!count) html+='<span class="ltvg-guide-noprog">Keine Programmdaten</span>';
            html+='</div></div>';
        });
        html+='<div class="ltvg-guide-now" hidden></div></div></div>'+diagnostics(data);
        content(html); const scroller = page().querySelector('.ltvg-guide-scroll');
        scroller.addEventListener('scroll',keepLabelsVisible,{passive:true}); updateNow();
    }
    function keepLabelsVisible() {
        const scroll=page()?.querySelector('.ltvg-guide-scroll'); if(!scroll)return;
        const viewport=scroll.clientWidth-(scroll.querySelector('.ltvg-guide-ch')?.offsetWidth||0);
        scroll.querySelectorAll('.ltvg-guide-prog').forEach(e=>{
            const offset=Math.max(0,Math.min(scroll.scrollLeft-Number(e.dataset.left),Number(e.dataset.width)-8));
            const caption=e.firstElementChild;caption.style.transform='translateX('+offset+'px)';
            caption.style.maxWidth=Math.max(0,Math.min(viewport,Number(e.dataset.width)-offset)-12)+'px';
        });
        scroll.querySelectorAll('.ltvg-guide-noprog').forEach(e=>e.style.transform='translateX('+scroll.scrollLeft+'px)');
    }
    function updateNow() {
        const root=page();if(!root||!guideData)return; const now=Date.now(),start=Date.parse(guideData.Start),end=Date.parse(guideData.End);
        const line=root.querySelector('.ltvg-guide-now'); if(line){line.hidden=now<start||now>=end;line.style.left='calc(var(--ltvg-channel-width) + '+((now-start)/60000*prefs.Zoom)+'px)';}
        root.querySelectorAll('.ltvg-guide-prog').forEach(e=>e.classList.toggle('ltvg-guide-live',Number(e.dataset.from)<=now&&Number(e.dataset.to)>now));
    }
    function localDate(date) { return date.getFullYear()+'-'+String(date.getMonth()+1).padStart(2,'0')+'-'+String(date.getDate()).padStart(2,'0'); }
    let modalCancel = null, modalOpener = null;
    function closeModal() { const cancel=modalCancel;modalCancel=null;document.getElementById(MODAL)?.remove();modalOpener?.focus({preventScroll:true});modalOpener=null;cancel?.(); }
    function modal(html,cancel) {
        closeModal();modalOpener=document.activeElement;modalCancel=cancel;
        const wrapper=document.createElement('div');wrapper.id=MODAL;wrapper.className='ltvg-modal-backdrop';
        wrapper.innerHTML='<div class="ltvg-modal" role="dialog" aria-modal="true" aria-labelledby="ltvg-dialog-title">'+html+'<p class="ltvg-modal-error" role="alert" hidden></p></div>';
        wrapper.addEventListener('click',e=>{if(e.target===wrapper)closeModal();});
        wrapper.addEventListener('keydown',e=>{if(e.key==='Escape'){e.preventDefault();closeModal();}if(e.key==='Tab'){
            const els=Array.from(wrapper.querySelectorAll('button:not(:disabled),input,select')).filter(el=>el.getClientRects().length);const first=els[0],last=els[els.length-1];
            if(e.shiftKey&&document.activeElement===first){e.preventDefault();last?.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first?.focus();}
        }});document.body.appendChild(wrapper);wrapper.querySelector('input,select,button')?.focus();return wrapper;
    }
    function modalError(wrapper,e) { const target=wrapper.querySelector('.ltvg-modal-error');target.hidden=false;target.textContent=e.message; }
    function askName(title,initial='') {
        return new Promise(resolve=>{ const wrapper=modal('<h2 id="ltvg-dialog-title">'+esc(title)+'</h2><form><label>Name<input class="ltvg-input" maxlength="100" required value="'+esc(initial)+'"></label><div class="ltvg-modal-actions">'+button('cancel','Abbrechen')+'<button class="ltvg-btn ltvg-primary" type="submit">Speichern</button></div></form>',()=>resolve(null));
            wrapper.querySelector('[data-action="cancel"]').onclick=closeModal;wrapper.querySelector('form').onsubmit=e=>{e.preventDefault();const value=wrapper.querySelector('input').value.trim();modalCancel=null;closeModal();resolve(value||null);}; });
    }
    function confirmDelete(name) {
        return new Promise(resolve=>{const wrapper=modal('<h2 id="ltvg-dialog-title">Gruppe löschen</h2><p>Gruppe „'+esc(name)+'“ löschen? Die Sender bleiben erhalten.</p><div class="ltvg-modal-actions">'+button('cancel','Abbrechen')+button('confirm','Löschen','','ltvg-danger')+'</div>',()=>resolve(false));
            wrapper.querySelector('[data-action="cancel"]').onclick=closeModal;wrapper.querySelector('[data-action="confirm"]').onclick=()=>{modalCancel=null;closeModal();resolve(true);}; });
    }
    async function settings() {
        const wrapper=modal('<h2 id="ltvg-dialog-title">Gruppeneinstellungen</h2><form><fieldset><legend>Sichtbare Gruppen</legend>'+groups.map(g=>'<label class="ltvg-picker-row"><input type="checkbox" data-group="'+esc(g.Id)+'"'+(!prefs.HiddenGroupIds.some(hidden=>id(hidden)===id(g.Id))?' checked':'')+'>'+esc(g.Name)+'</label>').join('')+'</fieldset>'
            +'<label>Standardgruppe<select class="ltvg-input" name="group"><option value="">Alle sichtbaren Gruppen</option>'+groups.map(g=>'<option value="'+esc(g.Id)+'">'+esc(g.Name)+'</option>').join('')+'</select></label>'
            +'<label>Standardansicht<select class="ltvg-input" name="view">'+['programs','guide','channels'].map(v=>'<option value="'+v+'">'+views[v]+'</option>').join('')+'</select></label>'
            +'<label class="ltvg-picker-row"><input type="checkbox" name="remember">Letzte Gruppe und Ansicht merken</label>'
            +'<label>EPG-Zoom<select class="ltvg-input" name="zoom"><option value="3">Kompakt</option><option value="5">Normal</option><option value="8">Groß</option></select></label>'
            +'<div class="ltvg-modal-actions">'+button('cancel','Abbrechen')+'<button type="submit" class="ltvg-btn ltvg-primary">Speichern</button></div></form>');
        wrapper.querySelector('[name="group"]').value=prefs.DefaultGroupId||'';wrapper.querySelector('[name="view"]').value=prefs.DefaultView;
        wrapper.querySelector('[name="remember"]').checked=prefs.RememberLastView;wrapper.querySelector('[name="zoom"]').value=prefs.Zoom;
        wrapper.querySelector('[data-action="cancel"]').onclick=closeModal;
        wrapper.querySelector('form').onsubmit=async e=>{e.preventDefault();const form=e.target;const save=form.querySelector('[type="submit"]');save.disabled=true;
            const updated={...prefs,HiddenGroupIds:Array.from(wrapper.querySelectorAll('[data-group]:not(:checked)')).map(el=>el.dataset.group),DefaultGroupId:form.elements.group.value||null,DefaultView:form.elements.view.value,RememberLastView:form.elements.remember.checked,Zoom:Number(form.elements.zoom.value)};
            try{if(updated.DefaultGroupId&&updated.HiddenGroupIds.includes(updated.DefaultGroupId))throw new Error('Die Standardgruppe muss sichtbar sein.');await savePreferences(updated);prefs=updated;closeModal();if(!visible().some(g=>id(g.Id)===id(state.group)))state.group='all';writeRoute();await render();}catch(e){modalError(wrapper,e);}finally{save.disabled=false;}
        };
    }
    async function administration() {
        const data = await api('GET','Administration');
        const wrapper = modal('<h2 id="ltvg-dialog-title">Gruppenverwaltung</h2><form>'
            +'<label>Betriebsart<select class="ltvg-input" name="mode" aria-label="Betriebsart"><option value="personal">Persönliche Gruppen je Benutzer</option><option value="shared">Zentrale Gruppen vom Admin</option></select></label>'
            +'<p>Persönlich: Jeder Benutzer erstellt seine eigenen Gruppen. Zentral: Administratoren erstellen Gruppen und bestimmen deren Benutzerfreigabe.</p>'
            +'<label class="ltvg-picker-row"><input type="checkbox" name="import">Meine persönlichen Gruppen in zentrale Gruppen kopieren</label>'
            +'<p>Beide Gruppensammlungen bleiben beim Umschalten erhalten. Administratoren können alle zentralen Gruppen verwalten. Die Jellyfin-Kanalberechtigung bleibt Voraussetzung.</p>'
            +'<div class="ltvg-modal-actions">'+button('cancel','Abbrechen')+'<button type="submit" class="ltvg-btn ltvg-primary">Speichern</button></div></form>');
        const form = wrapper.querySelector('form');form.elements.mode.value=data.Configuration.Mode;
        const update=()=>{form.elements.import.disabled=form.elements.mode.value!=='shared';if(form.elements.import.disabled)form.elements.import.checked=false;};
        form.elements.mode.onchange=update;update();wrapper.querySelector('[data-action="cancel"]').onclick=closeModal;
        form.onsubmit=async e=>{e.preventDefault();const save=form.querySelector('[type="submit"]');save.disabled=true;
            try{await api('PUT','Administration',{Mode:form.elements.mode.value,ImportPersonalGroups:form.elements.import.checked});closeModal();state.view='guide';state.group='all';await reloadGroups();}
            catch(e){modalError(wrapper,e);}finally{save.disabled=false;}
        };
    }
    async function groupAccess(groupId) {
        const data=await api('GET','Administration'),group=data.Configuration.Groups.find(g=>id(g.Id)===id(groupId));if(!group)return;
        const checks = { all:new Set((group.DeniedUserIds||[]).map(id)), selected:new Set((group.AllowedUserIds||[]).filter(u=>!(group.DeniedUserIds||[]).some(d=>id(d)===id(u))).map(id)) };
        let mode=group.VisibleToAllUsers?'all':'selected';
        const wrapper=modal('<h2 id="ltvg-dialog-title">Benutzerfreigabe: '+esc(group.Name)+'</h2><form>'
            +'<label>Sichtbarkeit<select class="ltvg-input" name="visibility" aria-label="Sichtbarkeit"><option value="all">Alle Benutzer außer ausgewählten</option><option value="selected">Nur ausgewählte Benutzer</option></select></label>'
            +'<p data-access-hint></p><div class="ltvg-picker">'+data.Users.map(u=>'<label class="ltvg-picker-row"><input type="checkbox" data-user="'+esc(u.Id)+'"'+(u.IsAdministrator?' disabled':'')+'>'+esc(u.Name)+(u.IsAdministrator?' · Admin (immer Zugriff)':!u.HasLiveTvAccess?' · ohne Live-TV-Zugriff':'')+'</label>').join('')+'</div>'
            +'<div class="ltvg-modal-actions">'+button('cancel','Abbrechen')+'<button type="submit" class="ltvg-btn ltvg-primary">Speichern</button></div></form>');
        const form=wrapper.querySelector('form');form.elements.visibility.value=mode;
        const draw=()=>{wrapper.querySelector('[data-access-hint]').textContent=mode==='all'?'Markierte Benutzer sehen diese Gruppe nicht.':'Nur markierte Benutzer sehen diese Gruppe.';
            wrapper.querySelectorAll('[data-user]').forEach(el=>el.checked=el.disabled?mode==='selected':checks[mode].has(id(el.dataset.user)));};
        form.elements.visibility.onchange=()=>{wrapper.querySelectorAll('[data-user]:not(:disabled)').forEach(el=>el.checked?checks[mode].add(id(el.dataset.user)):checks[mode].delete(id(el.dataset.user)));mode=form.elements.visibility.value;draw();};draw();
        wrapper.querySelector('[data-action="cancel"]').onclick=closeModal;
        form.onsubmit=async e=>{e.preventDefault();const save=form.querySelector('[type="submit"]');save.disabled=true;
            const ids=Array.from(wrapper.querySelectorAll('[data-user]:checked:not(:disabled)')).map(el=>el.dataset.user);
            try{await api('PUT','Administration/Groups/'+encodeURIComponent(groupId)+'/Access',{VisibleToAllUsers:mode==='all',AllowedUserIds:mode==='selected'?ids:[],DeniedUserIds:mode==='all'?ids:[]});closeModal();await reloadGroups();}
            catch(e){modalError(wrapper,e);}finally{save.disabled=false;}
        };
    }
    async function editChannels() {
        const group=groups.find(g=>id(g.Id)===id(state.group));if(!group)return;
        const current=loadedChannels.map(c=>c.Id),chosen=new Set(current);
        const wrapper=modal('<h2 id="ltvg-dialog-title">Sender für „'+esc(group.Name)+'“</h2><input type="search" class="ltvg-input" aria-label="Sender suchen" placeholder="Sender suchen…"><label class="ltvg-picker-row"><input type="checkbox" data-selected-only>Nur ausgewählte Sender</label><span class="ltvg-picker-count"></span><div class="ltvg-picker"><p role="status">Lädt…</p></div><div class="ltvg-modal-actions">'+button('cancel','Abbrechen')+button('save','Speichern','disabled','ltvg-primary')+'</div>');
        wrapper.querySelector('[data-action="cancel"]').onclick=closeModal;
        try{const result=await api('GET','AvailableChannels');if(!wrapper.isConnected)return;const channels=result.Items||[];
            const list=wrapper.querySelector('.ltvg-picker');list.innerHTML=channels.map(c=>'<label class="ltvg-picker-row" data-search="'+esc(((c.ChannelNumber||'')+' '+c.Name).toLowerCase())+'"><input type="checkbox" value="'+esc(c.Id)+'"'+(chosen.has(c.Id)?' checked':'')+'><span class="ltvg-number">'+esc(c.ChannelNumber||'')+'</span>'+esc(c.Name)+'</label>').join('');
            const filter=()=>{const term=wrapper.querySelector('[type="search"]').value.trim().toLowerCase(),only=wrapper.querySelector('[data-selected-only]').checked;let matches=0;list.querySelectorAll('.ltvg-picker-row').forEach(row=>{row.hidden=!row.dataset.search.includes(term)||(only&&!chosen.has(row.querySelector('input').value));if(!row.hidden)matches++;});wrapper.querySelector('.ltvg-picker-count').textContent=chosen.size+' ausgewählt · '+matches+' Treffer';};
            list.onchange=e=>{e.target.checked?chosen.add(e.target.value):chosen.delete(e.target.value);filter();};wrapper.querySelector('[type="search"]').oninput=filter;wrapper.querySelector('[data-selected-only]').onchange=filter;filter();
            const save=wrapper.querySelector('[data-action="save"]');save.disabled=false;save.onclick=async()=>{save.disabled=true;const ids=current.filter(value=>chosen.has(value));channels.forEach(c=>{if(chosen.has(c.Id)&&!ids.includes(c.Id))ids.push(c.Id);});try{await api('PUT','Groups/'+group.Id+'/Channels',ids);closeModal();await reloadGroups();}catch(e){modalError(wrapper,e);save.disabled=false;}};
        }catch(e){modalError(wrapper,e);}
    }
    async function reloadGroups() {
        const results=await Promise.all([api('GET','Access'),api('GET','Groups'),api('GET','Preferences')]);
        access=results[0];groups=results[1];prefs=results[2];
        if(state.group!=='all'&&!visible().some(g=>id(g.Id)===id(state.group)))state.group='all';
        if(state.view==='manage'&&!access.CanManage)state.view='guide';
        capture();writeRoute();await render();
    }
    function dragSort(list,save) {
        let dragged=null;list.addEventListener('dragstart',e=>{dragged=e.target.closest('[draggable]');if(dragged){e.dataTransfer.setData('text/plain',dragged.dataset.id);dragged.classList.add('ltvg-dragging');}});
        list.addEventListener('dragover',e=>{if(!dragged)return;e.preventDefault();const target=e.target.closest('[draggable]');if(!target||target===dragged)return;const rect=target.getBoundingClientRect();list.insertBefore(dragged,e.clientY>rect.top+rect.height/2?target.nextSibling:target);});
        list.addEventListener('dragend',()=>{if(!dragged)return;dragged.classList.remove('ltvg-dragging');dragged=null;save(Array.from(list.children).map(e=>e.dataset.id)).catch(error);});
    }
    async function move(action,value) {
        const channel=action.startsWith('channel'),items=channel?loadedChannels:groups,index=items.findIndex(i=>id(i.Id)===id(value)),to=index+(action.endsWith('up')?-1:1);
        if(index<0||to<0||to>=items.length)return;const ids=items.map(i=>i.Id);[ids[index],ids[to]]=[ids[to],ids[index]];
        await api('PUT',channel?'Groups/'+state.group+'/Channels':'Groups/Order',ids);channel?await render(true):await reloadGroups();
    }
    async function action(target) {
        const action=target.dataset.action,value=target.dataset.id;
        if(action==='refresh'){page()?.querySelector('.ltvg-error')?.setAttribute('hidden','');prefs?await Promise.all([reloadGroups(),refreshPlayers()]):await boot();return;}
        if(action==='players-refresh'){await refreshPlayers();return;}
        if(action==='settings'){await settings();return;}
        if(action==='guide'){state.view='guide';state.reorder=false;writeRoute();remember();await render();return;}
        if(action==='administration'){if(access.IsAdministrator)await administration();return;}
        if(action==='group-access'){if(access.IsAdministrator&&access.Mode==='shared')await groupAccess(value);return;}
        if(!access.CanManage && (['create','rename','delete','edit-channels','manage','reorder'].includes(action)||action.startsWith('group-')||action.startsWith('channel-')))return;
        if(action==='create'){const name=await askName('Neue Gruppe');if(name){const created=await api('POST','Groups',{Name:name});state.group=created.Id;state.view='channels';writeRoute();await reloadGroups();}return;}
        if(action==='rename'){const group=groups.find(g=>id(g.Id)===id(value)),name=await askName('Gruppe umbenennen',group.Name);if(name){await api('PUT','Groups/'+value,{Name:name});await reloadGroups();}return;}
        if(action==='delete'){const group=groups.find(g=>id(g.Id)===id(value));if(await confirmDelete(group.Name)){await api('DELETE','Groups/'+value);await reloadGroups();}return;}
        if(action==='edit-channels'){await editChannels();return;}
        if(action.startsWith('group-')||action.startsWith('channel-')){await move(action,value);return;}
        if(action==='details'||action==='play'){capture();writeRoute();if(action==='play'){if(prefs.PreferredTargetDeviceId){await playRemote(value);return;}if(playerBusy||playbackBusy)return;try{const sessions=await client().ajax({type:'GET',url:client().getUrl('Sessions',{deviceId:client().deviceId()})});if(!sessions.length)throw new Error('Keine Sitzung.');await client().ajax({type:'POST',url:client().getUrl('Sessions/'+sessions[0].Id+'/Playing',{playCommand:'PlayNow',itemIds:value})});return;}catch(_) { /* Native details remain the playback fallback. */ }}window.location.hash='#/details?id='+encodeURIComponent(value)+'&serverId='+encodeURIComponent(client().serverId());return;}
        capture();restoreFocus=null;
        if(action==='manage')state.view='manage';
        else if(action==='open'){state.group=value;state.view='guide';if(prefs.HiddenGroupIds.some(hidden=>id(hidden)===id(value))){prefs.HiddenGroupIds=prefs.HiddenGroupIds.filter(hidden=>id(hidden)!==id(value));await savePreferences();}}
        else if(action==='reorder')state.reorder=!state.reorder;
        else if(action==='now')state.start=null;
        else if(action==='earlier'||action==='later')state.start=(state.start===null?Date.now():state.start)+(action==='earlier'?-3:3)*3600000;
        else if(action==='evening'){const date=new Date();date.setHours(18,0,0,0);state.start=date.getTime();}
        else if(action==='day'){const date=new Date(Number(target.dataset.date)),today=new Date();if(date.toDateString()===today.toDateString())state.start=null;else{date.setHours(18,0,0,0);state.start=date.getTime();}}
        else return;
        writeRoute();remember();await render();
    }
    document.addEventListener('click',e=>{
        const target=e.target.closest('#'+PAGE+' [data-action]');if(!target)return;e.preventDefault();
        if(target.disabled)return;target.disabled=true;action(target).catch(error).finally(()=>target.disabled=false);
    });
    document.addEventListener('change',e=>{
        const target=e.target;if(!page()?.contains(target)||!target.dataset.control)return;
        if(target.dataset.control==='player'){choosePlayer(target).catch(error);return;}
        capture();restoreFocus=null;
        const control=target.dataset.control;
        if(control==='view'){state.view=target.value;state.reorder=false;}
        else if(control==='group'){state.group=target.value;state.reorder=false;}
        else if(control==='zoom'){prefs.Zoom=Number(target.value);savePreferences().catch(error);}
        else if(control==='date'||control==='time'){
            const dateInput=page().querySelector('[data-control="date"]'),timeInput=page().querySelector('[data-control="time"]');
            const date=new Date(dateInput.value+'T'+timeInput.value);if(!Number.isFinite(date.getTime()))return;state.start=date.getTime();
        }
        writeRoute();remember();render().catch(error);
    });
    function mount() {
        if(!ownRoute()){if(active){capture();stop();}return;}
        const candidate=document.querySelector('.page.libraryPage:not(.hide)');if(!candidate)return;
        if(host!==candidate||!page()){
            host?.removeAttribute('data-ltvg-host');host=candidate;host.setAttribute('data-ltvg-host','');styles();
            const root=document.createElement('main');root.id=PAGE;root.className='ltvg-page padded-left padded-right padded-bottom-page';root.innerHTML='<h1>Live-TV Gruppen</h1><p role="status">Lädt…</p>';host.appendChild(root);
            active=true;lastRoute=window.location.hash;boot();
        }else if(lastRoute!==window.location.hash&&prefs){capture();readRoute();lastRoute=window.location.hash;render();}
    }
    function ensure() {
        const current=client();const key=current?.getCurrentUserId?.()+'|'+current?.serverId?.();
        if(!current?.accessToken?.()||!current.getCurrentUserId()){if(active)stop();entry=null;userKey='';return;}
        if(key!==userKey){stop();entry=null;prefs=null;groups=[];players=[];playerRequest++;playerBusy=false;playbackBusy=false;playerMessage='';positions.clear();userKey=key;}
        if(!entry&&!entryPending){entryPending=true;const expected=key;api('GET','Entry').then(result=>{if(expected===userKey)entry=result;}).catch(()=>{}).finally(()=>{entryPending=false;if(entry)schedule();});return;}
        if(!entry?.ChannelId)return;
        // Rewrite only the own channel entry. Never touch Live TV links or requests.
        document.querySelectorAll('a[href*="parentId="]').forEach(link=>{
            try{const url=new URL(link.getAttribute('href').replace(/^#/,'') ,location.origin);if(url.pathname==='/list'&&id(url.searchParams.get('parentId'))===id(entry.ChannelId)&&!url.searchParams.has('liveTvGroups'))link.setAttribute('href',routeUrl());}catch(_){}
        });
        mount();
    }
    function schedule() { if(scheduled)return;scheduled=true;requestAnimationFrame(()=>{scheduled=false;ensure();}); }
    window.addEventListener('hashchange',schedule);document.addEventListener('viewshow',schedule);
    window.addEventListener('resize',keepLabelsVisible);
    function start() { new MutationObserver(schedule).observe(document.body,{childList:true,subtree:true});schedule(); }
    document.body?start():document.addEventListener('DOMContentLoaded',start);
})();
