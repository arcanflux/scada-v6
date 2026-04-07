// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Web.Plugins.PlgThermalCamera.Models;
using Scada.Web.Services;

namespace Scada.Web.Plugins.PlgThermalCamera.Code
{
    public class ThermalCameraContext(IWebContext webContext)
    {
        private readonly object lockObj = new();

        public string GetUserDataFilePath()
        {
            return Path.Combine(webContext.AppDirs.StorageDir, "PlgThermalCamera", "UserData.xml");
        }

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
