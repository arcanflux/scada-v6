// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Globalization;
using System.Xml;

namespace Scada.Web.Plugins.PlgMap.Code
{
    /// <summary>
    /// Defines the visual shape of a map marker.
    /// </summary>
    public enum MarkerType
    {
        Circle = 0,
        Triangle = 1,
        Pnu = 2
    }

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
    /// Represents a named group of extra channels for the secondary popup.
    /// </summary>
    public class MarkerChannelGroup
    {
        public string Name { get; set; } = "";
        public List<MarkerChannel> Channels { get; } = [];
    }

    /// <summary>
    /// Represents a marker on the map bound to one or more SCADA channels.
    /// <para>Представляет маркер на карте, связанный с каналами SCADA.</para>
    /// </summary>
    public class MapMarker
    {
        /// <summary>
        /// Gets or sets the marker visual type (circle, triangle, etc.).
        /// </summary>
        public MarkerType Type { get; set; } = MarkerType.Circle;

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
        /// Gets or sets the channel number for real-time latitude updates. 0 means static.
        /// </summary>
        public int LatCnlNum { get; set; }

        /// <summary>
        /// Gets or sets the channel number for real-time longitude updates. 0 means static.
        /// </summary>
        public int LonCnlNum { get; set; }

        /// <summary>
        /// Gets or sets the marker name / caption.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Gets or sets the marker description.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Gets or sets the photo URL/path for this marker location.
        /// </summary>
        public string Photo { get; set; } = "";

        /// <summary>
        /// Gets or sets the status channel number. 0 means not specified.
        /// Positive channel value = normal, otherwise needs attention.
        /// </summary>
        public int StatusCnlNum { get; set; }

        /// <summary>
        /// Gets or sets the linked view ID for detailed information. 0 means no link.
        /// </summary>
        public int LinkViewID { get; set; }

        /// <summary>
        /// Gets the list of channels bound to this marker.
        /// </summary>
        public List<MarkerChannel> Channels { get; } = [];

        /// <summary>
        /// Gets the list of status indicator channels for this marker.
        /// These are separate from data channels and shown as dots above the marker.
        /// </summary>
        public List<MarkerChannel> StatusChannels { get; } = [];

        /// <summary>
        /// Gets the list of extra channel groups shown in the secondary "more" popup.
        /// </summary>
        public List<MarkerChannelGroup> ExtraChannelGroups { get; } = [];


        /// <summary>
        /// Loads the marker from an XML node (new compact format).
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
                    node.Attributes?["caption"]?.Value ?? "",
                Type = Enum.TryParse<MarkerType>(node.Attributes?["type"]?.Value, true, out var mt)
                    ? mt : MarkerType.Circle,
                LatCnlNum = int.TryParse(node.Attributes?["latCnlNum"]?.Value, out int latCnl) ? latCnl : 0,
                LonCnlNum = int.TryParse(node.Attributes?["lonCnlNum"]?.Value, out int lonCnl) ? lonCnl : 0,
                Photo = node.Attributes?["photo"]?.Value ?? ""
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

            // load status channels
            foreach (XmlNode stNode in node.SelectNodes("Status"))
            {
                marker.StatusChannels.Add(new MarkerChannel
                {
                    CnlNum = int.Parse(stNode.Attributes?["num"]?.Value ?? "0"),
                    Alias = stNode.Attributes?["alias"]?.Value ?? ""
                });
            }

            // load extra channel groups (shown in secondary "more" popup)
            XmlNode extraNode = node.SelectSingleNode("ExtraChannels");
            if (extraNode != null)
            {
                foreach (XmlNode grpNode in extraNode.SelectNodes("Group"))
                {
                    MarkerChannelGroup grp = new()
                    {
                        Name = grpNode.Attributes?["name"]?.Value ?? ""
                    };
                    foreach (XmlNode chNode in grpNode.SelectNodes("Item"))
                    {
                        grp.Channels.Add(new MarkerChannel
                        {
                            CnlNum = int.Parse(chNode.Attributes?["num"]?.Value ?? "0"),
                            Alias = chNode.Attributes?["alias"]?.Value ?? ""
                        });
                    }
                    marker.ExtraChannelGroups.Add(grp);
                }

                // Also handle Item/Channel elements directly under ExtraChannels (no Group wrapper)
                MarkerChannelGroup defaultGrp = new() { Name = "" };
                foreach (XmlNode chNode in extraNode.ChildNodes)
                {
                    if (chNode.NodeType != XmlNodeType.Element) continue;
                    string tag = chNode.Name;
                    if (tag == "Group") continue; // already processed above

                    // Accept Item, Channel, or any other direct child element
                    int cnlNum = int.TryParse(
                        chNode.Attributes?["num"]?.Value ?? chNode.Attributes?["cnlNum"]?.Value ?? "0",
                        out int n) ? n : 0;
                    string alias = chNode.Attributes?["alias"]?.Value
                        ?? chNode.InnerText?.Trim() ?? "";

                    if (cnlNum > 0)
                        defaultGrp.Channels.Add(new MarkerChannel { CnlNum = cnlNum, Alias = alias });
                }
                if (defaultGrp.Channels.Count > 0)
                    marker.ExtraChannelGroups.Add(defaultGrp);
            }

