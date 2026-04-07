// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Xml;

namespace Scada.Web.Plugins.PlgThermalCamera.Models
{
    /// <summary>
    /// Represents user-editable data stored within the plugin (comments and commissioned status).
    /// <para>Представляет пользовательские данные, хранимые внутри плагина.</para>
    /// </summary>
    public class ThermalCameraUserData
    {
        /// <summary>
        /// Gets the dictionary of item user data keyed by item ID.
        /// </summary>
        public Dictionary<int, UserDataEntry> Entries { get; set; } = [];

        /// <summary>
        /// Loads user data from the specified file.
        /// </summary>
        public bool Load(string fileName, out string errMsg)
        {
            try
            {
                if (!File.Exists(fileName))
                {
                    errMsg = "";
                    return true;
                }

                XmlDocument xmlDoc = new();
                xmlDoc.Load(fileName);
                Entries.Clear();

                if (xmlDoc.DocumentElement?.SelectNodes("Entry") is XmlNodeList nodes)
                {
                    foreach (XmlNode node in nodes)
                    {
                        int id = GetAttrInt(node, "id");
                        if (id > 0)
                        {
                            Entries[id] = new UserDataEntry
                            {
                                Comment = GetAttrStr(node, "comment"),
                                IsCommissioned = GetAttrBool(node, "isCommissioned")
                            };
                        }
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
        /// Saves user data to the specified file.
        /// </summary>
        public bool Save(string fileName, out string errMsg)
        {
            try
            {
                XmlDocument xmlDoc = new();
                XmlDeclaration xmlDecl = xmlDoc.CreateXmlDeclaration("1.0", "utf-8", null);
                xmlDoc.AppendChild(xmlDecl);

                XmlElement rootElem = xmlDoc.CreateElement("ThermalCameraUserData");
                xmlDoc.AppendChild(rootElem);

                foreach (KeyValuePair<int, UserDataEntry> kvp in Entries)
                {
                    XmlElement entryElem = xmlDoc.CreateElement("Entry");
                    entryElem.SetAttribute("id", kvp.Key.ToString());
                    entryElem.SetAttribute("comment", kvp.Value.Comment);
                    entryElem.SetAttribute("isCommissioned", kvp.Value.IsCommissioned.ToString().ToLowerInvariant());
                    rootElem.AppendChild(entryElem);
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
    /// Represents a single user data entry for a thermal camera item.
    /// <para>Представляет запись пользовательских данных для объекта ТК.</para>
    /// </summary>
    public class UserDataEntry
    {
        /// <summary>
        /// Gets or sets the comment.
        /// </summary>
        public string Comment { get; set; } = "";

        /// <summary>
        /// Gets or sets whether the thermal camera is commissioned (введено в работу).
        /// </summary>
        public bool IsCommissioned { get; set; }
    }
}
