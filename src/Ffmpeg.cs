using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace Hd2Qd.HD2Music
{
    /// <summary>The decoder the player spawns. It travels in the DLL (assets\ffmpeg.exe.lzma,
    /// FFmpeg 6.1.1 essentials) and is written out once, next to the plugin or into the
    /// player's settings folder, when neither place has one. The extracted file is checked
    /// against the SHA-256 of the build that was packed.</summary>
    internal static class Ffmpeg
    {
        internal const string Resource = "ffmpeg.exe.lzma";
        internal const string Sha256 = "04e1307997530f9cf2fe35cba2ca7e8875ca91da02f89d6c7243df819c94ad00";
#if EMBEDDED_FFMPEG
        internal const bool Embedded = true;
#else
        internal const bool Embedded = false;
#endif
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
#if EMBEDDED_FFMPEG
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
#endif
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
                        throw new InvalidOperationException("ffmpeg.exe.lzma is not embedded");
                    }
                    using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        Unpack(packed, file);
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

        static Type _decoder;

        /// <summary>The resource is an .lzma stream: one byte of properties, four of dictionary
        /// size, eight of the unpacked length, then the data. GZipStream would leave the Full
        /// build at 29.6 MB, over the 25 MB a community plugin list accepts; this packs the
        /// same ffmpeg.exe to 20.7 MB. SevenZip.dll (the 7-Zip SDK's decoder, public domain)
        /// travels in the DLL as a resource and is loaded from bytes when first needed, as
        /// QrCode.cs does with QRCoder.dll.</summary>
        static void Unpack(Stream packed, Stream target)
        {
            byte[] header = new byte[13];
            for (int got = 0; got < header.Length; )
            {
                int read = packed.Read(header, got, header.Length - got);
                if (read <= 0)
                {
                    throw new InvalidDataException("the packed ffmpeg.exe is truncated");
                }
                got += read;
            }
            byte[] properties = new byte[5];
            Buffer.BlockCopy(header, 0, properties, 0, properties.Length);
            Type decoder = DecoderType();
            object coder = Activator.CreateInstance(decoder);
            decoder.GetMethod("SetDecoderProperties").Invoke(coder, new object[] { properties });
            MethodInfo code = decoder.GetMethods().First(m => m.Name == "Code" && m.GetParameters().Length == 5);
            code.Invoke(coder, new object[] { packed, target, -1L, BitConverter.ToInt64(header, properties.Length), null });
        }

        static Type DecoderType()
        {
            if (_decoder != null)
            {
                return _decoder;
            }
            using (Stream dll = typeof(Ffmpeg).Assembly.GetManifestResourceStream("SevenZip.dll"))
            {
                if (dll == null)
                {
                    throw new InvalidOperationException("SevenZip.dll is not embedded");
                }
                using (var bytes = new MemoryStream())
                {
                    dll.CopyTo(bytes);
                    _decoder = Assembly.Load(bytes.ToArray()).GetType("SevenZip.Compression.LZMA.Decoder");
                }
            }
            if (_decoder == null)
            {
                throw new InvalidOperationException("SevenZip.Compression.LZMA.Decoder is missing");
            }
            return _decoder;
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
            foreach (string candidate in WindowsCandidates())
            {
                yield return candidate;
            }
        }

        /// <summary>FFmpeg installed by winget, scoop or chocolatey, a manual unzip in the
        /// usual places, a PATH entry, and the App Paths registration Windows uses for
        /// "ffmpeg" typed in Run. Only files that look like a decoder (not a 0-byte app
        /// execution alias) are offered.</summary>
        static IEnumerable<string> WindowsCandidates()
        {
            foreach (string candidate in PathCandidates())
            {
                yield return candidate;
            }
            foreach (string candidate in AppPathCandidates())
            {
                yield return candidate;
            }
            foreach (string candidate in CommonCandidates())
            {
                yield return candidate;
            }
        }

        static IEnumerable<string> PathCandidates()
        {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path))
            {
                yield break;
            }
            foreach (string part in path.Split(';'))
            {
                string folder = Environment.ExpandEnvironmentVariables(part.Trim().Trim('"'));
                if (folder.Length == 0)
                {
                    continue;
                }
                string candidate = Path.Combine(folder, "ffmpeg.exe");
                if (Usable(candidate))
                {
                    yield return candidate;
                }
            }
        }

        static IEnumerable<string> AppPathCandidates()
        {
            RegistryHive[] hives = { RegistryHive.CurrentUser, RegistryHive.LocalMachine };
            RegistryView[] views = { RegistryView.Registry64, RegistryView.Registry32 };
            foreach (RegistryHive hive in hives)
            {
                foreach (RegistryView view in views)
                {
                    string candidate = null;
                    try
                    {
                        using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\ffmpeg.exe"))
                        {
                            if (key != null)
                            {
                                candidate = CleanExecutable(Convert.ToString(key.GetValue(null)));
                            }
                        }
                    }
                    catch
                    {
                    }
                    if (Usable(candidate))
                    {
                        yield return candidate;
                    }
                }
            }
        }

        static IEnumerable<string> CommonCandidates()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string choco = Environment.GetEnvironmentVariable("ChocolateyInstall");
            var folders = new List<string>
            {
                Path.Combine(programFiles, "ffmpeg", "bin"),
                Path.Combine(programFiles, "ffmpeg"),
                Path.Combine(programFilesX86, "ffmpeg", "bin"),
                Path.Combine(programFilesX86, "ffmpeg"),
                Path.Combine(user, "ffmpeg", "bin"),
                Path.Combine(user, "ffmpeg"),
                Path.Combine(user, "scoop", "shims"),
                Path.Combine(local, "Microsoft", "WinGet", "Links"),
                Path.Combine(user, "AppData", "Local", "Microsoft", "WinGet", "Links"),
                Path.Combine(roaming, "Microsoft", "Windows", "Start Menu", "Programs"),
                Path.Combine(common, "chocolatey", "bin"),
                string.IsNullOrEmpty(choco) ? null : Path.Combine(choco, "bin"),
                @"C:\ffmpeg\bin",
            };
            foreach (string folder in folders)
            {
                if (string.IsNullOrEmpty(folder))
                {
                    continue;
                }
                string candidate = Path.Combine(folder, "ffmpeg.exe");
                if (Usable(candidate))
                {
                    yield return candidate;
                }
            }
        }

        static string CleanExecutable(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            string text = Environment.ExpandEnvironmentVariables(value.Trim());
            if (text.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = text.IndexOf('"', 1);
                if (end > 1)
                {
                    text = text.Substring(1, end - 1);
                }
            }
            else
            {
                int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exe > 0)
                {
                    text = text.Substring(0, exe + 4);
                }
            }
            return text.Trim();
        }

        static bool Usable(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }
            try
            {
                var info = new FileInfo(path);
                return info.Exists && info.Length > 1024L * 1024L;
            }
            catch
            {
                return false;
            }
        }

        static IEnumerable<string> Targets()
        {
            yield return Path.Combine(Util.PluginDir, "ffmpeg.exe");
            yield return Path.Combine(Util.Root, "ffmpeg.exe");
        }
    }
}
