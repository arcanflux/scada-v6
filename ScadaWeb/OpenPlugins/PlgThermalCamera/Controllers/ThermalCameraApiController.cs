// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Mvc;
using Scada.Data.Const;
using Scada.Data.Models;
using Scada.Protocol;
using Scada.Storages;
using Scada.Web.Api;
using Scada.Web.Lang;
using Scada.Web.Plugins.PlgThermalCamera.Code;
using Scada.Web.Plugins.PlgThermalCamera.Models;
using Scada.Web.Services;
using System.Linq;

namespace Scada.Web.Plugins.PlgThermalCamera.Controllers
{
    [ApiController]
    [Route("Api/ThermalCamera/[action]")]
    public class ThermalCameraApiController(
        IWebContext webContext,
        IClientAccessor clientAccessor,
        IUserContext userContext,
        IViewLoader viewLoader,
        ThermalCameraContext thermalCameraContext) : ControllerBase
    {
        /// <summary>
        /// Gets current channel data for the specified thermal-camera view and, as a side
        /// effect, detects normal→flooded transitions so the chat log auto-records flood
        /// events. Also returns any chat messages newer than the supplied cursor so the
        /// browser can incrementally refresh its panels using the existing 1Hz poll.
        /// Follows the same pattern as PlgMain.GetCurDataByView / PlgMap:
        ///   1. Load the view by viewID from the view cache.
        ///   2. Use the view's CnlNumList that was registered during LoadView.
        ///   3. Ask SCADA Server for the latest values via ScadaClient.GetCurrentData.
        ///   4. Classify per-item flood flags + dispatch to DetectFloodTransitions.
        ///   5. Return {serverTime, data, chatUpdates}.
        /// </summary>
        public Dto<CurDataResult> GetCurData(int viewID, long chatCursor = 0)
        {
            try
            {
                if (!viewLoader.GetView(viewID, out ThermalCameraTableView view, out string errMsg))
                    return Dto<CurDataResult>.Fail(errMsg);

                // Одноразовый (на процесс) перевод user data на стабильные ID из имён ТК.
                thermalCameraContext.MigrateUserData(view.Items);

                // Тумблер «В работе» определяет активность ТК. Спящие ТК (тумблер
                // выключен или запись отсутствует) полностью исключаются из ответа:
                // их каналы не передаются клиенту, переходы затопления не фиксируются,
                // таймеры не считаются и квитирование по ним не формируется.
                // Карта состояний также отдаётся клиентам в каждом ответе, чтобы все
                // сессии видели одно и то же и подхватывали чужие переключения.
                Dictionary<int, bool> commissionedMap = thermalCameraContext.GetCommissionedMap(view.Items);
                List<ThermalCameraItem> activeItems = [];
                HashSet<int> sleepingCnlNums = [];
                HashSet<int> activeCnlNums = [];

                foreach (ThermalCameraItem item in view.Items)
                {
                    if (commissionedMap.TryGetValue(item.Id, out bool commissioned) && commissioned)
                    {
                        activeItems.Add(item);
                        activeCnlNums.UnionWith(item.GetAllCnlNums());
                    }
                    else
                    {
                        sleepingCnlNums.UnionWith(item.GetAllCnlNums());
                    }
                }

                // Канал, используемый хотя бы одной активной ТК, передаётся всегда.
                sleepingCnlNums.ExceptWith(activeCnlNums);

                List<int> cnlNumList = view.CnlNumList ?? [];
                int cnlCnt = cnlNumList.Count;

                Dictionary<int, CnlDataItem> dataItems = [];
                Dictionary<int, CnlData> rawByCnl = [];

                if (cnlCnt > 0)
                {
                    int[] cnlNumArr = [.. cnlNumList];
                    CnlData[] cnlDataArr = clientAccessor.ScadaClient.GetCurrentData(cnlNumArr, false, out _);
                    CnlDataFormatter formatter = new(webContext.ConfigDatabase, userContext.TimeZone);

                    for (int i = 0; i < cnlCnt; i++)
                    {
                        int cnlNum = cnlNumArr[i];
                        CnlData cnlData = i < cnlDataArr.Length ? cnlDataArr[i] : CnlData.Empty;
                        rawByCnl[cnlNum] = cnlData;

                        // Каналы спящих ТК не попадают в ответ.
                        if (sleepingCnlNums.Contains(cnlNum))
                            continue;

                        CnlDataFormatted formatted = formatter.FormatCnlData(cnlData, cnlNum, true);

                        dataItems[cnlNum] = new CnlDataItem
                        {
                            CnlNum = cnlNum,
                            Val = cnlData.Val,
                            Stat = cnlData.Stat,
                            Text = formatted.DispVal,
                            Color = formatted.Colors?.Length > 0 ? formatted.Colors[0] : ""
                        };
                    }
                }

                // Build a per-item flood snapshot and let the context emit transition messages.
                // Flood semantics mirror the JS: val == 0 && stat > 0 → flooded.
                // Only commissioned (active) items take part.
                Dictionary<int, FloodStateSnapshot> floodStates = [];
                foreach (ThermalCameraItem item in activeItems)
                {
                    FloodStateSnapshot snap = new();
                    if (item.Flood200CnlNum > 0 && rawByCnl.TryGetValue(item.Flood200CnlNum, out CnlData d200))
                    {
                        snap.Flood200HasValue = d200.Stat > 0;
                        snap.Flood200 = d200.Stat > 0 && d200.Val == 0;
                    }
                    if (item.Flood700CnlNum > 0 && rawByCnl.TryGetValue(item.Flood700CnlNum, out CnlData d700))
                    {
                        snap.Flood700HasValue = d700.Stat > 0;
                        snap.Flood700 = d700.Stat > 0 && d700.Val == 0;
                    }
                    if (item.OnlineCnlNum > 0 && rawByCnl.TryGetValue(item.OnlineCnlNum, out CnlData dOnl))
                    {
                        snap.OnlineHasValue = true;
                        snap.IsOnline = dOnl.Stat > 0 && dOnl.Val != 0;
                    }
                    floodStates[item.Id] = snap;
                }
                thermalCameraContext.DetectFloodTransitions(activeItems, floodStates);

                // Collect any new chat messages so the client can merge them in.
                ChatSyncResult chatSync = thermalCameraContext.GetChatUpdates(chatCursor);

                // Timer start timestamps (UTC ms) — persisted server-side so they survive restart.
                Dictionary<int, ItemTimers> timers = thermalCameraContext.GetTimers(
                    activeItems.Select(i => i.Id));

                // Build pending acks (700mm active, not yet acknowledged) and
                // acked items (700mm active, already acknowledged for this episode).
                List<PendingAckItem> pendingAcks = [];
                List<AckedItem> ackedItems = [];

                foreach (ThermalCameraItem item in activeItems)
                {
                    if (!floodStates.TryGetValue(item.Id, out FloodStateSnapshot s) || !s.Flood700)
                        continue;

                    long flood700StartMs = timers.TryGetValue(item.Id, out ItemTimers t) ? t.Flood700StartMs : 0;

                    // Only surface confirmed floods: a committed start timestamp (> 0) or the
                    // ">30 days" sentinel (-2). Unconfirmed states (0 = pending the 10-min
                    // confirmation delay, -1 = scan in progress) are withheld so post-restart
                    // phantom floods never enter the acknowledgment queue.
                    if (flood700StartMs <= 0 && flood700StartMs != -2)
                        continue;

                    AckRecord existingAck = flood700StartMs > 0
                        ? thermalCameraContext.FindAck(item.Id, flood700StartMs)
                        : null;

                    if (existingAck == null)
                    {
                        pendingAcks.Add(new PendingAckItem
                        {
                            ItemId = item.Id,
                            ItemName = item.Name ?? "",
                            Flood700StartMs = flood700StartMs
                        });
                    }
                    else
                    {
                        ackedItems.Add(new AckedItem
                        {
                            ItemId = item.Id,
                            Flood700StartMs = flood700StartMs,
                            AckedAtMs = existingAck.AckedAtMs,
                            AckedBy = existingAck.AckedBy,
                            Comment = existingAck.Comment
                        });
                    }
                }

                return Dto<CurDataResult>.Success(new CurDataResult
                {
                    ServerTime = DateTime.UtcNow.ToString("o"),
                    Data = dataItems,
                    ChatCursor = chatSync.Cursor,
                    ChatUpdates = chatSync.Messages,
                    Timers = timers,
                    PendingAcks = pendingAcks,
                    AckedItems = ackedItems,
                    Commissioned = commissionedMap
                });
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(GetCurData)));
                return Dto<CurDataResult>.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Returns the full chat history for a single item. Used when a panel is opened
        /// for the first time so the user immediately sees everything that was posted
        /// before their session started.
        /// </summary>
        [HttpGet]
        public Dto<ChatHistoryResult> GetChatHistory(int itemId)
        {
            try
            {
                List<ChatMessage> history = thermalCameraContext.GetHistory(itemId);
                return Dto<ChatHistoryResult>.Success(new ChatHistoryResult
                {
                    ItemId = itemId,
                    Messages = history
                });
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(GetChatHistory)));
                return Dto<ChatHistoryResult>.Fail(ex.Message);
            }
        }

        [HttpPost]
        public Dto<ChatMessage> PostChatMessage([FromBody] PostChatMessageRequest request)
        {
            try
            {
                if (request == null || request.ItemId <= 0)
                    return Dto<ChatMessage>.Fail("Некорректный запрос");

                string text = (request.Text ?? "").Trim();
                if (string.IsNullOrEmpty(text))
                    return Dto<ChatMessage>.Fail("Сообщение не может быть пустым");

                if (text.Length > 2000)
                    text = text[..2000];

                string author = userContext.UserEntity?.Name ?? "anonymous";
                ChatMessage msg = thermalCameraContext.AddUserMessage(request.ItemId, author, text, out string errMsg);
                return msg != null
                    ? Dto<ChatMessage>.Success(msg)
                    : Dto<ChatMessage>.Fail(errMsg);
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(PostChatMessage)));
                return Dto<ChatMessage>.Fail(ex.Message);
            }
        }

        [HttpPost]
        public Dto DeleteChatMessage([FromBody] DeleteChatMessageRequest request)
        {
            try
            {
                if (request == null || request.ItemId <= 0 || request.MessageId <= 0)
                    return Dto.Fail("Некорректный запрос");

                if (userContext.UserEntity?.RoleID != RoleID.Administrator)
                    return Dto.Fail("Удалять сообщения может только администратор");

                return thermalCameraContext.DeleteMessage(
                    request.ItemId, request.MessageId, out string errMsg)
                    ? Dto.Success()
                    : Dto.Fail(errMsg);
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(DeleteChatMessage)));
                return Dto.Fail(ex.Message);
            }
        }

        public Dto<ThermalCameraUserData> GetUserData()
        {
            try
            {
                ThermalCameraUserData userData = thermalCameraContext.LoadUserData();
                return Dto<ThermalCameraUserData>.Success(userData);
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(GetUserData)));
                return Dto<ThermalCameraUserData>.Fail(ex.Message);
            }
        }

        [HttpPost]
        public Dto SaveCommissioned([FromBody] SaveCommissionedRequest request)
        {
            try
            {
                if (request == null || request.ItemId <= 0)
                    return Dto.Fail("Некорректный запрос");

                return thermalCameraContext.SetCommissioned(
                    [request.ItemId], request.IsCommissioned, out string errMsg)
                    ? Dto.Success()
                    : Dto.Fail(errMsg);
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(SaveCommissioned)));
                return Dto.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Массовое переключение тумблера «В работе»: все ТК или ТК одного района
        /// одним запросом (меню в заголовке столбца). Состояние сохраняется в общий
        /// файл user data и разойдётся всем клиентам через опрос GetCurData.
        /// </summary>
        [HttpPost]
        public Dto SaveCommissionedBulk([FromBody] SaveCommissionedBulkRequest request)
        {
            try
            {
                if (request == null || request.ItemIds == null || request.ItemIds.Count == 0)
                    return Dto.Fail("Некорректный запрос");

                return thermalCameraContext.SetCommissioned(
                    request.ItemIds, request.IsCommissioned, out string errMsg)
                    ? Dto.Success()
                    : Dto.Fail(errMsg);
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(SaveCommissionedBulk)));
                return Dto.Fail(ex.Message);
            }
        }

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"
        };

        [HttpGet]
        public IActionResult GetPhoto(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                    return BadRequest("Path is required");

                // Directory traversal protection
                if (path.Contains("..") || path.Contains('~'))
                    return BadRequest("Invalid path");

                string ext = Path.GetExtension(path);
                if (!AllowedExtensions.Contains(ext))
                    return BadRequest("Unsupported image format");

                string contentType = ext.ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    ".bmp" => "image/bmp",
                    _ => "application/octet-stream"
                };

                if (webContext.Storage.ViewAvailable)
                {
                    using BinaryReader reader = webContext.Storage.OpenBinary(DataCategory.View, path);
                    MemoryStream ms = new();
                    reader.BaseStream.CopyTo(ms);
                    ms.Position = 0;
                    return File(ms, contentType);
                }
                else
                {
                    MemoryStream ms = new();
                    RelativePath relativePath = new(TopFolder.View, AppFolder.Root, path);
                    clientAccessor.ScadaClient.DownloadFile(relativePath, ms, true);
                    ms.Position = 0;
                    return File(ms, contentType);
                }
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(GetPhoto)));
                return NotFound("Photo not found");
            }
        }

        [HttpPost]
        public Dto<AckRecord> PostAcknowledgment([FromBody] PostAckRequest request)
        {
            try
            {
                if (request == null || request.ItemId <= 0)
                    return Dto<AckRecord>.Fail("Некорректный запрос");

                string comment = (request.Comment ?? "").Trim();
                if (string.IsNullOrEmpty(comment))
                    return Dto<AckRecord>.Fail("Комментарий обязателен");

                string author = userContext.UserEntity?.Name ?? "anonymous";
                AckRecord rec = thermalCameraContext.AddAcknowledgment(
                    request.ItemId, request.ItemName, request.FloodStartMs,
                    author, comment, out string errMsg);

                return rec != null
                    ? Dto<AckRecord>.Success(rec)
                    : Dto<AckRecord>.Fail(errMsg);
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(PostAcknowledgment)));
                return Dto<AckRecord>.Fail(ex.Message);
            }
        }

        [HttpGet]
        public Dto<List<AckRecord>> GetAckHistory()
        {
            try
            {
                return Dto<List<AckRecord>>.Success(thermalCameraContext.GetAllAcks());
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(GetAckHistory)));
                return Dto<List<AckRecord>>.Fail(ex.Message);
            }
        }
    }

    public class CurDataResult
    {
        public string ServerTime { get; set; } = "";
        public Dictionary<int, CnlDataItem> Data { get; set; } = [];
        public long ChatCursor { get; set; }
        public List<ChatUpdateItem> ChatUpdates { get; set; } = [];
        public Dictionary<int, ItemTimers> Timers { get; set; } = [];
        public List<PendingAckItem> PendingAcks { get; set; } = [];
        public List<AckedItem> AckedItems { get; set; } = [];

        /// <summary>
        /// Актуальное состояние тумблера «В работе» по каждой ТК представления —
        /// общее для всех пользователей, применяется клиентами на каждом опросе.
        /// </summary>
        public Dictionary<int, bool> Commissioned { get; set; } = [];
    }

    public class PendingAckItem
    {
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public long Flood700StartMs { get; set; }
    }

    public class AckedItem
    {
        public int ItemId { get; set; }
        public long Flood700StartMs { get; set; }
        public long AckedAtMs { get; set; }
        public string AckedBy { get; set; } = "";
        public string Comment { get; set; } = "";
    }

    public class CnlDataItem
    {
        public int CnlNum { get; set; }
        public double Val { get; set; }
        public int Stat { get; set; }
        public string Text { get; set; } = "";
        public string Color { get; set; } = "";
    }

    public class ChatHistoryResult
    {
        public int ItemId { get; set; }
        public List<ChatMessage> Messages { get; set; } = [];
    }

    public class PostChatMessageRequest
    {
        public int ItemId { get; set; }
        public string Text { get; set; } = "";
    }

    public class DeleteChatMessageRequest
    {
        public int ItemId { get; set; }
        public long MessageId { get; set; }
    }

    public class SaveCommissionedRequest
    {
        public int ItemId { get; set; }
        public bool IsCommissioned { get; set; }
    }

    public class SaveCommissionedBulkRequest
    {
        public List<int> ItemIds { get; set; } = [];
        public bool IsCommissioned { get; set; }
    }

    public class PostAckRequest
    {
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public long FloodStartMs { get; set; }
        public string Comment { get; set; } = "";
    }
}
