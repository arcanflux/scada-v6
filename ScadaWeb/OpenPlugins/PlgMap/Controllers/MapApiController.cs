// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Mvc;
using Scada.Data.Entities;
using Scada.Data.Models;
using Scada.Lang;
using Scada.Storages;
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
        /// Tries to load a MapView by parsing the .map file directly from local storage.
        /// Used as a fallback when the ViewLoader cannot retrieve the view from the server.
        /// </summary>
        private MapView LoadMapViewFromStorage(int viewID)
        {
            View viewEntity = webContext.ConfigDatabase.ViewTable.GetItem(viewID);

            if (viewEntity == null || string.IsNullOrEmpty(viewEntity.Path))
                return null;

            try
            {
                using BinaryReader reader = webContext.Storage.OpenBinary(DataCategory.View, viewEntity.Path);
                MapView mapView = new(viewEntity);
                mapView.LoadView(reader.BaseStream);
                return mapView;
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex, Locale.IsRussian ?
                    "Ошибка при загрузке представления карты из хранилища для ид. {0}" :
                    "Error loading map view from storage for ID {0}", viewID);
                return null;
            }
        }

        /// <summary>
        /// Builds the API response packet from a loaded MapView.
        /// </summary>
        private static MapDataPacket BuildMapDataPacket(MapView mapView)
        {
            return new MapDataPacket
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
                    return Dto<MapDataPacket>.Success(BuildMapDataPacket(mapView));
                }

                // fallback: try loading the .map file directly from local storage
                mapView = LoadMapViewFromStorage(viewID);

                if (mapView != null)
                {
                    return Dto<MapDataPacket>.Success(BuildMapDataPacket(mapView));
                }

                return Dto<MapDataPacket>.Fail(errMsg);
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
                if (!viewLoader.GetView(viewID, out MapView mapView, out string errMsg))
                {
                    // fallback: try loading from local storage
                    mapView = LoadMapViewFromStorage(viewID);
                }

                if (mapView != null)
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
                    return Dto<MarkerCurData>.Fail(errMsg ?? WebPhrases.UnableLoadView);
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
