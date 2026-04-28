// Thermal Camera — Flood history module
// Replaces the previous Chart.js overlay with a lightweight statistics view:
// - hover: current-month flood count (200мм + 700мм) for the TK under cursor
// - click: modal with monthly breakdown for a selectable year

var tcFloodHistory = (function () {
    var MONTH_NAMES = ['Январь', 'Февраль', 'Март', 'Апрель', 'Май', 'Июнь',
                       'Июль', 'Август', 'Сентябрь', 'Октябрь', 'Ноябрь', 'Декабрь'];
    var DATA_BASELINE_YEAR = 2026;

    function rootPath() {
        var base = document.querySelector("base");
        return base ? base.getAttribute("href") : "/";
    }

    function apiUrl(path) { return rootPath() + "Api/Main/" + path; }

    // ---- Single-flight queue + per-key dedup ----
    // Prevents a wave of parallel GetHistData requests when the user hovers
    // over many TKs in quick succession (which previously saturated the
    // SCADA server's network and CPU).
    var inFlightPromises = {};   // key -> Promise (dedup identical requests)
    var fetchQueue = [];          // queue of { run, resolve, reject }
    var fetchInProgress = false;

    function enqueueFetch(taskFn) {
        return new Promise(function (resolve, reject) {
            fetchQueue.push({ run: taskFn, resolve: resolve, reject: reject });
            processQueue();
        });
    }

    function processQueue() {
        if (fetchInProgress || !fetchQueue.length) return;
        fetchInProgress = true;
        var task = fetchQueue.shift();
        Promise.resolve()
            .then(function () { return task.run(); })
            .then(task.resolve, task.reject)
            .finally(function () {
                fetchInProgress = false;
                processQueue();
            });
    }

    // GET /Api/Main/GetHistData — standard PlgMain endpoint
    async function fetchHistData(cnlNums, startTime, endTime) {
        if (!cnlNums || !cnlNums.length) return null;
        var url = apiUrl("GetHistData") +
            "?archiveBit=1" +
            "&startTime=" + encodeURIComponent(startTime.toISOString()) +
            "&endTime=" + encodeURIComponent(endTime.toISOString()) +
            "&endInclusive=true" +
            "&cnlNums=" + encodeURIComponent(cnlNums.join(","));
        try {
            var resp = await fetch(url);
            var dto = await resp.json();
            if (dto.ok && dto.data) return dto.data;
        } catch (err) {
            console.error("Flood history fetch failed:", err);
        }
        return null;
    }

    function buildFloodDef(item) {
        var channels = [];
        if (item.flood200CnlNum > 0) channels.push({ cnlNum: item.flood200CnlNum, kind: '200' });
        if (item.flood700CnlNum > 0) channels.push({ cnlNum: item.flood700CnlNum, kind: '700' });
        return channels.length ? { name: item.name, itemId: item.id, channels: channels } : null;
    }

    // Total flood duration per month for a whole year. Single GetHistData
    // request for the entire year range, then integrated client-side.
    function fetchYearlyCounts(item, year) {
        var def = buildFloodDef(item);
        if (!def) return Promise.resolve(null);
        var key = "y_" + item.id + "_" + year;
        if (inFlightPromises[key]) return inFlightPromises[key];

        var now = new Date();
        var lastMonthIdx = (year === now.getFullYear()) ? now.getMonth() : 11;
        var yearStart = new Date(year, 0, 1, 0, 0, 0, 0);
        var yearEnd = (year === now.getFullYear()) ? now :
                      new Date(year, 11, 31, 23, 59, 59, 999);
        var cnls = def.channels.map(function (c) { return c.cnlNum; });

        var promise = enqueueFetch(function () {
            return fetchHistData(cnls, yearStart, yearEnd).then(function (histData) {
                return bucketByMonth(histData, def.channels, year, lastMonthIdx);
            });
        });
        promise = promise.finally(function () { delete inFlightPromises[key]; });
        inFlightPromises[key] = promise;
        return promise;
    }

    // Integrate total flood duration per month. For each record in the trend
    // that is in flooded state (val === 0, stat > 0), the device is treated as
    // flooded from ts[j].ms until the next record's timestamp (or the end of
    // the requested period for the trailing record). The span is split across
    // month boundaries so each calendar month gets only its overlap.
    function bucketByMonth(histData, floodChannels, year, lastMonthIdx) {
        var months = [];
        for (var m = 0; m <= lastMonthIdx; m++) {
            months.push({ month: m, time200: 0, time700: 0 });
        }
        if (!histData || !histData.cnlNums || !histData.trends || !histData.timestamps) {
            return months;
        }
        var cnlIdx = {};
        histData.cnlNums.forEach(function (n, i) { cnlIdx[n] = i; });
        var ts = histData.timestamps;
        var nowMs = Date.now();
        var periodEnd = (year === new Date().getFullYear())
            ? nowMs
            : new Date(year + 1, 0, 1).getTime();

        for (var c = 0; c < floodChannels.length; c++) {
            var ch = floodChannels[c];
            var idx = cnlIdx[ch.cnlNum];
            if (idx === undefined) continue;
            var trend = histData.trends[idx];
            for (var j = 0; j < trend.length; j++) {
                var rec = trend[j];
                if (!rec || !rec.d || rec.d.stat <= 0) continue;
                if (rec.d.val !== 0) continue;
                var startMs = tsMs(ts[j]);
                if (startMs === null) continue;
                var endMs = (j + 1 < trend.length) ? tsMs(ts[j + 1]) : null;
                if (endMs === null) endMs = periodEnd;
                addOverlapToMonths(months, startMs, endMs, year, ch.kind);
            }
        }
        return months;
    }

    function tsMs(rec) {
        if (rec && typeof rec.ms === 'number') return rec.ms;
        if (typeof rec === 'number') return rec;
        return null;
    }

    function addOverlapToMonths(months, startMs, endMs, year, kind) {
        if (endMs <= startMs) return;
        for (var m = 0; m < months.length; m++) {
            var ms0 = new Date(year, m, 1).getTime();
            var ms1 = new Date(year, m + 1, 1).getTime();
            var s = Math.max(startMs, ms0);
            var e = Math.min(endMs, ms1);
            if (e > s) months[m]['time' + kind] += (e - s);
        }
    }

    // Same Nд HH:MM:SS / HH:MM:SS shape as the live timer in thermal-camera.js
    function formatDuration(ms) {
        if (!ms || ms < 0) return "—";
        var sec = Math.floor(ms / 1000);
        var h = Math.floor(sec / 3600);
        var m = Math.floor((sec % 3600) / 60);
        var s = sec % 60;
        var pad = function (n) { return n < 10 ? "0" + n : "" + n; };
        if (h >= 24) {
            var days = Math.floor(h / 24);
            return days + "д " + pad(h % 24) + ":" + pad(m) + ":" + pad(s);
        }
        return pad(h) + ":" + pad(m) + ":" + pad(s);
    }

    // ---- Modal UI ----

    var currentItem = null;

    function openHistoryModal(item) {
        var modal = document.getElementById("tcFloodHistoryModal");
        if (!modal) return;
        currentItem = item;

        var titleEl = document.getElementById("tcFloodHistoryTitle");
        if (titleEl) {
            var name = item.name || ("ТК " + item.id);
            if (item.descr) name += ", " + item.descr;
            titleEl.textContent = "История затоплений: " + name;
        }

        // Populate year selector: DATA_BASELINE_YEAR .. current year (newest first).
        // Past years are hidden because the install has no archive older than 2026;
        // future years appear automatically as the calendar moves forward.
        var yearSel = document.getElementById("tcFloodHistoryYear");
        if (yearSel) {
            var curYear = new Date().getFullYear();
            var startYear = Math.min(DATA_BASELINE_YEAR, curYear);
            yearSel.innerHTML = '';
            for (var y = curYear; y >= startYear; y--) {
                var opt = document.createElement('option');
                opt.value = y;
                opt.textContent = y;
                yearSel.appendChild(opt);
            }
            yearSel.value = curYear;
        }

        modal.classList.add("show");
        loadYearData(new Date().getFullYear());
    }

    function closeHistoryModal() {
        var modal = document.getElementById("tcFloodHistoryModal");
        if (modal) modal.classList.remove("show");
        currentItem = null;
    }

    async function loadYearData(year) {
        if (!currentItem) return;
        var body = document.getElementById("tcFloodHistoryBody");
        if (!body) return;
        body.innerHTML = '<div class="tc-fh-loading">Загрузка...</div>';
        try {
            var months = await fetchYearlyCounts(currentItem, year);
            if (!months || !months.length) {
                body.innerHTML = '<div class="tc-fh-loading">Нет данных</div>';
                return;
            }
            renderTable(body, months);
        } catch (err) {
            console.error(err);
            body.innerHTML = '<div class="tc-fh-loading">Ошибка загрузки</div>';
        }
    }

    function renderTable(body, months) {
        var total200 = 0, total700 = 0;
        var rows = '';
        for (var i = 0; i < months.length; i++) {
            var m = months[i];
            total200 += m.time200;
            total700 += m.time700;
            var hasEvents = m.time200 > 0 || m.time700 > 0;
            rows += '<tr' + (hasEvents ? ' class="tc-fh-has-events"' : '') + '>' +
                '<td class="tc-fh-month">' + MONTH_NAMES[m.month] + '</td>' +
                '<td class="tc-fh-c200">' + formatDuration(m.time200) + '</td>' +
                '<td class="tc-fh-c700">' + formatDuration(m.time700) + '</td>' +
                '</tr>';
        }
        body.innerHTML =
            '<table class="tc-fh-table">' +
                '<thead>' +
                    '<tr>' +
                        '<th>Месяц</th>' +
                        '<th><span class="tc-fh-dot tc-fh-dot-200"></span>200мм</th>' +
                        '<th><span class="tc-fh-dot tc-fh-dot-700"></span>700мм</th>' +
                    '</tr>' +
                '</thead>' +
                '<tbody>' + rows + '</tbody>' +
                '<tfoot>' +
                    '<tr class="tc-fh-total-row">' +
                        '<td>Итого</td>' +
                        '<td>' + formatDuration(total200) + '</td>' +
                        '<td>' + formatDuration(total700) + '</td>' +
                    '</tr>' +
                '</tfoot>' +
            '</table>';
    }

    // ---- Initialization ----

    function init() {
        var modal = document.getElementById("tcFloodHistoryModal");
        if (!modal) return;

        modal.addEventListener("click", function (e) {
            if (e.target === modal) closeHistoryModal();
        });

        var closeBtn = modal.querySelector(".tc-menu-close");
        if (closeBtn) closeBtn.addEventListener("click", closeHistoryModal);

        var yearSel = document.getElementById("tcFloodHistoryYear");
        if (yearSel) {
            yearSel.addEventListener("change", function () {
                loadYearData(parseInt(yearSel.value, 10));
            });
        }

        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && modal.classList.contains("show")) {
                closeHistoryModal();
            }
        });
    }

    document.addEventListener("DOMContentLoaded", init);

    return {
        openHistoryModal: openHistoryModal,
        closeHistoryModal: closeHistoryModal
    };
})();
