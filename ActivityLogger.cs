/****************************************************************************

    ActivityLogger.cs

    Minimal save-path diagnostics helper writing to the Visual Studio
    Activity Log.

------------------------------------------------------------- LICENSE BEGINS HERE--------------------------------------------------------------------------------------

Copyright (c) Microsoft Corporation
All rights reserved.

Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0

THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE, MERCHANTABLITY OR NON-INFRINGEMENT.

See the Apache Version 2.0 License for specific language governing permissions and limitations under the License.
-------------------------------------------------------------- LICENSE ENDS HERE -----------------------------------------------------------------------------------------
****************************************************************************/
using System;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.OpenXMLEditor
{
    /// <summary>
    /// Writes diagnostic messages to the Visual Studio Activity Log
    /// (%AppData%\Microsoft\VisualStudio\&lt;version&gt;\ActivityLog.xml).
    /// Used at save time only (never per keystroke), so the volume is low.
    /// </summary>
    internal static class ActivityLogger
    {
        private static IServiceProvider serviceProvider;

        public static void Initialize(IServiceProvider serviceProvider)
        {
            ActivityLogger.serviceProvider = serviceProvider;
        }

        public static void Log(string message)
        {
            try
            {
                if (serviceProvider == null)
                {
                    return;
                }

                IVsActivityLog log = serviceProvider.GetService(typeof(SVsActivityLog)) as IVsActivityLog;
                if (log != null)
                {
                    log.LogEntry((uint)__ACTIVITYLOG_ENTRYTYPE.ALE_INFORMATION,
                                 AboutForm.AssemblyTitle,
                                 message);
                }
            }
            catch
            {
                // Logging must never break the save path.
            }
        }
    }
}
