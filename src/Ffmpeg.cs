using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Hd2Qd.HD2Music
{
    /// <summary>The decoder the player spawns. It travels in the DLL (assets\ffmpeg.exe.gz,
    /// FFmpeg 6.1.1 essentials) and is written out once, next to the plugin or into the
    /// player's settings folder, when neither place has one. The extracted file is checked
    /// against the SHA-256 of the build that was packed.</summary>
    internal static class Ffmpeg
    {
        internal const string Resource = "ffmpeg.exe.gz";
        internal const string Sha256 = "04e1307997530f9cf2fe35cba2ca7e8875ca91da02f89d6c7243df819c94ad00";
        const long MinimumLength = 50L * 1024 * 1024;

        static readonly object Gate = new object();
        static string _ready;

        /// <summary>The decoder to use, extracting the embedded one if the usual places have
        /// none. Null when the file could not be written anywhere.</summary>
        internal static string Ensure()
        {
            lock (Gate)
            {
                if (!string.IsNullOrEmpty(_ready) && File.Exists(_ready))
                {
                    return _ready;
                }
                foreach (string candidate in Candidates())
                {
                    if (File.Exists(candidate))
                    {
                        _ready = candidate;
                        return candidate;
                    }
                }
                foreach (string target in Targets())
                {
                    try
                    {
                        ExtractTo(target);
                        _ready = target;
                        return target;
                    }
                    catch
                    {
                    }
                }
                return null;
            }
        }

        internal static void ExtractTo(string target)
        {
            string folder = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }
            string temporary = target + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                using (Stream packed = typeof(Ffmpeg).Assembly.GetManifestResourceStream(Resource))
                {
                    if (packed == null)
                    {
                        throw new InvalidOperationException("ffmpeg.exe.gz is not embedded");
                    }
                    using (var gzip = new GZipStream(packed, CompressionMode.Decompress))
                    using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        gzip.CopyTo(file);
                    }
                }
                var info = new FileInfo(temporary);
                if (info.Length < MinimumLength || !HashMatches(temporary))
                {
                    throw new InvalidDataException("the extracted ffmpeg.exe does not match the packed build");
                }
                if (File.Exists(target))
                {
                    File.Delete(temporary);
                    return;
                }
                File.Move(temporary, target);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch
                {
                }
            }
        }

        static bool HashMatches(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                return string.Equals(BitConverter.ToString(hash).Replace("-", ""), Sha256, StringComparison.OrdinalIgnoreCase);
            }
        }

        static IEnumerable<string> Candidates()
        {
            yield return Environment.GetEnvironmentVariable("HD2MUSIC_FFMPEG");
            yield return Environment.GetEnvironmentVariable("NETMUSIC_FFMPEG");
            string saved = Util.ReadText(Path.Combine(Util.Root, "ncm_ffmpeg.txt"))?.Trim();
            if (!string.IsNullOrEmpty(saved))
            {
                yield return saved;
            }
            yield return Path.Combine(Util.PluginDir, "ffmpeg.exe");
            yield return Path.Combine(Util.PluginDir, "tools", "ffmpeg.exe");
            yield return Path.Combine(Util.Root, "ffmpeg.exe");
            yield return Path.Combine(Util.Root, "tools", "ffmpeg.exe");
        }

        static IEnumerable<string> Targets()
        {
            yield return Path.Combine(Util.PluginDir, "ffmpeg.exe");
            yield return Path.Combine(Util.Root, "ffmpeg.exe");
        }
    }
}
