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
            "?archiveBit=3" +
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
                return {
                    intervals: collectIntervals(histData, def.channels),
                    lastMonthIdx: lastMonthIdx
                };
            });
        });
        promise = promise.finally(function () { delete inFlightPromises[key]; });
        inFlightPromises[key] = promise;
        return promise;
    }

    // Extract flooded-day intervals [dayStart, dayStart+1day] per kind from the daily
    // archive. Each daily record is one sample for one calendar day; a flooded sample
    // (val === 0, stat > 0) marks that whole local day, regardless of the time of day
    // the archive happened to write the sample (start/noon/end). Snapping to the local
    // day boundary keeps a single flooded sample from bleeding into the next square.
    // Returned intervals are NOT yet merged.
    function collectIntervals(histData, floodChannels) {
        var byKind = { '200': [], '700': [] };
        if (!histData || !histData.cnlNums || !histData.trends || !histData.timestamps)
            return byKind;

        var cnlIdx = {};
        histData.cnlNums.forEach(function (n, i) { cnlIdx[n] = i; });
        var ts = histData.timestamps;

        for (var c = 0; c < floodChannels.length; c++) {
            var ch = floodChannels[c];
            var idx = cnlIdx[ch.cnlNum];
            if (idx === undefined) continue;
            var trend = histData.trends[idx];
            var list = byKind[ch.kind] || (byKind[ch.kind] = []);
            for (var j = 0; j < trend.length; j++) {
                var rec = trend[j];
                if (!rec || !rec.d || rec.d.stat <= 0 || rec.d.val !== 0) continue;
                var recMs = tsMs(ts[j]);
                if (recMs === null) continue;
                var dt = new Date(recMs);
                var dayStart = new Date(dt.getFullYear(), dt.getMonth(), dt.getDate()).getTime();
                list.push([dayStart, dayStart + 86400000]);
            }
        }
        return byKind;
    }

    // Merge overlapping / touching [start, end] intervals into a non-overlapping
    // set, sorted by start. Both the duration totals AND the day squares are then
    // derived from this same merged set, so they can never disagree. The previous
    // approach summed each record's span additively, which double-counted any
    // overlap (e.g. the live-timer tail over the archive) in the duration but not
    // in the squares — the cause of "30д" shown over only 24 coloured days.
    function mergeIntervals(list) {
        if (!list || !list.length) return [];
        var sorted = list.slice().sort(function (a, b) { return a[0] - b[0]; });
        var merged = [[sorted[0][0], sorted[0][1]]];
        for (var i = 1; i < sorted.length; i++) {
            var last = merged[merged.length - 1];
            if (sorted[i][0] <= last[1]) {
                if (sorted[i][1] > last[1]) last[1] = sorted[i][1];
            } else {
                merged.push([sorted[i][0], sorted[i][1]]);
            }
        }
        return merged;
    }

    // Build the per-month structure (flooded-day counts + per-day flags) from merged intervals.
    function bucketIntervals(intervalsByKind, year, lastMonthIdx) {
        var months = [];
        for (var m = 0; m <= lastMonthIdx; m++) {
            var daysInMonth = new Date(year, m + 1, 0).getDate();
            var days = [];
            for (var d = 0; d < daysInMonth; d++) days.push({ has200: false, has700: false });
            months.push({ month: m, days: days });
        }
        ['200', '700'].forEach(function (kind) {
            var merged = mergeIntervals(intervalsByKind[kind]);
            for (var i = 0; i < merged.length; i++)
                addOverlapToMonths(months, merged[i][0], merged[i][1], year, kind);
        });
        months.forEach(function (mo) {
            mo.days200 = mo.days.filter(function (d) { return d.has200; }).length;
            mo.days700 = mo.days.filter(function (d) { return d.has700; }).length;
        });
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
            if (e > s) {
                var days = months[m].days;
                for (var d = 0; d < days.length; d++) {
                    var ds = ms0 + d * 86400000;
                    if (Math.min(endMs, ds + 86400000) > Math.max(startMs, ds))
                        days[d]['has' + kind] = true;
                }
            }
        }
    }

    function formatDays(count) {
        if (!count || count <= 0) return "—";
        return count + "д";
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
            if (!result || !result.intervals) {
                body.innerHTML = '<div class="tc-fh-loading">Нет данных</div>';
                return;
            }
            var intervals = result.intervals;

            // Append the live-timer interval for an ongoing flood (current year).
            // This ensures an active flood is visible on the very first open even if
            // the daily archive hasn't recorded today yet. Because bucketIntervals
            // merges before integrating, adding [floodStart, now] can extend to the
            // current day but can never double-count days the archive already covers.
            if (year === new Date().getFullYear()) {
                var nowMs = Date.now();
                var lt = currentLiveTimers || {};
                if (lt.flood700StartMs > 0)
                    (intervals['700'] || (intervals['700'] = [])).push([lt.flood700StartMs, nowMs]);
                if (lt.flood200StartMs > 0)
                    (intervals['200'] || (intervals['200'] = [])).push([lt.flood200StartMs, nowMs]);
            }

            var months = bucketIntervals(intervals, year, result.lastMonthIdx);
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
            total200 += m.days200;
            total700 += m.days700;
            var hasEvents = m.days200 > 0 || m.days700 > 0;
            var rowCls = 'tc-fh-month-row' + (hasEvents ? ' tc-fh-has-events' : '');
            rows +=
                '<tr class="' + rowCls + '">' +
                    '<td class="tc-fh-month"><span class="tc-fh-expand-icon"><i class="fa-solid fa-chevron-right"></i></span>' + MONTH_NAMES[m.month] + '</td>' +
                    '<td class="tc-fh-c200">' + formatDays(m.days200) + '</td>' +
                    '<td class="tc-fh-c700">' + formatDays(m.days700) + '</td>' +
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
                        '<td>' + formatDays(total200) + '</td>' +
                        '<td>' + formatDays(total700) + '</td>' +
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
