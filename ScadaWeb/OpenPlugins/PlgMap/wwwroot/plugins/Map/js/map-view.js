// Map view for displaying SCADA markers on OpenStreetMap.
// Depends on scada-common.js and Leaflet.js (loaded from CDN).

// Leaflet CSS and JS are injected dynamically to keep the plugin self-contained.
var mapViewInstance = null;

class MapViewManager {
    static LEAFLET_CSS = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";
    static LEAFLET_JS = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.js";
    static DEFAULT_TILE_URL = "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png";

    // Default English phrases; overridden by options.lang or by the inline LANG
    // object rendered from SCADA language dictionaries in MapView.cshtml.
    static DEFAULT_LANG = {
        channel: "Ch",
        details: "Details",
        error: "Error",
        errorPrefix: "Error: "
    };

    constructor(options) {
        this.viewID = options.viewID;
        this.refreshRate = options.refreshRate || 1000;
        this.rootPath = this._detectRootPath();
        this.t = Object.assign({}, MapViewManager.DEFAULT_LANG, options.lang || {});

        this.map = null;
        this.markers = [];       // marker definitions from server
        this.leafletMarkers = {}; // keyed by marker id
        this.updateTimer = null;
    }

    // Detects the application root path.
    _detectRootPath() {
        let base = document.querySelector("base");
        return base ? base.getAttribute("href") : "/";
    }

    // Gets the API root path.
    _apiRoot() {
        return this.rootPath + "Api/Map/";
    }

    // Loads Leaflet CSS dynamically.
    _loadLeafletCss() {
        return new Promise((resolve) => {
            if (document.querySelector(`link[href*="leaflet"]`)) {
                resolve();
                return;
            }
            let link = document.createElement("link");
            link.rel = "stylesheet";
            link.href = MapViewManager.LEAFLET_CSS;
            link.integrity = "sha256-p4NxAoJBhIIN+hmNHrzRCf9tD/miZyoHS5obTRR9BMY=";
            link.crossOrigin = "";
            link.onload = resolve;
            document.head.appendChild(link);
        });
    }

    // Loads Leaflet JS dynamically.
    _loadLeafletJs() {
        return new Promise((resolve, reject) => {
            if (typeof L !== "undefined") {
                resolve();
                return;
            }
            let script = document.createElement("script");
            script.src = MapViewManager.LEAFLET_JS;
            script.integrity = "sha256-20nQCchB9co0qIjJZRGuk2/Z9VM+kNiyxNV1lvTlZBo=";
            script.crossOrigin = "";
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
        });
    }

    // Creates a colored circle icon for the marker based on channel status.
    _createMarkerIcon(color) {
        color = color || "#3388ff";
        let svg = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">` +
            `<circle cx="12" cy="12" r="10" fill="${color}" stroke="#fff" stroke-width="2"/>` +
            `</svg>`;
        return L.divIcon({
            html: svg,
            className: "map-marker-icon",
            iconSize: [24, 24],
            iconAnchor: [12, 12],
            popupAnchor: [0, -14]
        });
    }

    // Builds the popup HTML content for a marker with all its channels.
    _buildPopupContent(markerDef, dataMap) {
        let html = `<b>${this._escapeHtml(markerDef.name)}</b>`;

        if (markerDef.descr) {
            html += `<br/><i>${this._escapeHtml(markerDef.descr)}</i>`;
        }

        html += `<table class="map-popup-table">`;

        for (let ch of markerDef.channels) {
            let alias = ch.alias || (this.t.channel + " " + ch.cnlNum);
            let rec = dataMap[ch.cnlNum];
            let valText = rec ? rec.text : "--";

            html += `<tr>` +
                `<td class="map-popup-alias">${this._escapeHtml(alias)}</td>` +
                `<td class="map-popup-val">${this._escapeHtml(valText)}</td>` +
                `</tr>`;
        }

        html += `</table>`;

        if (markerDef.linkViewID > 0) {
            html += `<a href="${this.rootPath}Map/MapView?viewID=${markerDef.linkViewID}" ` +
                `class="map-popup-link">${this._escapeHtml(this.t.details)}</a>`;
        }

        return html;
    }

