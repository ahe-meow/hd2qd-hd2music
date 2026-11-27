using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Hd2Qd.HD2Music;

/// <summary>
/// Answers the mod's Data Hub handshake from the pack's own .stream file. The mod drops
/// hd2_music_start.dat; we answer hd2_music_data.dat with the .stream verbatim and
/// hd2_music_ready.dat with the request echoed. Nothing is decrypted here — the mod
/// decrypts the stream itself, so this class only moves bytes it can prove are genuine.
///
/// Recovered from 1.2.6's authorize(), whose source ships encrypted but is unsealed by
/// ncm_session_loader.py; see docs/data-hub-handshake.md. 1.2.5 and older ship a
/// zero-byte .stream and load their body straight from the patch, so this stays idle.
/// </summary>
sealed class DataHub
{
	sealed class Payload
	{
		public string Session;

		public long Size;

		public string Digest;
	}

	// stream_size and stream_digest, read out of each version's signed manifest
	static readonly Payload[] Known =
	{
		new Payload
		{
			Session = "4611eedcff5adcb7ba9a36cb",
			Size = 154907L,
			Digest = "0cda0f57f6fc3f2f152b4fb36312bd599450cdb1fa2dab601d1c80906a8ff1d4"
		},
		new Payload
		{
			Session = "b47b8bca95c7b43374b34f41",
			Size = 166097L,
			Digest = "8592e8c576df733aca98146f25c17f75fe85b4725058d1d9fd2fe144ffa0b134"
		}
	};

	const int MaxPayload = 1048576;

	readonly string _root;

	readonly Func<int> _pid;

	string _approved;

	byte[] _media;

	string _mediaSession;

	DateTime _nextScan;

	string _note;

	bool _handshaked;

	int _handshakePid;

	public string Note
	{
		get
		{
			return _note;
		}
	}

	/// <summary>
	/// True once we have answered a handshake for the game process that is running now. A
	/// new session has a new pid, so the flag lapses by itself instead of leaking across
	/// restarts; see GameWatch.Modern for why this counts as "the mod is alive".
	/// </summary>
	public bool Handshaked
	{
		get
		{
			return _handshaked && (_handshakePid <= 0 || _handshakePid == _pid());
		}
	}

	public DataHub(string root, Func<int> pid)
	{
		_root = root;
		_pid = pid;
	}

	public void Tick()
	{
		string request = ReadRequest();
		if (request == null)
		{
			// the mod withdrew its request (closed the phone), so the same text must be
			// answered again next time instead of being treated as already delivered
			_approved = null;
			return;
		}
		if (string.Equals(request, _approved, StringComparison.Ordinal))
		{
			return;
		}
		// the cached payload belongs to one build. Swap the deployed mod without restarting the
		// manager and the previous build's bytes would be handed to the new shell, which sits in
		// the same folder and asks under a different session: decrypt garbage, give up silently.
		string asked = request.Substring(0, request.IndexOf(':'));
		if (_media != null && !string.Equals(asked, _mediaSession, StringComparison.OrdinalIgnoreCase))
		{
			_media = null;
			_mediaSession = null;
			_nextScan = DateTime.MinValue;
		}
		if (_media == null && !FindMedia(request))
		{
			return;
		}
		Deliver(request);
	}

	string RequestPath
	{
		get
		{
			return Path.Combine(_root, "hd2_music_start.dat");
		}
	}

	string ReadRequest()
	{
		try
		{
			FileInfo info = new FileInfo(RequestPath);
			if (!info.Exists || info.Length <= 0 || info.Length > 128)
			{
				return null;
			}
			// a mod that is still waiting rewrites this file every second; an untouched one is
			// a leftover from an earlier session (an older mod, or a payload already delivered)
			// and answering it would claim a handshake nobody is waiting for
			if ((DateTime.UtcNow - info.LastWriteTimeUtc).TotalSeconds > 120.0)
			{
				return null;
			}
			string text = File.ReadAllText(RequestPath, Encoding.ASCII);
			// the mod's own reader demands "session:epoch:..." and a trailing newline
			if (!text.EndsWith("\n", StringComparison.Ordinal))
			{
				return null;
			}
			int first = text.IndexOf(':');
			int second = (first < 0) ? (-1) : text.IndexOf(':', first + 1);
			if (first <= 0 || second <= first + 1)
			{
				return null;
			}
			return text;
		}
		catch (IOException)
		{
			return null;
		}
		catch (UnauthorizedAccessException)
		{
			return null;
		}
	}

