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

    var state = { open: false, groupId: null, groupName: '', reorder: false };

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

    function closePanel() {
        state.open = false;
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
        renderLoading('Sendergruppen');

        api('GET', 'LiveTvGroups/Groups').then(function (groups) {
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

    function renderGroup(groupId, groupName) {
        state.groupId = groupId;
        state.groupName = groupName;
        renderLoading(groupName);

        api('GET', 'LiveTvGroups/Groups/' + groupId + '/Channels').then(function (result) {
            var panel = panelElement();
            if (!panel || state.groupId !== groupId) {
                return;
            }

            var items = result.Items || [];
            var html = '<div class="ltvg-header">'
                + '<button type="button" class="ltvg-btn" data-action="back">‹ Gruppen</button>'
                + '<h2>' + escapeHtml(groupName) + '</h2>'
                + '<div class="ltvg-actions">'
                + (items.length > 1
                    ? '<button type="button" class="ltvg-btn' + (state.reorder ? ' ltvg-btn-primary' : '') + '" data-action="toggle-reorder">'
                        + (state.reorder ? 'Fertig' : 'Reihenfolge ändern') + '</button>'
                    : '')
                + '<button type="button" class="ltvg-btn ltvg-btn-primary" data-action="edit-channels">Sender auswählen</button>'
                + '</div></div>'
                + '<div class="ltvg-error" hidden></div>';

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
