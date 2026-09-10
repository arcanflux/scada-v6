// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Xml;

namespace Scada.Web.Plugins.PlgThermalCamera.Models
{
    /// <summary>
    /// Represents user-editable data stored within the plugin.
    /// <para>Представляет пользовательские данные, хранимые внутри плагина (комментарии,
    /// статус «в работе», а также историю чата по каждому объекту ТК).</para>
    /// </summary>
    public class ThermalCameraUserData
    {
        /// <summary>
        /// Gets the dictionary of item user data keyed by item ID.
        /// </summary>
        public Dictionary<int, UserDataEntry> Entries { get; set; } = [];

        /// <summary>
        /// Gets or sets the last allocated chat message ID (monotonically increasing).
        /// </summary>
        public long LastMessageId { get; set; }

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
                LastMessageId = 0;

                XmlElement root = xmlDoc.DocumentElement;
                if (root == null)
                {
                    errMsg = "";
                    return true;
                }

                string lastIdAttr = root.GetAttribute("lastMessageId");
                if (long.TryParse(lastIdAttr, out long lastId))
                    LastMessageId = lastId;

                if (root.SelectNodes("Entry") is XmlNodeList nodes)
                {
                    foreach (XmlNode node in nodes)
                    {
                        int id = GetAttrInt(node, "id");
                        if (id <= 0)
                            continue;

                        UserDataEntry entry = new()
                        {
                            Name = GetAttrStr(node, "name"),
                            Descr = GetAttrStr(node, "descr"),
                            Comment = GetAttrStr(node, "comment"),
                            IsCommissioned = GetAttrBool(node, "isCommissioned"),
                            OfflineArchiveStartMs = GetAttrLong(node, "offlineArchiveStartMs"),
                            Flood200ArchiveStartMs = GetAttrLong(node, "flood200ArchiveStartMs"),
                            Flood700ArchiveStartMs = GetAttrLong(node, "flood700ArchiveStartMs"),
                            Flood200LastClearMs = GetAttrLong(node, "flood200LastClearMs"),
                            Flood700LastClearMs = GetAttrLong(node, "flood700LastClearMs")
                        };

                        if (node.SelectNodes("Message") is XmlNodeList msgNodes)
                        {
                            foreach (XmlNode msgNode in msgNodes)
                            {
                                ChatMessage msg = new()
                                {
                                    Id = GetAttrLong(msgNode, "id"),
                                    TimestampMs = GetAttrLong(msgNode, "ts"),
                                    Author = GetAttrStr(msgNode, "author"),
                                    Kind = GetAttrStr(msgNode, "kind"),
                                    Text = msgNode.InnerText ?? ""
                                };
                                if (string.IsNullOrEmpty(msg.Kind))
                                    msg.Kind = ChatMessageKind.User;
                                entry.Messages.Add(msg);

                                if (msg.Id > LastMessageId)
                                    LastMessageId = msg.Id;
                            }
                        }

                        if (node.SelectNodes("Ack") is XmlNodeList ackNodes)
                        {
                            foreach (XmlNode ackNode in ackNodes)
                            {
                                entry.AckHistory.Add(new AckRecord
                                {
                                    Id = GetAttrLong(ackNode, "id"),
                                    ItemId = GetAttrInt(ackNode, "itemId"),
                                    ItemName = GetAttrStr(ackNode, "itemName"),
                                    FloodStartMs = GetAttrLong(ackNode, "floodStartMs"),
                                    AckedAtMs = GetAttrLong(ackNode, "ackedAtMs"),
                                    AckedBy = GetAttrStr(ackNode, "ackedBy"),
                                    Comment = ackNode.InnerText ?? ""
                                });
                            }
                        }

                        Entries[id] = entry;
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
                rootElem.SetAttribute("lastMessageId", LastMessageId.ToString());
                xmlDoc.AppendChild(rootElem);

                foreach (KeyValuePair<int, UserDataEntry> kvp in Entries)
                {
                    XmlElement entryElem = xmlDoc.CreateElement("Entry");
                    entryElem.SetAttribute("id", kvp.Key.ToString());
                    entryElem.SetAttribute("name", kvp.Value.Name ?? "");
                    entryElem.SetAttribute("descr", kvp.Value.Descr ?? "");
                    entryElem.SetAttribute("comment", kvp.Value.Comment ?? "");
                    entryElem.SetAttribute("isCommissioned",
                        kvp.Value.IsCommissioned.ToString().ToLowerInvariant());
                    entryElem.SetAttribute("offlineArchiveStartMs", kvp.Value.OfflineArchiveStartMs.ToString());
                    entryElem.SetAttribute("flood200ArchiveStartMs", kvp.Value.Flood200ArchiveStartMs.ToString());
                    entryElem.SetAttribute("flood700ArchiveStartMs", kvp.Value.Flood700ArchiveStartMs.ToString());
                    entryElem.SetAttribute("flood200LastClearMs", kvp.Value.Flood200LastClearMs.ToString());
                    entryElem.SetAttribute("flood700LastClearMs", kvp.Value.Flood700LastClearMs.ToString());

                    foreach (ChatMessage msg in kvp.Value.Messages)
                    {
                        XmlElement msgElem = xmlDoc.CreateElement("Message");
                        msgElem.SetAttribute("id", msg.Id.ToString());
                        msgElem.SetAttribute("ts", msg.TimestampMs.ToString());
                        msgElem.SetAttribute("author", msg.Author ?? "");
                        msgElem.SetAttribute("kind", msg.Kind ?? ChatMessageKind.User);
                        msgElem.AppendChild(xmlDoc.CreateTextNode(msg.Text ?? ""));
                        entryElem.AppendChild(msgElem);
                    }

                    foreach (AckRecord ack in kvp.Value.AckHistory)
                    {
                        XmlElement ackElem = xmlDoc.CreateElement("Ack");
                        ackElem.SetAttribute("id", ack.Id.ToString());
                        ackElem.SetAttribute("itemId", ack.ItemId.ToString());
                        ackElem.SetAttribute("itemName", ack.ItemName ?? "");
                        ackElem.SetAttribute("floodStartMs", ack.FloodStartMs.ToString());
                        ackElem.SetAttribute("ackedAtMs", ack.AckedAtMs.ToString());
                        ackElem.SetAttribute("ackedBy", ack.AckedBy ?? "");
                        ackElem.AppendChild(xmlDoc.CreateTextNode(ack.Comment ?? ""));
                        entryElem.AppendChild(ackElem);
                    }

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

        private static long GetAttrLong(XmlNode node, string name)
        {
            string val = GetAttrStr(node, name);
            return long.TryParse(val, out long result) ? result : 0;
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
        /// Имя ТК на момент сохранения. Служит признаком нового формата записи
        /// (пустое имя + маленький ID = запись старого формата, подлежит миграции)
        /// и делает файл читаемым для человека. Ключом является стабильный ID.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Адрес ТК на момент сохранения. Вместе с именем образует стабильный ключ
        /// (имена ТК бывают неуникальны); хранится для читаемости файла и миграций.
        /// </summary>
        public string Descr { get; set; } = "";

        /// <summary>
        /// Legacy single-comment field (still stored for backward compatibility).
        /// </summary>
        public string Comment { get; set; } = "";

        /// <summary>
        /// Gets or sets whether the thermal camera is commissioned (введено в работу).
        /// </summary>
        public bool IsCommissioned { get; set; }

        /// <summary>
        /// UTC milliseconds when the device went Offline, derived from the minute archive.
        /// 0 = device is currently online (state not active).
        /// </summary>
        public long OfflineArchiveStartMs { get; set; }

        /// <summary>
        /// UTC milliseconds when 200mm flooding started, derived from the minute archive.
        /// 0 = not flooded.
        /// </summary>
        public long Flood200ArchiveStartMs { get; set; }

        /// <summary>
        /// UTC milliseconds when 700mm flooding started, derived from the minute archive.
        /// 0 = not flooded.
        /// </summary>
        public long Flood700ArchiveStartMs { get; set; }

        /// <summary>
        /// UTC ms when 200mm flood last cleared (sensor went non-flooded).
        /// 0 = flood is currently active or the clear timer has expired.
        /// Used to implement a 24-hour debounce so brief sensor glitches don't
        /// invalidate the existing flood start timestamp and its acknowledgment.
        /// </summary>
        public long Flood200LastClearMs { get; set; }

        /// <summary>
        /// UTC ms when 700mm flood last cleared. See <see cref="Flood200LastClearMs"/>.
        /// </summary>
        public long Flood700LastClearMs { get; set; }

        /// <summary>
        /// Gets the chat message history for this item.
        /// </summary>
        public List<ChatMessage> Messages { get; set; } = [];

        /// <summary>
        /// Acknowledgment history for 700mm flood events.
        /// </summary>
        public List<AckRecord> AckHistory { get; set; } = [];
    }

    /// <summary>
    /// Acknowledgment record for a 700mm flood event.
    /// </summary>
    public class AckRecord
    {
        public long Id { get; set; }
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public long FloodStartMs { get; set; }
        public long AckedAtMs { get; set; }
        public string AckedBy { get; set; } = "";
        public string Comment { get; set; } = "";
    }

    /// <summary>
    /// Represents a single chat message.
    /// <para>Представляет сообщение чата ТК (пользовательское или системное).</para>
    /// </summary>
    public class ChatMessage
    {
        public long Id { get; set; }
        public long TimestampMs { get; set; }
        public string Author { get; set; } = "";
        public string Text { get; set; } = "";

        /// <summary>
        /// Message kind — see <see cref="ChatMessageKind"/>.
        /// </summary>
        public string Kind { get; set; } = ChatMessageKind.User;
    }

    /// <summary>
    /// Chat message kinds used by the plugin UI to style bubbles.
    /// </summary>
    public static class ChatMessageKind
    {
        /// <summary>Regular user message.</summary>
        public const string User = "user";
        /// <summary>System message for 200мм flood event (yellow).</summary>
        public const string Flood200 = "flood200";
        /// <summary>System message for 700мм flood event (red).</summary>
        public const string Flood700 = "flood700";
        /// <summary>System message confirming a 700мм flood acknowledgment (green).</summary>
        public const string Ack = "ack";
    }
}
