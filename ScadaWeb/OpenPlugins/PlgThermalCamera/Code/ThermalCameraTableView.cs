// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Data.Entities;
using Scada.Web.Plugins.PlgThermalCamera.Models;
using System.Xml;

namespace Scada.Web.Plugins.PlgThermalCamera.Code
{
    public class ThermalCameraTableView : ViewBase
    {
        public ThermalCameraTableView(View viewEntity) : base(viewEntity)
        {
            // If no .map file path is specified, skip server-side file loading
            StoredOnServer = !string.IsNullOrEmpty(viewEntity.Path);
        }

        public List<ThermalCameraItem> Items { get; } = [];

        public override void LoadView(Stream stream)
        {
            XmlDocument xmlDoc = new();
            xmlDoc.Load(stream);
            Items.Clear();

            XmlNodeList locationNodes = xmlDoc.SelectNodes("//Location");
            if (locationNodes == null)
                return;

            foreach (XmlNode locationNode in locationNodes)
            {
                ThermalCameraItem item = ThermalCameraItem.ParseFromMapLocation(locationNode);
                if (item != null)
                {
                    Items.Add(item);

                    foreach (int cnlNum in item.GetAllCnlNums())
                    {
                        AddCnlNum(cnlNum);
                    }
                }
            }
        }
    }
}
