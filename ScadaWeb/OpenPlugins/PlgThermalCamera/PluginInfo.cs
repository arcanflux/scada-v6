// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Lang;

namespace Scada.Web.Plugins.PlgThermalCamera
{
    internal class PluginInfo : LibraryInfo
    {
        public override string Code => "PlgThermalCamera";

        public override string Name => Locale.IsRussian ?
            "Тепловые камеры" :
            "Thermal Cameras";

        public override string Descr => Locale.IsRussian ?
            "Плагин обеспечивает отображение таблицы тепловых камер с данными каналов в реальном времени." :
            "The plugin provides a table view of thermal cameras with real-time channel data.";
    }
}
