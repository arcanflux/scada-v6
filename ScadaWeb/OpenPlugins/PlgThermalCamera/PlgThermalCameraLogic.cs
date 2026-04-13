// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Scada.Lang;
using Scada.Web.Lang;
using Scada.Web.Plugins.PlgThermalCamera.Code;
using Scada.Web.Services;

namespace Scada.Web.Plugins.PlgThermalCamera
{
    /// <summary>
    /// Implements the plugin logic.
    /// <para>Реализует логику плагина.</para>
    /// </summary>
    public class PlgThermalCameraLogic : PluginLogic
    {
        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public PlgThermalCameraLogic(IWebContext webContext)
            : base(webContext)
        {
            Info = new PluginInfo();
        }

        /// <summary>
        /// Gets the view specifications.
        /// </summary>
        public override List<ViewSpec> ViewSpecs => [new ThermalCameraViewSpec()];

        /// <summary>
        /// Gets the CSS style URLs for the main layout.
        /// </summary>
        public override List<string> StyleUrls =>
        [
            "~/plugins/ThermalCamera/css/thermal-camera.css"
        ];

        /// <summary>
        /// Loads language dictionaries.
        /// </summary>
        public override void LoadDictionaries()
        {
            if (!Locale.LoadDictionaries(AppDirs.LangDir, Code, out string errMsg))
                Log.WriteError(WebPhrases.PluginMessage, Code, errMsg);
        }

        /// <summary>
        /// Adds services to the DI container.
        /// </summary>
        public override void AddServices(IServiceCollection services)
        {
            services.AddSingleton<ThermalCameraContext>();
        }
    }
}
