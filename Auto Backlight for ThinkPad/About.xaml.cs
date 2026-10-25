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
using System.Windows.Documents;

namespace Auto_Backlight_for_ThinkPad
{
    /// <summary>
    /// Interaction logic for About.xaml
    /// </summary>
    public partial class About : Window
    {
        /// <summary>
        /// Load informational strings and connect the buttons
        /// </summary>
        public About()
        {
            InitializeComponent();

            // Load strings
            Header.Content = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product;
            Subheader.Content = "Version: " + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion + " (" + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>().Configuration + ")";
            Body.Text = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyDescriptionAttribute>().Description;
            Footer.Content = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>().Copyright;

            // Load License.rtf document
            LicenseHeader.Content = "This software is licensed under the Apache License Version 2.0:";
            TextRange content = new TextRange(License.ContentStart, License.ContentEnd);
            string file = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "License.rtf");
            if (content.CanLoad(DataFormats.Rtf))
                content.Load(File.OpenRead(file), DataFormats.Rtf);
            StartAutoBox.IsChecked = 0 == Installer.DoCmd(Environment.GetEnvironmentVariable("ComSpec"), string.Format("/c schtasks /Query /TN {0} | findstr /C:Running /C:Ready", "\"" + "\\" + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product + "\""));

            // Connect actions to buttons
            VisitButton.Click += (sender, e) =>
            {
                // Open url in web browser
                var uri = new Uri(Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().First((i) => i.Key == "WebUrl").Value); ;
                if (uri.AbsoluteUri != "")
                {
                    var proc = new Process();
                    proc.StartInfo.FileName = uri.AbsoluteUri;
                    proc.Start();
                }
            };
            StartAutoBox.Click += (sender, e) =>
            {
                try
                {
                    if (StartAutoBox.IsChecked == true) Installer.DoCmd("schtasks", string.Format("/Change /TN {0} /ENABLE", "\"" + "\\" + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product + "\""), true);
                    else Installer.DoCmd("schtasks", string.Format("/Change /TN {0} /Disable", "\"" + "\\" + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product + "\""), true);
                }
                catch (Exception)
                {
                }
                StartAutoBox.IsChecked = 0 == Installer.DoCmd(Environment.GetEnvironmentVariable("ComSpec"), string.Format("/c schtasks /Query /TN {0} | findstr /C:Running /C:Ready", "\"" + "\\" + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product + "\""));
            };
            CloseButton.Click += (sender, e) => Close();
            UpdateButton.Click += (sender, e) => OpenProjectUrl("https://github.com/prat89/auto-backlight-for-thinkpad/releases");
        }
        // Visit a trusted project page. No background check or installer execution.
        private static void OpenProjectUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open the web browser: " + ex.Message,
                    "Auto Backlight for ThinkPad", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
