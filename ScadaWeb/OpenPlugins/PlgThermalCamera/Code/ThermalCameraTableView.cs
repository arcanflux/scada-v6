// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Data.Entities;
using Scada.Data.Models;
using Scada.Web.Plugins.PlgThermalCamera.Models;
using System.Xml;

namespace Scada.Web.Plugins.PlgThermalCamera.Code
{
    /// <summary>
    /// Represents a thermal camera table view that reads data from a .map file.
    /// <para>Представляет таблицу тепловых камер, данные берутся из файла .map.</para>
    /// </summary>
    public class ThermalCameraTableView : ViewBase
    {
        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public ThermalCameraTableView(View viewEntity)
            : base(viewEntity)
        {
            Items = new List<ThermalCameraItem>();
        }

        /// <summary>
        /// Gets the parsed thermal camera items (Triangle locations from .map).
        /// </summary>
        public List<ThermalCameraItem> Items { get; }

        /// <summary>
        /// Loads the view from a .map XML stream.
        /// Extracts Location elements with Type=Triangle.
        /// </summary>
        public override void LoadView(Stream stream)
        {
            XmlDocument xmlDoc = new();
            xmlDoc.Load(stream);
            Items.Clear();

            // Select all Location nodes from any LayerGroup
            XmlNodeList locationNodes = xmlDoc.SelectNodes("//Location");
            if (locationNodes == null)
                return;

            foreach (XmlNode locationNode in locationNodes)
            {
                ThermalCameraItem item = ThermalCameraItem.ParseFromMapLocation(locationNode);
                if (item != null)
                {
                    Items.Add(item);

                    // Register channel numbers for real-time data updates
                    foreach (int cnlNum in item.GetAllCnlNums())
                    {
                        AddCnlNum(cnlNum);
                    }
                }
            }
        }
    }
}
