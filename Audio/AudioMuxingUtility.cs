using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace CinematicRecorder.Audio
{
    public static class AudioMuxingUtility
    {
        public static void MuxAudioVideo(
            string videoPath,
            string audioPath,
            string ffmpegPath,
            Action<bool, string> onComplete)
        {
            // Validate inputs
            if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            {
                UnityEngine.Debug.LogError("[AudioMuxingUtility] Video file not found: " + videoPath);
                onComplete?.Invoke(false, "Video file not found");
                return;
            }

            if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
            {
                UnityEngine.Debug.LogError("[AudioMuxingUtility] Audio file not found: " + audioPath);
                onComplete?.Invoke(false, "Audio file not found");
                return;
            }

            if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                UnityEngine.Debug.LogError("[AudioMuxingUtility] FFmpeg not found: " + ffmpegPath);
                onComplete?.Invoke(false, "FFmpeg not found");
                return;
            }

            // Generate output path
            string outputDir = Path.GetDirectoryName(videoPath);
            string baseName = Path.GetFileNameWithoutExtension(videoPath);
            string muxedPath = Path.Combine(outputDir, $"{baseName}_muxed.mkv");

            // Normalize paths (use forward slashes for consistency)
            videoPath = videoPath.Replace('\\', '/');
            audioPath = audioPath.Replace('\\', '/');
            muxedPath = muxedPath.Replace('\\', '/');

            // Build FFmpeg arguments: copy video, AAC audio at 320kbps
            string args = $"-y -i \"{videoPath}\" -i \"{audioPath}\" -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 320k \"{muxedPath}\"";

            UnityEngine.Debug.Log($"[AudioMuxingUtility] Starting mux: {ffmpegPath} {args}");

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = args,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        WorkingDirectory = outputDir
                    },
                    EnableRaisingEvents = true
                };

                process.Exited += (sender, e) =>
                {
                    int exitCode = process.ExitCode;

                    string stderr = "";
                    try
                    {
                        stderr = process.StandardError.ReadToEnd();
                    }
                    catch { }

                    process.Dispose();

                    UnityEngine.Debug.Log($"[AudioMuxingUtility] FFmpeg exit code: {exitCode}");
                    if (!string.IsNullOrEmpty(stderr))
                    {
                        UnityEngine.Debug.Log($"[AudioMuxingUtility] FFmpeg stderr: {stderr}");
                    }

                    if (exitCode == 0)
                    {
                        UnityEngine.Debug.Log($"[AudioMuxingUtility] Muxing complete: {muxedPath}");
                        onComplete?.Invoke(true, muxedPath);
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"[AudioMuxingUtility] Muxing failed with exit code {exitCode}");
                        onComplete?.Invoke(false, $"Muxing failed (code {exitCode})");
                    }
                };

                process.Start();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[AudioMuxingUtility] Failed to start muxing: {ex.Message}");
                onComplete?.Invoke(false, "Failed to start muxing");
            }
        }
    }
}