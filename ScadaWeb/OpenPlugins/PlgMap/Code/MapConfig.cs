// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Globalization;
using System.Xml;

namespace Scada.Web.Plugins.PlgMap.Code
{
    /// <summary>
    /// Represents the map configuration loaded from a view file.
    /// <para>Представляет конфигурацию карты из файла представления.</para>
    /// </summary>
    public class MapConfig
    {
        /// <summary>
        /// Gets or sets the initial map center latitude.
        /// </summary>
        public double CenterLat { get; set; } = 55.030204;

        /// <summary>
        /// Gets or sets the initial map center longitude.
        /// </summary>
        public double CenterLng { get; set; } = 82.920430;

        /// <summary>
        /// Gets or sets the initial zoom level (1-18).
        /// </summary>
        public int Zoom { get; set; } = 13;

        /// <summary>
        /// Gets or sets the minimum zoom level.
        /// </summary>
        public int MinZoom { get; set; } = 2;

        /// <summary>
        /// Gets or sets the maximum zoom level.
        /// </summary>
        public int MaxZoom { get; set; } = 19;

        /// <summary>
        /// Gets or sets the tile layer URL template.
        /// </summary>
        public string TileUrlTemplate { get; set; } = "";


        /// <summary>
        /// Loads the map configuration from a MapConfig XML node (compact format).
        /// </summary>
        public static MapConfig LoadFromXml(XmlNode node)
        {
            return new MapConfig
            {
                CenterLat = double.Parse(node.Attributes?["centerLat"]?.Value ?? "55.030204",
                    CultureInfo.InvariantCulture),
                CenterLng = double.Parse(node.Attributes?["centerLng"]?.Value ?? "82.920430",
                    CultureInfo.InvariantCulture),
                Zoom = int.Parse(node.Attributes?["zoom"]?.Value ?? "13"),
                MinZoom = int.Parse(node.Attributes?["minZoom"]?.Value ?? "2"),
                MaxZoom = int.Parse(node.Attributes?["maxZoom"]?.Value ?? "19")
            };
        }

        /// <summary>
        /// Loads the map configuration from v5.8-style InitialView and Tiling XML nodes.
        /// </summary>
        public static MapConfig LoadFromV58Xml(XmlNode initialViewNode, XmlNode tilingNode)
        {
            MapConfig config = new();

            if (initialViewNode != null)
            {
                config.CenterLat = double.Parse(
                    initialViewNode.SelectSingleNode("Lat")?.InnerText ?? "55.030204",
                    CultureInfo.InvariantCulture);
                config.CenterLng = double.Parse(
                    initialViewNode.SelectSingleNode("Lon")?.InnerText ?? "82.920430",
                    CultureInfo.InvariantCulture);
                config.Zoom = int.Parse(
                    initialViewNode.SelectSingleNode("Zoom")?.InnerText ?? "13");
            }

            if (tilingNode != null)
            {
                config.TileUrlTemplate = tilingNode.SelectSingleNode("UrlTemplate")?.InnerText ?? "";
            }

            return config;
        }
    }
}
