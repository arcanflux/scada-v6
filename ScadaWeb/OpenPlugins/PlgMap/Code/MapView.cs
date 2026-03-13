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
    public class MapView : ViewBase
    {
        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public MapView(View viewEntity)
            : base(viewEntity)
        {
            Markers = new List<MapMarker>();
            MapConfig = new MapConfig();
        }


        /// <summary>
        /// Gets the list of markers on the map.
        /// </summary>
        public List<MapMarker> Markers { get; }

        /// <summary>
        /// Gets the map configuration.
        /// </summary>
        public MapConfig MapConfig { get; private set; }


        /// <summary>
        /// Loads the view from the specified stream.
        /// </summary>
        public override void LoadView(Stream stream)
        {
            XmlDocument xmlDoc = new();
            xmlDoc.Load(stream);

            XmlElement rootElem = xmlDoc.DocumentElement;
            if (!rootElem.Name.Equals("MapView", StringComparison.OrdinalIgnoreCase))
                throw new ScadaException(CommonPhrases.InvalidFileFormat);

            // load map configuration
            if (rootElem.SelectSingleNode("MapConfig") is XmlNode configNode)
            {
                MapConfig = MapConfig.LoadFromXml(configNode);
            }

            // load markers
            if (rootElem.SelectSingleNode("Markers") is XmlNode markersNode)
            {
                foreach (XmlNode markerNode in markersNode.SelectNodes("Marker"))
                {
                    MapMarker marker = MapMarker.LoadFromXml(markerNode);
                    Markers.Add(marker);

                    // register channel numbers used by this marker
                    AddCnlNum(marker.CnlNum);
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
