// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Mvc;
using Scada.Data.Entities;
using Scada.Data.Models;
using Scada.Storages;
using Scada.Web.Api;
using Scada.Web.Lang;
using Scada.Web.Plugins.PlgMap.Code;
using Scada.Web.Services;
using System.Collections.Concurrent;

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
        /// In-memory cache of loaded MapViews keyed by viewID.
        /// Avoids re-parsing large .map files on every data refresh.
        /// </summary>
        private static readonly ConcurrentDictionary<int, MapViewCacheEntry> ViewCache = new();
        private static readonly TimeSpan ViewCacheTTL = TimeSpan.FromMinutes(10);


        /// <summary>
        /// Gets a MapView from cache or loads it fresh.
        /// </summary>
        private MapView GetOrLoadMapView(int viewID, out string errMsg)
        {
            errMsg = null;

            // try cache first
            if (ViewCache.TryGetValue(viewID, out MapViewCacheEntry entry) &&
                DateTime.UtcNow - entry.LoadedAt < ViewCacheTTL)
            {
                return entry.View;
            }

            // try loading from local storage first (fast, avoids SCADA Server roundtrip)
            MapView mapView = viewID > 0 ? LoadMapViewFromStorage(viewID) : null;

            // fallback: try the standard ViewLoader (goes through SCADA Server)
            if (mapView == null && viewID > 0)
            {
                if (viewLoader.GetView(viewID, true, out mapView, out errMsg))
                {
                    // success via ViewLoader
                }
                else
                {
                    mapView = null;
                }
            }

            // fallback: scan for any .map file
            mapView ??= FindAndLoadFirstMapView();

            // cache the result
            if (mapView != null)
            {
                ViewCache[viewID] = new MapViewCacheEntry { View = mapView, LoadedAt = DateTime.UtcNow };
            }

            return mapView;
        }


        /// <summary>
        /// Tries to load a MapView by parsing the .map file directly from local storage.
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
                webContext.Log.WriteError(ex, PluginPhrases.ErrorLoadingView, viewEntity.ViewID);
                return null;
            }
        }

        /// <summary>
        /// Scans SortedViews for the first .map file and loads it from storage.
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
                webContext.Log.WriteError(ex, PluginPhrases.ErrorScanningStorage);
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
                    Type = m.Type == MarkerType.Circle ? null : m.Type.ToString().ToLowerInvariant(),
                    Lat = m.Latitude,
                    Lng = m.Longitude,
                    LatCnlNum = m.LatCnlNum,
                    LonCnlNum = m.LonCnlNum,
                    Name = m.Name,
                    Descr = m.Description,
                    Photo = string.IsNullOrEmpty(m.Photo) ? null : m.Photo,
                    StatusCnlNum = m.StatusCnlNum,
                    LinkViewID = m.LinkViewID,
                    Channels = [.. m.Channels.Select(c => new ChannelDto
                    {
                        CnlNum = c.CnlNum,
                        Alias = c.Alias
                    })],
                    StatusChannels = [.. m.StatusChannels.Select(c => new ChannelDto
                    {
                        CnlNum = c.CnlNum,
                        Alias = c.Alias
                    })],
                    ExtraChannelGroups = [.. m.ExtraChannelGroups.Select(g => new ChannelGroupDto
                    {
                        Name = g.Name,
                        Channels = [.. g.Channels.Select(c => new ChannelDto
                        {
                            CnlNum = c.CnlNum,
                            Alias = c.Alias
                        })]
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
                MapView mapView = GetOrLoadMapView(viewID, out string errMsg);

                if (mapView != null)
                {
                    return Dto<MapDataPacket>.Success(BuildMapDataPacket(mapView));
                }

                return Dto<MapDataPacket>.Fail(viewID > 0
                    ? (errMsg ?? WebPhrases.UnableLoadView)
                    : PluginPhrases.NoMapFilesFound);
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
                MapView mapView = GetOrLoadMapView(viewID, out string errMsg);

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

        /// <summary>
        /// Serves a photo file from the SCADA Views storage.
        /// Path is relative to the Views folder, e.g. "Photo/N.jpg".
        /// </summary>
        [HttpGet]
        public IActionResult GetPhoto(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("Path is required.");

            // Prevent directory traversal
            if (path.Contains("..") || path.Contains("~"))
                return BadRequest("Invalid path.");

            try
            {
                BinaryReader reader = webContext.Storage.OpenBinary(DataCategory.View, path);
                byte[] data;
                using (var ms = new System.IO.MemoryStream())
                {
                    reader.BaseStream.CopyTo(ms);
                    data = ms.ToArray();
                }
                reader.Dispose();

                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                string contentType = ext switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    ".bmp" => "image/bmp",
                    _ => "application/octet-stream"
                };

                return File(data, contentType);
            }
            catch (FileNotFoundException)
            {
                return NotFound("Photo not found: " + path);
            }
            catch (Exception ex)
            {
                webContext.Log.WriteError(ex, "Error loading photo: {0}", path);
                return StatusCode(500, "Error loading photo.");
            }
        }


        /// <summary>
        /// Cache entry for a loaded MapView.
        /// </summary>
        private class MapViewCacheEntry
        {
            public MapView View { get; set; }
            public DateTime LoadedAt { get; set; }
        }
    }
}
