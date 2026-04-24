// Thermal Camera — Flood history module
// Replaces the previous Chart.js overlay with a lightweight statistics view:
// - hover: current-month flood count (200мм + 700мм) for the TK under cursor
// - click: modal with monthly breakdown for a selectable year

var tcFloodHistory = (function () {
    var MONTH_NAMES = ['Январь', 'Февраль', 'Март', 'Апрель', 'Май', 'Июнь',
                       'Июль', 'Август', 'Сентябрь', 'Октябрь', 'Ноябрь', 'Декабрь'];

    function rootPath() {
        var base = document.querySelector("base");
        return base ? base.getAttribute("href") : "/";
    }

    function apiUrl(path) { return rootPath() + "Api/Main/" + path; }

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

    // Monthly count for current calendar month (used by cell hover tooltip)
    async function fetchCurrentMonthCount(item) {
        var def = buildFloodDef(item);
        if (!def) return null;
        var now = new Date();
        var monthStart = new Date(now.getFullYear(), now.getMonth(), 1, 0, 0, 0, 0);
        var histData = await fetchHistData(def.channels.map(function (c) { return c.cnlNum; }),
                                           monthStart, now);
        return countTransitions(histData, def.channels);
    }

    // Counts per month for a whole year (up to current month if year = this year)
    async function fetchYearlyCounts(item, year) {
        var def = buildFloodDef(item);
        if (!def) return null;
        var now = new Date();
        var lastMonthIdx = (year === now.getFullYear()) ? now.getMonth() : 11;
        var cnls = def.channels.map(function (c) { return c.cnlNum; });
        var months = [];
        for (var m = 0; m <= lastMonthIdx; m++) {
            var monthStart = new Date(year, m, 1, 0, 0, 0, 0);
            var monthEnd = new Date(year, m + 1, 1, 0, 0, 0, 0);
            monthEnd = new Date(monthEnd.getTime() - 1);
            if (monthEnd > now) monthEnd = now;
            var histData = await fetchHistData(cnls, monthStart, monthEnd);
            var counts = countTransitions(histData, def.channels);
            months.push({ month: m, count200: counts.count200, count700: counts.count700 });
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
            if (item.descr) name += " — " + item.descr;
            titleEl.textContent = "История затоплений: " + name;
        }

        // Populate year selector: current year .. current - 4
        var yearSel = document.getElementById("tcFloodHistoryYear");
        if (yearSel) {
            var curYear = new Date().getFullYear();
            yearSel.innerHTML = '';
            for (var y = curYear; y >= curYear - 4; y--) {
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
