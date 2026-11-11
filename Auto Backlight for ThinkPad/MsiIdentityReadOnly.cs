// Verify native MSI package identity.
// Read-only inspection of an MSI database through msi.dll.
// Never starts msiexec, requests elevation, or modifies the MSI.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class MsiIdentity
    {
        private const uint Success = 0;
        private const uint MoreData = 234;
        private const uint NoMoreItems = 259;
        private const string ExpectedName = "Auto Backlight for ThinkPad";
        private const string ExpectedManufacturer = "prat89";
        private const string ExpectedUpgradeCode = "{A78F780D-063F-4852-AC1B-D20FB7165BA2}";
        // Known v1.3.8 ProductCode is rejected as a basic major-upgrade guard.
        // Before production execution, also compare against the currently installed
        // ProductCode using Windows Installer registration (not just this baseline).
        private const string BaselineProductCode = "{FDBFF080-C75E-4EE0-BE12-9B5683CDEA16}";

        [DllImport("msi.dll", EntryPoint = "MsiOpenDatabaseW", CharSet = CharSet.Unicode)]
        private static extern uint MsiOpenDatabase(string path, IntPtr readOnlyMode, out uint database);

        [DllImport("msi.dll", EntryPoint = "MsiDatabaseOpenViewW", CharSet = CharSet.Unicode)]
        private static extern uint MsiDatabaseOpenView(uint database, string query, out uint view);

        [DllImport("msi.dll", EntryPoint = "MsiViewExecute")]
        private static extern uint MsiViewExecute(uint view, uint record);

        [DllImport("msi.dll", EntryPoint = "MsiViewFetch")]
        private static extern uint MsiViewFetch(uint view, out uint record);

        [DllImport("msi.dll", EntryPoint = "MsiRecordGetStringW", CharSet = CharSet.Unicode)]
        private static extern uint MsiRecordGetString(uint record, uint field,
            StringBuilder buffer, ref uint length);

        [DllImport("msi.dll", EntryPoint = "MsiCloseHandle")]
        private static extern uint MsiCloseHandle(uint handle);

        private static void Check(uint status)
        {
            if (status != Success)
                throw new InvalidDataException("Windows Installer metadata API returned " + status + ".");
        }

        private static string ReadProperty(uint database, string name)
        {
            // Property names are fixed string literals supplied only by this class.
            uint view = 0, record = 0;
            try
            {
                string sql = "SELECT `Value` FROM `Property` WHERE `Property` = '" + name + "'";
                Check(MsiDatabaseOpenView(database, sql, out view));
                Check(MsiViewExecute(view, 0));
                uint result = MsiViewFetch(view, out record);
                if (result == NoMoreItems || record == 0)
                    throw new InvalidDataException("MSI property absent.");
                Check(result);
                StringBuilder builder = new StringBuilder(256);
                uint length = (uint)builder.Capacity;
                result = MsiRecordGetString(record, 1, builder, ref length);
                if (result == MoreData)
                {
                    if (length > 4096) throw new InvalidDataException("Oversize MSI property.");
                    builder = new StringBuilder(checked((int)length + 1));
                    length = (uint)builder.Capacity;
                    result = MsiRecordGetString(record, 1, builder, ref length);
                }
                Check(result);
                string value = builder.ToString();
                if (String.IsNullOrWhiteSpace(value))
                    throw new InvalidDataException("Empty MSI property.");
                return value;
            }
            finally
            {
                if (record != 0) MsiCloseHandle(record);
                if (view != 0) MsiCloseHandle(view);
            }
        }

        internal static void Verify(string path, Version expectedVersion)
        {
            if (expectedVersion == null || expectedVersion.Build < 0 ||
                (expectedVersion.Revision != 0 && expectedVersion.Revision != -1))
                throw new InvalidDataException("Invalid expected MSI version.");
            if (String.IsNullOrWhiteSpace(path) ||
                !String.Equals(System.IO.Path.GetExtension(path), ".msi",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid MSI path.");

            uint database = 0;
            try
            {
                // IntPtr.Zero = MSIDBOPEN_READONLY. Do not change to TRANSACT / DIRECT.
                Check(MsiOpenDatabase(path, IntPtr.Zero, out database));
                if (database == 0) throw new InvalidDataException("MSI database not opened.");
                string version = ReadProperty(database, "ProductVersion");
                string productCode = ReadProperty(database, "ProductCode");
                string upgradeCode = ReadProperty(database, "UpgradeCode");
                string productName = ReadProperty(database, "ProductName");
                string manufacturer = ReadProperty(database, "Manufacturer");

                if (!String.Equals(version, expectedVersion.ToString(3), StringComparison.Ordinal))
                    throw new InvalidDataException("MSI version does not match signed manifest.");
                if (!String.Equals(productName, ExpectedName, StringComparison.Ordinal) ||
                    !String.Equals(manufacturer, ExpectedManufacturer, StringComparison.Ordinal))
                    throw new InvalidDataException("Unexpected MSI publisher/product.");
                if (!String.Equals(upgradeCode, ExpectedUpgradeCode, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unexpected MSI UpgradeCode.");
                Guid parsed;
                if (!Guid.TryParseExact(productCode, "B", out parsed) || parsed == Guid.Empty)
                    throw new InvalidDataException("Invalid MSI ProductCode.");
                if (String.Equals(productCode, BaselineProductCode, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Major upgrade reused the v1.3.8 ProductCode.");
                // Reuse the installed MSI product identity guard.
                // Compare with actual Windows Installer registration, not a hardcoded baseline.
                InstalledMsiGuard.Verify(productCode, expectedVersion);
            }
            finally
            {
                if (database != 0) MsiCloseHandle(database);
            }
        }
    }
}
