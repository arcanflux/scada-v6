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


    // Persistent state timers — start timestamps (UTC ms) from the server.
    // Zero means state is not active. Updated on every 1Hz poll.
    var timersByItem = {};              // itemId -> { offlineStartMs, flood200StartMs, flood700StartMs }
    var timerTickInterval = null;       // 1s interval for updating timer displays

    // Active flood events for Journal top panel
    var activeFloodEvents = {};         // itemId -> { kind, startMs, name }
    var pendingAckItems = {};           // itemId -> { itemName, flood700StartMs }
    var ackedByItem = {};               // itemId -> { flood700StartMs, ackedAtMs, ackedBy, comment }

    var ACK_REASONS = [
        "Дефект",
        "Парение/Конденсат",
        "Гидравлические испытания",
        "Ремонт ТС",
        "Талые/Дождевые воды",
        "Затопление ХВС",
        "Затопление канализационными водами",
        "Затопление грунтовыми водами",
        "Дренирование",
        "Остаточная вода после дефекта",
        "Обследованное ТК сухое"
    ];
    var journalFilter = { show200: true, show700: true };
    var journalBottomView = "pending"; // "pending" | "history"
    var onlineByItem = {};             // itemId -> true|false
    var flood200ByItem = {};           // itemId -> true|false
    var flood700ByItem = {};           // itemId -> true|false
    var hasLiveData = false;
    var ackHistory = [];                // loaded once and updated after each new ack

    var floodHoverTooltipEl = null;

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
        updateDistrictCounts();
        bindHeaderSort();
        updateSortIndicator();
        bindSearch();
        bindDistrictFilter();
        applyFilter();
        bindNavButtons();
        initJournal();
        loadAckHistory();
        initFloodHoverTooltip();
        initPhotoModal();
        requestData();
        startAutoUpdate();
        bindChatKeyboard();
        window.addEventListener("resize", function () {
            repositionChat();
            positionJournal();
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

    function updateDistrictCounts() {
        var counts = {};
        for (var i = 0; i < items.length; i++) {
            var d = items[i].districtNumber || 0;
            counts[d] = (counts[d] || 0) + 1;
        }
        var spans = document.querySelectorAll(".tc-district-count");
        for (var i = 0; i < spans.length; i++) {
            var d = parseInt(spans[i].getAttribute("data-district"));
            var n = counts[d] || 0;
            spans[i].textContent = n > 0 ? n : "";
        }
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
        updateHeaderCounters();
    }

    function updateHeaderCounters() {
        var rows = document.querySelectorAll("#tbodyThermalCameras tr");
        var total = 0, cntOnline = 0, cntNorm200 = 0, cntFlood200 = 0, cntNorm700 = 0, cntFlood700 = 0;
        for (var i = 0; i < rows.length; i++) {
            if (rows[i].style.display === "none") continue;
            total++;
            var id = parseInt(rows[i].getAttribute("data-item-id"));
            if (onlineByItem[id]) cntOnline++;
            if (flood200ByItem[id] === true)  cntFlood200++;
            else if (flood200ByItem[id] === false) cntNorm200++;
            if (flood700ByItem[id] === true)  cntFlood700++;
            else if (flood700ByItem[id] === false) cntNorm700++;
        }
        var setText = function (elId, val) {
            var el = document.getElementById(elId);
            if (el) el.textContent = val;
        };
        setText("tcHdrTotal", total);
        if (hasLiveData) {
            setText("tcHdrOnline",      cntOnline);
            setText("tcHdrOffline",     total - cntOnline);
            setText("tcHdrNorm200",     cntNorm200);
            setText("tcHdrFlood200cnt", cntFlood200);
            setText("tcHdrNorm700",     cntNorm700);
            setText("tcHdrFlood700cnt", cntFlood700);
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
            positionJournal();
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

            // 1. Commissioned status (В работе) — first column
            html += '<td class="tc-col-status text-center">' +
                '<label class="tc-commissioned-toggle">' +
                '<input type="checkbox" class="tc-commissioned-cb" data-item-id="' + item.id + '"' +
                (ud.isCommissioned ? ' checked' : '') + '>' +
                '<span class="tc-commissioned-visual"></span>' +
                '</label></td>';

            // 2. District number
            html += '<td class="tc-col-district text-center">' +
                '<span class="tc-district-badge">' + escapeHtml(String(item.districtNumber || "—")) + '</span></td>';

            // 3. Object name + chat button (icon only, right of name)
            html += '<td class="tc-col-name"><div class="tc-name-cell">' +
                '<span class="tc-name-text">' + escapeHtml(item.name) + '</span>' +
                '<button type="button" class="tc-chat-trigger tc-name-chat-btn" data-item-id="' + item.id + '">' +
                '<i class="fa-solid fa-comments"></i>' +
                '</button>' +
                '</div></td>';

            // 4. Address (descr) + photo button
            html += '<td class="tc-col-address">' +
                '<span class="tc-address-text">' + escapeHtml(item.descr) + '</span>';
            if (item.photoUrl) {
                html += ' <button class="tc-photo-btn" ' +
                    'onclick="thermalCamera.showPhoto(\'' + escapeAttr(item.photoUrl) + '\', \'' +
                    escapeAttr(item.name) + '\')">' +
                    '<i class="fa-solid fa-camera"></i></button>';
            }
            html += '</td>';

            // 5. Online status
            html += '<td class="tc-col-online text-center">' +
                '<span id="online-' + item.id + '" class="tc-online-indicator tc-status-unknown">' +
                '<i class="fa-solid fa-circle"></i></span></td>';

            // 6. Flooding status: two signal blocks (200mm yellow, 700mm red)
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

            // 7. Battery
            html += '<td class="tc-col-battery text-center">';
            if (item.batteryCnlNum > 0) {
                html += '<span id="battery-' + item.id + '" class="tc-battery-value tc-battery-unknown">' +
                    '<i class="fa-solid fa-battery-half"></i> \u2014</span>';
            } else {
                html += '<span class="text-muted">\u2014</span>';
            }
            html += '</td>';

            // 8. Journal column placeholder (panel overlay added in Part 3)
            html += '<td class="tc-col-journal"></td>';

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

        // Delegated click on the flooding cell — opens the flood history modal
        // (year-by-year monthly breakdown of 200мм/700мм flooding events).
        var tbody = document.getElementById("tbodyThermalCameras");
        if (tbody && !tbody._tcFloodHistoryBound) {
            tbody._tcFloodHistoryBound = true;
            tbody.addEventListener("click", function (e) {
                if (e.target.closest(".tc-photo-btn, .tc-chat-trigger, .tc-commissioned-toggle, input")) return;
                var td = e.target.closest("td.tc-col-flooding");
                if (!td) return;
                var tr = td.closest("tr");
                if (!tr) return;
                var itemId = parseInt(tr.getAttribute("data-item-id"));
                var item = items.find(function (x) { return x.id === itemId; });
                if (!item) return;
                if (typeof tcFloodHistory !== "undefined" && tcFloodHistory) {
                    tcFloodHistory.openHistoryModal(item);
                }
            });
        }

        // tbody was rebuilt — the fresh triggers don't carry highlight state,
        // so re-apply the "active chat" marker if there is one.
        syncChatTriggerHighlights();
    }

    // ---- Hover hint tooltip — used by flood cells and header stat dots ----
    // Trigger sources:
    //   - td.tc-col-flooding  → static "click for flood stats" message
    //   - any [data-tc-hint]  → text from the attribute
    // No network requests, fully static.

    var FLOOD_CELL_HINT = "Нажмите для просмотра статистики затоплений";

    function resolveHintTarget(targetEl) {
        if (!targetEl || !targetEl.closest) return null;
        var floodCell = targetEl.closest("td.tc-col-flooding");
        if (floodCell) return { el: floodCell, text: FLOOD_CELL_HINT };
        var hinted = targetEl.closest("[data-tc-hint]");
        if (hinted) return { el: hinted, text: hinted.getAttribute("data-tc-hint") };
        return null;
    }

    function initFloodHoverTooltip() {
        floodHoverTooltipEl = document.createElement("div");
        floodHoverTooltipEl.id = "tcFloodCountTooltip";
        floodHoverTooltipEl.className = "tc-flood-count-tooltip";
        floodHoverTooltipEl.style.display = "none";
        document.body.appendChild(floodHoverTooltipEl);

        document.addEventListener("mouseover", function (e) {
            var t = resolveHintTarget(e.target);
            if (!t || !floodHoverTooltipEl) return;
            floodHoverTooltipEl.textContent = t.text;
            floodHoverTooltipEl.style.display = "block";
        });

        document.addEventListener("mousemove", function (e) {
            if (floodHoverTooltipEl && floodHoverTooltipEl.style.display !== "none") {
                floodHoverTooltipEl.style.left = (e.clientX + 14) + "px";
                floodHoverTooltipEl.style.top = (e.clientY - 40) + "px";
            }
        });

        document.addEventListener("mouseout", function (e) {
            if (!floodHoverTooltipEl) return;
            var t = resolveHintTarget(e.target);
            if (t && !t.el.contains(e.relatedTarget)) {
                floodHoverTooltipEl.style.display = "none";
            }
        });
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
                    updateAckedItems(dto.data);
                    updateTableData(dto.data);
                    applyChatUpdates(dto.data);
                    updateTimers(dto.data);
                    updatePendingAcks(dto.data);
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

        // Reset per-item states before this polling cycle.
        for (var i = 0; i < items.length; i++) {
            onlineByItem[items[i].id] = false;
            flood200ByItem[items[i].id] = null;
            flood700ByItem[items[i].id] = null;
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

        hasLiveData = true;
        updateHeaderCounters();
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

        var isOnline = d.stat > 0 && d.val !== 0;
        onlineByItem[item.id] = isOnline;
        onlineEl.className = "tc-online-indicator " +
            (isOnline ? "tc-status-online" : "tc-status-offline");

        // Show persistent elapsed timer when offline
        var timerId = "offlineTimer-" + item.id;
        var existingTimer = document.getElementById(timerId);
        if (!isOnline) {
            if (!existingTimer) {
                var timerSpan = document.createElement("div");
                timerSpan.id = timerId;
                timerSpan.className = "tc-state-timer";
                onlineEl.parentNode.appendChild(timerSpan);
            }
        } else if (existingTimer) {
            existingTimer.remove();
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
                if (size === "200") flood200ByItem[itemId] = isUnknown ? null : isFlooded;
                if (size === "700") flood700ByItem[itemId] = isUnknown ? null : isFlooded;
                signalEl.className = "tc-flooding-signal " +
                    (isUnknown ? "tc-flood-unknown" : isFlooded ? alarmClass : "tc-flood-normal");

                // Show persistent elapsed timer inside the signal block when flooded
                var fTimerId = "flood" + size + "Timer-" + itemId;
                var existingFTimer = document.getElementById(fTimerId);
                var body = signalEl.querySelector(".tc-signal-body");
                if (isFlooded) {
                    if (!existingFTimer && body) {
                        var fTimerEl = document.createElement("div");
                        fTimerEl.id = fTimerId;
                        fTimerEl.className = "tc-state-timer";
                        body.appendChild(fTimerEl);
                    }
                } else if (existingFTimer) {
                    existingFTimer.remove();
                }

                // Show ack checkmark badge in 700mm cell when flooded and acknowledged
                if (size === "700") {
                    var ackBadgeId = "flood700AckBadge-" + itemId;
                    var existingBadge = document.getElementById(ackBadgeId);
                    if (isFlooded && ackedByItem[itemId]) {
                        if (!existingBadge && body) {
                            var badgeEl = document.createElement("div");
                            badgeEl.id = ackBadgeId;
                            badgeEl.className = "tc-flood-ack-badge";
                            badgeEl.innerHTML = '<i class="fa-solid fa-circle-check"></i> Квит.';
                            body.appendChild(badgeEl);
                        }
                    } else if (existingBadge) {
                        existingBadge.remove();
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

        // 1-second tick to update all elapsed timer displays without hitting the server
        if (timerTickInterval) clearInterval(timerTickInterval);
        timerTickInterval = setInterval(timerTick, 1000);
    }

    function formatDuration(ms) {
        if (!ms || ms < 0) return "";
        var sec = Math.floor(ms / 1000);
        var h = Math.floor(sec / 3600);
        var m = Math.floor((sec % 3600) / 60);
        var s = sec % 60;
        var pad = function (n) { return n < 10 ? "0" + n : "" + n; };
        if (h >= 24) {
            var days = Math.floor(h / 24);
            return days + "д " + pad(h % 24) + ":" + pad(m);
        }
        return pad(h) + ":" + pad(m) + ":" + pad(s);
    }

    function timerTick() {
        var now = Date.now();
        for (var i = 0; i < items.length; i++) {
            var id = items[i].id;
            var t = timersByItem[id];
            if (!t) continue;

            // Offline timer
            if (t.offlineStartMs > 0) {
                var el = document.getElementById("offlineTimer-" + id);
                if (el) el.textContent = formatDuration(now - t.offlineStartMs);
            }
            // Flood 200mm timer
            if (t.flood200StartMs > 0) {
                var el200 = document.getElementById("flood200Timer-" + id);
                if (el200) el200.textContent = formatDuration(now - t.flood200StartMs);
            }
            // Flood 700mm timer
            if (t.flood700StartMs > 0) {
                var el700 = document.getElementById("flood700Timer-" + id);
                if (el700) el700.textContent = formatDuration(now - t.flood700StartMs);
            }
        }
        // Also update journal event elapsed timers
        updateJournalEventTimers();
    }

    function updateTimers(result) {
        if (!result.timers) return;
        for (var idStr in result.timers) {
            if (!result.timers.hasOwnProperty(idStr)) continue;
            timersByItem[parseInt(idStr)] = result.timers[idStr];
        }
        // Update active flood events from timer data
        updateActiveFloodEvents();
        // Immediately render timer values
        timerTick();
    }

    function updateActiveFloodEvents() {
        for (var i = 0; i < items.length; i++) {
            var item = items[i];
            var t = timersByItem[item.id];
            if (!t) continue;

            if (t.flood700StartMs > 0) {
                activeFloodEvents[item.id] = { kind: "flood700", startMs: t.flood700StartMs, name: item.name };
            } else if (t.flood200StartMs > 0) {
                activeFloodEvents[item.id] = { kind: "flood200", startMs: t.flood200StartMs, name: item.name };
            } else {
                delete activeFloodEvents[item.id];
            }
        }
        renderJournalEvents();
    }

    function updateAckedItems(result) {
        ackedByItem = {};
        if (!result.ackedItems) return;
        for (var i = 0; i < result.ackedItems.length; i++) {
            var a = result.ackedItems[i];
            ackedByItem[a.itemId] = {
                flood700StartMs: a.flood700StartMs,
                ackedAtMs: a.ackedAtMs,
                ackedBy: a.ackedBy || "",
                comment: a.comment || ""
            };
        }
    }

    function updatePendingAcks(result) {
        if (!result.pendingAcks) return;
        var newPending = {};
        for (var i = 0; i < result.pendingAcks.length; i++) {
            var pa = result.pendingAcks[i];
            newPending[pa.itemId] = { itemName: pa.itemName, flood700StartMs: pa.flood700StartMs };
        }
        // Only rebuild DOM when the set of pending items changes (avoids clearing typed comments)
        var newKeys = Object.keys(newPending).sort().join(",");
        var oldKeys = Object.keys(pendingAckItems).sort().join(",");
        pendingAckItems = newPending;
        if (newKeys !== oldKeys) {
            renderJournalAck();
        }
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

    // -------- Navigation buttons ------------------------------------------

    function bindNavButtons() {
        var container = document.querySelector(".tc-container");
        if (!container) return;

        function navigate(viewId, frameUrl, pageUrl) {
            try {
                if (window.parent && window.parent.viewPage && typeof window.parent.viewPage.loadView === "function") {
                    window.parent.viewPage.loadView(viewId, frameUrl, pageUrl);
                } else if (pageUrl) {
                    window.top.location.href = pageUrl;
                }
            } catch (e) {
                if (pageUrl) window.top.location.href = pageUrl;
            }
        }

        var mainBtn = document.getElementById("btnNavMain");
        if (mainBtn) {
            var mainId = parseInt(container.getAttribute("data-main-view-id")) || 0;
            var mainFrame = container.getAttribute("data-main-view-frame") || "";
            var mainPage = container.getAttribute("data-main-view-page") || "";
            mainBtn.addEventListener("click", function () { navigate(mainId, mainFrame, mainPage); });
        }

        var mapBtn = document.getElementById("btnNavMap");
        if (mapBtn) {
            var mapId = parseInt(container.getAttribute("data-map-view-id")) || 0;
            var mapFrame = container.getAttribute("data-map-view-frame") || "";
            var mapPage = container.getAttribute("data-map-view-page") || "";
            mapBtn.addEventListener("click", function () { navigate(mapId, mapFrame, mapPage); });
        }
    }

    // -------- Journal panel ------------------------------------------------

    function initJournal() {
        var existing = document.getElementById("tcJournal");
        if (existing) existing.remove();

        var panel = document.createElement("div");
        panel.id = "tcJournal";
        panel.className = "tc-journal-panel";
        panel.innerHTML =
            '<div class="tc-journal-top">' +
                '<div class="tc-journal-section-header">' +
                    '<span class="tc-journal-section-title">Активные события</span>' +
                    '<div class="tc-journal-filter">' +
                        '<button id="jflt200" class="tc-journal-flt-btn tc-journal-flt-btn-active" data-kind="200">' +
                            '<i class="fa-solid fa-water"></i> 200мм' +
                        '</button>' +
                        '<button id="jflt700" class="tc-journal-flt-btn tc-journal-flt-btn-active" data-kind="700">' +
                            '<i class="fa-solid fa-water"></i> 700мм' +
                        '</button>' +
                    '</div>' +
                '</div>' +
                '<div id="tcJournalEvents" class="tc-journal-events"></div>' +
            '</div>' +
            '<div class="tc-journal-divider"></div>' +
            '<div class="tc-journal-bottom">' +
                '<div class="tc-journal-section-header">' +
                    '<span class="tc-journal-section-title">Квитирование</span>' +
                    '<div class="tc-journal-filter">' +
                        '<button id="jbtnHistory" class="tc-journal-flt-btn">' +
                            '<i class="fa-solid fa-clock-rotate-left"></i> История' +
                        '</button>' +
                    '</div>' +
                '</div>' +
                '<div id="tcJournalAck" class="tc-journal-ack-list"></div>' +
                '<div id="tcJournalAckHistory" class="tc-journal-ack-history"></div>' +
            '</div>';

        document.body.appendChild(panel);

        document.getElementById("jflt200").addEventListener("click", function () {
            this.classList.toggle("tc-journal-flt-btn-active");
            journalFilter.show200 = this.classList.contains("tc-journal-flt-btn-active");
            renderJournalEvents();
        });
        document.getElementById("jflt700").addEventListener("click", function () {
            this.classList.toggle("tc-journal-flt-btn-active");
            journalFilter.show700 = this.classList.contains("tc-journal-flt-btn-active");
            renderJournalEvents();
        });

        document.getElementById("jbtnHistory").addEventListener("click", function () {
            switchJournalBottom(journalBottomView === "history" ? "pending" : "history");
        });
        switchJournalBottom("pending");

        positionJournal();
    }

    function switchJournalBottom(view) {
        journalBottomView = view;
        var ackBox  = document.getElementById("tcJournalAck");
        var histBox = document.getElementById("tcJournalAckHistory");
        var btn     = document.getElementById("jbtnHistory");
        var showHistory = (view === "history");
        if (ackBox)  ackBox.style.display  = showHistory ? "none" : "";
        if (histBox) histBox.style.display = showHistory ? ""     : "none";
        if (btn) btn.classList.toggle("tc-journal-flt-btn-active", showHistory);
    }

    function positionJournal() {
        var headerEl = document.querySelector(".tc-header");
        if (!headerEl) return;
        var headerRect = headerEl.getBoundingClientRect();

        // Journal panel
        var thJournal = document.querySelector("th.tc-col-journal");
        if (thJournal) {
            var jRect = thJournal.getBoundingClientRect();
            var panel = document.getElementById("tcJournal");
            if (panel) {
                var h = window.innerHeight - jRect.bottom;
                panel.style.left  = jRect.left + "px";
                panel.style.top   = jRect.bottom + "px";
                panel.style.width = (jRect.right - jRect.left) + "px";
                panel.style.height = Math.max(200, h) + "px";
            }
            // Server time centered over journal column
            var timeEl = document.getElementById("spanServerTime");
            if (timeEl) {
                timeEl.style.left = (jRect.left + (jRect.right - jRect.left) / 2 - headerRect.left) + "px";
            }
        }

        // Search width — end at right edge of Address column
        var thAddr = document.querySelector("th.tc-col-address");
        var searchWrap = document.querySelector(".tc-search-wrapper");
        if (thAddr && searchWrap) {
            var aRect = thAddr.getBoundingClientRect();
            var wRect = searchWrap.getBoundingClientRect();
            var w = aRect.right - wRect.left - 8;
            if (w > 80) searchWrap.style.width = w + "px";
        }

        // Online stats block — centered over Связь column
        var thOnline = document.querySelector("th.tc-col-online");
        var statOnline = document.getElementById("tcHdrOnlineStats");
        if (thOnline && statOnline) {
            var r = thOnline.getBoundingClientRect();
            statOnline.style.left = (r.left + r.width / 2 - headerRect.left) + "px";
        }

        // Flood stats — split over Состояние затопления column halves
        var thFlood = document.querySelector("th.tc-col-flooding");
        var stat200 = document.getElementById("tcHdrFlood200Stats");
        var stat700 = document.getElementById("tcHdrFlood700Stats");
        if (thFlood) {
            var fr = thFlood.getBoundingClientRect();
            if (stat200) stat200.style.left = (fr.left + fr.width * 0.25 - headerRect.left) + "px";
            if (stat700) stat700.style.left = (fr.left + fr.width * 0.75 - headerRect.left) + "px";
        }
    }

    function renderJournalEvents() {
        var box = document.getElementById("tcJournalEvents");
        if (!box) return;
        var now = Date.now();
        var html = "";
        var hasItems = false;

        for (var idStr in activeFloodEvents) {
            if (!activeFloodEvents.hasOwnProperty(idStr)) continue;
            var ev = activeFloodEvents[idStr];
            if (ev.kind === "flood200" && !journalFilter.show200) continue;
            if (ev.kind === "flood700" && !journalFilter.show700) continue;
            hasItems = true;

            var elapsed = ev.startMs > 0 ? formatDuration(now - ev.startMs) : "";
            var timeStr = ev.startMs > 0 ? formatChatTime(ev.startMs) : "";
            var kindLabel = ev.kind === "flood700" ? "700мм" : "200мм";
            var cls = "tc-je " + (ev.kind === "flood700" ? "tc-je-700" : "tc-je-200");

            var ackHtml = "";
            if (ev.kind === "flood700") {
                var ack = ackedByItem[parseInt(idStr)];
                if (ack) {
                    var responseMs = (ack.ackedAtMs > 0 && ev.startMs > 0) ? ack.ackedAtMs - ev.startMs : 0;
                    var responseStr = responseMs > 0 ? "за " + formatDuration(responseMs) : "";
                    var byStr = ack.ackedBy ? " · " + escapeHtml(ack.ackedBy) : "";
                    var cmtStr = ack.comment ? ": " + escapeHtml(ack.comment) : "";
                    ackHtml = '<div class="tc-je-ack">' +
                        '<i class="fa-solid fa-circle-check"></i>' +
                        '<span>' + responseStr + byStr + cmtStr + '</span>' +
                    '</div>';
                }
            }

            html += '<div class="' + cls + '">' +
                '<div class="tc-je-top">' +
                    '<span class="tc-je-kind">' + kindLabel + '</span>' +
                    '<span class="tc-je-name">' + escapeHtml(ev.name) + '</span>' +
                '</div>' +
                '<div class="tc-je-bottom">' +
                    '<span class="tc-je-since">с ' + timeStr + '</span>' +
                    '<span class="tc-je-elapsed" id="jev-elapsed-' + idStr + '">' + elapsed + '</span>' +
                '</div>' +
                ackHtml +
            '</div>';
        }

        if (!hasItems) {
            html = '<div class="tc-journal-empty">Нет активных событий</div>';
        }
        box.innerHTML = html;
    }

    function updateJournalEventTimers() {
        var now = Date.now();
        for (var idStr in activeFloodEvents) {
            if (!activeFloodEvents.hasOwnProperty(idStr)) continue;
            var ev = activeFloodEvents[idStr];
            if (!ev.startMs) continue;
            var el = document.getElementById("jev-elapsed-" + idStr);
            if (el) el.textContent = formatDuration(now - ev.startMs);
        }
        // Also update ack pending timers
        var ackEls = document.querySelectorAll(".tc-ack-elapsed");
        for (var i = 0; i < ackEls.length; i++) {
            var startMs = parseInt(ackEls[i].getAttribute("data-start")) || 0;
            if (startMs > 0) ackEls[i].textContent = formatDuration(now - startMs);
        }
    }

    function renderJournalAck() {
        var box = document.getElementById("tcJournalAck");
        if (!box) return;

        // Preserve selected reason and typed comment across rebuilds
        var savedStates = {};
        var existingItems = box.querySelectorAll(".tc-ack-item");
        for (var si = 0; si < existingItems.length; si++) {
            var sid = existingItems[si].getAttribute("data-item-id");
            var activeBtn = existingItems[si].querySelector(".tc-ack-reason-btn-active");
            var sta = existingItems[si].querySelector(".tc-ack-comment");
            savedStates[sid] = {
                reason: activeBtn ? activeBtn.getAttribute("data-reason") : "",
                comment: sta ? sta.value : ""
            };
        }

        var now = Date.now();
        var html = "";
        var count = 0;

        // Build reason pill buttons HTML (shared for all items)
        var reasonBtnsHtml = '<div class="tc-ack-reasons">';
        for (var ri = 0; ri < ACK_REASONS.length; ri++) {
            reasonBtnsHtml += '<button class="tc-ack-reason-btn" data-reason="' +
                escapeAttr(ACK_REASONS[ri]) + '">' + escapeHtml(ACK_REASONS[ri]) + '</button>';
        }
        reasonBtnsHtml += '</div>';

        for (var idStr in pendingAckItems) {
            if (!pendingAckItems.hasOwnProperty(idStr)) continue;
            var pa = pendingAckItems[idStr];
            count++;
            var elapsed = pa.flood700StartMs > 0 ? formatDuration(now - pa.flood700StartMs) : "";
            var timeStr = pa.flood700StartMs > 0 ? formatChatTime(pa.flood700StartMs) : "";

            html += '<div class="tc-ack-item" data-item-id="' + idStr + '">' +
                '<div class="tc-ack-header">' +
                    '<span class="tc-ack-kind">700мм</span>' +
                    '<span class="tc-ack-name">' + escapeHtml(pa.itemName) + '</span>' +
                '</div>' +
                '<div class="tc-ack-info">' +
                    'с ' + timeStr +
                    ' <span class="tc-ack-elapsed" data-start="' + pa.flood700StartMs + '">' + elapsed + '</span>' +
                '</div>' +
                reasonBtnsHtml +
                '<textarea class="tc-ack-comment" placeholder="Дополнительный комментарий..." rows="2"></textarea>' +
                '<button class="tc-ack-submit" data-item-id="' + idStr + '" ' +
                    'data-item-name="' + escapeAttr(pa.itemName) + '" ' +
                    'data-flood-start="' + pa.flood700StartMs + '">Квитировать</button>' +
            '</div>';
        }

        if (count === 0) {
            html = '<div class="tc-journal-empty">Нет событий для квитирования</div>';
        }
        box.innerHTML = html;

        // Restore previously selected reason and typed comment
        var newItems = box.querySelectorAll(".tc-ack-item");
        for (var ni = 0; ni < newItems.length; ni++) {
            var nid = newItems[ni].getAttribute("data-item-id");
            var state = savedStates[nid];
            if (!state) continue;
            if (state.reason) {
                var rBtns = newItems[ni].querySelectorAll(".tc-ack-reason-btn");
                for (var rb = 0; rb < rBtns.length; rb++) {
                    if (rBtns[rb].getAttribute("data-reason") === state.reason) {
                        rBtns[rb].classList.add("tc-ack-reason-btn-active");
                        break;
                    }
                }
            }
            if (state.comment) {
                var rta = newItems[ni].querySelector(".tc-ack-comment");
                if (rta) rta.value = state.comment;
            }
        }

        // Bind reason button toggles (radio-style: one active per item)
        var reasonBtns = box.querySelectorAll(".tc-ack-reason-btn");
        for (var b = 0; b < reasonBtns.length; b++) {
            reasonBtns[b].addEventListener("click", function () {
                var item = this.closest(".tc-ack-item");
                var siblings = item.querySelectorAll(".tc-ack-reason-btn");
                for (var s = 0; s < siblings.length; s++) siblings[s].classList.remove("tc-ack-reason-btn-active");
                this.classList.add("tc-ack-reason-btn-active");
                item.querySelector(".tc-ack-reasons").classList.remove("tc-ack-reasons-error");
            });
        }

        // Bind submit buttons
        var btns = box.querySelectorAll(".tc-ack-submit");
        for (var j = 0; j < btns.length; j++) {
            btns[j].addEventListener("click", function () {
                var itemId = parseInt(this.getAttribute("data-item-id"));
                var itemName = this.getAttribute("data-item-name");
                var floodStart = parseInt(this.getAttribute("data-flood-start")) || 0;
                var container = this.closest(".tc-ack-item");
                var activeReason = container ? container.querySelector(".tc-ack-reason-btn-active") : null;
                var reason = activeReason ? activeReason.getAttribute("data-reason") : "";
                if (!reason) {
                    var reasonsBox = container && container.querySelector(".tc-ack-reasons");
                    if (reasonsBox) reasonsBox.classList.add("tc-ack-reasons-error");
                    return;
                }
                var textarea = container ? container.querySelector(".tc-ack-comment") : null;
                var extra = textarea ? textarea.value.trim() : "";
                var fullComment = reason + (extra ? "\n" + extra : "");
                submitAcknowledgment(itemId, itemName, floodStart, fullComment, this);
            });
        }
    }

    function submitAcknowledgment(itemId, itemName, floodStartMs, comment, btn) {
        if (btn) btn.disabled = true;
        $.ajax({
            url: "/Api/ThermalCamera/PostAcknowledgment",
            type: "POST",
            contentType: "application/json",
            data: JSON.stringify({
                itemId: itemId,
                itemName: itemName,
                floodStartMs: floodStartMs,
                comment: comment
            }),
            dataType: "json",
            success: function (dto) {
                if (dto && dto.ok) {
                    delete pendingAckItems[itemId];
                    // Optimistic update: mark as acked immediately so the active-events
                    // panel and the 700mm cell reflect the ack before the next server poll.
                    ackedByItem[itemId] = {
                        flood700StartMs: floodStartMs,
                        ackedAtMs: Date.now(),
                        ackedBy: "",
                        comment: comment
                    };
                    renderJournalAck();
                    renderJournalEvents();
                    loadAckHistory();
                } else {
                    if (btn) btn.disabled = false;
                    alert("Ошибка квитирования: " + (dto && dto.msg ? dto.msg : "?"));
                }
            },
            error: function () {
                if (btn) btn.disabled = false;
                alert("Ошибка соединения при квитировании");
            }
        });
    }

    function loadAckHistory() {
        $.ajax({
            url: "/Api/ThermalCamera/GetAckHistory",
            type: "GET",
            dataType: "json",
            success: function (dto) {
                if (dto && dto.ok && dto.data) {
                    ackHistory = dto.data;
                    renderAckHistory();
                }
            }
        });
    }

    function renderAckHistory() {
        var box = document.getElementById("tcJournalAckHistory");
        if (!box) return;
        if (!ackHistory || !ackHistory.length) {
            box.innerHTML = '<div class="tc-journal-empty">История пуста</div>';
            return;
        }
        var html = "";
        var limit = Math.min(ackHistory.length, 50);
        for (var i = 0; i < limit; i++) {
            var rec = ackHistory[i];
            var ackTime = formatChatTime(rec.ackedAtMs);
            var floodTime = rec.floodStartMs > 0 ? formatChatTime(rec.floodStartMs) : "—";
            html += '<div class="tc-ack-hist-item">' +
                '<div class="tc-ack-hist-header">' +
                    '<span class="tc-ack-kind">700мм</span>' +
                    '<span class="tc-ack-name">' + escapeHtml(rec.itemName || "") + '</span>' +
                    '<span class="tc-ack-hist-time">' + ackTime + '</span>' +
                '</div>' +
                '<div class="tc-ack-hist-flood">Затопление с ' + floodTime + '</div>' +
                '<div class="tc-ack-hist-by">' + escapeHtml(rec.ackedBy || "") + ':</div>' +
                '<div class="tc-ack-hist-comment">' + escapeHtml(rec.comment || "") + '</div>' +
            '</div>';
        }
        box.innerHTML = html;
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
            '<div class="tc-chat-resize-grip"></div>' +
            '<div class="tc-chat-header">' +
                '<div class="tc-chat-header-title">' +
                    '<i class="fa-solid fa-comments"></i> ' + titleParts +
                '</div>' +
                '<div class="tc-chat-header-actions">' +
                    '<button type="button" class="tc-menu-close tc-chat-close" aria-label="Закрыть">' +
                        '<i class="fa-solid fa-xmark"></i>' +
                    '</button>' +
                '</div>' +
            '</div>' +
            '<div class="tc-chat-messages"></div>' +
            '<div class="tc-chat-input-row">' +
                '<textarea class="tc-chat-input" rows="2" ' +
                    'placeholder="Введите сообщение..." maxlength="2000"></textarea>' +
                '<button type="button" class="tc-chat-send">' +
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
        selectedMessageId = null;

        repositionChat();
        bindPanelEvents(itemId);
        syncChatTriggerHighlights();
        updateChatDistrictNotice();

        if (!chatHistoryLoaded[itemId]) {
            loadChatHistory(itemId);
        } else {
            renderChatMessages(itemId);
            scrollChatToBottom(itemId);
            var input = panel.querySelector(".tc-chat-input");
            if (input) input.focus();
        }
    }

    function repositionChat() {
        if (activeChatId === null) return;
        var p = chatPanels[activeChatId];
        if (!p || p.dragged) return;
        var w = 540, h = 480;
        var left = Math.max(0, Math.round((window.innerWidth - w) / 2));
        var top = Math.max(60, Math.round((window.innerHeight - h) / 2));
        p.el.style.left = left + "px";
        p.el.style.top = top + "px";
        p.el.style.width = w + "px";
        p.el.style.height = h + "px";
    }

    function bindPanelEvents(itemId) {
        var p = chatPanels[itemId];
        if (!p) return;
        var panel = p.el;

        var closeBtn = panel.querySelector(".tc-chat-close");
        var sendBtn = panel.querySelector(".tc-chat-send");
        var inputEl = panel.querySelector(".tc-chat-input");
        var header = panel.querySelector(".tc-chat-header");
        var grip = panel.querySelector(".tc-chat-resize-grip");

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

        // Drag by header
        if (header) {
            var dragging = false, dragStartX, dragStartY, dragLeft, dragTop;
            header.addEventListener("mousedown", function (e) {
                if (e.target.closest && e.target.closest(".tc-chat-close")) return;
                dragging = true;
                dragStartX = e.clientX;
                dragStartY = e.clientY;
                dragLeft = parseInt(panel.style.left) || 0;
                dragTop = parseInt(panel.style.top) || 0;
                e.preventDefault();
                function onDragMove(ev) {
                    if (!dragging) return;
                    p.dragged = true;
                    panel.style.left = (dragLeft + ev.clientX - dragStartX) + "px";
                    panel.style.top = (dragTop + ev.clientY - dragStartY) + "px";
                }
                function onDragUp() {
                    dragging = false;
                    document.removeEventListener("mousemove", onDragMove);
                    document.removeEventListener("mouseup", onDragUp);
                }
                document.addEventListener("mousemove", onDragMove);
                document.addEventListener("mouseup", onDragUp);
            });
        }

        // Resize from upper-left grip
        if (grip) {
            grip.addEventListener("mousedown", function (e) {
                e.stopPropagation();
                var rStartX = e.clientX, rStartY = e.clientY;
                var rStartW = panel.offsetWidth || 540;
                var rStartH = panel.offsetHeight || 480;
                var rStartLeft = parseInt(panel.style.left) || 0;
                var rStartTop = parseInt(panel.style.top) || 0;
                e.preventDefault();
                function onRMove(ev) {
                    var dx = ev.clientX - rStartX;
                    var dy = ev.clientY - rStartY;
                    var newW = Math.max(320, rStartW - dx);
                    var newH = Math.max(250, rStartH - dy);
                    panel.style.width = newW + "px";
                    panel.style.height = newH + "px";
                    panel.style.left = (rStartLeft + rStartW - newW) + "px";
                    panel.style.top = (rStartTop + rStartH - newH) + "px";
                    p.dragged = true;
                }
                function onRUp() {
                    document.removeEventListener("mousemove", onRMove);
                    document.removeEventListener("mouseup", onRUp);
                }
                document.addEventListener("mousemove", onRMove);
                document.addEventListener("mouseup", onRUp);
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
                ? (m.kind === "flood700" ? "tc-chat-flood700" :
                   m.kind === "ack"     ? "tc-chat-ack"     : "tc-chat-flood200")
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
                    '" aria-label="Удалить"><i class="fa-solid fa-xmark"></i></button>') +
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

    function initPhotoModal() {
        var overlay = document.getElementById("tcPhotoModal");
        if (!overlay) return;
        overlay.addEventListener("click", function (e) {
            if (e.target === overlay) closePhotoModal();
        });
        var closeBtn = overlay.querySelector(".tc-menu-close");
        if (closeBtn) closeBtn.addEventListener("click", closePhotoModal);
        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && overlay.classList.contains("show")) closePhotoModal();
        });
    }

    function showPhoto(photoPath, name) {
        var overlay  = document.getElementById("tcPhotoModal");
        var img      = document.getElementById("imgPhoto");
        var errorDiv = document.getElementById("divPhotoError");
        var titleEl  = document.getElementById("tcPhotoTitle");
        if (!overlay) return;

        var url = "/Api/ThermalCamera/GetPhoto?path=" + encodeURIComponent(photoPath);
        if (titleEl) titleEl.textContent = "Фото: " + (name || "объекта");
        if (errorDiv) errorDiv.classList.add("d-none");
        if (img) {
            img.classList.remove("d-none");
            img.src = "";
            img.src = url;
            img.onerror = function () {
                img.classList.add("d-none");
                if (errorDiv) errorDiv.classList.remove("d-none");
            };
            img.onload = function () {
                if (errorDiv) errorDiv.classList.add("d-none");
            };
        }
        overlay.classList.add("show");
    }

    function closePhotoModal() {
        var overlay = document.getElementById("tcPhotoModal");
        if (overlay) overlay.classList.remove("show");
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
