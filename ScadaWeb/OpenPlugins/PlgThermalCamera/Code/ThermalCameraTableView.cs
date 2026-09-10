// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Data.Entities;
using Scada.Data.Models;
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

            // Стабильные ID вычисляются из имён ТК. Дубликаты имён (или крайне
            // маловероятная коллизия хэша) детерминированно разводятся инкрементом
            // в порядке следования по файлу .map.
            HashSet<int> usedIds = [];

            foreach (XmlNode locationNode in locationNodes)
            {
                ThermalCameraItem item = ThermalCameraItem.ParseFromMapLocation(locationNode);
                if (item != null)
                {
                    while (!usedIds.Add(item.Id))
                    {
                        item.Id = item.Id >= int.MaxValue
                            ? ThermalCameraItem.StableIdFloor
                            : item.Id + 1;
                    }

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
