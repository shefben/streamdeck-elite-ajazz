using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Elite
{
    /// <summary>Resolve the preset Elite names in StartPreset, independent of binding format version.</summary>
    public static class BindingResolver
    {
        public static string FindStartPreset(string directory)
        {
            if (!Directory.Exists(directory)) return null;
            // Elite rewrites the active marker when controls are selected. Older markers can
            // survive upgrades, so use the last-written marker rather than a fixed major version.
            return Directory.EnumerateFiles(directory, "StartPreset*.start")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        public static string Find(string directory, string preset)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || string.IsNullOrWhiteSpace(preset))
                return null;

            return Directory.EnumerateFiles(directory, "*.binds")
                .Select(path => ReadCandidate(path, preset.Trim()))
                .Where(candidate => candidate != null)
                .OrderByDescending(candidate => candidate.Version)
                .ThenByDescending(candidate => File.GetLastWriteTimeUtc(candidate.Path))
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .Select(candidate => candidate.Path).FirstOrDefault();
        }

        private sealed record Candidate(string Path, Version Version);

        private static Candidate ReadCandidate(string path, string preset)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
                var root = XElement.Load(reader);
                // Elite uses the internal PresetName; the filename can be Custom.4.x.binds.
                var name = (string)root.Attribute("PresetName");
                if (string.IsNullOrWhiteSpace(name)) name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!string.Equals(name, preset, StringComparison.OrdinalIgnoreCase)) return null;
                int.TryParse((string)root.Attribute("MajorVersion"), out var major);
                int.TryParse((string)root.Attribute("MinorVersion"), out var minor);
                return new Candidate(path, new Version(Math.Max(0, major), Math.Max(0, minor)));
            }
            catch (Exception ex) when (ex is IOException || ex is XmlException || ex is UnauthorizedAccessException)
            {
                return null; // One damaged or currently-being-written preset must not hide all the others.
            }
        }
    }
}
