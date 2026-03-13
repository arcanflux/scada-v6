// Map view for displaying SCADA markers on OpenStreetMap.
// Depends on scada-common.js and Leaflet.js (loaded from CDN).

// Leaflet CSS and JS are injected dynamically to keep the plugin self-contained.
var mapViewInstance = null;

class MapViewManager {
    static LEAFLET_CSS = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css";
    static LEAFLET_JS = "https://unpkg.com/leaflet@1.9.4/dist/leaflet.js";

    constructor(options) {
        this.viewID = options.viewID;
        this.refreshRate = options.refreshRate || 1000;
        this.rootPath = this._detectRootPath();

        this.map = null;
        this.markers = [];       // marker definitions from server
        this.leafletMarkers = {}; // keyed by cnlNum
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

    // Initializes the Leaflet map and loads marker definitions.
    async init() {
        await this._loadLeafletCss();
        await this._loadLeafletJs();

        // Fetch map data from server
        let response = await fetch(this._apiRoot() + "GetMapData?viewID=" + this.viewID);
        let dto = await response.json();

        if (!dto.ok) {
            console.error("Failed to load map data:", dto.msg);
            document.getElementById("divMapContainer").textContent = "Error: " + dto.msg;
            return;
        }

        let config = dto.data.config;
        this.markers = dto.data.markers;

        // Create Leaflet map
        this.map = L.map("divMapContainer", {
            minZoom: config.minZoom,
            maxZoom: config.maxZoom
        }).setView([config.centerLat, config.centerLng], config.zoom);

        // Add OSM tile layer
        L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
            maxZoom: config.maxZoom
        }).addTo(this.map);

        // Create markers
        for (let m of this.markers) {
            let marker = L.marker([m.lat, m.lng], {
                icon: this._createMarkerIcon(null),
                title: m.caption
            }).addTo(this.map);

            let popupContent = m.caption
                ? `<b>${this._escapeHtml(m.caption)}</b><br/><span class="map-popup-val">--</span>`
                : `<span class="map-popup-val">--</span>`;
            marker.bindPopup(popupContent);

            this.leafletMarkers[m.cnlNum] = { marker: marker, def: m };
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

        for (let cnlNum in this.leafletMarkers) {
            let entry = this.leafletMarkers[cnlNum];
            let rec = dataMap[cnlNum];
            if (!rec) continue;

            // Determine color based on status
            let color = this._getColorForStatus(rec.stat);
            entry.marker.setIcon(this._createMarkerIcon(color));

            // Update popup content
            let def = entry.def;
            let popupText;

            if (def.popupTemplate) {
                popupText = def.popupTemplate
                    .replace(/\{val\}/g, this._escapeHtml(rec.text))
                    .replace(/\{stat\}/g, rec.stat);
            } else if (def.caption) {
                popupText = `<b>${this._escapeHtml(def.caption)}</b><br/>` +
                    `<span class="map-popup-val">${this._escapeHtml(rec.text)}</span>`;
            } else {
                popupText = `<span class="map-popup-val">${this._escapeHtml(rec.text)}</span>`;
            }

            entry.marker.setPopupContent(popupText);
        }
    }

    // Returns a color string based on SCADA channel status.
    _getColorForStatus(stat) {
        switch (stat) {
            case 0:   return "#999";    // undefined
            case 2:   return "#28a745"; // normal (defined)
            case 3:   return "#ffc107"; // warning
            case 4:   return "#dc3545"; // error / alarm
            case 5:   return "#dc3545"; // critical
            default:  return "#3388ff"; // default blue
        }
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
    if (typeof mapViewOptions !== "undefined") {
        mapViewInstance = new MapViewManager(mapViewOptions);
        mapViewInstance.init().catch(error => {
            console.error("Map initialization failed:", error);
        });
    }
});
