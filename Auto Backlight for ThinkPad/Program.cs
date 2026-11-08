/*
 * Copyright 2019 Parth Patel
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace Auto_Backlight_for_ThinkPad
{
    internal class Program
    {
        /// <summary>
        /// Provide a custom entry point for the program, supersede WPF App built-in entry point
        /// </summary>
        [STAThread]
        public static void Main(string[] commandLineArgs)
        {
            // PHASE30B7D3_SINGLE_EXE_DISPATCH_V1
            // Allows a second process of the SAME installed EXE to verify a bundle
            // without creating WPF, checking the single-instance GUI lock or elevating.
            if (commandLineArgs != null && commandLineArgs.Length > 0)
            {
                if (commandLineArgs[0] == SingleExeUpdateAssessment.Switch)
                {
                    Environment.ExitCode = SingleExeUpdateAssessment.Run(commandLineArgs);
                    return;
                }
                // PHASE30B7D5_PROTECTED_STAGE_DISPATCH_V1
                if (commandLineArgs[0] == ProtectedUpdateStaging.Switch)
                {
                    Environment.ExitCode = ProtectedUpdateStaging.Run(commandLineArgs);
                    return;
                }
                // PHASE30B7D6A_INSTALLED_MSI_AUDIT_DISPATCH_V1
                if (commandLineArgs[0] == InstalledMsiGuard.AuditSwitch)
                {
                    Environment.ExitCode = InstalledMsiGuard.RunAudit(commandLineArgs);
                    return;
                }
                // PHASE30B7D6B_UAC_PROBE_DISPATCH_V1
                // Strictly inert: DO NOT use for production update execution.
                if (commandLineArgs[0] == UacProbe.ParentSwitch ||
                    commandLineArgs[0] == UacProbe.ChildSwitch)
                {
                    Environment.ExitCode = UacProbe.Run(commandLineArgs);
                    return;
                }
                // PHASE30B7D6C_STAGE_UAC_LAB_DISPATCH_V1
                // Protected copying ONLY; never executes or installs the MSI.
                if (commandLineArgs[0] == StageUacHandoff.ParentSwitch ||
                    commandLineArgs[0] == StageUacHandoff.ChildSwitch)
                {
                    Environment.ExitCode = StageUacHandoff.Run(commandLineArgs);
                    return;
                }
                // PHASE30B7D6F_DETACHED_LIFECYCLE_LAB_V1
                // Strictly synthetic: detached PowerShell monitor, NO MSI or UAC.
                if (commandLineArgs[0] == DetachedLab.Switch)
                {
                    Environment.ExitCode = DetachedLab.Run(commandLineArgs);
                    return;
                }
                // PHASE30B7D6G_PROTECTED_PREFLIGHT_DISPATCH_V1
                // READ-ONLY. Never starts an installer or another process.
                if (commandLineArgs[0] == ProtectedInstallPreflight.Switch)
                {
                    Environment.ExitCode = ProtectedInstallPreflight.Run(commandLineArgs);
                    return;
                }
                // PHASE30B7D6H_DETACHED_PROTECTED_TRANSACTION_LAB_V1
                // Detached monitor validates hashes AFTER launcher exits; never MSI.
                if (commandLineArgs[0] == ProtectedTransactionHandoffLab.Switch)
                {
                    Environment.ExitCode = ProtectedTransactionHandoffLab.Run(commandLineArgs);
                    return;
                }
                // PHASE30B7D6M2_TRUSTED_INSTALLED_UAC_DISPATCH_V1
                // No MSI, stage writes, settings changes or automatic elevation.
                if (commandLineArgs[0] == TrustedUacBoundary.AuditSwitch ||
                    commandLineArgs[0] == TrustedUacBoundary.ParentSwitch ||
                    commandLineArgs[0] == TrustedUacBoundary.ChildSwitch)
                {
                    Environment.ExitCode = TrustedUacBoundary.Run(commandLineArgs);
                    return;
                }
                // PHASE30B7D6M3C_TRUSTED_PROTECTED_STAGE_DISPATCH_V1
                // Installed, trusted EXE only; protected COPY; never install MSI.
                if (commandLineArgs[0] == ProtectedHandoff.ParentSwitch ||
                    commandLineArgs[0] == ProtectedHandoff.ChildSwitch)
                {
                    Environment.ExitCode = ProtectedHandoff.Run(commandLineArgs);
                    return;
                }
                // PHASE30B7D6M3D_TRUSTED_READONLY_PREFLIGHT_DISPATCH_V1
                // Protected copy must already exist; no MSI, UAC from developer EXE,
                // or user-settings/task modifications.
                if (commandLineArgs[0] == ProtectedPreflightUi.ParentSwitch ||
                    commandLineArgs[0] == ProtectedPreflightUi.ChildSwitch)
                {
                    Environment.ExitCode = ProtectedPreflightUi.Run(commandLineArgs);
                    return;
                }
                // PHASE30B_PRODUCTION_APPROVED_CHILD_DISPATCH_RC1
                if (commandLineArgs[0] == ProductionUpdateInstaller.ChildSwitch)
                {
                    Environment.ExitCode = ProductionUpdateInstaller.RunApprovedChild(commandLineArgs);
                    return;
                }
                // PHASE30B7D6M3E_DISABLED_PRODUCTION_INSTALL_DISPATCH_V1
                // Future production CLI reserved, but ALWAYS refuses MSI in 6M.3E.
                if (commandLineArgs[0] == InstallConsentGate.ParentSwitch ||
                    commandLineArgs[0] == InstallConsentGate.ChildSwitch)
                {
                    Environment.ExitCode = InstallConsentGate.Run(commandLineArgs);
                    return;
                }
                // PHASE30B7D6M1_RELEASE_SAFETY_AUDIT_DISPATCH_V1
                // Read-only audit; returns HOLD until production updater is wired.
                if (commandLineArgs[0] == ReleaseSafetyGate.Switch)
                {
                    Environment.ExitCode = ReleaseSafetyGate.Run(commandLineArgs);
                    return;
                }
                // PHASE30B7D6I_DETACHED_MSI_LAB_DISPATCH_V1
                // Not wired to UI or automatic updater. Real install needs separate
                // elevated, explicit lab CLI plus acknowledgement; normal tests dry-run.
                if (commandLineArgs[0] == ProtectedMsiExecutorLab.DrySwitch ||
                    commandLineArgs[0] == ProtectedMsiExecutorLab.ExecuteSwitch)
                {
                    Environment.ExitCode = ProtectedMsiExecutorLab.Run(commandLineArgs);
                    return;
                }
                Environment.ExitCode = 10; // refuse unrecognized command-line modes
                return;
            }
            // PHASE30B_PRODUCTION_RESTART_GATE_RC1: worker holds this mutex through MSI.
            if (!ProductionUpdateInstaller.StartupAllowed())
            {
                Environment.ExitCode = 43; // updater in progress / uncertain lock state
                return;
            }
            DiagnosticsLog.Info("Process start. OS=" + Environment.OSVersion + "; CLR=" + Environment.Version);
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                DiagnosticsLog.Error("Unhandled exception", args.ExceptionObject as Exception);
            // Allow only a single instance of this application to run at once
            string proc = Process.GetCurrentProcess().ProcessName;
            if (Process.GetProcessesByName(proc).Length > 1)
            {
                MessageBox.Show(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product + " is already running, only 1 instance allowed.", "Already running", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
            }

            // Redirect loading of certain assemblies to internal embedded resources
            AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
            {
                string resourceName = new AssemblyName(e.Name).Name;
                using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(Program), resourceName + ".dll"))
                    if (res != null)
                    {
                        var len = res.Length;
                        var arr = new byte[len];
                        res.Read(arr, 0, (int)len);
                        return Assembly.Load(arr);
                    }
                return null;
            };

            // Back to WPF App built-in entry point
            try
            {
                App.Main();
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Application startup failed", ex);
                MessageBox.Show("The application could not start. See log:\n" +
                    DiagnosticsLog.FilePath + "\n\n" + ex.Message,
                    "Auto Backlight for ThinkPad", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
