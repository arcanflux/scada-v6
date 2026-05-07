// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Client;
using Scada.Data.Models;
using Scada.Web.Plugins.PlgThermalCamera.Models;
using Scada.Web.Services;

namespace Scada.Web.Plugins.PlgThermalCamera.Code
{
    /// <summary>
    /// Per-plugin singleton that owns chat history and user-editable data.
    /// <para>Синглтон плагина, хранящий историю чата и пользовательские данные.</para>
    /// </summary>
    public class ThermalCameraContext(IWebContext webContext)
    {
        /// <summary>
        /// Hard upper limit on how many chat messages are kept per TK when
        /// <see cref="EnableMessageLimit"/> is enabled.
        /// </summary>
        public const int MaxMessagesPerItem = 1000;

        /// <summary>
        /// Set to <c>false</c> to retain the entire chat history without trimming.
        /// </summary>
        public const bool EnableMessageLimit = true;

        // MinuteArchive bit — matches the archiveBit used by the flood-history JS endpoint.
        private const int MinuteArchiveBit = 1;

        // Expanding scan windows (hours): 1h → 6h → 1d → 7d → 30d.
        private static readonly int[] ScanWindowHours = [1, 6, 24, 168, 720];

        // Stored in *ArchiveStartMs when the state has been active for more than 30 days
        // and no transition point was found in the full 30-day archive window.
        // Value 1L (1 ms after Unix epoch) is clearly not a real flood-start timestamp.
        private const long MoreThan30DaysSentinel = 1L;

        private readonly object lockObj = new();
        private ThermalCameraUserData cache;
        private readonly Dictionary<int, bool> lastFlood200State = [];
        private readonly Dictionary<int, bool> lastFlood700State = [];
        private readonly HashSet<(int itemId, string kind)> scansInProgress = [];

        public string GetUserDataFilePath()
        {
            return Path.Combine(webContext.AppDirs.StorageDir, "PlgThermalCamera", "UserData.xml");
        }

        /// <summary>
        /// Loads (and caches) the full user-data document.
        /// </summary>
        public ThermalCameraUserData LoadUserData()
        {
            lock (lockObj)
            {
                if (cache != null)
                    return cache;

                ThermalCameraUserData userData = new();
                string fileName = GetUserDataFilePath();

                if (!userData.Load(fileName, out string errMsg))
                    webContext.Log.WriteError("PlgThermalCamera: " + errMsg);

                cache = userData;
                return cache;
            }
        }

        /// <summary>
        /// Persists user data to disk (called after every mutating chat operation).
        /// </summary>
        public bool SaveUserData(ThermalCameraUserData userData, out string errMsg)
        {
            lock (lockObj)
            {
                cache = userData;
                string fileName = GetUserDataFilePath();
                return userData.Save(fileName, out errMsg);
            }
        }

        private static UserDataEntry GetOrCreateEntry(ThermalCameraUserData userData, int itemId)
        {
            if (!userData.Entries.TryGetValue(itemId, out UserDataEntry entry))
            {
                entry = new UserDataEntry();
                userData.Entries[itemId] = entry;
            }
            return entry;
        }

        private static void TrimMessages(UserDataEntry entry)
        {
#pragma warning disable CS0162 // Unreachable code detected
            if (!EnableMessageLimit)
                return;
            int excess = entry.Messages.Count - MaxMessagesPerItem;
            if (excess > 0)
                entry.Messages.RemoveRange(0, excess);
#pragma warning restore CS0162
        }

        /// <summary>
        /// Appends a user chat message and persists the store.
        /// </summary>
        public ChatMessage AddUserMessage(int itemId, string author, string text, out string errMsg)
        {
            lock (lockObj)
            {
                ThermalCameraUserData userData = LoadUserData();
                UserDataEntry entry = GetOrCreateEntry(userData, itemId);

                ChatMessage msg = new()
                {
                    Id = ++userData.LastMessageId,
                    TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Author = author ?? "",
                    Text = text ?? "",
                    Kind = ChatMessageKind.User
                };
                entry.Messages.Add(msg);
                TrimMessages(entry);

                return SaveUserData(userData, out errMsg) ? msg : null;
            }
        }

