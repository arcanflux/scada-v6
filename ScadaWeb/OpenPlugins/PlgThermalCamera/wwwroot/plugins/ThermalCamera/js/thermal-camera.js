// Thermal Camera Plugin - Real-time table with channel data
// Плагин тепловых камер - Таблица с данными каналов в реальном времени

var thermalCamera = (function () {
    // Constants
    var UPDATE_INTERVAL = 3000; // ms
    var COMMENT_SAVE_DELAY = 1000; // ms debounce

    // State
    var items = [];
    var userData = {};
    var allCnlNums = [];
    var commentTimers = {};
    var updateTimer = null;

    // Initialize the table
    function init() {
        if (typeof thermalCameraData === "undefined") return;

        items = thermalCameraData.items || [];
        userData = thermalCameraData.userData || {};

        collectChannelNumbers();
        renderTable();
        requestData();
        startAutoUpdate();
    }

    // Collect all unique channel numbers from items
    function collectChannelNumbers() {
        var cnlSet = {};
        for (var i = 0; i < items.length; i++) {
            var item = items[i];
            if (item.onlineCnlNum > 0) cnlSet[item.onlineCnlNum] = true;
            if (item.floodingSignals) {
                for (var j = 0; j < item.floodingSignals.length; j++) {
                    var sig = item.floodingSignals[j];
                    if (sig.statusCnlNum > 0) cnlSet[sig.statusCnlNum] = true;
                    if (sig.temperatureCnlNum > 0) cnlSet[sig.temperatureCnlNum] = true;
                }
            }
        }
        allCnlNums = Object.keys(cnlSet).map(Number).sort(function (a, b) { return a - b; });
    }

    // Render the table body
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
                '<span class="badge bg-secondary">' + escapeHtml(item.districtNumber.toString()) + '</span></td>';

            // 2. Object name
            html += '<td class="tc-col-name">' +
                '<span class="tc-marker-icon">&#9650;</span> ' +
                escapeHtml(item.name) + '</td>';

            // 3. Address + photo button
            html += '<td class="tc-col-address">' +
                '<span class="tc-address-text">' + escapeHtml(item.address) + '</span>';
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
                '<i class="fa-solid fa-circle"></i> <span class="tc-online-text">—</span></span></td>';

            // 5. Flooding status signals
            html += '<td class="tc-col-flooding"><div class="tc-flooding-container">';
            if (item.floodingSignals && item.floodingSignals.length > 0) {
                for (var j = 0; j < item.floodingSignals.length; j++) {
                    var sig = item.floodingSignals[j];
                    html += '<div class="tc-flooding-signal" id="flood-' + item.id + '-' + j + '">' +
                        '<div class="tc-signal-label">' + escapeHtml(sig.label) + '</div>' +
                        '<div class="tc-signal-body">' +
                        '<div class="tc-signal-status" id="flood-status-' + item.id + '-' + j + '">' +
                        '<i class="fa-solid fa-droplet"></i></div>' +
                        '<div class="tc-signal-temp" id="flood-temp-' + item.id + '-' + j + '">—</div>' +
                        '</div></div>';
                }
            } else {
                html += '<span class="text-muted">—</span>';
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

    // Bind events to dynamically created elements
    function bindEvents() {
        // Comment auto-save with debounce
        var commentInputs = document.querySelectorAll(".tc-comment-input");
        for (var i = 0; i < commentInputs.length; i++) {
            commentInputs[i].addEventListener("input", function () {
                var itemId = parseInt(this.getAttribute("data-item-id"));
                var value = this.value;
                debounceComment(itemId, value);
            });
        }

        // Commissioned checkbox
        var checkboxes = document.querySelectorAll(".tc-commissioned-cb");
        for (var i = 0; i < checkboxes.length; i++) {
            checkboxes[i].addEventListener("change", function () {
                var itemId = parseInt(this.getAttribute("data-item-id"));
                var isChecked = this.checked;
                saveCommissioned(itemId, isChecked);
            });
        }

        // Refresh button
        var btnRefresh = document.getElementById("btnRefresh");
        if (btnRefresh) {
            btnRefresh.addEventListener("click", function () {
                requestData();
            });
        }
    }

    // Request current data from API
    function requestData() {
        if (allCnlNums.length === 0) return;

        var url = "/Api/ThermalCamera/GetCurData?cnlNums=" + allCnlNums.join(",");
        $.ajax({
            url: url,
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

    // Update the table with fresh data
    function updateTableData(result) {
        var data = result.data || {};

        // Update server time
        if (result.serverTime) {
            var timeSpan = document.getElementById("spanServerTime");
            if (timeSpan) {
                var dt = new Date(result.serverTime);
                timeSpan.textContent = dt.toLocaleTimeString();
            }
        }

        for (var i = 0; i < items.length; i++) {
            var item = items[i];

            // Update online status
            if (item.onlineCnlNum > 0) {
                var onlineEl = document.getElementById("online-" + item.id);
                if (onlineEl) {
                    var onlineData = data[item.onlineCnlNum];
                    if (onlineData) {
                        var isOnline = onlineData.val !== 0 && onlineData.stat > 0;
                        var textEl = onlineEl.querySelector(".tc-online-text");
                        onlineEl.className = "tc-online-indicator " +
                            (onlineData.stat <= 0 ? "tc-status-unknown" :
                                isOnline ? "tc-status-online" : "tc-status-offline");
                        if (textEl) {
                            textEl.textContent = onlineData.stat <= 0 ? "—" :
                                isOnline ? "Online" : "Offline";
                        }
                    }
                }
            }

            // Update flooding signals
            if (item.floodingSignals) {
                for (var j = 0; j < item.floodingSignals.length; j++) {
                    var sig = item.floodingSignals[j];
                    var signalEl = document.getElementById("flood-" + item.id + "-" + j);
                    var statusEl = document.getElementById("flood-status-" + item.id + "-" + j);
                    var tempEl = document.getElementById("flood-temp-" + item.id + "-" + j);

                    // Update flooding status (color)
                    if (statusEl && sig.statusCnlNum > 0) {
                        var statusData = data[sig.statusCnlNum];
                        if (statusData) {
                            var isFlooded = statusData.val !== 0 && statusData.stat > 0;
                            var isUnknown = statusData.stat <= 0;
                            if (signalEl) {
                                signalEl.className = "tc-flooding-signal " +
                                    (isUnknown ? "tc-flood-unknown" :
                                        isFlooded ? "tc-flood-alarm" : "tc-flood-normal");
                            }
                        }
                    }

                    // Update temperature value
                    if (tempEl && sig.temperatureCnlNum > 0) {
                        var tempData = data[sig.temperatureCnlNum];
                        if (tempData) {
                            if (tempData.stat > 0) {
                                tempEl.textContent = tempData.text || tempData.val.toFixed(1) + "°C";
                            } else {
                                tempEl.textContent = "—";
                            }
                        }
                    }
                }
            }
        }
    }

    // Start automatic data updates
    function startAutoUpdate() {
        if (updateTimer) clearInterval(updateTimer);
        updateTimer = setInterval(requestData, UPDATE_INTERVAL);
    }

    // Debounce comment saving
    function debounceComment(itemId, value) {
        if (commentTimers[itemId]) clearTimeout(commentTimers[itemId]);
        commentTimers[itemId] = setTimeout(function () {
            saveComment(itemId, value);
        }, COMMENT_SAVE_DELAY);
    }

    // Save comment via API
    function saveComment(itemId, comment) {
        $.ajax({
            url: "/Api/ThermalCamera/SaveComment",
            type: "POST",
            contentType: "application/json",
            data: JSON.stringify({ itemId: itemId, comment: comment }),
            dataType: "json",
            success: function (dto) {
                if (!dto || !dto.ok) {
                    console.error("ThermalCamera: failed to save comment: " + (dto ? dto.msg : ""));
                }
            },
            error: function () {
                console.error("ThermalCamera: failed to save comment");
            }
        });
    }

    // Save commissioned status via API
    function saveCommissioned(itemId, isCommissioned) {
        $.ajax({
            url: "/Api/ThermalCamera/SaveCommissioned",
            type: "POST",
            contentType: "application/json",
            data: JSON.stringify({ itemId: itemId, isCommissioned: isCommissioned }),
            dataType: "json",
            success: function (dto) {
                if (!dto || !dto.ok) {
                    console.error("ThermalCamera: failed to save status: " + (dto ? dto.msg : ""));
                }
            },
            error: function () {
                console.error("ThermalCamera: failed to save status");
            }
        });
    }

    // Show photo in modal
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

    // Utility: escape HTML
    function escapeHtml(text) {
        var div = document.createElement("div");
        div.appendChild(document.createTextNode(text));
        return div.innerHTML;
    }

    // Utility: escape attribute value
    function escapeAttr(text) {
        return text.replace(/'/g, "\\'").replace(/"/g, '\\"');
    }

    // Public API
    return {
        init: init,
        showPhoto: showPhoto,
        refresh: requestData
    };
})();

// Initialize when DOM is ready
$(document).ready(function () {
    thermalCamera.init();
});
