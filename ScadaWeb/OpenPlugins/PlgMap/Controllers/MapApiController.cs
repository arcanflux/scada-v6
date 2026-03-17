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
    public class MapApiController(IWebContext webContext, IUserContext userContext,
        IClientAccessor clientAccessor, IViewLoader viewLoader) : ControllerBase
    {


        /// <summary>
        /// Tries to load a MapView by parsing the .map file directly from local storage.
        /// Used as a fallback when the ViewLoader cannot retrieve the view from the server.
        /// </summary>
        private MapView LoadMapViewFromStorage(int viewID)
        {
            View viewEntity = webContext.ConfigDatabase.ViewTable.GetItem(viewID);

            if (viewEntity == null || string.IsNullOrEmpty(viewEntity.Path))
                return null;

            return LoadMapViewFromPath(viewEntity);
        }

        /// <summary>
        /// Loads a MapView from a View entity's path in storage.
        /// </summary>
        private MapView LoadMapViewFromPath(View viewEntity)
        {
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
                    "Error loading map view from storage for ID {0}", viewEntity.ViewID);
                return null;
            }
        }

        /// <summary>
        /// Scans SortedViews for the first .map file and loads it from storage.
        /// Used as a last-resort fallback when viewID is 0 or invalid.
        /// </summary>
        private MapView FindAndLoadFirstMapView()
        {
            // First, scan the config database for registered .map views
            foreach (var viewEntity in webContext.ConfigDatabase.SortedViews)
            {
                if (!viewEntity.Hidden &&
                    viewEntity.Path != null &&
                    viewEntity.Path.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
                {
                    MapView mapView = LoadMapViewFromPath(viewEntity);
                    if (mapView != null)
                        return mapView;
                }
            }

            // Second, scan storage directly for .map files
            try
            {
                ICollection<string> mapFiles = webContext.Storage.GetFileList(
                    DataCategory.View, "", "*.map");

                foreach (string filePath in mapFiles)
                {
                    try
                    {
                        View fakeEntity = new() { ViewID = 0, Path = filePath };
                        using BinaryReader reader = webContext.Storage.OpenBinary(DataCategory.View, filePath);
                        MapView mapView = new(fakeEntity);
                        mapView.LoadView(reader.BaseStream);
                        return mapView;
                    }
                    catch
                    {
                        // skip files that fail to parse
                    }
                }
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex, Locale.IsRussian ?
                    "Ошибка при поиске файлов карт в хранилище" :
                    "Error scanning storage for map files");
            }

            return null;
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
                    MaxZoom = mapView.MapConfig.MaxZoom,
                    TileUrlTemplate = mapView.MapConfig.TileUrlTemplate
                },
                Markers = [.. mapView.Markers.Select(m => new MarkerDto
                {
                    Id = m.Id,
                    Lat = m.Latitude,
                    Lng = m.Longitude,
                    Name = m.Name,
                    Descr = m.Description,
                    StatusCnlNum = m.StatusCnlNum,
                    LinkViewID = m.LinkViewID,
                    Channels = [.. m.Channels.Select(c => new ChannelDto
                    {
                        CnlNum = c.CnlNum,
                        Alias = c.Alias
                    })]
                })]
            };
        }

        /// <summary>
        /// Gets the map configuration and marker definitions.
        /// </summary>
        public Dto<MapDataPacket> GetMapData(int viewID)
        {
            try
            {
                string errMsg = null;
                MapView mapView = null;

                if (viewID > 0 && viewLoader.GetView(viewID, true, out mapView, out errMsg))
                {
                    return Dto<MapDataPacket>.Success(BuildMapDataPacket(mapView));
                }

                // fallback 1: try loading the specific view from local storage
                MapView fallbackView = viewID > 0 ? LoadMapViewFromStorage(viewID) : null;

                // fallback 2: scan for any .map file in config database and storage
                fallbackView ??= FindAndLoadFirstMapView();

                if (fallbackView != null)
                {
                    return Dto<MapDataPacket>.Success(BuildMapDataPacket(fallbackView));
                }

                return Dto<MapDataPacket>.Fail(viewID > 0
                    ? (errMsg ?? WebPhrases.UnableLoadView)
                    : (Locale.IsRussian
                        ? "Не найдены файлы карт (.map) в хранилище"
                        : "No map files (.map) found in storage"));
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
                MapView mapView = null;
                string errMsg = null;

                if (viewID > 0)
                {
                    if (!viewLoader.GetView(viewID, out mapView, out errMsg))
                    {
                        // fallback: try loading from local storage by ID
                        mapView = LoadMapViewFromStorage(viewID);
                    }
                }

                // fallback: scan for any .map file
                mapView ??= FindAndLoadFirstMapView();

                if (mapView != null)
                {
                    int[] cnlNums = [.. mapView.CnlNumList];
                    CnlData[] cnlDataArr = cnlNums.Length > 0
                        ? clientAccessor.ScadaClient.GetCurrentData(cnlNums, false, out _)
                        : [];

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
