// Check that the registered MSI product matches the running EXE.
// Read-only inspection of the CURRENTLY REGISTERED MSI product.
// No UAC, installer invocation, registry writes, process launches or task edits.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class InstalledMsiGuard
    {
        internal const string AuditSwitch = "--update-installed-audit";
        private const string UpgradeCode = "{A78F780D-063F-4852-AC1B-D20FB7165BA2}";
        private const string ExpectedName = "Auto Backlight for ThinkPad";
        private const uint Success = 0, NoMoreItems = 259, MoreData = 234;

        [DllImport("msi.dll", EntryPoint = "MsiEnumRelatedProductsW", CharSet = CharSet.Unicode)]
        private static extern uint EnumRelatedProducts(string upgradeCode, uint reserved,
            uint index, StringBuilder productCode);

        [DllImport("msi.dll", EntryPoint = "MsiGetProductInfoW", CharSet = CharSet.Unicode)]
        private static extern uint GetProductInfo(string productCode, string property,
            StringBuilder output, ref uint capacity);

        internal static int RunAudit(string[] args)
        {
            if (args == null || args.Length != 1 || args[0] != AuditSwitch) return 10;
            try
            {
                RegisteredProduct installed = ReadInstalled();
                ValidateCurrentExe(installed.Version);
                if (String.IsNullOrEmpty(installed.ProductCode)) return 31;
                return 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Installed MSI audit rejected: " + ex.GetType().Name);
                return 31;
            }
        }

        private sealed class RegisteredProduct
        {
            internal string ProductCode;
            internal Version Version;
        }

        private static string ProductInfo(string productCode, string property)
        {
            var value = new StringBuilder(256);
            uint size = (uint)value.Capacity;
            uint result = GetProductInfo(productCode, property, value, ref size);
            if (result == MoreData && size < 4096)
            {
                value = new StringBuilder(checked((int)size + 1));
                size = (uint)value.Capacity;
                result = GetProductInfo(productCode, property, value, ref size);
            }
            if (result != Success || String.IsNullOrWhiteSpace(value.ToString()))
                throw new InvalidDataException("Registered MSI property could not be read.");
            return value.ToString();
        }

        private static RegisteredProduct ReadInstalled()
        {
            // Require exactly one related product. An advertised-only product must fail
            // when querying InstalledProductName / VersionString below.
            var product = new StringBuilder(39);
            uint result = EnumRelatedProducts(UpgradeCode, 0, 0, product);
            if (result != Success) throw new InvalidDataException("No unique installed MSI product.");
            string code = product.ToString();
            Guid parsed;
            if (!Guid.TryParseExact(code, "B", out parsed) || parsed == Guid.Empty)
                throw new InvalidDataException("Invalid registered ProductCode.");
            product = new StringBuilder(39);
            if (EnumRelatedProducts(UpgradeCode, 0, 1, product) != NoMoreItems)
                throw new InvalidDataException("Multiple or unreadable related MSI products.");
            if (!String.Equals(ProductInfo(code, "InstalledProductName"), ExpectedName,
                StringComparison.Ordinal))
                throw new InvalidDataException("Registered product name mismatch.");
            string rawVersion = ProductInfo(code, "VersionString");
            Version version;
            if (!Version.TryParse(rawVersion, out version) || version.Build < 0 ||
                (version.Revision != -1 && version.Revision != 0))
                throw new InvalidDataException("Invalid registered ProductVersion.");
            return new RegisteredProduct {
                ProductCode = code,
                Version = new Version(version.Major, version.Minor, version.Build)
            };
        }

        private static void ValidateCurrentExe(Version version)
        {
            Version exe = Assembly.GetExecutingAssembly().GetName().Version;
            if (exe == null || exe.Build < 0 ||
                (exe.Revision != 0 && exe.Revision != -1) ||
                new Version(exe.Major, exe.Minor, exe.Build) != version)
                throw new InvalidDataException("EXE version differs from registered MSI version.");
        }

        internal static void Verify(string newProductCode, Version targetVersion)
        {
            if (targetVersion == null || targetVersion.Build < 0 ||
                (targetVersion.Revision != -1 && targetVersion.Revision != 0))
                throw new InvalidDataException("Invalid target version.");
            Guid parsed;
            if (!Guid.TryParseExact(newProductCode, "B", out parsed) || parsed == Guid.Empty)
                throw new InvalidDataException("Invalid target ProductCode.");
            RegisteredProduct installed = ReadInstalled();
            ValidateCurrentExe(installed.Version);
            if (String.Equals(installed.ProductCode, newProductCode,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Major upgrade reuses installed ProductCode.");
            Version target = new Version(targetVersion.Major, targetVersion.Minor, targetVersion.Build);
            if (target.CompareTo(installed.Version) <= 0)
                throw new InvalidDataException("Candidate MSI is not newer than installed MSI.");
        }
    }
}
