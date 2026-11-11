// Verify trusted publisher signatures on release manifests.
// Detached publisher-signed update manifest verification only. No installer execution.
// Public RSA-3072 trust anchor is pinned here; NEVER embed private key material.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace Auto_Backlight_for_ThinkPad
{
    public sealed class VerifiedManifest
    {
        public Version Version { get; private set; }
        public string AssetName { get; private set; }
        public string Sha256 { get; private set; }
        public long SizeBytes { get; private set; }

        internal VerifiedManifest(Version version, string asset, string sha, long size)
        {
            Version = version;
            AssetName = asset;
            Sha256 = sha;
            SizeBytes = size;
        }
    }

    public static class ReleaseManifestTrust
    {
        public const string PublicCertificateSha256 = "30916ADE0227A9C1D5156022BFABE0A29440671EC0341055C0AB04763F3EE701";
        private const string Repository = "prat89/auto-backlight-for-thinkpad";
        private const string FamilyUpgradeCode = "{A78F780D-063F-4852-AC1B-D20FB7165BA2}";
        private const int MaximumManifestBytes = 2048;
        private const long MaximumMsiBytes = 128L * 1024L * 1024L;
        private const string DottedAsset = "Auto.Backlight.for.ThinkPad.Setup.msi";
        private const string SpacedAsset = "Auto Backlight for ThinkPad Setup.msi";

        // Exact DER certificate bytes, read from the user's PUBLIC .cer. Never a release-supplied certificate.
        private const string CertificateDerBase64 =
            "MIIEPTCCAqWgAwIBAgIQdxfNj7tGk6pByP/fgxD9LjANBgkqhkiG9w0BAQsFADBBMT8wPQYDVQQDDDZBdXRvIEJhY2tsaWdo" +
            "dCBmb3IgVGhpbmtQYWQgLSBSZWxlYXNlIE1hbmlmZXN0IFNpZ25pbmcwHhcNMjYxMTA1MTk0MzI1WhcNMzExMTA1MTk1MzI1" +
            "WjBBMT8wPQYDVQQDDDZBdXRvIEJhY2tsaWdodCBmb3IgVGhpbmtQYWQgLSBSZWxlYXNlIE1hbmlmZXN0IFNpZ25pbmcwggGi" +
            "MA0GCSqGSIb3DQEBAQUAA4IBjwAwggGKAoIBgQCsmwKPSDZgYeJ0qhUqhbGpfmohxbPQ96Td8Ba4e+KsHvgqWmlBlc1AeGaV" +
            "uKynMBQxOr3NNZQzbYd+7NWrajU8TS3Xok2CF06AQd47sJlNCjcvCqZaB2Ib9fzLEUp1VjTFl2Tmf2037/IBnSaCDaNL1Aeq" +
            "gVlR4OFeraQABvstx9ixmWxJ3/QpDK3T6Mn3RnfvwFunX7DFIrhAX3cWRlfZEXcdPoueyEsMghptuFH758dcEat7TsBqKNMq" +
            "fochKcpffOpe8FQJpwzO8MHeQ4x65q10og2n9kHsm6WZ1C7Zl2FIwLZkT7fTJQDV+m39AArZAss5SCBmpPVNfSE0xoLPFH6h" +
            "5Id/96C13kU9JI9sMqvZkyYHlBqadpppt+ZREnrcPSmvQj3mvPGt0wgKfCohYTXnChl6reDGs4G9SRwBgJ7DDCJG+MseiORd" +
            "30cBt+7Eh2dJVZUdUTT34UlNtkEdLsG498V+bO9FiT/AtmWW++QmvhOEZ403tOyqDSW5HaUCAwEAAaMxMC8wDgYDVR0PAQH/" +
            "BAQDAgeAMB0GA1UdDgQWBBRo4FyhXlOQ0ZUy4rQDvATMVqU2fTANBgkqhkiG9w0BAQsFAAOCAYEAPAgaD9xOYw92KRIcFaOb" +
            "py3ZLrpganUvKviJN3kBSF6bjG9lxxYsQJaQtaPHHHsZWZM8vHFzOBdBAeT4sc2BOIKWqvriHK2gGjf1wYcsTzJpRxH53oxq" +
            "oROm6upQdvL60BL2ujmRNq/P11UQxIuhctE2p7ZRc8oAMNReEIabOpO/+EWRFtG2nhSerp1SXxXMt8s6ic826yj4+BDTcD0n" +
            "B0FIN5ogtwUVFlNz63My35LXs/zen/T8tRVHh8E67YH0xZAq+82fUBtA+7qsAgQBDp/13nb9Nv1OHJl7MJJxLiONvYx8XZGw" +
            "fzRBzHx1eBv2yl9ZJmEJzZx5pZ7S+YAbP1Bf/yh+zyn2G1v6K3LhoYWbAVwF73jqK2OJqO4PW7IdKzlqjpBIO32L1cXhL8qq" +
            "tVb+9c85AXvzVyWY/XfWOVUptTpWEtLEMIvfLi9PbpwyVbVWXIl19ahnvN88HDl/vKA4fOEICIss3oEiaM+leMbj0SC646a8" +
            "0z5uTy+wEng8";

        private static void Reject(bool invalid, string reason)
        {
            if (invalid) throw new InvalidDataException(reason);
        }

        private static string RequireLine(string line, string key)
        {
            string prefix = key + "=";
            Reject(!line.StartsWith(prefix, StringComparison.Ordinal), "Manifest field order/name mismatch: " + key);
            return line.Substring(prefix.Length);
        }

        private static Version StrictMsiVersion(string text)
        {
            Reject(!Regex.IsMatch(text, @"^(0|[1-9][0-9]{0,2})\.(0|[1-9][0-9]{0,2})\.(0|[1-9][0-9]{0,4})$",
                RegexOptions.CultureInvariant), "Manifest version is not canonical X.Y.Z.");
            Version result;
            Reject(!Version.TryParse(text, out result), "Manifest version is invalid.");
            Reject(result.Major < 1 || result.Major > 255 || result.Minor > 255 || result.Build > 65535,
                "Manifest version is outside Windows Installer bounds.");
            return result;
        }

        /// <summary>
        /// Verify exact manifest bytes and detached base64 signature with a build-pinned publisher key.
        /// Does not trust release-provided certificate, does not access network and does not install software.
        /// </summary>
        public static VerifiedManifest Verify(byte[] manifestBytes, byte[] signatureBase64Bytes)
        {
            Reject(manifestBytes == null || manifestBytes.Length < 120 ||
                manifestBytes.Length > MaximumManifestBytes, "Missing or oversized manifest.");
            Reject(signatureBase64Bytes == null || signatureBase64Bytes.Length != 512,
                "Signature must be 512 canonical ASCII base64 bytes for RSA-3072.");
            // Format is ASCII-only UTF-8, exactly LF, no BOM/CR, no control characters.
            foreach (byte b in manifestBytes)
                Reject(b != 10 && (b < 32 || b > 126), "Manifest must be canonical printable ASCII with LF.");
            foreach (byte b in signatureBase64Bytes)
                Reject(!((b >= 65 && b <= 90) || (b >= 97 && b <= 122) ||
                    (b >= 48 && b <= 57) || b == 43 || b == 47),
                    "Signature is not canonical base64.");
            string signatureText = Encoding.ASCII.GetString(signatureBase64Bytes);
            byte[] signature;
            try { signature = Convert.FromBase64String(signatureText); }
            catch (FormatException) { throw new InvalidDataException("Invalid signature encoding."); }
            Reject(signature.Length != 384 || Convert.ToBase64String(signature) != signatureText,
                "Noncanonical RSA-3072 signature.");
            byte[] certDer = Convert.FromBase64String(CertificateDerBase64);
            using (var hash = SHA256.Create())
            {
                string actualPin = BitConverter.ToString(hash.ComputeHash(certDer)).Replace("-", "");
                Reject(!String.Equals(actualPin, PublicCertificateSha256, StringComparison.Ordinal),
                    "Built-in public certificate pin mismatch.");
            }
            using (var certificate = new X509Certificate2(certDer))
            using (RSA publicKey = RSACertificateExtensions.GetRSAPublicKey(certificate))
            {
                Reject(publicKey == null || publicKey.KeySize != 3072, "Invalid pinned RSA public key.");
                Reject(!publicKey.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1), "Publisher signature is invalid.");
            }

            string manifest = Encoding.ASCII.GetString(manifestBytes);
            Reject(!manifest.EndsWith("\n", StringComparison.Ordinal), "Missing trailing LF.");
            string[] lines = manifest.Split('\n');
            Reject(lines.Length != 8 || lines[7] != "", "Manifest must have exactly seven LF-terminated lines.");
            Reject(lines[0] != "AUTOBACKLIGHT-UPDATE-MANIFEST-V1", "Unexpected manifest format.");
            Reject(RequireLine(lines[1], "repository") != Repository, "Unexpected repository identity.");
            Version version = StrictMsiVersion(RequireLine(lines[2], "version"));
            string asset = RequireLine(lines[3], "msi");
            Reject(asset != DottedAsset && asset != SpacedAsset, "Unexpected MSI asset name.");
            string sha = RequireLine(lines[4], "sha256");
            Reject(!Regex.IsMatch(sha, "^[0-9A-F]{64}$", RegexOptions.CultureInvariant),
                "Manifest SHA-256 must be 64 uppercase hex characters.");
            string sizeText = RequireLine(lines[5], "size");
            Reject(!Regex.IsMatch(sizeText, "^[1-9][0-9]{0,9}$", RegexOptions.CultureInvariant),
                "Manifest MSI size is not canonical decimal.");
            long size;
            Reject(!Int64.TryParse(sizeText, out size) || size > MaximumMsiBytes,
                "Manifest MSI size is outside allowed bounds.");
            Reject(RequireLine(lines[6], "upgrade_code") != FamilyUpgradeCode,
                "Unexpected MSI UpgradeCode.");
            return new VerifiedManifest(version, asset, sha, size);
        }

        /// <summary>Bind a verified manifest to concrete GitHub release metadata (untrusted until matched).</summary>
        public static void AssertMatchesMetadata(VerifiedManifest manifest,
            Version expectedVersion, string assetName, string sha256, long sizeBytes)
        {
            Reject(manifest == null || expectedVersion == null, "Missing manifest or release version.");
            // Compare MSI versions using three numeric components.
            // GitHub metadata uses 1.4.0.0 while the signed MSI manifest uses 1.4.0.
            // Compare Windows Installer's three parts, but reject nonzero revision.
            Reject(manifest.Version.Major != expectedVersion.Major ||
                manifest.Version.Minor != expectedVersion.Minor ||
                manifest.Version.Build != expectedVersion.Build ||
                (expectedVersion.Revision != -1 && expectedVersion.Revision != 0),
                "Signed release version mismatch.");
            Reject(!String.Equals(manifest.AssetName, assetName, StringComparison.Ordinal),
                "Signed release MSI filename mismatch.");
            Reject(String.IsNullOrEmpty(sha256) ||
                !Regex.IsMatch(sha256, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant) ||
                !String.Equals(manifest.Sha256, sha256, StringComparison.OrdinalIgnoreCase),
                "Signed release MSI digest mismatch.");
            Reject(manifest.SizeBytes != sizeBytes, "Signed release MSI size mismatch.");
        }
    }
}
