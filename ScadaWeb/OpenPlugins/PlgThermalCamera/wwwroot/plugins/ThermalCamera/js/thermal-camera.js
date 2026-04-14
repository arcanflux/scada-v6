// Thermal Camera Plugin - Real-time table with channel data from .map file
// Плагин тепловых камер - Таблица с данными каналов в реальном времени из файла .map

var thermalCamera = (function () {
    var UPDATE_INTERVAL = 1000;
    var CHAT_PREVIEW_LEN = 60;

    var items = [];
    var userData = {};
    var viewID = 0;
    var updateTimer = null;
    var districtSortAsc = true;
    var searchQuery = "";
    var selectedDistricts = {};

    // Chat state
    // chatByItem[itemId] = array of message objects {id, timestampMs, author, text, kind}
    var chatByItem = {};
    var chatCursor = 0;
    var chatHistoryLoaded = {};
    var openChatItemId = null;
    var selectedMessageId = null;
    var outsideClickHandler = null;

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
        window.addEventListener("resize", function () {
            if (openChatItemId === null) return;
            var panel = document.getElementById("tcChatPanel");
            var row = document.querySelector("tr[data-item-id='" + openChatItemId + "']");
            if (panel && row) positionChatPanel(panel, row);
        });
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
                if (openChatItemId === itemId) {
                    closeChat();
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

        var btnRefresh = document.getElementById("btnRefresh");
        if (btnRefresh) {
            btnRefresh.addEventListener("click", function () { requestData(); });
        }
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
            if (openChatItemId === id) {
                renderChatMessages(id);
                scrollChatToBottom();
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

    function openChat(itemId) {
        if (openChatItemId !== null && openChatItemId !== itemId) {
            closeChat();
        }
        openChatItemId = itemId;
        selectedMessageId = null;
        clearChatBadge(itemId);

        var row = document.querySelector("tr[data-item-id='" + itemId + "']");
        if (!row) return;

        var item = null;
        for (var i = 0; i < items.length; i++) {
            if (items[i].id === itemId) { item = items[i]; break; }
        }

        var panel = document.getElementById("tcChatPanel");
        if (!panel) {
            panel = document.createElement("div");
            panel.id = "tcChatPanel";
            panel.className = "tc-chat-panel";
            document.body.appendChild(panel);
        }

        var title = item ? escapeHtml(item.name || "Объект ТК") : "Объект ТК";
        panel.innerHTML =
            '<div class="tc-chat-header">' +
                '<div class="tc-chat-header-title">' +
                    '<i class="fa-solid fa-comments"></i> Чат — ' + title +
                '</div>' +
                '<button type="button" class="tc-chat-close" id="tcChatClose" title="Закрыть">' +
                    '<i class="fa-solid fa-xmark"></i>' +
                    '<span>Закрыть</span>' +
                '</button>' +
            '</div>' +
            '<div class="tc-chat-messages" id="tcChatMessages">' +
                '<div class="tc-chat-loading">Загрузка истории…</div>' +
            '</div>' +
            '<div class="tc-chat-input-row">' +
                '<textarea id="tcChatInput" class="tc-chat-input" rows="2" ' +
                    'placeholder="Введите сообщение..." maxlength="2000"></textarea>' +
                '<button type="button" class="tc-chat-send" id="tcChatSend" title="Отправить">' +
                    '<i class="fa-solid fa-paper-plane"></i>' +
                    '<span>Отправить</span>' +
                '</button>' +
            '</div>';

        positionChatPanel(panel, row);

        document.getElementById("tcChatClose").addEventListener("click", function (e) {
            e.stopPropagation();
            closeChat();
        });
        document.getElementById("tcChatSend").addEventListener("click", function (e) {
            e.stopPropagation();
            sendChatMessage();
        });
        var inputEl = document.getElementById("tcChatInput");
        inputEl.addEventListener("keydown", function (e) {
            if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                sendChatMessage();
            }
        });
        // Prevent clicks inside the panel from triggering the outside handler.
        panel.addEventListener("click", function (e) { e.stopPropagation(); });

        // Click-outside close.
        outsideClickHandler = function () { closeChat(); };
        setTimeout(function () {
            document.addEventListener("click", outsideClickHandler);
        }, 0);

        // Load full history on first open, otherwise just render.
        if (!chatHistoryLoaded[itemId]) {
            loadChatHistory(itemId);
        } else {
            renderChatMessages(itemId);
            scrollChatToBottom();
            inputEl.focus();
        }
    }

    function positionChatPanel(panel, row) {
        var rowRect = row.getBoundingClientRect();
        var panelWidth = Math.min(560, window.innerWidth - 40);
        var panelHeight = Math.min(480, window.innerHeight - 40);

        // Center vertically on the row when possible, clamp to viewport.
        var top = rowRect.top + rowRect.height / 2 - panelHeight / 2;
        if (top < 20) top = 20;
        if (top + panelHeight > window.innerHeight - 20) {
            top = window.innerHeight - panelHeight - 20;
        }

        // Anchor to the right edge of the row (comment column is near the right).
        var left = rowRect.right - panelWidth;
        if (left < 20) left = 20;
        if (left + panelWidth > window.innerWidth - 20) {
            left = window.innerWidth - panelWidth - 20;
        }

        panel.style.width = panelWidth + "px";
        panel.style.height = panelHeight + "px";
        panel.style.top = top + "px";
        panel.style.left = left + "px";
    }

    function closeChat() {
        var panel = document.getElementById("tcChatPanel");
        if (panel) panel.remove();
        if (outsideClickHandler) {
            document.removeEventListener("click", outsideClickHandler);
            outsideClickHandler = null;
        }
        openChatItemId = null;
        selectedMessageId = null;
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
                    if (openChatItemId === itemId) {
                        renderChatMessages(itemId);
                        scrollChatToBottom();
                        var inputEl = document.getElementById("tcChatInput");
                        if (inputEl) inputEl.focus();
                    }
                }
            },
            error: function () {
                console.error("ThermalCamera: failed to load chat history");
            }
        });
    }

    function renderChatMessages(itemId) {
        var box = document.getElementById("tcChatMessages");
        if (!box) return;
        var msgs = chatByItem[itemId] || [];
        if (!msgs.length) {
            box.innerHTML = '<div class="tc-chat-empty">Нет сообщений. Будьте первым!</div>';
            return;
        }
        // Sort by id to preserve temporal order.
        msgs.sort(function (a, b) { return a.id - b.id; });

        var html = "";
        for (var i = 0; i < msgs.length; i++) {
            var m = msgs[i];
            var isSystem = m.kind && m.kind !== "user";
            var kindClass = isSystem
                ? (m.kind === "flood700" ? "tc-chat-flood700" : "tc-chat-flood200")
                : "tc-chat-user";
            var timeStr = formatChatTime(m.timestampMs);
            html += '<div class="tc-chat-msg ' + kindClass +
                (selectedMessageId === m.id ? " tc-chat-selected" : "") +
                '" data-msg-id="' + m.id + '" data-kind="' + (m.kind || "user") + '">' +
                '<div class="tc-chat-bubble">' +
                    '<div class="tc-chat-meta">' +
                        '<span class="tc-chat-author">' + escapeHtml(m.author || "") + '</span>' +
                        '<span class="tc-chat-time">' + timeStr + '</span>' +
                    '</div>' +
                    '<div class="tc-chat-text">' + escapeHtml(m.text || "") + '</div>' +
                '</div>' +
                (isSystem ? "" :
                    '<button type="button" class="tc-chat-del" data-msg-id="' + m.id +
                    '" title="Удалить"><i class="fa-solid fa-xmark"></i></button>') +
                '</div>';
        }
        box.innerHTML = html;

        // Selection toggle.
        var msgEls = box.querySelectorAll(".tc-chat-msg");
        for (var j = 0; j < msgEls.length; j++) {
            msgEls[j].addEventListener("click", function (e) {
                e.stopPropagation();
                var id = parseInt(this.getAttribute("data-msg-id"));
                if (this.getAttribute("data-kind") !== "user") return;
                selectedMessageId = (selectedMessageId === id) ? null : id;
                renderChatMessages(openChatItemId);
            });
        }
        // Side crosshair delete buttons.
        var delEls = box.querySelectorAll(".tc-chat-del");
        for (var k = 0; k < delEls.length; k++) {
            delEls[k].addEventListener("click", function (e) {
                e.stopPropagation();
                var id = parseInt(this.getAttribute("data-msg-id"));
                deleteChatMessage(openChatItemId, id);
            });
        }

        // Global "Delete" key deletes the selected message.
        // (Bound once via document keydown in init.)
    }

    function scrollChatToBottom() {
        var box = document.getElementById("tcChatMessages");
        if (box) box.scrollTop = box.scrollHeight;
    }

    function formatChatTime(ms) {
        if (!ms) return "";
        var d = new Date(ms);
        var pad = function (n) { return n < 10 ? "0" + n : "" + n; };
        return pad(d.getDate()) + "." + pad(d.getMonth() + 1) + " " +
            pad(d.getHours()) + ":" + pad(d.getMinutes());
    }

    function sendChatMessage() {
        if (openChatItemId === null) return;
        var inputEl = document.getElementById("tcChatInput");
        if (!inputEl) return;
        var text = (inputEl.value || "").trim();
        if (!text) return;
        var itemId = openChatItemId;
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
                    // Optimistically insert; delta poll will reconcile and dedupe.
                    if (!chatByItem[itemId]) chatByItem[itemId] = [];
                    var exists = false;
                    for (var i = 0; i < chatByItem[itemId].length; i++) {
                        if (chatByItem[itemId][i].id === dto.data.id) { exists = true; break; }
                    }
                    if (!exists) chatByItem[itemId].push(dto.data);
                    if (dto.data.id > chatCursor) chatCursor = dto.data.id;
                    updateChatPreview(itemId);
                    if (openChatItemId === itemId) {
                        renderChatMessages(itemId);
                        scrollChatToBottom();
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
                    if (selectedMessageId === messageId) selectedMessageId = null;
                    updateChatPreview(itemId);
                    if (openChatItemId === itemId) {
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
            if (openChatItemId === null) return;
            if (e.key === "Escape") {
                closeChat();
                return;
            }
            if ((e.key === "Delete" || e.key === "Backspace") && selectedMessageId !== null) {
                // Don't steal Backspace from the textarea input.
                var tag = (e.target && e.target.tagName) || "";
                if (tag === "TEXTAREA" || tag === "INPUT") return;
                e.preventDefault();
                deleteChatMessage(openChatItemId, selectedMessageId);
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
        refresh: requestData,
        openChat: openChat,
        closeChat: closeChat
    };
})();

$(document).ready(function () {
    thermalCamera.init();
});
