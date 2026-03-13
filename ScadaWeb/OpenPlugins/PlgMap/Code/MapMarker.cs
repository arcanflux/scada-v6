// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Globalization;
using System.Xml;

namespace Scada.Web.Plugins.PlgMap.Code
{
    /// <summary>
    /// Represents a marker on the map bound to a SCADA channel.
    /// <para>Представляет маркер на карте, связанный с каналом SCADA.</para>
    /// </summary>
    public class MapMarker
    {
        /// <summary>
        /// Gets or sets the marker latitude.
        /// </summary>
        public double Latitude { get; set; }

        /// <summary>
        /// Gets or sets the marker longitude.
        /// </summary>
        public double Longitude { get; set; }

        /// <summary>
        /// Gets or sets the channel number bound to this marker.
        /// </summary>
        public int CnlNum { get; set; }

        /// <summary>
        /// Gets or sets the marker caption.
        /// </summary>
        public string Caption { get; set; } = "";

        /// <summary>
        /// Gets or sets the popup text template. Use {val} and {stat} placeholders.
        /// </summary>
        public string PopupTemplate { get; set; } = "";


        /// <summary>
        /// Loads the marker from an XML node.
        /// </summary>
        public static MapMarker LoadFromXml(XmlNode node)
        {
            return new MapMarker
            {
                Latitude = double.Parse(node.Attributes?["latitude"]?.Value ?? "0",
                    CultureInfo.InvariantCulture),
                Longitude = double.Parse(node.Attributes?["longitude"]?.Value ?? "0",
                    CultureInfo.InvariantCulture),
                CnlNum = int.Parse(node.Attributes?["cnlNum"]?.Value ?? "0"),
                Caption = node.Attributes?["caption"]?.Value ?? "",
                PopupTemplate = node.Attributes?["popupTemplate"]?.Value ?? ""
            };
        }
    }
}
