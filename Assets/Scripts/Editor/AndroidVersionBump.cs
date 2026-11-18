using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Shaderpark.Scrabble.EditorTools
{
    /// <summary>
    /// Increments the Android version code before every Android build, so each new
    /// test APK installs as an update over the previous one instead of being refused
    /// as a same-or-older version. The user-facing version (PlayerSettings.bundleVersion,
    /// e.g. "1.1") is left alone -- change that by hand for real releases.
    /// </summary>
    public sealed class AndroidVersionBump : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            PlayerSettings.Android.bundleVersionCode++;
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("Android build: version " + PlayerSettings.bundleVersion +
                                  " (code " + PlayerSettings.Android.bundleVersionCode + ")");
        }
    }
}
