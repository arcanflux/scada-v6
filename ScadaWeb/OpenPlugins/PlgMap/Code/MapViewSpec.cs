// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Web.Plugins;

namespace Scada.Web.Plugins.PlgMap.Code
{
    /// <summary>
    /// Represents a map view specification.
    /// <para>Представляет спецификацию представления карты.</para>
    /// </summary>
    public class MapViewSpec : ViewSpec
    {
        /// <summary>
        /// Gets the view type code.
        /// </summary>
        public override string TypeCode => nameof(MapView);

        /// <summary>
        /// Gets the extension of view files.
        /// </summary>
        public override string FileExtension => "map";

        /// <summary>
        /// Gets the view icon URL.
        /// </summary>
        public override string IconUrl => "~/plugins/Map/images/map-icon.png";

        /// <summary>
        /// Gets the view type.
        /// </summary>
        public override Type ViewType => typeof(MapView);


        /// <summary>
        /// Gets the view frame URL.
        /// </summary>
        public override string GetFrameUrl(int viewID) => "~/Map/MapView/" + viewID;
    }
}
