// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Mvc.RazorPages;
using Scada.Data.Const;
using Scada.Web.Plugins.PlgThermalCamera.Code;
using Scada.Web.Plugins.PlgThermalCamera.Models;
using Scada.Web.Services;
using Scada.Web.TreeView;
using System.Text.Json;

namespace Scada.Web.Plugins.PlgThermalCamera.Areas.ThermalCamera.Pages
{
    public class ThermalCameraTableModel(
        IUserContext userContext,
        IViewLoader viewLoader,
        ThermalCameraContext thermalCameraContext) : PageModel
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public int ViewID { get; private set; }
        public string ItemsJson { get; private set; } = "[]";
        public string UserDataJson { get; private set; } = "{}";
        public string ErrorMessage { get; private set; } = "";
        public bool IsAdmin { get; private set; }

        public int MainViewID { get; private set; }
        public string MainViewFrameUrl { get; private set; } = "";
        public string MainViewPageUrl { get; private set; } = "";
        public int MapViewID { get; private set; }
        public string MapViewFrameUrl { get; private set; } = "";
        public string MapViewPageUrl { get; private set; } = "";

        private static ViewNode FindViewByID(List<ViewNode> nodes, int viewID)
        {
            foreach (ViewNode node in nodes)
            {
                if (!node.IsEmpty && node.ViewID == viewID)
                    return node;
                ViewNode found = FindViewByID(node.ChildNodes, viewID);
                if (found != null) return found;
            }
            return null;
        }

        public void OnGet(int? id)
        {
            int viewID = id ?? userContext.Views.GetFirstViewID() ?? 0;
            ViewID = viewID;
            IsAdmin = userContext.UserEntity?.RoleID == RoleID.Administrator;

            ViewNode mainNode = FindViewByID(userContext.Views.ViewNodes, 1);
            if (mainNode != null)
            {
                MainViewID = mainNode.ViewID;
                MainViewFrameUrl = mainNode.ViewFrameUrl;
                MainViewPageUrl = mainNode.Url;
            }

            ViewNode mapNode = FindViewByID(userContext.Views.ViewNodes, 2);
            if (mapNode != null)
            {
                MapViewID = mapNode.ViewID;
                MapViewFrameUrl = mapNode.ViewFrameUrl;
                MapViewPageUrl = mapNode.Url;
            }

            if (viewLoader.GetView(viewID, true, out ThermalCameraTableView view, out string errMsg))
            {
                ViewData["Title"] = view.Title;

                if (view.Items.Count == 0)
                {
                    ErrorMessage = "Файл карты (.map) не указан или не содержит объектов ТК (Location с Type=Triangle). " +
                        "Укажите путь к файлу .map в поле «Путь» настроек представления.";
                    return;
                }

                var sortedItems = view.Items
                    .OrderBy(i => i.DistrictNumber)
                    .ThenBy(i => i.Name)
                    .ToList();

                ItemsJson = JsonSerializer.Serialize(sortedItems, JsonOpts);

                ThermalCameraUserData userData = thermalCameraContext.LoadUserData();
                UserDataJson = JsonSerializer.Serialize(userData.Entries, JsonOpts);
            }
            else
            {
                ViewData["Title"] = "Тепловые камеры";
                ErrorMessage = errMsg;
            }
        }
    }
}