            return marker;
        }

        /// <summary>
        /// Loads the marker from a v5.8-style Location XML node.
        /// </summary>
        public static MapMarker LoadFromLocationXml(XmlNode node, int index)
        {
            MapMarker marker = new()
            {
                Id = index,
                Latitude = double.Parse(node.SelectSingleNode("Lat")?.InnerText ?? "0",
                    CultureInfo.InvariantCulture),
                Longitude = double.Parse(node.SelectSingleNode("Lon")?.InnerText ?? "0",
                    CultureInfo.InvariantCulture),
                Name = node.SelectSingleNode("Name")?.InnerText ?? "",
                Description = node.SelectSingleNode("Descr")?.InnerText ?? "",
                StatusCnlNum = int.Parse(node.SelectSingleNode("StatusCnlNum")?.InnerText ?? "0"),
                Type = Enum.TryParse<MarkerType>(node.SelectSingleNode("Type")?.InnerText, true, out var mt)
                    ? mt : MarkerType.Circle,
                LatCnlNum = int.TryParse(node.SelectSingleNode("LatCnlNum")?.InnerText, out int latCnl2) ? latCnl2 : 0,
                LonCnlNum = int.TryParse(node.SelectSingleNode("LonCnlNum")?.InnerText, out int lonCnl2) ? lonCnl2 : 0,
                Photo = node.SelectSingleNode("Photo")?.InnerText ?? ""
            };

            // parse Link viewID
            XmlNode linkNode = node.SelectSingleNode("Link");
            if (linkNode != null)
            {
                marker.LinkViewID = int.Parse(linkNode.Attributes?["viewID"]?.Value ?? "0");
            }

            // parse Data > DataItem elements
            XmlNode dataNode = node.SelectSingleNode("Data");
            if (dataNode != null)
            {
                foreach (XmlNode itemNode in dataNode.SelectNodes("DataItem"))
                {
                    string cnlNumStr = itemNode.Attributes?["cnlNum"]?.Value ?? "0";
                    marker.Channels.Add(new MarkerChannel
                    {
                        CnlNum = int.Parse(cnlNumStr),
                        Alias = itemNode.InnerText?.Trim() ?? ""
                    });
                }
            }

            // parse Statuses > StatusItem elements (object status indicators)
            XmlNode statusesNode = node.SelectSingleNode("Statuses");
            if (statusesNode != null)
            {
                foreach (XmlNode stNode in statusesNode.SelectNodes("StatusItem"))
                {
                    string stCnlStr = stNode.Attributes?["cnlNum"]?.Value ?? "0";
                    marker.StatusChannels.Add(new MarkerChannel
                    {
                        CnlNum = int.Parse(stCnlStr),
                        Alias = stNode.InnerText?.Trim() ?? ""
                    });
                }
            }

            // parse ExtraChannels > Group > ExtraItem elements (secondary popup channels)
            XmlNode extrasNode = node.SelectSingleNode("ExtraChannels");
            if (extrasNode != null)
            {
                foreach (XmlNode grpNode in extrasNode.SelectNodes("Group"))
                {
                    MarkerChannelGroup grp = new()
                    {
                        Name = grpNode.Attributes?["name"]?.Value ?? ""
                    };
                    foreach (XmlNode exNode in grpNode.SelectNodes("ExtraItem"))
                    {
                        grp.Channels.Add(new MarkerChannel
                        {
                            CnlNum = int.Parse(exNode.Attributes?["cnlNum"]?.Value ?? "0"),
                            Alias = exNode.InnerText?.Trim() ?? ""
                        });
                    }
                    marker.ExtraChannelGroups.Add(grp);
                }

                // Also handle ExtraItem/DataItem elements directly under ExtraChannels (no Group wrapper)
                MarkerChannelGroup defaultGrp = new() { Name = "" };
                foreach (XmlNode exNode in extrasNode.ChildNodes)
                {
                    if (exNode.NodeType != XmlNodeType.Element) continue;
                    if (exNode.Name == "Group") continue; // already processed above

                    int cnlNum = int.TryParse(
                        exNode.Attributes?["cnlNum"]?.Value ?? exNode.Attributes?["num"]?.Value ?? "0",
                        out int n) ? n : 0;
                    string alias = exNode.Attributes?["alias"]?.Value
                        ?? exNode.InnerText?.Trim() ?? "";

                    if (cnlNum > 0)
                        defaultGrp.Channels.Add(new MarkerChannel { CnlNum = cnlNum, Alias = alias });
                }
                if (defaultGrp.Channels.Count > 0)
                    marker.ExtraChannelGroups.Add(defaultGrp);
            }

            // also register the StatusCnlNum as a channel if specified
            if (marker.StatusCnlNum > 0)
            {
                bool alreadyExists = marker.Channels.Any(c => c.CnlNum == marker.StatusCnlNum);
                if (!alreadyExists)
                {
                    marker.Channels.Insert(0, new MarkerChannel
                    {
                        CnlNum = marker.StatusCnlNum,
                        Alias = PluginPhrases.Status
                    });
                }
            }

            return marker;
        }
    }
}
