using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MooMoo.AudioLibraryCurator
{
    internal enum AudioReviewFilter
    {
        All,
        Shortlisted,
        Unreviewed,
        Imported,
    }

    /// <summary>Fast external-library audition, review, and selective ingest for any Unity project.</summary>
    internal sealed class AudioLibraryCuratorWindow : EditorWindow
    {
        static readonly string[] FilterLabels = { "All", "Shortlisted", "Unreviewed", "Imported" };
        static readonly string[] RatingLabels = { "—", "1", "2", "3", "4", "5" };

        AudioLibraryRegistry _registry;
        AudioReviewStore _reviews;
        ExternalAudioPreview _preview;
        List<AudioLibraryItem> _items = new();
        Vector2 _listScroll;
        Vector2 _detailsScroll;
        int _libraryIndex;
        string _selectedRelativePath;
        string _search = string.Empty;
        AudioReviewFilter _filter;
        bool _autoPreview = true;
        string _status = string.Empty;

        [MenuItem("Tools/Audio/Library Curator")]
        public static void Open()
        {
            var window = GetWindow<AudioLibraryCuratorWindow>();
            window.titleContent = new GUIContent("Audio Curator");
            window.minSize = new Vector2(820f, 480f);
            window.Show();
        }

        void OnEnable()
        {
            _registry = AudioLibraryRegistry.Load();
            _reviews = AudioReviewStore.Load();
            _preview = new ExternalAudioPreview(Repaint);
            _libraryIndex = Mathf.Clamp(_libraryIndex, 0, Mathf.Max(0, _registry.Libraries.Count - 1));
            Refresh();
        }

        void OnDisable()
        {
            _preview?.Dispose();
            _reviews?.Save();
        }

        void OnGUI()
        {
            DrawLibraryBar();
            if (_registry.Libraries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Register a purchased audio-library folder. The masters stay outside Assets and the path is shared across your Unity projects on this machine.",
                    MessageType.Info);
                if (GUILayout.Button("Add Library Folder…", GUILayout.Height(32f))) AddLibrary();
                return;
            }

            var library = CurrentLibrary;
            if (library == null) return;
            if (!Directory.Exists(library.rootPath))
                EditorGUILayout.HelpBox($"Library folder is unavailable: {library.rootPath}", MessageType.Error);

            DrawFilterBar();

            var visible = VisibleItems(library).ToList();
            EditorGUILayout.BeginHorizontal();
            DrawList(library, visible);
            DrawDetails(library, visible);
            EditorGUILayout.EndHorizontal();

            DrawFooter(library, visible);
            HandleKeyboard(library, visible);
        }

        AudioLibraryDefinition CurrentLibrary =>
            _registry.Libraries.Count == 0 ? null : _registry.Libraries[Mathf.Clamp(_libraryIndex, 0, _registry.Libraries.Count - 1)];

        void DrawLibraryBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (_registry.Libraries.Count > 0)
            {
                var names = _registry.Libraries.Select(library => library.name).ToArray();
                var next = EditorGUILayout.Popup(_libraryIndex, names, EditorStyles.toolbarPopup, GUILayout.MinWidth(180f));
                if (next != _libraryIndex)
                {
                    _libraryIndex = next;
                    _selectedRelativePath = null;
                    Refresh();
                }
            }
            else GUILayout.Label("No libraries registered", EditorStyles.miniLabel);

            if (GUILayout.Button("Add…", EditorStyles.toolbarButton, GUILayout.Width(50f))) AddLibrary();
            using (new EditorGUI.DisabledScope(CurrentLibrary == null))
            {
                if (GUILayout.Button("Remove", EditorStyles.toolbarButton, GUILayout.Width(60f))) RemoveLibrary();
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60f))) Refresh();
                if (GUILayout.Button("Reveal", EditorStyles.toolbarButton, GUILayout.Width(52f)))
                    EditorUtility.RevealInFinder(CurrentLibrary.rootPath);
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{_items.Count} audio files", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        void DrawFilterBar()
        {
            EditorGUILayout.BeginHorizontal();
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(220f));
            _filter = (AudioReviewFilter)GUILayout.Toolbar((int)_filter, FilterLabels, GUILayout.Width(360f));
            _autoPreview = GUILayout.Toggle(_autoPreview, "Auto-play selection", GUILayout.Width(135f));
            EditorGUILayout.EndHorizontal();
        }

        void DrawList(AudioLibraryDefinition library, IReadOnlyList<AudioLibraryItem> visible)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.55f));
            EditorGUILayout.LabelField($"Results ({visible.Count})", EditorStyles.boldLabel);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            foreach (var item in visible)
            {
                var review = _reviews.Get(library.id, item.RelativePath);
                var selected = string.Equals(_selectedRelativePath, item.RelativePath, StringComparison.OrdinalIgnoreCase);
                var rowStyle = selected ? "SelectionRect" : "Label";

                EditorGUILayout.BeginHorizontal(selected ? EditorStyles.helpBox : GUIStyle.none);
                var shortlist = GUILayout.Toggle(review.shortlisted, review.shortlisted ? "★" : "☆", "Button", GUILayout.Width(28f));
                if (shortlist != review.shortlisted)
                {
                    review.shortlisted = shortlist;
                    _reviews.Save();
                }

                var display = CleanDisplayName(item.FileName);
                if (GUILayout.Button(display, rowStyle, GUILayout.ExpandWidth(true))) Select(item);
                GUILayout.Label(string.IsNullOrEmpty(item.Duration) ? "" : item.Duration, GUILayout.Width(42f));
                GUILayout.Label(review.rating > 0 ? $"{review.rating}★" : "", GUILayout.Width(30f));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawDetails(AudioLibraryDefinition library, IReadOnlyList<AudioLibraryItem> visible)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            _detailsScroll = EditorGUILayout.BeginScrollView(_detailsScroll);
            var item = SelectedItem(visible);
            if (item == null)
            {
                EditorGUILayout.HelpBox("Select a sound. Space plays or stops; ↑/↓ changes selection; S toggles the shortlist; I imports.", MessageType.None);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                return;
            }

            var review = _reviews.Get(library.id, item.RelativePath);
            EditorGUILayout.LabelField(CleanDisplayName(item.FileName), EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(item.RelativePath, EditorStyles.miniLabel, GUILayout.Height(34f));

            EditorGUILayout.BeginHorizontal();
            var previewingThis = string.Equals(_preview.LoadedPath, item.FullPath, StringComparison.OrdinalIgnoreCase);
            var playLabel = _preview.IsLoading ? "Loading…" : previewingThis && _preview.IsPlaying ? "Stop" : "Play";
            using (new EditorGUI.DisabledScope(_preview.IsLoading))
                if (GUILayout.Button(playLabel, GUILayout.Height(30f))) _preview.Toggle(item.FullPath);
            var loop = GUILayout.Toggle(_preview.Loop, "Loop", "Button", GUILayout.Width(54f), GUILayout.Height(30f));
            if (loop != _preview.Loop) _preview.SetLoop(loop);
            if (GUILayout.Button("Reveal Source", GUILayout.Height(30f))) EditorUtility.RevealInFinder(item.FullPath);
            EditorGUILayout.EndHorizontal();

            if (previewingThis && _preview.Duration > 0f)
            {
                EditorGUI.BeginChangeCheck();
                var position = EditorGUILayout.Slider(
                    $"{FormatTime(_preview.Position)} / {FormatTime(_preview.Duration)}",
                    _preview.Position, 0f, _preview.Duration);
                if (EditorGUI.EndChangeCheck()) _preview.Seek(position);
            }

            if (!string.IsNullOrWhiteSpace(_preview.Error))
                EditorGUILayout.HelpBox(_preview.Error, MessageType.Warning);

            EditorGUILayout.Space(6f);
            DrawMetadata("Duration", item.Duration);
            DrawMetadata("Format", item.Format);
            DrawMetadata("Channels", item.Channels);
            DrawMetadata("Originator", item.Originator);
            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                EditorGUILayout.LabelField("Library description", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox(item.Description, MessageType.None);
            }

            EditorGUILayout.Space(8f);
            EditorGUI.BeginChangeCheck();
            review.shortlisted = EditorGUILayout.Toggle("Shortlisted", review.shortlisted);
            review.rating = GUILayout.Toolbar(Mathf.Clamp(review.rating, 0, 5), RatingLabels);
            review.tags = EditorGUILayout.TextField("Project tags", review.tags ?? string.Empty);
            EditorGUILayout.LabelField("Decision notes", EditorStyles.miniBoldLabel);
            review.notes = EditorGUILayout.TextArea(review.notes ?? string.Empty, GUILayout.MinHeight(68f));
            if (EditorGUI.EndChangeCheck()) _reviews.Save();

            EditorGUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(!AudioPath.IsValidAssetDestination(_reviews.DestinationRoot)))
            {
                if (GUILayout.Button("Import This Sound", GUILayout.Height(34f))) Import(library, item, review);
            }
            if (!string.IsNullOrWhiteSpace(review.importedAssetPath))
            {
                EditorGUILayout.LabelField("Last imported asset", EditorStyles.miniBoldLabel);
                if (GUILayout.Button(review.importedAssetPath, EditorStyles.linkLabel))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(review.importedAssetPath);
                    if (asset != null) EditorGUIUtility.PingObject(asset);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawFooter(AudioLibraryDefinition library, IReadOnlyList<AudioLibraryItem> visible)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            _reviews.DestinationRoot = EditorGUILayout.TextField("Import root", _reviews.DestinationRoot);
            if (EditorGUI.EndChangeCheck()) _reviews.Save();
            if (GUILayout.Button("Choose…", GUILayout.Width(72f))) ChooseDestination();
            EditorGUILayout.EndHorizontal();

            if (!AudioPath.IsValidAssetDestination(_reviews.DestinationRoot))
                EditorGUILayout.HelpBox("Import root must be Assets or a folder beneath Assets.", MessageType.Error);

            EditorGUILayout.BeginHorizontal();
            var shortlisted = _items.Count(item => _reviews.Get(library.id, item.RelativePath).shortlisted);
            GUILayout.Label("Only explicitly imported files are copied; source libraries and review notes remain outside the build.", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(shortlisted == 0 || !AudioPath.IsValidAssetDestination(_reviews.DestinationRoot)))
                if (GUILayout.Button($"Import Shortlist ({shortlisted})", GUILayout.Width(150f))) ImportShortlist(library);
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        IEnumerable<AudioLibraryItem> VisibleItems(AudioLibraryDefinition library)
        {
            foreach (var item in _items)
            {
                var review = _reviews.Get(library.id, item.RelativePath);
                if (!string.IsNullOrWhiteSpace(_search))
                {
                    var searchable = $"{item.SearchText} {review.tags} {review.notes}";
                    if (searchable.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                }

                if (_filter == AudioReviewFilter.Shortlisted && !review.shortlisted) continue;
                if (_filter == AudioReviewFilter.Unreviewed && (review.rating > 0 || review.shortlisted || !string.IsNullOrWhiteSpace(review.notes))) continue;
                if (_filter == AudioReviewFilter.Imported && string.IsNullOrWhiteSpace(review.importedAssetPath)) continue;
                yield return item;
            }
        }

        void AddLibrary()
        {
            var folder = EditorUtility.OpenFolderPanel("Choose purchased audio library", string.Empty, string.Empty);
            if (string.IsNullOrWhiteSpace(folder)) return;
            var added = _registry.Add(folder);
            _libraryIndex = _registry.Libraries.ToList().FindIndex(library => library.id == added.id);
            _selectedRelativePath = null;
            Refresh();
        }

        void RemoveLibrary()
        {
            var library = CurrentLibrary;
            if (library == null) return;
            if (!EditorUtility.DisplayDialog("Remove library registration?",
                    $"Remove {library.name} from the curator? No source or imported audio will be deleted.", "Remove", "Cancel")) return;
            _registry.Remove(library);
            _libraryIndex = Mathf.Clamp(_libraryIndex, 0, Mathf.Max(0, _registry.Libraries.Count - 1));
            _selectedRelativePath = null;
            Refresh();
        }

        void Refresh()
        {
            _preview?.Stop();
            _items = CurrentLibrary == null ? new List<AudioLibraryItem>() : AudioLibraryScanner.Scan(CurrentLibrary);
            if (_selectedRelativePath != null && _items.All(item => item.RelativePath != _selectedRelativePath))
                _selectedRelativePath = null;
            _status = string.Empty;
            Repaint();
        }

        void Select(AudioLibraryItem item)
        {
            _selectedRelativePath = item.RelativePath;
            _status = string.Empty;
            if (_autoPreview) _preview.Play(item.FullPath);
            Repaint();
        }

        AudioLibraryItem SelectedItem(IReadOnlyList<AudioLibraryItem> visible) =>
            visible.FirstOrDefault(item => string.Equals(item.RelativePath, _selectedRelativePath, StringComparison.OrdinalIgnoreCase));

        void Import(AudioLibraryDefinition library, AudioLibraryItem item, AudioReviewRecord review)
        {
            try
            {
                var result = AudioIngest.CopyIntoProject(library, item, _reviews.DestinationRoot);
                _status = result.Message;
                if (!result.Succeeded) return;
                review.importedAssetPath = result.AssetPath;
                _reviews.Save();
                var asset = AssetDatabase.LoadAssetAtPath<AudioClip>(result.AssetPath);
                if (asset != null) EditorGUIUtility.PingObject(asset);
            }
            catch (Exception exception)
            {
                _status = $"Import failed: {exception.Message}";
                Debug.LogException(exception);
            }
        }

        void ImportShortlist(AudioLibraryDefinition library)
        {
            var selected = _items.Where(item => _reviews.Get(library.id, item.RelativePath).shortlisted).ToList();
            var imported = 0;
            try
            {
                for (var index = 0; index < selected.Count; index++)
                {
                    var item = selected[index];
                    EditorUtility.DisplayProgressBar("Importing audio shortlist", item.FileName, (float)index / selected.Count);
                    var review = _reviews.Get(library.id, item.RelativePath);
                    var result = AudioIngest.CopyIntoProject(library, item, _reviews.DestinationRoot);
                    if (!result.Succeeded) continue;
                    review.importedAssetPath = result.AssetPath;
                    imported++;
                }

                _reviews.Save();
                AssetDatabase.Refresh();
                _status = $"Imported {imported} of {selected.Count} shortlisted sounds.";
            }
            catch (Exception exception)
            {
                _status = $"Shortlist import stopped: {exception.Message}";
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        void ChooseDestination()
        {
            var absoluteAssets = Application.dataPath;
            var chosen = EditorUtility.OpenFolderPanel("Choose Unity import folder", absoluteAssets, string.Empty);
            if (string.IsNullOrWhiteSpace(chosen)) return;

            var normalized = AudioPath.NormalizeFullPath(chosen);
            var projectRoot = AudioPath.NormalizeFullPath(Path.GetDirectoryName(Application.dataPath));
            var relative = AudioPath.RelativeTo(projectRoot, normalized);
            if (!AudioPath.IsValidAssetDestination(relative))
            {
                EditorUtility.DisplayDialog("Folder is outside Assets", "Select Assets or a folder beneath it.", "OK");
                return;
            }

            _reviews.DestinationRoot = relative;
            _reviews.Save();
        }

        void HandleKeyboard(AudioLibraryDefinition library, IReadOnlyList<AudioLibraryItem> visible)
        {
            if (EditorGUIUtility.editingTextField || visible.Count == 0) return;
            var current = SelectedItem(visible);
            var currentIndex = current == null ? -1 : visible.ToList().IndexOf(current);
            var keyboard = Event.current;
            if (keyboard.type != EventType.KeyDown) return;

            if (keyboard.keyCode == KeyCode.DownArrow || keyboard.keyCode == KeyCode.UpArrow)
            {
                var delta = keyboard.keyCode == KeyCode.DownArrow ? 1 : -1;
                var next = Mathf.Clamp(currentIndex + delta, 0, visible.Count - 1);
                Select(visible[next]);
                keyboard.Use();
            }
            else if (keyboard.keyCode == KeyCode.Space && current != null)
            {
                _preview.Toggle(current.FullPath);
                keyboard.Use();
            }
            else if (keyboard.keyCode == KeyCode.S && current != null)
            {
                var review = _reviews.Get(library.id, current.RelativePath);
                review.shortlisted = !review.shortlisted;
                _reviews.Save();
                keyboard.Use();
            }
            else if (keyboard.keyCode == KeyCode.I && current != null)
            {
                Import(library, current, _reviews.Get(library.id, current.RelativePath));
                keyboard.Use();
            }
        }

        static void DrawMetadata(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) EditorGUILayout.LabelField(label, value);
        }

        static string CleanDisplayName(string fileName)
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            return stem.Replace('_', ' ');
        }

        static string FormatTime(float seconds)
        {
            var span = TimeSpan.FromSeconds(Mathf.Max(0f, seconds));
            return span.TotalHours >= 1d ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span.Seconds:00}";
        }
    }
}
