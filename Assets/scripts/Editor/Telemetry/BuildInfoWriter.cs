using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Overpower.Telemetry;

namespace Overpower.EditorTools
{
    /// <summary>Bakes the current commit hash into a Resources text asset before every build, so a
    /// Player - which ships with no .git folder to read from - can still stamp its telemetry session
    /// line with the commit it was built from. MatchTelemetry.ReadCommitHash reads this exact file
    /// back at runtime via Resources.Load; GitCommitReader (Assets/scripts/Telemetry, shared with the
    /// Editor path) is the one place that actually parses .git.
    ///
    /// The output file is a build artifact, not source - it goes stale the moment someone pulls
    /// without rebuilding, so it is gitignored (see .gitignore's own comment next to the two lines
    /// this file's output added).</summary>
    public class BuildInfoWriter : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public const string OutputPath = "Assets/Resources/BuildInfo.txt";

        public void OnPreprocessBuild(BuildReport report) => Write();

        /// <summary>Public and static so the T2 verification step (and any future automated build
        /// pipeline step) can call this directly without running a full Player build.</summary>
        public static void Write()
        {
            string hash = GitCommitReader.ReadShortHash(10);

            string directory = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(OutputPath, hash);
            AssetDatabase.ImportAsset(OutputPath);
        }
    }
}
