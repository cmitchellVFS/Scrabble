using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Shaderpark.Scrabble.EditorTools
{
    /// <summary>
    /// Versioning for Android builds. Before every Android build this:
    /// <list type="number">
    /// <item>increments the Android version code (so each test APK installs as an
    /// update over the previous one), and</item>
    /// <item>sets the version name to MAJOR.MINOR.BUILD, where BUILD is that version
    /// code -- e.g. 1.1.4. This is Application.version, shown on the start screen.</item>
    /// </list>
    /// MAJOR.MINOR is yours to set: change Player Settings > Version to e.g. "1.2"
    /// (or "1.2.0") for a significant release; the next build becomes 1.2.&lt;build&gt;.
    /// The build number keeps counting up across major/minor changes.
    /// </summary>
    public sealed class AndroidVersionBump : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            int build = PlayerSettings.Android.bundleVersionCode + 1;
            PlayerSettings.Android.bundleVersionCode = build;
            PlayerSettings.bundleVersion = MajorMinor(PlayerSettings.bundleVersion) + "." + build;
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("Android build: version " + PlayerSettings.bundleVersion + " (code " + build + ")");
        }

        /// <summary>First two parts of a version string: "1.1.3" -> "1.1", "2" -> "2.0".</summary>
        public static string MajorMinor(string version)
        {
            string[] parts = (version ?? "").Trim().Split('.');
            int major, minor;
            if (parts.Length < 1 || !int.TryParse(parts[0], out major)) major = 1;
            if (parts.Length < 2 || !int.TryParse(parts[1], out minor)) minor = 0;
            return major + "." + minor;
        }
    }
}
