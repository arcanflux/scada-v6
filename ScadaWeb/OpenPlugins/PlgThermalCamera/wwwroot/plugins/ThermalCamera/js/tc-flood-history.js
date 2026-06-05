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
    function tcApiUrl(path) { return rootPath() + "Api/ThermalCamera/" + path; }

    // ---- Single-flight queue + per-key dedup + result cache ----
    // Prevents a wave of parallel GetHistData requests when several TKs are
    // opened in quick succession (which previously saturated the SCADA server's
    // network and CPU). Modal requests are enqueued with priority so the open
    // window never waits behind other queued tasks, while the queue still
    // serializes everything to avoid parallel load spikes.
    var inFlightPromises = {};   // key -> Promise (dedup identical requests)
    var fetchQueue = [];          // queue of { run, resolve, reject }
    var fetchInProgress = false;

    // Per-(item, year) result cache. The modal serves cached data instantly
    // (stale-while-revalidate): a stale current-year entry is shown immediately
    // and refreshed in the background, so reopening a TK is always fast. The TTL
    // only decides when a background refresh is triggered — never whether
    // something is shown.
    var resultCache = {};        // key -> { ts, data }
    // Client-side TTLs: short because the server carries the expensive cache.
    // When stale the client re-fetches from the server, which usually serves
    // from its own 10-min / 6-h cache — so the client call is cheap.
    var CACHE_TTL_CURRENT = 60 * 1000;         // 1 min for current year
    var CACHE_TTL_PAST    = 30 * 60 * 1000;    // 30 min for past (near-immutable) years

    // Background-preload progress, surfaced by getPreloadProgress() for the
    // "Кэш" status badge in the header.
    var preloadState = { total: 0, done: 0, active: false, startedMs: 0, currentName: '' };

    function isCacheStale(key, year) {
        var c = resultCache[key];
        if (!c) return true;
        var ttl = (year === new Date().getFullYear()) ? CACHE_TTL_CURRENT : CACHE_TTL_PAST;
        return Date.now() - c.ts >= ttl;
    }

    function enqueueFetch(taskFn, priority) {
        return new Promise(function (resolve, reject) {
            var task = { run: taskFn, resolve: resolve, reject: reject };
            // Priority tasks jump to the front so the active modal loads first.
            if (priority) fetchQueue.unshift(task);
            else fetchQueue.push(task);
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

    // GET /Api/ThermalCamera/GetFloodHistory — server-side computed + cached.
    // Returns { intervals: { "200": [[startMs,endMs],...], "700": [...] }, cachedAtMs }.
    // The server computes from the SCADA archive once and caches for 10 min (current year)
    // or 6 h (past years), shared across all users. Much lighter than GetHistData which
    // returns the full raw minute archive.
    async function fetchFloodHistory(item, year) {
        var url = tcApiUrl("GetFloodHistory") +
            "?itemId=" + item.id +
            "&year=" + year +
            (item.flood200CnlNum > 0 ? "&cnl200=" + item.flood200CnlNum : "") +
            (item.flood700CnlNum > 0 ? "&cnl700=" + item.flood700CnlNum : "");
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
    // priority=true (default): jumps the queue — used by modal opens.
    // priority=false: goes to the back — used by background preload.
    function fetchYearlyCounts(item, year, priority) {
        var def = buildFloodDef(item);
        if (!def) return Promise.resolve(null);
        var key = "y_" + item.id + "_" + year;

        // 1) Fresh cached result → return instantly, no network.
        var cached = resultCache[key];
        if (cached) {
            var ttl = (year === new Date().getFullYear()) ? CACHE_TTL_CURRENT : CACHE_TTL_PAST;
            if (Date.now() - cached.ts < ttl)
                return Promise.resolve(cached.data);
        }

        // 2) An identical request is already running → share it.
        if (inFlightPromises[key]) return inFlightPromises[key];

        var isHighPriority = (priority !== false); // default true for modal opens
        var promise = enqueueFetch(function () {
            // fetchFloodHistory hits the ThermalCamera server endpoint which returns
            // pre-computed, merged intervals (tiny JSON) instead of the full raw
            // minute archive. Server caches result for all users for 10 min.
            return fetchFloodHistory(item, year).then(function (data) {
                // Only cache genuine responses. Failed fetches (data == null) must
                // not be cached so they retry on the next modal open.
                if (data) resultCache[key] = { ts: Date.now(), data: data };
                return data;
            });
        }, isHighPriority);
        promise = promise.finally(function () { delete inFlightPromises[key]; });
        inFlightPromises[key] = promise;
        return promise;
    }

    // Sequentially pre-populate the result cache for all items so every modal
    // opens instantly. Runs in the background after the first successful poll;
    // re-runs automatically on page reload (cache is in-memory only).
    // Low priority (false) ensures an open modal always jumps ahead in the queue.
    // Progress is exposed via getPreloadProgress() for the "Кэш" status badge.
    async function preloadAll(itemsList) {
        var year = new Date().getFullYear();
        var targets = (itemsList || []).filter(function (it) { return !!buildFloodDef(it); });
        preloadState = {
            total: targets.length, done: 0, active: targets.length > 0,
            startedMs: Date.now(), currentName: ''
        };
        for (var i = 0; i < targets.length; i++) {
            var item = targets[i];
            preloadState.currentName = item.name || ('ТК ' + item.id);
            var key = "y_" + item.id + "_" + year;
            if (!isCacheStale(key, year)) {  // already fresh — count as done
                preloadState.done++;
                continue;
            }
            try {
                await fetchYearlyCounts(item, year, false);
            } catch (e) {
                // Ignore; modal open will retry on demand.
            }
            preloadState.done++;
        }
        preloadState.active = false;
        preloadState.currentName = '';
    }

    // Snapshot of preload progress for the header badge. percent + rough ETA
    // (remaining items × average time per finished item).
    function getPreloadProgress() {
        var total = preloadState.total, done = preloadState.done;
        var pct = total > 0 ? Math.round(done / total * 100) : 0;
        var etaMs = 0;
        if (preloadState.active && done > 0 && done < total) {
            var avg = (Date.now() - preloadState.startedMs) / done;
            etaMs = Math.max(0, (total - done) * avg);
        }
        return {
            total: total, done: done, pct: pct,
            active: preloadState.active,
            currentName: preloadState.currentName,
            etaMs: etaMs
        };
    }

    // Merge a list of [start, end] intervals into a sorted, non-overlapping set.
    // Touching intervals (next.start === prev.end) are joined too.
    function mergeIntervals(list) {
        if (!list || !list.length) return [];
        list.sort(function (a, b) { return a[0] - b[0]; });
        var out = [[list[0][0], list[0][1]]];
        for (var i = 1; i < list.length; i++) {
            var last = out[out.length - 1];
            if (list[i][0] <= last[1]) {
                if (list[i][1] > last[1]) last[1] = list[i][1];
            } else {
                out.push([list[i][0], list[i][1]]);
            }
        }
        return out;
    }

    // Build the per-month structure (duration + day flags) from merged intervals.
    function bucketIntervals(intervalsByKind, year, lastMonthIdx) {
        var months = [];
        for (var m = 0; m <= lastMonthIdx; m++) {
            var daysInMonth = new Date(year, m + 1, 0).getDate();
            var days = [];
            for (var d = 0; d < daysInMonth; d++) days.push({ has200: false, has700: false });
            months.push({ month: m, time200: 0, time700: 0, days: days });
        }
        for (var kind in intervalsByKind) {
            if (!intervalsByKind.hasOwnProperty(kind)) continue;
            var list = intervalsByKind[kind];
            for (var i = 0; i < list.length; i++)
                addOverlapToMonths(months, list[i][0], list[i][1], year, kind);
        }
        return months;
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
        var reqItemId = currentItem.id;
        var key = "y_" + reqItemId + "_" + year;

        // The modal may have been closed or switched to another TK/year while a
        // fetch was awaiting — only touch the DOM if it still shows this request.
        function stillCurrent() {
            var sel = document.getElementById("tcFloodHistoryYear");
            var selYear = sel ? parseInt(sel.value, 10) : year;
            return currentItem && currentItem.id === reqItemId && selYear === year;
        }

        // Stale-while-revalidate: if anything is cached, show it instantly (even
        // if stale) so reopening a TK never waits on the network; then refresh
        // in the background and re-render in place when fresh data arrives.
        var cached = resultCache[key];
        if (cached && cached.data) {
            renderExtract(body, cached.data, year);
            if (isCacheStale(key, year)) {
                fetchYearlyCounts(currentItem, year, true).then(function (fresh) {
                    if (fresh && stillCurrent()) renderExtract(body, fresh, year);
                }).catch(function () {});
            }
            return;
        }

        // Cold cache → show the loader and fetch with priority.
        body.innerHTML = '<div class="tc-fh-loading">Загрузка...</div>';
        try {
            var data = await fetchYearlyCounts(currentItem, year, true);
            if (!stillCurrent()) return;
            renderExtract(body, data, year);
        } catch (err) {
            console.error(err);
            if (stillCurrent()) body.innerHTML = '<div class="tc-fh-loading">Ошибка загрузки</div>';
        }
    }

    // Build months from a cached extract and render. The live-timer tail for an
    // ongoing flood is appended and the whole set re-merged, so the duration and
    // the coloured day squares are always computed from one non-overlapping set
    // (nothing is ever double-counted, and the two views cannot disagree).
    function renderExtract(body, extract, year) {
        if (!extract || !extract.intervals) {
            body.innerHTML = '<div class="tc-fh-loading">Нет данных</div>';
            return;
        }
        var now = new Date();
        var lastMonthIdx = (year === now.getFullYear()) ? now.getMonth() : 11;

        // Clone intervals so the cached extract is never mutated.
        var byKind = {};
        for (var kind in extract.intervals) {
            if (!extract.intervals.hasOwnProperty(kind)) continue;
            byKind[kind] = extract.intervals[kind].map(function (iv) { return [iv[0], iv[1]]; });
        }

        // Append the live-timer interval for an ongoing flood (current year only).
        // Merging unions it with the archive coverage, so this can extend the
        // tail to "now" but never counts overlapping time twice.
        if (year === now.getFullYear()) {
            var nowMs = Date.now();
            var lt = currentLiveTimers || {};
            if (lt.flood700StartMs > 0) (byKind['700'] = byKind['700'] || []).push([lt.flood700StartMs, nowMs]);
            if (lt.flood200StartMs > 0) (byKind['200'] = byKind['200'] || []).push([lt.flood200StartMs, nowMs]);
        }

        for (var k in byKind) {
            if (byKind.hasOwnProperty(k)) byKind[k] = mergeIntervals(byKind[k]);
        }

        var months = bucketIntervals(byKind, year, lastMonthIdx);
        if (!months.length) {
            body.innerHTML = '<div class="tc-fh-loading">Нет данных</div>';
            return;
        }
        var ackDayMap = buildAckDayMap(currentAckList, year);
        renderTable(body, months, ackDayMap);
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
        closeHistoryModal: closeHistoryModal,
        preloadAll: preloadAll,
        getPreloadProgress: getPreloadProgress
    };
})();
