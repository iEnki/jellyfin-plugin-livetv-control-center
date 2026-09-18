(function () {
    'use strict';
    if (window.LiveTvGroupsChannelAccess) return;
    const t = key => window.LiveTvGroupsI18n?.t(key) || key;
    const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const copy = value => JSON.parse(JSON.stringify(value));
    const same = (a,b) => a.ServiceName && a.ExternalId && b.ServiceName && b.ExternalId ? a.ServiceName === b.ServiceName && a.ExternalId === b.ExternalId : a.ItemId === b.ItemId;
    const label = c => [c.Number,c.Name,c.ServiceName].filter(Boolean).join(' · ');
    async function open() {
        if (document.getElementById('ltvg-acl')) return;
        const actor = window.ApiClient?.getCurrentUserId?.();
        const api = window.ApiClient;
        const before = document.activeElement;
        const root = document.createElement('div'); root.id = 'ltvg-acl';
        root.innerHTML = `<style>
#ltvg-acl{position:fixed;inset:0;z-index:99999;background:#000a;display:flex;justify-content:center;align-items:center;color:#eee;font:16px sans-serif}
#ltvg-acl .acl-dialog{background:#202020;width:min(1100px,96vw);max-height:94vh;overflow:auto;padding:22px;border-radius:8px;box-sizing:border-box}
#ltvg-acl button,#ltvg-acl select,#ltvg-acl input[type=text]{background:#303030;color:#eee;border:1px solid #666;border-radius:4px;padding:9px;max-width:100%;box-sizing:border-box}
#ltvg-acl button{cursor:pointer}#ltvg-acl button:disabled{opacity:.5;cursor:default}#ltvg-acl label{display:block;margin:8px 0}#ltvg-acl input[type=checkbox]{margin-right:8px}
#ltvg-acl .acl-actions{display:flex;gap:10px;flex-wrap:wrap;margin:12px 0}#ltvg-acl .acl-list{max-height:250px;overflow:auto;border:1px solid #555;padding:10px}#ltvg-acl .acl-rule{padding:12px;border:1px solid #555;margin:8px 0}
#ltvg-acl .acl-error{color:#ffb4b4;white-space:pre-wrap}#ltvg-acl .acl-muted{color:#bbb;font-size:14px}#ltvg-acl .acl-primary{background:#007b9d}#ltvg-acl h2,#ltvg-acl h3{margin-top:14px}#ltvg-acl .acl-preview-row{padding:6px;border-bottom:1px solid #444}
</style><section class="acl-dialog" role="dialog" aria-modal="true" aria-labelledby="acl-title"><h2 id="acl-title">${t('Channel access')}</h2><p>${t('Loading…')}</p></section>`;
        document.body.append(root);
        const panel = root.querySelector('section');
        let state, editing, preview, generation = 0, busy = false;
        function close() { generation++; root.remove(); window.removeEventListener('hashchange',close); document.removeEventListener('keydown',key); before?.focus(); }
        function key(e) { if(e.key==='Escape') close(); if(e.key==='Tab'){const nodes=[...root.querySelectorAll('button:not(:disabled),input,select')].filter(n=>n.offsetParent);const first=nodes[0],last=nodes.at(-1);if(e.shiftKey&&document.activeElement===first){e.preventDefault();last?.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first?.focus();}} }
        document.addEventListener('keydown',key); window.addEventListener('hashchange',close);
        async function request(method, suffix='', body) {
            if(actor !== api.getCurrentUserId?.()) throw Error(t('Please sign in again.'));
            const r = await fetch(api.getUrl('LiveTvGroups/Administration/ChannelAccess'+suffix), {method, headers:{Authorization:'MediaBrowser Token="'+api.accessToken()+'"','Content-Type':'application/json'},body:body ? JSON.stringify(body) : undefined});
            if(actor !== api.getCurrentUserId?.()) { close(); throw Error(t('Please sign in again.')); }
            if(!r.ok) throw Error(r.status===409 ? t('Channel access changed. Reload before saving.') : t('Request failed (HTTP ')+r.status+'): '+await r.text());
            return r.json();
        }
        function error(e) { const node=root.querySelector('.acl-error'); if(node) node.textContent=e.message; }
        function payload() {return {Revision:state.Revision,Enabled:state.Enabled,Rules:state.Rules,Recordings:state.Assignments,PreviewUserId:root.querySelector('#acl-user')?.value || state.Users.find(u=>!u.IsAdministrator)?.Id || actor};}
        function dirty() { preview=null;generation++; const save=root.querySelector('#acl-save');if(save)save.disabled=true; }
        function wire(selector,event,fn) { root.querySelector(selector)?.addEventListener(event,fn); }
        function render() {
            if(!root.isConnected) return;
            const selected=root.querySelector('#acl-user')?.value;
            panel.innerHTML=`<h2 id="acl-title">${t('Channel access')}</h2>
<p>${t('Named rules apply to personal and shared groups, ordinary Live TV, TV apps and associated recordings. Existing Jellyfin permissions still apply.')}</p>
<label><input id="acl-enabled" type="checkbox" ${state.Enabled?'checked':''}>${t('Enable central channel access')}</label>
<p class="acl-muted">${t('Unclassified new channels remain visible. Administrators retain management access. A denial in any rule takes precedence.')}</p>
<div class="acl-actions"><button id="acl-add">${t('New rule')}</button><button id="acl-reload">${t('Reload saved settings')}</button></div>
<div id="acl-rules">${state.Rules.map(r=>`<div class="acl-rule"><strong>${esc(r.Name)}</strong> · ${r.Channels.length} ${t('Channels')} · ${t(r.VisibleToAllUsers?'Everyone except selected users':'Only selected users')}<div class="acl-actions"><button data-edit="${r.Id}">${t('Edit')}</button><button data-delete="${r.Id}">${t('Delete')}</button></div></div>`).join('')||t('No rules.')}</div>
<div id="acl-editor"></div>
<h3>${t('Recording channel assignment')}</h3><p class="acl-muted">${t('Assign old recordings with unknown origin, or correct a source. Unassigned old recordings keep their existing Jellyfin permissions.')}</p>
<label>${t('Recording')}<select id="acl-recording" aria-label="${t('Recording')}"><option value="">${t('Choose a recording')}</option>${state.Recordings.map(r=>`<option value="${r.Id}">${esc(r.Name)}${state.Assignments.some(a=>a.ItemId===r.Id)?' · '+t('Assigned'):''}</option>`).join('')}</select></label>
<label>${t('Channel')}<select id="acl-recording-channel" aria-label="${t('Channel')}"><option value="">${t('Choose a channel')}</option>${state.Channels.map(c=>`<option value="${c.ItemId}">${esc(label(c))}</option>`).join('')}</select></label><button id="acl-assign">${t('Assign recording')}</button>
<h3>${t('User preview')}</h3><label>${t('User')}<select id="acl-user" aria-label="${t('User')}">${state.Users.map(u=>`<option value="${u.Id}" ${u.Id===(selected||state.Users.find(x=>!x.IsAdministrator)?.Id||actor)?'selected':''}>${esc(u.Name)}</option>`).join('')}</select></label>
<label>${t('Search preview')}<input id="acl-preview-search" type="text"></label><button id="acl-preview-refresh">${t('Preview changes')}</button><div id="acl-preview"></div>
<p class="acl-error" role="alert"></p><div class="acl-actions"><button id="acl-save" class="acl-primary" disabled>${t('Save channel access')}</button><button id="acl-close">${t('Close')}</button></div>`;
            wire('#acl-enabled','change',e=>{state.Enabled=e.target.checked;dirty();});
            wire('#acl-close','click',close);
            wire('#acl-reload','click',async()=>{try{state=await request('GET');dirty();editing=null;render();await showPreview();}catch(e){error(e);}});
            wire('#acl-add','click',()=>edit({Id:crypto.randomUUID(),Name:'',VisibleToAllUsers:false,AllowedUserIds:[],DeniedUserIds:[],Channels:[]}));
            root.querySelectorAll('[data-edit]').forEach(b=>b.onclick=()=>edit(copy(state.Rules.find(r=>r.Id===b.dataset.edit))));
            root.querySelectorAll('[data-delete]').forEach(b=>b.onclick=()=>{state.Rules=state.Rules.filter(r=>r.Id!==b.dataset.delete);dirty();editing=null;render();});
            wire('#acl-assign','click',()=>{const id=root.querySelector('#acl-recording').value,channel=state.Channels.find(c=>c.ItemId===root.querySelector('#acl-recording-channel').value);if(!id||!channel)return;state.Assignments=state.Assignments.filter(a=>a.ItemId!==id);state.Assignments.push({ItemId:id,Channel:copy(channel)});dirty();render();});
            wire('#acl-user','change',showPreview);wire('#acl-preview-refresh','click',showPreview);wire('#acl-preview-search','input',renderPreview);
            wire('#acl-save','click',async()=>{if(busy||editing||!preview)return;busy=true;root.querySelector('#acl-save').disabled=true;try{await request('PUT','',payload());window.dispatchEvent(new Event('ltvg-channel-access-changed'));close();}catch(e){error(e);dirty();}finally{busy=false;}});
        }
        function edit(rule) {
            dirty();editing=rule;
            const container=root.querySelector('#acl-editor');
            container.innerHTML=`<div class="acl-rule"><h3>${t('Edit rule')}</h3><label>${t('Rule name')}<input id="acl-name" type="text" maxlength="80" value="${esc(rule.Name)}"></label>
<label>${t('Visibility policy')}<select id="acl-mode" aria-label="${t('Visibility policy')}"><option value="selected" ${!rule.VisibleToAllUsers?'selected':''}>${t('Only selected users')}</option><option value="except" ${rule.VisibleToAllUsers?'selected':''}>${t('Everyone except selected users')}</option></select></label>
<p class="acl-muted">${t('Checked users are allowed in selected-user mode and denied in everyone-except mode. New users are excluded in selected-user mode.')}</p>
<div id="acl-rule-users" class="acl-list"></div>
<label>${t('Search channels by name, number or source')}<input id="acl-search" type="text"></label>
<label>${t('Source')}<select id="acl-source" aria-label="${t('Source')}"><option value="">${t('All sources')}</option>${[...new Set(state.Channels.map(c=>c.ServiceName||''))].filter(Boolean).sort().map(s=>`<option>${esc(s)}</option>`).join('')}</select></label>
<div class="acl-actions"><button id="acl-select">${t('Select filtered channels')}</button><button id="acl-clear">${t('Deselect filtered channels')}</button></div>
<div id="acl-channels" class="acl-list"></div><div id="acl-unresolved"></div>
<div class="acl-actions"><button id="acl-apply">${t('Apply rule to draft')}</button><button id="acl-cancel">${t('Cancel')}</button></div></div>`;
            function users() { const list=rule.VisibleToAllUsers?rule.DeniedUserIds:rule.AllowedUserIds;root.querySelector('#acl-rule-users').innerHTML=state.Users.map(u=>`<label><input type="checkbox" data-user="${u.Id}" ${list.includes(u.Id)&&(rule.VisibleToAllUsers||!rule.DeniedUserIds.includes(u.Id))?'checked':''} ${u.IsAdministrator?'disabled':''}>${esc(u.Name)} ${u.IsAdministrator?'· '+t('Administrator'):!u.HasLiveTvAccess?'· '+t('No Live TV access.'):''}</label>`).join('');root.querySelectorAll('[data-user]').forEach(n=>n.onchange=()=>{const key=rule.VisibleToAllUsers?'DeniedUserIds':'AllowedUserIds';rule[key]=rule[key].filter(id=>id!==n.dataset.user);if(n.checked){rule[key].push(n.dataset.user);if(!rule.VisibleToAllUsers)rule.DeniedUserIds=rule.DeniedUserIds.filter(id=>id!==n.dataset.user);}}); }
            function matches() {const q=root.querySelector('#acl-search').value.toLowerCase(),source=root.querySelector('#acl-source').value;return state.Channels.filter(c=>(!source||c.ServiceName===source)&&label(c).toLowerCase().includes(q));}
            function channels() {const items=matches();root.querySelector('#acl-channels').innerHTML=`<p class="acl-muted">${items.length} ${t('Channels')} · ${t('Showing up to 500 results; bulk selection applies to all matches.')}</p>`+items.slice(0,500).map(c=>`<label><input type="checkbox" data-channel="${c.ItemId}" ${rule.Channels.some(r=>same(r,c))?'checked':''}>${esc(label(c))}</label>`).join('');root.querySelectorAll('[data-channel]').forEach(n=>n.onchange=()=>{const channel=state.Channels.find(c=>c.ItemId===n.dataset.channel);rule.Channels=rule.Channels.filter(r=>!same(r,channel));if(n.checked)rule.Channels.push(copy(channel));});}
            function unresolved() {const refs=state.References.filter(r=>r.RuleId===rule.Id&&r.Status!=='resolved');root.querySelector('#acl-unresolved').innerHTML=refs.map(r=>`<label>${esc(r.Name)} · ${t(r.Status==='missing'?'Missing source':'Ambiguous source')}<select data-repair="${r.Index}"><option value="">${t('Keep saved reference')}</option><option value="remove">${t('Remove reference')}</option>${state.Channels.map(c=>`<option value="${c.ItemId}">${esc(label(c))}</option>`).join('')}</select></label>`).join('');}
            users();channels();unresolved();
            wire('#acl-mode','change',e=>{rule.VisibleToAllUsers=e.target.value==='except';if(rule.VisibleToAllUsers)rule.AllowedUserIds=[];else rule.DeniedUserIds=[];users();});wire('#acl-search','input',channels);wire('#acl-source','change',channels);
            wire('#acl-select','click',()=>{for(const c of matches())if(!rule.Channels.some(r=>same(r,c)))rule.Channels.push(copy(c));channels();});
            wire('#acl-clear','click',()=>{const selected=matches();rule.Channels=rule.Channels.filter(r=>!selected.some(c=>same(r,c)));channels();});
            wire('#acl-cancel','click',()=>{editing=null;render();});
            wire('#acl-apply','click',()=>{rule.Name=root.querySelector('#acl-name').value.trim();if(!rule.Name){error(Error(t('Enter a rule name.')));return;}const original=state.Rules.find(r=>r.Id===rule.Id);root.querySelectorAll('[data-repair]').forEach(n=>{if(!n.value||!original)return;const old=original.Channels[+n.dataset.repair];rule.Channels=rule.Channels.filter(r=>!same(r,old));if(n.value!=='remove'){const c=state.Channels.find(c=>c.ItemId===n.value);if(c&&!rule.Channels.some(r=>same(r,c)))rule.Channels.push(copy(c));}});state.Rules=state.Rules.filter(r=>r.Id!==rule.Id).concat(rule);editing=null;render();showPreview();});
            root.querySelector('#acl-name').focus();
        }
        async function showPreview() {
            if(editing||busy)return;dirty();const current=++generation;
            try{const result=await request('POST','/Preview',payload());if(current!==generation||!root.isConnected)return;preview=result;renderPreview();root.querySelector('#acl-save').disabled=false;}catch(e){if(current===generation)error(e);}
        }
        function renderPreview() {
            if(!preview)return;
            const q=root.querySelector('#acl-preview-search').value.toLowerCase(),items=preview.Channels.filter(c=>label(c).toLowerCase().includes(q));
            root.querySelector('#acl-preview').innerHTML=`<p>${preview.Channels.filter(c=>c.Allowed).length} ${t('Allowed')} · ${preview.Channels.filter(c=>!c.Allowed).length} ${t('Blocked')}</p>`+items.slice(0,500).map(c=>`<div class="acl-preview-row">${esc(label(c))} · ${t(c.Allowed?'Allowed':'Blocked')}${c.Rules.length?' · '+esc(c.Rules.join(', ')):!c.Allowed?' · '+t('Jellyfin permissions'):''}</div>`).join('')+`<h3>${t('Affected users')}</h3>`+preview.Users.map(u=>`<div>${esc(u.Name)} · ${u.DeniedCount} ${t('Blocked')}</div>`).join('')+`<h3>${t('Active playback')}</h3>`+(preview.ActivePlayback.map(p=>`<div>${esc(p.DeviceName)} · ${esc(p.Name)} · ${t(p.WillStop?'Will stop':'Will continue')}</div>`).join('')||t('No active playback.'));
        }
        try{state=await request('GET');render();root.querySelector('#acl-close').focus();await showPreview();}catch(e){panel.innerHTML=`<h2 id="acl-title">${t('Channel access')}</h2><p class="acl-error" role="alert">${esc(e.message)}</p><button id="acl-close">${t('Close')}</button>`;wire('#acl-close','click',close);}
    }
    window.LiveTvGroupsChannelAccess = { open };
})();
