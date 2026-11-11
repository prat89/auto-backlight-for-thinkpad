// Administrator-protected signed update staging.
// Explicit elevated command-line mode. It prepares ONLY a locked-down copy of a
// publisher-signed MSI bundle and re-verifies it. No installer/UAC launch here.
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class ProtectedUpdateStaging
    {
        internal const string Switch = "--update-protected-stage";
        private const string Manifest = "update-manifest-v1.txt";
        private const string Signature = "update-manifest-v1.sig";
        private const string ProtectedRootName = "AutoBacklightProtectedStaging";
        private const long MaxMsiLength = 128L * 1024L * 1024L;
        private static readonly SecurityIdentifier AdminSid =
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        private static readonly SecurityIdentifier SystemSid =
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        internal static int Run(string[] args)
        {
            if (args == null || args.Length != 2 || args[0] != Switch)
                return 10;
            try
            {
                // An ordinary user must not create protected staging or trigger elevation.
                using (WindowsIdentity current = WindowsIdentity.GetCurrent())
                {
                    if (current == null || !new WindowsPrincipal(current).IsInRole(WindowsBuiltInRole.Administrator))
                        return 13;
                }
                Prepare(args[1]);
                return 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Protected staging rejected: " + ex.GetType().Name);
                return 14;
            }
        }

        private static bool IsReparse(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static DirectorySecurity LockedDirectorySecurity()
        {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(AdminSid);
            FileSystemRights rights = FileSystemRights.FullControl;
            InheritanceFlags inherits = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(AdminSid, rights, inherits,
                PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(SystemSid, rights, inherits,
                PropagationFlags.None, AccessControlType.Allow));
            return security;
        }

        private static FileSecurity LockedFileSecurity()
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(AdminSid);
            security.AddAccessRule(new FileSystemAccessRule(AdminSid,
                FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(SystemSid,
                FileSystemRights.FullControl, AccessControlType.Allow));
            return security;
        }

        private static void VerifySecurity(string path, bool directory)
        {
            if (IsReparse(path)) throw new InvalidDataException("Protected path is a reparse point.");
            FileSystemSecurity security = directory
                ? (FileSystemSecurity)Directory.GetAccessControl(path, AccessControlSections.Owner | AccessControlSections.Access)
                : (FileSystemSecurity)File.GetAccessControl(path, AccessControlSections.Owner | AccessControlSections.Access);
            if (!security.AreAccessRulesProtected)
                throw new UnauthorizedAccessException("Protected staging inherited an ACL.");
            var owner = (SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier));
            if (!owner.Equals(AdminSid) && !owner.Equals(SystemSid))
                throw new UnauthorizedAccessException("Protected staging has an unsafe owner.");
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
            int allowCount = 0;
            foreach (FileSystemAccessRule rule in rules)
            {
                var sid = (SecurityIdentifier)rule.IdentityReference;
                if (rule.IsInherited || rule.AccessControlType != AccessControlType.Allow ||
                    (!sid.Equals(AdminSid) && !sid.Equals(SystemSid)))
                    throw new UnauthorizedAccessException("Unexpected ACL in protected staging.");
                allowCount++;
            }
            if (allowCount < 2) throw new UnauthorizedAccessException("Incomplete protected staging ACL.");
        }

        private static void CopyBounded(string from, string to, long max)
        {
            if (IsReparse(from)) throw new InvalidDataException("Source asset is a reparse point.");
            using (var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (source.Length < 1 || source.Length > max)
                    throw new InvalidDataException("Source asset is empty or oversized.");
                // Destination is protected from the instant it is created, not after File.Copy.
                using (var destination = new FileStream(to, FileMode.CreateNew,
                    FileSystemRights.Write, FileShare.None, 81920, FileOptions.None, LockedFileSecurity()))
                {
                    source.CopyTo(destination);
                    destination.Flush(true);
                }
                if (new FileInfo(to).Length != source.Length)
                    throw new InvalidDataException("Copied asset has a different size.");
            }
            VerifySecurity(to, false);
        }

        private static void Prepare(string source)
        {
            // Unprivileged staging must be a single GUID folder under our exact LocalAppData root.
            if (SingleExeUpdateAssessment.Run(new string[] {
                SingleExeUpdateAssessment.Switch, source }) != 0)
                throw new InvalidDataException("The source bundle did not pass complete assessment.");

            string sourceDir = Path.GetFullPath(source);
            string sourceRoot = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Auto Backlight for ThinkPad", "UpdateStaging"));
            if (!String.Equals(Path.GetDirectoryName(sourceDir), sourceRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Source stage has an unexpected parent.");
            if (IsReparse(sourceDir) || IsReparse(sourceRoot) ||
                IsReparse(Path.GetDirectoryName(sourceRoot)))
                throw new InvalidDataException("Source folder contains reparse points.");

            // Derive the exact MSI filename from a fresh reading of the signed manifest.
            string manifestFile = Path.Combine(sourceDir, Manifest);
            string signatureFile = Path.Combine(sourceDir, Signature);
            byte[] manifest = ReadBounded(manifestFile, 2048);
            byte[] signature = ReadBounded(signatureFile, 512);
            var verified = ReleaseManifestTrust.Verify(manifest, signature);
            string msiName = "AutoBacklight-" + verified.Version.ToString(3) + ".msi";

            string programData = Path.GetFullPath(Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData));
            if (String.IsNullOrWhiteSpace(programData) || !Directory.Exists(programData) ||
                IsReparse(programData))
                throw new InvalidDataException("Invalid ProgramData location.");
            string root = Path.Combine(programData, ProtectedRootName);
            // No attacker-selected paths. The root and child each get protected ACLs atomically.
            if (Directory.Exists(root)) VerifySecurity(root, true);
            else Directory.CreateDirectory(root, LockedDirectorySecurity());
            VerifySecurity(root, true);

            string destination = Path.Combine(root, Path.GetFileName(sourceDir));
            if (Directory.Exists(destination) || File.Exists(destination))
                throw new InvalidDataException("Destination already exists; refuse to reuse it.");
            Directory.CreateDirectory(destination, LockedDirectorySecurity());
            try
            {
                VerifySecurity(destination, true);
                CopyBounded(manifestFile, Path.Combine(destination, Manifest), 2048);
                CopyBounded(signatureFile, Path.Combine(destination, Signature), 512);
                CopyBounded(Path.Combine(sourceDir, msiName), Path.Combine(destination, msiName), MaxMsiLength);
                // The only authoritative assessment is on protected copies with locked ACLs.
                SingleExeUpdateAssessment.VerifySignedContents(destination);
                VerifySecurity(destination, true);
                VerifySecurity(Path.Combine(destination, Manifest), false);
                VerifySecurity(Path.Combine(destination, Signature), false);
                VerifySecurity(Path.Combine(destination, msiName), false);
                // Deliberately do not execute MSI. Only the approved installer may consume this bundle.
            }
            catch
            {
                // Only remove the child that this process created; never touch pre-existing folders.
                try { Directory.Delete(destination, true); } catch { /* manual admin cleanup */ }
                throw;
            }
        }

        // Read-only audit of protected staging contents.
        // Read-only, fail-closed revalidation of a previously protected bundle.
        // Takes only a canonical GUID; no user-chosen filename or directory.
        internal static string VerifyProtectedReadOnly(string id)
        {
            Guid parsed;
            if (String.IsNullOrEmpty(id) || id.Length != 32 ||
                !Guid.TryParseExact(id, "N", out parsed) || parsed == Guid.Empty ||
                !String.Equals(parsed.ToString("N"), id, StringComparison.Ordinal))
                throw new InvalidDataException("Invalid protected bundle identifier.");

            string programData = Path.GetFullPath(Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData));
            if (!Directory.Exists(programData) || IsReparse(programData))
                throw new InvalidDataException("Invalid ProgramData location.");
            string root = Path.Combine(programData, ProtectedRootName);
            string stage = Path.Combine(root, id);
            if (!Directory.Exists(root) || !Directory.Exists(stage))
                throw new DirectoryNotFoundException("Protected stage is absent.");
            VerifySecurity(root, true);
            VerifySecurity(stage, true);

            string manifestFile = Path.Combine(stage, Manifest);
            string signatureFile = Path.Combine(stage, Signature);
            byte[] manifest = ReadBounded(manifestFile, 2048);
            byte[] signature = ReadBounded(signatureFile, 512);
            var verified = ReleaseManifestTrust.Verify(manifest, signature);
            string msiName = "AutoBacklight-" + verified.Version.ToString(3) + ".msi";
            string msiPath = Path.Combine(stage, msiName);
            // Reject unexpected files/dirs (including planted logs and reparse points).
            if (Directory.GetDirectories(stage).Length != 0)
                throw new InvalidDataException("Unexpected protected subdirectory.");
            string[] files = Directory.GetFiles(stage);
            if (files.Length != 3)
                throw new InvalidDataException("Protected bundle must contain exactly three assets.");
            var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (string file in files) names.Add(Path.GetFileName(file));
            if (!names.Contains(Manifest) || !names.Contains(Signature) ||
                !names.Contains(msiName) || names.Count != 3)
                throw new InvalidDataException("Unexpected asset in protected bundle.");
            VerifySecurity(manifestFile, false);
            VerifySecurity(signatureFile, false);
            VerifySecurity(msiPath, false);
            // This also rechecks publisher RSA signature, hash, version,
            // native MSI identity AND installed MSI registration.
            SingleExeUpdateAssessment.VerifySignedContents(stage);
            VerifySecurity(root, true);
            VerifySecurity(stage, true);
            return msiPath;
        }

        private static byte[] ReadBounded(string path, long max)
        {
            if (IsReparse(path)) throw new InvalidDataException("Reparse source asset.");
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length < 1 || stream.Length > max)
                    throw new InvalidDataException("Unacceptable signed asset size.");
                byte[] bytes = new byte[checked((int)stream.Length)];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int count = stream.Read(bytes, offset, bytes.Length - offset);
                    if (count == 0) throw new EndOfStreamException();
                    offset += count;
                }
                return bytes;
            }
        }
    }
}
