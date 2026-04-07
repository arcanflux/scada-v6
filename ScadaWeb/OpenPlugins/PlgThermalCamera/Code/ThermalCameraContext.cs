// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Web.Plugins.PlgThermalCamera.Models;
using Scada.Web.Services;

namespace Scada.Web.Plugins.PlgThermalCamera.Code
{
    /// <summary>
    /// Provides shared context for the thermal camera plugin.
    /// Stores user-editable data (comments, commissioned status).
    /// <para>Предоставляет общий контекст для плагина тепловых камер.</para>
    /// </summary>
    public class ThermalCameraContext
    {
        private readonly IWebContext webContext;
        private readonly object lockObj = new();

        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public ThermalCameraContext(IWebContext webContext)
        {
            this.webContext = webContext;
        }

        /// <summary>
        /// Gets the path to the plugin storage file for user data.
        /// </summary>
        public string GetUserDataFilePath()
        {
            return Path.Combine(webContext.AppDirs.StorageDir, "PlgThermalCamera", "UserData.xml");
        }

        /// <summary>
        /// Loads user-editable data (comments and commissioned status).
        /// </summary>
        public ThermalCameraUserData LoadUserData()
        {
            lock (lockObj)
            {
                ThermalCameraUserData userData = new();
                string fileName = GetUserDataFilePath();

                if (userData.Load(fileName, out string errMsg))
                    return userData;

                webContext.Log.WriteError("PlgThermalCamera: " + errMsg);
                return userData;
            }
        }

        /// <summary>
        /// Saves user-editable data (comments and commissioned status).
        /// </summary>
        public bool SaveUserData(ThermalCameraUserData userData, out string errMsg)
        {
            lock (lockObj)
            {
                string fileName = GetUserDataFilePath();
                return userData.Save(fileName, out errMsg);
            }
        }
    }
}
