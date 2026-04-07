// Thermal Camera Plugin - Real-time table with channel data from .map file
// Плагин тепловых камер - Таблица с данными каналов в реальном времени из файла .map

var thermalCamera = (function () {
    var UPDATE_INTERVAL = 3000;
    var COMMENT_SAVE_DELAY = 1000;

    var items = [];
    var userData = {};
    var allCnlNums = [];
    var commentTimers = {};
    var updateTimer = null;

    function init() {
        if (typeof thermalCameraData === "undefined") return;

        items = thermalCameraData.items || [];
        userData = thermalCameraData.userData || {};

        collectChannelNumbers();
        renderTable();
        requestData();
        startAutoUpdate();
    }

    function collectChannelNumbers() {
        var cnlSet = {};
        for (var i = 0; i < items.length; i++) {
            var item = items[i];
            if (item.onlineCnlNum > 0) cnlSet[item.onlineCnlNum] = true;
            if (item.temp200CnlNum > 0) cnlSet[item.temp200CnlNum] = true;
            if (item.temp700CnlNum > 0) cnlSet[item.temp700CnlNum] = true;
            if (item.flood200CnlNum > 0) cnlSet[item.flood200CnlNum] = true;
            if (item.flood700CnlNum > 0) cnlSet[item.flood700CnlNum] = true;
        }
        allCnlNums = Object.keys(cnlSet).map(Number).sort(function (a, b) { return a - b; });
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

            // 2. Object name (with triangle marker)
            html += '<td class="tc-col-name">' +
                '<span class="tc-marker-icon">&#9650;</span> ' +
                escapeHtml(item.name) + '</td>';

            // 3. Address (descr) + photo button
            html += '<td class="tc-col-address">' +
                '<span class="tc-address-text">' + escapeHtml(item.descr) + '</span>';
            if (item.photoUrl) {
                html += ' <button class="btn btn-sm btn-outline-primary tc-photo-btn" ' +
                    'onclick="thermalCamera.showPhoto(\'' + escapeAttr(item.photoUrl) + '\', \'' +
                    escapeAttr(item.name) + '\')" title="Открыть фото">' +
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
                    '<div class="tc-signal-status" id="flood200-status-' + item.id + '">' +
                    '<i class="fa-solid fa-droplet"></i></div>' +
                    '<div class="tc-signal-temp" id="flood200-temp-' + item.id + '">\u2014</div>' +
                    '</div></div>';
            }

            // 700mm signal
            if (item.flood700CnlNum > 0 || item.temp700CnlNum > 0) {
                html += '<div class="tc-flooding-signal tc-flood-unknown" id="flood700-' + item.id + '">' +
                    '<div class="tc-signal-label">700мм</div>' +
                    '<div class="tc-signal-body">' +
                    '<div class="tc-signal-status" id="flood700-status-' + item.id + '">' +
                    '<i class="fa-solid fa-droplet"></i></div>' +
                    '<div class="tc-signal-temp" id="flood700-temp-' + item.id + '">\u2014</div>' +
                    '</div></div>';
            }

            if (!item.flood200CnlNum && !item.temp200CnlNum && !item.flood700CnlNum && !item.temp700CnlNum) {
                html += '<span class="text-muted">\u2014</span>';
            }
            html += '</div></td>';

            // 6. Comment
            html += '<td class="tc-col-comment">' +
                '<textarea class="form-control form-control-sm tc-comment-input" ' +
                'data-item-id="' + item.id + '" rows="1" ' +
                'placeholder="Комментарий...">' + escapeHtml(ud.comment) + '</textarea></td>';

            // 7. Commissioned status
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
        var commentInputs = document.querySelectorAll(".tc-comment-input");
        for (var i = 0; i < commentInputs.length; i++) {
            commentInputs[i].addEventListener("input", function () {
                var itemId = parseInt(this.getAttribute("data-item-id"));
                var value = this.value;
                debounceComment(itemId, value);
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
        if (allCnlNums.length === 0) return;

        $.ajax({
            url: "/Api/ThermalCamera/GetCurData?cnlNums=" + allCnlNums.join(","),
            type: "GET",
            dataType: "json",
            success: function (dto) {
                if (dto && dto.ok && dto.data) {
                    updateTableData(dto.data);
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
                timeSpan.textContent = dt.toLocaleTimeString();
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
        }
    }

    function updateOnlineStatus(item, data) {
        if (item.onlineCnlNum <= 0) return;
        var onlineEl = document.getElementById("online-" + item.id);
        if (!onlineEl) return;

        var d = data[item.onlineCnlNum];
        if (!d) return;

        var isOnline = d.val !== 0 && d.stat > 0;
        var textEl = onlineEl.querySelector(".tc-online-text");
        onlineEl.className = "tc-online-indicator " +
            (d.stat <= 0 ? "tc-status-unknown" : isOnline ? "tc-status-online" : "tc-status-offline");
        if (textEl) {
            textEl.textContent = d.stat <= 0 ? "\u2014" : isOnline ? "Online" : "Offline";
        }
    }

    function updateFloodingSignal(itemId, size, floodCnlNum, tempCnlNum, data, alarmClass) {
        var signalEl = document.getElementById("flood" + size + "-" + itemId);
        var tempEl = document.getElementById("flood" + size + "-temp-" + itemId);

        // Update flooding status (yellow for 200mm, red for 700mm)
        if (signalEl && floodCnlNum > 0) {
            var fd = data[floodCnlNum];
            if (fd) {
                var isFlooded = fd.val !== 0 && fd.stat > 0;
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
                    ? (td.text || td.val.toFixed(1) + "\u00b0C")
                    : "\u2014";
            }
        }
    }

    function startAutoUpdate() {
        if (updateTimer) clearInterval(updateTimer);
        updateTimer = setInterval(requestData, UPDATE_INTERVAL);
    }

    function debounceComment(itemId, value) {
        if (commentTimers[itemId]) clearTimeout(commentTimers[itemId]);
        commentTimers[itemId] = setTimeout(function () {
            saveComment(itemId, value);
        }, COMMENT_SAVE_DELAY);
    }

    function saveComment(itemId, comment) {
        $.ajax({
            url: "/Api/ThermalCamera/SaveComment",
            type: "POST",
            contentType: "application/json",
            data: JSON.stringify({ itemId: itemId, comment: comment }),
            dataType: "json",
            success: function (dto) {
                if (!dto || !dto.ok) console.error("ThermalCamera: save comment failed");
            }
        });
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

    function showPhoto(url, name) {
        var img = document.getElementById("imgPhoto");
        var errorDiv = document.getElementById("divPhotoError");
        var label = document.getElementById("photoModalLabel");

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
        refresh: requestData
    };
})();

$(document).ready(function () {
    thermalCamera.init();
});
