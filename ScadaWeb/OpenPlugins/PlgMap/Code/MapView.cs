// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Data.Entities;
using Scada.Data.Models;
using Scada.Lang;
using System.Xml;

namespace Scada.Web.Plugins.PlgMap.Code
{
    /// <summary>
    /// Represents a map view containing markers with geographic coordinates.
    /// <para>Представляет представление карты с маркерами и географическими координатами.</para>
    /// </summary>
    public class MapView(View viewEntity) : ViewBase(viewEntity)
    {
        /// <summary>
        /// Gets the list of markers on the map.
        /// </summary>
        public List<MapMarker> Markers { get; } = [];

        /// <summary>
        /// Gets the map configuration.
        /// </summary>
        public MapConfig MapConfig { get; private set; } = new();


        /// <summary>
        /// Loads the view from the specified stream.
        /// </summary>
        public override void LoadView(Stream stream)
        {
            XmlDocument xmlDoc = new();
            xmlDoc.Load(stream);

            XmlElement rootElem = xmlDoc.DocumentElement;
            string rootName = rootElem.Name;

            if (rootName.Equals("Map", StringComparison.OrdinalIgnoreCase))
            {
                // v5.8 format: <Map> with <InitialView>, <Tiling>, <Locations>
                // or new compact format: <Map> with <Marker> children
                if (rootElem.SelectSingleNode("Locations") != null ||
                    rootElem.SelectSingleNode("InitialView") != null)
                {
                    LoadV58Format(rootElem);
                }
                else
                {
                    LoadNewFormat(rootElem);
                }
            }
            else if (rootName.Equals("MapView", StringComparison.OrdinalIgnoreCase))
            {
                // Legacy format: <MapView> with <MapConfig> and <Markers>
                LoadLegacyFormat(rootElem);
            }
            else
            {
                throw new ScadaException(CommonPhrases.InvalidFileFormat);
            }
        }

        /// <summary>
        /// Loads the v5.8 XML format with InitialView, Tiling, and Locations sections.
        /// </summary>
        private void LoadV58Format(XmlElement rootElem)
        {
            XmlNode initialViewNode = rootElem.SelectSingleNode("InitialView");
            XmlNode tilingNode = rootElem.SelectSingleNode("Tiling");
            MapConfig = MapConfig.LoadFromV58Xml(initialViewNode, tilingNode);

            XmlNode locationsNode = rootElem.SelectSingleNode("Locations");
            if (locationsNode != null)
            {
                int index = 1;
                foreach (XmlNode locNode in locationsNode.SelectNodes("Location"))
                {
                    MapMarker marker = MapMarker.LoadFromLocationXml(locNode, index++);
                    Markers.Add(marker);

                    foreach (MarkerChannel ch in marker.Channels)
                    {
                        AddCnlNum(ch.CnlNum);
                    }
                }
            }
        }

        /// <summary>
        /// Loads the new XML format with multi-channel markers.
        /// </summary>
        private void LoadNewFormat(XmlElement rootElem)
        {
            // load optional map config
            if (rootElem.SelectSingleNode("MapConfig") is XmlNode configNode)
            {
                MapConfig = MapConfig.LoadFromXml(configNode);
            }

            // load markers directly under root
            foreach (XmlNode markerNode in rootElem.SelectNodes("Marker"))
            {
                MapMarker marker = MapMarker.LoadFromXml(markerNode);
                Markers.Add(marker);

                foreach (MarkerChannel ch in marker.Channels)
                {
                    AddCnlNum(ch.CnlNum);
                }
            }

            // auto-compute center if not specified and markers exist
            if (Markers.Count > 0 && MapConfig.CenterLat == 0 && MapConfig.CenterLng == 0)
            {
                MapConfig.CenterLat = Markers.Average(m => m.Latitude);
                MapConfig.CenterLng = Markers.Average(m => m.Longitude);
            }
        }

        /// <summary>
        /// Loads the legacy XML format (single channel per marker).
        /// </summary>
        private void LoadLegacyFormat(XmlElement rootElem)
        {
            if (rootElem.SelectSingleNode("MapConfig") is XmlNode configNode)
            {
                MapConfig = MapConfig.LoadFromXml(configNode);
            }

            if (rootElem.SelectSingleNode("Markers") is XmlNode markersNode)
            {
                foreach (XmlNode markerNode in markersNode.SelectNodes("Marker"))
                {
                    MapMarker marker = MapMarker.LoadFromXml(markerNode);
                    Markers.Add(marker);

                    foreach (MarkerChannel ch in marker.Channels)
                    {
                        AddCnlNum(ch.CnlNum);
                    }
                }
            }
        }

        /// <summary>
        /// Binds the view to the configuration database.
        /// </summary>
        public override void Bind(ConfigDataset configDataset)
        {
            AddCnlNumsForArrays(configDataset.CnlTable);
        }
    }
}
