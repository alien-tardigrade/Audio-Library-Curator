using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MooMoo.AudioLibraryCurator
{
    [Serializable]
    internal sealed class AudioLibraryDefinition
    {
        public string id;
        public string name;
        public string rootPath;
    }

    [Serializable]
    internal sealed class AudioLibraryRegistryData
    {
        public List<AudioLibraryDefinition> libraries = new();
    }

    /// <summary>
    /// Machine-local roots shared by every Unity project. Purchased masters remain outside Assets;
    /// projects only remember a stable library id and paths relative to its registered root.
    /// </summary>
    internal sealed class AudioLibraryRegistry
    {
        const string EditorPrefsKey = "MooMoo.AudioLibraryCurator.Libraries.v1";

        readonly AudioLibraryRegistryData _data;

        AudioLibraryRegistry(AudioLibraryRegistryData data) => _data = data;

        public IReadOnlyList<AudioLibraryDefinition> Libraries => _data.libraries;

        public static AudioLibraryRegistry Load()
        {
            var json = EditorPrefs.GetString(EditorPrefsKey, string.Empty);
            var data = string.IsNullOrWhiteSpace(json)
                ? new AudioLibraryRegistryData()
                : JsonUtility.FromJson<AudioLibraryRegistryData>(json) ?? new AudioLibraryRegistryData();
            data.libraries ??= new List<AudioLibraryDefinition>();
            data.libraries.RemoveAll(library => library == null || string.IsNullOrWhiteSpace(library.rootPath));
            return new AudioLibraryRegistry(data);
        }

        public AudioLibraryDefinition Add(string rootPath)
        {
            var normalized = AudioPath.NormalizeFullPath(rootPath);
            var existing = _data.libraries.FirstOrDefault(library =>
                string.Equals(AudioPath.NormalizeFullPath(library.rootPath), normalized, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            var definition = new AudioLibraryDefinition
            {
                id = Guid.NewGuid().ToString("N"),
                name = new DirectoryInfo(normalized).Name,
                rootPath = normalized,
            };
            _data.libraries.Add(definition);
            Save();
            return definition;
        }

        public void Remove(AudioLibraryDefinition definition)
        {
            if (definition == null) return;
            _data.libraries.RemoveAll(library => library.id == definition.id);
            Save();
        }

        void Save() => EditorPrefs.SetString(EditorPrefsKey, JsonUtility.ToJson(_data));
    }

    [Serializable]
    internal sealed class AudioReviewRecord
    {
        public string libraryId;
        public string relativePath;
        public int rating;
        public bool shortlisted;
        public string tags;
        public string notes;
        public string importedAssetPath;
    }

    [Serializable]
    internal sealed class AudioProjectReviewData
    {
        public string destinationRoot = "Assets/Audio/Curated";
        public List<AudioReviewRecord> reviews = new();
    }

    /// <summary>Per-project, per-user decisions. UserSettings is intentionally not shipped.</summary>
    internal sealed class AudioReviewStore
    {
        public const string RelativeStorePath = "UserSettings/AudioLibraryCurator.json";

        readonly AudioProjectReviewData _data;

        AudioReviewStore(AudioProjectReviewData data) => _data = data;

        public string DestinationRoot
        {
            get => string.IsNullOrWhiteSpace(_data.destinationRoot) ? "Assets/Audio/Curated" : _data.destinationRoot;
            set => _data.destinationRoot = value;
        }

        public IEnumerable<AudioReviewRecord> Reviews => _data.reviews;

        public static AudioReviewStore Load()
        {
            AudioProjectReviewData data = null;
            if (File.Exists(RelativeStorePath))
            {
                try { data = JsonUtility.FromJson<AudioProjectReviewData>(File.ReadAllText(RelativeStorePath)); }
                catch (Exception exception) { Debug.LogWarning($"Audio Library Curator could not read its review file: {exception.Message}"); }
            }

            data ??= new AudioProjectReviewData();
            data.reviews ??= new List<AudioReviewRecord>();
            return new AudioReviewStore(data);
        }

        public AudioReviewRecord Get(string libraryId, string relativePath)
        {
            relativePath = AudioPath.NormalizeRelativePath(relativePath);
            var review = _data.reviews.FirstOrDefault(candidate =>
                candidate.libraryId == libraryId &&
                string.Equals(candidate.relativePath, relativePath, StringComparison.OrdinalIgnoreCase));
            if (review != null) return review;

            review = new AudioReviewRecord
            {
                libraryId = libraryId,
                relativePath = relativePath,
                tags = string.Empty,
                notes = string.Empty,
                importedAssetPath = string.Empty,
            };
            _data.reviews.Add(review);
            return review;
        }

        public void Save()
        {
            var directory = Path.GetDirectoryName(RelativeStorePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(RelativeStorePath, JsonUtility.ToJson(_data, true));
        }
    }

    internal sealed class AudioLibraryItem
    {
        public string FullPath;
        public string RelativePath;
        public string FileName;
        public string Duration;
        public string Format;
        public string Channels;
        public string Description;
        public string Originator;

        public string SearchText => $"{RelativePath} {Description} {Originator}";
    }

    internal sealed class AudioMetadataRecord
    {
        public string FileName;
        public string Duration;
        public string Format;
        public string Channels;
        public string Description;
        public string Originator;
    }

    internal static class AudioLibraryScanner
    {
        static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".wav", ".aif", ".aiff", ".mp3", ".ogg", ".flac",
        };

        public static bool IsSupportedAudio(string path) =>
            !string.IsNullOrWhiteSpace(path) && SupportedExtensions.Contains(Path.GetExtension(path));

        public static List<AudioLibraryItem> Scan(AudioLibraryDefinition library)
        {
            var result = new List<AudioLibraryItem>();
            if (library == null || !Directory.Exists(library.rootPath)) return result;

            var metadata = LoadMetadata(library.rootPath);
            foreach (var fullPath in Directory.EnumerateFiles(library.rootPath, "*", SearchOption.AllDirectories))
            {
                if (!IsSupportedAudio(fullPath)) continue;

                var fileName = Path.GetFileName(fullPath);
                metadata.TryGetValue(fileName, out var record);
                result.Add(new AudioLibraryItem
                {
                    FullPath = AudioPath.NormalizeFullPath(fullPath),
                    RelativePath = AudioPath.RelativeTo(library.rootPath, fullPath),
                    FileName = fileName,
                    Duration = record?.Duration ?? string.Empty,
                    Format = record?.Format ?? string.Empty,
                    Channels = record?.Channels ?? string.Empty,
                    Description = record?.Description ?? string.Empty,
                    Originator = record?.Originator ?? string.Empty,
                });
            }

            return result.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static Dictionary<string, AudioMetadataRecord> ParseMetadata(string text)
        {
            var result = new Dictionary<string, AudioMetadataRecord>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text)) return result;

            var rows = DelimitedText.Parse(text, ';');
            if (rows.Count == 0) return result;

            var header = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < rows[0].Count; i++) header[rows[0][i].Trim()] = i;

            for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                var fileName = Cell(row, header, "FileName");
                if (string.IsNullOrWhiteSpace(fileName)) continue;
                result[fileName] = new AudioMetadataRecord
                {
                    FileName = fileName,
                    Duration = Cell(row, header, "Time"),
                    Format = Cell(row, header, "Format"),
                    Channels = Cell(row, header, "Channels"),
                    Description = Cell(row, header, "Description"),
                    Originator = Cell(row, header, "Originator"),
                };
            }

            return result;
        }

        static Dictionary<string, AudioMetadataRecord> LoadMetadata(string rootPath)
        {
            try
            {
                var metadataPath = Directory.EnumerateFiles(rootPath, "Metadata.csv", SearchOption.AllDirectories)
                    .FirstOrDefault();
                return metadataPath == null
                    ? new Dictionary<string, AudioMetadataRecord>(StringComparer.OrdinalIgnoreCase)
                    : ParseMetadata(File.ReadAllText(metadataPath));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Audio Library Curator could not read metadata: {exception.Message}");
                return new Dictionary<string, AudioMetadataRecord>(StringComparer.OrdinalIgnoreCase);
            }
        }

        static string Cell(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> header, string name) =>
            header.TryGetValue(name, out var index) && index >= 0 && index < row.Count ? row[index].Trim() : string.Empty;
    }

    internal static class DelimitedText
    {
        public static List<List<string>> Parse(string text, char separator)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;

            for (var i = 0; i < text.Length; i++)
            {
                var character = text[i];
                if (character == '"')
                {
                    if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else quoted = !quoted;
                    continue;
                }

                if (!quoted && character == separator)
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    continue;
                }

                if (!quoted && (character == '\r' || character == '\n'))
                {
                    if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString());
                    cell.Clear();
                    if (row.Any(value => !string.IsNullOrEmpty(value))) rows.Add(row);
                    row = new List<string>();
                    continue;
                }

                cell.Append(character);
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                if (row.Any(value => !string.IsNullOrEmpty(value))) rows.Add(row);
            }

            return rows;
        }
    }

    internal static class AudioPath
    {
        public static string NormalizeFullPath(string path) =>
            Path.GetFullPath(path ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        public static string NormalizeRelativePath(string path) => (path ?? string.Empty).Replace('\\', '/').TrimStart('/');

        public static string RelativeTo(string rootPath, string fullPath) =>
            NormalizeRelativePath(Path.GetRelativePath(NormalizeFullPath(rootPath), NormalizeFullPath(fullPath)));

        public static bool IsValidAssetDestination(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var normalized = path.Replace('\\', '/').TrimEnd('/');
            if (!(normalized == "Assets" || normalized.StartsWith("Assets/", StringComparison.Ordinal))) return false;
            return normalized.Split('/').All(segment => segment != ".." && segment != ".");
        }

        public static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)) return false;
            return NormalizeRelativePath(path).Split('/').All(segment =>
                !string.IsNullOrWhiteSpace(segment) && segment != "." && segment != "..");
        }

        public static string SafeSegment(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string((value ?? string.Empty).Select(character =>
                invalid.Contains(character) || character == '/' || character == '\\' ? '_' : character).ToArray()).Trim();
            return string.IsNullOrEmpty(cleaned) ? "Library" : cleaned;
        }
    }

    [Serializable]
    internal sealed class AudioImportManifest
    {
        public List<AudioImportManifestEntry> entries = new();
    }

    [Serializable]
    internal sealed class AudioImportManifestEntry
    {
        public string sourceLibrary;
        public string sourceRelativePath;
        public string sourceSha256;
        public string destinationAssetPath;
        public string importedUtc;
    }

    internal readonly struct AudioImportResult
    {
        public AudioImportResult(bool succeeded, string assetPath, string message)
        {
            Succeeded = succeeded;
            AssetPath = assetPath;
            Message = message;
        }

        public bool Succeeded { get; }
        public string AssetPath { get; }
        public string Message { get; }
    }

    internal static class AudioIngest
    {
        const string ManifestName = "_AudioCuratorManifest.json";

        public static AudioImportResult CopyIntoProject(
            AudioLibraryDefinition library,
            AudioLibraryItem item,
            string destinationRoot)
        {
            if (library == null || item == null) return new AudioImportResult(false, string.Empty, "No library item selected.");
            if (!AudioPath.IsValidAssetDestination(destinationRoot))
                return new AudioImportResult(false, string.Empty, "Destination must be Assets or a folder beneath Assets.");
            if (!AudioPath.IsSafeRelativePath(item.RelativePath))
                return new AudioImportResult(false, string.Empty, "The library-relative source path is unsafe.");
            if (!File.Exists(item.FullPath))
                return new AudioImportResult(false, string.Empty, "The source file no longer exists.");

            var libraryFolder = AudioPath.SafeSegment(library.name);
            var relativeDirectory = Path.GetDirectoryName(item.RelativePath)?.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(relativeDirectory))
                relativeDirectory = string.Join("/", relativeDirectory.Split('/').Select(AudioPath.SafeSegment));
            var assetDirectory = string.IsNullOrWhiteSpace(relativeDirectory)
                ? $"{destinationRoot.TrimEnd('/')}/{libraryFolder}"
                : $"{destinationRoot.TrimEnd('/')}/{libraryFolder}/{relativeDirectory}";
            Directory.CreateDirectory(assetDirectory);

            var candidate = $"{assetDirectory}/{AudioPath.SafeSegment(Path.GetFileName(item.RelativePath))}".Replace('\\', '/');
            var sourceHash = Sha256(item.FullPath);
            var destination = UniqueOrMatchingPath(candidate, sourceHash);
            var alreadyPresent = File.Exists(destination) && Sha256(destination) == sourceHash;
            if (!alreadyPresent) File.Copy(item.FullPath, destination, false);

            WriteManifest(destinationRoot, library, item, sourceHash, destination);
            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset($"{destinationRoot.TrimEnd('/')}/{ManifestName}", ImportAssetOptions.ForceSynchronousImport);
            return new AudioImportResult(true, destination,
                alreadyPresent ? "Identical audio was already present; provenance refreshed." : "Copied and imported.");
        }

        internal static string UniqueOrMatchingPath(string candidate, string sourceHash)
        {
            if (!File.Exists(candidate) || Sha256(candidate) == sourceHash) return candidate;

            var directory = Path.GetDirectoryName(candidate) ?? string.Empty;
            var stem = Path.GetFileNameWithoutExtension(candidate);
            var extension = Path.GetExtension(candidate);
            for (var suffix = 2; suffix < 10000; suffix++)
            {
                var next = Path.Combine(directory, $"{stem}_{suffix}{extension}").Replace('\\', '/');
                if (!File.Exists(next) || Sha256(next) == sourceHash) return next;
            }

            throw new IOException($"Could not allocate a unique destination for {candidate}.");
        }

        internal static string Sha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var hash = SHA256.Create();
            return string.Concat(hash.ComputeHash(stream).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        static void WriteManifest(
            string destinationRoot,
            AudioLibraryDefinition library,
            AudioLibraryItem item,
            string sourceHash,
            string destination)
        {
            var path = $"{destinationRoot.TrimEnd('/')}/{ManifestName}";
            AudioImportManifest manifest = null;
            if (File.Exists(path))
            {
                try { manifest = JsonUtility.FromJson<AudioImportManifest>(File.ReadAllText(path)); }
                catch (Exception exception) { Debug.LogWarning($"Audio Library Curator could not read {path}: {exception.Message}"); }
            }

            manifest ??= new AudioImportManifest();
            manifest.entries ??= new List<AudioImportManifestEntry>();
            var entry = manifest.entries.FirstOrDefault(candidate => candidate.destinationAssetPath == destination);
            if (entry == null)
            {
                entry = new AudioImportManifestEntry();
                manifest.entries.Add(entry);
            }

            entry.sourceLibrary = library.name;
            entry.sourceRelativePath = item.RelativePath;
            entry.sourceSha256 = sourceHash;
            entry.destinationAssetPath = destination;
            entry.importedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            File.WriteAllText(path, JsonUtility.ToJson(manifest, true));
        }
    }
}
