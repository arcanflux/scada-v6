// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Data.Entities;
using Scada.Data.Models;
using Scada.Web.Plugins.PlgThermalCamera.Models;
using System.Xml;

namespace Scada.Web.Plugins.PlgThermalCamera.Code
{
    /// <summary>
    /// Represents a thermal camera table view.
    /// <para>Представляет представление таблицы тепловых камер.</para>
    /// </summary>
    public class ThermalCameraTableView : ViewBase
    {
        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public ThermalCameraTableView(View viewEntity)
            : base(viewEntity)
        {
            Config = new ThermalCameraConfig();
        }

        /// <summary>
        /// Gets the thermal camera configuration.
        /// </summary>
        public ThermalCameraConfig Config { get; }

        /// <summary>
        /// Loads the view from the specified stream.
        /// </summary>
        public override void LoadView(Stream stream)
        {
            XmlDocument xmlDoc = new();
            xmlDoc.Load(stream);
            Config.Items.Clear();

            if (xmlDoc.DocumentElement?.SelectNodes("ThermalCamera") is XmlNodeList nodes)
            {
                foreach (XmlNode node in nodes)
                {
                    ThermalCameraItem item = new();
                    item.LoadFromXml(node);
                    Config.Items.Add(item);

                    // Register channel numbers for real-time data
                    if (item.OnlineCnlNum > 0)
                        AddCnlNum(item.OnlineCnlNum);

                    foreach (FloodingSignal signal in item.FloodingSignals)
                    {
                        if (signal.StatusCnlNum > 0)
                            AddCnlNum(signal.StatusCnlNum);
                        if (signal.TemperatureCnlNum > 0)
                            AddCnlNum(signal.TemperatureCnlNum);
                    }
                }
            }
        }
    }
}
