// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Admin.Extensions.ExtChannelSearch.Code;
using Scada.Admin.Extensions.ExtChannelSearch.Controls;
using Scada.Admin.Lang;
using Scada.Forms;
using Scada.Lang;
using System.Windows.Forms;

namespace Scada.Admin.Extensions.ExtChannelSearch
{
    /// <summary>
    /// Represents an extension logic.
    /// <para>Представляет логику расширения.</para>
    /// </summary>
    public class ExtChannelSearchLogic : ExtensionLogic
    {
        private CtrlChannelSearch ctrlChannelSearch; // the toolbar search field control


        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        public ExtChannelSearchLogic(IAdminContext adminContext)
            : base(adminContext)
        {
        }


        /// <summary>
        /// Gets the channel search control, creating it on first access.
        /// </summary>
        private CtrlChannelSearch ChannelSearchControl
        {
            get
            {
                // do not create IWin32Window objects before calling SetCompatibleTextRenderingDefault
                if (ctrlChannelSearch == null)
                {
                    ctrlChannelSearch = new CtrlChannelSearch(AdminContext);
                    FormTranslator.Translate(ctrlChannelSearch, typeof(CtrlChannelSearch).FullName);
                }

                return ctrlChannelSearch;
            }
        }

        /// <summary>
        /// Gets the extension code.
        /// </summary>
        public override string Code
        {
            get
            {
                return "ExtChannelSearch";
            }
        }

        /// <summary>
        /// Gets the extension name.
        /// </summary>
        public override string Name
        {
            get
            {
                return Locale.IsRussian ? "Поиск каналов" : "Channel Search";
            }
        }

        /// <summary>
        /// Gets the extension description.
        /// </summary>
        public override string Descr
        {
            get
            {
                return Locale.IsRussian ?
                    "Расширение добавляет в панель инструментов поле для поиска каналов по имени, коду или номеру." :
                    "The extension adds a toolbar field for searching channels by name, code or number.";
            }
        }


        /// <summary>
        /// Loads language dictionaries.
        /// </summary>
        public override void LoadDictionaries()
        {
            if (!Locale.LoadDictionaries(AdminContext.AppDirs.LangDir, Code, out string errMsg))
                AdminContext.ErrLog.WriteError(AdminPhrases.ExtensionMessage, Code, errMsg);

            ExtensionPhrases.Init();
        }

        /// <summary>
        /// Gets tool buttons to add to the toolbar.
        /// </summary>
        public override ToolStripItem[] GetToobarButtons()
        {
            return ChannelSearchControl.GetToolbarButtons();
        }
    }
}
