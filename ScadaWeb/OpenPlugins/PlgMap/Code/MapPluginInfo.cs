// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Lang;

namespace Scada.Web.Plugins.PlgMap.Code
{
    /// <summary>
    /// Represents information about the map plugin.
    /// <para>Представляет информацию о плагине карты.</para>
    /// </summary>
    internal class MapPluginInfo : LibraryInfo
    {
        /// <summary>
        /// The plugin code.
        /// </summary>
        public const string PluginCode = "PlgMap";

        /// <summary>
        /// Gets the plugin code.
        /// </summary>
        public override string Code => PluginCode;

        /// <summary>
        /// Gets the plugin name.
        /// </summary>
        public override string Name => Locale.IsRussian ?
            "Карта" :
            "Map";

        /// <summary>
        /// Gets the plugin description.
        /// </summary>
        public override string Descr => Locale.IsRussian ?
            "Плагин обеспечивает отображение маркеров SCADA на карте OpenStreetMap." :
            "The plugin provides displaying SCADA markers on an OpenStreetMap.";
    }
}
