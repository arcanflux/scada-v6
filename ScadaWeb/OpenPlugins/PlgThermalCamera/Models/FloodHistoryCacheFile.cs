// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Text;
using System.Xml;

namespace Scada.Web.Plugins.PlgThermalCamera.Models
{
    /// <summary>
    /// On-disk persistence for the server-side flood-history cache.
    /// <para>Хранение серверного кэша истории затоплений на диске. По образцу
    /// <see cref="ThermalCameraUserData"/> — единый XML-файл в Storage/PlgThermalCamera.
    /// Позволяет кэшу пережить перезапуск веб-сервера, чтобы история открывалась
    /// мгновенно сразу после рестарта, а не прогревалась заново 1–2 минуты.</para>
    /// </summary>
    public class FloodHistoryCacheFile
    {
        /// <summary>
        /// Gets the cached flood-history records (one per item + year).
        /// </summary>
        public List<CachedFloodHistory> Entries { get; set; } = [];

        /// <summary>
        /// Loads the cache file. A missing file is treated as an empty cache.
        /// </summary>
        public bool Load(string fileName, out string errMsg)
        {
            try
            {
                Entries.Clear();

                if (!File.Exists(fileName))
                {
                    errMsg = "";
                    return true;
                }

                XmlDocument xmlDoc = new();
                xmlDoc.Load(fileName);

                XmlElement root = xmlDoc.DocumentElement;
                if (root == null)
                {
                    errMsg = "";
                    return true;
                }

                if (root.SelectNodes("Entry") is XmlNodeList nodes)
                {
                    foreach (XmlNode node in nodes)
                    {
                        int itemId = GetAttrInt(node, "itemId");
                        int year = GetAttrInt(node, "year");
                        if (itemId <= 0 || year <= 0)
                            continue;

                        CachedFloodHistory entry = new()
                        {
                            ItemId = itemId,
                            Year = year,
                            CachedAtMs = GetAttrLong(node, "cachedAtMs")
                        };

                        if (node.SelectNodes("Channel") is XmlNodeList chNodes)
                        {
                            foreach (XmlNode chNode in chNodes)
                            {
                                string kind = GetAttrStr(chNode, "kind");
                                if (string.IsNullOrEmpty(kind))
                                    continue;

                                long[][] intervals = ParseIntervals(chNode.InnerText);
                                if (intervals.Length > 0)
                                    entry.Intervals[kind] = intervals;
                            }
                        }

                        Entries.Add(entry);
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
        /// Saves the cache file atomically (write to a temp file, then replace).
        /// </summary>
        public bool Save(string fileName, out string errMsg)
        {
            try
            {
                XmlDocument xmlDoc = new();
                XmlDeclaration xmlDecl = xmlDoc.CreateXmlDeclaration("1.0", "utf-8", null);
                xmlDoc.AppendChild(xmlDecl);

                XmlElement rootElem = xmlDoc.CreateElement("ThermalCameraFloodHistoryCache");
                xmlDoc.AppendChild(rootElem);

                foreach (CachedFloodHistory entry in Entries)
                {
                    XmlElement entryElem = xmlDoc.CreateElement("Entry");
                    entryElem.SetAttribute("itemId", entry.ItemId.ToString());
                    entryElem.SetAttribute("year", entry.Year.ToString());
                    entryElem.SetAttribute("cachedAtMs", entry.CachedAtMs.ToString());

                    foreach (KeyValuePair<string, long[][]> ch in entry.Intervals)
                    {
                        XmlElement chElem = xmlDoc.CreateElement("Channel");
                        chElem.SetAttribute("kind", ch.Key);
                        chElem.AppendChild(xmlDoc.CreateTextNode(FormatIntervals(ch.Value)));
                        entryElem.AppendChild(chElem);
                    }

                    rootElem.AppendChild(entryElem);
                }

                string dir = Path.GetDirectoryName(fileName);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // Atomic write: a crash mid-save can never leave a half-written
                // (corrupt) cache file behind — the old file stays intact until
                // the fully-written temp file replaces it in one move.
                string tmpFile = fileName + ".tmp";
                xmlDoc.Save(tmpFile);
                File.Move(tmpFile, fileName, true);

                errMsg = "";
                return true;
            }
            catch (Exception ex)
            {
                errMsg = ex.Message;
                return false;
            }
        }

        // Intervals are packed as "start:end;start:end;..." inside the Channel
        // element text. This is far more compact than one XML element per interval,
        // which matters when a noisy sensor produces thousands of intervals a year.
        private static string FormatIntervals(long[][] intervals)
        {
            if (intervals == null || intervals.Length == 0)
                return "";

            StringBuilder sb = new();
            for (int i = 0; i < intervals.Length; i++)
            {
                if (intervals[i] == null || intervals[i].Length < 2)
                    continue;
                if (sb.Length > 0)
                    sb.Append(';');
                sb.Append(intervals[i][0]).Append(':').Append(intervals[i][1]);
            }
            return sb.ToString();
        }

        private static long[][] ParseIntervals(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return [];

            string[] parts = text.Split(';', StringSplitOptions.RemoveEmptyEntries);
            List<long[]> list = new(parts.Length);

            foreach (string part in parts)
            {
                int sep = part.IndexOf(':');
                if (sep <= 0)
                    continue;
                if (long.TryParse(part.AsSpan(0, sep), out long s) &&
                    long.TryParse(part.AsSpan(sep + 1), out long e) && e > s)
                {
                    list.Add([s, e]);
                }
            }
            return [.. list];
        }

        private static string GetAttrStr(XmlNode node, string name)
        {
            return node.Attributes?[name]?.Value ?? "";
        }

        private static int GetAttrInt(XmlNode node, string name)
        {
            return int.TryParse(GetAttrStr(node, name), out int result) ? result : 0;
        }

        private static long GetAttrLong(XmlNode node, string name)
        {
            return long.TryParse(GetAttrStr(node, name), out long result) ? result : 0;
        }
    }

    /// <summary>
    /// One cached flood-history record (a single item + year) as persisted to disk.
    /// <para>Одна запись кэша истории затоплений (один ТК за один год).</para>
    /// </summary>
    public class CachedFloodHistory
    {
        public int ItemId { get; set; }
        public int Year { get; set; }
        public long CachedAtMs { get; set; }
        public Dictionary<string, long[][]> Intervals { get; set; } = [];
    }
}
