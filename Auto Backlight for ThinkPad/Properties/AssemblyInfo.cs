using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("Auto Backlight for ThinkPad")]
[assembly: AssemblyDescription("Automatically controls ThinkPad keyboard backlighting based on user activity, with optional external input devices, hardware diagnostics and optional camera-based screen brightness. Community-maintained fork by prat89, based on the original application by Parth Patel.")]
[assembly: AssemblyCompany("prat89")]
[assembly: AssemblyProduct("Auto Backlight for ThinkPad")]
[assembly: AssemblyCopyright("Original © 2019 Parth Patel; fork contributions © 2026 prat89")]
[assembly: AssemblyMetadata("WebUrl", @"https://github.com/prat89/auto-backlight-for-thinkpad")]
[assembly: AssemblyMetadata("ReleasesUrl", @"https://github.com/prat89/auto-backlight-for-thinkpad/releases")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

//In order to begin building localizable applications, set
//<UICulture>CultureYouAreCodingWith</UICulture> in your .csproj file
//inside a <PropertyGroup>.  For example, if you are using US english
//in your source files, set the <UICulture> to en-US.  Then uncomment
//the NeutralResourceLanguage attribute below.  Update the "en-US" in
//the line below to match the UICulture setting in the project file.

//[assembly: NeutralResourcesLanguage("en-US", UltimateResourceFallbackLocation.Satellite)]


[assembly: ThemeInfo(
    ResourceDictionaryLocation.None, //where theme specific resource dictionaries are located
                                     //(used if a resource is not found in the page,
                                     // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly //where the generic resource dictionary is located
                                              //(used if a resource is not found in the page,
                                              // app, or any theme specific resource dictionaries)
)]


// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion("1.3.5.0")]
[assembly: AssemblyFileVersion("1.3.5.0")]
[assembly: AssemblyInformationalVersion("1.3.5")]
#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif
