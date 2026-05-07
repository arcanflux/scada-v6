// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
        /// <see cref="EnableMessageLimit"/> is enabled. Older messages are trimmed
        /// after each new write. Flip <see cref="EnableMessageLimit"/> to false to
        /// keep the full history regardless of size.
        /// <para>Жёсткий лимит сообщений на один объект ТК. Если
        /// <see cref="EnableMessageLimit"/> = false, лимит отключается.</para>
        /// </summary>
        public const int MaxMessagesPerItem = 1000;

        /// <summary>
        /// Code-level switch that enables/disables the per-TK message limit.
        /// Set to <c>false</c> to retain the entire chat history without trimming.
        /// </summary>
        public const bool EnableMessageLimit = true;

        /// <summary>
        /// How long (in ms) the channel must continuously report "normal" before an
        /// active offline / flood timer is actually cleared. The pending-since timestamp
        /// is persisted to disk so back-to-back SCADA restarts cannot accumulate enough
        /// transient polls to bypass it.
        /// </summary>
        private const long ClearDebounceMs = 60_000;

        private readonly object lockObj = new();
        private ThermalCameraUserData cache;
        private readonly Dictionary<int, bool> lastFlood200State = [];
        private readonly Dictionary<int, bool> lastFlood700State = [];
        private readonly Dictionary<int, bool> lastOnlineState = [];

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
            // EnableMessageLimit is a const switch — when it's false the rest of the
            // method is dead-code-eliminated, which is exactly the toggle behavior we
            // want, so the unreachable-code warning is suppressed locally.
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
        /// Removes a user chat message. The caller (controller) is responsible for enforcing
        /// that only administrators reach this method. System messages (flood events) cannot
        /// be deleted.
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
        /// Returns a flat list of {itemId, message} pairs for any messages whose id is greater than
        /// the given cursor. Also returns the set of item IDs that saw at least one deletion so the
        /// client can invalidate its local cache for them.
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
        /// Compares the latest flood-state snapshot against the previous one and appends a
        /// system message for every normal→flooded transition. Only one system message is
        /// emitted per episode (state stays "flooded" until the driver reports normal again).
        /// Also updates persistent start-time timestamps for Offline / 200mm / 700mm states.
        /// </summary>
        public void DetectFloodTransitions(
            IEnumerable<ThermalCameraItem> items,
            Dictionary<int, FloodStateSnapshot> currentStates)
        {
            if (items == null || currentStates == null)
                return;

            lock (lockObj)
            {
                ThermalCameraUserData userData = LoadUserData();
                bool dirty = false;
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                foreach (ThermalCameraItem item in items)
                {
                    if (!currentStates.TryGetValue(item.Id, out FloodStateSnapshot cur))
                        continue;

                    UserDataEntry entry = GetOrCreateEntry(userData, item.Id);

                    // Online / Offline timer — state-based so already-offline devices
                    // get a start timestamp even on the first poll cycle after plugin startup.
                    bool curOnline = cur.IsOnline;
                    if (cur.OnlineHasValue)
                    {
                        if (!curOnline)
                        {
                            // Device is offline — cancel any pending debounce and start timer.
                            if (entry.OfflineClearPendingSinceMs != 0)
                            {
                                entry.OfflineClearPendingSinceMs = 0;
                                dirty = true;
                            }
                            if (entry.OfflineStartMs == 0)
                            {
                                entry.OfflineStartMs = now;
                                dirty = true;
                            }
                            lastOnlineState[item.Id] = false;
                        }
                        else if (entry.OfflineStartMs > 0)
                        {
                            // Timer is active but current reading says "online" — debounce by elapsed time.
                            if (entry.OfflineClearPendingSinceMs == 0)
                            {
                                entry.OfflineClearPendingSinceMs = now;
                                dirty = true;
                            }
                            else if (now - entry.OfflineClearPendingSinceMs >= ClearDebounceMs)
                            {
                                entry.OfflineStartMs = 0;
                                entry.OfflineClearPendingSinceMs = 0;
                                dirty = true;
                                lastOnlineState[item.Id] = true;
                            }
                            // Keep lastOnlineState as false during debounce so a re-offline
                            // reading is treated as a continuation, not a new transition.
                        }
                        else
                        {
                            if (entry.OfflineClearPendingSinceMs != 0)
                            {
                                entry.OfflineClearPendingSinceMs = 0;
                                dirty = true;
                            }
                            lastOnlineState[item.Id] = true;
                        }
                    }

                    // 200mm transition
                    if (cur.Flood200HasValue)
                    {
                        bool prevKnown200 = lastFlood200State.TryGetValue(item.Id, out bool p200);
                        bool prev200 = prevKnown200 ? p200 : false;
                        if (cur.Flood200)
                        {
                            if (entry.Flood200ClearPendingSinceMs != 0)
                            {
                                entry.Flood200ClearPendingSinceMs = 0;
                                dirty = true;
                            }
                            if (!prev200 && prevKnown200) // Real transition — notify once
                            {
                                AddSystemMessage(item.Id,
                                    "Зафиксировано затопление 200мм — " +
                                    (string.IsNullOrEmpty(item.Name) ? "объект ТК" : item.Name),
                                    ChatMessageKind.Flood200);
                            }
                            if (entry.Flood200StartMs == 0)
                            {
                                entry.Flood200StartMs = now;
                                dirty = true;
                            }
                            lastFlood200State[item.Id] = true;
                        }
                        else if (entry.Flood200StartMs > 0)
                        {
                            // Timer is active but current reading says "not flooded" — debounce by elapsed time.
                            if (entry.Flood200ClearPendingSinceMs == 0)
                            {
                                entry.Flood200ClearPendingSinceMs = now;
                                dirty = true;
                            }
                            else if (now - entry.Flood200ClearPendingSinceMs >= ClearDebounceMs)
                            {
                                entry.Flood200StartMs = 0;
                                entry.Flood200ClearPendingSinceMs = 0;
                                dirty = true;
                                lastFlood200State[item.Id] = false;
                            }
                            // Keep lastFlood200State as true during debounce so a re-flooded
                            // reading doesn't fire a duplicate alarm notification.
                        }
                        else
                        {
                            if (entry.Flood200ClearPendingSinceMs != 0)
                            {
                                entry.Flood200ClearPendingSinceMs = 0;
                                dirty = true;
                            }
                            lastFlood200State[item.Id] = false;
                        }
                    }

                    // 700mm transition
                    if (cur.Flood700HasValue)
                    {
                        bool prevKnown700 = lastFlood700State.TryGetValue(item.Id, out bool p700);
                        bool prev700 = prevKnown700 ? p700 : false;
                        if (cur.Flood700)
                        {
                            if (entry.Flood700ClearPendingSinceMs != 0)
                            {
                                entry.Flood700ClearPendingSinceMs = 0;
                                dirty = true;
                            }
                            if (!prev700 && prevKnown700) // Real transition — notify once
                            {
                                AddSystemMessage(item.Id,
                                    "ТРЕВОГА: затопление 700мм — " +
                                    (string.IsNullOrEmpty(item.Name) ? "объект ТК" : item.Name),
                                    ChatMessageKind.Flood700);
                            }
                            if (entry.Flood700StartMs == 0)
                            {
                                entry.Flood700StartMs = now;
                                dirty = true;
                            }
                            lastFlood700State[item.Id] = true;
                        }
                        else if (entry.Flood700StartMs > 0)
                        {
                            // Timer is active but current reading says "not flooded" — debounce by elapsed time.
                            if (entry.Flood700ClearPendingSinceMs == 0)
                            {
                                entry.Flood700ClearPendingSinceMs = now;
                                dirty = true;
                            }
                            else if (now - entry.Flood700ClearPendingSinceMs >= ClearDebounceMs)
                            {
                                entry.Flood700StartMs = 0;
                                entry.Flood700ClearPendingSinceMs = 0;
                                dirty = true;
                                lastFlood700State[item.Id] = false;
                            }
                            // Keep lastFlood700State as true during debounce so a re-flooded
                            // reading doesn't fire a duplicate alarm notification.
                        }
                        else
                        {
                            if (entry.Flood700ClearPendingSinceMs != 0)
                            {
                                entry.Flood700ClearPendingSinceMs = 0;
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

        /// <summary>
        /// Returns per-item timer start timestamps (UTC ms) for Offline, 200mm, 700mm.
        /// Zero means the state is not active.
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
                            OfflineStartMs = e.OfflineStartMs,
                            Flood200StartMs = e.Flood200StartMs,
                            Flood700StartMs = e.Flood700StartMs
                        };
                    }
                    else
                    {
                        result[id] = new ItemTimers();
                    }
                }
                return result;
            }
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

                // Post a chat message so the TK's chat log shows the acknowledgment
                string duration = FormatDuration(rec.AckedAtMs - floodStartMs);
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
