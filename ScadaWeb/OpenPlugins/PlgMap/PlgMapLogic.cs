// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Lang;
using Scada.Web.Lang;
using Scada.Web.Plugins.PlgMap.Code;
using Scada.Web.Services;
using Scada.Web.TreeView;
using Scada.Web.Users;

namespace Scada.Web.Plugins.PlgMap
{
    /// <summary>
    /// Implements the map plugin logic.
    /// <para>Реализует логику плагина карты.</para>
    /// </summary>
    public class PlgMapLogic : PluginLogic
    {
        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public PlgMapLogic(IWebContext webContext)
            : base(webContext)
        {
            Info = new MapPluginInfo();
        }


        /// <summary>
        /// Gets the view specifications.
        /// </summary>
        public override List<ViewSpec> ViewSpecs => [new MapViewSpec()];


        /// <summary>
        /// Gets the menu items available for the user.
        /// </summary>
        public override List<MenuItem> GetUserMenuItems(User user, UserRights userRights)
        {
            return
            [
                new MenuItem
                {
                    Text = PluginPhrases.MapMenuItem,
                    Url = "~/Map/MapView",
                    SortOrder = MenuItemSortOrder.First
                }
            ];
        }


        /// <summary>
        /// Loads language dictionaries.
        /// </summary>
        public override void LoadDictionaries()
        {
            if (!Locale.LoadDictionaries(AppDirs.LangDir, Code, out string errMsg))
                Log.WriteError(WebPhrases.PluginMessage, Code, errMsg);

            PluginPhrases.Init();
        }
    }
}
