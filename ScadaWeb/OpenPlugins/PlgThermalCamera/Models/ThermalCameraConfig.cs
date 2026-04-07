// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Xml;

namespace Scada.Web.Plugins.PlgThermalCamera.Models
{
    /// <summary>
    /// Represents the thermal camera plugin configuration.
    /// <para>Представляет конфигурацию плагина тепловых камер.</para>
    /// </summary>
    public class ThermalCameraConfig
    {
        /// <summary>
        /// Gets the list of thermal camera objects.
        /// </summary>
        public List<ThermalCameraItem> Items { get; set; } = new();

        /// <summary>
        /// Loads the configuration from the specified file.
        /// </summary>
        public bool Load(string fileName, out string errMsg)
        {
            try
            {
                if (!File.Exists(fileName))
                {
                    Items = new List<ThermalCameraItem>();
                    errMsg = "";
                    return true;
                }

                XmlDocument xmlDoc = new();
                xmlDoc.Load(fileName);
                Items.Clear();

                if (xmlDoc.DocumentElement?.SelectNodes("ThermalCamera") is XmlNodeList nodes)
                {
                    foreach (XmlNode node in nodes)
                    {
                        ThermalCameraItem item = new();
                        item.LoadFromXml(node);
                        Items.Add(item);
                    }
                }

                errMsg = "";
                return true;
            }
            catch (Exception ex)
            {
                errMsg = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Saves the configuration to the specified file.
        /// </summary>
        public bool Save(string fileName, out string errMsg)
        {
            try
            {
                XmlDocument xmlDoc = new();
                XmlDeclaration xmlDecl = xmlDoc.CreateXmlDeclaration("1.0", "utf-8", null);
                xmlDoc.AppendChild(xmlDecl);

                XmlElement rootElem = xmlDoc.CreateElement("ThermalCameraConfig");
                xmlDoc.AppendChild(rootElem);

                foreach (ThermalCameraItem item in Items)
                {
                    XmlElement itemElem = xmlDoc.CreateElement("ThermalCamera");
                    item.SaveToXml(itemElem);
                    rootElem.AppendChild(itemElem);
                }

                string dir = Path.GetDirectoryName(fileName);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                xmlDoc.Save(fileName);
                errMsg = "";
                return true;
            }
            catch (Exception ex)
            {
                errMsg = ex.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// Represents a thermal camera item (ТК object).
    /// <para>Представляет объект тепловой камеры.</para>
    /// </summary>
    public class ThermalCameraItem
    {
        /// <summary>
        /// Gets or sets the unique identifier.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the district number for sorting.
        /// </summary>
        public int DistrictNumber { get; set; }

        /// <summary>
        /// Gets or sets the thermal camera object name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Gets or sets the real address.
        /// </summary>
        public string Address { get; set; } = "";

        /// <summary>
        /// Gets or sets the photo URL.
        /// </summary>
        public string PhotoUrl { get; set; } = "";

        /// <summary>
        /// Gets or sets the channel number for online status.
        /// </summary>
        public int OnlineCnlNum { get; set; }

        /// <summary>
        /// Gets or sets the channel numbers for flooding status signals.
        /// Each channel represents a separate flooding zone/pipe signal.
        /// </summary>
        public List<FloodingSignal> FloodingSignals { get; set; } = new();

        /// <summary>
        /// Gets or sets the comment for the object (saved in plugin).
        /// </summary>
        public string Comment { get; set; } = "";

        /// <summary>
        /// Gets or sets whether the thermal camera is commissioned.
        /// </summary>
        public bool IsCommissioned { get; set; }

        /// <summary>
        /// Loads the item from an XML node.
        /// </summary>
        public void LoadFromXml(XmlNode node)
        {
            Id = GetAttrInt(node, "id");
            DistrictNumber = GetAttrInt(node, "districtNumber");
            Name = GetAttrStr(node, "name");
            Address = GetAttrStr(node, "address");
            PhotoUrl = GetAttrStr(node, "photoUrl");
            OnlineCnlNum = GetAttrInt(node, "onlineCnlNum");
            Comment = GetAttrStr(node, "comment");
            IsCommissioned = GetAttrBool(node, "isCommissioned");

            FloodingSignals.Clear();
            if (node.SelectNodes("FloodingSignal") is XmlNodeList signalNodes)
            {
                foreach (XmlNode signalNode in signalNodes)
                {
                    FloodingSignals.Add(new FloodingSignal
                    {
                        Label = GetAttrStr(signalNode, "label"),
                        StatusCnlNum = GetAttrInt(signalNode, "statusCnlNum"),
                        TemperatureCnlNum = GetAttrInt(signalNode, "temperatureCnlNum")
                    });
                }
            }
        }

        /// <summary>
        /// Saves the item to an XML element.
        /// </summary>
        public void SaveToXml(XmlElement elem)
        {
            elem.SetAttribute("id", Id.ToString());
            elem.SetAttribute("districtNumber", DistrictNumber.ToString());
            elem.SetAttribute("name", Name);
            elem.SetAttribute("address", Address);
            elem.SetAttribute("photoUrl", PhotoUrl);
            elem.SetAttribute("onlineCnlNum", OnlineCnlNum.ToString());
            elem.SetAttribute("comment", Comment);
            elem.SetAttribute("isCommissioned", IsCommissioned.ToString().ToLowerInvariant());

            foreach (FloodingSignal signal in FloodingSignals)
            {
                XmlElement signalElem = elem.OwnerDocument.CreateElement("FloodingSignal");
                signalElem.SetAttribute("label", signal.Label);
                signalElem.SetAttribute("statusCnlNum", signal.StatusCnlNum.ToString());
                signalElem.SetAttribute("temperatureCnlNum", signal.TemperatureCnlNum.ToString());
                elem.AppendChild(signalElem);
            }
        }

        private static string GetAttrStr(XmlNode node, string name)
        {
            return node.Attributes?[name]?.Value ?? "";
        }

        private static int GetAttrInt(XmlNode node, string name)
        {
            string val = GetAttrStr(node, name);
            return int.TryParse(val, out int result) ? result : 0;
        }

        private static bool GetAttrBool(XmlNode node, string name)
        {
            string val = GetAttrStr(node, name);
            return bool.TryParse(val, out bool result) && result;
        }
    }

    /// <summary>
    /// Represents a flooding status signal with temperature.
    /// <para>Представляет сигнал состояния затопления с температурой.</para>
    /// </summary>
    public class FloodingSignal
    {
        /// <summary>
        /// Gets or sets the signal label (e.g., "Т1", "Т2", "ОТ1", "ОТ2").
        /// </summary>
        public string Label { get; set; } = "";

        /// <summary>
        /// Gets or sets the channel number for flooding status (0/1).
        /// </summary>
        public int StatusCnlNum { get; set; }

        /// <summary>
        /// Gets or sets the channel number for temperature value.
        /// </summary>
        public int TemperatureCnlNum { get; set; }
    }
}
