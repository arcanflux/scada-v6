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

        private static ViewNode FindViewByScheme(List<ViewNode> nodes, string scheme)
        {
            if (int.TryParse(scheme, out int viewID))
                return FindViewByID(nodes, viewID);

            string normalizedScheme = scheme.Replace('\\', '/');
            string fileName = Path.GetFileName(normalizedScheme);
            string fileStem = Path.GetFileNameWithoutExtension(fileName);
            foreach (ViewNode node in nodes)
            {
                if (!node.IsEmpty)
                {
                    string normalizedNodePath = node.ShortPath.Replace('\\', '/');
                    string nodeFileName = Path.GetFileName(normalizedNodePath);
                    string nodeFileStem = Path.GetFileNameWithoutExtension(nodeFileName);

                    if (string.Equals(normalizedNodePath, normalizedScheme, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(nodeFileName, fileName, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(fileStem) &&
                         string.Equals(nodeFileStem, fileStem, StringComparison.OrdinalIgnoreCase)))
                        return node;
                }
            string fileName = Path.GetFileName(scheme.Replace('\\', '/'));
            foreach (ViewNode node in nodes)
            {
                if (!node.IsEmpty &&
                    (string.Equals(node.ShortPath, scheme, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(node.ShortPath, fileName, StringComparison.OrdinalIgnoreCase)))
                    return node;

                ViewNode found = FindViewByScheme(node.ChildNodes, scheme);
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

                foreach (ThermalCameraItem item in sortedItems)
                {
                    if (!string.IsNullOrEmpty(item.SchemeUrl))
                    {
                        string normalizedScheme = item.SchemeUrl.Replace('\\', '/');
                        string extension = Path.GetExtension(normalizedScheme);
                        bool isImage = extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                            extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                            extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
                            extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
                            extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);

                        if (!isImage)
                        {
                            ViewNode schemeNode = FindViewByScheme(userContext.Views.ViewNodes, item.SchemeUrl);
                            if (schemeNode != null)
                                item.SchemeViewUrl = Url.Content(schemeNode.ViewFrameUrl);
                        }
                    if (!string.IsNullOrEmpty(item.SchemeUrl) &&
                        (int.TryParse(item.SchemeUrl, out _) ||
                         string.Equals(Path.GetExtension(item.SchemeUrl), ".mim", StringComparison.OrdinalIgnoreCase)))
                    {
                        ViewNode schemeNode = FindViewByScheme(userContext.Views.ViewNodes, item.SchemeUrl);
                        if (schemeNode != null)
                            item.SchemeViewUrl = Url.Content(schemeNode.ViewFrameUrl);
                    }
                }

                ItemsJson = JsonSerializer.Serialize(sortedItems, JsonOpts);

                // Миграция user data на стабильные ID должна пройти до сериализации,
                // иначе первая загрузка страницы после обновления покажет тумблеры
                // по старым (уже не совпадающим) ключам.
                thermalCameraContext.MigrateUserData(view.Items);

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
