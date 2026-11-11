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

using System.Collections;
using System;
using System.Configuration.Install;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace Auto_Backlight_for_ThinkPad
{
    /// <summary>
    /// Additional operations to run during Install/Uninstall when invoked by the .msi installer
    /// </summary>
    [RunInstaller(true)]
    public class Installer : System.Configuration.Install.Installer
    {
        // Save the scheduled task state BEFORE
        // RemoveExistingProducts removes the previous MSI package.
        // Only boolean and string values are stored in the Installer state dictionary.
        private const string S_Captured = "P30B3_TaskCaptured";
        private const string S_Existed = "P30B3_TaskExisted";
        private const string S_Enabled = "P30B3_TaskEnabled";
        private const string S_Xml = "P30B3_TaskXml";
        private const string S_Upgrade = "P30B3_RelatedProductFound";
        private const string UpgradeCode = "{A78F780D-063F-4852-AC1B-D20FB7165BA2}";

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiEnumRelatedProducts(string upgradeCode, uint reserved,
            uint productIndex, StringBuilder productCode);

        private sealed class TaskSnapshot
        {
            public bool Exists;
            public bool Enabled;
            public string Xml;
        }

        private static string TaskName
        {
            get { return Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product; }
        }

        private static bool FindPreviousMsiProduct()
        {
            var product = new StringBuilder(39);
            uint code = MsiEnumRelatedProducts(UpgradeCode, 0, 0, product);
            if (code == 0) return true;  // ERROR_SUCCESS
            if (code == 259) return false; // ERROR_NO_MORE_ITEMS
            throw new InstallException("Cannot determine whether an earlier MSI version is installed (" + code + ").");
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                try { Marshal.FinalReleaseComObject(value); }
                catch (InvalidComObjectException) { }
            }
        }

        private static TaskSnapshot ReadTaskSnapshot()
        {
            object serviceObject = null;
            object folderObject = null;
            object taskObject = null;
            try
            {
                Type serviceType = Type.GetTypeFromProgID("Schedule.Service");
                if (serviceType == null)
                    throw new InstallException("Windows Task Scheduler COM service is unavailable.");
                serviceObject = Activator.CreateInstance(serviceType);
                dynamic service = serviceObject;
                service.Connect();
                folderObject = service.GetFolder("\\");
                dynamic folder = folderObject;
                try
                {
                    taskObject = folder.GetTask(TaskName);
                }
                catch (COMException e)
                {
                    // ERROR_FILE_NOT_FOUND and SCHED_E_TASK_NOT_FOUND are the only
                    // cases that may be treated as an absent task. Access denied fails closed.
                    if (e.ErrorCode == unchecked((int)0x80070002) ||
                        e.ErrorCode == unchecked((int)0x8004130F))
                        return new TaskSnapshot { Exists = false, Enabled = false, Xml = null };
                    throw;
                }
                dynamic task = taskObject;
                string xml = (string)task.Xml;
                if (string.IsNullOrWhiteSpace(xml))
                    throw new InstallException("Existing scheduled task has no readable XML definition.");
                return new TaskSnapshot { Exists = true, Enabled = (bool)task.Enabled, Xml = xml };
            }
            finally
            {
                ReleaseComObject(taskObject);
                ReleaseComObject(folderObject);
                ReleaseComObject(serviceObject);
            }
        }

        protected override void OnBeforeInstall(IDictionary savedState)
        {
            base.OnBeforeInstall(savedState);
            if (savedState == null) throw new InstallException("Installer state dictionary is missing.");
            TaskSnapshot prior = ReadTaskSnapshot();
            bool replacing = FindPreviousMsiProduct();
            savedState[S_Existed] = prior.Exists;
            savedState[S_Enabled] = prior.Enabled;
            savedState[S_Xml] = prior.Xml ?? "";
            savedState[S_Upgrade] = replacing;
            savedState[S_Captured] = true;
        }

        private static bool ReadSavedBool(IDictionary state, string key)
        {
            if (state == null || !state.Contains(key) || !(state[key] is bool))
                throw new InstallException("Required installer state is unavailable: " + key);
            return (bool)state[key];
        }

        private static string BuildTaskXml()
        {
            string xml;
            using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(Program), "task.xml"))
            {
                if (res == null) throw new InstallException("Embedded task.xml was not found.");
                using (var reader = new StreamReader(res, true)) xml = reader.ReadToEnd();
            }
            var asm = Assembly.GetExecutingAssembly();
            xml = xml.Replace("{Author}", asm.GetCustomAttribute<AssemblyCompanyAttribute>().Company);
            xml = xml.Replace("{Description}", asm.GetCustomAttribute<AssemblyDescriptionAttribute>().Description);
            xml = xml.Replace("{URI}", "\"\\" + TaskName + "\"");
            xml = xml.Replace("{Command}", "\"" + asm.Location + "\"");
            xml = xml.Replace("{Arguments}", "");
            return xml;
        }

        private static string UpdateExistingTaskXml(string xml)
        {
            XDocument document = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var execs = document.Descendants(ns + "Exec");
            XElement command = null;
            int n = 0;
            foreach (var exec in execs)
            {
                n++;
                command = exec.Element(ns + "Command");
            }
            if (n != 1 || command == null)
                throw new InstallException("The existing startup task has an unexpected number of Exec actions.");
            command.Value = "\"" + Assembly.GetExecutingAssembly().Location + "\"";
            return document.ToString();
        }

        private static void RunTaskCommand(string args)
        {
            int result = DoCmd("schtasks.exe", args, true);
            if (result != 0)
                throw new InstallException("Task Scheduler command failed with exit code " + result + ".");
        }

        private static void RegisterTaskXml(string xml, bool enabled)
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, xml, Encoding.Unicode);
                RunTaskCommand("/Create /TN \"\\" + TaskName + "\" /F /XML \"" + path + "\"");
                RunTaskCommand("/Change /TN \"\\" + TaskName + "\" " + (enabled ? "/ENABLE" : "/DISABLE"));
                if (ReadTaskSnapshot().Enabled != enabled)
                    throw new InstallException("Scheduled task enabled state differs from preserved value.");
            }
            finally
            {
                try { File.Delete(path); } catch (IOException) { }
            }
        }

        protected override void OnCommitted(IDictionary savedState)
        {
            base.OnCommitted(savedState);
            if (!ReadSavedBool(savedState, S_Captured))
                throw new InstallException("Scheduled-task snapshot was not persisted by MSI.");
            bool existed = ReadSavedBool(savedState, S_Existed);
            bool enabled = ReadSavedBool(savedState, S_Enabled);
            bool upgrade = ReadSavedBool(savedState, S_Upgrade);
            if (!existed && upgrade)
            {
                // An upgrade must not silently create a startup task that was absent.
                return;
            }
            string xml = existed ? UpdateExistingTaskXml((string)savedState[S_Xml]) : BuildTaskXml();
            RegisterTaskXml(xml, existed ? enabled : true);
            if (!existed || enabled)
            {
                // Best effort: the scheduled task could be running already.
                DoCmd("schtasks.exe", "/Run /TN \"\\" + TaskName + "\"", true);
            }
        }

        public override void Rollback(IDictionary savedState)
        {
            try
            {
                if (savedState != null && savedState.Contains(S_Captured) &&
                    savedState[S_Captured] is bool && (bool)savedState[S_Captured])
                {
                    bool existed = ReadSavedBool(savedState, S_Existed);
                    if (existed)
                    {
                        // Restore the original XML, not the new executable's XML.
                        RegisterTaskXml((string)savedState[S_Xml], ReadSavedBool(savedState, S_Enabled));
                    }
                    else
                    {
                        TaskSnapshot current = ReadTaskSnapshot();
                        if (current.Exists)
                            RunTaskCommand("/Delete /TN \"\\" + TaskName + "\" /F");
                    }
                }
            }
            finally { base.Rollback(savedState); }
        }
        protected override void OnBeforeUninstall(IDictionary savedState)
        {
            base.OnBeforeUninstall(savedState);

            // Kill running instances
            var procs = Process.GetProcessesByName(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product);
            foreach (var p in procs)
            {
                p.Kill();
                p.WaitForExit();
            }

            // Delete task
            DoCmd("schtasks", string.Format("/Delete /TN {0} /F", "\"" + "\\" + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product + "\""), true);
        }
        public static int DoCmd(string cmd, string args, bool asAdmin = false)
        {
            var proc = new Process();
            proc.StartInfo.FileName = cmd;
            proc.StartInfo.Arguments = args;
            proc.StartInfo.Verb = asAdmin ? "runas" : "";
            proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            proc.Start();
            proc.WaitForExit();
            return proc.ExitCode;
        }
    }
}
