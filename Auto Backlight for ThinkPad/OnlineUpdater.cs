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
using System.Linq;
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

        public OnlineUpdater() : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
        }

        // Test seam. Production always uses the no-redirect, no-cookie handler above.
        internal OnlineUpdater(HttpMessageHandler handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            _httpClient = new HttpClient(handler);
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

        // PHASE30B4_VERIFIED_MSI_STAGING_V1
        // Stages a downloaded MSI only; never launches msiexec, exits the app, or elevates.
        // GitHub's digest detects accidental replacement/corruption; it is NOT a publisher signature.
        private const long Phase30B4MaximumMsiBytes = 128L * 1024L * 1024L;
        private const int Phase30B4MaximumRedirects = 5;
        private static readonly byte[] Phase30B4CompoundFileMagic =
            { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

        public enum StageStatus
        {
            Staged,
            InvalidMetadata,
            MissingDigest,
            InvalidRedirect,
            HttpError,
            NetworkError,
            SizeMismatch,
            HashMismatch,
            InvalidMsiHeader,
            IoError
        }

        public sealed class StageResult
        {
            public StageStatus Status { get; private set; }
            public string Message { get; private set; }
            public string Path { get; private set; }
            public string Sha256 { get; private set; }
            public long SizeBytes { get; private set; }
            internal StageResult(StageStatus status, string message, string path = null,
                string sha256 = null, long sizeBytes = 0)
            {
                Status = status;
                Message = message;
                Path = path;
                Sha256 = sha256;
                SizeBytes = sizeBytes;
            }
        }

        private static bool Phase30B4ValidUpdate(Downloadable update)
        {
            if (update == null || update.Version == null || update.Url == null ||
                update.AssetSizeBytes < 8 || update.AssetSizeBytes > Phase30B4MaximumMsiBytes ||
                !IsKnownMsiAssetName(update.AssetName)) return false;
            Uri url = update.Url;
            if (url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort ||
                !String.IsNullOrEmpty(url.UserInfo) || !String.IsNullOrEmpty(url.Query) ||
                !String.IsNullOrEmpty(url.Fragment) ||
                !String.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return false;
            string path = Uri.UnescapeDataString(url.AbsolutePath);
            if (!path.StartsWith(ReleaseDownloadPathPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            string suffix = path.Substring(ReleaseDownloadPathPrefix.Length);
            int separator = suffix.IndexOf('/');
            if (separator <= 0 || separator != suffix.LastIndexOf('/')) return false;
            string tag = suffix.Substring(0, separator);
            string assetName = suffix.Substring(separator + 1);
            Version parsed;
            return TryParseStableMsiVersion(tag, out parsed) &&
                CompareMsiVersions(parsed, update.Version) == 0 &&
                String.Equals(assetName, update.AssetName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Phase30B4SafeRedirect(Uri destination, Uri original)
        {
            if (destination == null || destination.Scheme != Uri.UriSchemeHttps ||
                !destination.IsDefaultPort || !String.IsNullOrEmpty(destination.UserInfo) ||
                !String.IsNullOrEmpty(destination.Fragment)) return false;
            if (String.Equals(destination.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                return destination.Equals(original); // no other GitHub path may replace the release asset
            return String.Equals(destination.Host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(destination.Host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Downloads a verified MSI to the current user's local staging directory.
        /// This method does not install; the existing DownloadVersion remains disabled.
        /// A missing digest fails closed. No proxy credentials/cookies or redirects to arbitrary hosts.
        /// </summary>
        public async Task<StageResult> StageMsiAsync(Downloadable update,
            System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            if (_disposed) throw new ObjectDisposedException("OnlineUpdater");
            if (!Phase30B4ValidUpdate(update))
                return new StageResult(StageStatus.InvalidMetadata, "Invalid release asset metadata.");
            if (String.IsNullOrEmpty(update.Sha256Digest) ||
                !Regex.IsMatch(update.Sha256Digest, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
                return new StageResult(StageStatus.MissingDigest, "GitHub asset has no usable SHA-256 digest.");

            string temporaryDirectory = null;
            bool success = false;
            try
            {
                using (var timeout = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(2));
                    Uri current = update.Url;
                    HttpResponseMessage final = null;
                    for (int redirectCount = 0; redirectCount <= Phase30B4MaximumRedirects; redirectCount++)
                    {
                        HttpResponseMessage response;
                        using (var request = new HttpRequestMessage(HttpMethod.Get, current))
                        {
                            // API discovery uses JSON; an MSI download must request binary data.
                            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                            response = await _httpClient.SendAsync(request,
                                HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                        }
                        int statusCode = (int)response.StatusCode;
                        if (statusCode == 301 || statusCode == 302 || statusCode == 303 ||
                            statusCode == 307 || statusCode == 308)
                        {
                            Uri location = response.Headers.Location;
                            response.Dispose();
                            if (location == null || redirectCount == Phase30B4MaximumRedirects)
                                return new StageResult(StageStatus.InvalidRedirect, "Missing or excessive redirect.");
                            Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);
                            if (!Phase30B4SafeRedirect(next, update.Url))
                                return new StageResult(StageStatus.InvalidRedirect, "Untrusted redirect target.");
                            current = next;
                            continue;
                        }
                        if (response.StatusCode != HttpStatusCode.OK)
                        {
                            response.Dispose();
                            return new StageResult(StageStatus.HttpError, "MSI download returned HTTP " + statusCode + ".");
                        }
                        final = response;
                        break;
                    }
                    if (final == null)
                        return new StageResult(StageStatus.InvalidRedirect, "Redirect limit exceeded.");
                    using (final)
                    {
                        if (final.Content == null ||
                            (final.Content.Headers.ContentLength.HasValue &&
                             final.Content.Headers.ContentLength.Value != update.AssetSizeBytes))
                            return new StageResult(StageStatus.SizeMismatch, "Unexpected MSI Content-Length.");

                        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Auto Backlight for ThinkPad", "UpdateStaging");
                        temporaryDirectory = Path.Combine(root, Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(temporaryDirectory);
                        string partialPath = Path.Combine(temporaryDirectory, "update.partial");
                        long bytesReadTotal = 0;
                        using (Stream source = await final.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var destination = new FileStream(partialPath, FileMode.CreateNew,
                            FileAccess.Write, FileShare.None, 81920, true))
                        {
                            byte[] buffer = new byte[81920];
                            int count;
                            while ((count = await source.ReadAsync(buffer, 0, buffer.Length,
                                timeout.Token).ConfigureAwait(false)) > 0)
                            {
                                bytesReadTotal += count;
                                if (bytesReadTotal > update.AssetSizeBytes ||
                                    bytesReadTotal > Phase30B4MaximumMsiBytes)
                                    return new StageResult(StageStatus.SizeMismatch, "MSI exceeded expected size.");
                                await destination.WriteAsync(buffer, 0, count, timeout.Token).ConfigureAwait(false);
                            }
                            await destination.FlushAsync(timeout.Token).ConfigureAwait(false);
                        }
                        if (bytesReadTotal != update.AssetSizeBytes)
                            return new StageResult(StageStatus.SizeMismatch, "MSI size differs from release metadata.");

                        string actualHash;
                        using (var source = new FileStream(partialPath, FileMode.Open,
                            FileAccess.Read, FileShare.Read))
                        using (var hash = System.Security.Cryptography.SHA256.Create())
                        {
                            byte[] header = new byte[Phase30B4CompoundFileMagic.Length];
                            if (source.Read(header, 0, header.Length) != header.Length ||
                                !header.SequenceEqual(Phase30B4CompoundFileMagic))
                                return new StageResult(StageStatus.InvalidMsiHeader, "Invalid MSI compound-file signature.");
                            source.Position = 0;
                            actualHash = BitConverter.ToString(hash.ComputeHash(source)).Replace("-", "");
                        }
                        if (!String.Equals(actualHash, update.Sha256Digest, StringComparison.OrdinalIgnoreCase))
                            return new StageResult(StageStatus.HashMismatch, "MSI SHA-256 differs from GitHub metadata.");
                        string completePath = Path.Combine(temporaryDirectory,
                            "AutoBacklight-" + update.Version.ToString(3) + ".msi");
                        File.Move(partialPath, completePath);
                        success = true;
                        return new StageResult(StageStatus.Staged, "MSI downloaded and verified; not installed.",
                            completePath, actualHash, bytesReadTotal);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return new StageResult(StageStatus.NetworkError, "MSI download was cancelled or timed out.");
            }
            catch (HttpRequestException)
            {
                return new StageResult(StageStatus.NetworkError, "Could not download the MSI from GitHub.");
            }
            catch (IOException)
            {
                return new StageResult(StageStatus.IoError, "Could not stage the MSI on disk.");
            }
            catch (UnauthorizedAccessException)
            {
                return new StageResult(StageStatus.IoError, "MSI staging directory is not writable.");
            }
            finally
            {
                if (!success && temporaryDirectory != null)
                {
                    try { Directory.Delete(temporaryDirectory, true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }


        // PHASE30B7C_SIGNED_RELEASE_DOWNLOAD_V1
        // Verify independently signed release assets *before* downloading an MSI.
        // This path does not elevate, invoke UpdateHelper, or execute installers.
        private const string Phase30B7CManifestName = "update-manifest-v1.txt";
        private const string Phase30B7CSignatureName = "update-manifest-v1.sig";
        private const int Phase30B7CMaximumManifestBytes = 2048;
        private const int Phase30B7CMaximumSignatureBytes = 512;

        public enum SignedManifestStatus
        {
            Verified,
            InvalidMetadata,
            HttpError,
            InvalidRedirect,
            Oversize,
            SignatureRejected,
            NetworkError
        }

        // PHASE30B7D2_SIGNED_BUNDLE_STAGING_V1
        // Authenticated bytes are carried from verification to per-release staging.
        // This is STILL USER-WRITABLE staging; it is never a privilege boundary.
        public sealed class SignedManifestResult
        {
            public SignedManifestStatus Status { get; private set; }
            public string Message { get; private set; }
            public bool IsVerified { get { return Status == SignedManifestStatus.Verified; } }
            internal byte[] ManifestBytes { get; private set; }
            internal byte[] SignatureBytes { get; private set; }
            internal SignedManifestResult(SignedManifestStatus status, string message,
                byte[] manifest = null, byte[] signature = null)
            {
                Status = status;
                Message = message;
                if (status == SignedManifestStatus.Verified)
                {
                    if (manifest == null || signature == null)
                        throw new InvalidDataException("Verified release is missing signed asset bytes.");
                    ManifestBytes = (byte[])manifest.Clone();
                    SignatureBytes = (byte[])signature.Clone();
                }
            }
        }

        private sealed class Phase30B7CAssetFailure : Exception
        {
            internal SignedManifestStatus Status { get; private set; }
            internal Phase30B7CAssetFailure(SignedManifestStatus status) { Status = status; }
        }

        private static Uri Phase30B7CAssetUrl(Downloadable candidate, string assetName)
        {
            // Phase30B4ValidUpdate enforces the exact repository/tag/MSI URL shape.
            if (!Phase30B4ValidUpdate(candidate) ||
                (assetName != Phase30B7CManifestName && assetName != Phase30B7CSignatureName))
                throw new InvalidDataException("Release metadata does not identify a trusted asset location.");
            string url = candidate.Url.AbsoluteUri;
            int slash = url.LastIndexOf('/');
            if (slash <= 0) throw new InvalidDataException("Unexpected release asset URL.");
            Uri result = new Uri(url.Substring(0, slash + 1) + assetName, UriKind.Absolute);
            if (result.Scheme != Uri.UriSchemeHttps || !result.IsDefaultPort ||
                !String.Equals(result.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                !String.IsNullOrEmpty(result.Query) || !String.IsNullOrEmpty(result.Fragment))
                throw new InvalidDataException("Invalid signed asset origin.");
            return result;
        }

        private async Task<byte[]> Phase30B7CReadBoundedAssetAsync(Uri origin, int limit,
            System.Threading.CancellationToken cancellationToken)
        {
            Uri current = origin;
            for (int i = 0; i <= Phase30B4MaximumRedirects; i++)
            {
                HttpResponseMessage response;
                using (var request = new HttpRequestMessage(HttpMethod.Get, current))
                {
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken).ConfigureAwait(false);
                }
                using (response)
                {
                    int code = (int)response.StatusCode;
                    if (code == 301 || code == 302 || code == 303 || code == 307 || code == 308)
                    {
                        if (response.Headers.Location == null || i == Phase30B4MaximumRedirects)
                            throw new Phase30B7CAssetFailure(SignedManifestStatus.InvalidRedirect);
                        Uri location = response.Headers.Location.IsAbsoluteUri
                            ? response.Headers.Location : new Uri(current, response.Headers.Location);
                        if (!Phase30B4SafeRedirect(location, origin))
                            throw new Phase30B7CAssetFailure(SignedManifestStatus.InvalidRedirect);
                        current = location;
                        continue;
                    }
                    if (response.StatusCode != HttpStatusCode.OK || response.Content == null)
                        throw new Phase30B7CAssetFailure(SignedManifestStatus.HttpError);
                    if (response.Content.Headers.ContentLength.HasValue &&
                        response.Content.Headers.ContentLength.Value > limit)
                        throw new Phase30B7CAssetFailure(SignedManifestStatus.Oversize);
                    using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var destination = new MemoryStream())
                    {
                        byte[] buffer = new byte[1024];
                        int count;
                        while ((count = await stream.ReadAsync(buffer, 0, buffer.Length,
                            cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            if (destination.Length + count > limit)
                                throw new Phase30B7CAssetFailure(SignedManifestStatus.Oversize);
                            destination.Write(buffer, 0, count);
                        }
                        return destination.ToArray();
                    }
                }
            }
            throw new Phase30B7CAssetFailure(SignedManifestStatus.InvalidRedirect);
        }

        /// <summary>Fetches the expected release .txt/.sig and verifies the embedded publisher key.
        /// Does not download or execute an installer. Missing or invalid signature is fail-closed.
        /// </summary>
        public async Task<SignedManifestResult> VerifyRemoteReleaseAsync(Downloadable candidate,
            System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            if (_disposed) throw new ObjectDisposedException("OnlineUpdater");
            if (!Phase30B4ValidUpdate(candidate) || String.IsNullOrEmpty(candidate.Sha256Digest) ||
                !Regex.IsMatch(candidate.Sha256Digest, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
                return new SignedManifestResult(SignedManifestStatus.InvalidMetadata,
                    "Release metadata is missing or invalid.");
            try
            {
                using (var timeout = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    byte[] manifest = await Phase30B7CReadBoundedAssetAsync(
                        Phase30B7CAssetUrl(candidate, Phase30B7CManifestName),
                        Phase30B7CMaximumManifestBytes, timeout.Token).ConfigureAwait(false);
                    byte[] signature = await Phase30B7CReadBoundedAssetAsync(
                        Phase30B7CAssetUrl(candidate, Phase30B7CSignatureName),
                        Phase30B7CMaximumSignatureBytes, timeout.Token).ConfigureAwait(false);
                    string status;
                    if (!VerifySignedManifestForDiscoveredRelease(candidate, manifest, signature, out status))
                        return new SignedManifestResult(SignedManifestStatus.SignatureRejected,
                            "Publisher signature or signed release metadata was rejected.");
                    return new SignedManifestResult(SignedManifestStatus.Verified,
                        "Publisher-signed release manifest verified.", manifest, signature);
                }
            }
            catch (Phase30B7CAssetFailure ex)
            {
                return new SignedManifestResult(ex.Status,
                    "Signed release asset unavailable or invalid (" + ex.Status + ").");
            }
            catch (InvalidDataException)
            {
                return new SignedManifestResult(SignedManifestStatus.InvalidMetadata,
                    "Signed release asset URL is invalid.");
            }
            catch (OperationCanceledException)
            {
                return new SignedManifestResult(SignedManifestStatus.NetworkError,
                    "Signed release request timed out or was cancelled.");
            }
            catch (HttpRequestException)
            {
                return new SignedManifestResult(SignedManifestStatus.NetworkError,
                    "Could not fetch signed release assets.");
            }
            catch (IOException)
            {
                return new SignedManifestResult(SignedManifestStatus.NetworkError,
                    "Could not read signed release assets.");
            }
        }

        // PHASE30B7D2_SIGNED_BUNDLE_STAGE_METHOD_V1
        // Manifest/signature and MSI are preserved in the same GUID folder for independent helper
        // assessment. This folder is controlled by the signed-in user, NOT an elevated installer.
        private static bool Phase30B7D2ExpectedStageDirectory(string msiPath)
        {
            if (String.IsNullOrEmpty(msiPath)) return false;
            string root = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Auto Backlight for ThinkPad", "UpdateStaging"));
            string folder = Path.GetDirectoryName(Path.GetFullPath(msiPath));
            if (!String.Equals(Path.GetDirectoryName(folder), root,
                    StringComparison.OrdinalIgnoreCase) ||
                !Regex.IsMatch(Path.GetFileName(folder), "^[0-9a-f]{32}$", RegexOptions.CultureInvariant))
                return false;
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0 ||
                (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                return false;
            return true;
        }

        private static void Phase30B7D2WriteNewSmallFile(string path, byte[] data, int limit)
        {
            if (data == null || data.Length == 0 || data.Length > limit)
                throw new InvalidDataException("Signed release asset exceeds the expected length.");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data, 0, data.Length);
                stream.Flush(true);
            }
        }

        /// <summary>Fetches the verified manifest/signature and MSI into ONE user-writable folder.
        /// This is not protected staging, never elevates, never runs an MSI, and must never be
        /// treated as approval to install. Future privileged helper must independently authenticate
        /// and securely copy/rehash each asset before executing any installer.</summary>
        public async Task<StageResult> StageSignedMsiAsync(Downloadable candidate,
            System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            SignedManifestResult trust = await VerifyRemoteReleaseAsync(candidate,
                cancellationToken).ConfigureAwait(false);
            if (!trust.IsVerified)
                return new StageResult(StageStatus.InvalidMetadata,
                    "Publisher-signed release verification failed; MSI download refused.");
            StageResult msi = await StageMsiAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (msi.Status != StageStatus.Staged) return msi;
            string stagedFolder = null;
            bool complete = false;
            try
            {
                if (!Phase30B7D2ExpectedStageDirectory(msi.Path))
                    throw new InvalidDataException("Unexpected unsigned staging location.");
                stagedFolder = Path.GetDirectoryName(Path.GetFullPath(msi.Path));
                string manifestPath = Path.Combine(stagedFolder, Phase30B7CManifestName);
                string signaturePath = Path.Combine(stagedFolder, Phase30B7CSignatureName);
                Phase30B7D2WriteNewSmallFile(manifestPath, trust.ManifestBytes,
                    Phase30B7CMaximumManifestBytes);
                Phase30B7D2WriteNewSmallFile(signaturePath, trust.SignatureBytes,
                    Phase30B7CMaximumSignatureBytes);
                byte[] savedManifest = File.ReadAllBytes(manifestPath);
                byte[] savedSignature = File.ReadAllBytes(signaturePath);
                string outcome;
                if (!VerifySignedManifestForDiscoveredRelease(candidate, savedManifest,
                    savedSignature, out outcome))
                    throw new InvalidDataException("Staged release signature was not verified.");
                using (var file = new FileStream(msi.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    if (file.Length != msi.SizeBytes || file.Length != candidate.AssetSizeBytes ||
                        !String.Equals(BitConverter.ToString(sha.ComputeHash(file)).Replace("-", ""),
                            candidate.Sha256Digest, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Staged MSI does not match signed manifest.");
                }
                complete = true;
                return new StageResult(StageStatus.Staged,
                    "Signed release bundle staged for independent assessment only; not installable yet.",
                    msi.Path, msi.Sha256, msi.SizeBytes);
            }
            catch (IOException)
            {
                return new StageResult(StageStatus.IoError, "Unable to persist signed release bundle.");
            }
            catch (UnauthorizedAccessException)
            {
                return new StageResult(StageStatus.IoError, "Signed release staging access denied.");
            }
            catch (InvalidDataException)
            {
                return new StageResult(StageStatus.InvalidMetadata, "Signed release bundle integrity failure.");
            }
            finally
            {
                // Delete ONLY our newly created GUID folder, never a caller-provided arbitrary path.
                if (!complete && stagedFolder != null)
                {
                    try
                    {
                        if (Phase30B7D2ExpectedStageDirectory(msi.Path))
                            Directory.Delete(stagedFolder, true);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        /// <summary>Phase 30B.1 explicitly cannot download or execute an installer.</summary>
        public Task<bool> DownloadVersion(Downloadable update)
        {
            return Task.FromResult(false);
        }

        // PHASE30B7B_SIGNED_MANIFEST_BRIDGE_V1 - verification only; no MSI execution.
        // The next phase must fetch both assets from the same release and call this BEFORE trusting the MSI.
        public static bool VerifySignedManifestForDiscoveredRelease(Downloadable candidate,
            byte[] manifestBytes, byte[] signatureBase64Bytes, out string status)
        {
            try
            {
                if (candidate == null) throw new InvalidDataException("Missing candidate release.");
                Phase30B7BVerifiedManifest signed = Phase30B7BReleaseManifestTrust.Verify(
                    manifestBytes, signatureBase64Bytes);
                Phase30B7BReleaseManifestTrust.AssertMatchesMetadata(signed,
                    candidate.Version, candidate.AssetName, candidate.Sha256Digest, candidate.AssetSizeBytes);
                status = "SIGNED_MANIFEST_MATCH=TRUE";
                return true;
            }
            catch (Exception ex)
            {
                // Fail closed and keep status independent of attacker-controlled input strings.
                status = "SIGNED_MANIFEST_MATCH=FALSE; ERROR=" + ex.GetType().Name;
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
        }
    }
}