        /// <summary>
        /// Appends a system chat message (used for flood event notifications) and persists.
        /// </summary>
        public ChatMessage AddSystemMessage(int itemId, string text, string kind, string author = "Система")
        {
            lock (lockObj)
            {
                ThermalCameraUserData userData = LoadUserData();
                UserDataEntry entry = GetOrCreateEntry(userData, itemId);

                ChatMessage msg = new()
                {
                    Id = ++userData.LastMessageId,
                    TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Author = string.IsNullOrEmpty(author) ? "Система" : author,
                    Text = text ?? "",
                    Kind = kind ?? ChatMessageKind.User
                };
                entry.Messages.Add(msg);
                TrimMessages(entry);

                if (!SaveUserData(userData, out string errMsg))
                    webContext.Log.WriteError("PlgThermalCamera: " + errMsg);

                return msg;
            }
        }

        /// <summary>
        /// Removes a user chat message. System messages (flood events) cannot be deleted.
        /// </summary>
        public bool DeleteMessage(int itemId, long messageId, out string errMsg)
        {
            lock (lockObj)
            {
                ThermalCameraUserData userData = LoadUserData();
                if (!userData.Entries.TryGetValue(itemId, out UserDataEntry entry))
                {
                    errMsg = "Объект ТК не найден";
                    return false;
                }

                int idx = entry.Messages.FindIndex(m => m.Id == messageId);
                if (idx < 0)
                {
                    errMsg = "Сообщение не найдено";
                    return false;
                }

                ChatMessage msg = entry.Messages[idx];
                if (msg.Kind != ChatMessageKind.User)
                {
                    errMsg = "Системные сообщения нельзя удалять";
                    return false;
                }

                entry.Messages.RemoveAt(idx);
                return SaveUserData(userData, out errMsg);
            }
        }

        /// <summary>
        /// Returns incremental chat updates since the given cursor.
        /// </summary>
        public ChatSyncResult GetChatUpdates(long sinceMessageId)
        {
            lock (lockObj)
            {
                ThermalCameraUserData userData = LoadUserData();
                List<ChatUpdateItem> added = [];
                long maxId = sinceMessageId;

                foreach (KeyValuePair<int, UserDataEntry> kvp in userData.Entries)
                {
                    foreach (ChatMessage msg in kvp.Value.Messages)
                    {
                        if (msg.Id > sinceMessageId)
                            added.Add(new ChatUpdateItem { ItemId = kvp.Key, Message = msg });
                        if (msg.Id > maxId)
                            maxId = msg.Id;
                    }
                }

                added.Sort((a, b) => a.Message.Id.CompareTo(b.Message.Id));

                return new ChatSyncResult
                {
                    Cursor = Math.Max(maxId, userData.LastMessageId),
                    Messages = added
                };
            }
        }

        /// <summary>
        /// Returns the current full chat history for a single item.
        /// </summary>
        public List<ChatMessage> GetHistory(int itemId)
        {
            lock (lockObj)
            {
                ThermalCameraUserData userData = LoadUserData();
                if (userData.Entries.TryGetValue(itemId, out UserDataEntry entry))
                    return [.. entry.Messages];
                return [];
            }
        }

