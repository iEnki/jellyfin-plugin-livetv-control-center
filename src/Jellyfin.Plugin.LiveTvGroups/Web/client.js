/* Live-TV Groups – web client integration for Jellyfin 12.0 */
(function () {
    'use strict';

    if (window.__liveTvGroupsLoaded) {
        return;
    }
    window.__liveTvGroupsLoaded = true;

    var BUTTON_ID = 'ltvg-open-button';
    var PANEL_ID = 'ltvg-panel';
    var MODAL_ID = 'ltvg-modal';

    // Selectors of jellyfin-web 12.0. Keep all DOM assumptions here.
    var SELECTORS = {
        modernViewMenuButton: 'button[aria-controls="library-view-menu"]',
        modernViewMenu: '#library-view-menu',
        legacyTabsSlider: '.headerTabs .emby-tabs-slider',
        legacyTabButton: '.headerTabs .emby-tab-button',
        headers: ['.skinHeader', '.MuiAppBar-root'],
        livePages: ['#liveTvPage', '#liveTvSuggestedPage']
    };

    var state = { open: false, groupId: null, groupName: '', reorder: false, groupTab: 'channels', guideStart: null, guideTimer: null, guideTick: 0 };

    /* ---------- helpers ---------- */

    function client() {
        return window.ApiClient;
    }

    function isLiveTvRoute() {
        return /^#\/livetv(\?|$)/.test(window.location.hash);
    }

    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function api(method, path, body) {
        var apiClient = client();
        return fetch(apiClient.getUrl(path), {
            method: method,
            headers: {
                'Authorization': 'MediaBrowser Token="' + apiClient.accessToken() + '"',
                'Content-Type': 'application/json'
            },
            body: body === undefined ? undefined : JSON.stringify(body)
        }).then(function (response) {
            if (!response.ok) {
                return response.text().then(function (text) {
                    throw new Error(text || ('HTTP ' + response.status));
                });
            }
            return response.status === 204 ? null : response.json();
        });
    }

    function ensureStyles() {
        if (document.getElementById('ltvg-styles')) {
            return;
        }
        var link = document.createElement('link');
        link.id = 'ltvg-styles';
        link.rel = 'stylesheet';
        link.href = client().getUrl('LiveTvGroups/client.css');
        document.head.appendChild(link);
    }

    function formatTime(value) {
        var date = new Date(value);
        return isNaN(date) ? '' : date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }

    /* ---------- entry button ---------- */

    function ensureButton() {
        var existing = document.getElementById(BUTTON_ID);

        if (!isLiveTvRoute() || !client()) {
            if (existing) {
                existing.remove();
            }
            if (state.open) {
                closePanel();
            }
            return;
        }

        var modernButton = document.querySelector(SELECTORS.modernViewMenuButton);
        var legacySlider = document.querySelector(SELECTORS.legacyTabsSlider);
        if (existing) {
            // React or the legacy tab manager may re-render the toolbar; re-attach if our button got detached or misplaced.
            var placed = modernButton
                ? existing.previousElementSibling === modernButton
                : legacySlider
                    ? legacySlider.contains(existing)
                    : existing.classList.contains('ltvg-open-button-floating');
            if (!placed) {
                existing.remove();
                existing = null;
            }
        }
        if (existing) {
            existing.classList.toggle('ltvg-active', state.open);
            return;
        }

        var button = document.createElement('button');
        button.type = 'button';
        button.id = BUTTON_ID;
        button.textContent = 'Gruppen';
        button.addEventListener('click', function (event) {
            event.preventDefault();
            event.stopPropagation();
            if (state.open) {
                closePanel();
            } else {
                openPanel();
            }
        });

        if (modernButton) {
            button.className = 'ltvg-open-button ltvg-open-button-modern';
            modernButton.insertAdjacentElement('afterend', button);
        } else if (legacySlider) {
            button.className = 'ltvg-open-button ltvg-open-button-legacy';
            legacySlider.appendChild(button);
        } else if (SELECTORS.livePages.some(function (s) { return document.querySelector(s); })) {
            button.className = 'ltvg-open-button ltvg-open-button-floating';
            document.body.appendChild(button);
        } else {
            return;
        }

        button.classList.toggle('ltvg-active', state.open);
    }

    /* ---------- panel ---------- */

    function positionPanel(panel) {
        var top = 0;
        SELECTORS.headers.forEach(function (selector) {
            document.querySelectorAll(selector).forEach(function (header) {
                var rect = header.getBoundingClientRect();
                if (rect.height > 0 && rect.bottom > top) {
                    top = rect.bottom;
                }
            });
        });

        var left = 0;
        SELECTORS.livePages.forEach(function (selector) {
            var page = document.querySelector(selector);
            if (page) {
                var rect = page.getBoundingClientRect();
                if (rect.width > 0) {
                    left = Math.max(0, rect.left);
                }
            }
        });

        panel.style.top = top + 'px';
        panel.style.left = left + 'px';
    }

    function openPanel() {
        ensureStyles();
        state.open = true;
        state.groupId = null;
        state.reorder = false;

        var panel = document.getElementById(PANEL_ID);
        if (!panel) {
            panel = document.createElement('div');
            panel.id = PANEL_ID;
            panel.className = 'ltvg-panel';
            document.body.appendChild(panel);
        }
        positionPanel(panel);
        ensureButton();
        renderGroups();
    }

    function stopGuideTimer() {
        if (state.guideTimer) {
            window.clearInterval(state.guideTimer);
            state.guideTimer = null;
        }
    }

    function closePanel() {
        state.open = false;
        stopGuideTimer();
        var panel = document.getElementById(PANEL_ID);
        if (panel) {
            panel.remove();
        }
        closeModal();
        var button = document.getElementById(BUTTON_ID);
        if (button) {
            button.classList.remove('ltvg-active');
        }
    }

    function panelElement() {
        return document.getElementById(PANEL_ID);
    }

    function showError(error) {
        var panel = panelElement();
        if (!panel) {
            return;
        }
        var box = panel.querySelector('.ltvg-error');
        if (box) {
            box.textContent = 'Fehler: ' + (error && error.message ? error.message : error);
            box.hidden = false;
        }
    }

    function renderLoading(title) {
        var panel = panelElement();
        if (panel) {
            panel.innerHTML = '<div class="ltvg-header"><h2>' + escapeHtml(title) + '</h2></div>'
                + '<div class="ltvg-error" hidden></div><div class="ltvg-empty">Lädt…</div>';
        }
    }

    /* ---------- groups view ---------- */

    function renderGroups() {
        state.groupId = null;
        state.reorder = false;
        state.groupTab = 'channels';
        stopGuideTimer();
        renderLoading('Sendergruppen');

        Promise.all([
            api('GET', 'LiveTvGroups/Groups'),
            api('GET', 'LiveTvGroups/GuideFilter').catch(function () { return { GroupId: null }; })
        ]).then(function (results) {
            var groups = results[0];
            var activeGuideGroup = results[1] ? normalizeId(results[1].GroupId) : null;
            var panel = panelElement();
            if (!panel) {
                return;
            }

            var html = '<div class="ltvg-header">'
                + '<h2>Sendergruppen</h2>'
                + '<div class="ltvg-actions">'
                + '<button type="button" class="ltvg-btn ltvg-btn-primary" data-action="create">Neue Gruppe</button>'
                + '</div></div>'
                + '<div class="ltvg-error" hidden></div>';

            if (groups.length) {
                var activeGroup = groups.filter(function (g) { return normalizeId(g.Id) === activeGuideGroup; })[0];
                html += '<div class="ltvg-guide-filter">📺 TV-Programmführer (Fire TV, Android TV, …): '
                    + (activeGroup
                        ? '<b>' + escapeHtml(activeGroup.Name) + '</b> <button type="button" class="ltvg-btn ltvg-btn-small" data-action="guide-filter" data-id="">Alle Sender anzeigen</button>'
                        : '<b>Alle Sender</b> – mit 📺 an einer Gruppe nur deren Sender anzeigen')
                    + '</div>';
            }

            if (!groups.length) {
                html += '<div class="ltvg-empty">Noch keine Gruppen. Lege mit „Neue Gruppe“ deine erste Gruppe an.</div>';
            } else {
                html += '<div class="ltvg-hint">Tipp: Gruppen per Ziehen umsortieren.</div><div class="ltvg-grid" data-list="groups">';
                groups.forEach(function (group) {
                    html += '<div class="ltvg-card ltvg-group-card" draggable="true" data-id="' + escapeHtml(group.Id) + '">'
                        + '<button type="button" class="ltvg-card-main" data-action="open" data-id="' + escapeHtml(group.Id) + '" data-name="' + escapeHtml(group.Name) + '">'
                        + '<span class="ltvg-card-title">' + escapeHtml(group.Name) + '</span>'
                        + '<span class="ltvg-card-sub">' + group.ChannelCount + ' Sender</span>'
                        + '</button>'
                        + '<div class="ltvg-card-tools">'
                        + '<button type="button" class="ltvg-icon-btn' + (normalizeId(group.Id) === activeGuideGroup ? ' ltvg-icon-active' : '') + '" title="Im TV-Programmführer anzeigen" data-action="guide-filter" data-id="' + escapeHtml(normalizeId(group.Id) === activeGuideGroup ? '' : group.Id) + '">📺</button>'
                        + '<button type="button" class="ltvg-icon-btn" title="Umbenennen" data-action="rename" data-id="' + escapeHtml(group.Id) + '" data-name="' + escapeHtml(group.Name) + '">✎</button>'
                        + '<button type="button" class="ltvg-icon-btn" title="Löschen" data-action="delete" data-id="' + escapeHtml(group.Id) + '" data-name="' + escapeHtml(group.Name) + '">🗑</button>'
                        + '</div></div>';
                });
                html += '</div>';
            }

            panel.innerHTML = html;
            enableDragSort(panel.querySelector('[data-list="groups"]'), function (ids) {
                return api('PUT', 'LiveTvGroups/Groups/Order', ids);
            });
        }).catch(showError);
    }

    /* ---------- group view ---------- */

    function groupHeader(groupName, actionsHtml) {
        return '<div class="ltvg-header">'
            + '<button type="button" class="ltvg-btn" data-action="back">‹ Gruppen</button>'
            + '<h2>' + escapeHtml(groupName) + '</h2>'
            + '<div class="ltvg-tabs" role="tablist">'
            + '<button type="button" role="tab" class="ltvg-tab' + (state.groupTab === 'channels' ? ' ltvg-tab-active' : '') + '" data-action="tab" data-tab="channels">Sender</button>'
            + '<button type="button" role="tab" class="ltvg-tab' + (state.groupTab === 'guide' ? ' ltvg-tab-active' : '') + '" data-action="tab" data-tab="guide">Programm</button>'
            + '</div>'
            + '<div class="ltvg-actions">' + (actionsHtml || '') + '</div></div>'
            + '<div class="ltvg-error" hidden></div>';
    }

    function renderGroup(groupId, groupName) {
        if (state.groupId !== groupId) {
            state.groupTab = 'channels';
            state.guideStart = null;
        }
        state.groupId = groupId;
        state.groupName = groupName;
        if (state.groupTab === 'guide') {
            renderGuide();
            return;
        }
        stopGuideTimer();
        renderLoading(groupName);

        api('GET', 'LiveTvGroups/Groups/' + groupId + '/Channels').then(function (result) {
            var panel = panelElement();
            if (!panel || state.groupId !== groupId) {
                return;
            }

            var items = result.Items || [];
            var html = groupHeader(groupName,
                (items.length > 1
                    ? '<button type="button" class="ltvg-btn' + (state.reorder ? ' ltvg-btn-primary' : '') + '" data-action="toggle-reorder">'
                        + (state.reorder ? 'Fertig' : 'Reihenfolge ändern') + '</button>'
                    : '')
                + '<button type="button" class="ltvg-btn ltvg-btn-primary" data-action="edit-channels">Sender auswählen</button>');

            if (!items.length) {
                html += '<div class="ltvg-empty">Diese Gruppe enthält noch keine Sender.</div>';
            } else {
                if (state.reorder) {
                    html += '<div class="ltvg-hint">Sender per Ziehen umsortieren, dann „Fertig“.</div>';
                }
                html += '<div class="ltvg-grid ltvg-channel-grid" data-list="channels">';
                items.forEach(function (item) {
                    var image = item.ImageTags && item.ImageTags.Primary
                        ? client().getScaledImageUrl(item.Id, { type: 'Primary', maxHeight: 160, tag: item.ImageTags.Primary })
                        : null;
                    var program = item.CurrentProgram;
                    var programText = program
                        ? escapeHtml(program.Name) + (program.StartDate ? ' <span class="ltvg-time">' + formatTime(program.StartDate) + '–' + formatTime(program.EndDate) + '</span>' : '')
                        : '';

                    html += '<div class="ltvg-card ltvg-channel-card"' + (state.reorder ? ' draggable="true"' : '') + ' data-id="' + escapeHtml(item.Id) + '">'
                        + '<button type="button" class="ltvg-card-main" data-action="play" data-id="' + escapeHtml(item.Id) + '"' + (state.reorder ? ' disabled' : '') + '>'
                        + '<span class="ltvg-logo">' + (image ? '<img loading="lazy" alt="" src="' + escapeHtml(image) + '">' : '<span>' + escapeHtml((item.Name || '?').charAt(0)) + '</span>') + '</span>'
                        + '<span class="ltvg-card-title">' + (item.ChannelNumber ? '<span class="ltvg-number">' + escapeHtml(item.ChannelNumber) + '</span> ' : '') + escapeHtml(item.Name) + '</span>'
                        + '<span class="ltvg-card-sub">' + programText + '</span>'
                        + '</button></div>';
                });
                html += '</div>';
            }

            panel.innerHTML = html;
            if (state.reorder) {
                enableDragSort(panel.querySelector('[data-list="channels"]'), function (ids) {
                    return api('PUT', 'LiveTvGroups/Groups/' + groupId + '/Channels', ids);
                });
            }
            panel.dataset.channelIds = JSON.stringify(items.map(function (i) { return i.Id; }));
        }).catch(showError);
    }

    /* ---------- program guide ---------- */

    var GUIDE_SLOT_MINUTES = 30;

    function normalizeId(id) {
        return id ? String(id).replace(/-/g, '').toLowerCase() : null;
    }

    function guideHours() {
        return window.innerWidth < 700 ? 3 : 6;
    }

    function pixelsPerMinute() {
        return window.innerWidth < 700 ? 4 : 5;
    }

    function roundToSlot(date) {
        var d = new Date(date.getTime());
        d.setSeconds(0, 0);
        d.setMinutes(d.getMinutes() - (d.getMinutes() % GUIDE_SLOT_MINUTES));
        return d;
    }

    function formatDay(date) {
        return date.toLocaleDateString([], { weekday: 'short', day: '2-digit', month: '2-digit' });
    }

    function renderGuide(keepScroll) {
        var groupId = state.groupId;
        var groupName = state.groupName;
        var panel = panelElement();
        if (!panel) {
            return;
        }

        if (!state.guideStart) {
            state.guideStart = roundToSlot(new Date(Date.now() - 30 * 60000)).getTime();
        }

        var start = new Date(state.guideStart);
        var end = new Date(state.guideStart + guideHours() * 3600000);
        var oldScroller = panel.querySelector('.ltvg-guide-scroll');
        var previousScroll = keepScroll && oldScroller ? oldScroller.scrollLeft : null;

        if (!keepScroll) {
            panel.innerHTML = groupHeader(groupName) + '<div class="ltvg-empty">Lädt Programm…</div>';
        }

        api('GET', 'LiveTvGroups/Groups/' + groupId + '/Guide?start=' + encodeURIComponent(start.toISOString()) + '&end=' + encodeURIComponent(end.toISOString())).then(function (guide) {
            panel = panelElement();
            if (!panel || state.groupId !== groupId || state.groupTab !== 'guide') {
                return;
            }

            var ppm = pixelsPerMinute();
            var totalMinutes = (end.getTime() - start.getTime()) / 60000;
            var width = totalMinutes * ppm;
            var channels = guide.Channels || [];
            var programsByChannel = {};
            (guide.Programs || []).forEach(function (program) {
                var key = normalizeId(program.ChannelId);
                (programsByChannel[key] = programsByChannel[key] || []).push(program);
            });

            var today = new Date();
            today.setHours(0, 0, 0, 0);
            var dayOptions = '';
            for (var d = 0; d < 7; d++) {
                var day = new Date(today.getTime() + d * 86400000);
                var selected = start.getTime() >= day.getTime() && start.getTime() < day.getTime() + 86400000;
                dayOptions += '<option value="' + day.getTime() + '"' + (selected ? ' selected' : '') + '>'
                    + (d === 0 ? 'Heute' : d === 1 ? 'Morgen' : formatDay(day)) + '</option>';
            }

            var html = groupHeader(groupName)
                + '<div class="ltvg-guide-toolbar">'
                + '<button type="button" class="ltvg-btn" data-action="guide-shift" data-hours="-3">‹ früher</button>'
                + '<button type="button" class="ltvg-btn" data-action="guide-now">Jetzt</button>'
                + '<button type="button" class="ltvg-btn" data-action="guide-shift" data-hours="3">später ›</button>'
                + '<select class="ltvg-input ltvg-guide-day" data-action="guide-day" aria-label="Tag">' + dayOptions + '</select>'
                + '<span class="ltvg-guide-range">' + escapeHtml(formatDay(start) + ' ' + formatTime(start) + '–' + formatTime(end)) + '</span>'
                + '</div>';

            if (!channels.length) {
                panel.innerHTML = html + '<div class="ltvg-empty">Diese Gruppe enthält noch keine Sender.</div>';
                return;
            }

            html += '<div class="ltvg-guide-scroll"><div class="ltvg-guide" style="width:calc(var(--ltvg-guide-ch) + ' + width + 'px)">';

            html += '<div class="ltvg-guide-row ltvg-guide-timeline"><div class="ltvg-guide-ch"></div><div class="ltvg-guide-cells" style="width:' + width + 'px">';
            for (var m = 0; m < totalMinutes; m += GUIDE_SLOT_MINUTES) {
                html += '<span class="ltvg-guide-slot" style="left:' + (m * ppm) + 'px;width:' + (GUIDE_SLOT_MINUTES * ppm) + 'px">'
                    + formatTime(new Date(start.getTime() + m * 60000)) + '</span>';
            }
            html += '</div></div>';

            var now = Date.now();
            var hasPrograms = false;
            channels.forEach(function (channel) {
                var image = channel.ImageTags && channel.ImageTags.Primary
                    ? client().getScaledImageUrl(channel.Id, { type: 'Primary', maxHeight: 80, tag: channel.ImageTags.Primary })
                    : null;
                html += '<div class="ltvg-guide-row">'
                    + '<button type="button" class="ltvg-guide-ch" data-action="play" data-id="' + escapeHtml(channel.Id) + '" title="' + escapeHtml(channel.Name) + ' abspielen">'
                    + (image ? '<img alt="" loading="lazy" src="' + escapeHtml(image) + '">' : '')
                    + '<span class="ltvg-guide-ch-name">' + (channel.ChannelNumber ? '<span class="ltvg-number">' + escapeHtml(channel.ChannelNumber) + '</span> ' : '') + escapeHtml(channel.Name) + '</span>'
                    + '</button><div class="ltvg-guide-cells" style="width:' + width + 'px">';

                var programs = programsByChannel[normalizeId(channel.Id)] || [];
                if (!programs.length) {
                    html += '<span class="ltvg-guide-noprog">Keine Programmdaten</span>';
                }

                programs.forEach(function (program) {
                    var programStart = new Date(program.StartDate).getTime();
                    var programEnd = new Date(program.EndDate).getTime();
                    var ps = Math.max(programStart, start.getTime());
                    var pe = Math.min(programEnd, end.getTime());
                    if (!(pe > ps)) {
                        return;
                    }
                    hasPrograms = true;

                    var classes = 'ltvg-guide-prog';
                    if (programStart <= now && programEnd > now) {
                        classes += ' ltvg-guide-live';
                    }
                    if (program.IsMovie) {
                        classes += ' ltvg-cat-movie';
                    } else if (program.IsSports) {
                        classes += ' ltvg-cat-sports';
                    } else if (program.IsNews) {
                        classes += ' ltvg-cat-news';
                    } else if (program.IsKids) {
                        classes += ' ltvg-cat-kids';
                    }

                    var times = formatTime(program.StartDate) + '–' + formatTime(program.EndDate);
                    var label = (program.Name || '') + (program.EpisodeTitle ? ' – ' + program.EpisodeTitle : '');
                    html += '<button type="button" class="' + classes + '"'
                        + ' style="left:' + ((ps - start.getTime()) / 60000 * ppm) + 'px;width:' + Math.max(2, (pe - ps) / 60000 * ppm - 2) + 'px"'
                        + ' data-action="program" data-id="' + escapeHtml(program.Id) + '" title="' + escapeHtml(label + ' (' + times + ')') + '">'
                        + '<span class="ltvg-guide-prog-title">' + (program.TimerId ? '<span class="ltvg-guide-rec">●</span> ' : '') + escapeHtml(program.Name) + '</span>'
                        + '<span class="ltvg-guide-prog-time">' + times + (program.EpisodeTitle ? ' · ' + escapeHtml(program.EpisodeTitle) : '') + '</span>'
                        + '</button>';
                });
                html += '</div></div>';
            });

            html += '<div class="ltvg-guide-now" hidden></div></div></div>';
            if (!hasPrograms) {
                html += '<div class="ltvg-hint">Für diesen Zeitraum liefern die Sender keine Programmdaten (EPG). Prüfe die Guide-Daten unter Dashboard → Live-TV.</div>';
            }

            panel.innerHTML = html;

            var updateNowLine = function () {
                var line = panel.querySelector('.ltvg-guide-now');
                var t = Date.now();
                if (!line || t < start.getTime() || t > end.getTime()) {
                    if (line) {
                        line.hidden = true;
                    }
                    return null;
                }
                var offset = (t - start.getTime()) / 60000 * ppm;
                line.style.left = 'calc(var(--ltvg-guide-ch) + ' + offset + 'px)';
                line.hidden = false;
                return offset;
            };

            var scroller = panel.querySelector('.ltvg-guide-scroll');
            var nowOffset = updateNowLine();
            if (previousScroll !== null) {
                scroller.scrollLeft = previousScroll;
            } else if (nowOffset !== null) {
                scroller.scrollLeft = Math.max(0, nowOffset - 120);
            }

            // Move the "now" line every minute, reload program data every 5 minutes.
            stopGuideTimer();
            state.guideTick = 0;
            state.guideTimer = window.setInterval(function () {
                if (!panelElement() || state.groupTab !== 'guide' || state.groupId !== groupId) {
                    stopGuideTimer();
                    return;
                }
                state.guideTick++;
                if (state.guideTick % 5 === 0) {
                    renderGuide(true);
                } else {
                    updateNowLine();
                }
            }, 60000);
        }).catch(showError);
    }


    function play(itemId) {
        var apiClient = client();
        api('GET', 'Sessions?deviceId=' + encodeURIComponent(apiClient.deviceId())).then(function (sessions) {
            if (!sessions || !sessions.length) {
                throw new Error('no session');
            }
            return api('POST', 'Sessions/' + sessions[0].Id + '/Playing?playCommand=PlayNow&itemIds=' + encodeURIComponent(itemId));
        }).catch(function () {
            window.location.hash = '#/details?id=' + encodeURIComponent(itemId);
        });
    }

    /* ---------- drag sorting ---------- */

    function enableDragSort(list, save) {
        if (!list) {
            return;
        }
        var dragged = null;

        list.addEventListener('dragstart', function (event) {
            dragged = event.target.closest('.ltvg-card');
            if (dragged) {
                dragged.classList.add('ltvg-dragging');
                event.dataTransfer.effectAllowed = 'move';
                event.dataTransfer.setData('text/plain', dragged.dataset.id);
            }
        });

        list.addEventListener('dragover', function (event) {
            if (!dragged) {
                return;
            }
            event.preventDefault();
            var target = event.target.closest('.ltvg-card');
            if (!target || target === dragged) {
                return;
            }
            var rect = target.getBoundingClientRect();
            var after = (event.clientY - rect.top) / rect.height > 0.5
                || ((event.clientY - rect.top) / rect.height > 0.2 && (event.clientX - rect.left) / rect.width > 0.5);
            list.insertBefore(dragged, after ? target.nextSibling : target);
        });

        list.addEventListener('dragend', function () {
            if (!dragged) {
                return;
            }
            dragged.classList.remove('ltvg-dragging');
            dragged = null;
            var ids = Array.prototype.map.call(list.querySelectorAll('.ltvg-card'), function (card) {
                return card.dataset.id;
            });
            save(ids).catch(showError);
        });
    }

    /* ---------- modals ---------- */

    function closeModal() {
        var modal = document.getElementById(MODAL_ID);
        if (modal) {
            modal.remove();
        }
    }

    function openModal(innerHtml) {
        ensureStyles();
        closeModal();
        var modal = document.createElement('div');
        modal.id = MODAL_ID;
        modal.className = 'ltvg-modal-backdrop';
        modal.innerHTML = '<div class="ltvg-modal" role="dialog" aria-modal="true">' + innerHtml + '</div>';
        modal.addEventListener('click', function (event) {
            if (event.target === modal) {
                closeModal();
            }
        });
        modal.addEventListener('keydown', function (event) {
            if (event.key === 'Escape') {
                closeModal();
            }
        });
        document.body.appendChild(modal);
        return modal;
    }

    function askName(title, initial) {
        return new Promise(function (resolve) {
            var modal = openModal('<h3>' + escapeHtml(title) + '</h3>'
                + '<form class="ltvg-form"><input class="ltvg-input" type="text" maxlength="100" required value="' + escapeHtml(initial || '') + '">'
                + '<div class="ltvg-modal-actions"><button type="button" class="ltvg-btn" data-modal="cancel">Abbrechen</button>'
                + '<button type="submit" class="ltvg-btn ltvg-btn-primary">Speichern</button></div></form>');
            var input = modal.querySelector('input');
            input.focus();
            input.select();
            modal.querySelector('[data-modal="cancel"]').addEventListener('click', function () {
                closeModal();
                resolve(null);
            });
            modal.querySelector('form').addEventListener('submit', function (event) {
                event.preventDefault();
                var value = input.value.trim();
                closeModal();
                resolve(value || null);
            });
        });
    }

    function confirmDialog(text) {
        return new Promise(function (resolve) {
            var modal = openModal('<p>' + escapeHtml(text) + '</p>'
                + '<div class="ltvg-modal-actions"><button type="button" class="ltvg-btn" data-modal="cancel">Abbrechen</button>'
                + '<button type="button" class="ltvg-btn ltvg-btn-danger" data-modal="ok">Löschen</button></div>');
            modal.querySelector('[data-modal="cancel"]').addEventListener('click', function () {
                closeModal();
                resolve(false);
            });
            var ok = modal.querySelector('[data-modal="ok"]');
            ok.focus();
            ok.addEventListener('click', function () {
                closeModal();
                resolve(true);
            });
        });
    }

    function editChannels(groupId, groupName) {
        var panel = panelElement();
        var currentIds = [];
        try {
            currentIds = JSON.parse((panel && panel.dataset.channelIds) || '[]');
        } catch (e) {
            currentIds = [];
        }

        var modal = openModal('<h3>Sender für „' + escapeHtml(groupName) + '“</h3>'
            + '<input class="ltvg-input" type="search" placeholder="Sender suchen…" data-modal="search">'
            + '<div class="ltvg-picker-count" data-modal="count"></div>'
            + '<div class="ltvg-picker" data-modal="list"><div class="ltvg-empty">Lädt…</div></div>'
            + '<div class="ltvg-modal-actions"><button type="button" class="ltvg-btn" data-modal="cancel">Abbrechen</button>'
            + '<button type="button" class="ltvg-btn ltvg-btn-primary" data-modal="save">Speichern</button></div>');
        modal.querySelector('.ltvg-modal').classList.add('ltvg-modal-wide');

        var selected = new Set(currentIds);
        var listElement = modal.querySelector('[data-modal="list"]');
        var countElement = modal.querySelector('[data-modal="count"]');
        var searchElement = modal.querySelector('[data-modal="search"]');

        function updateCount() {
            countElement.textContent = selected.size + ' ausgewählt';
        }

        modal.querySelector('[data-modal="cancel"]').addEventListener('click', closeModal);

        api('GET', 'LiveTv/Channels?userId=' + encodeURIComponent(client().getCurrentUserId())
            + '&enableImages=false&addCurrentProgram=false&enableUserData=false').then(function (result) {
            var channels = result.Items || [];
            listElement.innerHTML = channels.map(function (channel) {
                return '<label class="ltvg-picker-row" data-search="' + escapeHtml(((channel.ChannelNumber || '') + ' ' + channel.Name).toLowerCase()) + '">'
                    + '<input type="checkbox" value="' + escapeHtml(channel.Id) + '"' + (selected.has(channel.Id) ? ' checked' : '') + '>'
                    + '<span class="ltvg-number">' + escapeHtml(channel.ChannelNumber || '') + '</span>'
                    + '<span>' + escapeHtml(channel.Name) + '</span></label>';
            }).join('') || '<div class="ltvg-empty">Keine Sender gefunden.</div>';
            updateCount();

            listElement.addEventListener('change', function (event) {
                if (event.target.type === 'checkbox') {
                    if (event.target.checked) {
                        selected.add(event.target.value);
                    } else {
                        selected.delete(event.target.value);
                    }
                    updateCount();
                }
            });

            searchElement.addEventListener('input', function () {
                var term = searchElement.value.trim().toLowerCase();
                listElement.querySelectorAll('.ltvg-picker-row').forEach(function (row) {
                    row.hidden = term !== '' && row.dataset.search.indexOf(term) === -1;
                });
            });
            searchElement.focus();

            modal.querySelector('[data-modal="save"]').addEventListener('click', function () {
                // Keep the existing order, append newly selected channels in list order.
                var ids = currentIds.filter(function (id) { return selected.has(id); });
                channels.forEach(function (channel) {
                    if (selected.has(channel.Id) && ids.indexOf(channel.Id) === -1) {
                        ids.push(channel.Id);
                    }
                });
                api('PUT', 'LiveTvGroups/Groups/' + groupId + '/Channels', ids).then(function () {
                    closeModal();
                    renderGroup(groupId, groupName);
                }).catch(function (error) {
                    closeModal();
                    showError(error);
                });
            });
        }).catch(function (error) {
            listElement.innerHTML = '<div class="ltvg-error">Fehler: ' + escapeHtml(error.message) + '</div>';
        });
    }

    /* ---------- actions ---------- */

    document.addEventListener('click', function (event) {
        var panel = panelElement();
        var target = event.target.closest('[data-action]');
        if (!panel || !target || !panel.contains(target)) {
            return;
        }

        var id = target.dataset.id;
        var name = target.dataset.name;

        switch (target.dataset.action) {
            case 'create':
                askName('Neue Gruppe', '').then(function (value) {
                    if (value) {
                        api('POST', 'LiveTvGroups/Groups', { Name: value }).then(function (group) {
                            renderGroup(group.Id, group.Name);
                        }).catch(showError);
                    }
                });
                break;
            case 'rename':
                askName('Gruppe umbenennen', name).then(function (value) {
                    if (value && value !== name) {
                        api('PUT', 'LiveTvGroups/Groups/' + id, { Name: value }).then(renderGroups).catch(showError);
                    }
                });
                break;
            case 'delete':
                confirmDialog('Gruppe „' + name + '“ löschen? Die Sender selbst bleiben erhalten.').then(function (ok) {
                    if (ok) {
                        api('DELETE', 'LiveTvGroups/Groups/' + id).then(renderGroups).catch(showError);
                    }
                });
                break;
            case 'open':
                renderGroup(id, name);
                break;
            case 'back':
                renderGroups();
                break;
            case 'tab':
                state.groupTab = target.dataset.tab;
                state.reorder = false;
                renderGroup(state.groupId, state.groupName);
                break;
            case 'guide-shift':
                state.guideStart += Number(target.dataset.hours) * 3600000;
                renderGuide();
                break;
            case 'guide-now':
                state.guideStart = null;
                renderGuide();
                break;
            case 'guide-day':
                return;
            case 'program':
                window.location.hash = '#/details?id=' + encodeURIComponent(id);
                break;
            case 'guide-filter':
                api('PUT', 'LiveTvGroups/GuideFilter', { GroupId: id || null }).then(renderGroups).catch(showError);
                break;
            case 'toggle-reorder':
                state.reorder = !state.reorder;
                renderGroup(state.groupId, state.groupName);
                break;
            case 'edit-channels':
                editChannels(state.groupId, state.groupName);
                break;
            case 'play':
                play(id);
                break;
            default:
                return;
        }

        event.preventDefault();
    });

    document.addEventListener('change', function (event) {
        var panel = panelElement();
        var target = event.target;
        if (!panel || !panel.contains(target) || target.dataset.action !== 'guide-day') {
            return;
        }
        var dayStart = Number(target.value);
        var today = new Date();
        today.setHours(0, 0, 0, 0);
        // Today starts at the current time, other days in the evening.
        state.guideStart = dayStart === today.getTime() ? null : dayStart + 18 * 3600000;
        renderGuide();
    });

    // Switching to another Live TV view closes the groups panel.
    document.addEventListener('click', function (event) {
        if (!state.open) {
            return;
        }
        if (event.target.closest(SELECTORS.modernViewMenu + ' [role="menuitem"]')
            || event.target.closest(SELECTORS.legacyTabButton)) {
            closePanel();
        }
    }, true);

    window.addEventListener('hashchange', function () {
        if (!isLiveTvRoute()) {
            closePanel();
        }
        scheduleEnsure();
    });

    window.addEventListener('resize', function () {
        var panel = panelElement();
        if (panel) {
            positionPanel(panel);
        }
    });

    /* ---------- DOM observation ---------- */

    var scheduled = false;

    function scheduleEnsure() {
        if (scheduled) {
            return;
        }
        scheduled = true;
        window.requestAnimationFrame(function () {
            scheduled = false;
            try {
                ensureButton();
            } catch (e) {
                console.warn('[LiveTvGroups]', e);
            }
        });
    }

    function start() {
        new MutationObserver(scheduleEnsure).observe(document.body, { childList: true, subtree: true });
        scheduleEnsure();
    }

    if (document.body) {
        start();
    } else {
        document.addEventListener('DOMContentLoaded', start);
    }
})();
