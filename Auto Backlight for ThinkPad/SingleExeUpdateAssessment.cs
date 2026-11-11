// Standalone EXE update assessment.
// Standalone read-only invocation mode of the EXISTING application EXE.
// NEVER elevates, stages protected files, invokes msiexec, or changes installation.
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class SingleExeUpdateAssessment
    {
        internal const string Switch = "--update-assess";
        private const long MaxMsiBytes = 128L * 1024L * 1024L;
        private const int MaxManifestBytes = 2048;
        private const int SignatureBytes = 512;
        private const string ManifestName = "update-manifest-v1.txt";
        private const string SignatureName = "update-manifest-v1.sig";

        internal static int Run(string[] args)
        {
            // Only a diagnostic boundary: success means authentic signed bundle, NOT installable MSI.
            if (args == null || args.Length != 2 || args[0] != Switch)
                return 10;
            try
            {
                Assess(args[1]);
                return 0;
            }
            catch (Exception ex)
            {
                // Never include untrusted exception text in user-visible startup UI.
                System.Diagnostics.Debug.WriteLine("Update assessment rejected: " + ex.GetType().Name);
                return 11;
            }
        }

        private static bool HasReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static byte[] ReadExactBounded(string path, long maxBytes)
        {
            if (HasReparsePoint(path)) throw new InvalidDataException("Reparse-point asset.");
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length < 1 || stream.Length > maxBytes)
                    throw new InvalidDataException("Unexpected asset length.");
                byte[] data = new byte[checked((int)stream.Length)];
                int read = 0;
                while (read < data.Length)
                {
                    int n = stream.Read(data, read, data.Length - read);
                    if (n <= 0) throw new EndOfStreamException();
                    read += n;
                }
                return data;
            }
        }

        private static void Assess(string folder)
        {
            if (String.IsNullOrWhiteSpace(folder)) throw new InvalidDataException("Empty folder.");
            string root = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Auto Backlight for ThinkPad", "UpdateStaging"));
            string stage = Path.GetFullPath(folder);
            if (!Directory.Exists(root) || !Directory.Exists(stage))
                throw new DirectoryNotFoundException();
            if (!String.Equals(Path.GetDirectoryName(stage), root, StringComparison.OrdinalIgnoreCase) ||
                !Regex.IsMatch(Path.GetFileName(stage), "^[0-9a-f]{32}$", RegexOptions.CultureInvariant))
                throw new InvalidDataException("Not a direct staging subfolder.");
            if (HasReparsePoint(root) || HasReparsePoint(stage))
                throw new InvalidDataException("Reparse-point staging directory.");
            VerifySignedContents(stage);
        }

        // Reused ONLY after the caller has validated a separate protected staging path.
        // This method rechecks publisher signature, file size/hash and native MSI identity.
        internal static void VerifySignedContents(string stage)
        {
            string manifestPath = Path.Combine(stage, ManifestName);
            string signaturePath = Path.Combine(stage, SignatureName);
            byte[] manifest = ReadExactBounded(manifestPath, MaxManifestBytes);
            byte[] signature = ReadExactBounded(signaturePath, SignatureBytes);
            var verified = ReleaseManifestTrust.Verify(manifest, signature);
            Version installed = Assembly.GetExecutingAssembly().GetName().Version;
            if (installed == null || installed.Build < 0 ||
                (installed.Revision != 0 && installed.Revision != -1))
                throw new InvalidDataException("Unsupported installed version.");
            Version current = new Version(installed.Major, installed.Minor, installed.Build);
            if (verified.Version.CompareTo(current) <= 0)
                throw new InvalidDataException("Target is not newer.");
            string msiPath = Path.Combine(stage, "AutoBacklight-" + verified.Version.ToString(3) + ".msi");
            if (HasReparsePoint(msiPath)) throw new InvalidDataException("Reparse-point MSI.");
            using (var file = new FileStream(msiPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create())
            {
                if (file.Length != verified.SizeBytes || file.Length < 8 ||
                    file.Length > MaxMsiBytes)
                    throw new InvalidDataException("MSI size mismatch.");
                byte[] header = new byte[8];
                if (file.Read(header, 0, header.Length) != header.Length)
                    throw new InvalidDataException("Incomplete MSI header.");
                byte[] expectedHeader = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
                for (int i = 0; i < expectedHeader.Length; i++)
                    if (header[i] != expectedHeader[i])
                        throw new InvalidDataException("MSI signature mismatch.");
                file.Position = 0;
                string digest = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                if (!String.Equals(digest, verified.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException("MSI digest mismatch.");
            }
            // Verify native MSI package identity.
            MsiIdentity.Verify(msiPath, verified.Version);
            // Read-only identity validation is done AFTER signature, size and hash checks.
            // Later elevated installation MUST repeat checks on a protected, immutable copy.
        }
    }
}
