// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Mvc.RazorPages;
using Scada.Web.Plugins.PlgThermalCamera.Code;
using Scada.Web.Plugins.PlgThermalCamera.Models;
using Scada.Web.Services;
using System.Text.Json;

namespace Scada.Web.Plugins.PlgThermalCamera.Areas.ThermalCamera.Pages
{
    /// <summary>
    /// Represents the thermal camera table page model.
    /// <para>Представляет модель страницы таблицы тепловых камер.</para>
    /// </summary>
    public class ThermalCameraTableModel : PageModel
    {
        private readonly IUserContext userContext;
        private readonly IViewLoader viewLoader;
        private readonly ThermalCameraContext thermalCameraContext;

        public ThermalCameraTableModel(IUserContext userContext, IViewLoader viewLoader,
            ThermalCameraContext thermalCameraContext)
        {
            this.userContext = userContext;
            this.viewLoader = viewLoader;
            this.thermalCameraContext = thermalCameraContext;
        }

        /// <summary>
        /// Gets the JSON-serialized thermal camera items for client-side rendering.
        /// </summary>
        public string ItemsJson { get; private set; } = "[]";

        /// <summary>
        /// Gets the JSON-serialized user data (comments, commissioned status).
        /// </summary>
        public string UserDataJson { get; private set; } = "{}";

        /// <summary>
        /// Gets the error message, if any.
        /// </summary>
        public string ErrorMessage { get; private set; } = "";

        public void OnGet(int? id)
        {
            int viewID = id ?? userContext.Views.GetFirstViewID() ?? 0;

            if (viewLoader.GetView(viewID, true, out ThermalCameraTableView view, out string errMsg))
            {
                ViewData["Title"] = view.Title;

                // Sort items by district number
                List<ThermalCameraItem> sortedItems = view.Config.Items
                    .OrderBy(i => i.DistrictNumber)
                    .ThenBy(i => i.Name)
                    .ToList();

                ItemsJson = JsonSerializer.Serialize(sortedItems, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                // Load user data
                ThermalCameraUserData userData = thermalCameraContext.LoadUserData();
                UserDataJson = JsonSerializer.Serialize(userData.Entries, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            else
            {
                ViewData["Title"] = "Тепловые камеры";
                ErrorMessage = errMsg;
            }
        }
    }
}
