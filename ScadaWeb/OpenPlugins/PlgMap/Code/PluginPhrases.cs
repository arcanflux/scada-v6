// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Lang;

namespace Scada.Web.Plugins.PlgMap.Code
{
    /// <summary>
    /// The phrases used by the plugin.
    /// <para>Фразы, используемые плагином.</para>
    /// </summary>
    internal static class PluginPhrases
    {
        // Scada.Web.Plugins.PlgMap.PlgMapLogic
        public static string MapMenuItem { get; private set; }

        // Scada.Web.Plugins.PlgMap.Controllers.MapApiController
        public static string ErrorLoadingView { get; private set; }
        public static string ErrorScanningStorage { get; private set; }
        public static string NoMapFilesFound { get; private set; }

        // Scada.Web.Plugins.PlgMap.Code.MapMarker
        public static string Status { get; private set; }

        public static void Init()
        {
            LocaleDict dict = Locale.GetDictionary("Scada.Web.Plugins.PlgMap.PlgMapLogic");
            MapMenuItem = dict[nameof(MapMenuItem)];

            dict = Locale.GetDictionary("Scada.Web.Plugins.PlgMap.Controllers.MapApiController");
            ErrorLoadingView = dict[nameof(ErrorLoadingView)];
            ErrorScanningStorage = dict[nameof(ErrorScanningStorage)];
            NoMapFilesFound = dict[nameof(NoMapFilesFound)];

            dict = Locale.GetDictionary("Scada.Web.Plugins.PlgMap.Code.MapMarker");
            Status = dict[nameof(Status)];
        }
    }
}
