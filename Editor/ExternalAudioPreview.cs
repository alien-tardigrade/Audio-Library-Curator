using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace MooMoo.AudioLibraryCurator
{
    /// <summary>Loads an external file without adding it to Assets, then uses Unity's editor preview player.</summary>
    internal sealed class ExternalAudioPreview : IDisposable
    {
        readonly Action _changed;
        UnityWebRequest _request;
        AudioClip _clip;
        string _requestedPath;

        public ExternalAudioPreview(Action changed) => _changed = changed;

        public bool IsLoading => _request != null && !_request.isDone;
        public bool IsPlaying => AudioPreviewBridge.IsPlaying();
        public string LoadedPath { get; private set; }
        public string Error { get; private set; }
        public float Duration => _clip != null ? _clip.length : 0f;
        public float Position => _clip != null ? AudioPreviewBridge.Position() : 0f;
        public bool Loop { get; private set; }

        public void Toggle(string path)
        {
            if (!string.IsNullOrEmpty(LoadedPath) &&
                string.Equals(LoadedPath, path, StringComparison.OrdinalIgnoreCase) && IsPlaying)
            {
                Stop();
                return;
            }

            Play(path);
        }

        public void Play(string path)
        {
            StopAndRelease();
            Error = string.Empty;
            LoadedPath = string.Empty;

            if (!File.Exists(path))
            {
                Error = "Source file not found.";
                _changed?.Invoke();
                return;
            }

            _requestedPath = path;
            try
            {
                _request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioTypeFor(path));
                _request.SendWebRequest();
                EditorApplication.update += Poll;
            }
            catch (Exception exception)
            {
                Error = exception.Message;
                StopAndRelease();
            }

            _changed?.Invoke();
        }

        public void Stop()
        {
            AudioPreviewBridge.Stop();
            _changed?.Invoke();
        }

        public void Dispose() => StopAndRelease();

        void Poll()
        {
            if (_request == null || !_request.isDone) return;
            EditorApplication.update -= Poll;

            if (_request.result != UnityWebRequest.Result.Success)
            {
                Error = $"Preview load failed: {_request.error}";
                _request.Dispose();
                _request = null;
                _changed?.Invoke();
                return;
            }

            try
            {
                _clip = DownloadHandlerAudioClip.GetContent(_request);
                if (_clip == null) throw new InvalidOperationException("Unity did not create an AudioClip for this file.");
                _clip.hideFlags = HideFlags.HideAndDontSave;
                _clip.name = $"Curator preview — {Path.GetFileName(_requestedPath)}";
                if (!AudioPreviewBridge.Play(_clip, out var error))
                    throw new InvalidOperationException(error);
                LoadedPath = _requestedPath;
                AudioPreviewBridge.SetLoop(Loop);
                EditorApplication.update += Tick;
            }
            catch (Exception exception)
            {
                Error = exception.Message;
                if (_clip != null) UnityEngine.Object.DestroyImmediate(_clip);
                _clip = null;
            }
            finally
            {
                _request.Dispose();
                _request = null;
                _changed?.Invoke();
            }
        }

        void StopAndRelease()
        {
            EditorApplication.update -= Poll;
            EditorApplication.update -= Tick;
            AudioPreviewBridge.Stop();
            _request?.Abort();
            _request?.Dispose();
            _request = null;
            if (_clip != null) UnityEngine.Object.DestroyImmediate(_clip);
            _clip = null;
            _requestedPath = string.Empty;
        }

        public void SetLoop(bool loop)
        {
            Loop = loop;
            if (_clip != null) AudioPreviewBridge.SetLoop(loop);
            _changed?.Invoke();
        }

        public void Seek(float seconds)
        {
            if (_clip == null) return;
            var sample = Mathf.RoundToInt(Mathf.Clamp(seconds, 0f, _clip.length) * _clip.frequency);
            AudioPreviewBridge.SetSamplePosition(_clip, sample);
            _changed?.Invoke();
        }

        void Tick()
        {
            _changed?.Invoke();
            if (IsPlaying) return;
            EditorApplication.update -= Tick;
        }

        static AudioType AudioTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => AudioType.WAV,
            ".aif" => AudioType.AIFF,
            ".aiff" => AudioType.AIFF,
            ".mp3" => AudioType.MPEG,
            ".ogg" => AudioType.OGGVORBIS,
            _ => AudioType.UNKNOWN,
        };
    }

    /// <summary>
    /// AudioUtil is editor-internal and has changed method names across Unity releases. Keep the
    /// reflection isolated here so a future Unity upgrade has one compatibility point.
    /// </summary>
    internal static class AudioPreviewBridge
    {
        static readonly Type AudioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");

        internal static bool IsAvailable => AudioUtilType != null &&
            (Find("PlayPreviewClip", typeof(AudioClip), typeof(int), typeof(bool)) != null ||
             Find("PlayClip", typeof(AudioClip), typeof(int), typeof(bool)) != null);

        public static bool Play(AudioClip clip, out string error)
        {
            error = string.Empty;
            if (AudioUtilType == null)
            {
                error = "UnityEditor.AudioUtil is unavailable in this Unity version.";
                return false;
            }

            Stop();
            var method = Find("PlayPreviewClip", typeof(AudioClip), typeof(int), typeof(bool))
                         ?? Find("PlayClip", typeof(AudioClip), typeof(int), typeof(bool));
            if (method == null)
            {
                error = "Unity's editor audio-preview API changed; update AudioPreviewBridge.";
                return false;
            }

            try
            {
                method.Invoke(null, new object[] { clip, 0, false });
                return true;
            }
            catch (TargetInvocationException exception)
            {
                error = exception.InnerException?.Message ?? exception.Message;
                return false;
            }
        }

        public static void Stop()
        {
            var method = Find("StopAllPreviewClips") ?? Find("StopAllClips");
            try { method?.Invoke(null, null); }
            catch (Exception) { /* Preview failure must never interrupt editor work. */ }
        }

        public static bool IsPlaying()
        {
            var method = Find("IsPreviewClipPlaying") ?? Find("IsClipPlaying");
            try { return method != null && (bool)method.Invoke(null, null); }
            catch (Exception) { return false; }
        }

        public static float Position()
        {
            var method = Find("GetPreviewClipPosition");
            try { return method != null ? (float)method.Invoke(null, null) : 0f; }
            catch (Exception) { return 0f; }
        }

        public static void SetSamplePosition(AudioClip clip, int sample)
        {
            var method = Find("SetPreviewClipSamplePosition", typeof(AudioClip), typeof(int));
            try { method?.Invoke(null, new object[] { clip, sample }); }
            catch (Exception) { /* Scrubbing is optional; playback remains usable. */ }
        }

        public static void SetLoop(bool loop)
        {
            var method = Find("LoopPreviewClip", typeof(bool));
            try { method?.Invoke(null, new object[] { loop }); }
            catch (Exception) { /* Looping is optional; playback remains usable. */ }
        }

        static MethodInfo Find(string name, params Type[] parameters) =>
            AudioUtilType?.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, parameters ?? Array.Empty<Type>(), null);
    }
}
