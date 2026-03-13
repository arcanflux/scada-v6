// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Scada.Web.Plugins.PlgMap.View
{
    /// <summary>
    /// Implements the plugin user interface for the Administrator application.
    /// <para>Реализует пользовательский интерфейс плагина для приложения Администратор.</para>
    /// </summary>
    public class PlgMapView : PluginView
    {
        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public PlgMapView()
        {
            Info = new MapPluginInfo();
        }
    }
}
