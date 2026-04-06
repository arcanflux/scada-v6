// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Scada.Web.Plugins.PlgMap.Code
{
    /// <summary>
    /// Represents data returned by the map API for rendering markers.
    /// <para>Представляет данные, возвращаемые API карты для отображения маркеров.</para>
    /// </summary>
    public class MapDataPacket
    {
        /// <summary>
        /// Gets or sets the view stamp.
        /// </summary>
        public long ViewStamp { get; set; }

        /// <summary>
        /// Gets or sets the map configuration.
        /// </summary>
        public MapConfigDto Config { get; set; }

        /// <summary>
        /// Gets or sets the marker data.
        /// </summary>
        public List<MarkerDto> Markers { get; set; }
    }

    /// <summary>
    /// Represents the map configuration for the client.
    /// </summary>
    public class MapConfigDto
    {
        public double CenterLat { get; set; }
        public double CenterLng { get; set; }
        public int Zoom { get; set; }
        public int MinZoom { get; set; }
        public int MaxZoom { get; set; }
        public string TileUrlTemplate { get; set; }
    }

    /// <summary>
    /// Represents a channel reference inside a marker DTO.
    /// </summary>
    public class ChannelDto
    {
        public int CnlNum { get; set; }
        public string Alias { get; set; }
    }

    /// <summary>
    /// Represents a marker with its channel list for the client.
    /// </summary>
    public class MarkerDto
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public double Lat { get; set; }
        public double Lng { get; set; }
        public int LatCnlNum { get; set; }
        public int LonCnlNum { get; set; }
        public string Name { get; set; }
        public string Descr { get; set; }
        public int StatusCnlNum { get; set; }
        public int LinkViewID { get; set; }
        public List<ChannelDto> Channels { get; set; }
        public List<ChannelDto> StatusChannels { get; set; }
        public List<ChannelGroupDto> ExtraChannelGroups { get; set; }
    }

    /// <summary>
    /// Represents a named group of channels for the client.
    /// </summary>
    public class ChannelGroupDto
    {
        public string Name { get; set; }
        public List<ChannelDto> Channels { get; set; }
    }

    /// <summary>
    /// Represents current data for markers on the map.
    /// </summary>
    public class MarkerCurData
    {
        public List<MarkerCurDataRecord> Records { get; set; }
    }

    /// <summary>
    /// Represents a single channel's current data record.
    /// </summary>
    public class MarkerCurDataRecord
    {
        public int CnlNum { get; set; }
        public double Val { get; set; }
        public int Stat { get; set; }
        public string Text { get; set; }
    }
}
