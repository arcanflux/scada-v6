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
        public double CenterLat { get; set; } = 51.505;

        /// <summary>
        /// Gets or sets the initial map center longitude.
        /// </summary>
        public double CenterLng { get; set; } = -0.09;

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
        /// Loads the map configuration from an XML node.
        /// </summary>
        public static MapConfig LoadFromXml(XmlNode node)
        {
            return new MapConfig
            {
                CenterLat = double.Parse(node.Attributes?["centerLat"]?.Value ?? "51.505",
                    CultureInfo.InvariantCulture),
                CenterLng = double.Parse(node.Attributes?["centerLng"]?.Value ?? "-0.09",
                    CultureInfo.InvariantCulture),
                Zoom = int.Parse(node.Attributes?["zoom"]?.Value ?? "13"),
                MinZoom = int.Parse(node.Attributes?["minZoom"]?.Value ?? "2"),
                MaxZoom = int.Parse(node.Attributes?["maxZoom"]?.Value ?? "19")
            };
        }
    }
}
