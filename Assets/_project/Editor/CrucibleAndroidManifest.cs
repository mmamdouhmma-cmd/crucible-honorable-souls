using System.IO;
using System.Xml;
using UnityEditor.Android;

/// <summary>
/// Adds what the in-game updater needs to the generated Android manifest: permission to ask the system
/// to install a package, and CrucibleApkProvider, which hands the downloaded APK to the installer.
/// Done here rather than in a custom Assets/Plugins/Android/AndroidManifest.xml so the rest of the
/// manifest stays Unity's own.
/// </summary>
public class CrucibleAndroidManifest : IPostGenerateGradleAndroidProject
{
    const string AndroidNs = "http://schemas.android.com/apk/res/android";

    public int callbackOrder => 0;

    public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
    {
        string path = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");
        var doc = new XmlDocument();
        doc.Load(path);
        var manifest = doc.DocumentElement;
        var application = manifest["application"];

        var permission = doc.CreateElement("uses-permission");
        permission.SetAttribute("name", AndroidNs, "android.permission.REQUEST_INSTALL_PACKAGES");
        manifest.InsertBefore(permission, application);

        var provider = doc.CreateElement("provider");
        provider.SetAttribute("name", AndroidNs, "com.honorablesouls.crucible.CrucibleApkProvider");
        provider.SetAttribute("authorities", AndroidNs, "${applicationId}.update");
        provider.SetAttribute("exported", AndroidNs, "false");
        provider.SetAttribute("grantUriPermissions", AndroidNs, "true");
        application.AppendChild(provider);

        doc.Save(path);
    }
}
