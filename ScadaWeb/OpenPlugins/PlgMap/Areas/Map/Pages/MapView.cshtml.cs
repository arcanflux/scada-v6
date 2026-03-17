// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Mvc.RazorPages;
using Scada.Lang;
using Scada.Web.Services;

namespace Scada.Web.Plugins.PlgMap.Areas.Map.Pages
{
    /// <summary>
    /// Represents a map view page.
    /// <para>Представляет страницу представления карты.</para>
    /// </summary>
    public class MapViewModel(IWebContext webContext, IUserContext userContext) : PageModel
    {

        public int ViewID { get; set; }
        public int RefreshRate { get; set; }

        /// <summary>
        /// Gets the JavaScript localization dictionary.
        /// </summary>
        public dynamic JsDict { get; private set; }

        public void OnGet(int? id)
        {
            ViewID = id ?? FindFirstMapViewID() ?? 0;
            RefreshRate = webContext.AppConfig.DisplayOptions.RefreshRate;
            JsDict = Locale.GetDictionary("Scada.Web.Plugins.PlgMap.Areas.Map.Pages.MapView.Js");

            LocaleDict pageDict = Locale.GetDictionary("Scada.Web.Plugins.PlgMap.Areas.Map.Pages.MapView");
            ViewData["Title"] = pageDict["PageTitle"] + " " + ViewID;
        }

        /// <summary>
        /// Finds the first view with a .map file extension in the configuration database.
        /// </summary>
        private int? FindFirstMapViewID()
        {
            foreach (var viewEntity in webContext.ConfigDatabase.SortedViews)
            {
                if (!viewEntity.Hidden &&
                    viewEntity.Path != null &&
                    viewEntity.Path.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
                {
                    return viewEntity.ViewID;
                }
            }

            return userContext.Views.GetFirstViewID();
        }
    }
}