	bool FindMedia(string request)
	{
		if (DateTime.UtcNow < _nextScan)
		{
			return false;
		}
		_nextScan = DateTime.UtcNow.AddSeconds(1.0);
		string session = request.Substring(0, request.IndexOf(':'));
		Payload known = null;
		foreach (Payload item in Known)
		{
			if (string.Equals(item.Session, session, StringComparison.OrdinalIgnoreCase))
			{
				known = item;
				break;
			}
		}
		foreach (string path in Candidates())
		{
			try
			{
				FileInfo info = new FileInfo(path);
				if (!info.Exists || info.Length <= 0 || info.Length > MaxPayload)
				{
					continue;
				}
				// a known session pins the exact build; an unknown one still gets served when
				// the bytes hash to a digest we trust, so a patch bump does not break delivery
				if (known != null && info.Length != known.Size)
				{
					continue;
				}
				byte[] data = File.ReadAllBytes(path);
				if (data.Length <= 0 || data.Length > MaxPayload)
				{
					continue;
				}
				if (!Trusted(Hash(data)))
				{
					continue;
				}
				_media = data;
				_mediaSession = session;
				_note = null;
				return true;
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
		_note = "找不到模组主体（HD2 Music 的 .patch_0.stream）";
		return false;
	}

	static bool Trusted(string digest)
	{
		foreach (Payload item in Known)
		{
			if (string.Equals(item.Digest, digest, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	IEnumerable<string> Candidates()
	{
		string game = GameDataDir(_pid());
		if (game != null)
		{
			foreach (string path in Sorted(game, "*.patch_*.stream"))
			{
				yield return path;
			}
		}
		foreach (string library in Libraries())
		{
			foreach (string folder in Sorted(library, "*HD2 Music*"))
			{
				string data = Path.Combine(folder, "data");
				foreach (string path in Sorted(data, "*.patch_*.stream"))
				{
					yield return path;
				}
			}
		}
		string cache = Path.Combine(_root, "hd2_music_stream.bin");
		if (File.Exists(cache))
		{
			yield return cache;
		}
	}

	/// <summary>
	/// Arsenal writes a zero-byte .stream next to the deployed patch, so the library copy is
	/// the one that still holds the body. The manager's own config names the folders.
	/// </summary>
	IEnumerable<string> Libraries()
	{
		List<string> found = new List<string>();
		string config = Path.Combine(Util.SettingsHome, "config.json");
		string raw = Util.ReadText(config);
		if (raw != null)
		{
			foreach (string value in JsonStrings(raw, "library_folders"))
			{
				found.Add(value);
			}
			foreach (string value in JsonStrings(raw, "mods_dir"))
			{
				found.Add(value);
			}
		}
		string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
		found.Add(Path.Combine(documents, "Helldivers2", "mods"));
		found.Add(Path.Combine(documents, "Helldivers2", "library"));
		return found;
	}

	static IEnumerable<string> JsonStrings(string json, string key)
	{
		int at = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
		if (at < 0)
		{
			yield break;
		}
		int colon = json.IndexOf(':', at);
		if (colon < 0)
		{
			yield break;
		}
		int i = colon + 1;
		while (i < json.Length)
		{
			char c = json[i];
			if (c == ']')
			{
				yield break;
			}
			if (c != '"')
			{
				i++;
				continue;
			}
			StringBuilder builder = new StringBuilder();
			i++;
			while (i < json.Length && json[i] != '"')
			{
				if (json[i] == '\\' && i + 1 < json.Length)
				{
					i++;
					builder.Append(json[i] == '\\' ? '\\' : json[i]);
				}
				else
				{
					builder.Append(json[i]);
				}
				i++;
			}
			i++;
			yield return builder.ToString();
		}
	}

	static IEnumerable<string> Sorted(string folder, string pattern)
	{
		string[] files;
		try
		{
			if (!Directory.Exists(folder))
			{
				return new string[0];
			}
			files = Directory.GetFiles(folder, pattern);
		}
		catch (IOException)
		{
			return new string[0];
		}
		catch (UnauthorizedAccessException)
		{
			return new string[0];
		}
		Array.Sort(files, StringComparer.OrdinalIgnoreCase);
		return files;
	}

	void Deliver(string request)
	{
		try
		{
			Util.AtomicWrite(Path.Combine(_root, "hd2_music_data.dat"), _media);
			Util.AtomicWrite(Path.Combine(_root, "hd2_music_ready.dat"), Encoding.ASCII.GetBytes(request));
			_approved = request;
			_handshaked = true;
			_handshakePid = _pid();
			_note = null;
		}
		catch (IOException error)
		{
			_note = "无法交付模组主体：" + error.Message;
		}
		catch (UnauthorizedAccessException error2)
		{
			_note = "无法交付模组主体：" + error2.Message;
		}
	}

	static string Hash(byte[] data)
	{
		using (SHA256 sha = SHA256.Create())
		{
			byte[] digest = sha.ComputeHash(data);
			StringBuilder builder = new StringBuilder(digest.Length * 2);
			foreach (byte b in digest)
			{
				builder.Append(b.ToString("x2"));
			}
			return builder.ToString();
		}
	}

	static string GameDataDir(int pid)
	{
		if (pid <= 0)
		{
			return null;
		}
		IntPtr handle = OpenProcess(4096u, false, pid);
		if (handle == IntPtr.Zero)
		{
			return null;
		}
		try
		{
			StringBuilder buffer = new StringBuilder(32768);
			int size = buffer.Capacity;
			if (!QueryFullProcessImageName(handle, 0u, buffer, ref size))
			{
				return null;
			}
			string executable = buffer.ToString();
			if (!string.Equals(Path.GetFileName(executable), "helldivers2.exe", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			DirectoryInfo folder = new DirectoryInfo(Path.GetDirectoryName(executable));
			for (int depth = 0; depth < 2; depth++)
			{
				if (folder == null)
				{
					break;
				}
				string data = Path.Combine(folder.FullName, "data");
				if (Directory.Exists(data))
				{
					return data;
				}
				folder = folder.Parent;
			}
			return null;
		}
		finally
		{
			CloseHandle(handle);
		}
	}

	[DllImport("kernel32", SetLastError = true)]
	static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

	[DllImport("kernel32", SetLastError = true)]
	static extern bool CloseHandle(IntPtr handle);
}
