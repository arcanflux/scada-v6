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
            var daysInMonth = new Date(year, m + 1, 0).getDate();
            var days = [];
            for (var d = 0; d < daysInMonth; d++) days.push({ has200: false, has700: false });
            months.push({ month: m, time200: 0, time700: 0, days: days });
        }
        var archiveOngoing = {}; // kind -> true if archive last record is active flood (extrapolated to now)
        if (!histData || !histData.cnlNums || !histData.trends || !histData.timestamps) {
            return { months: months, archiveOngoing: archiveOngoing };
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
            // Determine if archive already covers the ongoing flood up to now
            // (last valid record is flooded → was extrapolated to periodEnd above)
            for (var k = trend.length - 1; k >= 0; k--) {
                var lr = trend[k];
                if (!lr || !lr.d || lr.d.stat <= 0) continue;
                archiveOngoing[ch.kind] = (lr.d.val === 0);
                break;
            }
        }
        return { months: months, archiveOngoing: archiveOngoing };
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
            if (e > s) {
                months[m]['time' + kind] += (e - s);
                var days = months[m].days;
                for (var d = 0; d < days.length; d++) {
                    var ds = ms0 + d * 86400000;
                    if (Math.min(endMs, ds + 86400000) > Math.max(startMs, ds))
                        days[d]['has' + kind] = true;
                }
            }
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
    var currentLiveTimers = {};
    var currentAckList = [];

    function openHistoryModal(item, liveTimers, ackList) {
        var modal = document.getElementById("tcFloodHistoryModal");
        if (!modal) return;
        currentItem = item;
        currentLiveTimers = liveTimers || {};
        currentAckList = ackList || [];

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
        currentLiveTimers = {};
        currentAckList = [];
    }

    async function loadYearData(year) {
        if (!currentItem) return;
        var body = document.getElementById("tcFloodHistoryBody");
        if (!body) return;
        body.innerHTML = '<div class="tc-fh-loading">Загрузка...</div>';
        try {
            var result = await fetchYearlyCounts(currentItem, year);
            var months = result ? result.months : null;
            if (!months || !months.length) {
                body.innerHTML = '<div class="tc-fh-loading">Нет данных</div>';
                return;
            }
            // Inject live timer data for channels not yet captured by the archive.
            // This ensures an active flood is visible on the very first open even if
            // the minute archive hasn't written the current interval yet.
            if (year === new Date().getFullYear()) {
                var archiveOngoing = result.archiveOngoing || {};
                var nowMs = Date.now();
                var lt = currentLiveTimers;
                if (lt.flood700StartMs > 0 && !archiveOngoing['700'])
                    addOverlapToMonths(months, lt.flood700StartMs, nowMs, year, '700');
                if (lt.flood200StartMs > 0 && !archiveOngoing['200'])
                    addOverlapToMonths(months, lt.flood200StartMs, nowMs, year, '200');
            }
            var ackDayMap = buildAckDayMap(currentAckList, year);
            renderTable(body, months, ackDayMap);
        } catch (err) {
            console.error(err);
            body.innerHTML = '<div class="tc-fh-loading">Ошибка загрузки</div>';
        }
    }

    var SHORT_MONTHS = ['янв','фев','мар','апр','май','июн','июл','авг','сен','окт','ноя','дек'];

    // Build map: ackDayMap[monthIdx][dayIdx(0-based)] = [{ackedAtMs, ackedBy, comment}, ...]
    function buildAckDayMap(ackList, year) {
        var map = {};
        for (var i = 0; i < ackList.length; i++) {
            var r = ackList[i];
            if (!r.ackedAtMs) continue;
            var dt = new Date(r.ackedAtMs);
            if (dt.getFullYear() !== year) continue;
            var mo = dt.getMonth(), dy = dt.getDate() - 1;
            if (!map[mo]) map[mo] = {};
            if (!map[mo][dy]) map[mo][dy] = [];
            map[mo][dy].push(r);
        }
        return map;
    }

    function buildDayGrid(m, ackDayMap) {
        var pad = function (n) { return n < 10 ? '0' + n : '' + n; };
        var days200 = '', days700 = '';
        for (var d = 0; d < m.days.length; d++) {
            var day = m.days[d];
            var label = d + 1;
            var dayAcks = (ackDayMap && ackDayMap[m.month] && ackDayMap[m.month][d]) ? ackDayMap[m.month][d] : null;
            var ackCls = dayAcks ? ' tc-fh-day-acked' : '';
            var ackAttr = '';
            if (dayAcks) {
                var r = dayAcks[0];
                var dt = new Date(r.ackedAtMs);
                var dateStr = pad(dt.getDate()) + '.' + pad(dt.getMonth() + 1) + '.' + dt.getFullYear() +
                              ' ' + pad(dt.getHours()) + ':' + pad(dt.getMinutes());
                var comment = (r.comment || '').replace(/"/g, '&quot;').replace(/\n/g, ' ');
                ackAttr = ' data-tc-hint="Квитировал: ' + (r.ackedBy || '—') + '&#10;' + dateStr + '&#10;' + comment + '"';
            }
            days200 += '<div class="tc-fh-day ' + (day.has200 ? 'tc-fh-day-200' : 'tc-fh-day-empty') + ackCls + '"' + ackAttr + '>' + label + '</div>';
            days700 += '<div class="tc-fh-day ' + (day.has700 ? 'tc-fh-day-700' : 'tc-fh-day-empty') + ackCls + '"' + ackAttr + '>' + label + '</div>';
        }
        return '<div class="tc-fh-day-grid">' +
            '<div class="tc-fh-day-strip"><span class="tc-fh-dot tc-fh-dot-200"></span><div class="tc-fh-days-wrap">' + days200 + '</div></div>' +
            '<div class="tc-fh-day-strip"><span class="tc-fh-dot tc-fh-dot-700"></span><div class="tc-fh-days-wrap">' + days700 + '</div></div>' +
        '</div>';
    }

    function renderTable(body, months, ackDayMap) {
        ackDayMap = ackDayMap || {};
        var total200 = 0, total700 = 0;
        var rows = '';
        for (var i = 0; i < months.length; i++) {
            var m = months[i];
            total200 += m.time200;
            total700 += m.time700;
            var hasEvents = m.time200 > 0 || m.time700 > 0;
            var rowCls = 'tc-fh-month-row' + (hasEvents ? ' tc-fh-has-events' : '');
            rows +=
                '<tr class="' + rowCls + '">' +
                    '<td class="tc-fh-month"><span class="tc-fh-expand-icon"><i class="fa-solid fa-chevron-right"></i></span>' + MONTH_NAMES[m.month] + '</td>' +
                    '<td class="tc-fh-c200">' + formatDuration(m.time200) + '</td>' +
                    '<td class="tc-fh-c700">' + formatDuration(m.time700) + '</td>' +
                '</tr>' +
                '<tr class="tc-fh-day-row"><td colspan="3">' + buildDayGrid(m, ackDayMap) + '</td></tr>';
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

        body.querySelector('tbody').addEventListener('click', function (e) {
            var row = e.target.closest('.tc-fh-month-row');
            if (!row) return;
            var dayRow = row.nextElementSibling;
            if (!dayRow) return;
            var isOpen = dayRow.style.display === 'table-row';
            dayRow.style.display = isOpen ? '' : 'table-row';
            var icon = row.querySelector('.tc-fh-expand-icon i');
            if (icon) {
                icon.className = isOpen ? 'fa-solid fa-chevron-right' : 'fa-solid fa-chevron-down';
            }
        });
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
