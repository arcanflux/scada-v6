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

    // Count normal→flooded transitions (prev val >= 1, cur val === 0) per channel.
    function countTransitions(histData, floodChannels) {
        var result = { count200: 0, count700: 0 };
        if (!histData || !histData.cnlNums || !histData.trends) return result;
        var cnlIdx = {};
        histData.cnlNums.forEach(function (n, i) { cnlIdx[n] = i; });
        for (var i = 0; i < floodChannels.length; i++) {
            var ch = floodChannels[i];
            var idx = cnlIdx[ch.cnlNum];
            if (idx === undefined) continue;
            var trend = histData.trends[idx];
            var prevVal = null, count = 0;
            for (var j = 0; j < trend.length; j++) {
                var rec = trend[j];
                if (!rec || !rec.d || rec.d.stat <= 0) { prevVal = null; continue; }
                if (prevVal !== null && prevVal >= 1 && rec.d.val === 0) count++;
                prevVal = rec.d.val;
            }
            result['count' + ch.kind] = count;
        }
        return result;
    }

    function buildFloodDef(item) {
        var channels = [];
        if (item.flood200CnlNum > 0) channels.push({ cnlNum: item.flood200CnlNum, kind: '200' });
        if (item.flood700CnlNum > 0) channels.push({ cnlNum: item.flood700CnlNum, kind: '700' });
        return channels.length ? { name: item.name, itemId: item.id, channels: channels } : null;
    }

    // Monthly count for current calendar month (used by cell hover tooltip).
    // Goes through the global single-flight queue and dedups concurrent
    // hovers on the same item to a single in-flight Promise.
    function fetchCurrentMonthCount(item) {
        var def = buildFloodDef(item);
        if (!def) return Promise.resolve(null);
        var now = new Date();
        var key = "m_" + item.id + "_" + now.getFullYear() + "_" + now.getMonth();
        if (inFlightPromises[key]) return inFlightPromises[key];

        var promise = enqueueFetch(function () {
            var monthStart = new Date(now.getFullYear(), now.getMonth(), 1, 0, 0, 0, 0);
            return fetchHistData(def.channels.map(function (c) { return c.cnlNum; }),
                                 monthStart, now)
                .then(function (histData) { return countTransitions(histData, def.channels); });
        });
        promise = promise.finally(function () { delete inFlightPromises[key]; });
        inFlightPromises[key] = promise;
        return promise;
    }

    // Counts per month for a whole year. Single GetHistData request for the
    // entire year range, then bucketed client-side — replaces 12 sequential
    // requests to ease pressure on the SCADA server.
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

    // Splits 1→0 (normal→flooded) transitions across the months of `year`.
    // histData.timestamps[j].ms — ms-since-epoch (same shape as PlgMap chart).
    function bucketByMonth(histData, floodChannels, year, lastMonthIdx) {
        var months = [];
        for (var m = 0; m <= lastMonthIdx; m++) {
            months.push({ month: m, count200: 0, count700: 0 });
        }
        if (!histData || !histData.cnlNums || !histData.trends || !histData.timestamps) {
            return months;
        }
        var cnlIdx = {};
        histData.cnlNums.forEach(function (n, i) { cnlIdx[n] = i; });
        var ts = histData.timestamps;
        for (var c = 0; c < floodChannels.length; c++) {
            var ch = floodChannels[c];
            var idx = cnlIdx[ch.cnlNum];
            if (idx === undefined) continue;
            var trend = histData.trends[idx];
            var prevVal = null;
            for (var j = 0; j < trend.length; j++) {
                var rec = trend[j];
                if (!rec || !rec.d || rec.d.stat <= 0) { prevVal = null; continue; }
                if (prevVal !== null && prevVal >= 1 && rec.d.val === 0) {
                    var msRec = ts[j];
                    var msVal = msRec && typeof msRec.ms === 'number' ? msRec.ms :
                                (typeof msRec === 'number' ? msRec : null);
                    if (msVal !== null) {
                        var t = new Date(msVal);
                        if (t.getFullYear() === year) {
                            var bucket = months[t.getMonth()];
                            if (bucket) bucket['count' + ch.kind]++;
                        }
                    }
                }
                prevVal = rec.d.val;
            }
        }
        return months;
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
            total200 += m.count200;
            total700 += m.count700;
            var total = m.count200 + m.count700;
            rows += '<tr' + (total > 0 ? ' class="tc-fh-has-events"' : '') + '>' +
                '<td class="tc-fh-month">' + MONTH_NAMES[m.month] + '</td>' +
                '<td class="tc-fh-c200">' + m.count200 + '</td>' +
                '<td class="tc-fh-c700">' + m.count700 + '</td>' +
                '<td class="tc-fh-total">' + total + '</td>' +
                '</tr>';
        }
        var grandTotal = total200 + total700;
        body.innerHTML =
            '<table class="tc-fh-table">' +
                '<thead>' +
                    '<tr>' +
                        '<th>Месяц</th>' +
                        '<th><span class="tc-fh-dot tc-fh-dot-200"></span>200мм</th>' +
                        '<th><span class="tc-fh-dot tc-fh-dot-700"></span>700мм</th>' +
                        '<th>Всего</th>' +
                    '</tr>' +
                '</thead>' +
                '<tbody>' + rows + '</tbody>' +
                '<tfoot>' +
                    '<tr class="tc-fh-total-row">' +
                        '<td>Итого</td>' +
                        '<td>' + total200 + '</td>' +
                        '<td>' + total700 + '</td>' +
                        '<td>' + grandTotal + '</td>' +
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

        var closeBtn = modal.querySelector(".tc-fh-close");
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
        fetchCurrentMonthCount: fetchCurrentMonthCount,
        openHistoryModal: openHistoryModal,
        closeHistoryModal: closeHistoryModal
    };
})();