        /// <summary>
        /// Compares the latest flood-state snapshot against the previous one, fires chat
        /// notifications for new transitions, and triggers background archive scans to find
        /// the exact start timestamp of each active state.
        /// Timer values are persisted in the SCADA minute archive (not in plugin-managed
        /// timestamps) so they survive server restarts without resetting.
        /// </summary>
        public void DetectFloodTransitions(
            IEnumerable<ThermalCameraItem> items,
            Dictionary<int, FloodStateSnapshot> currentStates,
            ScadaClient scadaClient)
        {
            if (items == null || currentStates == null || scadaClient == null)
                return;

            lock (lockObj)
            {
                ThermalCameraUserData userData = LoadUserData();
                bool dirty = false;

                foreach (ThermalCameraItem item in items)
                {
                    if (!currentStates.TryGetValue(item.Id, out FloodStateSnapshot cur))
                        continue;

                    UserDataEntry entry = GetOrCreateEntry(userData, item.Id);

                    // Offline timer
                    if (cur.OnlineHasValue)
                    {
                        if (!cur.IsOnline)
                        {
                            if (entry.OfflineArchiveStartMs == 0 && item.OnlineCnlNum > 0)
                                TriggerArchiveScan(item.Id, item.OnlineCnlNum, "offline", scadaClient);
                        }
                        else if (entry.OfflineArchiveStartMs != 0)
                        {
                            entry.OfflineArchiveStartMs = 0;
                            dirty = true;
                        }
                    }

                    // 200mm flood
                    if (cur.Flood200HasValue)
                    {
                        bool prevKnown = lastFlood200State.TryGetValue(item.Id, out bool prev);
                        if (cur.Flood200)
                        {
                            if (!prev && prevKnown)
                            {
                                AddSystemMessage(item.Id,
                                    "Зафиксировано затопление 200мм — " +
                                    (string.IsNullOrEmpty(item.Name) ? "объект ТК" : item.Name),
                                    ChatMessageKind.Flood200);
                            }
                            if (entry.Flood200ArchiveStartMs == 0 && item.Flood200CnlNum > 0)
                                TriggerArchiveScan(item.Id, item.Flood200CnlNum, "flood200", scadaClient);
                            lastFlood200State[item.Id] = true;
                        }
                        else
                        {
                            if (entry.Flood200ArchiveStartMs != 0)
                            {
                                entry.Flood200ArchiveStartMs = 0;
                                dirty = true;
                            }
                            lastFlood200State[item.Id] = false;
                        }
                    }

                    // 700mm flood
                    if (cur.Flood700HasValue)
                    {
                        bool prevKnown = lastFlood700State.TryGetValue(item.Id, out bool prev);
                        if (cur.Flood700)
                        {
                            if (!prev && prevKnown)
                            {
                                AddSystemMessage(item.Id,
                                    "ТРЕВОГА: затопление 700мм — " +
                                    (string.IsNullOrEmpty(item.Name) ? "объект ТК" : item.Name),
                                    ChatMessageKind.Flood700);
                            }
                            if (entry.Flood700ArchiveStartMs == 0 && item.Flood700CnlNum > 0)
                                TriggerArchiveScan(item.Id, item.Flood700CnlNum, "flood700", scadaClient);
                            lastFlood700State[item.Id] = true;
                        }
                        else
                        {
                            if (entry.Flood700ArchiveStartMs != 0)
                            {
                                entry.Flood700ArchiveStartMs = 0;
                                dirty = true;
                            }
                            lastFlood700State[item.Id] = false;
                        }
                    }
                }

                if (dirty)
                {
                    if (!SaveUserData(userData, out string errMsg))
                        webContext.Log.WriteError("PlgThermalCamera: " + errMsg);
                }
            }
        }

        // Must be called inside lockObj.
        private void TriggerArchiveScan(int itemId, int cnlNum, string kind, ScadaClient client)
        {
            var key = (itemId, kind);
            if (!scansInProgress.Add(key))
                return; // scan already running for this item+kind
            Task.Run(() => DoArchiveScan(itemId, cnlNum, kind, client, key));
        }

        private void DoArchiveScan(int itemId, int cnlNum, string kind, ScadaClient client, (int, string) key)
        {
            long archiveStartMs = 0;
            try
            {
                archiveStartMs = kind == "offline"
                    ? ScanOfflineStartMs(client, cnlNum)
                    : ScanFloodStartMs(client, cnlNum);
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(
                    $"PlgThermalCamera: archive scan failed for item {itemId}/{kind}: {ex.Message}");
            }

            lock (lockObj)
            {
                scansInProgress.Remove(key);

                // archiveStartMs == 0 means flood was not yet committed to archive — retry next poll.
                if (archiveStartMs == 0)
                    return;

                ThermalCameraUserData userData = LoadUserData();
                UserDataEntry entry = GetOrCreateEntry(userData, itemId);
                bool updated = false;

                switch (kind)
                {
                    case "offline" when entry.OfflineArchiveStartMs == 0:
                        entry.OfflineArchiveStartMs = archiveStartMs;
                        updated = true;
                        break;
                    case "flood200" when entry.Flood200ArchiveStartMs == 0:
                        entry.Flood200ArchiveStartMs = archiveStartMs;
                        updated = true;
                        break;
                    case "flood700" when entry.Flood700ArchiveStartMs == 0:
                        // Variant B migration: align existing ack records to the archive-derived start.
                        if (archiveStartMs != MoreThan30DaysSentinel)
                        {
                            foreach (AckRecord ack in entry.AckHistory)
                            {
                                if (ack.AckedAtMs >= archiveStartMs && ack.FloodStartMs != archiveStartMs)
                                    ack.FloodStartMs = archiveStartMs;
                            }
                        }
                        entry.Flood700ArchiveStartMs = archiveStartMs;
                        updated = true;
                        break;
                }

                if (updated)
                {
                    if (!SaveUserData(userData, out string saveErr))
                        webContext.Log.WriteError("PlgThermalCamera: " + saveErr);
                }
            }
        }

