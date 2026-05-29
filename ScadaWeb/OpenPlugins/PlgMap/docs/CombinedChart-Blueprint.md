# SCADA Combined Chart — Full Blueprint

Self-contained guide to reproduce the SCADA Map combined chart in another project.
Includes all CSS, JS, HTML, API contract, and integration instructions.

---

## Table of Contents

1. [Dependencies](#1-dependencies)
2. [HTML Markup](#2-html-markup)
3. [CSS Styles](#3-css-styles)
4. [JavaScript — Full Class](#4-javascript--full-class)
5. [API Contract](#5-api-contract)
6. [Integration Guide](#6-integration-guide)

---

## 1. Dependencies

Load these scripts dynamically (the code below does it automatically):

| Library | Version | CDN URL |
|---------|---------|---------|
| Chart.js | 4.4.7 | `https://cdn.jsdelivr.net/npm/chart.js@4.4.7/dist/chart.umd.min.js` |
| chartjs-adapter-date-fns | 3.0.0 | `https://cdn.jsdelivr.net/npm/chartjs-adapter-date-fns@3.0.0/dist/chartjs-adapter-date-fns.bundle.min.js` |
| chartjs-plugin-zoom | 2.0.1 | `https://cdn.jsdelivr.net/npm/chartjs-plugin-zoom@2.0.1/dist/chartjs-plugin-zoom.min.js` |
| Hammer.js | 2.0.8 | `https://cdn.jsdelivr.net/npm/hammerjs@2.0.8/hammer.min.js` |

No other dependencies. No build tools required.

---

## 2. HTML Markup

Place this in your page body:

```html
<!-- Combined Chart Overlay -->
<div id="mapChartOverlay" class="map-chart-overlay">
    <div class="map-chart-panel">
        <div class="map-chart-header">
            <span id="mapChartTitle">График</span>
        </div>
        <div id="mapChartToolbar" class="map-chart-toolbar"></div>
        <div id="mapChartBody" class="map-chart-body">
            <div class="map-chart-loading">Загрузка...</div>
        </div>
    </div>
</div>
```

To open the chart, call:
```js
chartManager.openChart(markerIndex);
```

Clicking the overlay background closes the chart.

---

## 3. CSS Styles

```css
/* ============================================
   CHART OVERLAY PANEL
   ============================================ */
.map-chart-overlay {
    display: none;
    position: fixed;
    top: 0; left: 0; right: 0; bottom: 0;
    background: rgba(0,0,0,0.45);
    z-index: 2000;
    align-items: center;
    justify-content: center;
}

.map-chart-overlay.show {
    display: flex;
}

.map-chart-panel {
    background: #fff;
    border-radius: 8px;
    box-shadow: 0 8px 32px rgba(0,0,0,0.3);
    width: 94vw;
    max-width: 94vw;
    height: 90vh;
    max-height: 90vh;
    overflow: hidden;
    display: flex;
    flex-direction: column;
}

.map-chart-header {
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 12px 16px;
    border-bottom: none;
    font-family: 'Segoe UI', 'Roboto', 'Helvetica Neue', Arial, sans-serif;
    font-weight: 600;
    font-size: 1.25em;
    position: relative;
}

.map-chart-toolbar {
    display: flex;
    gap: 4px;
    padding: 8px 16px;
    border-bottom: none;
    flex-wrap: wrap;
    align-items: center;
    justify-content: center;
}

.map-chart-toolbar label {
    font-size: 15px;
    font-weight: 500;
    color: #666;
    margin-right: 4px;
}

.map-chart-period-btn {
    padding: 6px 14px;
    font-size: 15px;
    font-weight: 500;
    border: 1px solid #ccc;
    border-radius: 3px;
    background: #fff;
    cursor: pointer;
    color: #333;
}

.map-chart-period-btn:hover {
    background: #f0f7ff;
}

.map-chart-period-btn.active {
    background: #0073aa;
    color: #fff;
    border-color: #0073aa;
}

.map-chart-body {
    position: relative;
    padding: 12px 16px;
    flex: 1;
    overflow: hidden;
    display: flex;
    flex-direction: column;
}

.map-chart-canvas-wrap {
    position: relative;
    width: 100%;
    flex: 1;
    min-height: 300px;
}

.map-chart-loading {
    text-align: center;
    color: #999;
    padding: 40px;
    font-size: 0.95em;
}

.map-chart-pan-loading {
    font-size: 13px;
    font-weight: 500;
    color: #0073aa;
    margin-left: 12px;
}

/* ============================================
   DATE RANGE PICKER
   ============================================ */
.map-daterange-wrapper {
    position: relative;
    display: inline-flex;
    align-items: center;
}

.map-daterange-box {
    display: inline-flex;
    align-items: stretch;
    border: 1px solid #ccc;
    border-radius: 4px;
    background: #fff;
    cursor: pointer;
    font-size: 14px;
    font-family: 'Segoe UI', 'Roboto', 'Helvetica Neue', Arial, sans-serif;
    overflow: hidden;
    user-select: none;
}

.map-daterange-zone {
    padding: 6px 14px;
    display: flex;
    align-items: center;
    gap: 6px;
    transition: background 0.15s;
}

.map-daterange-zone:hover {
    background: #f0f7ff;
}

.map-daterange-zone.active {
    background: #e8f2fc;
}

.map-daterange-zone-label {
    font-size: 11px;
    font-weight: 600;
    color: #888;
    text-transform: uppercase;
    letter-spacing: 0.5px;
}

.map-daterange-zone-value {
    font-weight: 500;
    color: #333;
    white-space: nowrap;
}

.map-daterange-separator {
    width: 1px;
    background: #ccc;
    align-self: stretch;
}

.map-daterange-dropdown {
    display: none;
    position: absolute;
    top: 100%;
    left: 0;
    margin-top: 4px;
    background: #fff;
    border: 1px solid #ccc;
    border-radius: 6px;
    box-shadow: 0 8px 24px rgba(0,0,0,0.18);
    z-index: 100;
    padding: 16px;
    box-sizing: border-box;
}

.map-daterange-dropdown.open {
    display: block;
}

.map-daterange-section-label {
    font-size: 12px;
    font-weight: 600;
    color: #0073aa;
    text-transform: uppercase;
    letter-spacing: 0.5px;
    margin-bottom: 8px;
    display: flex;
    justify-content: space-between;
    align-items: center;
}

.map-daterange-now-btn {
    font-size: 11px;
    font-weight: 600;
    color: #fff;
    background: #0073aa;
    border: none;
    border-radius: 4px;
    padding: 2px 10px;
    cursor: pointer;
    text-transform: none;
    letter-spacing: 0;
}

.map-daterange-now-btn:hover {
    background: #005a87;
}

/* ============================================
   TIME SCROLLBAR (tuner dial)
   ============================================ */
.map-time-scrollbar-wrapper {
    position: relative;
    margin-bottom: 10px;
}

.map-time-scrollbar {
    position: relative;
    width: 100%;
    height: 52px;
    overflow-x: auto;
    overflow-y: hidden;
    border: 1px solid #ddd;
    border-radius: 4px;
    background: #fafafa;
    cursor: grab;
    scrollbar-width: none;
}

.map-time-scrollbar::-webkit-scrollbar {
    display: none;
}

.map-time-scrollbar:active {
    cursor: grabbing;
}

.map-time-scrollbar-track {
    position: absolute;
    top: 0;
    height: 100%;
    display: flex;
    align-items: flex-end;
}

.map-time-tick {
    position: absolute;
    bottom: 0;
    width: 1px;
    background: #ccc;
}

.map-time-tick-hour {
    background: #666;
}

.map-time-tick-label {
    position: absolute;
    top: 4px;
    font-size: 10px;
    color: #555;
    font-weight: 600;
    transform: translateX(-50%);
    white-space: nowrap;
}

.map-time-indicator {
    position: absolute;
    top: 0;
    bottom: 0;
    width: 1px;
    background: rgba(230, 57, 70, 1);
    z-index: 2;
    pointer-events: none;
}

.map-time-indicator::before {
    content: '';
    position: absolute;
    top: 0px;
    left: 45%;
    transform: translateX(-45%);
    width: 0;
    height: 0;
    border-left: 5px solid transparent;
    border-right: 5px solid transparent;
    border-top: 6px solid rgba(230, 57, 70, 1);
}

.map-time-indicator-selection {
    position: absolute;
    top: 1px;
    bottom: 1px;
    left: 50%;
    width: 1px;
    background: rgba(230, 57, 70, 0.5);
    z-index: 3;
    pointer-events: none;
}

.map-time-edge-arrow {
    position: absolute;
    top: 50%;
    transform: translateY(-50%);
    z-index: 4;
    pointer-events: none;
    width: 0;
    height: 0;
}

.map-time-edge-arrow-left {
    left: 3px;
    border-top: 6px solid transparent;
    border-bottom: 6px solid transparent;
    border-right: 8px solid rgba(230, 57, 70, 0.85);
}

.map-time-edge-arrow-right {
    right: 3px;
    border-top: 6px solid transparent;
    border-bottom: 6px solid transparent;
    border-left: 8px solid rgba(230, 57, 70, 0.85);
}

.map-time-display {
    text-align: center;
    font-size: 27px;
    font-weight: 700;
    color: #333;
    margin-bottom: 6px;
}

/* ============================================
   CALENDAR
   ============================================ */
.map-calendar {
    user-select: none;
}

.map-calendar-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 6px;
}

.map-calendar-nav {
    background: none;
    border: 1px solid #ccc;
    border-radius: 3px;
    cursor: pointer;
    font-size: 16px;
    padding: 2px 8px;
    color: #555;
    line-height: 1;
}

.map-calendar-nav:hover {
    background: #f0f7ff;
}

.map-calendar-month {
    font-size: 14px;
    font-weight: 600;
    color: #333;
}

.map-calendar-weekdays {
    display: grid;
    grid-template-columns: repeat(7, 1fr);
    text-align: center;
    font-size: 11px;
    font-weight: 600;
    color: #888;
    margin-bottom: 2px;
}

.map-calendar-grid {
    display: grid;
    grid-template-columns: repeat(7, 1fr);
    gap: 2px;
}

.map-calendar-day {
    text-align: center;
    padding: 5px 0;
    font-size: 13px;
    border-radius: 3px;
    cursor: pointer;
    color: #333;
}

.map-calendar-day:hover {
    background: #f0f7ff;
}

.map-calendar-day.other-month {
    color: #bbb;
}

.map-calendar-day.today {
    font-weight: 700;
    border: 1px solid #0073aa;
}

.map-calendar-day.selected {
    background: #0073aa;
    color: #fff;
}

.map-calendar-day.empty {
    cursor: default;
}

.map-daterange-apply {
    display: block;
    width: 100%;
    padding: 8px 0;
    margin-top: 12px;
    background: #0073aa;
    color: #fff;
    border: none;
    border-radius: 4px;
    font-size: 14px;
    font-weight: 600;
    cursor: pointer;
}

.map-daterange-apply:hover {
    background: #005f8a;
}

/* ============================================
   LIVE LEGEND (replaces floating tooltip + built-in legend)
   ============================================ */
.map-chart-live-legend {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 6px 16px;
    padding: 6px 16px;
    font-family: 'Segoe UI', 'Roboto', 'Helvetica Neue', Arial, sans-serif;
    font-size: 15px;
    min-height: 28px;
    border-bottom: 1px solid #e8e8e8;
}
.map-chart-live-legend-ts {
    font-weight: 700;
    color: #222;
    white-space: nowrap;
    margin-right: 4px;
}
.map-chart-live-legend-item {
    display: inline-flex;
    align-items: center;
    gap: 5px;
    white-space: nowrap;
}
.map-chart-live-legend-line {
    vertical-align: middle;
}
.map-chart-live-legend-alias {
    color: #555;
}
.map-chart-live-legend-val {
    font-weight: 700;
    color: #111;
}
.map-chart-live-legend-unit {
    font-weight: 400;
    color: #888;
    margin-left: 2px;
}
.map-chart-live-legend-statuses {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 6px 16px;
    padding: 2px 16px 6px;
    font-size: 14px;
}
.map-chart-live-legend-status-item {
    display: inline-flex;
    align-items: center;
    gap: 5px;
    white-space: nowrap;
}
.map-chart-live-legend-status-val {
    font-weight: 600;
}
.map-chart-live-legend-status-val.on  { color: #2e7d32; }
.map-chart-live-legend-status-val.off { color: #999; }
```

---

## 4. JavaScript — Full Class

Paste this as a `<script>` block or a standalone `.js` file.
The class is self-contained: loads its own libraries, fetches data, renders the chart.

### 4.1 Constants & Configuration

```js
class ScadaChart {

    // CDN URLs for Chart.js + plugins
    static CHARTJS_URL = "https://cdn.jsdelivr.net/npm/chart.js@4.4.7/dist/chart.umd.min.js";
    static CHARTJS_ADAPTER_URL = "https://cdn.jsdelivr.net/npm/chartjs-adapter-date-fns@3.0.0/dist/chartjs-adapter-date-fns.bundle.min.js";
    static CHARTJS_ZOOM_URL = "https://cdn.jsdelivr.net/npm/chartjs-plugin-zoom@2.0.1/dist/chartjs-plugin-zoom.min.js";
    static HAMMERJS_URL = "https://cdn.jsdelivr.net/npm/hammerjs@2.0.8/hammer.min.js";

    // Color palette for datasets (by channel index)
    static CHART_COLORS = [
        "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728", "#9467bd",
        "#8c564b", "#e377c2", "#7f7f7f", "#bcbd22", "#17becf"
    ];

    // Fixed colors for named channel aliases (pipeline conventions)
    static ALIAS_COLORS = {
        "T1": "#d32f2f",  // red — supply temperature
        "T2": "#1f77b4",  // blue — return temperature
        "T3": "#ff7f0e",  // orange — DHW supply
        "P1": "#2ca02c",  // green — supply pressure
        "P2": "#17becf"   // teal — return pressure
    };

    // Crosshair plugin — draws a vertical dashed line at the cursor position
    static _crosshairPlugin = {
        id: 'crosshairLine',
        afterDraw(chart) {
            if (chart.tooltip && chart.tooltip._active && chart.tooltip._active.length) {
                let x = chart.tooltip._active[0].element.x;
                let yAxis = chart.scales.y || chart.scales.yTemp || Object.values(chart.scales).find(s => s.axis === 'y');
                if (!yAxis) return;
                let ctx = chart.ctx;
                ctx.save();
                ctx.beginPath();
                ctx.moveTo(x, yAxis.top);
                ctx.lineTo(x, yAxis.bottom);
                ctx.lineWidth = 1;
                ctx.strokeStyle = 'rgba(0,0,0,0.25)';
                ctx.setLineDash([3, 3]);
                ctx.stroke();
                ctx.restore();
            }
        }
    };

    // --- Localization strings (replace with your language) ---
    t = {
        combinedChart: "График",
        loading: "Загрузка...",
        loadingChart: "Загрузка данных...",
        noData: "Нет архивных данных",
        errorChart: "Ошибка загрузки данных",
        error: "Ошибка",
        errorPrefix: "Ошибка: ",
        pressureUnit: "кгс/см²",
        flowRateUnit: "т/ч",
        channel: "Канал",
        start: "Начало",
        end: "Конец",
        startDateTime: "Начало графика",
        endDateTime: "Конец графика",
        apply: "Применить",
        reset: "Сброс",
        now: "сейчас",
        statusesLabel: "Состояния"
    };
```

### 4.2 Constructor

```js
    constructor(options) {
        // options.apiRoot — base URL for your API (e.g. "/Api/Main/")
        // options.markers — array of marker definitions (see §5)
        // options.refreshRate — real-time update interval in ms (default: 10000)
        this.apiRoot = options.apiRoot || "/Api/Main/";
        this.markers = options.markers || [];
        this.refreshRate = options.refreshRate || 10000;
        this._chartCache = {};
        this._activeChart = null;
        this._chartDatasetCount = 0;
        this._showStatusTimelines = false;
        this._chartRealtimePaused = false;
        this._chartAtLiveEdge = true;

        // Initialize overlay close handler
        let overlay = document.getElementById("mapChartOverlay");
        if (overlay) {
            overlay.addEventListener("click", (ev) => {
                if (ev.target === overlay) this._closeChartOverlay();
            });
        }
    }
```

### 4.3 Library Loader

```js
    _loadChartJs() {
        if (this._chartJsLoadPromise) return this._chartJsLoadPromise;
        this._chartJsLoadPromise = new Promise((resolve, reject) => {
            if (this._chartJsLoaded || typeof Chart !== "undefined") {
                this._chartJsLoaded = true;
                resolve();
                return;
            }
            let loadScript = (url) => new Promise((res, rej) => {
                let s = document.createElement("script");
                s.src = url;
                s.onload = res;
                s.onerror = rej;
                document.head.appendChild(s);
            });
            loadScript(ScadaChart.CHARTJS_URL)
                .then(() => Promise.all([
                    loadScript(ScadaChart.CHARTJS_ADAPTER_URL),
                    loadScript(ScadaChart.HAMMERJS_URL)
                ]))
                .then(() => loadScript(ScadaChart.CHARTJS_ZOOM_URL))
                .then(() => { this._chartJsLoaded = true; resolve(); })
                .catch(reject);
        });
        return this._chartJsLoadPromise;
    }
```

### 4.4 Formatting Helpers

```js
    _fmtDateTime(d) {
        let dd = String(d.getDate()).padStart(2, '0');
        let mm = String(d.getMonth() + 1).padStart(2, '0');
        let hh = String(d.getHours()).padStart(2, '0');
        let mi = String(d.getMinutes()).padStart(2, '0');
        return dd + '.' + mm + ' ' + hh + ':' + mi;
    }

    _fmtDate(d) {
        let dd = String(d.getDate()).padStart(2, '0');
        let mm = String(d.getMonth() + 1).padStart(2, '0');
        return dd + '.' + mm;
    }

    _fmtTime(d) {
        let hh = String(d.getHours()).padStart(2, '0');
        let mi = String(d.getMinutes()).padStart(2, '0');
        return hh + ':' + mi;
    }

    _fmtTimeSec(d) {
        let hh = String(d.getHours()).padStart(2, '0');
        let mi = String(d.getMinutes()).padStart(2, '0');
        let ss = String(d.getSeconds()).padStart(2, '0');
        return hh + ':' + mi + ':' + ss;
    }

    _fmtDateTimeFull(d) {
        let dd = String(d.getDate()).padStart(2, '0');
        let mm = String(d.getMonth() + 1).padStart(2, '0');
        let yy = d.getFullYear();
        let hh = String(d.getHours()).padStart(2, '0');
        let mi = String(d.getMinutes()).padStart(2, '0');
        return dd + '.' + mm + '.' + yy + ' ' + hh + ':' + mi;
    }

    _escapeHtml(text) {
        if (!text) return "";
        let div = document.createElement("div");
        div.appendChild(document.createTextNode(text));
        return div.innerHTML;
    }
```

### 4.5 Data Helpers

```js
    // Insert null gaps where data has gaps > 5× median interval
    _insertGapNulls(data) {
        if (data.length < 3) return data;
        let n = Math.min(20, data.length - 1);
        let intervals = [];
        for (let i = 0; i < n; i++) intervals.push(data[i + 1].x - data[i].x);
        intervals.sort((a, b) => a - b);
        let median = intervals[Math.floor(intervals.length / 2)];
        if (median <= 0) return data;
        let threshold = median * 5;
        let result = [];
        for (let i = 0; i < data.length; i++) {
            result.push(data[i]);
            if (i < data.length - 1 && data[i + 1].x - data[i].x > threshold) {
                result.push({ x: data[i].x + median, y: null });
            }
        }
        return result;
    }

    // Choose archive resolution: 0=seconds (<=1h), 1=minutes (1h-3d), 2=hours (>3d)
    _chooseArchiveBit(startTime, endTime) {
        let hours = (endTime.getTime() - startTime.getTime()) / 3600000;
        if (hours <= 1) return 0;
        if (hours > 72) return 2;
        return 1;
    }

    // Map alias → color (fixed per pipeline convention, fallback to palette)
    _getChannelColor(alias, index) {
        let a = (alias || "").toUpperCase();
        return ScadaChart.ALIAS_COLORS[a] || ScadaChart.CHART_COLORS[index % ScadaChart.CHART_COLORS.length];
    }

    // Map alias → Y-axis group
    _getAxisGroup(alias) {
        let a = (alias || "").toUpperCase();
        if (a === "P1" || a === "P2") return "pressure";
        return "temp";
    }

    // Known axis limits per alias
    _getAxisLimits(alias) {
        let a = (alias || "").toUpperCase();
        if (a === "T1" || a === "T2") return { min: 0, max: 120, unit: "°C" };
        if (a === "T3")               return { min: 0, max: 80,  unit: "°C" };
        if (a === "P1" || a === "P2") return { min: 0, max: 25,  unit: this.t.pressureUnit };
        return null;
    }
```

### 4.6 API Fetch (with Cache)

```js
    // Fetch historical data from the SCADA API
    // Returns: { timestamps: [{ms: epoch}], trends: [[{d:{val, stat}}]], cnlNums: [int] }
    async _fetchHistDataRange(cnlNums, startTime, endTime, archiveBit) {
        let startMs = startTime.getTime();
        let endMs = endTime.getTime();
        if (archiveBit === undefined) archiveBit = this._chooseArchiveBit(startTime, endTime);
        let cacheKey = archiveBit + "_" + cnlNums.join(",") + "_" + startMs + "_" + endMs;
        let cached = this._chartCache[cacheKey];
        let cacheMs = (endMs - startMs) <= 24 * 3600 * 1000 ? 30000 : 300000;
        if (cached && (Date.now() - cached.time) < cacheMs) {
            return cached.data;
        }

        let url = this.apiRoot + "GetHistData" +
            "?archiveBit=" + archiveBit +
            "&startTime=" + encodeURIComponent(startTime.toISOString()) +
            "&endTime=" + encodeURIComponent(endTime.toISOString()) +
            "&endInclusive=true" +
            "&cnlNums=" + encodeURIComponent(cnlNums.join(","));

        try {
            let resp = await fetch(url);
            let dto = await resp.json();
            if (dto.ok && dto.data) {
                this._chartCache[cacheKey] = { data: dto.data, time: Date.now() };
                return dto.data;
            }
        } catch (err) {
            console.error("Failed to fetch hist data range:", err);
        }
        return null;
    }
```

### 4.7 Date Range Picker

```js
    _buildDateRangeBox(toolbar, onApply, idPrefix) {
        idPrefix = idPrefix || 'mapDR';
        this._drPrefix = idPrefix;
        this._drApplyCallback = onApply || null;

        if (this._drOutsideClickHandler) {
            document.removeEventListener('click', this._drOutsideClickHandler);
            this._drOutsideClickHandler = null;
        }

        let wrapper = document.createElement('div');
        wrapper.className = 'map-daterange-wrapper';

        let box = document.createElement('div');
        box.className = 'map-daterange-box';
        box.id = idPrefix + 'Box';

        let startZone = document.createElement('div');
        startZone.className = 'map-daterange-zone';
        startZone.id = idPrefix + 'StartZone';
        startZone.innerHTML =
            '<span class="map-daterange-zone-label">' + this._escapeHtml(this.t.start) + '</span>' +
            '<span class="map-daterange-zone-value" id="' + idPrefix + 'StartVal">' +
            this._escapeHtml(this._fmtDateTimeFull(this._chartStartTime)) + '</span>';

        let sep = document.createElement('div');
        sep.className = 'map-daterange-separator';

        let endZone = document.createElement('div');
        endZone.className = 'map-daterange-zone';
        endZone.id = idPrefix + 'EndZone';
        endZone.innerHTML =
            '<span class="map-daterange-zone-label">' + this._escapeHtml(this.t.end) + '</span>' +
            '<span class="map-daterange-zone-value" id="' + idPrefix + 'EndVal">' +
            this._escapeHtml(this._fmtDateTimeFull(this._chartEndTime)) + '</span>';

        box.appendChild(startZone);
        box.appendChild(sep);
        box.appendChild(endZone);
        wrapper.appendChild(box);

        let dropdown = document.createElement('div');
        dropdown.className = 'map-daterange-dropdown';
        dropdown.id = idPrefix + 'Dropdown';
        wrapper.appendChild(dropdown);

        toolbar.appendChild(wrapper);

        this._drPickerMode = null;
        this._drTempStart = new Date(this._chartStartTime);
        this._drTempEnd = new Date(this._chartEndTime);

        let self = this;
        startZone.addEventListener('click', (e) => {
            e.stopPropagation();
            self._openDateRangeDropdown('start');
        });
        endZone.addEventListener('click', (e) => {
            e.stopPropagation();
            self._openDateRangeDropdown('end');
        });

        let pfx = idPrefix;
        this._drOutsideClickHandler = (e) => {
            let dd = document.getElementById(pfx + 'Dropdown');
            let bx = document.getElementById(pfx + 'Box');
            if (dd && !dd.contains(e.target) && bx && !bx.contains(e.target)) {
                dd.classList.remove('open');
                this._drPickerMode = null;
                if (this._timeIndicatorInterval) {
                    clearInterval(this._timeIndicatorInterval);
                    this._timeIndicatorInterval = null;
                }
                let sz = document.getElementById(pfx + 'StartZone');
                let ez = document.getElementById(pfx + 'EndZone');
                if (sz) sz.classList.remove('active');
                if (ez) ez.classList.remove('active');
            }
        };
        document.addEventListener('click', this._drOutsideClickHandler);
    }

    _openDateRangeDropdown(mode) {
        this._chartRealtimePaused = true;
        this._drPickerMode = mode;
        let pfx = this._drPrefix;
        let dropdown = document.getElementById(pfx + 'Dropdown');
        let box = document.getElementById(pfx + 'Box');

        dropdown.style.width = box.offsetWidth + 'px';
        dropdown.style.minWidth = '300px';

        let sz = document.getElementById(pfx + 'StartZone');
        let ez = document.getElementById(pfx + 'EndZone');
        sz.classList.toggle('active', mode === 'start');
        ez.classList.toggle('active', mode === 'end');

        let currentDate = mode === 'start' ? this._drTempStart : this._drTempEnd;
        this._drViewMonth = currentDate.getMonth();
        this._drViewYear = currentDate.getFullYear();

        this._renderDateRangeDropdown(dropdown, currentDate);
        dropdown.classList.add('open');
    }

    _renderDateRangeDropdown(dropdown, currentDate) {
        if (this._timeIndicatorInterval) {
            clearInterval(this._timeIndicatorInterval);
            this._timeIndicatorInterval = null;
        }
        dropdown.innerHTML = '';
        let mode = this._drPickerMode;
        let self = this;

        let label = document.createElement('div');
        label.className = 'map-daterange-section-label';
        let labelText = document.createElement('span');
        labelText.textContent = mode === 'start' ? this.t.startDateTime : this.t.endDateTime;
        label.appendChild(labelText);

        let nowBtn = document.createElement('button');
        nowBtn.className = 'map-daterange-now-btn';
        nowBtn.textContent = this.t.now;
        nowBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            let now = new Date();
            if (self._drPickerMode === 'start') {
                self._drTempStart = now;
            } else {
                self._drTempEnd = now;
            }
            self._renderDateRangeDropdown(dropdown, now);
        });
        label.appendChild(nowBtn);
        dropdown.appendChild(label);

        let timeDisplay = document.createElement('div');
        timeDisplay.className = 'map-time-display';
        timeDisplay.id = this._drPrefix + 'TimeDisplay';
        let hh = currentDate.getHours();
        let mm = currentDate.getMinutes();
        timeDisplay.textContent = String(hh).padStart(2, '0') + ':' + String(mm).padStart(2, '0');
        dropdown.appendChild(timeDisplay);

        let scrollbar = this._buildTimeScrollbar(currentDate);
        dropdown.appendChild(scrollbar);

        let calendar = this._buildCalendar(currentDate);
        dropdown.appendChild(calendar);

        let applyBtn = document.createElement('button');
        applyBtn.className = 'map-daterange-apply';
        applyBtn.textContent = this.t.apply;
        applyBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            self._applyDateRange();
        });
        dropdown.appendChild(applyBtn);
    }
```

### 4.8 Time Scrollbar (Tuner Dial)

```js
    _buildTimeScrollbar(currentDate) {
        let wrapper = document.createElement('div');
        wrapper.className = 'map-time-scrollbar-wrapper';
        let container = document.createElement('div');
        container.className = 'map-time-scrollbar';
        let pxPerMin = 3;
        let totalWidth = 1440 * pxPerMin;
        let track = document.createElement('div');
        track.className = 'map-time-scrollbar-track';
        track.style.width = totalWidth + 'px';

        for (let h = 0; h < 24; h++) {
            for (let m = 0; m < 60; m++) {
                let totalMin = h * 60 + m;
                let x = totalMin * pxPerMin;
                let tick = document.createElement('div');
                tick.className = 'map-time-tick';
                tick.style.left = x + 'px';
                if (m === 0) {
                    tick.classList.add('map-time-tick-hour');
                    tick.style.height = '26px';
                    let lbl = document.createElement('div');
                    lbl.className = 'map-time-tick-label';
                    lbl.style.left = x + 'px';
                    lbl.textContent = String(h).padStart(2, '0');
                    track.appendChild(lbl);
                } else if (m % 15 === 0) {
                    tick.style.height = '16px';
                } else if (m % 5 === 0) {
                    tick.style.height = '10px';
                } else {
                    tick.style.height = '5px';
                }
                track.appendChild(tick);
            }
        }

        let now = new Date();
        let nowMin = now.getHours() * 60 + now.getMinutes();
        let indicator = document.createElement('div');
        indicator.className = 'map-time-indicator';
        indicator.style.left = (nowMin * pxPerMin) + 'px';
        track.appendChild(indicator);
        container.appendChild(track);
        wrapper.appendChild(container);

        let selIndicator = document.createElement('div');
        selIndicator.className = 'map-time-indicator-selection';
        wrapper.appendChild(selIndicator);

        let arrowLeft = document.createElement('div');
        arrowLeft.className = 'map-time-edge-arrow map-time-edge-arrow-left';
        arrowLeft.style.display = 'none';
        wrapper.appendChild(arrowLeft);

        let arrowRight = document.createElement('div');
        arrowRight.className = 'map-time-edge-arrow map-time-edge-arrow-right';
        arrowRight.style.display = 'none';
        wrapper.appendChild(arrowRight);

        let currentMin = currentDate.getHours() * 60 + currentDate.getMinutes();
        let self = this;

        function updateEdgeArrows() {
            let indicatorLeft = indicator.offsetLeft;
            let scrollL = container.scrollLeft;
            let scrollR = scrollL + container.clientWidth;
            if (indicatorLeft < scrollL) {
                arrowLeft.style.display = '';
                arrowRight.style.display = 'none';
            } else if (indicatorLeft > scrollR) {
                arrowLeft.style.display = 'none';
                arrowRight.style.display = '';
            } else {
                arrowLeft.style.display = 'none';
                arrowRight.style.display = 'none';
            }
        }

        setTimeout(() => {
            let centerOffset = container.clientWidth / 2;
            container.scrollLeft = currentMin * pxPerMin - centerOffset;
            updateEdgeArrows();
        }, 50);

        this._timeIndicatorInterval = setInterval(() => {
            let n = new Date();
            let nMin = n.getHours() * 60 + n.getMinutes();
            indicator.style.left = (nMin * pxPerMin) + 'px';
            updateEdgeArrows();
        }, 10000);

        // Drag to scroll
        let isDragging = false, didDrag = false, startX = 0, startScroll = 0;
        container.addEventListener('mousedown', (e) => {
            isDragging = true; didDrag = false;
            startX = e.clientX; startScroll = container.scrollLeft;
            e.preventDefault();
        });
        document.addEventListener('mousemove', (e) => {
            if (!isDragging) return;
            let dx = e.clientX - startX;
            if (Math.abs(dx) > 3) didDrag = true;
            container.scrollLeft = startScroll - dx;
            self._updateTimeFromScroll(container, pxPerMin);
            updateEdgeArrows();
        });
        document.addEventListener('mouseup', (e) => {
            if (!isDragging) return;
            isDragging = false;
            if (didDrag) {
                self._snapTimeScroll(container, pxPerMin);
            } else {
                let rect = container.getBoundingClientRect();
                let clickX = e.clientX - rect.left;
                let center = rect.width / 2;
                let nudge = clickX >= center ? pxPerMin : -pxPerMin;
                container.scrollLeft += nudge;
                self._updateTimeFromScroll(container, pxPerMin);
                self._snapTimeScroll(container, pxPerMin);
            }
            updateEdgeArrows();
        });
        container.addEventListener('wheel', (e) => {
            e.preventDefault();
            container.scrollLeft += e.deltaY > 0 ? 30 : -30;
            self._updateTimeFromScroll(container, pxPerMin);
            updateEdgeArrows();
            clearTimeout(self._timeScrollSnapTimeout);
            self._timeScrollSnapTimeout = setTimeout(() => {
                self._snapTimeScroll(container, pxPerMin);
            }, 150);
        });

        // Touch support
        let touchStartX = 0, touchStartScroll = 0;
        container.addEventListener('touchstart', (e) => {
            touchStartX = e.touches[0].clientX;
            touchStartScroll = container.scrollLeft;
        });
        container.addEventListener('touchmove', (e) => {
            let dx = e.touches[0].clientX - touchStartX;
            container.scrollLeft = touchStartScroll - dx;
            self._updateTimeFromScroll(container, pxPerMin);
            updateEdgeArrows();
            e.preventDefault();
        }, { passive: false });
        container.addEventListener('touchend', () => {
            self._snapTimeScroll(container, pxPerMin);
            updateEdgeArrows();
        });

        return wrapper;
    }

    _updateTimeFromScroll(container, pxPerMin) {
        let centerX = container.scrollLeft + container.clientWidth / 2;
        let totalMin = Math.round(centerX / pxPerMin);
        totalMin = Math.max(0, Math.min(1439, totalMin));
        let h = Math.floor(totalMin / 60);
        let m = totalMin % 60;
        let display = document.getElementById(this._drPrefix + 'TimeDisplay');
        if (display) {
            display.textContent = String(h).padStart(2, '0') + ':' + String(m).padStart(2, '0');
        }
        let target = this._drPickerMode === 'start' ? this._drTempStart : this._drTempEnd;
        target.setHours(h, m, 0, 0);
    }

    _snapTimeScroll(container, pxPerMin) {
        let centerX = container.scrollLeft + container.clientWidth / 2;
        let totalMin = Math.round(centerX / pxPerMin);
        totalMin = Math.max(0, Math.min(1439, totalMin));
        let snappedX = totalMin * pxPerMin;
        container.scrollLeft = snappedX - container.clientWidth / 2;
        this._updateTimeFromScroll(container, pxPerMin);
    }
```

### 4.9 Calendar

```js
    _buildCalendar(currentDate) {
        let container = document.createElement('div');
        container.className = 'map-calendar';
        container.id = this._drPrefix + 'Calendar';
        let self = this;
        let year = this._drViewYear;
        let month = this._drViewMonth;
        let selectedDay = currentDate.getDate();
        let selectedMonth = currentDate.getMonth();
        let selectedYear = currentDate.getFullYear();

        let header = document.createElement('div');
        header.className = 'map-calendar-header';

        let prevBtn = document.createElement('button');
        prevBtn.className = 'map-calendar-nav';
        prevBtn.textContent = '<';
        prevBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            self._drViewMonth--;
            if (self._drViewMonth < 0) { self._drViewMonth = 11; self._drViewYear--; }
            self._refreshCalendar(currentDate);
        });

        let nextBtn = document.createElement('button');
        nextBtn.className = 'map-calendar-nav';
        nextBtn.textContent = '>';
        nextBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            self._drViewMonth++;
            if (self._drViewMonth > 11) { self._drViewMonth = 0; self._drViewYear++; }
            self._refreshCalendar(currentDate);
        });

        // Change month names to your locale
        let monthNames = ['Январь', 'Февраль', 'Март', 'Апрель', 'Май', 'Июнь',
            'Июль', 'Август', 'Сентябрь', 'Октябрь', 'Ноябрь', 'Декабрь'];
        let monthLabel = document.createElement('span');
        monthLabel.className = 'map-calendar-month';
        monthLabel.textContent = monthNames[month] + ' ' + year;

        header.appendChild(prevBtn);
        header.appendChild(monthLabel);
        header.appendChild(nextBtn);
        container.appendChild(header);

        let weekdays = document.createElement('div');
        weekdays.className = 'map-calendar-weekdays';
        let dayNames = ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс'];
        for (let dn of dayNames) {
            let d = document.createElement('div');
            d.textContent = dn;
            weekdays.appendChild(d);
        }
        container.appendChild(weekdays);

        let grid = document.createElement('div');
        grid.className = 'map-calendar-grid';
        let firstDay = new Date(year, month, 1).getDay();
        let startOffset = (firstDay + 6) % 7;
        let daysInMonth = new Date(year, month + 1, 0).getDate();
        let today = new Date();

        for (let i = 0; i < startOffset; i++) {
            let empty = document.createElement('div');
            empty.className = 'map-calendar-day empty';
            grid.appendChild(empty);
        }

        for (let d = 1; d <= daysInMonth; d++) {
            let cell = document.createElement('div');
            cell.className = 'map-calendar-day';
            cell.textContent = d;
            if (d === today.getDate() && month === today.getMonth() && year === today.getFullYear()) {
                cell.classList.add('today');
            }
            if (d === selectedDay && month === selectedMonth && year === selectedYear) {
                cell.classList.add('selected');
            }
            cell.addEventListener('click', (e) => {
                e.stopPropagation();
                let dayNum = parseInt(cell.textContent, 10);
                let target = self._drPickerMode === 'start' ? self._drTempStart : self._drTempEnd;
                target.setFullYear(self._drViewYear, self._drViewMonth, dayNum);
                grid.querySelectorAll('.map-calendar-day.selected').forEach(c => c.classList.remove('selected'));
                cell.classList.add('selected');
            });
            grid.appendChild(cell);
        }

        container.appendChild(grid);
        return container;
    }

    _refreshCalendar(currentDate) {
        let container = document.getElementById(this._drPrefix + 'Calendar');
        if (!container) return;
        let parent = container.parentNode;
        let newCal = this._buildCalendar(currentDate);
        parent.replaceChild(newCal, container);
    }

    _applyDateRange() {
        if (this._drTempStart >= this._drTempEnd) {
            let tmp = new Date(this._drTempStart);
            this._drTempStart = new Date(this._drTempEnd);
            this._drTempEnd = tmp;
        }

        this._chartStartTime = new Date(this._drTempStart);
        this._chartEndTime = new Date(this._drTempEnd);
        this._chartRealtimePaused = true;
        this._chartAtLiveEdge = false;
        this._combinedDays = (this._chartEndTime - this._chartStartTime) / (24 * 60 * 60 * 1000);

        let pfx = this._drPrefix;
        let startVal = document.getElementById(pfx + 'StartVal');
        let endVal = document.getElementById(pfx + 'EndVal');
        if (startVal) startVal.textContent = this._fmtDateTimeFull(this._chartStartTime);
        if (endVal) endVal.textContent = this._fmtDateTimeFull(this._chartEndTime);

        let dropdown = document.getElementById(pfx + 'Dropdown');
        if (dropdown) dropdown.classList.remove('open');
        this._drPickerMode = null;

        if (this._drApplyCallback) {
            this._drApplyCallback();
        } else {
            this.openChart(this._combinedMarkerIdx, this._combinedDays);
        }
    }
```

### 4.10 Live Legend Tooltip (replaces floating tooltip)

```js
    _renderCombinedTooltip(ctx) {
        let tooltipModel = ctx.tooltip;
        let tsEl = document.getElementById('mapChartLLTime');

        if (!tooltipModel || tooltipModel.opacity === 0) {
            if (tsEl) tsEl.textContent = '--';
            for (let i = 0; i < (this._chartDatasetCount || 0); i++) {
                let el = document.getElementById('mapChartLLVal' + i);
                if (el) {
                    el.textContent = '--';
                    el.className = el.className.replace(/ on| off/g, '') + ' off';
                }
            }
            return;
        }

        if (tsEl && tooltipModel.dataPoints && tooltipModel.dataPoints.length > 0) {
            let ts = new Date(tooltipModel.dataPoints[0].parsed.x);
            let dd = String(ts.getDate()).padStart(2, '0');
            let mm = String(ts.getMonth() + 1).padStart(2, '0');
            let yy = ts.getFullYear();
            let hh = String(ts.getHours()).padStart(2, '0');
            let mi = String(ts.getMinutes()).padStart(2, '0');
            let ss = String(ts.getSeconds()).padStart(2, '0');
            tsEl.textContent = dd + '.' + mm + '.' + yy + '  ' + hh + ':' + mi + ':' + ss;
        }

        for (let dp of tooltipModel.dataPoints) {
            let ds = ctx.chart.data.datasets[dp.datasetIndex];
            let el = document.getElementById('mapChartLLVal' + dp.datasetIndex);
            if (!el) continue;

            if (ds._isStatus) {
                let active = dp.parsed.y > ds._bandBase;
                el.textContent = active ? 'Вкл' : 'Выкл';
                el.className = 'map-chart-live-legend-status-val ' + (active ? 'on' : 'off');
            } else {
                let val = dp.parsed.y;
                if (val === null || val === undefined) {
                    el.textContent = '--';
                    continue;
                }
                if (ds._scale && typeof val === 'number') {
                    el.textContent = (val / ds._scale).toFixed(1);
                } else {
                    el.textContent = typeof val === 'number' ? val.toFixed(2) : String(val);
                }
            }
        }
    }
```

### 4.11 Main Chart Renderer

```js
    _renderCombinedChart(canvas, def, histData) {
        if (this._activeChart) {
            this._activeChart.destroy();
            this._activeChart = null;
        }

        let timestamps = histData.timestamps.map(t => t.ms);
        let datasets = [];
        this._chartDataStart = timestamps.length > 0 ? timestamps[0] : Date.now();
        this._chartDataEnd = timestamps.length > 0 ? timestamps[timestamps.length - 1] : Date.now();

        let cnlToTrendIdx = {};
        for (let i = 0; i < histData.cnlNums.length; i++) {
            cnlToTrendIdx[histData.cnlNums[i]] = i;
        }

        let chartChannels = def.channels;
        let allCnlNums = chartChannels.map(c => c.cnlNum);
        if (this._showStatusTimelines && def.statusChannels && def.statusChannels.length > 0) {
            allCnlNums = allCnlNums.concat(def.statusChannels.map(c => c.cnlNum));
        }
        this._chartCnlNums = allCnlNums;
        this._chartMarkerDef = def;

        // Build line datasets
        for (let i = 0; i < chartChannels.length; i++) {
            let ch = chartChannels[i];
            let alias = ch.alias || (this.t.channel + ch.cnlNum);
            let color = this._getChannelColor(alias, i);
            let trendIdx = cnlToTrendIdx[ch.cnlNum];
            if (trendIdx === undefined) continue;

            let trend = histData.trends[trendIdx];
            let data = this._insertGapNulls(trend.map((rec, idx) => ({
                x: timestamps[idx],
                y: (rec && rec.d && rec.d.stat > 0) ? rec.d.val : null
            })));

            let axisGroup = this._getAxisGroup(alias);
            let unit = axisGroup === "pressure" ? this.t.pressureUnit : "°C";
            datasets.push({
                label: alias,
                data: data,
                parsing: false,
                borderColor: color,
                backgroundColor: color + "20",
                borderWidth: 1.5,
                pointRadius: 0,
                pointHoverRadius: 4,
                tension: 0.3,
                fill: false,
                spanGaps: false,
                yAxisID: axisGroup === "pressure" ? "yPressure" : "yTemp",
                _cnlNum: ch.cnlNum,
                _unit: unit
            });
        }

        // Status timeline bands (optional)
        let STATUS_COLORS = ["#f0ad4e", "#f57c00", "#d32f2f", "#ff9800", "#7b1fa2"];
        if (this._showStatusTimelines && def.statusChannels && def.statusChannels.length > 0) {
            let statusCount = def.statusChannels.length;
            let bandHeight = 120 / statusCount;
            for (let i = 0; i < def.statusChannels.length; i++) {
                let sch = def.statusChannels[i];
                let alias = sch.alias || (this.t.channel + sch.cnlNum);
                let color = STATUS_COLORS[i % STATUS_COLORS.length];
                let trendIdx = cnlToTrendIdx[sch.cnlNum];
                if (trendIdx === undefined) continue;
                let bandTop = 120 - i * bandHeight;
                let bandBase = bandTop - bandHeight;
                let trend = histData.trends[trendIdx];
                let data = trend.map((rec, idx) => ({
                    x: timestamps[idx],
                    y: (rec && rec.d && rec.d.stat > 0 && rec.d.val >= 1) ? bandTop : bandBase
                }));
                datasets.push({
                    label: alias,
                    data: data,
                    parsing: false,
                    borderColor: color,
                    backgroundColor: color + "40",
                    borderWidth: 0,
                    pointRadius: 0,
                    pointHoverRadius: 0,
                    tension: 0,
                    stepped: 'before',
                    fill: { target: { value: bandBase } },
                    spanGaps: true,
                    yAxisID: 'yTemp',
                    _cnlNum: sch.cnlNum,
                    _isStatus: true,
                    _bandBase: bandBase,
                    _bandTop: bandTop
                });
            }
        }

        let self = this;

        // X-axis tick formatting
        let xTickCallback = function(value, index, ticksArr) {
            let ts = new Date(ticksArr[index].value);
            let visibleMs = this.max - this.min;
            let showSecs = visibleMs <= 3600000;
            let timeStr = showSecs ? self._fmtTimeSec(ts) : self._fmtTime(ts);
            if (index === 0) return self._fmtDate(ts) + ' ' + timeStr;
            let prevTs = new Date(ticksArr[index - 1].value);
            if (ts.getDate() !== prevTs.getDate() || ts.getMonth() !== prevTs.getMonth()) {
                return self._fmtDate(ts) + ' ' + timeStr;
            }
            return timeStr;
        };

        let scales = {
            x: {
                type: 'time',
                time: {
                    displayFormats: {
                        millisecond: 'HH:mm:ss',
                        second: 'HH:mm:ss',
                        minute: 'HH:mm',
                        hour: 'HH:mm',
                        day: 'dd.MM',
                        week: 'dd.MM',
                        month: 'MM.yyyy'
                    },
                    major: { enabled: true }
                },
                title: { display: false },
                ticks: {
                    font: { size: 15, weight: '500' },
                    color: '#333',
                    callback: xTickCallback,
                    maxRotation: 0,
                    autoSkipPadding: 20,
                    major: { enabled: true }
                }
            },
            yTemp: {
                type: 'linear',
                position: 'left',
                min: 0,
                max: 120,
                title: {
                    display: true,
                    text: '°C',
                    font: { size: 17, weight: '600' },
                    color: '#000',
                    padding: { bottom: 6 }
                },
                ticks: { font: { size: 15, weight: '500' }, color: '#333' }
            },
            yPressure: {
                type: 'linear',
                position: 'right',
                min: 0,
                max: 25,
                title: {
                    display: true,
                    text: this.t.pressureUnit,
                    font: { size: 17, weight: '600' },
                    color: '#000',
                    padding: { bottom: 6 }
                },
                ticks: { font: { size: 15, weight: '500' }, color: '#333' },
                grid: { drawOnChartArea: false }
            }
        };

        // Build live legend HTML (fixed bar above canvas)
        this._chartDatasetCount = datasets.length;
        let llHtml = '<div class="map-chart-live-legend" id="mapChartLiveLegend">';
        llHtml += '<span class="map-chart-live-legend-ts" id="mapChartLLTime">--</span>';
        for (let i = 0; i < datasets.length; i++) {
            let ds = datasets[i];
            if (ds._isStatus) continue;
            let color = ds.borderColor || '#333';
            let dashed = ds.borderDash ? ' stroke-dasharray="6 3"' : '';
            llHtml += '<span class="map-chart-live-legend-item">' +
                '<svg class="map-chart-live-legend-line" width="30" height="3"><line x1="0" y1="1.5" x2="30" y2="1.5" stroke="' + color + '" stroke-width="2"' + dashed + '/></svg>' +
                '<span class="map-chart-live-legend-alias">' + this._escapeHtml(ds.label) + '</span>' +
                '<span class="map-chart-live-legend-val" id="mapChartLLVal' + i + '">--</span>' +
                '<span class="map-chart-live-legend-unit">' + this._escapeHtml(ds._unit || '') + '</span>' +
                '</span>';
        }
        llHtml += '</div>';

        let hasStatusDS = datasets.some(ds => ds._isStatus);
        if (hasStatusDS) {
            llHtml += '<div class="map-chart-live-legend-statuses" id="mapChartLLStatuses">';
            for (let i = 0; i < datasets.length; i++) {
                let ds = datasets[i];
                if (!ds._isStatus) continue;
                let color = ds.borderColor || '#333';
                llHtml += '<span class="map-chart-live-legend-status-item">' +
                    '<svg width="30" height="3" style="vertical-align:middle"><line x1="0" y1="1.5" x2="30" y2="1.5" stroke="' + color + '" stroke-width="3"/></svg>' +
                    '<span class="map-chart-live-legend-alias">' + this._escapeHtml(ds.label) + '</span>' +
                    '<span class="map-chart-live-legend-status-val off" id="mapChartLLVal' + i + '">--</span>' +
                    '</span>';
            }
            llHtml += '</div>';
        }
        let llTarget = canvas.parentElement;
        llTarget.insertAdjacentHTML('beforebegin', llHtml);

        // Create Chart.js instance
        this._activeChart = new Chart(canvas, {
            type: 'line',
            data: { datasets: datasets },
            plugins: [ScadaChart._crosshairPlugin],
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: false,
                animations: { colors: false, x: false, y: false },
                transitions: { active: { animation: { duration: 0 } } },
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: { display: false },
                    decimation: {
                        enabled: true,
                        algorithm: 'lttb',
                        samples: 600
                    },
                    tooltip: {
                        enabled: false,
                        external: function(ctx) {
                            self._renderCombinedTooltip(ctx);
                        }
                    },
                    zoom: {
                        pan: {
                            enabled: true,
                            mode: 'x',
                            modifierKey: null,
                            onPanComplete: function({ chart }) {
                                self._chartAtLiveEdge = self._checkIfAtLiveEdge(chart);
                                self._onChartPanComplete(chart);
                            }
                        },
                        zoom: {
                            wheel: { enabled: true, modifierKey: null },
                            pinch: { enabled: true },
                            drag: { enabled: false },
                            mode: 'x',
                            onZoomComplete: function({ chart }) {
                                self._chartAtLiveEdge = self._checkIfAtLiveEdge(chart);
                            }
                        },
                        limits: {
                            x: { minRange: 60000 }
                        }
                    }
                },
                scales: scales
            }
        });
    }
```

### 4.12 Chart Open / Close / Real-time

```js
    // Public entry point — opens the chart for a given marker index
    async openChart(markerIdx, days) {
        let def = this.markers[markerIdx];
        if (!def || !def.channels || def.channels.length === 0) return;

        this._combinedMarkerIdx = markerIdx;
        this._combinedDays = days || 1;

        let overlay = document.getElementById("mapChartOverlay");
        let title = document.getElementById("mapChartTitle");
        let body = document.getElementById("mapChartBody");
        let toolbar = document.getElementById("mapChartToolbar");

        title.textContent = def.name || this.t.combinedChart;

        if (!this._chartEndTime || !this._chartStartTime) {
            this._chartEndTime = new Date();
            this._chartStartTime = new Date(this._chartEndTime.getTime() - 24 * 60 * 60 * 1000);
        }

        toolbar.innerHTML = '';
        this._buildDateRangeBox(toolbar, null, 'mapDRChart');

        let resetBtn = document.createElement('button');
        resetBtn.className = 'map-chart-period-btn';
        resetBtn.style.marginLeft = '12px';
        resetBtn.textContent = '⟲ ' + this.t.reset;
        toolbar.appendChild(resetBtn);
        resetBtn.addEventListener('click', () => {
            this._chartRealtimePaused = false;
            this._chartAtLiveEdge = true;
            this._chartStartTime = null;
            this._chartEndTime = null;
            this.openChart(this._combinedMarkerIdx, this._combinedDays);
        });

        if (def.statusChannels && def.statusChannels.length > 0) {
            let stBtn = document.createElement('button');
            stBtn.className = this._showStatusTimelines ? 'map-chart-period-btn active' : 'map-chart-period-btn';
            stBtn.style.marginLeft = '12px';
            stBtn.textContent = this.t.statusesLabel;
            toolbar.appendChild(stBtn);
            stBtn.addEventListener('click', () => {
                this._showStatusTimelines = !this._showStatusTimelines;
                this.openChart(this._combinedMarkerIdx, this._combinedDays);
            });
        }

        body.innerHTML = '<div class="map-chart-loading">' + this._escapeHtml(this.t.loadingChart) + '</div>';
        overlay.classList.add("show");

        try {
            let cnlNums = def.channels.map(c => c.cnlNum);
            if (this._showStatusTimelines && def.statusChannels && def.statusChannels.length > 0) {
                cnlNums = cnlNums.concat(def.statusChannels.map(c => c.cnlNum));
            }

            let [, histData] = await Promise.all([
                this._loadChartJs(),
                this._fetchHistDataRange(cnlNums, this._chartStartTime, this._chartEndTime)
            ]);

            if (!histData) {
                body.innerHTML = '<div class="map-chart-loading">' + this._escapeHtml(this.t.noData) + '</div>';
                return;
            }

            body.innerHTML = '<div class="map-chart-canvas-wrap"><canvas id="mapCombinedCanvas"></canvas></div>';
            let canvas = document.getElementById("mapCombinedCanvas");
            this._renderCombinedChart(canvas, def, histData);

            if (!this._chartRealtimePaused) {
                this._startChartRealtime();
            }
        } catch (err) {
            console.error("Chart loading error:", err);
            body.innerHTML = '<div class="map-chart-loading">' + this._escapeHtml(this.t.errorChart) + '</div>';
        }
    }

    _closeChartOverlay() {
        let overlay = document.getElementById("mapChartOverlay");
        overlay.classList.remove("show");
        if (this._activeChart) {
            this._activeChart.destroy();
            this._activeChart = null;
        }
        this._chartPanLoading = false;
        this._stopChartRealtime();
        this._chartStartTime = null;
        this._chartEndTime = null;
        if (this._drOutsideClickHandler) {
            document.removeEventListener('click', this._drOutsideClickHandler);
            this._drOutsideClickHandler = null;
        }
    }

    _startChartRealtime() {
        this._stopChartRealtime();
        this._chartRealtimePaused = false;
        this._chartAtLiveEdge = true;
        this._chartRealtimeTimer = setInterval(() => {
            if (this._chartRealtimePaused) return;
            this._tickChartRealtime();
        }, this.refreshRate);
    }

    _stopChartRealtime() {
        if (this._chartRealtimeTimer) {
            clearInterval(this._chartRealtimeTimer);
            this._chartRealtimeTimer = null;
        }
        this._chartRealtimePaused = false;
    }

    _checkIfAtLiveEdge(chart) {
        if (!chart || !chart.scales || !chart.scales.x) return false;
        let xScale = chart.scales.x;
        let visibleMax = xScale.max;
        let dataMax = 0;
        for (let ds of chart.data.datasets) {
            if (ds.data.length > 0) {
                let lastPoint = ds.data[ds.data.length - 1];
                let ts = lastPoint.x instanceof Date ? lastPoint.x.getTime() : lastPoint.x;
                if (ts > dataMax) dataMax = ts;
            }
        }
        if (dataMax === 0) return true;
        let tolerance = Math.max((xScale.max - xScale.min) * 0.02, 30000);
        return visibleMax >= dataMax - tolerance;
    }

    async _tickChartRealtime() {
        if (!this._chartStartTime || !this._chartEndTime) return;
        let overlay = document.getElementById("mapChartOverlay");
        if (!overlay || !overlay.classList.contains("show")) return;
        if (!this._activeChart) return;

        let now = new Date();
        this._chartEndTime = now;
        this._chartDataEnd = now.getTime();

        let fetchStart = new Date(now.getTime() - this.refreshRate * 3);
        try {
            let def = this.markers[this._combinedMarkerIdx];
            if (!def) return;
            let cnlNums = def.channels.map(c => c.cnlNum);
            if (this._showStatusTimelines && def.statusChannels) {
                cnlNums = cnlNums.concat(def.statusChannels.map(c => c.cnlNum));
            }
            let histData = await this._fetchHistDataRange(cnlNums, fetchStart, now);
            if (!histData || !histData.timestamps || histData.timestamps.length === 0) return;

            this._appendChartData(this._activeChart, histData);

            if (this._chartAtLiveEdge) {
                let xScale = this._activeChart.scales.x;
                let visibleDuration = xScale.max - xScale.min;
                this._activeChart.options.scales.x.min = now.getTime() - visibleDuration;
                this._activeChart.options.scales.x.max = now.getTime();
                this._activeChart.update('none');
            }
        } catch (err) {
            console.error("Chart realtime update error:", err);
        }
    }

    _appendChartData(chart, histData) {
        let newTimestamps = histData.timestamps.map(t => t.ms);
        let cnlToTrendIdx = {};
        for (let i = 0; i < histData.cnlNums.length; i++) {
            cnlToTrendIdx[histData.cnlNums[i]] = i;
        }
        for (let ds of chart.data.datasets) {
            let trendIdx = cnlToTrendIdx[ds._cnlNum];
            if (trendIdx === undefined) continue;
            let trend = histData.trends[trendIdx];
            let lastTs = ds.data.length > 0 ? ds.data[ds.data.length - 1].x : 0;
            for (let idx = 0; idx < newTimestamps.length; idx++) {
                let ts = newTimestamps[idx];
                if (ts <= lastTs) continue;
                let rec = trend[idx];
                if (ds._isStatus) {
                    let active = rec && rec.d && rec.d.stat > 0 &&
                        (ds._invertStatus ? rec.d.val === 0 : rec.d.val >= 1);
                    ds.data.push({ x: ts, y: active ? ds._bandTop : ds._bandBase });
                } else {
                    ds.data.push({ x: ts, y: (rec && rec.d && rec.d.stat > 0) ? rec.d.val : null });
                }
            }
        }
        chart.update('none');
    }

    // Pan-to-load: fetch older data when user pans to left edge
    async _onChartPanComplete(chart) {
        if (this._chartPanLoading || !this._chartCnlNums) return;
        let xScale = chart.scales.x;
        let loadedRange = this._chartDataEnd - this._chartDataStart;
        let threshold = this._chartDataStart + loadedRange * 0.2;
        if (xScale.min > threshold) return;
        this._chartPanLoading = true;

        try {
            let fetchDurationMs = this._combinedDays * 24 * 60 * 60 * 1000;
            let newEnd = new Date(this._chartDataStart);
            let newStart = new Date(this._chartDataStart - fetchDurationMs);
            let histData = await this._fetchHistDataRange(this._chartCnlNums, newStart, newEnd);

            if (histData && histData.timestamps && histData.timestamps.length > 0) {
                this._prependChartData(chart, histData);
                this._chartDataStart = newStart.getTime();
            }
        } catch (err) {
            console.error("Pan-load error:", err);
        } finally {
            this._chartPanLoading = false;
        }
    }

    _prependChartData(chart, histData) {
        let newTimestamps = histData.timestamps.map(t => t.ms);
        let cnlToTrendIdx = {};
        for (let i = 0; i < histData.cnlNums.length; i++) {
            cnlToTrendIdx[histData.cnlNums[i]] = i;
        }
        for (let ds of chart.data.datasets) {
            let trendIdx = cnlToTrendIdx[ds._cnlNum];
            if (trendIdx === undefined) continue;
            let trend = histData.trends[trendIdx];
            let newPoints;
            if (ds._isStatus) {
                newPoints = trend.map((rec, idx) => ({
                    x: newTimestamps[idx],
                    y: (rec && rec.d && rec.d.stat > 0 &&
                        (ds._invertStatus ? rec.d.val === 0 : rec.d.val >= 1)) ? ds._bandTop : ds._bandBase
                }));
            } else if (ds._scale) {
                newPoints = trend.map((rec, idx) => ({
                    x: newTimestamps[idx],
                    y: (rec && rec.d && rec.d.stat > 0) ? rec.d.val * ds._scale : null
                }));
            } else {
                newPoints = trend.map((rec, idx) => ({
                    x: newTimestamps[idx],
                    y: (rec && rec.d && rec.d.stat > 0) ? rec.d.val : null
                }));
            }
            let existingFirstX = ds.data.length > 0 ? ds.data[0].x : Infinity;
            let filtered = newPoints.filter(p => p.x < existingFirstX);
            ds.data = filtered.concat(ds.data);
        }
        chart.update('none');
    }

} // end class ScadaChart
```

---

## 5. API Contract

The chart fetches data from a single endpoint:

### `GET /Api/Main/GetHistData`

**Query parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `archiveBit` | int | Resolution: 0=second, 1=minute, 2=hour |
| `startTime` | ISO 8601 | Start of range (e.g. `2025-01-01T00:00:00.000Z`) |
| `endTime` | ISO 8601 | End of range |
| `endInclusive` | bool | Include the end timestamp |
| `cnlNums` | string | Comma-separated channel numbers (e.g. `101,102,103`) |

**Response JSON:**

```json
{
  "ok": true,
  "data": {
    "timestamps": [
      { "ms": 1706745600000 },
      { "ms": 1706745660000 }
    ],
    "cnlNums": [101, 102, 103],
    "trends": [
      [
        { "d": { "val": 85.3, "stat": 1 } },
        { "d": { "val": 86.1, "stat": 1 } }
      ],
      [
        { "d": { "val": 45.2, "stat": 1 } },
        { "d": { "val": null, "stat": 0 } }
      ],
      [
        { "d": { "val": 12.8, "stat": 1 } },
        { "d": { "val": 12.9, "stat": 1 } }
      ]
    ]
  }
}
```

- `timestamps` — array of `{ms: epoch_ms}`, shared across all channels
- `cnlNums` — array matching `trends` order (may differ from request order)
- `trends[i]` — array of records for `cnlNums[i]`, same length as `timestamps`
- `d.val` — the value; `d.stat` — signal quality (0=bad, >0=good)

### Marker definition format

```js
let markers = [
    {
        name: "ЦТП-01",                // display name
        channels: [
            { cnlNum: 101, alias: "T1" },   // supply temperature
            { cnlNum: 102, alias: "T2" },   // return temperature
            { cnlNum: 103, alias: "P1" },   // supply pressure
            { cnlNum: 104, alias: "P2" }    // return pressure
        ],
        statusChannels: [                   // optional status bands
            { cnlNum: 201, alias: "Авария" },
            { cnlNum: 202, alias: "Неисправность" }
        ]
    }
];
```

---

## 6. Integration Guide

### Minimal integration (5 steps)

1. **Copy the HTML** (§2) into your page

2. **Copy the CSS** (§3) into a `<style>` tag or `.css` file

3. **Copy the JS class** (§4) into a `<script>` tag or `.js` file

4. **Initialize:**
```js
let chart = new ScadaChart({
    apiRoot: "/your/api/path/",   // must serve GetHistData
    markers: [
        {
            name: "My Sensor",
            channels: [
                { cnlNum: 101, alias: "T1" },
                { cnlNum: 102, alias: "P1" }
            ],
            statusChannels: []
        }
    ],
    refreshRate: 10000
});
```

5. **Open a chart:**
```js
chart.openChart(0);   // opens chart for markers[0]
```

### Key features included

- **Zoom & Pan** — mouse wheel zoom, drag to pan, pinch on mobile
- **Pan-to-load** — automatically fetches older data when panning left
- **Real-time updates** — polls for new data at `refreshRate` interval
- **Live legend** — fixed bar above chart shows values at cursor position (no floating tooltip)
- **Crosshair** — vertical dashed line follows cursor
- **Date range picker** — custom widget with calendar + time tuner dial
- **Auto-resolution** — picks second/minute/hour archive based on visible range
- **Gap detection** — inserts null breaks when data has gaps
- **Status bands** — optional colored timeline bars for on/off signals
- **LTTB decimation** — Chart.js decimation for smooth rendering of large datasets

### Adapting to your API

If your API has a different format, modify `_fetchHistDataRange()` to transform
the response into the expected structure:

```js
{
    timestamps: [{ms: epoch_ms}, ...],
    cnlNums: [int, ...],
    trends: [[ {d: {val: number, stat: number}}, ... ], ...]
}
```

### Localization

Change the `t = { ... }` object in the class to your language. Month and weekday
names are in `_buildCalendar()`.
