// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace Scada.Admin.Extensions.ExtCommConfig.Code
{
    /// <summary>
    /// Stores the folders of communication lines used only by the Administrator.
    /// <para>Хранит папки линий связи, используемые только Администратором.</para>
    /// </summary>
    /// <remarks>
    /// Persisted next to ScadaCommConfig.xml. The Communicator service ignores this file,
    /// so grouping is an Administrator-only organizational layer.
    /// </remarks>
    internal class LineGroupConfig
    {
        /// <summary>
        /// The default file name.
        /// </summary>
        public const string DefaultFileName = "CommLineGroups.xml";


        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public LineGroupConfig()
        {
            Folders = new List<string>();
            LineFolders = new Dictionary<int, string>();
        }


        /// <summary>
        /// Gets the ordered folder names, including empty folders.
        /// </summary>
        public List<string> Folders { get; }

        /// <summary>
        /// Gets the folder name of a line accessed by line number.
        /// </summary>
        public Dictionary<int, string> LineFolders { get; }


        /// <summary>
        /// Gets the folder name assigned to the specified line, or an empty string.
        /// </summary>
        public string GetFolder(int commLineNum)
        {
            return LineFolders.TryGetValue(commLineNum, out string folder) ? folder : "";
        }

        /// <summary>
        /// Assigns the line to the specified folder, or to the root if the folder is empty.
        /// </summary>
        public void SetFolder(int commLineNum, string folder)
        {
            if (string.IsNullOrEmpty(folder))
                LineFolders.Remove(commLineNum);
            else
                LineFolders[commLineNum] = folder;
        }

        /// <summary>
        /// Adds a folder if it does not exist yet.
        /// </summary>
        public bool AddFolder(string folder)
        {
            if (!string.IsNullOrWhiteSpace(folder) && !Folders.Contains(folder))
            {
                Folders.Add(folder);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the path of the folder configuration file.
        /// </summary>
        public static string GetFilePath(string configDir)
        {
            return Path.Combine(configDir, DefaultFileName);
        }

        /// <summary>
        /// Loads the folder configuration, returning an empty configuration if the file is missing or invalid.
        /// </summary>
        public static LineGroupConfig Load(string configDir)
        {
            LineGroupConfig config = new();

            try
            {
                string path = GetFilePath(configDir);

                if (string.IsNullOrEmpty(configDir) || !File.Exists(path))
                    return config;

                XmlDocument xmlDoc = new();
                xmlDoc.Load(path);
                XmlElement rootElem = xmlDoc.DocumentElement;

                if (rootElem.SelectSingleNode("Folders") is XmlNode foldersNode)
                {
                    foreach (XmlElement folderElem in foldersNode.SelectNodes("Folder"))
                        config.AddFolder(folderElem.GetAttribute("name"));
                }

                if (rootElem.SelectSingleNode("Lines") is XmlNode linesNode)
                {
                    foreach (XmlElement lineElem in linesNode.SelectNodes("Line"))
                    {
                        string folder = lineElem.GetAttribute("folder");

                        if (int.TryParse(lineElem.GetAttribute("number"), out int commLineNum) &&
                            commLineNum > 0 && !string.IsNullOrEmpty(folder))
                        {
                            config.LineFolders[commLineNum] = folder;
                        }
                    }
                }
            }
            catch
            {
                // ignore corrupt metadata - grouping is non-critical
            }

            return config;
        }

        /// <summary>
        /// Saves the folder configuration.
        /// </summary>
        public bool Save(string configDir, out string errMsg)
        {
            try
            {
                Directory.CreateDirectory(configDir);

                XmlDocument xmlDoc = new();
                xmlDoc.AppendChild(xmlDoc.CreateXmlDeclaration("1.0", "utf-8", null));
                XmlElement rootElem = xmlDoc.CreateElement("CommLineGroups");
                xmlDoc.AppendChild(rootElem);

                XmlElement foldersElem = (XmlElement)rootElem.AppendChild(xmlDoc.CreateElement("Folders"));
                foreach (string folder in Folders)
                {
                    XmlElement folderElem = (XmlElement)foldersElem.AppendChild(xmlDoc.CreateElement("Folder"));
                    folderElem.SetAttribute("name", folder);
                }

                XmlElement linesElem = (XmlElement)rootElem.AppendChild(xmlDoc.CreateElement("Lines"));
                foreach (KeyValuePair<int, string> pair in LineFolders)
                {
                    if (!string.IsNullOrEmpty(pair.Value))
                    {
                        XmlElement lineElem = (XmlElement)linesElem.AppendChild(xmlDoc.CreateElement("Line"));
                        lineElem.SetAttribute("number", pair.Key.ToString());
                        lineElem.SetAttribute("folder", pair.Value);
                    }
                }

                xmlDoc.Save(GetFilePath(configDir));
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
}
