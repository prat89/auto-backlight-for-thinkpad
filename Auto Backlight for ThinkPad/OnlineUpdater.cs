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

// PHASE30B1_RELEASE_CHECKER: read-only GitHub Releases discovery.
// Phase30B2 adds a throttled metadata-only startup check and separate HKCU preferences.
// No MSI download, installer launch or elevation is permitted.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Win32;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Auto_Backlight_for_ThinkPad
{
    /// <summary>Independent update UI preferences. Never touches legacy serialized Settings.</summary>
    public static class Phase30B2UpdatePreferences
    {
        private const string RegistryPath = @"Software\Auto Backlight for ThinkPad";
        private const string AutoInstallValue = "AutoInstallUpdates";
        private const string LastCheckValue = "LastUpdateCheckUtcTicks";
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

        public static bool ReadAutoInstallUpdates()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, false))
                {
                    object value = key == null ? null : key.GetValue(AutoInstallValue, null);
                    return value is int && (int)value == 1;
                }
            }
            catch (Exception)
            {
                return false; // fail closed: never enable automatic installation implicitly
            }
        }

        public static void SaveAutoInstallUpdates(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                if (key == null) throw new InvalidOperationException("Update settings registry key unavailable");
                key.SetValue(AutoInstallValue, enabled ? 1 : 0, RegistryValueKind.DWord);
                key.Flush();
            }
        }

        /// <summary>Reserve one background check per 24 hours, across application restarts.</summary>
        public static bool TryReserveStartupCheck()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key == null) return false;
                    long now = DateTime.UtcNow.Ticks;
                    object previous = key.GetValue(LastCheckValue, null);
                    if (previous is long)
                    {
                        long ticks = (long)previous;
                        if (ticks > 0 && ticks <= now && now - ticks < CheckInterval.Ticks)
                            return false;
                    }
                    key.SetValue(LastCheckValue, now, RegistryValueKind.QWord);
                    key.Flush();
                    return true;
                }
            }
            catch (Exception)
            {
                return false; // no repeated requests when registry is inaccessible
            }
        }
    }

    /// <summary>Read-only discovery of a stable, newer MSI release from the project's GitHub repository.</summary>
    public sealed class OnlineUpdater : IDisposable
    {
        public const string LatestReleaseApiUrl =
            "https://api.github.com/repos/prat89/auto-backlight-for-thinkpad/releases/latest";
        public const string ExpectedMsiAssetName = "Auto.Backlight.for.ThinkPad.Setup.msi";
        public const string LegacyMsiAssetName = "Auto Backlight for ThinkPad Setup.msi";
        private const string ReleaseDownloadPathPrefix =
            "/prat89/auto-backlight-for-thinkpad/releases/download/";
        private const int MaximumReleaseJsonBytes = 1024 * 1024;

        public enum CheckStatus
        {
            UpdateAvailable,
            UpToDate,
            NoInstallableMsi,
            InvalidRelease,
            RateLimited,
            HttpError,
            NetworkError
        }

        /// <summary>Metadata only. Its Url is NEVER opened or downloaded by Phase 30B.1.</summary>
        public sealed class Downloadable
        {
            public Version Version { get; private set; }
            public Uri Url { get; private set; }
            public string AssetName { get; private set; }
            public long AssetSizeBytes { get; private set; }
            public string Sha256Digest { get; private set; }
            public Uri ReleaseNotesUrl { get; private set; }

            public Downloadable(Version version, Uri url)
                : this(version, url, null, 0, null, null) { }

            internal Downloadable(Version version, Uri url, string assetName,
                long assetSizeBytes, string sha256Digest, Uri releaseNotesUrl)
            {
                Version = version;
                Url = url;
                AssetName = assetName;
                AssetSizeBytes = assetSizeBytes;
                Sha256Digest = sha256Digest;
                ReleaseNotesUrl = releaseNotesUrl;
            }
        }

        public sealed class UpdateCheckResult
        {
            public CheckStatus Status { get; private set; }
            public string Message { get; private set; }
            public Version LatestVersion { get; private set; }
            public Downloadable Update { get; private set; }

            internal UpdateCheckResult(CheckStatus status, string message,
                Version latestVersion = null, Downloadable update = null)
            {
                Status = status;
                Message = message;
                LatestVersion = latestVersion;
                Update = update;
            }
        }

        [DataContract]
        private sealed class GitHubRelease
        {
            [DataMember(Name = "tag_name")] public string TagName { get; set; }
            [DataMember(Name = "draft")] public bool Draft { get; set; }
            [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
            [DataMember(Name = "html_url")] public string HtmlUrl { get; set; }
            [DataMember(Name = "assets")] public List<GitHubAsset> Assets { get; set; }
        }

        [DataContract]
        private sealed class GitHubAsset
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "state")] public string State { get; set; }
            [DataMember(Name = "browser_download_url")] public string BrowserDownloadUrl { get; set; }
            [DataMember(Name = "size")] public long Size { get; set; }
            [DataMember(Name = "digest")] public string Digest { get; set; }
        }

        private readonly HttpClient _httpClient;
        private bool _disposed;

        public OnlineUpdater()
        {
            _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            _httpClient.Timeout = TimeSpan.FromSeconds(12);
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AutoBacklightForThinkPad/" + version);
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        /// <summary>Queries only release metadata; Phase 30B.2 owns the notification/UI flow.</summary>
        public Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            return CheckForUpdatesAsync(Assembly.GetExecutingAssembly().GetName().Version);
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(Version installedVersion)
        {
            if (_disposed) throw new ObjectDisposedException("OnlineUpdater");
            if (installedVersion == null) throw new ArgumentNullException("installedVersion");

            try
            {
                using (var response = await _httpClient.GetAsync(
                    LatestReleaseApiUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (response.StatusCode == (HttpStatusCode)429 ||
                        (response.StatusCode == HttpStatusCode.Forbidden && IsRateLimited(response)))
                    {
                        return new UpdateCheckResult(CheckStatus.RateLimited,
                            "GitHub API rate limit reached; try again later.");
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        return new UpdateCheckResult(CheckStatus.HttpError,
                            "GitHub API returned HTTP " + (int)response.StatusCode + ".");
                    }
                    string json = await ReadBoundedJsonAsync(response.Content).ConfigureAwait(false);
                    return ParseLatestReleaseJson(json, installedVersion);
                }
            }
            catch (TaskCanceledException)
            {
                return new UpdateCheckResult(CheckStatus.NetworkError,
                    "GitHub request timed out or was cancelled.");
            }
            catch (HttpRequestException)
            {
                return new UpdateCheckResult(CheckStatus.NetworkError,
                    "Could not contact GitHub API.");
            }
            catch (InvalidDataException)
            {
                return new UpdateCheckResult(CheckStatus.InvalidRelease,
                    "GitHub API response exceeded the size limit.");
            }
            catch (DecoderFallbackException)
            {
                return new UpdateCheckResult(CheckStatus.InvalidRelease,
                    "GitHub API response is not valid UTF-8.");
            }
            catch (IOException)
            {
                return new UpdateCheckResult(CheckStatus.NetworkError,
                    "Could not read the GitHub API response.");
            }
        }

        private static bool IsRateLimited(HttpResponseMessage response)
        {
            IEnumerable<string> remaining;
            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out remaining))
            {
                foreach (string value in remaining)
                    if (value == "0") return true;
            }
            return response.Headers.RetryAfter != null;
        }

        private static async Task<string> ReadBoundedJsonAsync(HttpContent content)
        {
            if (content.Headers.ContentLength.HasValue &&
                content.Headers.ContentLength.Value > MaximumReleaseJsonBytes)
                throw new InvalidDataException("Oversized JSON response");
            using (Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var buffer = new MemoryStream())
            {
                byte[] chunk = new byte[8192];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + bytesRead > MaximumReleaseJsonBytes)
                        throw new InvalidDataException("Oversized JSON response");
                    buffer.Write(chunk, 0, bytesRead);
                }
                return new UTF8Encoding(false, true).GetString(buffer.ToArray());
            }
        }

        /// <summary>
        /// Pure, offline-testable parsing. Requires a stable X.Y.Z MSI release with a single
        /// expected asset and a download URL in this repository. Does not access the network.
        /// </summary>
        public static UpdateCheckResult ParseLatestReleaseJson(string json, Version installedVersion)
        {
            if (installedVersion == null) throw new ArgumentNullException("installedVersion");
            if (String.IsNullOrWhiteSpace(json) ||
                Encoding.UTF8.GetByteCount(json) > MaximumReleaseJsonBytes)
                return new UpdateCheckResult(CheckStatus.InvalidRelease, "Missing or oversized release JSON.");

            GitHubRelease release;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(GitHubRelease));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    release = (GitHubRelease)serializer.ReadObject(stream);
            }
            catch (Exception ex)
            {
                if (!(ex is SerializationException) && !(ex is ArgumentException) &&
                    !(ex is InvalidCastException) && !(ex is FormatException) &&
                    !(ex is System.Xml.XmlException)) throw;
                return new UpdateCheckResult(CheckStatus.InvalidRelease, "Malformed GitHub release JSON.");
            }

            Version releasedVersion;
            if (release == null || release.Draft || release.Prerelease ||
                !TryParseStableMsiVersion(release.TagName, out releasedVersion))
                return new UpdateCheckResult(CheckStatus.InvalidRelease,
                    "Release is draft, prerelease or has an unsupported version tag.");

            if (CompareMsiVersions(releasedVersion, installedVersion) <= 0)
                return new UpdateCheckResult(CheckStatus.UpToDate, "Already at this release or newer.", releasedVersion);

            GitHubAsset candidate = null;
            int matchingNames = 0;
            if (release.Assets != null)
            {
                foreach (GitHubAsset asset in release.Assets)
                {
                    if (asset != null && IsKnownMsiAssetName(asset.Name))
                    {
                        matchingNames++;
                        candidate = asset;
                    }
                }
            }
            if (matchingNames != 1 || candidate.Size <= 0 ||
                !String.Equals(candidate.State, "uploaded", StringComparison.OrdinalIgnoreCase))
                return new UpdateCheckResult(CheckStatus.NoInstallableMsi,
                    "Newer release has no unique, uploaded, nonempty expected MSI asset.", releasedVersion);

            Uri assetUrl;
            if (!IsExpectedAssetUrl(candidate.BrowserDownloadUrl, release.TagName, candidate.Name, out assetUrl))
                return new UpdateCheckResult(CheckStatus.NoInstallableMsi,
                    "MSI asset download URL is not the expected repository release URL.", releasedVersion);

            Uri notesUrl;
            if (!Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out notesUrl) ||
                notesUrl.Scheme != Uri.UriSchemeHttps ||
                !String.Equals(notesUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                notesUrl = null;

            string sha256 = TryParseSha256(candidate.Digest);
            var update = new Downloadable(releasedVersion, assetUrl, candidate.Name,
                candidate.Size, sha256, notesUrl);
            return new UpdateCheckResult(CheckStatus.UpdateAvailable,
                "A newer stable MSI release is available (metadata only).", releasedVersion, update);
        }

        private static bool TryParseStableMsiVersion(string tag, out Version version)
        {
            version = null;
            if (String.IsNullOrWhiteSpace(tag)) return false;
            var match = Regex.Match(tag, @"^[vV]?(\d+)\.(\d+)\.(\d+)(?:\.(0))?$",
                RegexOptions.CultureInvariant);
            if (!match.Success) return false;
            int major, minor, build;
            if (!Int32.TryParse(match.Groups[1].Value, out major) ||
                !Int32.TryParse(match.Groups[2].Value, out minor) ||
                !Int32.TryParse(match.Groups[3].Value, out build)) return false;
            version = new Version(major, minor, build, 0);
            return true;
        }

        private static int CompareMsiVersions(Version left, Version right)
        {
            if (left.Major != right.Major) return left.Major.CompareTo(right.Major);
            if (left.Minor != right.Minor) return left.Minor.CompareTo(right.Minor);
            return left.Build.CompareTo(right.Build);
        }

        private static bool IsKnownMsiAssetName(string name)
        {
            return String.Equals(name, ExpectedMsiAssetName, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(name, LegacyMsiAssetName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExpectedAssetUrl(string rawUrl, string tag, string assetName, out Uri assetUrl)
        {
            assetUrl = null;
            Uri uri;
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !String.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                !uri.IsDefaultPort || !String.IsNullOrEmpty(uri.UserInfo) ||
                !String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment)) return false;

            if (!IsKnownMsiAssetName(assetName)) return false;
            string expectedPath = ReleaseDownloadPathPrefix + tag + "/" + assetName;
            string decodedPath = Uri.UnescapeDataString(uri.AbsolutePath);
            if (!String.Equals(decodedPath, expectedPath, StringComparison.OrdinalIgnoreCase)) return false;
            assetUrl = uri;
            return true;
        }

        private static string TryParseSha256(string digest)
        {
            if (String.IsNullOrEmpty(digest)) return null;
            if (!Regex.IsMatch(digest, @"^sha256:[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)) return null;
            return digest.Substring(7).ToUpperInvariant();
        }

        /// <summary>Legacy API: metadata-only. Returns null when no newer installable MSI exists.</summary>
        public async Task<Downloadable> GetLatestVersion()
        {
            UpdateCheckResult result = await CheckForUpdatesAsync().ConfigureAwait(false);
            return result.Status == CheckStatus.UpdateAvailable ? result.Update : null;
        }

        /// <summary>Phase 30B.1 explicitly cannot download or execute an installer.</summary>
        public Task<bool> DownloadVersion(Downloadable update)
        {
            return Task.FromResult(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
        }
    }
}
