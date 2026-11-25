using UnityEditor;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Verbose logging gate for the ShaderGen pipeline.
    ///
    /// Info-level logs fire on every import and every importer-inspector refresh,
    /// which gets noisy as more project shaders move onto the importer. They're off
    /// by default and toggled via Tools/ShaderProcessor/Verbose Logging.
    /// Warnings and errors are never gated - only routine progress messages go
    /// through here.
    /// </summary>
    public static class ShaderGenLog
    {
        const string MENU_PATH = "Tools/ShaderProcessor/Verbose Logging";
        const string PREF_KEY = "FS.ShaderGen.VerboseLogging";

        /// <summary>Whether verbose info logging is enabled (persisted per-user).</summary>
        public static bool Verbose
        {
            get => EditorPrefs.GetBool(PREF_KEY, false);
            set => EditorPrefs.SetBool(PREF_KEY, value);
        }

        /// <summary>Log an info message if verbose logging is enabled.</summary>
        public static void Info(string message)
        {
            if (Verbose)
                Debug.Log(message);
        }

        [MenuItem(MENU_PATH)]
        static void ToggleVerbose() => Verbose = !Verbose;

        [MenuItem(MENU_PATH, true)]
        static bool ToggleVerboseValidate()
        {
            Menu.SetChecked(MENU_PATH, Verbose);
            return true;
        }
    }
}
