var tcChartInstance = null;

class TcChartManager {
    static LEAFLET_CSS = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";
    static LEAFLET_JS = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.js";
    static CHARTJS_URL = "https://cdn.jsdelivr.net/npm/chart.js@4.4.7/dist/chart.umd.min.js";
    static CHARTJS_ADAPTER_URL = "https://cdn.jsdelivr.net/npm/chartjs-adapter-date-fns@3.0.0/dist/chartjs-adapter-date-fns.bundle.min.js";
    static CHARTJS_ZOOM_URL = "https://cdn.jsdelivr.net/npm/chartjs-plugin-zoom@2.0.1/dist/chartjs-plugin-zoom.min.js";
    static HAMMERJS_URL = "https://cdn.jsdelivr.net/npm/hammerjs@2.0.8/hammer.min.js";
    static CHART_COLORS = [
        "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728", "#9467bd",
        "#8c564b", "#e377c2", "#7f7f7f", "#bcbd22", "#17becf"
    ];

    // --- Localization (from SCADA language settings) ---
    static LANG = {
        searchPlaceholder: "Поиск маркеров...",
        noResults: "Нет результатов",
        combinedChart: "Графики",
        loading: "Загрузка...",
        loadingChart: "Загрузка данных...",
        noData: "Нет архивных данных",
        errorChart: "Ошибка загрузки данных",
        error: "Ошибка",
        errorPrefix: "Ошибка: ",
        ago1h: "-1ч",
        ago1d: "-1д",
        now: "сейчас",
        channel: "Кан",
        details: "Подробнее",
        pressureUnit: "кгс/см²",
        statusesLabel: "Статусы",
        gradientLabel: "Градиент",
        gradientOff: "Выкл",
        start: "Начало",
        end: "Конец",
        startDateTime: "Дата и время начала",
        endDateTime: "Дата и время конца",
        apply: "Применить",
        reset: "Сброс",
        markerFilterLabel: "Маркеры",
        markerFilterAll: "Все маркеры",
        markerFilterCircles: "Круги",
        markerFilterTriangles: "Треугольники"
    };

    constructor(options) {
        this.viewID = options.viewID;
        this.refreshRate = options.refreshRate || 1000;
        this.rootPath = this._detectRootPath();
        this.map = null;
        this.markers = [];
        this.markerEntries = [];
        this.updateTimer = null;
        this.curDataMap = {};
        this._chartJsLoaded = false;
        this._chartCache = {};
        this._activeChart = null;
        this._activeMiniChart = null;
        this._miniChartHideTimer = null;
        this._combinedMarkerIdx = -1;
        this._combinedDays = 1;
        this._chartDataStart = null;
        this._chartDataEnd = null;
        this._chartCnlNums = null;
        this._chartMarkerDef = null;
        this._chartPanLoading = false;
        this.t = TcChartManager.LANG;
    }

    _detectRootPath() {
        let b = document.querySelector("base");
        return b ? b.getAttribute("href") : "/";
    }

    _apiRoot() { return this.rootPath + "Api/Map/"; }
    _mainApiRoot() { return this.rootPath + "Api/Main/"; }

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

    // --- Resource loading ---
    _loadLeafletCss() {
        return new Promise((resolve) => {
            if (document.querySelector('link[href*="leaflet"]')) { resolve(); return; }
            let link = document.createElement("link");
            link.rel = "stylesheet";
            link.href = TcChartManager.LEAFLET_CSS;
            link.integrity = "sha256-p4NxAoJBhIIN+hmNHrzRCf9tD/miZyoHS5obTRR9BMY=";
            link.crossOrigin = "";
            link.onload = resolve;
            document.head.appendChild(link);
        });
    }

