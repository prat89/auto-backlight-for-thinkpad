// Phase30B5D guarded helper: READ-ONLY / EXECUTION BLOCKED; NO MSI LAUNCH.
// Copyright 2026 contributors to Auto Backlight for ThinkPad.
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoBacklightUpdateHelper
{
    internal static class Program
    {
        private const string ProductName = "Auto Backlight for ThinkPad";
        private const string UpgradeCode = "{A78F780D-063F-4852-AC1B-D20FB7165BA2}";
        private const long MaximumSize = 128L * 1024L * 1024L;
        private static readonly byte[] CompoundFileHeader = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

        [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiOpenDatabaseW")]
        private static extern uint MsiOpenDatabase(string path, IntPtr persistence, out IntPtr database);
        [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiDatabaseOpenViewW")]
        private static extern uint MsiDatabaseOpenView(IntPtr database, string query, out IntPtr view);
        [DllImport("msi.dll", EntryPoint = "MsiViewExecute")]
        private static extern uint MsiViewExecute(IntPtr view, IntPtr record);
        [DllImport("msi.dll", EntryPoint = "MsiViewFetch")]
        private static extern uint MsiViewFetch(IntPtr view, out IntPtr record);
        [DllImport("msi.dll", EntryPoint = "MsiViewClose")]
        private static extern uint MsiViewClose(IntPtr view);
        [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiRecordGetStringW")]
        private static extern uint MsiRecordGetString(IntPtr record, uint field, StringBuilder result, ref uint length);
        [DllImport("msi.dll", EntryPoint = "MsiCloseHandle")]
        private static extern uint MsiCloseHandle(IntPtr handle);

        private static string ReadMsiProperty(IntPtr database, string property)
        {
            if (!Regex.IsMatch(property, "^[A-Za-z]+$"))
                throw new InvalidOperationException("Unsafe Windows Installer property name.");
            string query = "SELECT `Value` FROM `Property` WHERE `Property`='" + property + "'";
            IntPtr view = IntPtr.Zero;
            IntPtr record = IntPtr.Zero;
            try
            {
                uint status = MsiDatabaseOpenView(database, query, out view);
                if (status != 0) throw new InvalidOperationException("MSI view open failed: " + status);
                status = MsiViewExecute(view, IntPtr.Zero);
                if (status != 0) throw new InvalidOperationException("MSI view execute failed: " + status);
                status = MsiViewFetch(view, out record);
                if (status == 259) return null;
                if (status != 0) throw new InvalidOperationException("MSI record fetch failed: " + status);
                uint length = 0;
                StringBuilder small = new StringBuilder(1);
                status = MsiRecordGetString(record, 1, small, ref length);
                if (status != 0 && status != 234)
                    throw new InvalidOperationException("MSI string sizing failed: " + status);
                if (length > 4096) throw new InvalidOperationException("Oversized MSI property.");
                var output = new StringBuilder((int)length + 2);
                uint capacity = (uint)output.Capacity;
                status = MsiRecordGetString(record, 1, output, ref capacity);
                if (status != 0) throw new InvalidOperationException("MSI string fetch failed: " + status);
                return output.ToString();
            }
            finally
            {
                if (record != IntPtr.Zero) MsiCloseHandle(record);
                if (view != IntPtr.Zero)
                {
                    MsiViewClose(view);
                    MsiCloseHandle(view);
                }
            }
        }

        private static Version ParseMsiVersion(string value)
        {
            Version version;
            if (String.IsNullOrEmpty(value) || !Regex.IsMatch(value, @"^\d+\.\d+\.\d+$") ||
                !Version.TryParse(value, out version) || version.Major < 0 ||
                version.Minor < 0 || version.Build < 0)
                throw new ArgumentException("Expected MSI version with exactly three numeric components.");
            return version;
        }

        private static string StagingRoot()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Auto Backlight for ThinkPad", "UpdateStaging");
        }

        private static void ValidateStagingPath(string candidate, Version expected)
        {
            string full = Path.GetFullPath(candidate);
            string root = Path.GetFullPath(StagingRoot()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("MSI must reside inside the current user's UpdateStaging directory.");
            string remainder = full.Substring(root.Length);
            string[] parts = remainder.Split(Path.DirectorySeparatorChar);
            if (parts.Length != 2 || !Regex.IsMatch(parts[0], "^[a-fA-F0-9]{32}$"))
                throw new InvalidOperationException("Unexpected UpdateStaging subdirectory structure.");
            string expectedFileName = "AutoBacklight-" + expected.ToString(3) + ".msi";
            if (!String.Equals(parts[1], expectedFileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Staged MSI filename does not match expected release version.");
            string[] checkDirs = { StagingRoot(), Path.Combine(StagingRoot(), parts[0]) };
            foreach (string directory in checkDirs)
            {
                if (!Directory.Exists(directory)) throw new InvalidOperationException("Staging directory not found.");
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Staging directory must not be a reparse point.");
            }
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("MSI must not be a reparse point.");
        }

        private static string CalculateHash(string file, out long length)
        {
            using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var algorithm = SHA256.Create())
            {
                length = input.Length;
                if (length < 8 || length > MaximumSize)
                    throw new InvalidOperationException("MSI size exceeds safety limits.");
                byte[] header = new byte[CompoundFileHeader.Length];
                if (input.Read(header, 0, header.Length) != header.Length)
                    throw new InvalidOperationException("Invalid MSI header.");
                for (int index = 0; index < header.Length; index++)
                    if (header[index] != CompoundFileHeader[index])
                        throw new InvalidOperationException("MSI compound-file signature mismatch.");
                input.Position = 0;
                return BitConverter.ToString(algorithm.ComputeHash(input)).Replace("-", "");
            }
        }

        private static string ReadMsi(string file, string name)
        {
            IntPtr database = IntPtr.Zero;
            try
            {
                uint status = MsiOpenDatabase(file, IntPtr.Zero, out database);
                if (status != 0) throw new InvalidOperationException("Could not open MSI database (" + status + ").");
                return ReadMsiProperty(database, name);
            }
            finally
            {
                if (database != IntPtr.Zero) MsiCloseHandle(database);
            }
        }

        private static string RequireArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (String.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            throw new ArgumentException("Missing argument: " + name);
        }

        private static void SelfTest()
        {
            if (!(ParseMsiVersion("1.3.8").CompareTo(ParseMsiVersion("1.3.7")) > 0))
                throw new InvalidOperationException("Version compare test failed.");
            if (!(ParseMsiVersion("1.3.8").CompareTo(ParseMsiVersion("1.3.8")) == 0))
                throw new InvalidOperationException("Version equality test failed.");
            bool rejected = false;
            try { ParseMsiVersion("1.3.7.1"); }
            catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("Fourth version component was accepted.");
            Console.WriteLine("PHASE30B5A_SELFTEST=PASS");
            Console.WriteLine("INSTALL_CAPABILITY=DISABLED");
        }

        private static int Preflight(string[] args)
        {
            string file = RequireArg(args, "--msi");
            string expectedHash = RequireArg(args, "--sha256");
            Version current = ParseMsiVersion(RequireArg(args, "--current"));
            Version expected = ParseMsiVersion(RequireArg(args, "--expected"));
            if (!Regex.IsMatch(expectedHash, "^[0-9a-fA-F]{64}$"))
                throw new ArgumentException("Expected SHA-256 must contain exactly 64 hex digits.");
            ValidateStagingPath(file, expected);
            long size;
            string actualHash = CalculateHash(file, out size);
            if (!String.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SHA-256 does not match the verified download metadata.");
            string actualProduct = ReadMsi(file, "ProductName");
            string actualUpgrade = ReadMsi(file, "UpgradeCode");
            string actualVersion = ReadMsi(file, "ProductVersion");
            string actualCode = ReadMsi(file, "ProductCode");
            if (!String.Equals(actualProduct, ProductName, StringComparison.Ordinal))
                throw new InvalidOperationException("MSI ProductName mismatch.");
            if (!String.Equals(actualUpgrade, UpgradeCode, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("MSI UpgradeCode mismatch.");
            if (String.IsNullOrWhiteSpace(actualCode) || !GuidRegex(actualCode))
                throw new InvalidOperationException("MSI ProductCode invalid.");
            Version packaged = ParseMsiVersion(actualVersion);
            if (packaged.CompareTo(expected) != 0)
                throw new InvalidOperationException("MSI ProductVersion differs from GitHub release version.");
            Console.WriteLine("MSI_PRODUCT_NAME=" + actualProduct);
            Console.WriteLine("MSI_PRODUCT_VERSION=" + packaged.ToString(3));
            Console.WriteLine("MSI_PRODUCT_CODE=" + actualCode);
            Console.WriteLine("MSI_UPGRADE_CODE=" + actualUpgrade);
            Console.WriteLine("MSI_SIZE=" + size);
            Console.WriteLine("MSI_SHA256=" + actualHash);
            Console.WriteLine("AUTHENTICODE=NOT_VALIDATED");
            Console.WriteLine("INSTALL_CAPABILITY=DISABLED");
            if (packaged.CompareTo(current) <= 0)
            {
                Console.WriteLine("PHASE30B5A_PREFLIGHT=NOT_NEWER");
                return 3;
            }
            Console.WriteLine("PHASE30B5A_PREFLIGHT=PASS");
            Console.WriteLine("INSTALL_ELIGIBILITY=NOT_ESTABLISHED");
            return 0;
        }

        private static bool GuidRegex(string candidate)
        {
            return Regex.IsMatch(candidate, @"^\{[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\}$");
        }

        // Phase 30B.5C: read-only actual installed-product inventory.
        // Do not infer an installed version from caller-provided --current.
        [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiEnumRelatedProductsW")]
        private static extern uint MsiEnumRelatedProducts(string upgradeCode, uint reserved, uint index, StringBuilder productCode);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiGetProductInfoW")]
        private static extern uint MsiGetProductInfo(string productCode, string property, StringBuilder value, ref uint length);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiQueryProductStateW")]
        private static extern int MsiQueryProductState(string productCode);

        private sealed class InstalledProduct
        {
            internal string Code;
            internal Version Version;
        }

        private static string InstalledProperty(string code, string property)
        {
            if (property != "ProductName" && property != "VersionString")
                throw new InvalidOperationException("Unexpected MSI installed-property query.");
            uint capacity = 512;
            var value = new StringBuilder((int)capacity + 1);
            uint status = MsiGetProductInfo(code, property, value, ref capacity);
            if (status == 234) // ERROR_MORE_DATA; returned size excludes the terminator.
            {
                if (capacity > 4096) throw new InvalidOperationException("Oversized registered-product property.");
                value = new StringBuilder((int)capacity + 2);
                capacity = (uint)value.Capacity;
                status = MsiGetProductInfo(code, property, value, ref capacity);
            }
            if (status != 0)
                throw new InvalidOperationException("Installed-product property unavailable: " + property + " (" + status + ").");
            if (value.Length == 0) throw new InvalidOperationException("Empty installed-product property: " + property);
            return value.ToString();
        }

        private static InstalledProduct ReadActualInstallation()
        {
            InstalledProduct single = null;
            for (uint index = 0; index < 32; index++)
            {
                // Braced GUID is 38 chars plus terminating NUL.
                var codeBuffer = new StringBuilder(39);
                uint status = MsiEnumRelatedProducts(UpgradeCode, 0, index, codeBuffer);
                if (status == 259) return single; // ERROR_NO_MORE_ITEMS
                if (status != 0) throw new InvalidOperationException("Related-product enumeration failed: " + status);
                string code = codeBuffer.ToString();
                if (!GuidRegex(code)) throw new InvalidOperationException("Registered ProductCode is invalid.");
                // MsiEnumRelatedProducts also includes advertised products. Reject them.
                if (MsiQueryProductState(code) != 5) // INSTALLSTATE_DEFAULT: installed locally
                    throw new InvalidOperationException("Related product is not installed; refusing to guess current version.");
                if (!String.Equals(InstalledProperty(code, "ProductName"), ProductName, StringComparison.Ordinal))
                    throw new InvalidOperationException("Related product name mismatch.");
                Version version = ParseMsiVersion(InstalledProperty(code, "VersionString"));
                if (single != null)
                    throw new InvalidOperationException("Multiple related products detected; manual intervention required.");
                single = new InstalledProduct { Code = code, Version = version };
            }
            throw new InvalidOperationException("Too many related-product entries.");
        }

        private static int Inventory()
        {
            InstalledProduct product = ReadActualInstallation();
            if (product == null)
            {
                Console.WriteLine("PHASE30B5C_INVENTORY=NOT_INSTALLED");
                Console.WriteLine("MSI_EXECUTED=FALSE");
                return 4;
            }
            Console.WriteLine("ACTUAL_INSTALLED_PRODUCT_CODE=" + product.Code);
            Console.WriteLine("ACTUAL_INSTALLED_VERSION=" + product.Version.ToString(3));
            Console.WriteLine("ACTUAL_INSTALL_STATE=INSTALLED");
            Console.WriteLine("MSI_EXECUTED=FALSE");
            Console.WriteLine("PHASE30B5C_INVENTORY=PASS");
            return 0;
        }

        private static int AssessActualUpgrade(string[] args)
        {
            // --assess --msi FILE --sha256 HASH --expected VERSION: no --current!
            InstalledProduct product = ReadActualInstallation();
            if (product == null)
            {
                Console.WriteLine("PHASE30B5C_ASSESS=NOT_INSTALLED");
                Console.WriteLine("MSI_EXECUTED=FALSE");
                return 4;
            }
            string expectedVersion = RequireArg(args, "--expected");
            ParseMsiVersion(expectedVersion);
            string[] preflightArgs = {
                "--preflight", "--msi", RequireArg(args, "--msi"),
                "--sha256", RequireArg(args, "--sha256"),
                "--current", product.Version.ToString(3), "--expected", expectedVersion
            };
            Console.WriteLine("ACTUAL_INSTALLED_PRODUCT_CODE=" + product.Code);
            Console.WriteLine("ACTUAL_INSTALLED_VERSION=" + product.Version.ToString(3));
            int result = Preflight(preflightArgs);
            Console.WriteLine("PUBLISHER_AUTHENTICITY=NOT_ESTABLISHED");
            Console.WriteLine("MSI_EXECUTED=FALSE");
            if (result == 3)
            {
                Console.WriteLine("PHASE30B5C_ASSESS=NOT_NEWER");
                return 3;
            }
            if (result != 0) return result;
            string proposedCode = ReadMsi(RequireArg(args, "--msi"), "ProductCode");
            if (String.Equals(proposedCode, product.Code, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Major upgrade requires a changed ProductCode.");
            Console.WriteLine("PHASE30B5C_ASSESS=NEWER_PACKAGE_VALIDATED_READ_ONLY");
            Console.WriteLine("INSTALL_CAPABILITY=DISABLED");
            return 0;
        }

        private static int DenyExecution()
        {
            Console.WriteLine("EXECUTION_POLICY=BLOCKED_BY_DESIGN");
            Console.WriteLine("INSTALL_CAPABILITY=DISABLED");
            Console.WriteLine("MSI_EXECUTED=FALSE");
            return 7;
        }

        // PHASE30B5D_SIGNED_INSTALL_GUARD_V1
        // Release signing has not been configured yet. This pinned certificate fingerprint
        // must be set to a independently verified signing certificate SHA-256 only after
        // separate release-signing and isolated-VM upgrade tests.
        private const string PinnedPublisherCertificateSha256 = "";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WintrustFileInfo
        {
            public uint Size;
            public IntPtr FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WintrustData
        {
            public uint Size;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UIChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UIContext;
            public IntPtr SignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WintrustData trustData);

        private static bool TrustedPublisherSignature(string file, out string reason)
        {
            reason = "PUBLISHER_PIN_NOT_CONFIGURED";
            if (!Regex.IsMatch(PinnedPublisherCertificateSha256, "^[A-Fa-f0-9]{64}$")) return false;
            IntPtr fileName = IntPtr.Zero;
            IntPtr filePtr = IntPtr.Zero;
            try
            {
                fileName = Marshal.StringToHGlobalUni(file);
                WintrustFileInfo fileInfo = new WintrustFileInfo();
                fileInfo.Size = (uint)Marshal.SizeOf(typeof(WintrustFileInfo));
                fileInfo.FilePath = fileName;
                filePtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WintrustFileInfo)));
                Marshal.StructureToPtr(fileInfo, filePtr, false);
                WintrustData data = new WintrustData();
                data.Size = (uint)Marshal.SizeOf(typeof(WintrustData));
                data.UIChoice = 2;                    // WTD_UI_NONE
                data.RevocationChecks = 1;            // WTD_REVOKE_WHOLECHAIN
                data.UnionChoice = 1;                 // WTD_CHOICE_FILE
                data.FileInfo = filePtr;
                data.StateAction = 0;                 // WTD_STATEACTION_IGNORE
                data.ProviderFlags = 0x80;           // WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT
                Guid policy = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                int trust = WinVerifyTrust(IntPtr.Zero, ref policy, ref data);
                if (trust != 0)
                {
                    reason = "AUTHENTICODE_TRUST_FAILED_0x" + trust.ToString("X8");
                    return false;
                }
                using (var signedCert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                    System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(file)))
                using (var sha = SHA256.Create())
                {
                    string fingerprint = BitConverter.ToString(sha.ComputeHash(signedCert.RawData)).Replace("-", "");
                    if (!String.Equals(fingerprint, PinnedPublisherCertificateSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        reason = "PUBLISHER_CERTIFICATE_PIN_MISMATCH";
                        return false;
                    }
                }
                reason = "TRUSTED_SIGNATURE_AND_PIN";
                return true;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                reason = "AUTHENTICODE_CERTIFICATE_UNAVAILABLE";
                return false;
            }
            finally
            {
                if (filePtr != IntPtr.Zero) Marshal.FreeHGlobal(filePtr);
                if (fileName != IntPtr.Zero) Marshal.FreeHGlobal(fileName);
            }
        }

        private static int TrustSelfTest()
        {
            string reason;
            bool accepted = TrustedPublisherSignature("nonexistent-msi", out reason);
            if (accepted || reason != "PUBLISHER_PIN_NOT_CONFIGURED")
                throw new InvalidOperationException("The publisher pin MUST be unset for this test phase.");
            Console.WriteLine("PHASE30B5D_TRUST_SELFTEST=PASS");
            Console.WriteLine("PUBLISHER_AUTHENTICITY=NOT_ESTABLISHED");
            Console.WriteLine("MSI_EXECUTED=FALSE");
            return 0;
        }

        private static int ControlledUpgrade(string[] args, bool installRequested)
        {
            // No simulated --current value is accepted. Execution is deliberately
            // unavailable until a trusted publisher certificate is pinned in source.
            Console.WriteLine("PHASE30B5D_MODE=" + (installRequested ? "EXECUTE_REQUESTED" : "READINESS_ONLY"));
            Console.WriteLine("ACTUAL_INSTALLATION_CHECK=WINDOWS_INSTALLER_API");
            InstalledProduct installed = ReadActualInstallation();
            if (installed == null)
            {
                Console.WriteLine("PHASE30B5D_GATE=NO_INSTALLED_PRODUCT");
                Console.WriteLine("MSI_EXECUTED=FALSE");
                return 4;
            }
            string expectedText = RequireArg(args, "--expected");
            Version expected = ParseMsiVersion(expectedText);
            Console.WriteLine("ACTUAL_INSTALLED_VERSION=" + installed.Version.ToString(3));
            if (expected.CompareTo(installed.Version) <= 0)
            {
                Console.WriteLine("PHASE30B5D_GATE=NOT_NEWER");
                Console.WriteLine("MSI_EXECUTED=FALSE");
                return 3;
            }
            string file = RequireArg(args, "--msi");
            string hash = RequireArg(args, "--sha256");
            // Re-run full preflight with the ACTUAL registered product version.
            string[] check = { "--preflight", "--msi", file, "--sha256", hash,
                               "--current", installed.Version.ToString(3), "--expected", expectedText };
            int result = Preflight(check);
            if (result != 0)
            {
                Console.WriteLine("PHASE30B5D_GATE=MSI_PREFLIGHT_FAILED");
                Console.WriteLine("MSI_EXECUTED=FALSE");
                return result;
            }
            string proposedCode = ReadMsi(file, "ProductCode");
            if (String.Equals(proposedCode, installed.Code, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Major upgrade requires a different ProductCode.");
            string reason;
            if (!TrustedPublisherSignature(Path.GetFullPath(file), out reason))
            {
                Console.WriteLine("PHASE30B5D_GATE=" + reason);
                Console.WriteLine("EXECUTION_POLICY=BLOCKED");
                Console.WriteLine("MSI_EXECUTED=FALSE");
                return 7;
            }
            Console.WriteLine("PHASE30B5D_GATE=SIGNED_PACKAGE_VERIFIED");
            if (!installRequested)
            {
                Console.WriteLine("EXECUTION_POLICY=READINESS_ONLY");
                Console.WriteLine("MSI_EXECUTED=FALSE");
                return 0;
            }
            // Stop before elevation if the actual app is still running.
            if (System.Diagnostics.Process.GetProcessesByName("Auto Backlight for ThinkPad").Length != 0)
            {
                Console.WriteLine("PHASE30B5D_GATE=APPLICATION_STILL_RUNNING");
                Console.WriteLine("MSI_EXECUTED=FALSE");
                return 7;
            }
            if (Console.IsInputRedirected || Console.IsOutputRedirected)
            {
                Console.WriteLine("PHASE30B5D_GATE=INTERACTIVE_CONSENT_REQUIRED");
                Console.WriteLine("MSI_EXECUTED=FALSE");
                return 7;
            }
            Console.Write("Type INSTALL " + expected.ToString(3) + " to authorize this MSI upgrade: ");
            string consent = Console.ReadLine();
            if (!String.Equals(consent, "INSTALL " + expected.ToString(3), StringComparison.Ordinal))
            {
                Console.WriteLine("PHASE30B5D_GATE=CONSENT_DECLINED");
                Console.WriteLine("MSI_EXECUTED=FALSE");
                return 7;
            }
            // The following path is unreachable in the current release: pinned cert is empty.
            // Before enabling a publisher pin, add a protected elevated staging directory
            // to prevent file-swap races between verification and msiexec opening the file.
            Console.WriteLine("PHASE30B5D_GATE=PROTECTED_STAGING_NOT_IMPLEMENTED");
            Console.WriteLine("MSI_EXECUTED=FALSE");
            return 7;
        }

        // PHASE30B7D1_INDEPENDENT_SIGNED_ASSESS_V1
        // This is a read-only trust boundary. It NEVER launches msiexec or elevates privileges.
        // Never treat signed-assess=PASS as permission to install from user-writable staging.
        private static byte[] ReadSmallSignedAsset(string path, string expectedName, int maximum)
        {
            string full = Path.GetFullPath(path);
            if (!String.Equals(Path.GetFileName(full), expectedName, StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected signed asset filename.");
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Signed asset must not be a reparse point.");
            using (var input = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length == 0 || input.Length > maximum)
                    throw new InvalidOperationException("Signed asset size outside limit.");
                byte[] result = new byte[(int)input.Length];
                int position = 0;
                while (position < result.Length)
                {
                    int count = input.Read(result, position, result.Length - position);
                    if (count <= 0) throw new EndOfStreamException("Truncated signed asset.");
                    position += count;
                }
                return result;
            }
        }

        private static int SignedAssess(string[] args)
        {
            string manifestPath = Path.GetFullPath(RequireArg(args, "--manifest"));
            string signaturePath = Path.GetFullPath(RequireArg(args, "--signature"));
            string msiPath = Path.GetFullPath(RequireArg(args, "--msi"));
            string parent = Path.GetDirectoryName(msiPath);
            if (!String.Equals(Path.GetDirectoryName(manifestPath), parent, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(Path.GetDirectoryName(signaturePath), parent, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Manifest, signature and MSI must share one staging directory.");
            if (!Directory.Exists(parent) || (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Unexpected staging directory.");
            string[] parents = { Path.GetDirectoryName(parent), Path.GetDirectoryName(Path.GetDirectoryName(parent)) };
            foreach (string directory in parents)
                if (String.IsNullOrEmpty(directory) || !Directory.Exists(directory) ||
                    (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Unsafe staging parent directory.");

            byte[] manifestBytes = ReadSmallSignedAsset(manifestPath, "update-manifest-v1.txt", 2048);
            byte[] signatureBytes = ReadSmallSignedAsset(signaturePath, "update-manifest-v1.sig", 512);
            Auto_Backlight_for_ThinkPad.Phase30B7BVerifiedManifest signed =
                Auto_Backlight_for_ThinkPad.Phase30B7BReleaseManifestTrust.Verify(manifestBytes, signatureBytes);
            Console.WriteLine("SIGNED_MANIFEST_VERIFIED=TRUE");
            Console.WriteLine("PINNED_PUBLIC_CERT_SHA256=" +
                Auto_Backlight_for_ThinkPad.Phase30B7BReleaseManifestTrust.PublicCertificateSha256);
            ValidateStagingPath(msiPath, signed.Version);
            InstalledProduct installed = ReadActualInstallation();
            if (installed == null)
                throw new InvalidOperationException("Application is not installed.");
            Console.WriteLine("ACTUAL_INSTALLED_VERSION=" + installed.Version.ToString(3));
            if (signed.Version.CompareTo(installed.Version) <= 0)
            {
                Console.WriteLine("PHASE30B7D1_SIGNED_ASSESS=NOT_NEWER");
                Console.WriteLine("MSI_EXECUTED=FALSE");
                return 3;
            }
            long length;
            string calculatedHash = CalculateHash(msiPath, out length);
            if (length != signed.SizeBytes ||
                !String.Equals(calculatedHash, signed.Sha256, StringComparison.Ordinal))
                throw new InvalidOperationException("Signed MSI size or digest mismatch.");
            string[] check = { "--preflight", "--msi", msiPath,
                "--sha256", signed.Sha256, "--current", installed.Version.ToString(3),
                "--expected", signed.Version.ToString(3) };
            int status = Preflight(check);
            if (status != 0) throw new InvalidOperationException("MSI preflight rejected the signed package.");
            string code = ReadMsi(msiPath, "ProductCode");
            if (String.Equals(code, installed.Code, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Major upgrade requires a new ProductCode.");
            Console.WriteLine("PHASE30B7D1_SIGNED_ASSESS=PASS");
            Console.WriteLine("STAGING_TRUST_LEVEL=USER_WRITABLE_NOT_EXECUTION_SAFE");
            Console.WriteLine("INSTALL_CAPABILITY=DISABLED");
            Console.WriteLine("MSI_EXECUTED=FALSE");
            return 0;
        }

        // Phase 30B.5B: plan-only installer orchestration. Never executes msiexec.
        private static string QuoteWindowsArgument(string value)
        {
            if (String.IsNullOrEmpty(value) || value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
                throw new ArgumentException("Unsupported command-line argument.");
            return "\"" + value + "\"";
        }

        private static string InstallationExitClassification(int code)
        {
            switch (code)
            {
                case 0: return "SUCCESS";
                case 3010: return "SUCCESS_REBOOT_REQUIRED";
                case 1641: return "REBOOT_INITIATED";
                case 1602: return "USER_CANCELLED";
                case 1603: return "INSTALL_FAILED";
                case 1618: return "ANOTHER_INSTALL_IN_PROGRESS";
                case 1619: return "PACKAGE_OPEN_FAILED";
                case 1620: return "PACKAGE_INVALID";
                case 1223: return "ELEVATION_CANCELLED";
                default: return "UNEXPECTED_EXIT_CODE";
            }
        }

        private static int SimulateExitCode(string[] args)
        {
            if (args.Length != 2) throw new ArgumentException("Usage: --simulate-exit CODE");
            int code;
            if (!Int32.TryParse(args[1], out code) || code < 0 || code > 65535)
                throw new ArgumentException("Expected nonnegative Windows Installer exit code.");
            Console.WriteLine("SIMULATED_EXIT_CODE=" + code);
            Console.WriteLine("SIMULATED_EXIT_STATUS=" + InstallationExitClassification(code));
            Console.WriteLine("MSI_EXECUTED=FALSE");
            return 0;
        }

        private static int PlanInstall(string[] args)
        {
            // Uses the same preflight validation as Phase 30B.5A; the passed
            // --current value is SIMULATED and may not reflect Windows installed state.
            int result = Preflight(args);
            if (result != 0)
            {
                Console.WriteLine("PHASE30B5B_PLAN=REJECTED");
                return result;
            }
            string path = Path.GetFullPath(RequireArg(args, "--msi"));
            string version = RequireArg(args, "--expected");
            // This is a hypothetical diagnostic log path, not a created file.
            string logRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Auto Backlight for ThinkPad", "UpdateLogs");
            string logPath = Path.Combine(logRoot, "AutoBacklight-" + version + "-upgrade.log");
            string installer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe");
            Console.WriteLine("PLANNED_EXECUTABLE=" + installer);
            Console.WriteLine("PLANNED_ARGUMENTS=/i " + QuoteWindowsArgument(path) +
                " /norestart /L*v " + QuoteWindowsArgument(logPath));
            Console.WriteLine("PLANNED_UAC=WINDOWS_CONTROLLED");
            Console.WriteLine("INSTALLED_VERSION_CHECK=NOT_IMPLEMENTED");
            Console.WriteLine("PUBLISHER_AUTHENTICITY=NOT_ESTABLISHED");
            Console.WriteLine("APP_SHUTDOWN_AND_RELAUNCH=NOT_IMPLEMENTED");
            Console.WriteLine("MSI_EXECUTED=FALSE");
            Console.WriteLine("PHASE30B5B_PLAN=PASS");
            return 0;
        }

        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 1 && args[0] == "--selftest") { SelfTest(); return 0; }
                if (args.Length == 1 && args[0] == "--trust-selftest") return TrustSelfTest();
                if (args.Length == 9 && args[0] == "--preflight") return Preflight(args);
                if (args.Length == 9 && args[0] == "--plan") return PlanInstall(args);
                if (args.Length == 1 && args[0] == "--inventory") return Inventory();
                if (args.Length == 7 && args[0] == "--assess") return AssessActualUpgrade(args);
                if (args.Length == 7 && args[0] == "--signed-assess") return SignedAssess(args);
                if (args.Length == 7 && args[0] == "--readiness") return ControlledUpgrade(args, false);
                if (args.Length == 7 && args[0] == "--execute") return ControlledUpgrade(args, true);
                if (args.Length >= 1 && args[0] == "--execute") return DenyExecution();
                if (args.Length == 2 && args[0] == "--simulate-exit") return SimulateExitCode(args);
                Console.Error.WriteLine("Usage: UpdateHelper.exe --selftest");
                Console.Error.WriteLine("   or: UpdateHelper.exe --preflight --msi PATH --sha256 HASH --current 1.3.8 --expected 1.3.9");
                Console.Error.WriteLine("   or: UpdateHelper.exe --plan --msi PATH --sha256 HASH --current 1.3.7 --expected 1.3.8");
                Console.Error.WriteLine("   or: UpdateHelper.exe --simulate-exit 3010");
                Console.Error.WriteLine("   or: UpdateHelper.exe --inventory");
                Console.Error.WriteLine("   or: UpdateHelper.exe --signed-assess --manifest PATH --signature PATH --msi PATH (READ ONLY)");
                Console.Error.WriteLine("   or: UpdateHelper.exe --assess --msi PATH --sha256 HASH --expected 1.3.9");
                Console.Error.WriteLine("   or: UpdateHelper.exe --readiness --msi PATH --sha256 HASH --expected 1.3.9");
                Console.Error.WriteLine("   or: UpdateHelper.exe --execute --msi PATH --sha256 HASH --expected 1.3.9 (BLOCKED until protected staging + publisher signing)");
                Console.Error.WriteLine("Read-only inspection only. Actual installer execution remains BLOCKED.");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PHASE30B5A_PREFLIGHT=FAIL: " + ex.Message);
                return 1;
            }
        }
    }
}
