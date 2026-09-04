using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Gamekit3D.Experiment
{
    public static class ExperimentDataExporter
    {
        public static string Export(ExperimentSession session)
        {
            string json = JsonUtility.ToJson(session, true);
            string fileName = string.Format("participant_{0}_{1:yyyyMMdd_HHmmss}.json",
                Sanitize(session.participant_id), DateTime.Now);

            string path = TryWrite(GetDownloadsFolder(), fileName, json);
            if (path != null) return path;

            return TryWrite(Path.Combine(Application.persistentDataPath, "ExportedData"), fileName, json);
        }

        // The user's Downloads folder — easy to find on both macOS and Windows without needing a
        // native folder-picker dialog (which Unity doesn't provide out of the box in a build).
        static string GetDownloadsFolder()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Downloads", "ExportedData");
        }

        static string TryWrite(string folder, string fileName, string json)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string fullPath = Path.Combine(folder, fileName);
                File.WriteAllText(fullPath, json);
                return fullPath;
            }
            catch (Exception e)
            {
                Debug.LogWarning("ExperimentDataExporter: échec de l'écriture dans " + folder + " — " + e.Message);
                return null;
            }
        }

        static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
