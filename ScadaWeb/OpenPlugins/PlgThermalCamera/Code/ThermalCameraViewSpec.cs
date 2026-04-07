// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Scada.Web.Plugins.PlgThermalCamera.Code
{
    /// <summary>
    /// Represents a thermal camera table view specification.
    /// <para>Представляет спецификацию представления таблицы тепловых камер.</para>
    /// </summary>
    public class ThermalCameraViewSpec : ViewSpec
    {
        public override string TypeCode => nameof(ThermalCameraTableView);

        public override string FileExtension => "map";

        public override string IconUrl => "";

        public override Type ViewType => typeof(ThermalCameraTableView);

        public override string GetFrameUrl(int viewID) => "~/ThermalCamera/ThermalCameraTable/" + viewID;
    }
}