        // Scans the minute archive backwards (expanding windows) to find when flooding began.
        // Returns the UTC ms of the first flooded reading, 0 if flood not yet archived, or
        // MoreThan30DaysSentinel if no transition was found within 30 days.
        private static long ScanFloodStartMs(ScadaClient client, int cnlNum)
        {
            DateTime now = DateTime.UtcNow;
            foreach (int hours in ScanWindowHours)
            {
                Trend trend = client.GetTrend(
                    MinuteArchiveBit,
                    new TimeRange(now.AddHours(-hours), now, true),
                    cnlNum);

                if (trend == null || trend.Points.Count == 0)
                    continue;

                // Find the LAST point that was NOT flooded (Stat > 0 && Val != 0).
                int lastNormalIdx = -1;
                for (int i = trend.Points.Count - 1; i >= 0; i--)
                {
                    TrendPoint p = trend.Points[i];
                    if (p.Stat > 0 && p.Val != 0)
                    {
                        lastNormalIdx = i;
                        break;
                    }
                }

                if (lastNormalIdx >= 0)
                {
                    int floodIdx = lastNormalIdx + 1;
                    if (floodIdx < trend.Points.Count)
                    {
                        // Flood started at this archive point.
                        DateTime ts = trend.Points[floodIdx].Timestamp; // already UTC
                        return new DateTimeOffset(ts, TimeSpan.Zero).ToUnixTimeMilliseconds();
                    }
                    // Most recent archived point is "normal" — flood not yet committed to archive.
                    return 0;
                }

                // All points in window are flooded — try a wider window.
                if (hours == ScanWindowHours[^1])
                    return MoreThan30DaysSentinel;
            }

            return MoreThan30DaysSentinel;
        }

        // Scans the minute archive to find the last time the device was online (last heartbeat).
        // The offline timer counts from that point forward.
        private static long ScanOfflineStartMs(ScadaClient client, int cnlNum)
        {
            DateTime now = DateTime.UtcNow;
            foreach (int hours in ScanWindowHours)
            {
                Trend trend = client.GetTrend(
                    MinuteArchiveBit,
                    new TimeRange(now.AddHours(-hours), now, true),
                    cnlNum);

                if (trend == null || trend.Points.Count == 0)
                    continue;

                // Find the LAST "online" point (Stat > 0 && Val != 0).
                for (int i = trend.Points.Count - 1; i >= 0; i--)
                {
                    TrendPoint p = trend.Points[i];
                    if (p.Stat > 0 && p.Val != 0)
                    {
                        // Offline started after this last heartbeat — use its timestamp.
                        DateTime ts = p.Timestamp; // already UTC
                        return new DateTimeOffset(ts, TimeSpan.Zero).ToUnixTimeMilliseconds();
                    }
                }

                // No online point in window — expand.
                if (hours == ScanWindowHours[^1])
                    return MoreThan30DaysSentinel;
            }

            return MoreThan30DaysSentinel;
        }

        /// <summary>
        /// Returns per-item timer start timestamps (UTC ms) for Offline, 200mm, 700mm.
        /// Special values: 0 = state not active; -1 = archive scan in progress; -2 = active > 30 days.
        /// </summary>
        public Dictionary<int, ItemTimers> GetTimers(IEnumerable<int> itemIds)
        {
            lock (lockObj)
            {
                ThermalCameraUserData userData = LoadUserData();
                var result = new Dictionary<int, ItemTimers>();
                foreach (int id in itemIds)
                {
                    if (userData.Entries.TryGetValue(id, out UserDataEntry e))
                    {
                        result[id] = new ItemTimers
                        {
                            OfflineStartMs = MapTimerValue(e.OfflineArchiveStartMs, id, "offline"),
                            Flood200StartMs = MapTimerValue(e.Flood200ArchiveStartMs, id, "flood200"),
                            Flood700StartMs = MapTimerValue(e.Flood700ArchiveStartMs, id, "flood700")
                        };
                    }
                    else
                    {
                        result[id] = new ItemTimers
                        {
                            OfflineStartMs = scansInProgress.Contains((id, "offline")) ? -1 : 0,
                            Flood200StartMs = scansInProgress.Contains((id, "flood200")) ? -1 : 0,
                            Flood700StartMs = scansInProgress.Contains((id, "flood700")) ? -1 : 0
                        };
                    }
                }
                return result;
            }
        }

