// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Mvc;
using Scada.Data.Models;
using Scada.Web.Api;
using Scada.Web.Lang;
using Scada.Web.Plugins.PlgMap.Code;
using Scada.Web.Services;

namespace Scada.Web.Plugins.PlgMap.Controllers
{
    /// <summary>
    /// Represents a web API for accessing map data.
    /// <para>Представляет веб API для доступа к данным карты.</para>
    /// </summary>
    [ApiController]
    [Route("Api/Map/[action]")]
    public class MapApiController : ControllerBase
    {
        private readonly IWebContext webContext;
        private readonly IUserContext userContext;
        private readonly IClientAccessor clientAccessor;
        private readonly IViewLoader viewLoader;


        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public MapApiController(IWebContext webContext, IUserContext userContext,
            IClientAccessor clientAccessor, IViewLoader viewLoader)
        {
            this.webContext = webContext;
            this.userContext = userContext;
            this.clientAccessor = clientAccessor;
            this.viewLoader = viewLoader;
        }


        /// <summary>
        /// Gets the map configuration and marker definitions.
        /// </summary>
        public Dto<MapDataPacket> GetMapData(int viewID)
        {
            try
            {
                if (viewLoader.GetView(viewID, true, out MapView mapView, out string errMsg))
                {
                    MapDataPacket packet = new()
                    {
                        ViewStamp = mapView.ViewStamp,
                        Config = new MapConfigDto
                        {
                            CenterLat = mapView.MapConfig.CenterLat,
                            CenterLng = mapView.MapConfig.CenterLng,
                            Zoom = mapView.MapConfig.Zoom,
                            MinZoom = mapView.MapConfig.MinZoom,
                            MaxZoom = mapView.MapConfig.MaxZoom
                        },
                        Markers = mapView.Markers.Select(m => new MarkerDto
                        {
                            Id = m.Id,
                            Lat = m.Latitude,
                            Lng = m.Longitude,
                            Name = m.Name,
                            Channels = m.Channels.Select(c => new ChannelDto
                            {
                                CnlNum = c.CnlNum,
                                Alias = c.Alias
                            }).ToList()
                        }).ToList()
                    };

                    return Dto<MapDataPacket>.Success(packet);
                }
                else
                {
                    return Dto<MapDataPacket>.Fail(errMsg);
                }
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(GetMapData)));
                return Dto<MapDataPacket>.Fail(ex.Message);
            }
        }

        /// <summary>
        /// Gets the current data for all channels used by the map view.
        /// </summary>
        public Dto<MarkerCurData> GetCurData(int viewID)
        {
            try
            {
                if (viewLoader.GetView(viewID, out MapView mapView, out string errMsg))
                {
                    int[] cnlNums = mapView.CnlNumList.ToArray();
                    CnlData[] cnlDataArr = cnlNums.Length > 0
                        ? clientAccessor.ScadaClient.GetCurrentData(cnlNums, false, out _)
                        : Array.Empty<CnlData>();

                    CnlDataFormatter formatter = new(webContext.ConfigDatabase, userContext.TimeZone);
                    List<MarkerCurDataRecord> records = new(cnlNums.Length);

                    for (int i = 0; i < cnlNums.Length; i++)
                    {
                        CnlData cnlData = i < cnlDataArr.Length ? cnlDataArr[i] : CnlData.Empty;
                        CnlDataFormatted formatted = formatter.FormatCnlData(cnlData, cnlNums[i], true);

                        records.Add(new MarkerCurDataRecord
                        {
                            CnlNum = cnlNums[i],
                            Val = double.IsNaN(cnlData.Val) ? 0 : cnlData.Val,
                            Stat = cnlData.Stat,
                            Text = formatted.DispVal
                        });
                    }

                    return Dto<MarkerCurData>.Success(new MarkerCurData { Records = records });
                }
                else
                {
                    return Dto<MarkerCurData>.Fail(errMsg);
                }
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex.BuildErrorMessage(WebPhrases.ErrorInWebApi, nameof(GetCurData)));
                return Dto<MarkerCurData>.Fail(ex.Message);
            }
        }
    }
}
