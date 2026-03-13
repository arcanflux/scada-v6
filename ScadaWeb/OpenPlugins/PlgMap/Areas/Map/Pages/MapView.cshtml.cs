// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Mvc.RazorPages;
using Scada.Web.Services;

namespace Scada.Web.Plugins.PlgMap.Areas.Map.Pages
{
    /// <summary>
    /// Represents a map view page.
    /// <para>Представляет страницу представления карты.</para>
    /// </summary>
    public class MapViewModel : PageModel
    {
        private readonly IWebContext webContext;
        private readonly IUserContext userContext;

        public MapViewModel(IWebContext webContext, IUserContext userContext)
        {
            this.webContext = webContext;
            this.userContext = userContext;
        }

        public int ViewID { get; set; }
        public int RefreshRate { get; set; }

        public void OnGet(int? id)
        {
            ViewID = id ?? userContext.Views.GetFirstViewID() ?? 0;
            RefreshRate = webContext.AppConfig.DisplayOptions.RefreshRate;
            ViewData["Title"] = "Map " + ViewID;
        }
    }
}