        private long MapTimerValue(long storedMs, int itemId, string kind)
        {
            if (storedMs == MoreThan30DaysSentinel)
                return -2; // "> 30d" sentinel for JS
            if (storedMs == 0 && scansInProgress.Contains((itemId, kind)))
                return -1; // scan in progress
            return storedMs;
        }

        private static string FormatDuration(long ms)
        {
            if (ms <= 0) return "?";
            var ts = TimeSpan.FromMilliseconds(ms);
            if (ts.TotalDays >= 1)
                return $"{(int)ts.TotalDays}д {ts.Hours:D2}:{ts.Minutes:D2}";
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        /// <summary>
        /// Records a 700mm flood acknowledgment with a mandatory comment.
        /// Also appends a green system message to the TK's chat showing the response time.
        /// </summary>
        public AckRecord AddAcknowledgment(int itemId, string itemName, long floodStartMs,
            string ackedBy, string comment, out string errMsg)
        {
            lock (lockObj)
            {
                ThermalCameraUserData userData = LoadUserData();
                UserDataEntry entry = GetOrCreateEntry(userData, itemId);

                AckRecord rec = new()
                {
                    Id = ++userData.LastMessageId,
                    ItemId = itemId,
                    ItemName = itemName ?? "",
                    FloodStartMs = floodStartMs,
                    AckedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    AckedBy = ackedBy ?? "",
                    Comment = comment ?? ""
                };
                entry.AckHistory.Add(rec);

                if (!SaveUserData(userData, out errMsg))
                    return null;

                string duration = floodStartMs > 0 ? FormatDuration(rec.AckedAtMs - floodStartMs) : "?";
                string sysText = $"Квитировано (время реагирования: {duration})\n{comment}";
                AddSystemMessage(itemId, sysText, ChatMessageKind.Ack, ackedBy);

                return rec;
            }
        }

        /// <summary>
        /// Returns all acknowledgment records across all items, sorted newest first.
        /// </summary>
        public List<AckRecord> GetAllAcks()
        {
            lock (lockObj)
            {
                ThermalCameraUserData userData = LoadUserData();
                List<AckRecord> all = [];
                foreach (UserDataEntry e in userData.Entries.Values)
                    all.AddRange(e.AckHistory);
                all.Sort((a, b) => b.AckedAtMs.CompareTo(a.AckedAtMs));
                return all;
            }
        }
    }

    /// <summary>
    /// Per-item flood and online flags for the current polling tick.
    /// </summary>
    public class FloodStateSnapshot
    {
        public bool Flood200 { get; set; }
        public bool Flood200HasValue { get; set; }
        public bool Flood700 { get; set; }
        public bool Flood700HasValue { get; set; }
        public bool IsOnline { get; set; }
        public bool OnlineHasValue { get; set; }
    }

    /// <summary>
    /// Per-item timer start timestamps returned to the client on every poll.
    /// 0 = not active; -1 = scan in progress; -2 = active more than 30 days.
    /// </summary>
    public class ItemTimers
    {
        public long OfflineStartMs { get; set; }
        public long Flood200StartMs { get; set; }
        public long Flood700StartMs { get; set; }
    }

    /// <summary>
    /// A single chat message delivery wrapped with its owning item ID.
    /// </summary>
    public class ChatUpdateItem
    {
        public int ItemId { get; set; }
        public ChatMessage Message { get; set; }
    }

    /// <summary>
    /// Result of a chat incremental sync call.
    /// </summary>
    public class ChatSyncResult
    {
        public long Cursor { get; set; }
        public List<ChatUpdateItem> Messages { get; set; } = [];
    }
}
