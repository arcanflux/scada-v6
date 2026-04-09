// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Mvc;
using Scada.Data.Const;
using Scada.Data.Models;
using Scada.Protocol;
using Scada.Web.Api;
using Scada.Web.Lang;
using Scada.Web.Plugins.PlgThermalCamera.Code;
using Scada.Web.Plugins.PlgThermalCamera.Models;
using Scada.Web.Services;

namespace Scada.Web.Plugins.PlgThermalCamera.Controllers
{
    [ApiController]
    [Route("Api/ThermalCamera/[action]")]
    public class ThermalCameraApiController(
        IWebContext webContext,
        IClientAccessor clientAccessor,
        IUserContext userContext,
        ThermalCameraContext thermalCameraContext) : ControllerBase
    {
        public Dto<CurDataResult> GetCurData(string cnlNums)
        {
            try
            {
                int[] cnlNumArr = string.IsNullOrEmpty(cnlNums)
                    ? []
                    : cnlNums.Split(',', StringSplitOptions.RemoveEmptyEntries)
                             .Select(s => int.TryParse(s.Trim(), out int n) ? n : 0)
                             .Where(n => n > 0)
                             .ToArray();

                if (cnlNumArr.Length == 0)
                    return Dto<CurDataResult>.Success(new CurDataResult());

                CnlData[] cnlDataArr = clientAccessor.ScadaClient.GetCurrentData(cnlNumArr, false, out _);
                CnlDataFormatter formatter = new(webContext.ConfigDatabase, userContext.TimeZone);

                Dictionary<int, CnlDataItem> dataItems = [];
                for (int i = 0; i < cnlNumArr.Length; i++)
                {
                    int cnlNum = cnlNumArr[i];
                    CnlData cnlData = i < cnlDataArr.Length ? cnlDataArr[i] : CnlData.Empty;
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

                return Dto<CurDataResult>.Success(new CurDataResult
                {
                    ServerTime = DateTime.UtcNow.ToString("o"),
                    Data = dataItems
                });
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(GetCurData)));
                return Dto<CurDataResult>.Fail(ex.Message);
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
        public Dto SaveComment([FromBody] SaveCommentRequest request)
        {
            try
            {
                ThermalCameraUserData userData = thermalCameraContext.LoadUserData();

                if (!userData.Entries.TryGetValue(request.ItemId, out UserDataEntry entry))
                {
                    entry = new UserDataEntry();
                    userData.Entries[request.ItemId] = entry;
                }

                entry.Comment = request.Comment ?? "";

                return thermalCameraContext.SaveUserData(userData, out string errMsg)
                    ? Dto.Success()
                    : Dto.Fail(errMsg);
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(SaveComment)));
                return Dto.Fail(ex.Message);
            }
        }

        [HttpPost]
        public Dto SaveCommissioned([FromBody] SaveCommissionedRequest request)
        {
            try
            {
                ThermalCameraUserData userData = thermalCameraContext.LoadUserData();

                if (!userData.Entries.TryGetValue(request.ItemId, out UserDataEntry entry))
                {
                    entry = new UserDataEntry();
                    userData.Entries[request.ItemId] = entry;
                }

                entry.IsCommissioned = request.IsCommissioned;

                return thermalCameraContext.SaveUserData(userData, out string errMsg)
                    ? Dto.Success()
                    : Dto.Fail(errMsg);
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(SaveCommissioned)));
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
    }

    public class CurDataResult
    {
        public string ServerTime { get; set; } = "";
        public Dictionary<int, CnlDataItem> Data { get; set; } = [];
    }

    public class CnlDataItem
    {
        public int CnlNum { get; set; }
        public double Val { get; set; }
        public int Stat { get; set; }
        public string Text { get; set; } = "";
        public string Color { get; set; } = "";
    }

    public class SaveCommentRequest
    {
        public int ItemId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class SaveCommissionedRequest
    {
        public int ItemId { get; set; }
        public bool IsCommissioned { get; set; }
    }
}
