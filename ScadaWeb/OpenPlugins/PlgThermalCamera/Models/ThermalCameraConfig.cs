// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Xml;

namespace Scada.Web.Plugins.PlgThermalCamera.Models
{
    /// <summary>
    /// Represents a parsed thermal camera item extracted from a .map file Location element.
    /// <para>Представляет объект ТК, извлечённый из элемента Location файла .map.</para>
    /// </summary>
    public class ThermalCameraItem
    {
        // Стабильные ID начинаются с этого значения, поэтому никогда не пересекаются
        // со старыми порядковыми ID (1..N — счётчик парсинга в прежних версиях).
        // На этом построена миграция user data со старого формата.
        public const int StableIdFloor = 100000;

        /// <summary>
        /// Gets or sets the stable identifier derived from the item name and address.
        /// <para>Стабильный идентификатор, вычисляемый из имени и адреса ТК: не зависит
        /// от порядка в .map, перезапусков ScadaWeb и перечиток представления из кэша.
        /// Адрес входит в ключ, потому что имена ТК бывают неуникальны (например,
        /// «ТК 1404» в разных районах) — различает их именно адрес.
        /// К идентификатору привязаны user data (тумблер «В работе», чат, квитирования).</para>
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Вычисляет стабильный идентификатор из имени и адреса ТК (FNV-1a 32 бит,
        /// старший бит сброшен). Результат всегда не меньше <see cref="StableIdFloor"/>.
        /// Переименование ТК или правка адреса в .map меняет идентификатор —
        /// состояние этой ТК отвяжется (начнёт с чистого листа).
        /// </summary>
        public static int GetStableId(string name, string descr)
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            uint hash = offsetBasis;

            string key = (name ?? "").Trim() + "\n" + (descr ?? "").Trim();
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(key))
            {
                hash ^= b;
                hash *= prime;
            }

            int id = (int)(hash & 0x7FFFFFFF);
            return id < StableIdFloor ? id + StableIdFloor : id;
        }

        /// <summary>
        /// Gets or sets the district number for sorting.
        /// </summary>
        public int DistrictNumber { get; set; }

        /// <summary>
        /// Gets or sets the thermal camera object name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Gets or sets the description (used as address).
        /// </summary>
        public string Descr { get; set; } = "";

        /// <summary>
        /// Gets or sets the photo URL.
        /// </summary>
        public string PhotoUrl { get; set; } = "";

        /// <summary>
        /// Gets or sets the scheme image URL.
        /// </summary>
        public string SchemeUrl { get; set; } = "";

        /// <summary>
        /// Gets or sets the channel number for online status.
        /// </summary>
        public int OnlineCnlNum { get; set; }

        /// <summary>
        /// Gets or sets the temperature channel for 200mm pipe.
        /// </summary>
        public int Temp200CnlNum { get; set; }

        /// <summary>
        /// Gets or sets the temperature channel for 700mm pipe.
        /// </summary>
        public int Temp700CnlNum { get; set; }

        /// <summary>
        /// Gets or sets the flooding status channel for 200mm pipe.
        /// </summary>
        public int Flood200CnlNum { get; set; }

        /// <summary>
        /// Gets or sets the flooding status channel for 700mm pipe.
        /// </summary>
        public int Flood700CnlNum { get; set; }

        /// <summary>
        /// Gets or sets the battery percentage channel.
        /// </summary>
        public int BatteryCnlNum { get; set; }

        /// <summary>
        /// Gets or sets the status channel number.
        /// </summary>
        public int StatusCnlNum { get; set; }