    // Initializes the Leaflet map and loads marker definitions.
    async init() {
        await this._loadLeafletCss();
        await this._loadLeafletJs();

        // Fetch map data from server
        let response = await fetch(this._apiRoot() + "GetMapData?viewID=" + this.viewID);
        let dto = await response.json();

        if (!dto.ok) {
            console.error("Failed to load map data:", dto.msg);
            document.getElementById("divMapContainer").textContent = this.t.errorPrefix + dto.msg;
            return;
        }

        let config = dto.data.config;
        this.markers = dto.data.markers;

        // Create Leaflet map
        this.map = L.map("divMapContainer", {
            minZoom: config.minZoom,
            maxZoom: config.maxZoom
        }).setView([config.centerLat, config.centerLng], config.zoom);

        // Add tile layer (use custom URL template if specified)
        let tileUrl = config.tileUrlTemplate || MapViewManager.DEFAULT_TILE_URL;
        L.tileLayer(tileUrl, {
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
            maxZoom: config.maxZoom
        }).addTo(this.map);

        // Create markers
        for (let m of this.markers) {
            let marker = L.marker([m.lat, m.lng], {
                icon: this._createMarkerIcon(null),
                title: m.name
            }).addTo(this.map);

            let popupContent = this._buildPopupContent(m, {});
            marker.bindPopup(popupContent, { maxWidth: 300 });

            this.leafletMarkers[m.id] = { marker: marker, def: m };
        }

        // Start data refresh
        this._refreshData();
        this.updateTimer = setInterval(() => this._refreshData(), this.refreshRate);
    }

    // Refreshes current data for all markers.
    _refreshData() {
        fetch(this._apiRoot() + "GetCurData?viewID=" + this.viewID)
            .then(response => response.json())
            .then(dto => {
                if (dto.ok) {
                    this._updateMarkers(dto.data.records);
                }
            })
            .catch(error => console.error("Map data refresh error:", error));
    }

    // Updates marker icons and popups with current data.
    _updateMarkers(records) {
        let dataMap = {};
        for (let rec of records) {
            dataMap[rec.cnlNum] = rec;
        }

        for (let id in this.leafletMarkers) {
            let entry = this.leafletMarkers[id];
            let def = entry.def;

            let color = this._getMarkerColor(def, dataMap);
            entry.marker.setIcon(this._createMarkerIcon(color));

            // Update popup content with all channel values
            let popupContent = this._buildPopupContent(def, dataMap);
            entry.marker.setPopupContent(popupContent);
        }
    }

    // Returns aggregate marker color based on how many channels are working.
    // Gray (#999) = no channels working, Yellow (#f0ad4e) = some working,
    // Blue (#3388ff) = all working or single channel working.
    _getMarkerColor(def, dataMap) {
        let channels = def.channels || [];
        if (channels.length === 0) return "#999";

        let total = channels.length;
        let working = 0;
        for (let ch of channels) {
            let rec = dataMap[ch.cnlNum];
            if (rec && rec.stat > 0) working++;
        }

        if (working === 0) return "#999";      // gray - none working
        if (working < total) return "#f0ad4e";  // amber - partial
        return "#3388ff";                        // blue - all working
    }

    // Escapes HTML special characters.
    _escapeHtml(text) {
        if (!text) return "";
        let div = document.createElement("div");
        div.appendChild(document.createTextNode(text));
        return div.innerHTML;
    }

    // Destroys the map and stops data refresh.
    destroy() {
        if (this.updateTimer) {
            clearInterval(this.updateTimer);
            this.updateTimer = null;
        }
        if (this.map) {
            this.map.remove();
            this.map = null;
        }
    }
}

// Initialize when DOM is ready.
document.addEventListener("DOMContentLoaded", function () {
    var container = document.getElementById("divMapContainer");
    if (container && container.dataset.viewId) {
        var options = {
            viewID: parseInt(container.dataset.viewId, 10),
            refreshRate: parseInt(container.dataset.refreshRate, 10) || 1000
        };
        mapViewInstance = new MapViewManager(options);
        mapViewInstance.init().catch(function (error) {
            console.error("Map initialization failed:", error);
        });
    }
});
