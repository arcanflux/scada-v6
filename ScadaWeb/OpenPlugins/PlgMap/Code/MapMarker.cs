// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Globalization;
using System.Xml;

namespace Scada.Web.Plugins.PlgMap.Code
{
    /// <summary>
    /// Represents a channel bound to a map marker.
    /// <para>Представляет канал, привязанный к маркеру карты.</para>
    /// </summary>
    public class MarkerChannel
    {
        /// <summary>
        /// Gets or sets the channel number.
        /// </summary>
        public int CnlNum { get; set; }

        /// <summary>
        /// Gets or sets the channel alias displayed in the popup.
        /// </summary>
        public string Alias { get; set; } = "";
    }

    /// <summary>
    /// Represents a marker on the map bound to one or more SCADA channels.
    /// <para>Представляет маркер на карте, связанный с каналами SCADA.</para>
    /// </summary>
    public class MapMarker
    {
        /// <summary>
        /// Gets or sets the marker ID.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the marker latitude.
        /// </summary>
        public double Latitude { get; set; }

        /// <summary>
        /// Gets or sets the marker longitude.
        /// </summary>
        public double Longitude { get; set; }

        /// <summary>
        /// Gets or sets the marker name / caption.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Gets the list of channels bound to this marker.
        /// </summary>
        public List<MarkerChannel> Channels { get; } = new();


        /// <summary>
        /// Loads the marker from an XML node.
        /// </summary>
        public static MapMarker LoadFromXml(XmlNode node)
        {
            MapMarker marker = new()
            {
                Id = int.Parse(node.Attributes?["id"]?.Value ?? "0"),
                Latitude = double.Parse(node.Attributes?["lat"]?.Value ??
                    node.Attributes?["latitude"]?.Value ?? "0",
                    CultureInfo.InvariantCulture),
                Longitude = double.Parse(node.Attributes?["lon"]?.Value ??
                    node.Attributes?["longitude"]?.Value ?? "0",
                    CultureInfo.InvariantCulture),
                Name = node.Attributes?["name"]?.Value ??
                    node.Attributes?["caption"]?.Value ?? ""
            };

            // load child Channel elements
            foreach (XmlNode chNode in node.SelectNodes("Channel"))
            {
                marker.Channels.Add(new MarkerChannel
                {
                    CnlNum = int.Parse(chNode.Attributes?["num"]?.Value ?? "0"),
                    Alias = chNode.Attributes?["alias"]?.Value ?? ""
                });
            }

            // backward compat: single cnlNum attribute
            if (marker.Channels.Count == 0)
            {
                string cnlNumStr = node.Attributes?["cnlNum"]?.Value;
                if (!string.IsNullOrEmpty(cnlNumStr) && int.TryParse(cnlNumStr, out int cnlNum) && cnlNum > 0)
                {
                    marker.Channels.Add(new MarkerChannel
                    {
                        CnlNum = cnlNum,
                        Alias = node.Attributes?["caption"]?.Value ?? ""
                    });
                }
            }

            return marker;
        }
    }
}
