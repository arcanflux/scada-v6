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
        private static int idCounter = 0;

        public ThermalCameraItem()
        {
            Id = Interlocked.Increment(ref idCounter);
        }

        /// <summary>
        /// Gets the auto-generated unique identifier.
        /// </summary>
        public int Id { get; }

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
        /// Gets or sets the channel number for online status.
        /// </summary>
        public int OnlineCnlNum { get; set; }

        /// <summary>
        /// Gets or sets the channel number for battery percentage.
        /// </summary>
        public int BatteryCnlNum { get; set; }

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
        /// Gets or sets the status channel number.
        /// </summary>
        public int StatusCnlNum { get; set; }

        /// <summary>
        /// Parses a Location XML node from a .map file.
        /// Only processes nodes with Type=Triangle.
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
                PhotoUrl = GetChildText(locationNode, "PhotoUrl"),
                StatusCnlNum = GetChildInt(locationNode, "StatusCnlNum"),
                DistrictNumber = GetChildInt(locationNode, "District")
            };

            // Parse DataItem elements to identify channels by label
            XmlNode dataNode = locationNode.SelectSingleNode("Data");
            if (dataNode != null)
            {
                foreach (XmlNode dataItem in dataNode.SelectNodes("DataItem"))
                {
                    int cnlNum = 0;
                    if (dataItem.Attributes?["cnlNum"] != null)
                        int.TryParse(dataItem.Attributes["cnlNum"].Value, out cnlNum);

                    if (cnlNum <= 0)
                        continue;

                    string label = dataItem.InnerText?.Trim() ?? "";
                    string labelLower = label.ToLowerInvariant();

                    if (labelLower.Contains("затопление") && labelLower.Contains("200"))
                        item.Flood200CnlNum = cnlNum;
                    else if (labelLower.Contains("затопление") && labelLower.Contains("700"))
                        item.Flood700CnlNum = cnlNum;
                    else if ((labelLower.StartsWith("т ") || labelLower.StartsWith("т\u00a0")) && labelLower.Contains("200"))
                        item.Temp200CnlNum = cnlNum;
                    else if ((labelLower.StartsWith("т ") || labelLower.StartsWith("т\u00a0")) && labelLower.Contains("700"))
                        item.Temp700CnlNum = cnlNum;
                    else if (labelLower.Contains("онлайн") || labelLower.Contains("online"))
                        item.OnlineCnlNum = cnlNum;
                    else if (labelLower.Contains("батарея") || labelLower.Contains("battery"))
                        item.BatteryCnlNum = cnlNum;
                }
            }

            return item;
        }

        /// <summary>
        /// Collects all channel numbers used by this item.
        /// </summary>
        public List<int> GetAllCnlNums()
        {
            List<int> cnlNums = new();
            if (OnlineCnlNum > 0) cnlNums.Add(OnlineCnlNum);
            if (BatteryCnlNum > 0) cnlNums.Add(BatteryCnlNum);
            if (Temp200CnlNum > 0) cnlNums.Add(Temp200CnlNum);
            if (Temp700CnlNum > 0) cnlNums.Add(Temp700CnlNum);
            if (Flood200CnlNum > 0) cnlNums.Add(Flood200CnlNum);
            if (Flood700CnlNum > 0) cnlNums.Add(Flood700CnlNum);
            if (StatusCnlNum > 0) cnlNums.Add(StatusCnlNum);
            return cnlNums;
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
