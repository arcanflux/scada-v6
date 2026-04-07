// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Mvc;
using Scada.Data.Models;
using Scada.Web.Api;
using Scada.Web.Lang;
using Scada.Web.Plugins.PlgThermalCamera.Code;
using Scada.Web.Plugins.PlgThermalCamera.Models;
using Scada.Web.Services;

namespace Scada.Web.Plugins.PlgThermalCamera.Controllers
{
    /// <summary>
    /// Represents the thermal camera plugin web API.
    /// <para>Представляет веб-API плагина тепловых камер.</para>
    /// </summary>
    [ApiController]
    [Route("Api/ThermalCamera/[action]")]
    public class ThermalCameraApiController : ControllerBase
    {
        private readonly IWebContext webContext;
        private readonly IClientAccessor clientAccessor;
        private readonly IUserContext userContext;
        private readonly ThermalCameraContext thermalCameraContext;

        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public ThermalCameraApiController(IWebContext webContext, IClientAccessor clientAccessor,
            IUserContext userContext, ThermalCameraContext thermalCameraContext)
        {
            this.webContext = webContext;
            this.clientAccessor = clientAccessor;
            this.userContext = userContext;
            this.thermalCameraContext = thermalCameraContext;
        }

        /// <summary>
        /// Gets the current data for the specified channel numbers.
        /// </summary>
        public Dto<CurDataResult> GetCurData(string cnlNums)
        {
            try
            {
                int[] cnlNumArr = string.IsNullOrEmpty(cnlNums)
                    ? Array.Empty<int>()
                    : cnlNums.Split(',', StringSplitOptions.RemoveEmptyEntries)
                             .Select(s => int.TryParse(s.Trim(), out int n) ? n : 0)
                             .Where(n => n > 0)
                             .ToArray();

                if (cnlNumArr.Length == 0)
                    return Dto<CurDataResult>.Success(new CurDataResult());

                CnlData[] cnlDataArr = clientAccessor.ScadaClient.GetCurrentData(cnlNumArr, false, out _);
                CnlDataFormatter formatter = new(webContext.ConfigDatabase, userContext.TimeZone);

                Dictionary<int, CnlDataItem> dataItems = new();
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

        /// <summary>
        /// Gets the user data (comments and commissioned status).
        /// </summary>
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

        /// <summary>
        /// Saves a comment for a thermal camera item.
        /// </summary>
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

                if (thermalCameraContext.SaveUserData(userData, out string errMsg))
                    return Dto.Success();

                return Dto.Fail(errMsg);
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(SaveComment)));
                return Dto.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Saves the commissioned status for a thermal camera item.
        /// </summary>
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

                if (thermalCameraContext.SaveUserData(userData, out string errMsg))
                    return Dto.Success();

                return Dto.Fail(errMsg);
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(SaveCommissioned)));
                return Dto.Fail(ex.Message);
            }
        }
    }

    /// <summary>
    /// Represents the result of a current data request.
    /// </summary>
    public class CurDataResult
    {
        public string ServerTime { get; set; } = "";
        public Dictionary<int, CnlDataItem> Data { get; set; } = new();
    }

    /// <summary>
    /// Represents a single channel data item.
    /// </summary>
    public class CnlDataItem
    {
        public int CnlNum { get; set; }
        public double Val { get; set; }
        public int Stat { get; set; }
        public string Text { get; set; } = "";
        public string Color { get; set; } = "";
    }

    /// <summary>
    /// Represents a request to save a comment.
    /// </summary>
    public class SaveCommentRequest
    {
        public int ItemId { get; set; }
        public string Comment { get; set; } = "";
    }

    /// <summary>
    /// Represents a request to save the commissioned status.
    /// </summary>
    public class SaveCommissionedRequest
    {
        public int ItemId { get; set; }
        public bool IsCommissioned { get; set; }
    }
}