    _loadLeafletJs() {
        return new Promise((resolve, reject) => {
            if (typeof L !== "undefined") { resolve(); return; }
            let script = document.createElement("script");
            script.src = TcChartManager.LEAFLET_JS;
            script.integrity = "sha256-20nQCchB9co0qIjJZRGuk2/Z9VM+kNiyxNV1lvTlZBo=";
            script.crossOrigin = "";
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
        });
    }

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
            // Load Chart.js first, then adapter + Hammer.js in parallel, then zoom plugin
            loadScript(TcChartManager.CHARTJS_URL)
                .then(() => Promise.all([
                    loadScript(TcChartManager.CHARTJS_ADAPTER_URL),
                    loadScript(TcChartManager.HAMMERJS_URL)
                ]))
                .then(() => loadScript(TcChartManager.CHARTJS_ZOOM_URL))
                .then(() => { this._chartJsLoaded = true; resolve(); })
                .catch(reject);
        });
        return this._chartJsLoadPromise;
    }

    // --- Marker icons (zoom-dependent size) ---
    _getMarkerSize() {
        if (!this.map) return 20;
        let z = this.map.getZoom();
        // 10px at zoom 5, 20px at zoom 10, 32px at zoom 15, 42px at zoom 18+
        return Math.round(Math.min(42, Math.max(10, z * 2.4 - 2)));
    }

    _createMarkerIcon(color, hasAlert) {
        color = color || "#3388ff";
        let size = this._getMarkerSize();
        let r = Math.round(size * 0.42);
        let sw = Math.max(1.5, size * 0.08);
        let half = size / 2;
        let ringColor = hasAlert ? "#d32f2f" : "#fff";
        let ringClass = hasAlert ? ' class="marker-ring-blink"' : '';
        let svg = '<svg xmlns="http://www.w3.org/2000/svg" width="' + size + '" height="' + size + '" viewBox="0 0 ' + size + ' ' + size + '" overflow="hidden">' +
            '<circle cx="' + half + '" cy="' + half + '" r="' + r + '" fill="' + color + '" stroke="none"/>' +
            '<circle cx="' + half + '" cy="' + half + '" r="' + r + '" fill="none" stroke="' + ringColor + '" stroke-width="' + sw + '"' + ringClass + '/>' +
            '</svg>';
        return L.divIcon({
            html: svg,
            className: "map-marker-icon",
            iconSize: [size, size],
            iconAnchor: [half, half],
            popupAnchor: [0, -half - 2]
        });
    }

    _createTriangleMarkerIcon(color, hasAlert, alertColor) {
        color = color || "#3388ff";
        let size = this._getMarkerSize();
        let half = size / 2;
        let sw = Math.max(1.5, size * 0.08);
        let ringColor = hasAlert ? (alertColor || "#d32f2f") : "#fff";
        let svgClass = hasAlert ? ' class="marker-ring-blink"' : '';
        // Inset vertices by half the stroke width so stroke stays within SVG viewport
        let inset = Math.ceil(sw / 2) + 1;
        // Triangle points: downward — top-left, top-right, bottom-center
        let pts = inset + ',' + inset + ' ' + (size - inset) + ',' + inset + ' ' + half + ',' + (size - inset);
        let svg = '<svg xmlns="http://www.w3.org/2000/svg" width="' + size + '" height="' + size + '" viewBox="0 0 ' + size + ' ' + size + '" overflow="hidden"' + svgClass + '>' +
            '<polygon points="' + pts + '" fill="' + color + '" stroke="none"/>' +
            '<polygon points="' + pts + '" fill="none" stroke="' + ringColor + '" stroke-width="' + sw + '" stroke-linejoin="round"/>' +
            '</svg>';
        return L.divIcon({
            html: svg,
            className: "map-marker-icon",
            iconSize: [size, size],
            iconAnchor: [half, size],
            popupAnchor: [0, -size]
        });
    }

    // Check triangle flood alert: returns { active, color }
    // 700mm flood (ch[3], val===0) = red (#d32f2f), 200mm flood (ch[2], val===0) = yellow (#f0ad4e)
    // Red takes priority over yellow
    _getTriangleFloodAlert(def) {
        let ch = def.channels || [];
        if (!this._hasAnyWorkingChannel(def)) return { active: false, color: null };
        let f700 = ch[3] ? this.curDataMap[ch[3].cnlNum] : null;
        let f200 = ch[2] ? this.curDataMap[ch[2].cnlNum] : null;
        if (f700 && f700.stat > 0 && f700.val === 0) return { active: true, color: '#d32f2f' };
        if (f200 && f200.stat > 0 && f200.val === 0) return { active: true, color: '#f0ad4e' };
        return { active: false, color: null };
    }

    // Triangle marker color based on flood state:
    // gray=no signal, blue=ok, yellow=200mm flood, red=700mm flood
    _getTriangleMarkerColor(def) {
        let channels = def.channels || [];
        if (channels.length === 0) return "#999";
        let working = 0;
        for (let ch of channels) {
            let rec = this.curDataMap[ch.cnlNum];
            if (rec && rec.stat > 0) working++;
        }
        if (working === 0) return "#999";
        let f700 = channels[3] ? this.curDataMap[channels[3].cnlNum] : null;
        let f200 = channels[2] ? this.curDataMap[channels[2].cnlNum] : null;
        if (f700 && f700.stat > 0 && f700.val === 0) return "#d32f2f"; // red
        if (f200 && f200.stat > 0 && f200.val === 0) return "#f0ad4e"; // yellow
        return "#3388ff"; // blue — no flood
    }

    // Check if at least one parameter channel has valid signal
    _hasAnyWorkingChannel(def) {
        let channels = def.channels || [];
        for (let ch of channels) {
            let rec = this.curDataMap[ch.cnlNum];
            if (rec && rec.stat > 0) return true;
        }
        return false;
    }

    // Check if marker has any active status (val >= 1 AND valid signal)
    // If all parameter channels are dead, status is unreliable — return false
    _hasActiveStatus(def) {
        let stCh = def.statusChannels;
        if (!stCh || stCh.length === 0) return false;
        if (!this._hasAnyWorkingChannel(def)) return false;
        for (let ch of stCh) {
            let rec = this.curDataMap[ch.cnlNum];
            if (rec && rec.stat > 0 && rec.val >= 1) return true;
        }
        return false;
    }

    // Returns status dot data from statusChannels
    // If all parameter channels are dead, all dots show as off
    _getStatusDots(def) {
        let stCh = def.statusChannels;
        if (!stCh || stCh.length === 0) return [];
        let anyWorking = this._hasAnyWorkingChannel(def);
        return stCh.map(ch => {
            let rec = this.curDataMap[ch.cnlNum];
            return { alias: ch.alias, on: !!(anyWorking && rec && rec.stat > 0 && rec.val >= 1), cnlNum: ch.cnlNum };
        });
    }

    _refreshAllMarkerIcons() {
        for (let entry of this.markerEntries) {
            let isTriangle = entry.def.type === 'triangle';
            let color = isTriangle
                ? this._getTriangleMarkerColor(entry.def)
                : this._getGradientMarkerColor(entry.def);
            let icon;
            if (isTriangle) {
                icon = this._createTriangleMarkerIcon(color, false, null);
            } else {
                let alert = this._hasActiveStatus(entry.def);
                icon = this._createMarkerIcon(color, alert);
            }
            entry.leafletMarker.setIcon(icon);
        }
    }

    _getGradientMarkerColor(def) {
        if (!this._activeGradientAlias) return this._getMarkerColor(def);
        let ch = (def.channels || []).find(c => c.alias === this._activeGradientAlias);
        if (!ch) return "#999";
        let rec = this.curDataMap[ch.cnlNum];
        if (!rec || rec.stat <= 0) return "#999";
        let gl = this._getGradientLimits(this._activeGradientAlias);
        return this._getGradientColor(rec.val, gl.min, gl.max);
    }

    // --- Axis helpers ---
    _getAxisLimits(alias) {
        let a = (alias || "").toUpperCase();
        if (a === "T1" || a === "T2") return { min: 0, max: 120, unit: "°C" };
        if (a === "T3")               return { min: 0, max: 80,  unit: "°C" };
        if (a === "P1" || a === "P2") return { min: 0, max: 25,  unit: this.t.pressureUnit };
        return null;
    }

    _getAxisGroup(alias) {
        let a = (alias || "").toUpperCase();
        if (a === "P1" || a === "P2") return "pressure";
        return "temp";
    }

    // Fixed color per channel alias (pipeline conventions)
    static ALIAS_COLORS = {
        "T1": "#d32f2f",  // red — supply temp
        "T2": "#1f77b4",  // blue — return temp
        "T3": "#ff7f0e",  // orange — DHW supply
        "P1": "#2ca02c",  // green — supply pressure
        "P2": "#17becf"   // teal — return pressure
    };

    _getChannelColor(alias, index) {
        let a = (alias || "").toUpperCase();
        return TcChartManager.ALIAS_COLORS[a] || TcChartManager.CHART_COLORS[index % TcChartManager.CHART_COLORS.length];
    }

    _isPressureAlias(alias) {
        let a = (alias || "").toUpperCase();
        return a === "P1" || a === "P2";
    }

    // --- Init ---
    // No-op in ThermalCamera context — original PlgMap init() bootstraps Leaflet
    // and fetches map markers; we only need the chart subsystem. The chart entry
    // is openCombinedChart(def, days), which lazy-loads Chart.js on first use.
    async init() { return; }

    async _initOriginalUnused() {
        await this._loadLeafletCss();
        await this._loadLeafletJs();

        let response = await fetch(this._apiRoot() + "GetMapData?viewID=" + this.viewID);
        let dto = await response.json();

        if (!dto.ok) {
            console.error("Failed to load map data:", dto.msg);
            document.getElementById("divMapContainer").textContent = this.t.errorPrefix + dto.msg;
            return;
        }

        let config = dto.data.config;
        this.markers = dto.data.markers;

        // set search placeholder
        let searchInput = document.getElementById("mapSearchInput");
        if (searchInput) searchInput.placeholder = this.t.searchPlaceholder;

        this.map = L.map("divMapContainer", {
            minZoom: config.minZoom,
            maxZoom: config.maxZoom
        }).setView([config.centerLat, config.centerLng], config.zoom);

        let tileUrl = config.tileUrlTemplate || "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png";
        L.tileLayer(tileUrl, {
            attribution: '',
            maxZoom: config.maxZoom
        }).addTo(this.map);

        this.markerEntries = [];

        for (let i = 0; i < this.markers.length; i++) {
            let m = this.markers[i];
            if (!m.name && m.caption) m.name = m.caption;
            if (!m.name) m.name = "";
            if (!m.id) m.id = i + 1;
            if (!m.channels || m.channels.length === 0) {
                if (m.cnlNum) {
                    m.channels = [{ cnlNum: m.cnlNum, alias: m.caption || "" }];
                } else {
                    m.channels = [];
                }
            }
            if (!m.statusChannels) m.statusChannels = [];
            if (!m.extraChannelGroups) m.extraChannelGroups = [];

            let isTriangle = m.type === 'triangle';
            let icon = isTriangle
                ? this._createTriangleMarkerIcon(null)
                : this._createMarkerIcon(null);

            let lMarker = L.marker([m.lat, m.lng], {
                icon: icon,
                title: m.name
            }).addTo(this.map);

            let popupOpts = { maxWidth: 350, minWidth: 160, closeButton: false, autoPan: false };
            let popupHtml = isTriangle
                ? this._buildTrianglePopupHtml(m)
                : this._buildPopupHtml(m);
            lMarker.bindPopup(popupHtml, popupOpts);
            lMarker.on('popupclose', () => this._hideExtraPopup());

            this.markerEntries.push({ id: m.id, def: m, leafletMarker: lMarker });
        }

        // resize markers on zoom
        this.map.on('zoomend', () => this._refreshAllMarkerIcons());

        // Hide extra popup on map move/zoom (it uses position:fixed and can't track the Leaflet popup)
        this.map.on('move zoom zoomstart', () => this._hideExtraPopup());

        this._initSearch();
        this._initChartOverlay();
        this._initMiniChartTooltip();
        this._initStatusPanel();
        this._initGradientPanel();
        this._initMarkerFilter();
        this._refreshData();
        this.updateTimer = setInterval(() => this._refreshData(), this.refreshRate);
    }

    // --- Search ---
    _initSearch() {
        let input = document.getElementById("mapSearchInput");
        let results = document.getElementById("mapSearchResults");
        if (!input || !results) return;

        input.addEventListener("input", () => {
            let query = input.value.trim().toLowerCase();
            if (query.length === 0) {
                results.classList.remove("show");
                results.innerHTML = "";
                return;
            }

            let matches = this.markerEntries.filter(e => {
                if (!e.def.name.toLowerCase().includes(query)) return false;
                if (this._markerFilter === 'circle' && e.def.type === 'triangle') return false;
                if (this._markerFilter === 'triangle' && e.def.type !== 'triangle') return false;
                return true;
            });

            if (matches.length === 0) {
                results.innerHTML = '<div class="map-search-item" style="color:#999">' +
                    this._escapeHtml(this.t.noResults) + '</div>';
                results.classList.add("show");
                return;
            }

            results.innerHTML = matches.slice(0, 20).map(e =>
                '<div class="map-search-item" data-marker-id="' + e.id + '">' +
                this._escapeHtml(e.def.name) + '</div>'
            ).join("");
            results.classList.add("show");
        });

        results.addEventListener("click", (ev) => {
            let item = ev.target.closest(".map-search-item");
            if (!item || !item.dataset.markerId) return;

            let id = parseInt(item.dataset.markerId, 10);
            let entry = this.markerEntries.find(e => e.id === id);
            if (entry) {
                this.map.setView(entry.leafletMarker.getLatLng(), 15);
                entry.leafletMarker.openPopup();
            }

            results.classList.remove("show");
            input.value = "";
        });

        document.addEventListener("click", (ev) => {
            if (!ev.target.closest(".map-search-container")) {
                results.classList.remove("show");
            }
        });
    }

    // --- Status Panel (persistent button top-right) ---
    _initStatusPanel() {
        // Inject keyframes via JS to avoid Razor CSS parser warnings
        let style = document.createElement("style");
        style.textContent = "@@keyframes ringBlink { 0%, 100% { opacity: 1; } 50% { opacity: 0.15; } }";
        document.head.appendChild(style);

        let btn = document.getElementById("mapStatusBtn");
        let panel = document.getElementById("mapStatusPanel");
        if (!btn || !panel) return;

        btn.addEventListener("click", () => {
            panel.classList.toggle("show");
        });

        document.addEventListener("click", (ev) => {
            if (!ev.target.closest(".tc-status-btn")) {
                panel.classList.remove("show");
            }
        });

        panel.addEventListener("click", (ev) => {
            let item = ev.target.closest(".tc-status-panel-item");
            if (!item || !item.dataset.markerId) return;
            let id = parseInt(item.dataset.markerId, 10);
            let entry = this.markerEntries.find(e => e.id === id);
            if (entry) {
                this.map.setView(entry.leafletMarker.getLatLng(), 15);
                entry.leafletMarker.openPopup();
            }
        });
    }

    // --- Gradient Panel ---
    _activeGradientAlias = null;

    _initGradientPanel() {
        let btn = document.getElementById("mapGradientBtn");
        let panel = document.getElementById("mapGradientPanel");
        if (!btn || !panel) return;

        // Collect unique temperature channel aliases (T1, T2, T3) across all markers
        let tempAliases = new Set();
        for (let entry of this.markerEntries) {
            for (let ch of (entry.def.channels || [])) {
                let a = (ch.alias || "").toUpperCase();
                if (a === "T1" || a === "T2" || a === "T3") {
                    tempAliases.add(ch.alias);
                }
            }
        }
        this._gradientAliases = [...tempAliases].sort();

        if (this._gradientAliases.length === 0) {
            btn.style.display = "none";
            return;
        }

        btn.addEventListener("click", () => {
            this._buildGradientPanel();
            panel.classList.toggle("show");
        });

        document.addEventListener("click", (ev) => {
            if (!ev.target.closest(".map-gradient-btn")) {
                panel.classList.remove("show");
            }
        });
    }

    _buildGradientPanel() {
        let panel = document.getElementById("mapGradientPanel");
        let html = '';

        // "Off" option
        html += '<div class="map-gradient-item' + (!this._activeGradientAlias ? ' selected' : '') + '" data-alias="">' +
            '<span class="radio"></span>' + this._escapeHtml(this.t.gradientOff) + '</div>';

        for (let alias of this._gradientAliases) {
            let sel = this._activeGradientAlias === alias ? ' selected' : '';
            html += '<div class="map-gradient-item' + sel + '" data-alias="' + this._escapeHtml(alias) + '">' +
                '<span class="radio"></span>' + this._escapeHtml(alias) + '</div>';
        }

        // Gradient legend bar
        if (this._activeGradientAlias) {
            let gl = this._getGradientLimits(this._activeGradientAlias);
            html += '<div class="map-gradient-legend"></div>';
            html += '<div class="map-gradient-legend-labels"><span>' + gl.min + '°C</span><span>' + gl.max + '°C</span></div>';
        }

        panel.innerHTML = html;

        // Bind clicks
        panel.querySelectorAll('.map-gradient-item').forEach(item => {
            item.addEventListener('click', () => {
                let alias = item.dataset.alias;
                this._activeGradientAlias = alias || null;

                let btn = document.getElementById("mapGradientBtn");
                if (this._activeGradientAlias) {
                    btn.classList.add("active");
                    btn.textContent = this.t.gradientLabel + ': ' + this._activeGradientAlias;
                } else {
                    btn.classList.remove("active");
                    btn.textContent = this.t.gradientLabel;
                }

                this._buildGradientPanel();
                this._refreshAllMarkerIcons();
            });
        });
    }

    _getGradientLimits(alias) {
        let a = (alias || this._activeGradientAlias || "").toUpperCase();
        if (a === "T3") return { min: 40, max: 80 };
        return { min: 40, max: 120 };
    }

    _getGradientColor(value, min, max) {
        // Clamp to [0, 1]
        let t = (value - min) / (max - min);
        t = Math.max(0, Math.min(1, t));
        // Blue (#3388ff) -> Purple (#9b59b6) -> Red (#d32f2f)
        let r, g, b;
        if (t <= 0.5) {
            let s = t * 2; // 0..1 for first half
            r = Math.round(51 + (155 - 51) * s);
            g = Math.round(136 + (89 - 136) * s);
            b = Math.round(255 + (182 - 255) * s);
        } else {
            let s = (t - 0.5) * 2; // 0..1 for second half
            r = Math.round(155 + (211 - 155) * s);
            g = Math.round(89 + (47 - 89) * s);
            b = Math.round(182 + (47 - 182) * s);
        }
        return "rgb(" + r + "," + g + "," + b + ")";
    }

    // --- Marker Type Filter ---
    _markerFilter = 'all'; // 'all', 'circle', 'triangle'

    _initMarkerFilter() {
        let btn = document.getElementById("mapMarkerFilterBtn");
        let panel = document.getElementById("mapMarkerFilterPanel");
        if (!btn || !panel) return;

        // Check if we have both types; hide button if only one type exists
        let hasCircle = this.markerEntries.some(e => e.def.type !== 'triangle');
        let hasTriangle = this.markerEntries.some(e => e.def.type === 'triangle');
        if (!hasCircle || !hasTriangle) {
            btn.parentElement.style.display = 'none';
            return;
        }

        btn.addEventListener("click", () => {
            this._buildMarkerFilterPanel();
            panel.classList.toggle("show");
        });

        document.addEventListener("click", (ev) => {
            if (!ev.target.closest(".map-marker-filter-btn")) {
                panel.classList.remove("show");
            }
        });
    }

    _buildMarkerFilterPanel() {
        let panel = document.getElementById("mapMarkerFilterPanel");
        let options = [
            { key: 'all', label: this.t.markerFilterAll },
            { key: 'circle', label: this.t.markerFilterCircles },
            { key: 'triangle', label: this.t.markerFilterTriangles }
        ];
        let html = '';
        for (let opt of options) {
            let sel = this._markerFilter === opt.key ? ' selected' : '';
            html += '<div class="map-marker-filter-item' + sel + '" data-filter="' + opt.key + '">' +
                '<span class="radio"></span>' + this._escapeHtml(opt.label) + '</div>';
        }
        panel.innerHTML = html;

        panel.querySelectorAll('.map-marker-filter-item').forEach(item => {
            item.addEventListener('click', () => {
                this._markerFilter = item.dataset.filter;
                this._applyMarkerFilter();
                this._buildMarkerFilterPanel();
                panel.classList.remove("show");
            });
        });
    }

    _applyMarkerFilter() {
        for (let entry of this.markerEntries) {
            let isTriangle = entry.def.type === 'triangle';
            let visible = this._markerFilter === 'all' ||
                (this._markerFilter === 'triangle' && isTriangle) ||
                (this._markerFilter === 'circle' && !isTriangle);

            if (visible) {
                if (!this.map.hasLayer(entry.leafletMarker)) {
                    entry.leafletMarker.addTo(this.map);
                }
            } else {
                if (this.map.hasLayer(entry.leafletMarker)) {
                    this.map.removeLayer(entry.leafletMarker);
                }
            }
        }
    }

    _updateStatusPanel() {
        let panel = document.getElementById("mapStatusPanel");
        let countEl = document.getElementById("mapStatusCount");
        if (!panel || !countEl) return;

        let problemEntries = [];
        for (let entry of this.markerEntries) {
            let isTriangle = entry.def.type === 'triangle';
            if (this._markerFilter === 'circle' && isTriangle) continue;
            if (this._markerFilter === 'triangle' && !isTriangle) continue;

            if (isTriangle) {
                // Triangle: check flood channels (ch[2]=200mm, ch[3]=700mm), val===0 means flood
                let ch = entry.def.channels || [];
                let floodDots = [];
                if (ch[2]) {
                    let rec = this.curDataMap[ch[2].cnlNum];
                    if (rec && rec.stat > 0 && rec.val === 0)
                        floodDots.push({ alias: ch[2].alias || '200мм', on: true, cnlNum: ch[2].cnlNum, color: '#f0ad4e' });
                }
                if (ch[3]) {
                    let rec = this.curDataMap[ch[3].cnlNum];
                    if (rec && rec.stat > 0 && rec.val === 0)
                        floodDots.push({ alias: ch[3].alias || '700мм', on: true, cnlNum: ch[3].cnlNum, color: '#d32f2f' });
                }
                if (floodDots.length > 0) {
                    problemEntries.push({ entry, dots: floodDots, activeCount: floodDots.length, isTriangle: true });
                }
            } else {
                let dots = this._getStatusDots(entry.def);
                let active = dots.filter(d => d.on);
                if (active.length > 0) {
                    problemEntries.push({ entry, dots, activeCount: active.length });
                }
            }
        }

        if (problemEntries.length === 0) {
            countEl.classList.add("hidden");
            panel.innerHTML = '<div class="tc-status-panel-empty">' +
                this._escapeHtml(this.t.noResults) + '</div>';
            return;
        }

        countEl.textContent = problemEntries.length;
        countEl.classList.remove("hidden");

        let html = '';
        for (let pe of problemEntries) {
            html += '<div class="tc-status-panel-item" data-marker-id="' + pe.entry.id + '">';
            html += '<div class="tc-status-panel-name">' + this._escapeHtml(pe.entry.def.name) + '</div>';
            html += '<div class="tc-status-panel-dots">';
            for (let d of pe.dots) {
                if (!d.on) continue; // only show active statuses
                let dotColor = d.color || '#d32f2f';
                html += '<span class="tc-status-panel-dot">' +
                    '<span class="dot" style="background:' + dotColor + '"></span>' +
                    this._escapeHtml(d.alias) + '</span>';
            }
            html += '</div></div>';
        }
        panel.innerHTML = html;
    }

    // --- Photo viewer ---
    _openPhoto(url) {
        let overlay = document.getElementById("tcPhotoOverlay");
        let img = document.getElementById("tcPhotoImg");
        if (!overlay || !img) return;
        img.src = url;
        overlay.classList.add("show");
    }

    _buildPhotoButton(photoUrl) {
        if (!photoUrl) return '';
        // Build URL through the API endpoint to serve from SCADA Views storage
        let apiUrl = this._apiRoot() + 'GetPhoto?path=' + encodeURIComponent(photoUrl);
        return '<button class="map-popup-photo-btn" onclick="event.stopPropagation();tcChartInstance._openPhoto(\'' +
            apiUrl.replace(/'/g, "\\'") + '\')">' +
            '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#555" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
            '<rect x="3" y="3" width="18" height="18" rx="2"/>' +
            '<circle cx="8.5" cy="8.5" r="1.5"/>' +
            '<path d="M21 15l-5-5L5 21"/>' +
            '</svg></button>';
    }

    // --- Popups (clean fonts, button for combined chart) ---
    _buildPopupHtml(def) {
        let statusDots = this._getStatusDots(def);
        let html = '';

        // Name + photo button row
        let photoBtn = this._buildPhotoButton(def.photo);
        if (photoBtn) {
            html += '<div class="map-popup-name-row"><span class="map-popup-name">' + this._escapeHtml(def.name) + '</span>' + photoBtn + '</div>';
        } else {
            html += '<span class="map-popup-name">' + this._escapeHtml(def.name) + '</span>';
        }

        // Status dots horizontal row under name
        let POPUP_STATUS_COLORS = ["#f0ad4e", "#f57c00", "#d32f2f", "#ff9800", "#7b1fa2"];
        if (statusDots.length > 0) {
            html += '<div class="map-popup-header-dots">';
            for (let si = 0; si < statusDots.length; si++) {
                let d = statusDots[si];
                let severityColor = POPUP_STATUS_COLORS[si % POPUP_STATUS_COLORS.length];
                let dotColor = d.on ? severityColor : "#bbb";
                html += '<span class="map-popup-status-dot" style="background:' + dotColor + '" ' +
                    'onmouseenter="tcChartInstance._showMiniChart(event, ' + d.cnlNum + ', \'' +
                        this._escapeHtml(d.alias).replace(/'/g, "\\'") + '\', \'' + dotColor + '\', \'above\')" ' +
                    'onmouseleave="tcChartInstance._scheduleMiniChartHide()"' +
                    '></span>';
            }
            html += '</div>';
        }

        if (def.descr) {
            html += '<div class="map-popup-descr">' + this._escapeHtml(def.descr) + '</div>';
        }

        if (def.channels && def.channels.length > 0) {
            let hasExtra = def.extraChannelGroups && def.extraChannelGroups.length > 0;
            let markerIdx = this.markerEntries.findIndex(e => e.def === def);
            if (markerIdx < 0) markerIdx = this.markers.indexOf(def);

            if (hasExtra) html += '<div class="map-popup-channels-row">';

            html += '<table class="map-popup-table">';
            for (let i = 0; i < def.channels.length; i++) {
                let ch = def.channels[i];
                let rec = this.curDataMap[ch.cnlNum];
                let valText = rec ? this._escapeHtml(rec.text) : "--";
                let alias = ch.alias || (this.t.channel + ch.cnlNum);
                let color = this._getChannelColor(alias, i);
                let dash = '';

                html += '<tr>' +
                    '<td class="alias">' + this._escapeHtml(alias) + '</td>' +
                    '<td class="color-line"><svg width="40" height="2" style="vertical-align:middle;display:block"><line x1="0" y1="1" x2="40" y2="1" stroke="' + color + '" stroke-width="2"' + dash + '/></svg></td>' +
                    '<td class="val" data-cnl="' + ch.cnlNum + '">' + valText + '</td>' +
                    '<td class="chart-btn" ' +
                        'onmouseenter="tcChartInstance._showMiniChart(event, ' + ch.cnlNum + ', \'' +
                            this._escapeHtml(alias).replace(/'/g, "\\'") + '\', \'' + color + '\')" ' +
                        'onmouseleave="tcChartInstance._scheduleMiniChartHide()">' +
                        '<svg width="20" height="20" viewBox="0 0 14 14" style="vertical-align:middle">' +
                        '<polyline points="1,12 4,6 7,9 10,3 13,7" fill="none" stroke="' + color + '" stroke-width="1.5"' + dash + '/>' +
                        '</svg>' +
                    '</td>' +
                    '</tr>';
            }
            html += '</table>';

            // "More" button — narrow, spans full table height, opens extra channels popup
            if (hasExtra) {
                html += '<div class="map-popup-more-btn" ' +
                    'onclick="tcChartInstance._toggleExtraPopup(event, ' + (markerIdx >= 0 ? markerIdx : 0) + ')">' +
                    '<svg width="14" height="22" viewBox="0 0 14 22">' +
                    '<polyline points="4,4 10,11 4,18" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/>' +
                    '</svg>' +
                    '</div>';
                html += '</div>'; // close .map-popup-channels-row
            }

            html += '<button class="map-popup-chart-btn" ' +
                'onclick="tcChartInstance._openCombinedChart(' + (markerIdx >= 0 ? markerIdx : 0) + ')">' +
                this._escapeHtml(this.t.combinedChart) + '</button>';
        }

        return html;
    }

    // Build popup for triangle markers (flood/water level sensors)
    // Channels order: [0]=T200, [1]=T700, [2]=Flood200, [3]=Flood700, [4]=Battery%, [5]=Online
    _buildTrianglePopupHtml(def) {
        let markerIdx = this.markerEntries.findIndex(e => e.def === def);
        if (markerIdx < 0) markerIdx = this.markers.indexOf(def);
        let ch = def.channels || [];
        let photoBtn = this._buildPhotoButton(def.photo);
        let html = '';
        if (photoBtn) {
            html += '<div class="map-popup-name-row"><span class="map-popup-name">' + this._escapeHtml(def.name) + '</span>' + photoBtn + '</div>';
        } else {
            html += '<span class="map-popup-name">' + this._escapeHtml(def.name) + '</span>';
        }

        if (def.descr) {
            html += '<div class="map-popup-descr">' + this._escapeHtml(def.descr) + '</div>';
        }

        // Two zones side by side: 200mm and 700mm
        let t200 = ch[0] ? this.curDataMap[ch[0].cnlNum] : null;
        let t700 = ch[1] ? this.curDataMap[ch[1].cnlNum] : null;
        let f200 = ch[2] ? this.curDataMap[ch[2].cnlNum] : null;
        let f700 = ch[3] ? this.curDataMap[ch[3].cnlNum] : null;
        let t200Text = t200 ? (t200.text + ' °C') : '--';
        let t700Text = t700 ? (t700.text + ' °C') : '--';
        // Per-zone state: 'off' (no signal), 'flood' (val===0), 'ok' (no flood)
        let f200State = (!f200 || f200.stat <= 0) ? 'off' : (f200.val === 0 ? 'flood' : 'ok');
        let f700State = (!f700 || f700.stat <= 0) ? 'off' : (f700.val === 0 ? 'flood' : 'ok');

        html += '<div class="map-tri-zones">';
        // Left zone: 200mm
        html += '<div class="map-tri-zone flood-200 zone-' + f200State + '" data-zone-cnl="' + (ch[2] ? ch[2].cnlNum : 0) + '">';
        html += '<div class="map-tri-zone-label">200мм</div>';
        html += '<div class="map-tri-zone-temp" data-cnl="' + (ch[0] ? ch[0].cnlNum : 0) + '">' + this._escapeHtml(t200Text) + '</div>';
        html += '<div class="map-tri-flood-indicator" data-flood-cnl="' + (ch[2] ? ch[2].cnlNum : 0) + '"></div>';
        html += '</div>';
        // Right zone: 700mm
        html += '<div class="map-tri-zone flood-700 zone-' + f700State + '" data-zone-cnl="' + (ch[3] ? ch[3].cnlNum : 0) + '">';
        html += '<div class="map-tri-zone-label">700мм</div>';
        html += '<div class="map-tri-zone-temp" data-cnl="' + (ch[1] ? ch[1].cnlNum : 0) + '">' + this._escapeHtml(t700Text) + '</div>';
        html += '<div class="map-tri-flood-indicator" data-flood-cnl="' + (ch[3] ? ch[3].cnlNum : 0) + '"></div>';
        html += '</div>';
        html += '</div>';

        // Combined chart button
        html += '<button class="map-popup-chart-btn" ' +
            'onclick="tcChartInstance._openCombinedChart(' + (markerIdx >= 0 ? markerIdx : 0) + ')">' +
            this._escapeHtml(this.t.combinedChart) + '</button>';

        return html;
    }

    // Update triangle popup values in-place (no full rebuild)
    _updateTrianglePopupInPlace(popupEl, def) {
        let ch = def.channels || [];
        // Temperature values
        for (let i = 0; i < 2; i++) {
            if (!ch[i]) continue;
            let el = popupEl.querySelector('.map-tri-zone-temp[data-cnl="' + ch[i].cnlNum + '"]');
            let rec = this.curDataMap[ch[i].cnlNum];
            if (el && rec) el.textContent = rec.text ? (rec.text + ' °C') : '--';
        }
        // Flood zone state (border + indicator color)
        for (let i = 2; i < 4; i++) {
            if (!ch[i]) continue;
            let zoneEl = popupEl.querySelector('.map-tri-zone[data-zone-cnl="' + ch[i].cnlNum + '"]');
            if (!zoneEl) continue;
            let rec = this.curDataMap[ch[i].cnlNum];
            let state = (!rec || rec.stat <= 0) ? 'off' : (rec.val === 0 ? 'flood' : 'ok');
            zoneEl.classList.remove('zone-off', 'zone-ok', 'zone-flood');
            zoneEl.classList.add('zone-' + state);
        }
    }

    // --- Chart Overlay (Combined) ---
    _initChartOverlay() {
        let overlay = document.getElementById("tcChartOverlay");
        if (!overlay) return;

        overlay.addEventListener("click", (ev) => {
            if (ev.target === overlay) this._closeChartOverlay();
        });

        // Close button removed — user closes by clicking overlay background
    }

    _closeChartOverlay() {
        let overlay = document.getElementById("tcChartOverlay");
        overlay.classList.remove("show");
        if (this._activeChart) {
            this._activeChart.destroy();
            this._activeChart = null;
        }
        this._chartPanLoading = false;
        this._chartDataStart = null;
        this._chartDataEnd = null;
        this._chartCnlNums = null;
        this._chartMarkerDef = null;
        // Stop real-time chart updates
        this._stopChartRealtime();
        // Reset date range so next open starts fresh
        this._chartStartTime = null;
        this._chartEndTime = null;
        // Clean up date range picker outside click handler
        if (this._drOutsideClickHandler) {
            document.removeEventListener('click', this._drOutsideClickHandler);
            this._drOutsideClickHandler = null;
        }
    }

    // --- Real-time chart update methods ---
    _startChartRealtime() {
        this._stopChartRealtime();
        this._chartRealtimePaused = false;
        this._chartAtLiveEdge = true; // start following live data
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

    // Check if the chart viewport is at the rightmost (newest) data edge
    _checkIfAtLiveEdge(chart) {
        if (!chart || !chart.scales || !chart.scales.x) return false;
        let xScale = chart.scales.x;
        let visibleMax = xScale.max;
        // Find the maximum timestamp in the data
        let dataMax = 0;
        for (let ds of chart.data.datasets) {
            if (ds.data.length > 0) {
                let lastPoint = ds.data[ds.data.length - 1];
                let ts = lastPoint.x instanceof Date ? lastPoint.x.getTime() : lastPoint.x;
                if (ts > dataMax) dataMax = ts;
            }
        }
        if (dataMax === 0) return true;
        // Consider "at live edge" if visible right edge is within 2% of data range or 30s of data max
        let tolerance = Math.max((xScale.max - xScale.min) * 0.02, 30000);
        return visibleMax >= dataMax - tolerance;
    }

    async _tickChartRealtime() {
        if (!this._chartStartTime || !this._chartEndTime) return;
        let overlay = document.getElementById("tcChartOverlay");
        if (!overlay || !overlay.classList.contains("show")) return;
        if (!this._activeChart) return;

        // Extend the data window end to now, keep the original start
        let now = new Date();
        this._chartEndTime = now;
        this._chartDataEnd = now.getTime();

        // Fetch only the recent slice (last tick interval + buffer)
        let fetchStart = new Date(now.getTime() - this.refreshRate * 3);
        try {
            let def = (this._combinedMarkerIdx === -1) ? this._chartMarkerDef : this.markers[this._combinedMarkerIdx];
            if (!def) return;
            let cnlNums = def.channels.map(c => c.cnlNum);
            if (this._showStatusTimelines && def.statusChannels && def.statusChannels.length > 0) {
                cnlNums = cnlNums.concat(def.statusChannels.map(c => c.cnlNum));
            }
            let histData = await this._fetchHistDataRange(cnlNums, fetchStart, now);
            if (!histData || !histData.timestamps || histData.timestamps.length === 0) return;

            this._appendChartData(this._activeChart, histData);

            // If user is at the live edge, auto-scroll to follow new data
            if (this._chartAtLiveEdge) {
                let duration = this._chartStartTime ?
                    (now.getTime() - this._chartStartTime.getTime()) : 24 * 60 * 60 * 1000;
                // Shift the visible window to show the latest data
                let xScale = this._activeChart.scales.x;
                let visibleDuration = xScale.max - xScale.min;
                this._activeChart.options.scales.x.min = now.getTime() - visibleDuration;
                this._activeChart.options.scales.x.max = now.getTime();
                this._activeChart.update('none');
            }

            // Update both Start and End toolbar date display
            this._chartStartTime = new Date(now.getTime() - this._combinedDays * 24 * 60 * 60 * 1000);
            this._drTempStart = new Date(this._chartStartTime);
            this._drTempEnd = new Date(this._chartEndTime);

            let startVal = document.getElementById('mapDateRangeStartVal');
            if (startVal) startVal.textContent = this._fmtDateTimeFull(this._chartStartTime);
            let endVal = document.getElementById('mapDateRangeEndVal');
            if (endVal) endVal.textContent = this._fmtDateTimeFull(this._chartEndTime);

            // If date/time picker dropdown is open, refresh it live
            let dropdown = document.getElementById('mapDateRangeDropdown');
            if (dropdown && dropdown.classList.contains('open')) {
                if (this._drPickerMode === 'end') {
                    this._renderDateRangeDropdown(dropdown, this._drTempEnd);
                } else if (this._drPickerMode === 'start') {
                    this._renderDateRangeDropdown(dropdown, this._drTempStart);
                }
            }
        } catch (err) {
            console.error("Chart realtime update error:", err);
        }
    }

    // Append new data points to existing chart (no re-render)
    _appendChartData(chart, histData) {
        let newTimestamps = histData.timestamps.map(t => new Date(t.ms));
        let cnlToTrendIdx = {};
        for (let i = 0; i < histData.cnlNums.length; i++) {
            cnlToTrendIdx[histData.cnlNums[i]] = i;
        }

        for (let ds of chart.data.datasets) {
            let cnlNum = ds._cnlNum;
            let trendIdx = cnlToTrendIdx[cnlNum];
            if (trendIdx === undefined) continue;

            let trend = histData.trends[trendIdx];
            // Find the last existing timestamp to avoid duplicates
            let lastExistingTs = ds.data.length > 0 ?
                (ds.data[ds.data.length - 1].x instanceof Date ?
                    ds.data[ds.data.length - 1].x.getTime() :
                    ds.data[ds.data.length - 1].x) : 0;

            for (let idx = 0; idx < newTimestamps.length; idx++) {
                let ts = newTimestamps[idx];
                if (ts.getTime() <= lastExistingTs) continue; // skip duplicates

                let rec = trend[idx];
                let point;
                if (ds._isStatus) {
                    point = {
                        x: ts,
                        y: (rec && rec.d && rec.d.stat > 0 && rec.d.val >= 1) ? ds._bandTop : ds._bandBase
                    };
                } else {
                    point = {
                        x: ts,
                        y: (rec && rec.d && rec.d.stat > 0) ? rec.d.val : null
                    };
                }
                ds.data.push(point);
            }
        }

        chart.update('none');
    }

    // Public entry-point for ThermalCamera: accepts a marker definition object
    // ({name, channels, statusChannels}) without requiring it to be pre-stashed
    // in this.markers. The internal _openCombinedChart still uses a marker index
    // for backwards compat; markerIdx === -1 means "use this._chartMarkerDef".
    openCombinedChart(def, days) {
        if (!def) return;
        this._chartMarkerDef = def;
        return this._openCombinedChart(-1, days);
    }

    async _openCombinedChart(markerIdx, days) {
        let def = (markerIdx === -1) ? this._chartMarkerDef : this.markers[markerIdx];
        if (!def || !def.channels || def.channels.length === 0) return;

        // Keep _chartMarkerDef in sync so _tickChartRealtime can find the def
        // through the same -1 sentinel after a reset/reopen cycle.
        this._chartMarkerDef = def;
        this._combinedMarkerIdx = markerIdx;
        this._combinedDays = days || 1;

        let overlay = document.getElementById("tcChartOverlay");
        let title = document.getElementById("tcChartTitle");
        let body = document.getElementById("tcChartBody");
        let toolbar = document.getElementById("tcChartToolbar");

        title.textContent = def.name || this.t.combinedChart;

        // Initialize date range (default: 1 day back from now)
        if (!this._chartEndTime || !this._chartStartTime) {
            this._chartEndTime = new Date();
            this._chartStartTime = new Date(this._chartEndTime.getTime() - 24 * 60 * 60 * 1000);
        }

        // build toolbar with date range picker + action buttons
        toolbar.innerHTML = '';
        this._buildDateRangeBox(toolbar);

        // Reset zoom button
        let resetBtn = document.createElement('button');
        resetBtn.className = 'tc-chart-period-btn';
        resetBtn.id = 'tcChartResetZoom';
        resetBtn.style.marginLeft = '12px';
        resetBtn.textContent = '⟲ ' + this.t.reset;
        toolbar.appendChild(resetBtn);

        // Status toggle button (only if marker has status channels)
        if (def.statusChannels && def.statusChannels.length > 0) {
            let stBtn = document.createElement('button');
            stBtn.className = this._showStatusTimelines ? 'tc-chart-period-btn active' : 'tc-chart-period-btn';
            stBtn.id = 'tcChartStatusToggle';
            stBtn.style.marginLeft = '12px';
            stBtn.textContent = this.t.statusesLabel;
            toolbar.appendChild(stBtn);
        }

        // wire reset — resume real-time updates
        document.getElementById("tcChartResetZoom").addEventListener('click', () => {
            this._chartRealtimePaused = false;
            this._chartAtLiveEdge = true;
            this._chartStartTime = null;
            this._chartEndTime = null;
            this._openCombinedChart(this._combinedMarkerIdx, this._combinedDays);
        });

        // wire status toggle
        let statusToggleBtn = document.getElementById("tcChartStatusToggle");
        if (statusToggleBtn) {
            statusToggleBtn.addEventListener('click', () => {
                this._showStatusTimelines = !this._showStatusTimelines;
                this._openCombinedChart(this._combinedMarkerIdx, this._combinedDays);
            });
        }

        body.innerHTML = '<div class="tc-chart-loading">' + this._escapeHtml(this.t.loadingChart) + '</div>';
        overlay.classList.add("show");

        try {
            await this._loadChartJs();
            let cnlNums = def.channels.map(c => c.cnlNum);
            if (this._showStatusTimelines && def.statusChannels && def.statusChannels.length > 0) {
                cnlNums = cnlNums.concat(def.statusChannels.map(c => c.cnlNum));
            }
            let histData = await this._fetchHistDataRange(cnlNums, this._chartStartTime, this._chartEndTime);

            if (!histData) {
                body.innerHTML = '<div class="tc-chart-loading">' + this._escapeHtml(this.t.noData) + '</div>';
                return;
            }

            body.innerHTML = '<div class="tc-chart-canvas-wrap"><canvas id="mapCombinedCanvas"></canvas></div>';
            let canvas = document.getElementById("mapCombinedCanvas");
            this._renderCombinedChart(canvas, def, histData);

            // Add status legend above the canvas (like parameter names in a line below)
            if (this._statusLegendItems && this._statusLegendItems.length > 0) {
                let legendHtml = '<div class="tc-chart-status-legend">';
                for (let item of this._statusLegendItems) {
                    legendHtml += '<span class="tc-chart-status-legend-item">' +
                        '<svg width="30" height="3" style="vertical-align:middle"><line x1="0" y1="1.5" x2="30" y2="1.5" stroke="' + item.color + '" stroke-width="3"/></svg> ' +
                        this._escapeHtml(item.alias) + '</span>';
                }
                legendHtml += '</div>';
                let canvasWrap = body.querySelector('.tc-chart-canvas-wrap');
                canvasWrap.insertAdjacentHTML('beforebegin', legendHtml);
            }
            // Start real-time updates if not paused
            if (!this._chartRealtimePaused) {
                this._startChartRealtime();
            }
        } catch (err) {
            console.error("Chart loading error:", err);
            body.innerHTML = '<div class="tc-chart-loading">' + this._escapeHtml(this.t.errorChart) + '</div>';
        }
    }

    // --- Date Range Picker ---
    _fmtDateTimeFull(d) {
        let dd = String(d.getDate()).padStart(2, '0');
        let mm = String(d.getMonth() + 1).padStart(2, '0');
        let yy = d.getFullYear();
        let hh = String(d.getHours()).padStart(2, '0');
        let mi = String(d.getMinutes()).padStart(2, '0');
        return dd + '.' + mm + '.' + yy + ' ' + hh + ':' + mi;
    }

    _buildDateRangeBox(toolbar) {
        let wrapper = document.createElement('div');
        wrapper.className = 'tc-daterange-wrapper';

        let box = document.createElement('div');
        box.className = 'tc-daterange-box';
        box.id = 'mapDateRangeBox';

        // Start zone
        let startZone = document.createElement('div');
        startZone.className = 'tc-daterange-zone';
        startZone.id = 'mapDateRangeStartZone';
        startZone.innerHTML =
            '<span class="tc-daterange-zone-label">' + this._escapeHtml(this.t.start) + '</span>' +
            '<span class="tc-daterange-zone-value" id="mapDateRangeStartVal">' +
            this._escapeHtml(this._fmtDateTimeFull(this._chartStartTime)) + '</span>';

        // Separator
        let sep = document.createElement('div');
        sep.className = 'tc-daterange-separator';

        // End zone
        let endZone = document.createElement('div');
        endZone.className = 'tc-daterange-zone';
        endZone.id = 'mapDateRangeEndZone';
        endZone.innerHTML =
            '<span class="tc-daterange-zone-label">' + this._escapeHtml(this.t.end) + '</span>' +
            '<span class="tc-daterange-zone-value" id="mapDateRangeEndVal">' +
            this._escapeHtml(this._fmtDateTimeFull(this._chartEndTime)) + '</span>';

        box.appendChild(startZone);
        box.appendChild(sep);
        box.appendChild(endZone);
        wrapper.appendChild(box);

        // Dropdown
        let dropdown = document.createElement('div');
        dropdown.className = 'tc-daterange-dropdown';
        dropdown.id = 'mapDateRangeDropdown';
        wrapper.appendChild(dropdown);

        toolbar.appendChild(wrapper);

        // State for picker
        this._drPickerMode = null; // 'start' or 'end'
        this._drTempStart = new Date(this._chartStartTime);
        this._drTempEnd = new Date(this._chartEndTime);

        // Click handlers for zones
        let self = this;
        startZone.addEventListener('click', (e) => {
            e.stopPropagation();
            self._openDateRangeDropdown('start');
        });
        endZone.addEventListener('click', (e) => {
            e.stopPropagation();
            self._openDateRangeDropdown('end');
        });

        // Close dropdown on outside click
        this._drOutsideClickHandler = (e) => {
            let dd = document.getElementById('mapDateRangeDropdown');
            let bx = document.getElementById('mapDateRangeBox');
            if (dd && !dd.contains(e.target) && bx && !bx.contains(e.target)) {
                dd.classList.remove('open');
                this._drPickerMode = null;
                if (this._timeIndicatorInterval) {
                    clearInterval(this._timeIndicatorInterval);
                    this._timeIndicatorInterval = null;
                }
                let sz = document.getElementById('mapDateRangeStartZone');
                let ez = document.getElementById('mapDateRangeEndZone');
                if (sz) sz.classList.remove('active');
                if (ez) ez.classList.remove('active');
            }
        };
        document.addEventListener('click', this._drOutsideClickHandler);
    }

    _openDateRangeDropdown(mode) {
        // Pause real-time updates when user manually picks date
        this._chartRealtimePaused = true;
        this._drPickerMode = mode;
        let dropdown = document.getElementById('mapDateRangeDropdown');
        let box = document.getElementById('mapDateRangeBox');

        // Match dropdown width to box width
        dropdown.style.width = box.offsetWidth + 'px';
        dropdown.style.minWidth = '300px';

        // Highlight active zone
        let sz = document.getElementById('mapDateRangeStartZone');
        let ez = document.getElementById('mapDateRangeEndZone');
        sz.classList.toggle('active', mode === 'start');
        ez.classList.toggle('active', mode === 'end');

        let currentDate = mode === 'start' ? this._drTempStart : this._drTempEnd;
        this._drViewMonth = currentDate.getMonth();
        this._drViewYear = currentDate.getFullYear();

        this._renderDateRangeDropdown(dropdown, currentDate);
        dropdown.classList.add('open');
    }

    _renderDateRangeDropdown(dropdown, currentDate) {
        // Clean up previous time indicator interval
        if (this._timeIndicatorInterval) {
            clearInterval(this._timeIndicatorInterval);
            this._timeIndicatorInterval = null;
        }
        dropdown.innerHTML = '';
        let mode = this._drPickerMode;
        let self = this;

        // Section label with "Now" button on the right
        let label = document.createElement('div');
        label.className = 'tc-daterange-section-label';
        let labelText = document.createElement('span');
        labelText.textContent = mode === 'start' ? this.t.startDateTime : this.t.endDateTime;
        label.appendChild(labelText);

        let nowBtn = document.createElement('button');
        nowBtn.className = 'tc-daterange-now-btn';
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

        // Time display
        let timeDisplay = document.createElement('div');
        timeDisplay.className = 'tc-time-display';
        timeDisplay.id = 'mapTimeDisplay';
        let hh = currentDate.getHours();
        let mm = currentDate.getMinutes();
        timeDisplay.textContent = String(hh).padStart(2, '0') + ':' + String(mm).padStart(2, '0');
        dropdown.appendChild(timeDisplay);

        // Time scrollbar
        let scrollbar = this._buildTimeScrollbar(currentDate);
        dropdown.appendChild(scrollbar);

        // Calendar
        let calendar = this._buildCalendar(currentDate);
        dropdown.appendChild(calendar);

        // Apply button
        let applyBtn = document.createElement('button');
        applyBtn.className = 'tc-daterange-apply';
        applyBtn.textContent = this.t.apply;
        applyBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            self._applyDateRange();
        });
        dropdown.appendChild(applyBtn);
    }

    _buildTimeScrollbar(currentDate) {
        // Outer wrapper (non-scrolling) holds overlay elements
        let wrapper = document.createElement('div');
        wrapper.className = 'tc-time-scrollbar-wrapper';

        // Scrollable container
        let container = document.createElement('div');
        container.className = 'tc-time-scrollbar';

        // Track: 24 hours * 60 min = 1440 minutes, 3px per minute = 4320px wide
        let pxPerMin = 3;
        let totalWidth = 1440 * pxPerMin;

        let track = document.createElement('div');
        track.className = 'tc-time-scrollbar-track';
        track.style.width = totalWidth + 'px';

        // Draw tick marks
        for (let h = 0; h < 24; h++) {
            for (let m = 0; m < 60; m++) {
                let totalMin = h * 60 + m;
                let x = totalMin * pxPerMin;

                let tick = document.createElement('div');
                tick.className = 'tc-time-tick';
                tick.style.left = x + 'px';

                if (m === 0) {
                    // Hour tick — tall
                    tick.classList.add('tc-time-tick-hour');
                    tick.style.height = '26px';

                    let lbl = document.createElement('div');
                    lbl.className = 'tc-time-tick-label';
                    lbl.style.left = x + 'px';
                    lbl.textContent = String(h).padStart(2, '0');
                    track.appendChild(lbl);
                } else if (m % 15 === 0) {
                    // Quarter-hour tick — medium
                    tick.style.height = '16px';
                } else if (m % 5 === 0) {
                    // 5-minute tick
                    tick.style.height = '10px';
                } else {
                    // Minute tick — small
                    tick.style.height = '5px';
                }

                track.appendChild(tick);
            }
        }

        // Current time indicator on the track (solid red)
        let now = new Date();
        let nowMin = now.getHours() * 60 + now.getMinutes();
        let indicator = document.createElement('div');
        indicator.className = 'tc-time-indicator';
        indicator.style.left = (nowMin * pxPerMin) + 'px';
        track.appendChild(indicator);

        container.appendChild(track);
        wrapper.appendChild(container);

        // Selection indicator (transparent red, fixed at center of wrapper)
        let selIndicator = document.createElement('div');
        selIndicator.className = 'tc-time-indicator-selection';
        wrapper.appendChild(selIndicator);

        // Edge arrows on wrapper — show when current time is off-screen
        let arrowLeft = document.createElement('div');
        arrowLeft.className = 'tc-time-edge-arrow tc-time-edge-arrow-left';
        arrowLeft.style.display = 'none';
        wrapper.appendChild(arrowLeft);

        let arrowRight = document.createElement('div');
        arrowRight.className = 'tc-time-edge-arrow tc-time-edge-arrow-right';
        arrowRight.style.display = 'none';
        wrapper.appendChild(arrowRight);

        // Initial scroll position — center on the selected time
        let currentMin = currentDate.getHours() * 60 + currentDate.getMinutes();
        let self = this;

        // Update edge arrows visibility
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

        // Use setTimeout to ensure container is laid out and has correct offsetWidth
        setTimeout(() => {
            let centerOffset = container.clientWidth / 2;
            container.scrollLeft = currentMin * pxPerMin - centerOffset;
            updateEdgeArrows();
        }, 50);

        // Update current time indicator every 10 seconds
        this._timeIndicatorInterval = setInterval(() => {
            let n = new Date();
            let nMin = n.getHours() * 60 + n.getMinutes();
            indicator.style.left = (nMin * pxPerMin) + 'px';
            updateEdgeArrows();
        }, 10000);

        // Drag to scroll + click to nudge ±1 min
        let isDragging = false;
        let didDrag = false;
        let startX = 0;
        let startScroll = 0;

        container.addEventListener('mousedown', (e) => {
            isDragging = true;
            didDrag = false;
            startX = e.clientX;
            startScroll = container.scrollLeft;
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
                // Click without drag — nudge ±1 minute
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

        // Scroll wheel
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
        let touchStartX = 0;
        let touchStartScroll = 0;
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

        let display = document.getElementById('mapTimeDisplay');
        if (display) {
            display.textContent = String(h).padStart(2, '0') + ':' + String(m).padStart(2, '0');
        }

        // Update temp date
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

    _buildCalendar(currentDate) {
        let container = document.createElement('div');
        container.className = 'map-calendar';
        container.id = 'mapCalendarContainer';

        let self = this;
        let year = this._drViewYear;
        let month = this._drViewMonth;
        let selectedDay = currentDate.getDate();
        let selectedMonth = currentDate.getMonth();
        let selectedYear = currentDate.getFullYear();

        // Header with nav
        let header = document.createElement('div');
        header.className = 'tc-calendar-header';

        let prevBtn = document.createElement('button');
        prevBtn.className = 'tc-calendar-nav';
        prevBtn.textContent = '<';
        prevBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            self._drViewMonth--;
            if (self._drViewMonth < 0) { self._drViewMonth = 11; self._drViewYear--; }
            self._refreshCalendar(currentDate);
        });

        let nextBtn = document.createElement('button');
        nextBtn.className = 'tc-calendar-nav';
        nextBtn.textContent = '>';
        nextBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            self._drViewMonth++;
            if (self._drViewMonth > 11) { self._drViewMonth = 0; self._drViewYear++; }
            self._refreshCalendar(currentDate);
        });

        let monthNames = ['Январь', 'Февраль', 'Март', 'Апрель', 'Май', 'Июнь',
            'Июль', 'Август', 'Сентябрь', 'Октябрь', 'Ноябрь', 'Декабрь'];
        let monthLabel = document.createElement('span');
        monthLabel.className = 'tc-calendar-month';
        monthLabel.textContent = monthNames[month] + ' ' + year;

        header.appendChild(prevBtn);
        header.appendChild(monthLabel);
        header.appendChild(nextBtn);
        container.appendChild(header);

        // Weekday labels
        let weekdays = document.createElement('div');
        weekdays.className = 'tc-calendar-weekdays';
        let dayNames = ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс'];
        for (let dn of dayNames) {
            let d = document.createElement('div');
            d.textContent = dn;
            weekdays.appendChild(d);
        }
        container.appendChild(weekdays);

        // Day grid
        let grid = document.createElement('div');
        grid.className = 'tc-calendar-grid';

        let firstDay = new Date(year, month, 1).getDay(); // 0=Sun
        let startOffset = (firstDay + 6) % 7; // shift to Mon=0
        let daysInMonth = new Date(year, month + 1, 0).getDate();
        let today = new Date();

        // Empty cells before first day
        for (let i = 0; i < startOffset; i++) {
            let empty = document.createElement('div');
            empty.className = 'tc-calendar-day empty';
            grid.appendChild(empty);
        }

        for (let d = 1; d <= daysInMonth; d++) {
            let cell = document.createElement('div');
            cell.className = 'tc-calendar-day';
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
                // Update selected highlight
                grid.querySelectorAll('.tc-calendar-day.selected').forEach(c => c.classList.remove('selected'));
                cell.classList.add('selected');
            });

            grid.appendChild(cell);
        }

        container.appendChild(grid);
        return container;
    }

    _refreshCalendar(currentDate) {
        let container = document.getElementById('mapCalendarContainer');
        if (!container) return;
        let parent = container.parentNode;
        let newCal = this._buildCalendar(currentDate);
        parent.replaceChild(newCal, container);
    }

    _applyDateRange() {
        // Validate start < end
        if (this._drTempStart >= this._drTempEnd) {
            let tmp = new Date(this._drTempStart);
            this._drTempStart = new Date(this._drTempEnd);
            this._drTempEnd = tmp;
        }

        this._chartStartTime = new Date(this._drTempStart);
        this._chartEndTime = new Date(this._drTempEnd);

        // Pause real-time updates — user selected a fixed range
        this._chartRealtimePaused = true;
        this._chartAtLiveEdge = false;

        // Compute _combinedDays for pan-to-load compatibility
        this._combinedDays = (this._chartEndTime - this._chartStartTime) / (24 * 60 * 60 * 1000);

        // Update display
        let startVal = document.getElementById('mapDateRangeStartVal');
        let endVal = document.getElementById('mapDateRangeEndVal');
        if (startVal) startVal.textContent = this._fmtDateTimeFull(this._chartStartTime);
        if (endVal) endVal.textContent = this._fmtDateTimeFull(this._chartEndTime);

        // Close dropdown
        let dropdown = document.getElementById('mapDateRangeDropdown');
        if (dropdown) dropdown.classList.remove('open');
        this._drPickerMode = null;
        let sz = document.getElementById('mapDateRangeStartZone');
        let ez = document.getElementById('mapDateRangeEndZone');
        if (sz) sz.classList.remove('active');
        if (ez) ez.classList.remove('active');

        // Reload chart
        this._openCombinedChart(this._combinedMarkerIdx, this._combinedDays);
    }

    _renderCombinedTooltip(ctx) {
        let tooltipModel = ctx.tooltip;
        let chartArea = ctx.chart.chartArea;
        let canvasEl = ctx.chart.canvas;
        let wrap = canvasEl.parentElement;

        // Get or create tooltip element
        let el = wrap.querySelector('.tc-chart-tooltip');
        if (!el) {
            el = document.createElement('div');
            el.className = 'tc-chart-tooltip';
            wrap.appendChild(el);
        }

        // Hide if no tooltip
        if (tooltipModel.opacity === 0) {
            el.style.opacity = '0';
            return;
        }

        // Build title (full date + time)
        let titleHtml = '';
        if (tooltipModel.dataPoints && tooltipModel.dataPoints.length > 0) {
            let ts = new Date(tooltipModel.dataPoints[0].parsed.x);
            let dd = String(ts.getDate()).padStart(2, '0');
            let mm = String(ts.getMonth() + 1).padStart(2, '0');
            let yy = ts.getFullYear();
            let hh = String(ts.getHours()).padStart(2, '0');
            let mi = String(ts.getMinutes()).padStart(2, '0');
            titleHtml = '<div class="tc-chart-tooltip-title">' +
                dd + '.' + mm + '.' + yy + '&nbsp;&nbsp;' + hh + ':' + mi + '</div>';
        }

        // Build rows
        let rowsHtml = '';
        for (let dp of tooltipModel.dataPoints) {
            let ds = ctx.chart.data.datasets[dp.datasetIndex];
            let color = ds.borderColor || '#333';

            if (ds._isStatus) {
                let active = dp.parsed.y > ds._bandBase;
                rowsHtml += '<div class="tc-chart-tooltip-row">' +
                    '<span class="tc-chart-tooltip-dot" style="background:' + color + ';border-radius:2px"></span>' +
                    '<span class="tc-chart-tooltip-label">' + this._escapeHtml(ds.label) + '</span>' +
                    '<span class="tc-chart-tooltip-status ' + (active ? 'on' : 'off') + '">' +
                    (active ? 'Вкл' : 'Выкл') + '</span></div>';
            } else {
                let val = dp.parsed.y;
                if (val === null || val === undefined) continue;
                let valStr = typeof val === 'number' ? val.toFixed(2) : String(val);
                let unit = ds._unit || '';
                rowsHtml += '<div class="tc-chart-tooltip-row">' +
                    '<span class="tc-chart-tooltip-dot" style="background:' + color + '"></span>' +
                    '<span class="tc-chart-tooltip-label">' + this._escapeHtml(ds.label) + '</span>' +
                    '<span class="tc-chart-tooltip-value">' + valStr +
                    '<span class="tc-chart-tooltip-unit">' + this._escapeHtml(unit) + '</span></span></div>';
            }
        }

        el.innerHTML = titleHtml + rowsHtml;
        el.style.opacity = '1';

        // Position: prefer right side of cursor, flip to left if not enough space
        let caretX = tooltipModel.caretX;
        let caretY = tooltipModel.caretY;
        let tooltipWidth = el.offsetWidth;
        let tooltipHeight = el.offsetHeight;
        let wrapWidth = wrap.offsetWidth;

        let left = caretX + 16;
        if (left + tooltipWidth > wrapWidth - 10) {
            left = caretX - tooltipWidth - 16;
        }
        let top = caretY - tooltipHeight / 2;
        top = Math.max(0, Math.min(top, wrap.offsetHeight - tooltipHeight));

        el.style.left = left + 'px';
        el.style.top = top + 'px';
    }

    _renderCombinedChart(canvas, def, histData) {
        if (this._activeChart) {
            this._activeChart.destroy();
            this._activeChart = null;
        }

        let isTriangle = def.type === 'triangle';
        let timestamps = histData.timestamps.map(t => new Date(t.ms));
        let datasets = [];

        // Track loaded data boundaries for pan-to-load
        this._chartDataStart = timestamps.length > 0 ? timestamps[0].getTime() : Date.now();
        this._chartDataEnd = timestamps.length > 0 ? timestamps[timestamps.length - 1].getTime() : Date.now();

        let cnlToTrendIdx = {};
        for (let i = 0; i < histData.cnlNums.length; i++) {
            cnlToTrendIdx[histData.cnlNums[i]] = i;
        }

        // For triangle markers: channels 0-1 = temp lines, 2-3 = flood status bands, 4-5 = battery/online (not charted)
        // Order: T700, T200, Flood700, Flood200
        // For circle markers: all channels are lines, status channels are separate bands
        let chartChannels = isTriangle
            ? [def.channels[1], def.channels[0]].filter(Boolean)
            : def.channels;
        let allCnlNums = chartChannels.map(c => c.cnlNum);

        if (isTriangle) {
            // Add flood channels (3=700mm first, 2=200mm second)
            if (def.channels[3]) allCnlNums.push(def.channels[3].cnlNum);
            if (def.channels[2]) allCnlNums.push(def.channels[2].cnlNum);
        } else if (this._showStatusTimelines && def.statusChannels && def.statusChannels.length > 0) {
            allCnlNums = allCnlNums.concat(def.statusChannels.map(c => c.cnlNum));
        }
        this._chartCnlNums = allCnlNums;
        this._chartMarkerDef = def;

        // Temperature/pressure line datasets
        let TRI_LINE_COLORS = ['#d32f2f', '#f0ad4e']; // 700mm=red, 200mm=yellow
        for (let i = 0; i < chartChannels.length; i++) {
            let ch = chartChannels[i];
            let alias = ch.alias || (this.t.channel + ch.cnlNum);
            let color = isTriangle ? TRI_LINE_COLORS[i] : this._getChannelColor(alias, i);
            let trendIdx = cnlToTrendIdx[ch.cnlNum];
            if (trendIdx === undefined) continue;

            let trend = histData.trends[trendIdx];
            let data = trend.map((rec, idx) => ({
                x: timestamps[idx],
                y: (rec && rec.d && rec.d.stat > 0) ? rec.d.val : null
            }));

            let axisGroup = isTriangle ? "temp" : this._getAxisGroup(alias);
            let unit = axisGroup === "pressure" ? this.t.pressureUnit : "°C";
            datasets.push({
                label: alias,
                data: data,
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

        // Status timeline bands
        // Circle severity order: 0=amber (deviation), 1=orange (malfunction), 2=red (emergency), 3=light orange (diagnostic)
        let STATUS_COLORS = ["#f0ad4e", "#f57c00", "#d32f2f", "#ff9800", "#7b1fa2"];
        this._statusLegendItems = [];

        if (isTriangle) {
            // Triangle: flood channels as status bands (700mm on top, 200mm on bottom)
            let floodChannels = [];
            if (def.channels[3]) floodChannels.push(def.channels[3]);
            if (def.channels[2]) floodChannels.push(def.channels[2]);
            let TRI_STATUS_COLORS = ['#ff5252', '#ffcc02']; // 700mm=bright red, 200mm=bright yellow
            let yMin = -10, yMax = 120, yRange = yMax - yMin;
            let bandHeight = yRange / Math.max(1, floodChannels.length);
            for (let i = 0; i < floodChannels.length; i++) {
                let sch = floodChannels[i];
                let alias = sch.alias || (this.t.channel + sch.cnlNum);
                let color = TRI_STATUS_COLORS[i % TRI_STATUS_COLORS.length];
                let trendIdx = cnlToTrendIdx[sch.cnlNum];
                if (trendIdx === undefined) continue;

                let bandTop = yMax - i * bandHeight;
                let bandBase = bandTop - bandHeight;
                let trend = histData.trends[trendIdx];
                // For flood: val===0 means flood active (show band)
                let data = trend.map((rec, idx) => ({
                    x: timestamps[idx],
                    y: (rec && rec.d && rec.d.stat > 0 && rec.d.val === 0) ? bandTop : bandBase
                }));

                datasets.push({
                    label: alias,
                    data: data,
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
                this._statusLegendItems.push({ alias: alias, color: color });
            }
        } else if (this._showStatusTimelines && def.statusChannels && def.statusChannels.length > 0) {
            // Circle: standard status bands
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
                this._statusLegendItems.push({ alias: alias, color: color });
            }
        }

        let self = this;
        // Determine visible time range to pick appropriate axis format
        let rangeDurationMs = this._chartEndTime.getTime() - this._chartStartTime.getTime();
        let isShortRange = rangeDurationMs <= 24 * 60 * 60 * 1000; // <= 1 day

        let xTimeFormats = isShortRange ? {
            millisecond: 'HH:mm',
            second: 'HH:mm',
            minute: 'HH:mm',
            hour: 'HH:mm',
            day: 'dd.MM',
            week: 'dd.MM',
            month: 'MM.yyyy'
        } : {
            millisecond: 'dd.MM HH:mm',
            second: 'dd.MM HH:mm',
            minute: 'dd.MM HH:mm',
            hour: 'dd.MM HH:mm',
            day: 'dd.MM',
            week: 'dd.MM',
            month: 'MM.yyyy'
        };

        let xTickCallback = function(value, index, ticksArr) {
            let ts = new Date(ticksArr[index].value);
            // First tick always shows date + time
            if (index === 0) return self._fmtDate(ts) + ' ' + self._fmtTime(ts);
            // Show date again only when the day changes from previous tick
            let prevTs = new Date(ticksArr[index - 1].value);
            if (ts.getDate() !== prevTs.getDate() || ts.getMonth() !== prevTs.getMonth()) {
                return self._fmtDate(ts) + ' ' + self._fmtTime(ts);
            }
            return self._fmtTime(ts);
        };

        // Build scales: triangle uses -10..120 temp only, circle uses 0..120 temp + pressure
        let scales = {
            x: {
                type: 'time',
                time: {
                    displayFormats: xTimeFormats,
                    major: { enabled: true }
                },
                title: { display: false },
                ticks: {
                    font: { size: 14 },
                    callback: xTickCallback,
                    maxRotation: 0,
                    autoSkipPadding: 20,
                    major: { enabled: true }
                }
            },
            yTemp: {
                type: 'linear',
                position: 'left',
                min: isTriangle ? -10 : 0,
                max: 120,
                title: { display: true, text: '°C', font: { size: 15 } },
                ticks: { font: { size: 14 } }
            }
        };

        if (!isTriangle) {
            scales.yPressure = {
                type: 'linear',
                position: 'right',
                min: 0,
                max: 25,
                title: { display: true, text: TcChartManager.LANG.pressureUnit, font: { size: 15 } },
                ticks: { font: { size: 14 } },
                grid: { drawOnChartArea: false }
            };
        }

        this._activeChart = new Chart(canvas, {
            type: 'line',
            data: { datasets: datasets },
            plugins: [TcChartManager._crosshairPlugin],
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: {
                        position: 'top',
                        labels: {
                            usePointStyle: true, pointStyle: 'line', boxWidth: 30, font: { size: 15 },
                            filter: function(item, chartData) {
                                let ds = chartData.datasets[item.datasetIndex];
                                return !ds._isStatus;
                            }
                        }
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
                            x: { minRange: 60000 }  // minimum 1 minute range
                        }
                    }
                },
                scales: scales
            }
        });
    }

    // --- Pan-to-load: fetch older data when user pans to left edge ---
    async _onChartPanComplete(chart) {
        if (this._chartPanLoading || !this._chartCnlNums) return;

        let xScale = chart.scales.x;
        let visibleMin = xScale.min; // left edge of visible area (ms)
        let loadedRange = this._chartDataEnd - this._chartDataStart;
        // trigger when visible left edge is within 20% of loaded data start
        let threshold = this._chartDataStart + loadedRange * 0.2;

        if (visibleMin > threshold) return; // not near left edge

        this._chartPanLoading = true;

        // Show loading indicator in toolbar
        let toolbar = document.getElementById("tcChartToolbar");
        let loadingEl = document.createElement("div");
        loadingEl.className = "tc-chart-pan-loading";
        loadingEl.textContent = this._escapeHtml(this.t.loading);
        loadingEl.animate([{ opacity: 1 }, { opacity: 0.6 }, { opacity: 1 }],
            { duration: 800, iterations: Infinity });
        toolbar.appendChild(loadingEl);

        try {
            // Fetch one more period to the left
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
            let el = document.getElementById("tcChartToolbar").querySelector(".tc-chart-pan-loading");
            if (el) el.remove();
        }
    }

    _prependChartData(chart, histData) {
        let newTimestamps = histData.timestamps.map(t => new Date(t.ms));

        let cnlToTrendIdx = {};
        for (let i = 0; i < histData.cnlNums.length; i++) {
            cnlToTrendIdx[histData.cnlNums[i]] = i;
        }

        for (let ds of chart.data.datasets) {
            let cnlNum = ds._cnlNum;
            let trendIdx = cnlToTrendIdx[cnlNum];
            if (trendIdx === undefined) continue;

            let trend = histData.trends[trendIdx];
            let newPoints;
            if (ds._isStatus) {
                let bandBase = ds._bandBase;
                let bandTop = ds._bandTop;
                newPoints = trend.map((rec, idx) => ({
                    x: newTimestamps[idx],
                    y: (rec && rec.d && rec.d.stat > 0 && rec.d.val >= 1) ? bandTop : bandBase
                }));
            } else {
                newPoints = trend.map((rec, idx) => ({
                    x: newTimestamps[idx],
                    y: (rec && rec.d && rec.d.stat > 0) ? rec.d.val : null
                }));
            }

            // Prepend new data (older) before existing data, skip duplicates
            let existingFirstX = ds.data.length > 0 ? ds.data[0].x.getTime() : Infinity;
            let filtered = newPoints.filter(p => p.x.getTime() < existingFirstX);
            ds.data = filtered.concat(ds.data);
        }

        chart.update('none'); // update without animation for smooth experience
    }

    // --- Mini Chart Tooltip (hover per parameter, 1h range) ---
    _initMiniChartTooltip() {
        let tip = document.getElementById("tcMiniChartTooltip");
        if (!tip) return;

        tip.addEventListener("mouseenter", () => {
            clearTimeout(this._miniChartHideTimer);
        });
        tip.addEventListener("mouseleave", () => {
            this._hideMiniChart();
        });
    }

    async _showMiniChart(event, cnlNum, alias, color, pos) {
        clearTimeout(this._miniChartHideTimer);

        let isStatus = pos === 'above';
        let tip = document.getElementById("tcMiniChartTooltip");
        let titleEl = document.getElementById("tcMiniChartTitle");
        let bodyEl = document.getElementById("tcMiniChartBody");

        let titleText = alias + " (" + this.t.channel + " " + cnlNum + ")";
        titleEl.textContent = titleText;
        bodyEl.innerHTML = '<div class="tc-minichart-loading">' + this._escapeHtml(this.t.loading) + '</div>';

        // Dynamic width based on title length (14px font ~8px per char)
        let tipWidth = Math.max(340, Math.min(520, titleText.length * 8 + 28));
        tip.style.width = tipWidth + "px";

        let popup = event.target.closest('.leaflet-popup');
        let contentWrap = popup ? popup.querySelector('.leaflet-popup-content-wrapper') : null;
        let popupRect = (contentWrap || popup || event.target).getBoundingClientRect();
        let tipHeight = 210;
        let left, top;

        // Show tooltip briefly to measure actual height
        tip.style.visibility = 'hidden';
        tip.classList.add("show");
        let actualTipHeight = tip.offsetHeight || tipHeight;
        tip.classList.remove("show");
        tip.style.visibility = '';

        if (isStatus) {
            // Status charts: above popup, same 6px gap
            left = popupRect.left + popupRect.width / 2 - tipWidth / 2;
            top = popupRect.top - actualTipHeight - 6;
            if (left < 8) left = 8;
            if (left + tipWidth > window.innerWidth - 8) left = window.innerWidth - tipWidth - 8;
            if (top < 8) top = popupRect.bottom + 6;
        } else {
            // Parameter charts: right of popup, fallback left
            left = popupRect.right + 6;
            top = popupRect.top + (popupRect.height / 2) - tipHeight / 2;
            if (left + tipWidth > window.innerWidth - 8) left = popupRect.left - tipWidth - 6;
            if (top < 8) top = 8;
            if (top + tipHeight > window.innerHeight - 8) top = window.innerHeight - tipHeight - 8;
        }

        tip.style.left = left + "px";
        tip.style.top = top + "px";
        tip.classList.add("show");

        try {
            await this._loadChartJs();
            // fetch 1 hour for parameters, 1 day for statuses
            let histData = await this._fetchHistData([cnlNum], isStatus ? 1 : 1 / 24);

            if (!histData || !histData.trends || histData.trends.length === 0) {
                bodyEl.innerHTML = '<div class="tc-minichart-loading">' + this._escapeHtml(this.t.noData) + '</div>';
                return;
            }

            if (!tip.classList.contains("show")) return;

            let wrapCls = "tc-minichart-canvas-wrap" + (isStatus ? " status-chart" : "");
            bodyEl.innerHTML = '<div class="' + wrapCls + '"><canvas id="mapMiniCanvas"></canvas></div>';
            let canvas = document.getElementById("mapMiniCanvas");
            this._renderMiniChart(canvas, cnlNum, alias, color, histData, isStatus);

            // Reposition status chart after content renders so it sits above popup
            if (isStatus) {
                let finalHeight = tip.offsetHeight;
                tip.style.top = (popupRect.top - finalHeight - 6) + "px";
                if (parseFloat(tip.style.top) < 8) {
                    tip.style.top = (popupRect.bottom + 6) + "px";
                }
            }
        } catch (err) {
            console.error("Mini chart error:", err);
            bodyEl.innerHTML = '<div class="tc-minichart-loading">' + this._escapeHtml(this.t.error) + '</div>';
        }
    }

    _miniChartExternalTooltip(context, isStatus) {
        let tooltipModel = context.tooltip;
        let el = document.getElementById("mapMiniChartExtTooltip");
        if (!el) {
            el = document.createElement("div");
            el.id = "mapMiniChartExtTooltip";
            el.className = "tc-minichart-ext-tooltip";
            document.body.appendChild(el);
        }

        if (tooltipModel.opacity === 0) {
            el.style.display = "none";
            return;
        }

        // Build content
        let title = '';
        if (tooltipModel.title && tooltipModel.title.length > 0) {
            let ts = new Date(tooltipModel.dataPoints[0].parsed.x);
            title = this._fmtDateTime(ts);
        }
        let bodyLines = [];
        if (tooltipModel.body) {
            for (let item of tooltipModel.body) {
                bodyLines = bodyLines.concat(item.lines);
            }
        }
        let html = '<div class="tc-minichart-ext-tooltip-title">' + this._escapeHtml(title) + '</div>';
        for (let line of bodyLines) {
            html += '<div class="tc-minichart-ext-tooltip-body">' + this._escapeHtml(line) + '</div>';
        }
        el.innerHTML = html;
        el.style.display = "block";

        // Position above the mini chart tooltip popup
        let miniTip = document.getElementById("tcMiniChartTooltip");
        let miniRect = miniTip.getBoundingClientRect();
        let elWidth = el.offsetWidth;
        let left = miniRect.left + miniRect.width / 2 - elWidth / 2;
        if (left < 8) left = 8;
        if (left + elWidth > window.innerWidth - 8) left = window.innerWidth - elWidth - 8;
        let top = miniRect.top - el.offsetHeight - 6;
        if (top < 8) top = miniRect.bottom + 6;

        el.style.left = left + "px";
        el.style.top = top + "px";
    }

    _scheduleMiniChartHide() {
        this._miniChartHideTimer = setTimeout(() => this._hideMiniChart(), 300);
    }

    _hideMiniChart() {
        let tip = document.getElementById("tcMiniChartTooltip");
        tip.classList.remove("show");
        let extTip = document.getElementById("mapMiniChartExtTooltip");
        if (extTip) extTip.style.display = "none";
        if (this._activeMiniChart) {
            this._activeMiniChart.destroy();
            this._activeMiniChart = null;
        }
    }

    // --- Extra Channels Popup (secondary "more" popup) ---
    _toggleExtraPopup(event, markerIdx) {
        event.stopPropagation();
        let popup = document.getElementById("tcExtraPopup");
        let def = this.markers[markerIdx];

        // If already showing for same marker, hide it
        if (popup.classList.contains("show") && this._extraPopupMarkerIdx === markerIdx) {
            this._hideExtraPopup();
            return;
        }

        this._extraPopupMarkerIdx = markerIdx;
        let bodyEl = document.getElementById("tcExtraPopupBody");

        let groups = def.extraChannelGroups || [];
        // Groups 1,2 go left (stacked), group 3 goes right
        let leftGroups = groups.slice(0, 2);
        let rightGroups = groups.slice(2);

        let html = '';

        // Left column: groups 1 and 2 stacked
        if (leftGroups.length > 0) {
            html += '<div class="tc-extra-popup-left">';
            for (let g of leftGroups) {
                html += '<div class="tc-extra-group tc-extra-group-left">';
                html += '<div class="tc-extra-group-name">' + this._escapeHtml(g.name) + '</div>';
                html += '<ul class="tc-extra-group-list">';
                for (let ch of g.channels) {
                    let rec = this.curDataMap[ch.cnlNum];
                    let valText = rec ? this._escapeHtml(rec.text) : "--";
                    let alias = ch.alias || (this.t.channel + ch.cnlNum);
                    html += '<li>' +
                        '<span class="extra-alias">' + this._escapeHtml(alias) + '</span>' +
                        '<span class="extra-val" data-cnl="' + ch.cnlNum + '">' + valText + '</span>' +
                        '</li>';
                }
                html += '</ul></div>';
            }
            html += '</div>';
        }

        // Right column: group 3
        if (rightGroups.length > 0) {
            html += '<div class="tc-extra-popup-right">';
            for (let g of rightGroups) {
                html += '<div class="tc-extra-group">';
                html += '<div class="tc-extra-group-name">' + this._escapeHtml(g.name) + '</div>';
                html += '<ul class="tc-extra-group-list">';
                for (let ch of g.channels) {
                    let rec = this.curDataMap[ch.cnlNum];
                    let valText = rec ? this._escapeHtml(rec.text) : "--";
                    let alias = ch.alias || (this.t.channel + ch.cnlNum);
                    html += '<li>' +
                        '<span class="extra-alias">' + this._escapeHtml(alias) + '</span>' +
                        '<span class="extra-val" data-cnl="' + ch.cnlNum + '">' + valText + '</span>' +
                        '</li>';
                }
                html += '</ul></div>';
            }
            html += '</div>';
        }

        bodyEl.innerHTML = html;

        // Position to the right of the clicked button
        let btn = event.currentTarget;
        let btnRect = btn.getBoundingClientRect();
        popup.classList.add("show");

        let popupRect = popup.getBoundingClientRect();
        let left = btnRect.right + 6;
        let top = btnRect.top + (btnRect.height / 2) - (popupRect.height / 2);

        // Fallback: if no space on right, show on left
        if (left + popupRect.width > window.innerWidth - 8) {
            left = btnRect.left - popupRect.width - 6;
        }
        // Keep within vertical bounds
        if (top < 8) top = 8;
        if (top + popupRect.height > window.innerHeight - 8) {
            top = window.innerHeight - popupRect.height - 8;
        }

        popup.style.left = left + 'px';
        popup.style.top = top + 'px';

        // Flip chevron arrow to '<' (open state)
        let svg = btn.querySelector('svg');
        if (svg) svg.style.transform = 'rotate(180deg)';

        // Close when clicking outside
        if (!this._extraPopupOutsideHandler) {
            this._extraPopupOutsideHandler = (e) => {
                if (!popup.contains(e.target) && !btn.contains(e.target)) {
                    this._hideExtraPopup();
                }
            };
            setTimeout(() => document.addEventListener('click', this._extraPopupOutsideHandler), 0);
        }
    }

    _hideExtraPopup() {
        let popup = document.getElementById("tcExtraPopup");
        popup.classList.remove("show");
        // Reset chevron arrow back to '>' (closed state)
        let openBtn = document.querySelector('.map-popup-more-btn svg');
        if (openBtn) openBtn.style.transform = '';
        this._extraPopupMarkerIdx = null;
        if (this._extraPopupOutsideHandler) {
            document.removeEventListener('click', this._extraPopupOutsideHandler);
            this._extraPopupOutsideHandler = null;
        }
    }

    // Vertical crosshair line plugin for Chart.js
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

    _renderMiniChart(canvas, cnlNum, alias, color, histData, isStatus) {
        if (this._activeMiniChart) {
            this._activeMiniChart.destroy();
            this._activeMiniChart = null;
        }

        let timestamps = histData.timestamps.map(t => new Date(t.ms));
        let trend = histData.trends[0];
        let data = trend.map((rec, idx) => ({
            x: timestamps[idx],
            y: (rec && rec.d && rec.d.stat > 0) ? rec.d.val : null
        }));

        let yConfig;
        if (isStatus) {
            yConfig = {
                min: -0.1, max: 1.1,
                ticks: { font: { size: 13 }, stepSize: 1, callback: v => (v === 0 || v === 1) ? v : '' },
                title: { display: false }
            };
        } else {
            let limits = this._getAxisLimits(alias);
            yConfig = { ticks: { font: { size: 13 } } };
            if (limits) {
                yConfig.min = limits.min;
                yConfig.max = limits.max;
                yConfig.title = { display: true, text: limits.unit, font: { size: 13 } };
            } else {
                yConfig.beginAtZero = false;
                yConfig.title = { display: false };
            }
        }

        // fixed range: -1h for parameters, -1d for statuses
        let now = new Date();
        let rangeMs = isStatus ? 24 * 60 * 60 * 1000 : 60 * 60 * 1000;
        let rangeStart = new Date(now.getTime() - rangeMs);
        let agoLabel = isStatus ? this.t.ago1d : this.t.ago1h;
        let nowLabel = this.t.now;
        let self = this;

        let dsConfig = {
            label: alias,
            data: data,
            borderColor: color,
            backgroundColor: color + "20",
            borderWidth: 1.5,
            pointRadius: 0,
            pointHoverRadius: 3,
            tension: isStatus ? 0 : 0.3,
            stepped: isStatus ? 'before' : false,
            fill: !isStatus,
            spanGaps: false
        };

        this._activeMiniChart = new Chart(canvas, {
            type: 'line',
            data: { datasets: [dsConfig] },
            plugins: [TcChartManager._crosshairPlugin],
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: 200 },
                interaction: {
                    mode: 'index',
                    intersect: false
                },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        enabled: false,
                        mode: 'index',
                        intersect: false,
                        external: function(context) {
                            self._miniChartExternalTooltip(context, isStatus);
                        }
                    },
                    zoom: {
                        pan: {
                            enabled: true,
                            mode: 'x'
                        },
                        zoom: {
                            wheel: { enabled: true, modifierKey: null },
                            pinch: { enabled: true },
                            drag: { enabled: false },
                            mode: 'x'
                        },
                        limits: {
                            x: {
                                min: rangeStart.getTime(),
                                max: now.getTime(),
                                minRange: 60000
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        type: 'time',
                        min: rangeStart.getTime(),
                        max: now.getTime(),
                        time: {
                            displayFormats: { millisecond: 'HH:mm:ss', second: 'HH:mm:ss', minute: 'HH:mm', hour: 'HH:mm' }
                        },
                        ticks: {
                            font: { size: 13 },
                            callback: function(value, index, ticksArr) {
                                if (index === 0) return agoLabel;
                                if (index === ticksArr.length - 1) return nowLabel;
                                let ts = new Date(ticksArr[index].value);
                                return isStatus ? self._fmtDate(ts) : self._fmtTime(ts);
                            }
                        },
                        title: { display: false }
                    },
                    y: yConfig
                }
            }
        });
    }

    // --- Historical Data Fetch ---
    async _fetchHistData(cnlNums, days) {
        days = days || 1;
        let cacheKey = cnlNums.join(",") + "_" + days;
        let cached = this._chartCache[cacheKey];
        // 60s cache for 1h data, 5min for longer periods
        let cacheMs = days <= 1 / 24 ? 60000 : 300000;
        if (cached && (Date.now() - cached.time) < cacheMs) {
            return cached.data;
        }

        let endTime = new Date();
        let startTime = new Date(endTime.getTime() - days * 24 * 60 * 60 * 1000);

        let url = this._mainApiRoot() + "GetHistData" +
            "?archiveBit=1" +
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
            console.error("Failed to fetch hist data:", err);
        }

        return null;
    }

    async _fetchHistDataRange(cnlNums, startTime, endTime) {
        let url = this._mainApiRoot() + "GetHistData" +
            "?archiveBit=1" +
            "&startTime=" + encodeURIComponent(startTime.toISOString()) +
            "&endTime=" + encodeURIComponent(endTime.toISOString()) +
            "&endInclusive=true" +
            "&cnlNums=" + encodeURIComponent(cnlNums.join(","));

        try {
            let resp = await fetch(url);
            let dto = await resp.json();
            if (dto.ok && dto.data) return dto.data;
        } catch (err) {
            console.error("Failed to fetch hist data range:", err);
        }
        return null;
    }

    // --- Data refresh ---
    _refreshData() {
        fetch(this._apiRoot() + "GetCurData?viewID=" + this.viewID)
            .then(r => r.json())
            .then(dto => {
                if (dto.ok) {
                    this._updateMarkers(dto.data.records);
                }
            })
            .catch(err => console.error("Map data refresh error:", err));
    }

    _updateMarkers(records) {
        for (let rec of records) {
            this.curDataMap[rec.cnlNum] = rec;
        }

        for (let entry of this.markerEntries) {
            // Update marker position from real-time coordinate channels
            let def = entry.def;
            if (def.latCnlNum && def.lonCnlNum) {
                let latRec = this.curDataMap[def.latCnlNum];
                let lonRec = this.curDataMap[def.lonCnlNum];
                if (latRec && latRec.stat > 0 && lonRec && lonRec.stat > 0) {
                    entry.leafletMarker.setLatLng([latRec.val, lonRec.val]);
                }
            }

            let isTriangle = entry.def.type === 'triangle';
            let color = isTriangle
                ? this._getTriangleMarkerColor(entry.def)
                : this._getGradientMarkerColor(entry.def);
            let icon;
            if (isTriangle) {
                icon = this._createTriangleMarkerIcon(color, false, null);
            } else {
                let alert = this._hasActiveStatus(entry.def);
                icon = this._createMarkerIcon(color, alert);
            }
            entry.leafletMarker.setIcon(icon);

            // Update values in-place if popup is open, otherwise rebuild
            let popup = entry.leafletMarker.getPopup();
            if (popup && popup.isOpen()) {
                let popupEl = popup.getElement();
                if (popupEl) {
                    if (isTriangle) {
                        // Update triangle popup in-place
                        this._updateTrianglePopupInPlace(popupEl, entry.def);
                    } else {
                        for (let ch of entry.def.channels) {
                            let valEl = popupEl.querySelector('td[data-cnl="' + ch.cnlNum + '"]');
                            let rec = this.curDataMap[ch.cnlNum];
                            if (valEl && rec) {
                                valEl.textContent = rec.text || "--";
                            }
                        }
                    }
                    continue;
                }
            }

            entry.leafletMarker.setPopupContent(
                isTriangle ? this._buildTrianglePopupHtml(entry.def) : this._buildPopupHtml(entry.def)
            );
        }

        // Update extra channels popup if open
        let extraPopup = document.getElementById("tcExtraPopup");
        if (extraPopup && extraPopup.classList.contains("show")) {
            let extraVals = extraPopup.querySelectorAll('[data-cnl]');
            for (let el of extraVals) {
                let cnl = parseInt(el.getAttribute('data-cnl'));
                let rec = this.curDataMap[cnl];
                if (rec) el.textContent = rec.text || "--";
            }
        }

        this._updateStatusPanel();
    }

    // Returns aggregate marker color based on how many channels are working.
    // Gray (#999) = no channels working, Yellow (#f0ad4e) = some working,
    // Blue (#3388ff) = all working or single channel working.
    _getMarkerColor(def) {
        let channels = def.channels || [];
        if (channels.length === 0) return "#999";

        let total = channels.length;
        let working = 0;
        for (let ch of channels) {
            let rec = this.curDataMap[ch.cnlNum];
            // stat > 0 means the channel has a defined status (working)
            if (rec && rec.stat > 0) working++;
        }

        if (working === 0) return "#999";      // gray - none working
        if (working < total) return "#f0ad4e";  // amber - partial
        return "#3388ff";                        // blue - all working
    }

    _escapeHtml(text) {
        if (!text) return "";
        let div = document.createElement("div");
        div.appendChild(document.createTextNode(text));
        return div.innerHTML;
    }

    destroy() {
        if (this.updateTimer) { clearInterval(this.updateTimer); this.updateTimer = null; }
        if (this._activeChart) { this._activeChart.destroy(); this._activeChart = null; }
        if (this._activeMiniChart) { this._activeMiniChart.destroy(); this._activeMiniChart = null; }
        if (this.map) { this.map.remove(); this.map = null; }
    }
}

// Global singleton used by thermal-camera.js — created lazily on DOM ready
// so Razor can render the overlay markup before any chart code runs.
var tcChart = null;
document.addEventListener("DOMContentLoaded", function () {
    if (document.getElementById("tcChartOverlay") && !tcChart) {
        tcChart = new TcChartManager({ refreshRate: 1000 });
        tcChartInstance = tcChart;
        // Click outside the panel closes the overlay
        document.getElementById("tcChartOverlay").addEventListener("click", function (e) {
            if (e.target === this) tcChart._closeChartOverlay();
        });
        // ESC also closes
        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && document.getElementById("tcChartOverlay").classList.contains("show")) {
                tcChart._closeChartOverlay();
            }
        });
    }
});

