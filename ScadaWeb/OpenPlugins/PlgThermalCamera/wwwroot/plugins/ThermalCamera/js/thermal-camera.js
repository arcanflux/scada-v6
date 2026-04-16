// Thermal Camera Plugin - Real-time table with channel data from .map file
// Плагин тепловых камер - Таблица с данными каналов в реальном времени из файла .map

var thermalCamera = (function () {
    var UPDATE_INTERVAL = 1000;
    var CHAT_PREVIEW_LEN = 60;

    var items = [];
    var userData = {};
    var viewID = 0;
    var isAdmin = false;
    var updateTimer = null;
    var districtSortAsc = true;
    var searchQuery = "";
    var selectedDistricts = {};

    // Chat state
    // chatByItem[itemId] = array of message objects {id, timestampMs, author, text, kind}
    var chatByItem = {};
    var chatCursor = 0;
    var chatHistoryLoaded = {};
    // Single chat panel state (only one panel can be open at a time)
    var chatPanels = {};                // itemId -> { el }  (max 1 entry)
    var activeChatId = null;            // itemId of the open panel (or null)
    var selectedMessageId = null;       // currently selected message for admin delete
    var floodStateByItem = {};          // itemId -> "flood700" | "flood200" | null

    function init() {
        var itemsEl = document.getElementById("tcItems");
        var userDataEl = document.getElementById("tcUserData");
        if (!itemsEl) return;

        items = JSON.parse(itemsEl.textContent) || [];
        userData = userDataEl ? JSON.parse(userDataEl.textContent) || {} : {};

        // viewID is read from data-view-id on .tc-container so the Razor page
        // does not need inline C# inside a <script> block.
        var containerEl = document.querySelector(".tc-container");
        viewID = containerEl ? parseInt(containerEl.getAttribute("data-view-id"), 10) || 0 : 0;
        isAdmin = containerEl ? containerEl.getAttribute("data-is-admin") === "1" : false;

        sortItemsByDistrict();
        renderTable();
        bindHeaderSort();
        updateSortIndicator();
        bindSearch();
        bindDistrictFilter();
        applyFilter();
        requestData();
        startAutoUpdate();
        bindChatKeyboard();
        window.addEventListener("resize", repositionChat);
    }

    function bindSearch() {
        var searchInput = document.getElementById("tcSearchInput");
        if (!searchInput) return;
        searchInput.addEventListener("input", function () {
            searchQuery = this.value;
            applyFilter();
        });
    }

    function bindDistrictFilter() {
        var checkboxes = document.querySelectorAll(".tc-district-cb");
        for (var i = 0; i < checkboxes.length; i++) {
            var cb = checkboxes[i];
            var v = parseInt(cb.value);
            if (cb.checked) selectedDistricts[v] = true;
            cb.addEventListener("change", function () {
                var val = parseInt(this.value);
                if (this.checked) selectedDistricts[val] = true;
                else delete selectedDistricts[val];
                updateDistrictLabel();
                applyFilter();
            });
        }
        updateDistrictLabel();
    }

    function updateDistrictLabel() {
        var label = document.getElementById("tcDistrictLabel");
        if (!label) return;
        var total = document.querySelectorAll(".tc-district-cb").length;
        var selected = 0;
        for (var k in selectedDistricts) {
            if (selectedDistricts[k]) selected++;
        }
        label.textContent = "Районы " + selected + "/" + total;
    }

    function applyFilter() {
        var q = (searchQuery || "").trim().toLowerCase();
        var rows = document.querySelectorAll("#tbodyThermalCameras tr");
        for (var i = 0; i < rows.length; i++) {
            var row = rows[i];
            var itemId = parseInt(row.getAttribute("data-item-id"));
            var item = null;
            for (var j = 0; j < items.length; j++) {
                if (items[j].id === itemId) { item = items[j]; break; }
            }
            if (!item) continue;
            var nameMatch = item.name && item.name.toLowerCase().indexOf(q) >= 0;
            var addrMatch = item.descr && item.descr.toLowerCase().indexOf(q) >= 0;
            var searchOk = !q || nameMatch || addrMatch;
            var districtOk = selectedDistricts[item.districtNumber || 0] === true;
            row.style.display = (searchOk && districtOk) ? "" : "none";
        }
        updateChatDistrictNotice();
    }

    function sortItemsByDistrict() {
        items.sort(function (a, b) {
            var da = a.districtNumber || 0;
            var db = b.districtNumber || 0;
            return districtSortAsc ? da - db : db - da;
        });
    }

    function bindHeaderSort() {
        var th = document.querySelector("#tblThermalCameras th.tc-col-district");
        if (!th) return;
        th.style.cursor = "pointer";
        th.addEventListener("click", function () {
            districtSortAsc = !districtSortAsc;
            sortItemsByDistrict();
            renderTable();
            updateSortIndicator();
            applyFilter();
            requestData();
        });
    }

    function updateSortIndicator() {
        var th = document.querySelector("#tblThermalCameras th.tc-col-district");
        if (!th) return;
        var existing = th.querySelector(".tc-sort-indicator");
        if (existing) existing.remove();
        var span = document.createElement("span");
        span.className = "tc-sort-indicator ms-1";
        span.innerHTML = districtSortAsc
            ? '<i class="fa-solid fa-arrow-down-short-wide"></i>'
            : '<i class="fa-solid fa-arrow-up-short-wide"></i>';
        th.appendChild(span);
    }


    function renderTable() {
        var tbody = document.getElementById("tbodyThermalCameras");
        if (!tbody) return;

        var html = "";
        for (var i = 0; i < items.length; i++) {
            var item = items[i];
            var ud = userData[item.id] || { comment: "", isCommissioned: false };

            html += '<tr data-item-id="' + item.id + '">';

            // 1. District number
            html += '<td class="tc-col-district text-center">' +
                '<span class="badge bg-secondary">' + escapeHtml(String(item.districtNumber || "—")) + '</span></td>';

            // 2. Object name
            html += '<td class="tc-col-name">' + escapeHtml(item.name) + '</td>';

            // 3. Address (descr) + photo button
            html += '<td class="tc-col-address">' +
                '<span class="tc-address-text">' + escapeHtml(item.descr) + '</span>';
            if (item.photoUrl) {
                html += ' <button class="btn btn-sm btn-outline-primary tc-photo-btn" ' +
                    'onclick="thermalCamera.showPhoto(\'' + escapeAttr(item.photoUrl) + '\', \'' +
                    escapeAttr(item.name) + '\')" title="Фото">' +
                    '<i class="fa-solid fa-camera"></i></button>';
            }
            html += '</td>';

            // 4. Online status
            html += '<td class="tc-col-online text-center">' +
                '<span id="online-' + item.id + '" class="tc-online-indicator tc-status-unknown">' +
                '<i class="fa-solid fa-circle"></i> <span class="tc-online-text">\u2014</span></span></td>';

            // 5. Flooding status: two signal blocks (200mm yellow, 700mm red)
            html += '<td class="tc-col-flooding"><div class="tc-flooding-container">';

            // 200mm signal
            if (item.flood200CnlNum > 0 || item.temp200CnlNum > 0) {
                html += '<div class="tc-flooding-signal tc-flood-unknown" id="flood200-' + item.id + '">' +
                    '<div class="tc-signal-label">200мм</div>' +
                    '<div class="tc-signal-body">' +
                    '<div class="tc-signal-temp" id="flood200-temp-' + item.id + '">\u2014</div>' +
                    '</div></div>';
            }

            // 700mm signal
            if (item.flood700CnlNum > 0 || item.temp700CnlNum > 0) {
                html += '<div class="tc-flooding-signal tc-flood-unknown" id="flood700-' + item.id + '">' +
                    '<div class="tc-signal-label">700мм</div>' +
                    '<div class="tc-signal-body">' +
                    '<div class="tc-signal-temp" id="flood700-temp-' + item.id + '">\u2014</div>' +
                    '</div></div>';
            }

            if (!item.flood200CnlNum && !item.temp200CnlNum && !item.flood700CnlNum && !item.temp700CnlNum) {
                html += '<span class="text-muted">\u2014</span>';
            }
            html += '</div></td>';

            // 6. Battery
            html += '<td class="tc-col-battery text-center">';
            if (item.batteryCnlNum > 0) {
                html += '<span id="battery-' + item.id + '" class="tc-battery-value tc-battery-unknown">' +
                    '<i class="fa-solid fa-battery-half"></i> \u2014</span>';
            } else {
                html += '<span class="text-muted">\u2014</span>';
            }
            html += '</td>';

            // 7. Chat trigger — replaces the old single-comment field. Clicking
            // opens an overlay panel anchored to this row with live multi-user
            // chat + auto-logged flood events.
            html += '<td class="tc-col-comment">' +
                '<button type="button" class="tc-chat-trigger" data-item-id="' + item.id + '">' +
                '<span class="tc-chat-trigger-icon"><i class="fa-solid fa-comments"></i></span>' +
                '<span class="tc-chat-trigger-text" id="chatPreview-' + item.id + '">' +
                'Открыть чат</span>' +
                '<span class="tc-chat-trigger-badge" id="chatBadge-' + item.id + '" ' +
                'style="display:none">0</span>' +
                '</button></td>';

            // 8. Commissioned status
            html += '<td class="tc-col-status text-center">' +
                '<div class="form-check d-flex justify-content-center">' +
                '<input class="form-check-input tc-commissioned-cb" type="checkbox" ' +
                'data-item-id="' + item.id + '"' +
                (ud.isCommissioned ? ' checked' : '') + '>' +
                '</div></td>';

            html += '</tr>';
        }

        tbody.innerHTML = html;
        bindEvents();
    }

    function bindEvents() {
        var chatTriggers = document.querySelectorAll(".tc-chat-trigger");
        for (var i = 0; i < chatTriggers.length; i++) {
            chatTriggers[i].addEventListener("click", function (e) {
                e.stopPropagation();
                var itemId = parseInt(this.getAttribute("data-item-id"));
                if (chatPanels[itemId]) {
                    closeChat(itemId);
                } else {
                    openChat(itemId);
                }
            });
        }

        var checkboxes = document.querySelectorAll(".tc-commissioned-cb");
        for (var i = 0; i < checkboxes.length; i++) {
            checkboxes[i].addEventListener("change", function () {
                var itemId = parseInt(this.getAttribute("data-item-id"));
                saveCommissioned(itemId, this.checked);
            });
        }

        // tbody was rebuilt — the fresh triggers don't carry highlight state,
        // so re-apply the "active chat" marker if there is one.
        syncChatTriggerHighlights();
    }

    function requestData() {
        // Follows the PlgMap / PlgMain pattern: ask the server for current
        // data by viewID — the backend resolves the view's CnlNumList on its
        // own, just like MapApiController.GetCurData / GetCurDataByView.
        // Also piggy-backs chat delta sync on this 1Hz poll via chatCursor.
        $.ajax({
            url: "/Api/ThermalCamera/GetCurData?viewID=" + viewID + "&chatCursor=" + chatCursor,
            type: "GET",
            dataType: "json",
            success: function (dto) {
                if (dto && dto.ok && dto.data) {
                    updateTableData(dto.data);
                    applyChatUpdates(dto.data);
                }
            },
            error: function () {
                console.error("ThermalCamera: failed to get current data");
            }
        });
    }

    function updateTableData(result) {
        var data = result.data || {};

        if (result.serverTime) {
            var timeSpan = document.getElementById("spanServerTime");
            if (timeSpan) {
                var dt = new Date(result.serverTime);
                timeSpan.textContent = dt.toLocaleTimeString("ru-RU", { hour12: false });
            }
        }

        // Reset per-item flood states before this polling cycle.
        for (var i = 0; i < items.length; i++) {
            floodStateByItem[items[i].id] = null;
        }

        for (var i = 0; i < items.length; i++) {
            var item = items[i];

            // Online status
            updateOnlineStatus(item, data);

            // 200mm flooding (yellow when flooded)
            updateFloodingSignal(item.id, "200", item.flood200CnlNum, item.temp200CnlNum, data, "tc-flood-warning");

            // 700mm flooding (red when flooded)
            updateFloodingSignal(item.id, "700", item.flood700CnlNum, item.temp700CnlNum, data, "tc-flood-alarm");

            // Battery
            updateBattery(item, data);
        }

        updateFloodTriggerIndicators();
    }

    function updateBattery(item, data) {
        if (item.batteryCnlNum <= 0) return;
        var el = document.getElementById("battery-" + item.id);
        if (!el) return;

        var d = data[item.batteryCnlNum];
        if (!d) return;

        if (d.stat <= 0) {
            el.className = "tc-battery-value tc-battery-unknown";
            el.innerHTML = '<i class="fa-solid fa-battery-half"></i> \u2014';
            return;
        }

        var pct = d.val;
        var icon = pct > 75 ? "fa-battery-full" :
                   pct > 50 ? "fa-battery-three-quarters" :
                   pct > 25 ? "fa-battery-half" :
                   pct > 10 ? "fa-battery-quarter" : "fa-battery-empty";
        var cls = pct > 50 ? "tc-battery-good" :
                  pct > 20 ? "tc-battery-low" : "tc-battery-critical";

        el.className = "tc-battery-value " + cls;
        el.innerHTML = '<i class="fa-solid ' + icon + '"></i> ' + pct.toFixed(0) + '%';
    }

    function updateOnlineStatus(item, data) {
        if (item.onlineCnlNum <= 0) return;
        var onlineEl = document.getElementById("online-" + item.id);
        if (!onlineEl) return;

        var d = data[item.onlineCnlNum];
        if (!d) return;

        // Offline covers both explicit 0 and stale/undefined data (stat<=0):
        // when the driver loses contact with the device, SCADA marks the channel
        // as Undefined, which from the user's point of view IS "offline".
        var isOnline = d.stat > 0 && d.val !== 0;
        var textEl = onlineEl.querySelector(".tc-online-text");
        onlineEl.className = "tc-online-indicator " +
            (isOnline ? "tc-status-online" : "tc-status-offline");
        if (textEl) {
            textEl.textContent = isOnline ? "Online" : "Offline";
        }
    }

    function updateFloodingSignal(itemId, size, floodCnlNum, tempCnlNum, data, alarmClass) {
        var signalEl = document.getElementById("flood" + size + "-" + itemId);
        var tempEl = document.getElementById("flood" + size + "-temp-" + itemId);

        // Update flooding status (yellow for 200mm, red for 700mm)
        if (signalEl && floodCnlNum > 0) {
            var fd = data[floodCnlNum];
            if (fd) {
                var isFlooded = fd.val === 0 && fd.stat > 0;
                var isUnknown = fd.stat <= 0;
                signalEl.className = "tc-flooding-signal " +
                    (isUnknown ? "tc-flood-unknown" : isFlooded ? alarmClass : "tc-flood-normal");

                // Track flood state for the trigger-button indicator.
                // 700mm (red) takes priority over 200mm (yellow).
                if (isFlooded) {
                    var cur = floodStateByItem[itemId];
                    if (size === "700" || !cur) {
                        floodStateByItem[itemId] = "flood" + size;
                    }
                }
            }
        }

        // Update temperature value inside the flooding signal block
        if (tempEl && tempCnlNum > 0) {
            var td = data[tempCnlNum];
            if (td) {
                tempEl.textContent = td.stat > 0
                    ? td.val.toFixed(1) + "\u00b0C"
                    : "\u2014";
            }
        }
    }

    function startAutoUpdate() {
        if (updateTimer) clearInterval(updateTimer);
        updateTimer = setInterval(requestData, UPDATE_INTERVAL);
    }

    function saveCommissioned(itemId, isCommissioned) {
        $.ajax({
            url: "/Api/ThermalCamera/SaveCommissioned",
            type: "POST",
            contentType: "application/json",
            data: JSON.stringify({ itemId: itemId, isCommissioned: isCommissioned }),
            dataType: "json",
            success: function (dto) {
                if (!dto || !dto.ok) console.error("ThermalCamera: save status failed");
            }
        });
    }

    // -------- Chat ---------------------------------------------------------

    function findItem(itemId) {
        for (var i = 0; i < items.length; i++) {
            if (items[i].id === itemId) return items[i];
        }
        return null;
    }

    function applyChatUpdates(result) {
        if (typeof result.chatCursor === "number" && result.chatCursor > chatCursor) {
            chatCursor = result.chatCursor;
        }
        var updates = result.chatUpdates;
        if (!updates || !updates.length) return;

        var touched = {};
        for (var i = 0; i < updates.length; i++) {
            var u = updates[i];
            var itemId = u.itemId;
            var msg = u.message;
            if (!chatByItem[itemId]) chatByItem[itemId] = [];
            // De-dupe by id.
            var exists = false;
            for (var j = 0; j < chatByItem[itemId].length; j++) {
                if (chatByItem[itemId][j].id === msg.id) { exists = true; break; }
            }
            if (!exists) {
                chatByItem[itemId].push(msg);
                touched[itemId] = true;
            }
        }

        for (var idStr in touched) {
            if (!touched.hasOwnProperty(idStr)) continue;
            var id = parseInt(idStr);
            updateChatPreview(id);
            if (chatPanels[id]) {
                renderChatMessages(id);
                scrollChatToBottom(id);
            } else {
                bumpChatBadge(id);
            }
        }
    }

    function updateChatPreview(itemId) {
        var previewEl = document.getElementById("chatPreview-" + itemId);
        if (!previewEl) return;
        var msgs = chatByItem[itemId] || [];
        if (!msgs.length) {
            previewEl.textContent = "Открыть чат";
            return;
        }
        var last = msgs[msgs.length - 1];
        var text = last.text || "";
        if (text.length > CHAT_PREVIEW_LEN) {
            text = text.substring(0, CHAT_PREVIEW_LEN) + "…";
        }
        var author = last.author || "";
        previewEl.textContent = (author ? author + ": " : "") + text;
    }

    function bumpChatBadge(itemId) {
        var badge = document.getElementById("chatBadge-" + itemId);
        if (!badge) return;
        var cur = parseInt(badge.textContent) || 0;
        badge.textContent = String(cur + 1);
        badge.style.display = "inline-flex";
    }

    function clearChatBadge(itemId) {
        var badge = document.getElementById("chatBadge-" + itemId);
        if (!badge) return;
        badge.textContent = "0";
        badge.style.display = "none";
    }

    function buildPanelHtml(item) {
        // Header title: "<district> <name> <address>" — the same fields the
        // user sees in the row, so the chat window is unambiguous.
        var district = escapeHtml(String(item.districtNumber || "—"));
        var name = escapeHtml(item.name || "Объект ТК");
        var address = escapeHtml(item.descr || "");
        var titleParts = '<span class="tc-chat-title-district">' + district + '</span>' +
                         '<span class="tc-chat-title-name">' + name + '</span>';
        if (address) {
            titleParts += '<span class="tc-chat-title-address">' + address + '</span>';
        }
        return '' +
            '<div class="tc-chat-header">' +
                '<div class="tc-chat-header-title">' +
                    '<i class="fa-solid fa-comments"></i> ' + titleParts +
                '</div>' +
                '<div class="tc-chat-header-actions">' +
                    '<button type="button" class="tc-chat-close" title="Закрыть">' +
                        '<i class="fa-solid fa-xmark"></i>' +
                        '<span>Закрыть</span>' +
                    '</button>' +
                '</div>' +
            '</div>' +
            '<div class="tc-chat-messages"></div>' +
            '<div class="tc-chat-input-row">' +
                '<textarea class="tc-chat-input" rows="2" ' +
                    'placeholder="Введите сообщение..." maxlength="2000"></textarea>' +
                '<button type="button" class="tc-chat-send" title="Отправить">' +
                    '<span>Отправить</span>' +
                '</button>' +
            '</div>';
    }

    function openChat(itemId) {
        if (chatPanels[itemId]) return;
        // Single-chat mode: close any existing panel first.
        if (activeChatId !== null && chatPanels[activeChatId]) {
            closeChat(activeChatId);
        }
        var item = findItem(itemId);
        if (!item) return;

        var panel = document.createElement("div");
        panel.id = "tcChatPanel-" + itemId;
        panel.className = "tc-chat-panel tc-chat-active";
        panel.setAttribute("data-item-id", itemId);
        panel.style.zIndex = 2001;
        panel.innerHTML = buildPanelHtml(item);
        document.body.appendChild(panel);

        chatPanels[itemId] = { el: panel };
        activeChatId = itemId;
        clearChatBadge(itemId);
        selectedMessageId = null;

        repositionChat();
        bindPanelEvents(itemId);
        syncChatTriggerHighlights();
        updateChatDistrictNotice();

        var wrapper = document.querySelector(".tc-table-wrapper");
        if (wrapper) wrapper.addEventListener("scroll", repositionChat);

        if (!chatHistoryLoaded[itemId]) {
            loadChatHistory(itemId);
        } else {
            renderChatMessages(itemId);
            scrollChatToBottom(itemId);
            var input = panel.querySelector(".tc-chat-input");
            if (input) input.focus();
        }
    }

    // Computes the viewport rectangle for the chat panel so it covers
    // the "Чат" and "В работе" columns from just below the sticky
    // thead to the bottom of the visible table area.
    function computeChatBounds() {
        var commentTh = document.querySelector("th.tc-col-comment");
        var wrapperEl = document.querySelector(".tc-table-wrapper");
        if (!commentTh || !wrapperEl) {
            return { left: window.innerWidth - 400, top: 100, width: 400, height: window.innerHeight - 120 };
        }
        var thRect = commentTh.getBoundingClientRect();
        var wrapperRect = wrapperEl.getBoundingClientRect();
        return {
            left: thRect.left,
            top: thRect.bottom,
            width: wrapperRect.right - thRect.left,
            height: Math.max(300, wrapperRect.bottom - thRect.bottom)
        };
    }

    function repositionChat() {
        if (activeChatId === null) return;
        var p = chatPanels[activeChatId];
        if (!p) return;
        var b = computeChatBounds();
        p.el.style.left = b.left + "px";
        p.el.style.top = b.top + "px";
        p.el.style.width = b.width + "px";
        p.el.style.height = b.height + "px";
    }

    function bindPanelEvents(itemId) {
        var p = chatPanels[itemId];
        if (!p) return;
        var panel = p.el;

        var closeBtn = panel.querySelector(".tc-chat-close");
        var sendBtn = panel.querySelector(".tc-chat-send");
        var inputEl = panel.querySelector(".tc-chat-input");

        if (closeBtn) {
            closeBtn.addEventListener("click", function (e) {
                e.stopPropagation();
                closeChat(itemId);
            });
        }
        if (sendBtn) {
            sendBtn.addEventListener("click", function (e) {
                e.stopPropagation();
                sendChatMessage(itemId);
            });
        }
        if (inputEl) {
            inputEl.addEventListener("keydown", function (e) {
                if (e.key === "Enter" && !e.shiftKey) {
                    e.preventDefault();
                    sendChatMessage(itemId);
                }
            });
        }
    }

    function syncChatTriggerHighlights() {
        var triggers = document.querySelectorAll(".tc-chat-trigger");
        for (var t = 0; t < triggers.length; t++) {
            var tid = parseInt(triggers[t].getAttribute("data-item-id"));
            if (tid === activeChatId) {
                triggers[t].classList.add("tc-chat-trigger-active");
            } else {
                triggers[t].classList.remove("tc-chat-trigger-active");
            }
        }
    }

    // Colors the ring around each "Open chat" button based on the last known
    // flood state for that TK — yellow (200mm) or red (700mm). 700mm wins if
    // both fire simultaneously. Called after every data poll.
    function updateFloodTriggerIndicators() {
        var triggers = document.querySelectorAll(".tc-chat-trigger");
        for (var i = 0; i < triggers.length; i++) {
            var tid = parseInt(triggers[i].getAttribute("data-item-id"));
            triggers[i].classList.remove("tc-chat-trigger-flood200");
            triggers[i].classList.remove("tc-chat-trigger-flood700");
            var st = floodStateByItem[tid];
            if (st === "flood700") {
                triggers[i].classList.add("tc-chat-trigger-flood700");
            } else if (st === "flood200") {
                triggers[i].classList.add("tc-chat-trigger-flood200");
            }
        }
    }

    // Shows/hides a warning strip inside the currently open chat panel when
    // the owning TK's row is hidden by the district/search filter — so the
    // user keeps the chat context even though the row itself is no longer
    // visible in the table.
    function updateChatDistrictNotice() {
        if (activeChatId === null) return;
        var p = chatPanels[activeChatId];
        if (!p) return;

        var row = document.querySelector("tr[data-item-id='" + activeChatId + "']");
        var hidden = !row || row.style.display === "none";

        var notice = p.el.querySelector(".tc-chat-notice");
        if (hidden) {
            if (!notice) {
                notice = document.createElement("div");
                notice.className = "tc-chat-notice";
                notice.innerHTML = '<i class="fa-solid fa-triangle-exclamation"></i>' +
                    '<span>Объект ТК скрыт фильтром и не отображается в таблице.</span>';
                var header = p.el.querySelector(".tc-chat-header");
                if (header && header.nextSibling) {
                    p.el.insertBefore(notice, header.nextSibling);
                } else {
                    p.el.appendChild(notice);
                }
            }
        } else if (notice) {
            notice.remove();
        }
    }

    function closeChat(itemId) {
        if (typeof itemId === "undefined" || itemId === null) {
            itemId = activeChatId;
        }
        if (itemId === null) return;
        var p = chatPanels[itemId];
        if (!p) return;
        p.el.remove();
        delete chatPanels[itemId];
        if (activeChatId === itemId) activeChatId = null;
        selectedMessageId = null;
        syncChatTriggerHighlights();
        var wrapper = document.querySelector(".tc-table-wrapper");
        if (wrapper) wrapper.removeEventListener("scroll", repositionChat);
    }

    function loadChatHistory(itemId) {
        $.ajax({
            url: "/Api/ThermalCamera/GetChatHistory?itemId=" + itemId,
            type: "GET",
            dataType: "json",
            success: function (dto) {
                if (dto && dto.ok && dto.data) {
                    chatByItem[itemId] = dto.data.messages || [];
                    chatHistoryLoaded[itemId] = true;
                    updateChatPreview(itemId);
                    if (chatPanels[itemId]) {
                        renderChatMessages(itemId);
                        scrollChatToBottom(itemId);
                        var input = chatPanels[itemId].el.querySelector(".tc-chat-input");
                        if (input) input.focus();
                    }
                }
            },
            error: function () {
                console.error("ThermalCamera: failed to load chat history");
            }
        });
    }

    function renderChatMessages(itemId) {
        var p = chatPanels[itemId];
        if (!p) return;
        var box = p.el.querySelector(".tc-chat-messages");
        if (!box) return;
        var msgs = chatByItem[itemId] || [];
        if (!msgs.length) {
            box.innerHTML = "";
            return;
        }
        msgs.sort(function (a, b) { return a.id - b.id; });

        var selectedId = selectedMessageId;
        var html = "";
        for (var i = 0; i < msgs.length; i++) {
            var m = msgs[i];
            var isSystem = m.kind && m.kind !== "user";
            var kindClass = isSystem
                ? (m.kind === "flood700" ? "tc-chat-flood700" : "tc-chat-flood200")
                : "tc-chat-user";
            var timeStr = formatChatTime(m.timestampMs);
            html += '<div class="tc-chat-msg ' + kindClass +
                (selectedId === m.id ? " tc-chat-selected" : "") +
                '" data-msg-id="' + m.id + '" data-kind="' + (m.kind || "user") + '">' +
                '<div class="tc-chat-bubble">' +
                    '<div class="tc-chat-meta">' +
                        '<span class="tc-chat-author">' + escapeHtml(m.author || "") + '</span>' +
                        '<span class="tc-chat-time">' + timeStr + '</span>' +
                    '</div>' +
                    '<div class="tc-chat-text">' + escapeHtml(m.text || "") + '</div>' +
                '</div>' +
                (isSystem || !isAdmin ? "" :
                    '<button type="button" class="tc-chat-del" data-msg-id="' + m.id +
                    '" title="Удалить"><i class="fa-solid fa-xmark"></i></button>') +
                '</div>';
        }
        box.innerHTML = html;

        var msgEls = box.querySelectorAll(".tc-chat-msg");
        for (var j = 0; j < msgEls.length; j++) {
            msgEls[j].addEventListener("click", function (e) {
                e.stopPropagation();
                if (!isAdmin) return;
                var id = parseInt(this.getAttribute("data-msg-id"));
                if (this.getAttribute("data-kind") !== "user") return;
                selectedMessageId = (selectedMessageId === id) ? null : id;
                renderChatMessages(itemId);
            });
        }
        var delEls = box.querySelectorAll(".tc-chat-del");
        for (var k = 0; k < delEls.length; k++) {
            delEls[k].addEventListener("click", function (e) {
                e.stopPropagation();
                var id = parseInt(this.getAttribute("data-msg-id"));
                deleteChatMessage(itemId, id);
            });
        }
    }

    function scrollChatToBottom(itemId) {
        var p = chatPanels[itemId];
        if (!p) return;
        var box = p.el.querySelector(".tc-chat-messages");
        if (box) box.scrollTop = box.scrollHeight;
    }

    function formatChatTime(ms) {
        if (!ms) return "";
        var d = new Date(ms);
        var pad = function (n) { return n < 10 ? "0" + n : "" + n; };
        return pad(d.getDate()) + "." + pad(d.getMonth() + 1) + " " +
            pad(d.getHours()) + ":" + pad(d.getMinutes());
    }

    function sendChatMessage(itemId) {
        var p = chatPanels[itemId];
        if (!p) return;
        var inputEl = p.el.querySelector(".tc-chat-input");
        if (!inputEl) return;
        var text = (inputEl.value || "").trim();
        if (!text) return;
        inputEl.disabled = true;
        $.ajax({
            url: "/Api/ThermalCamera/PostChatMessage",
            type: "POST",
            contentType: "application/json",
            data: JSON.stringify({ itemId: itemId, text: text }),
            dataType: "json",
            success: function (dto) {
                inputEl.disabled = false;
                if (dto && dto.ok && dto.data) {
                    inputEl.value = "";
                    inputEl.focus();
                    if (!chatByItem[itemId]) chatByItem[itemId] = [];
                    var exists = false;
                    for (var i = 0; i < chatByItem[itemId].length; i++) {
                        if (chatByItem[itemId][i].id === dto.data.id) { exists = true; break; }
                    }
                    if (!exists) chatByItem[itemId].push(dto.data);
                    if (dto.data.id > chatCursor) chatCursor = dto.data.id;
                    updateChatPreview(itemId);
                    if (chatPanels[itemId]) {
                        renderChatMessages(itemId);
                        scrollChatToBottom(itemId);
                    }
                } else {
                    alert("Не удалось отправить сообщение: " + (dto && dto.msg ? dto.msg : "?"));
                }
            },
            error: function () {
                inputEl.disabled = false;
                alert("Ошибка соединения при отправке сообщения");
            }
        });
    }

    function deleteChatMessage(itemId, messageId) {
        if (!itemId || !messageId) return;
        $.ajax({
            url: "/Api/ThermalCamera/DeleteChatMessage",
            type: "POST",
            contentType: "application/json",
            data: JSON.stringify({ itemId: itemId, messageId: messageId }),
            dataType: "json",
            success: function (dto) {
                if (dto && dto.ok) {
                    var list = chatByItem[itemId] || [];
                    for (var i = 0; i < list.length; i++) {
                        if (list[i].id === messageId) { list.splice(i, 1); break; }
                    }
                    if (selectedMessageId === messageId) {
                        selectedMessageId = null;
                    }
                    updateChatPreview(itemId);
                    if (chatPanels[itemId]) {
                        renderChatMessages(itemId);
                    }
                } else {
                    alert("Не удалось удалить сообщение: " + (dto && dto.msg ? dto.msg : "?"));
                }
            }
        });
    }

    function bindChatKeyboard() {
        document.addEventListener("keydown", function (e) {
            if (activeChatId === null) return;
            if (e.key === "Escape") {
                closeChat(activeChatId);
                return;
            }
            if (e.key === "Delete" || e.key === "Backspace") {
                if (!isAdmin) return;
                var tag = (e.target && e.target.tagName) || "";
                if (tag === "TEXTAREA" || tag === "INPUT") return;
                if (selectedMessageId !== null) {
                    e.preventDefault();
                    deleteChatMessage(activeChatId, selectedMessageId);
                }
            }
        });
    }

    function showPhoto(photoPath, name) {
        var img = document.getElementById("imgPhoto");
        var errorDiv = document.getElementById("divPhotoError");
        var label = document.getElementById("photoModalLabel");

        // Build URL through plugin API endpoint
        var url = "/Api/ThermalCamera/GetPhoto?path=" + encodeURIComponent(photoPath);

        if (label) label.textContent = "Фото: " + name;
        if (img) {
            img.classList.remove("d-none");
            img.src = url;
            img.onerror = function () {
                img.classList.add("d-none");
                if (errorDiv) errorDiv.classList.remove("d-none");
            };
            img.onload = function () {
                if (errorDiv) errorDiv.classList.add("d-none");
            };
        }
        if (errorDiv) errorDiv.classList.add("d-none");

        var modal = new bootstrap.Modal(document.getElementById("photoModal"));
        modal.show();
    }

    function escapeHtml(text) {
        var div = document.createElement("div");
        div.appendChild(document.createTextNode(text));
        return div.innerHTML;
    }

    function escapeAttr(text) {
        return text.replace(/'/g, "\\'").replace(/"/g, '\\"');
    }

    return {
        init: init,
        showPhoto: showPhoto,
        openChat: openChat,
        closeChat: closeChat
    };
})();

$(document).ready(function () {
    thermalCamera.init();
});
