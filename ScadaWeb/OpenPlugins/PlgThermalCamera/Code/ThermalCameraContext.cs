// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Client;
using Scada.Data.Models;
using Scada.Web.Plugins.PlgThermalCamera.Models;
using Scada.Web.Services;
using System.Linq;

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

        // If a background scan does not complete within this period (e.g. server hang),
        // it is evicted from scansInProgress so DetectFloodTransitions can retry.
        private const long ScanTimeoutMs = 5 * 60 * 1000; // 5 minutes

        // Flood-clear debounce window: ArchiveStartMs is only reset after the sensor has
        // reported "not flooded" continuously for this long.  Shorter gaps are treated as
        // sensor glitches and the existing flood episode (and its acknowledgment) is kept.
        private const long OneDayMs = 24L * 60 * 60 * 1000;

        // Confirmation delay: a flood must be continuously active in the archive for at
        // least this long before it is recorded. Suppresses brief phantom floods (e.g. a
        // sensor blip right after a device reboot) from ever reaching the active-events
        // journal, the acknowledgment queue, or the "fired in 24h" badge.
        private const long FloodConfirmDelayMs = 10L * 60 * 1000; // 10 minutes

        private readonly object lockObj = new();
        private ThermalCameraUserData cache;
        private readonly Dictionary<int, bool> lastFlood200State = [];
        private readonly Dictionary<int, bool> lastFlood700State = [];
        private readonly HashSet<(int itemId, string kind)> scansInProgress = [];
        // UTC ms when each scan was started — used to detect stalled scans.
        private readonly Dictionary<(int itemId, string kind), long> scanStartTimes = [];
        // Tracks which MoreThan30DaysSentinel values have been reset this session.
        // Prevents infinite re-scan loops for genuinely long floods while still
        // giving the improved ScanFloodStartMs algorithm one chance to run.
        private readonly HashSet<(int itemId, string kind)> sentinelsReset = [];
        // Serializes background archive scans so only one GetTrend runs at a time.
        // Without this, parallel scans corrupt the transaction ID on the shared
        // ScadaClient and the server rejects requests with "неверный идентификатор транзакции".
        private readonly SemaphoreSlim scanSemaphore = new(1, 1);
        // Flood starts found by a scan but still younger than FloodConfirmDelayMs.
        // Keyed by (itemId, kind). Committed by DetectFloodTransitions once they age past
        // the delay; discarded if the flood clears or the device goes offline first.
        private readonly Dictionary<(int itemId, string kind), long> floodPendingStartMs = [];

        // ---- Server-side flood-history cache ------------------------------------
        // Computed once per item/year, shared across all users and page reloads.
        // Current year: 10-min TTL (archive grows); past years: 6-hour TTL (immutable).
        private const long HistTtlCurrentMs = 10 * 60 * 1000L;
        private const long HistTtlPastMs    =  6 * 60 * 60 * 1000L;

        private readonly Dictionary<string, (long ts, FloodHistoryResult data)> histCache = [];
        private readonly SemaphoreSlim histSemaphore = new(2, 2); // max 2 parallel computes
        private bool histPreloadStarted = false;

        // De-dupes concurrent background refreshes of the same key (stale-while-revalidate).
        private readonly HashSet<string> histRefreshing = [];
        // Serializes writes to the on-disk cache file.
        private readonly object histFileLock = new();
        // The disk cache is read into histCache once, on first access.
        private bool histLoaded = false;

        public string GetUserDataFilePath()
        {
            return Path.Combine(webContext.AppDirs.StorageDir, "PlgThermalCamera", "UserData.xml");
        }

        /// <summary>
        /// Path of the persisted server-side flood-history cache (survives restarts).
        /// </summary>
        public string GetHistCacheFilePath()
        {
            return Path.Combine(webContext.AppDirs.StorageDir, "PlgThermalCamera", "FloodHistoryCache.xml");
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
            Dictionary<int, FloodStateSnapshot> currentStates)
        {
            if (items == null || currentStates == null)
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
                            // Device offline — drop any unconfirmed flood so a stale pending
                            // start cannot be committed when the device returns.
                            floodPendingStartMs.Remove((item.Id, "flood200"));
                            floodPendingStartMs.Remove((item.Id, "flood700"));

                            if (entry.OfflineArchiveStartMs == 0 && item.OnlineCnlNum > 0)
                                TriggerArchiveScan(item.Id, item.OnlineCnlNum, "offline");
                        }
                        else if (entry.OfflineArchiveStartMs != 0)
                        {
                            entry.OfflineArchiveStartMs = 0;
                            dirty = true;
                        }
                    }

                    // 200mm flood — 24-hour debounce before resetting start timestamp.
                    // Brief sensor glitches (< 24h clear) keep the original flood start and
                    // any existing acknowledgment intact. Only a genuine 24h+ gap resets everything.
                    if (cur.Flood200HasValue)
                    {
                        bool prevKnown = lastFlood200State.TryGetValue(item.Id, out bool prev);
                        if (cur.Flood200)
                        {
                            if (entry.Flood200LastClearMs != 0)
                            {
                                // Flood returned within the 24h grace window — sensor glitch.
                                // Cancel the clear timer; ArchiveStartMs and any ack remain valid.
                                entry.Flood200LastClearMs = 0;
                                dirty = true;
                                // No new chat message — it's still the same flood episode.
                            }
                            else if (!prev && prevKnown)
                            {
                                // Genuine new flood start (either fresh, or after 24h+ gap).
                                AddSystemMessage(item.Id,
                                    "Зафиксировано затопление 200мм — " +
                                    (string.IsNullOrEmpty(item.Name) ? "объект ТК" : item.Name),
                                    ChatMessageKind.Flood200);
                            }

                            // Give a previously wrong "> 30д" result one retry with the
                            // improved scan algorithm (which can find the start when
                            // no pre-flood dry points exist in the archive).
                            if (entry.Flood200ArchiveStartMs == MoreThan30DaysSentinel &&
                                sentinelsReset.Add((item.Id, "flood200")))
                            {
                                entry.Flood200ArchiveStartMs = 0;
                                dirty = true;
                            }
                            if (entry.Flood200ArchiveStartMs == 0 && item.Flood200CnlNum > 0)
                            {
                                if (floodPendingStartMs.TryGetValue((item.Id, "flood200"), out long pend200))
                                {
                                    // Scanned already — commit once the flood ages past the delay.
                                    if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - pend200 >= FloodConfirmDelayMs &&
                                        CommitFloodStart(entry, item.Id, "flood200", pend200))
                                    {
                                        dirty = true;
                                    }
                                }
                                else
                                {
                                    TriggerArchiveScan(item.Id, item.Flood200CnlNum, "flood200");
                                }
                            }

                            lastFlood200State[item.Id] = true;
                        }
                        else
                        {
                            // Flood cleared before confirmation — discard the unconfirmed phantom.
                            floodPendingStartMs.Remove((item.Id, "flood200"));

                            if (entry.Flood200ArchiveStartMs != 0)
                            {
                                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                if (entry.Flood200LastClearMs == 0)
                                {
                                    // First poll with no flood — start the 24h grace timer.
                                    entry.Flood200LastClearMs = nowMs;
                                    dirty = true;
                                }
                                else if (nowMs - entry.Flood200LastClearMs >= OneDayMs)
                                {
                                    // Genuinely clear for 24h+ — reset flood episode.
                                    entry.Flood200ArchiveStartMs = 0;
                                    entry.Flood200LastClearMs = 0;
                                    dirty = true;
                                }
                                // else: within 24h grace — keep waiting
                            }
                            else if (entry.Flood200LastClearMs != 0)
                            {
                                // ArchiveStartMs already 0 (scan pending), clear orphaned timer.
                                entry.Flood200LastClearMs = 0;
                                dirty = true;
                            }

                            lastFlood200State[item.Id] = false;
                        }
                    }

                    // 700mm flood — same 24-hour debounce logic as 200mm.
                    if (cur.Flood700HasValue)
                    {
                        bool prevKnown = lastFlood700State.TryGetValue(item.Id, out bool prev);
                        if (cur.Flood700)
                        {
                            if (entry.Flood700LastClearMs != 0)
                            {
                                entry.Flood700LastClearMs = 0;
                                dirty = true;
                            }
                            else if (!prev && prevKnown)
                            {
                                AddSystemMessage(item.Id,
                                    "ТРЕВОГА: затопление 700мм — " +
                                    (string.IsNullOrEmpty(item.Name) ? "объект ТК" : item.Name),
                                    ChatMessageKind.Flood700);
                            }

                            if (entry.Flood700ArchiveStartMs == MoreThan30DaysSentinel &&
                                sentinelsReset.Add((item.Id, "flood700")))
                            {
                                entry.Flood700ArchiveStartMs = 0;
                                dirty = true;
                            }
                            if (entry.Flood700ArchiveStartMs == 0 && item.Flood700CnlNum > 0)
                            {
                                if (floodPendingStartMs.TryGetValue((item.Id, "flood700"), out long pend700))
                                {
                                    // Scanned already — commit once the flood ages past the delay.
                                    if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - pend700 >= FloodConfirmDelayMs &&
                                        CommitFloodStart(entry, item.Id, "flood700", pend700))
                                    {
                                        dirty = true;
                                    }
                                }
                                else
                                {
                                    TriggerArchiveScan(item.Id, item.Flood700CnlNum, "flood700");
                                }
                            }

                            lastFlood700State[item.Id] = true;
                        }
                        else
                        {
                            // Flood cleared before confirmation — discard the unconfirmed phantom.
                            floodPendingStartMs.Remove((item.Id, "flood700"));

                            if (entry.Flood700ArchiveStartMs != 0)
                            {
                                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                if (entry.Flood700LastClearMs == 0)
                                {
                                    entry.Flood700LastClearMs = nowMs;
                                    dirty = true;
                                }
                                else if (nowMs - entry.Flood700LastClearMs >= OneDayMs)
                                {
                                    entry.Flood700ArchiveStartMs = 0;
                                    entry.Flood700LastClearMs = 0;
                                    dirty = true;
                                }
                            }
                            else if (entry.Flood700LastClearMs != 0)
                            {
                                entry.Flood700LastClearMs = 0;
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
        private void TriggerArchiveScan(int itemId, int cnlNum, string kind)
        {
            var key = (itemId, kind);
            if (!scansInProgress.Add(key))
                return; // scan already running for this item+kind
            scanStartTimes[key] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Task.Run(() => DoArchiveScan(itemId, cnlNum, kind, key));
        }

        private void DoArchiveScan(int itemId, int cnlNum, string kind, (int, string) key)
        {
            long archiveStartMs = 0;
            ScadaClient client = null;
            // Serialize all background scans so only one GetTrend runs at a time.
            // This prevents transaction-ID corruption on the shared client and avoids
            // hammering the SCADA Server when many TKs need scanning simultaneously.
            scanSemaphore.Wait();
            try
            {
                // Rent a dedicated client from the pool so scans never share state with
                // the request-scoped client used by HTTP API handlers.
                client = webContext.ClientPool.GetClient(webContext.AppConfig.ConnectionOptions);

                archiveStartMs = kind == "offline"
                    ? ScanOfflineStartMs(client, cnlNum)
                    : ScanFloodStartMs(client, cnlNum);
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(
                    $"PlgThermalCamera: archive scan failed for item {itemId}/{kind}: {ex.Message}");
            }
            finally
            {
                if (client != null)
                    webContext.ClientPool.ReturnClient(client);
                scanSemaphore.Release();
            }

            lock (lockObj)
            {
                scansInProgress.Remove(key);
                scanStartTimes.Remove(key);

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
                        updated = ConfirmOrPendFloodStart(entry, itemId, "flood200", archiveStartMs);
                        break;
                    case "flood700" when entry.Flood700ArchiveStartMs == 0:
                        updated = ConfirmOrPendFloodStart(entry, itemId, "flood700", archiveStartMs);
                        break;
                }

                if (updated)
                {
                    if (!SaveUserData(userData, out string saveErr))
                        webContext.Log.WriteError("PlgThermalCamera: " + saveErr);
                }
            }
        }

        // Decides, when a scan returns a flood start, whether to commit it immediately or
        // hold it pending until it has been active for FloodConfirmDelayMs. The ">30 days"
        // sentinel is always committed (clearly a real long-running flood, not a phantom).
        // Must be called inside lockObj. Returns true if the entry was modified.
        private bool ConfirmOrPendFloodStart(UserDataEntry entry, int itemId, string kind, long archiveStartMs)
        {
            bool confirmed = archiveStartMs == MoreThan30DaysSentinel
                || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - archiveStartMs >= FloodConfirmDelayMs;

            if (confirmed)
                return CommitFloodStart(entry, itemId, kind, archiveStartMs);

            // Too fresh — remember it; DetectFloodTransitions commits it once it ages past
            // the delay, or discards it if the flood clears / the device goes offline first.
            floodPendingStartMs[(itemId, kind)] = archiveStartMs;
            return false;
        }

        // Commits a confirmed flood start to the entry. Must be called inside lockObj.
        // Returns true if the entry was modified.
        private bool CommitFloodStart(UserDataEntry entry, int itemId, string kind, long archiveStartMs)
        {
            floodPendingStartMs.Remove((itemId, kind));

            if (kind == "flood200")
            {
                if (entry.Flood200ArchiveStartMs != 0)
                    return false;
                entry.Flood200ArchiveStartMs = archiveStartMs;
                return true;
            }

            if (entry.Flood700ArchiveStartMs != 0)
                return false;

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
            return true;
        }

        // Scans the minute archive backwards (expanding windows) to find when flooding began.
        // Returns the UTC ms of the first flooded reading, 0 if flood not yet archived, or
        // MoreThan30DaysSentinel if no transition was found within 30 days.
        private static long ScanFloodStartMs(ScadaClient client, int cnlNum)
        {
            DateTime now = DateTime.UtcNow;
            foreach (int hours in ScanWindowHours)
            {
                DateTime windowStart = now.AddHours(-hours);
                Trend trend = client.GetTrend(
                    MinuteArchiveBit,
                    new TimeRange(windowStart, now, true),
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

                // No dry point found — all archived points in this window are flooded.
                // This happens when the channel started archiving DURING an active flood
                // (so there are no pre-flood "normal" readings in the archive).
                // In that case the very first flooded point in the window is the best known
                // start time, provided it's well inside the window (not right at the edge).
                // "Right at the edge" (< 10 min from windowStart) indicates the flood was
                // already active before this window — widen the scan.
                for (int i = 0; i < trend.Points.Count; i++)
                {
                    TrendPoint p = trend.Points[i];
                    if (p.Stat > 0 && p.Val == 0)
                    {
                        if (p.Timestamp > windowStart.AddMinutes(10))
                        {
                            // First flooded point is well within the window — treat as flood start.
                            return new DateTimeOffset(p.Timestamp, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        }
                        break; // point is at window edge → try wider window
                    }
                }

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
            if (storedMs == 0)
            {
                var key = (itemId, kind);
                if (scansInProgress.Contains(key))
                {
                    // Evict stalled scan so DetectFloodTransitions can retry.
                    long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (scanStartTimes.TryGetValue(key, out long startMs) &&
                        nowMs - startMs > ScanTimeoutMs)
                    {
                        scansInProgress.Remove(key);
                        scanStartTimes.Remove(key);
                        webContext.Log.WriteError(
                            $"PlgThermalCamera: archive scan timed out for item {itemId}/{kind}, will retry");
                        return 0;
                    }
                    return -1; // scan in progress
                }
            }
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

        // ---- Flood-history server cache API ------------------------------------

        /// <summary>
        /// Returns merged flood intervals for one item + year. Serves from the
        /// in-memory server cache if fresh; otherwise computes from the SCADA
        /// minute archive and caches. Shared across all connected users.
        /// </summary>
        public async Task<FloodHistoryResult> GetFloodHistory(
            int itemId, int year, int cnl200, int cnl700)
        {
            EnsureHistCacheLoaded();

            string key = $"h_{itemId}_{year}";
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long ttl = (year == DateTime.UtcNow.Year) ? HistTtlCurrentMs : HistTtlPastMs;

            FloodHistoryResult stale = null;
            lock (lockObj)
            {
                if (histCache.TryGetValue(key, out var hit))
                {
                    if (nowMs - hit.ts < ttl)
                        return hit.data;        // fresh — serve directly
                    stale = hit.data;           // stale — serve now, revalidate in background
                }
            }

            // Stale-while-revalidate: return the (possibly disk-loaded) cached copy
            // instantly and refresh it in the background. This is what makes the
            // persisted cache useful after a restart — the first user gets data with
            // no wait while a background recompute brings it up to date.
            if (stale != null)
            {
                TriggerBackgroundRefresh(itemId, year, cnl200, cnl700);
                return stale;
            }

            // Cold miss: compute synchronously.
            await histSemaphore.WaitAsync();
            try
            {
                // Double-check: another task may have filled the cache while we waited.
                lock (lockObj)
                {
                    if (histCache.TryGetValue(key, out var hit2) && nowMs - hit2.ts < ttl)
                        return hit2.data;
                }

                FloodHistoryResult result = await Task.Run(
                    () => ComputeFloodHistory(year, cnl200, cnl700));

                StoreAndPersist(key, result);
                return result;
            }
            finally
            {
                histSemaphore.Release();
            }
        }

        /// <summary>
        /// Recomputes one item/year in the background and updates the cache.
        /// Used by stale-while-revalidate; de-duped per key so concurrent callers
        /// (e.g. several users opening the same history) trigger only one recompute.
        /// </summary>
        private void TriggerBackgroundRefresh(int itemId, int year, int cnl200, int cnl700)
        {
            string key = $"h_{itemId}_{year}";
            lock (lockObj)
            {
                if (!histRefreshing.Add(key))
                    return; // a refresh for this key is already running
            }

            Task.Run(async () =>
            {
                await histSemaphore.WaitAsync();
                try
                {
                    FloodHistoryResult result = await Task.Run(
                        () => ComputeFloodHistory(year, cnl200, cnl700));
                    StoreAndPersist(key, result);
                }
                catch (Exception ex)
                {
                    webContext.Log.WriteError(
                        $"PlgThermalCamera: background history refresh failed for {key}: {ex.Message}");
                }
                finally
                {
                    histSemaphore.Release();
                    lock (lockObj) { histRefreshing.Remove(key); }
                }
            });
        }

        /// <summary>
        /// Stores a computed result in the in-memory cache and persists the whole
        /// cache to disk so it survives a ScadaWeb restart.
        /// </summary>
        private void StoreAndPersist(string key, FloodHistoryResult result)
        {
            lock (lockObj)
            {
                histCache[key] = (result.CachedAtMs, result);
            }
            PersistHistCache();
        }

        /// <summary>
        /// Reads the on-disk cache into <see cref="histCache"/> exactly once, on first
        /// access. Entries are loaded with their original timestamps, so stale ones are
        /// served immediately and refreshed in the background (stale-while-revalidate).
        /// </summary>
        private void EnsureHistCacheLoaded()
        {
            lock (lockObj)
            {
                if (histLoaded)
                    return;
                histLoaded = true;

                FloodHistoryCacheFile file = new();
                if (!file.Load(GetHistCacheFilePath(), out string errMsg))
                {
                    webContext.Log.WriteError("PlgThermalCamera: failed to load flood-history cache: " + errMsg);
                    return;
                }

                foreach (CachedFloodHistory e in file.Entries)
                {
                    string key = $"h_{e.ItemId}_{e.Year}";
                    histCache[key] = (e.CachedAtMs, new FloodHistoryResult
                    {
                        Intervals = e.Intervals,
                        CachedAtMs = e.CachedAtMs
                    });
                }

                if (file.Entries.Count > 0)
                {
                    webContext.Log.WriteInfo(
                        $"PlgThermalCamera: loaded {file.Entries.Count} cached flood-history entries from disk.");
                }
            }
        }

        /// <summary>
        /// Serializes the whole in-memory cache to disk. Writes are serialized by
        /// <see cref="histFileLock"/> and the file itself is replaced atomically.
        /// </summary>
        private void PersistHistCache()
        {
            // histFileLock first, then snapshot under lockObj: serializing writers
            // and building the snapshot at write time means the last writer always
            // persists the most complete state — no out-of-order overwrite can drop
            // a just-added entry from disk.
            lock (histFileLock)
            {
                FloodHistoryCacheFile file = new();
                lock (lockObj)
                {
                    foreach (KeyValuePair<string, (long ts, FloodHistoryResult data)> kvp in histCache)
                    {
                        // key format: "h_{itemId}_{year}"
                        string[] parts = kvp.Key.Split('_');
                        if (parts.Length != 3 ||
                            !int.TryParse(parts[1], out int itemId) ||
                            !int.TryParse(parts[2], out int year))
                        {
                            continue;
                        }

                        file.Entries.Add(new CachedFloodHistory
                        {
                            ItemId = itemId,
                            Year = year,
                            CachedAtMs = kvp.Value.ts,
                            Intervals = kvp.Value.data.Intervals
                        });
                    }
                }

                if (!file.Save(GetHistCacheFilePath(), out string errMsg))
                    webContext.Log.WriteError("PlgThermalCamera: failed to persist flood-history cache: " + errMsg);
            }
        }

        private FloodHistoryResult ComputeFloodHistory(int year, int cnl200, int cnl700)
        {
            ScadaClient client = null;
            try
            {
                client = webContext.ClientPool.GetClient(webContext.AppConfig.ConnectionOptions);

                DateTime now = DateTime.UtcNow;
                DateTime yearStart = new(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                DateTime yearEnd = (year == now.Year)
                    ? now : new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                long periodEndMs = new DateTimeOffset(yearEnd).ToUnixTimeMilliseconds();

                Dictionary<string, long[][]> intervals = [];
                var channels = new[] { (CnlNum: cnl200, Kind: "200"),
                                       (CnlNum: cnl700, Kind: "700") };

                foreach (var (cnlNum, kind) in channels)
                {
                    if (cnlNum <= 0) continue;
                    Trend trend = client.GetTrend(
                        MinuteArchiveBit,
                        new TimeRange(yearStart, yearEnd, true),
                        cnlNum);
                    List<long[]> raw = ExtractFloodIntervalList(trend, periodEndMs);
                    if (raw.Count > 0)
                        intervals[kind] = MergeIntervalList(raw);
                }

                return new FloodHistoryResult
                {
                    Intervals = intervals,
                    CachedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
            }
            finally
            {
                if (client != null) webContext.ClientPool.ReturnClient(client);
            }
        }

        private static List<long[]> ExtractFloodIntervalList(Trend trend, long periodEndMs)
        {
            var result = new List<long[]>();
            if (trend == null || trend.Points.Count == 0) return result;
            for (int j = 0; j < trend.Points.Count; j++)
            {
                TrendPoint p = trend.Points[j];
                if (p.Stat <= 0 || p.Val != 0) continue;
                long startMs = new DateTimeOffset(p.Timestamp, TimeSpan.Zero)
                    .ToUnixTimeMilliseconds();
                long endMs = (j + 1 < trend.Points.Count)
                    ? new DateTimeOffset(trend.Points[j + 1].Timestamp, TimeSpan.Zero)
                        .ToUnixTimeMilliseconds()
                    : periodEndMs;
                if (endMs > startMs) result.Add([startMs, endMs]);
            }
            return result;
        }

        private static long[][] MergeIntervalList(List<long[]> intervals)
        {
            if (intervals.Count == 0) return [];
            intervals.Sort((a, b) => a[0].CompareTo(b[0]));
            List<long[]> merged = [[intervals[0][0], intervals[0][1]]];
            for (int i = 1; i < intervals.Count; i++)
            {
                long[] last = merged[^1];
                if (intervals[i][0] <= last[1]) { if (intervals[i][1] > last[1]) last[1] = intervals[i][1]; }
                else merged.Add([intervals[i][0], intervals[i][1]]);
            }
            return [.. merged];
        }

        /// <summary>
        /// Triggers a one-time background pre-warm of the history cache for all
        /// items. Safe to call on every poll — runs the actual work only once.
        /// </summary>
        public void TriggerHistoryPreload(IEnumerable<ThermalCameraItem> items)
        {
            lock (lockObj)
            {
                if (histPreloadStarted) return;
                histPreloadStarted = true;
            }

            // Bring any persisted cache into memory first, so freshly-restarted
            // servers serve disk data instantly and only recompute what is stale.
            EnsureHistCacheLoaded();

            var targets = items
                .Where(i => i.Flood200CnlNum > 0 || i.Flood700CnlNum > 0)
                .Select(i => (i.Id, i.Flood200CnlNum, i.Flood700CnlNum))
                .ToList();

            if (targets.Count == 0) return;
            int year = DateTime.UtcNow.Year;

            Task.Run(async () =>
            {
                webContext.Log.WriteInfo(
                    $"PlgThermalCamera: starting server-side history pre-warm ({targets.Count} items).");
                int ok = 0;
                foreach (var (itemId, cnl200, cnl700) in targets)
                {
                    try
                    {
                        await GetFloodHistory(itemId, year, cnl200, cnl700);
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        webContext.Log.WriteError(
                            $"PlgThermalCamera: history preload failed for item {itemId}: {ex.Message}");
                    }
                }
                webContext.Log.WriteInfo(
                    $"PlgThermalCamera: history pre-warm complete ({ok}/{targets.Count} cached).");
            });
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
    /// Compact flood-history result: merged non-overlapping intervals per kind.
    /// Keyed by "200" / "700"; each value is an array of [startMs, endMs] pairs.
    /// </summary>
    public class FloodHistoryResult
    {
        public Dictionary<string, long[][]> Intervals { get; set; } = [];
        public long CachedAtMs { get; set; }
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