        /// <summary>
        /// Parses a Location XML node from a .map file.
        /// Only processes nodes with Type=Triangle.
        /// Classifies DataItem channels by their label text:
        ///   "Затопление 200мм" / "flood 200"        → Flood200
        ///   "Затопление 700мм" / "flood 700"        → Flood700
        ///   "Батарея %" / "battery" / "заряд"       → Battery
        ///   "Онлайн" / "online" / "связь"           → Online
        ///   any other label containing "200"        → Temp200
        ///     (e.g. "Температура 200мм", "Т 200мм", "temp 200")
        ///   any other label containing "700"        → Temp700
        ///     (e.g. "Температура 700мм", "Т 700мм", "temp 700")
        /// </summary>
        public static ThermalCameraItem ParseFromMapLocation(XmlNode locationNode)
        {
            string type = GetChildText(locationNode, "Type");
            if (!string.Equals(type, "Triangle", StringComparison.OrdinalIgnoreCase))
                return null;

            ThermalCameraItem item = new()
            {
                Name = GetChildText(locationNode, "Name"),
                Descr = GetChildText(locationNode, "Descr"),
                PhotoUrl = GetChildText(locationNode, "Photo"),
                SchemeUrl = GetChildText(locationNode, "Scheme"),
                StatusCnlNum = GetChildInt(locationNode, "StatusCnlNum"),
                DistrictNumber = GetChildInt(locationNode, "District")
            };
            item.Id = GetStableId(item.Name, item.Descr);

            // Parse DataItem elements — classify purely by label text.
            // DataTypeID is unreliable because OPC UA drivers may store both numeric
            // flooding flags and temperatures as System.Single (DataTypeID.Double).
            XmlNode dataNode = locationNode.SelectSingleNode("Data");
            if (dataNode != null)
            {
                foreach (XmlNode dataItem in dataNode.SelectNodes("DataItem"))
                {
                    if (dataItem.Attributes?["cnlNum"] == null ||
                        !int.TryParse(dataItem.Attributes["cnlNum"].Value, out int cnlNum) ||
                        cnlNum <= 0)
                        continue;

                    string label = dataItem.InnerText?.Trim() ?? "";
                    string labelLower = label.ToLowerInvariant();
                    bool is200 = labelLower.Contains("200");
                    bool is700 = labelLower.Contains("700");

                    bool isFlood = labelLower.Contains("затопл") || labelLower.Contains("flood") ||
                                   labelLower.Contains("наводн");
                    bool isBattery = labelLower.Contains("батар") || labelLower.Contains("battery") ||
                                     labelLower.Contains("заряд");
                    bool isOnline = labelLower.Contains("онлайн") || labelLower.Contains("online") ||
                                    labelLower.Contains("связь");

                    // Flooding is checked FIRST because "Затопление 200мм" also contains "200".
                    if (isFlood)
                    {
                        if (is200)
                            item.Flood200CnlNum = cnlNum;
                        else if (is700)
                            item.Flood700CnlNum = cnlNum;
                    }
                    else if (isBattery)
                    {
                        item.BatteryCnlNum = cnlNum;
                    }
                    else if (isOnline)
                    {
                        item.OnlineCnlNum = cnlNum;
                    }
                    else if (is200)
                    {
                        // Any non-flooding label with "200" — temperature of 200mm pipe.
                        item.Temp200CnlNum = cnlNum;
                    }
                    else if (is700)
                    {
                        // Any non-flooding label with "700" — temperature of 700mm pipe.
                        item.Temp700CnlNum = cnlNum;
                    }
                }
            }

            return item;
        }

        /// <summary>
        /// Collects all channel numbers used by this item.
        /// </summary>
        public List<int> GetAllCnlNums()
        {
            int[] all = [OnlineCnlNum, Temp200CnlNum, Temp700CnlNum,
                         Flood200CnlNum, Flood700CnlNum, BatteryCnlNum, StatusCnlNum];
            return [.. all.Where(n => n > 0)];
        }

        private static string GetChildText(XmlNode parent, string childName)
        {
            return parent.SelectSingleNode(childName)?.InnerText?.Trim() ?? "";
        }

        private static int GetChildInt(XmlNode parent, string childName)
        {
            string text = GetChildText(parent, childName);
            return int.TryParse(text, out int result) ? result : 0;
        }
    }
}
