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

using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace Auto_Backlight_for_ThinkPad
{
    /// <summary>
    /// Ability to check online and download installer for new versions through the Github API
    /// </summary>
    public class OnlineUpdater : IDisposable
    {
        /// <summary>
        /// Describe a downloadable installer version discovered on Github
        /// </summary>
        public class Downloadable
        {
            /// <summary>
            /// Get the version of the file
            /// </summary>
            public Version Version { get; }
            /// <summary>
            /// Get the download link to the file
            /// </summary>
            public Uri Url { get; }

            public Downloadable(Version version, Uri url)
            {
                Version = version;
                Url = url;
            }
        }

        /// <summary>
        /// Get info about the latest program version available online via Github API
        /// </summary>
        /// <returns>Tuple with version# and download url</returns>
        // v5.2: no automatic network requests; Releases opened in browser from About.
        public Task<Downloadable> GetLatestVersion()
        {
            return Task.FromResult<Downloadable>(null);
        }
        /// <summary>
        /// Download the installer from Github API, run the installer in separate process
        /// </summary>
        /// <param name="update">Which version to retrieve</param>
        /// <returns>True on success</returns>
        // v5.2 safety: the old installer chain is deliberately disabled.
        public Task<bool> DownloadVersion(Downloadable update)
        {
            return Task.FromResult(false);
        }
        public OnlineUpdater()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
            string name = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product;
            string ver = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(name.Replace(" ", "") + "/" + ver);
        }

        public void Dispose()
        {
            ((IDisposable)_httpClient).Dispose();
        }

        private HttpClient _httpClient = new HttpClient();
    }
}
