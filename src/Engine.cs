using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Hd2Qd.HD2Music;

public sealed class Engine : IDisposable
{
	private sealed class PasswordCredentials
	{
		public string Country;

		public string Phone;

		public Session Session;
	}

	private sealed class MixMarker
	{
		public int Serial;

		public string List = "";

		public int Index;

		public override string ToString()
		{
			return Serial + "|" + List + "|" + Index;
		}
	}

	private readonly Dictionary<string, Task<CoverArt>> _artTasks = new Dictionary<string, Task<CoverArt>>(StringComparer.Ordinal);

	private readonly Dictionary<string, CoverArt> _artResults = new Dictionary<string, CoverArt>(StringComparer.Ordinal);

	private readonly Dictionary<string, string> _recommendTiles = new Dictionary<string, string>(StringComparer.Ordinal);

	private readonly Dictionary<string, string> _recommendKeys = new Dictionary<string, string>(StringComparer.Ordinal);

	private readonly Dictionary<string, string> _listTiles = new Dictionary<string, string>(StringComparer.Ordinal);

	private readonly Dictionary<string, string> _listKeys = new Dictionary<string, string>(StringComparer.Ordinal);

	private readonly HashSet<string> _visibleLists = new HashSet<string>(StringComparer.Ordinal);

	private readonly Dictionary<string, string> _recommendFiles = new Dictionary<string, string>(StringComparer.Ordinal);

	private int _artRunning;

	private bool _artCleanupDone;

	private string _coverUrl = "";

	private string _coverPreviewKey = "";

	private string _coverTextureKey = "";

	private string _miniKey = "";

	public static readonly string[] Qualities = new string[5] { "standard", "higher", "exhigh", "lossless", "hires" };

	public static readonly string[] Orders = new string[3] { "order", "shuffle", "repeat" };

	public const string WaitingForGame = "请先进入游戏并启用新版播放器";

	public readonly string Root;

	public string FfmpegPath;

	public NeteaseClient Api;

	public StreamPlayer Player;

	private readonly object _lock = new object();

	private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();

	private readonly Stopwatch _clock = Stopwatch.StartNew();

	private Thread _worker;

	private Thread _loop;

	private Thread _spectrumLoop;

	private volatile bool _stopped;

	private FileStream _lockFile;

	private bool _lockedOut;

	private static int _commandSerial;

	private double _qrExpires;

	private double _nextScan;

	private string _key = "";

	private string _matrix = "";

	private string _qrResult = "";

	private string _notice = "";

	private string _account = "";

	private string _state = "idle";

	private string _currentList = "";

	private string _currentListId = "";

	private int _index = -1;

	private int _trackFirst = 1;

	private int _seekSerial;

	private bool _seeking;

	private double? _resumePosition;

	private int _resumeAttempts;

	private double _resumeAt;

	private readonly List<Playlist> _lists = new List<Playlist>();

	private readonly List<Playlist> _recommended = new List<Playlist>();

	private readonly List<Track> _queueTracks = new List<Track>();

	private readonly List<Track> _searchResults = new List<Track>();

	private readonly List<Track> _podcastTracks = new List<Track>();

	private readonly List<RadioChannel> _radios = new List<RadioChannel>();

	private readonly List<LyricLine> _lyricsRows = new List<LyricLine>();

	private int _searchSerial;

	private string _searchKeyword = "";

	private string _searchState = "";

	private string _radioState = "";

	private string _podcastState = "";

	private string _podcastChannel = "";

	private string _recommendState = "";

	private string _quality = "standard";

	private string _order = "order";

	private bool _automix = true;

	private bool _lyricsEnabled = true;

	private readonly HashSet<int> _shufflePlayed = new HashSet<int>();

	private int? _shuffleNext;

	private readonly List<int> _shuffleHistory = new List<int>();

	private PasswordCredentials _sms;

	private DateTime _nextSms = DateTime.MinValue;

	private string _smsStage = "";

	private string _smsNotice = "";

	private string _cookieNotice = "";

	private Task<string> _mixTask;

	private object _mixGame;

	private MixMarker _mixMarker;

	private string _mixAttempt = "";

	private string _lyricsSong = "";

	private string _spectrumKey = "";

	private double _spectrumAt;

	private string CoverTextureKey
	{
		get
		{
			lock (_lock)
			{
				return _coverTextureKey;
			}
		}
	}

	private string CoverPreviewKey
	{
		get
		{
			lock (_lock)
			{
				return _coverPreviewKey;
			}
		}
	}

	private string MiniKey
	{
		get
		{
			lock (_lock)
			{
				return _miniKey;
			}
		}
	}

	public bool IsLockedOut
	{
		get
		{
			lock (_lock)
			{
				return _lockedOut;
			}
		}
	}

	public string CookieNotice
	{
		get
		{
			lock (_lock)
			{
				return _cookieNotice;
			}
		}
	}

	public void SetVisiblePlaylists(IEnumerable<string> ids)
	{
		lock (_lock)
		{
			_visibleLists.Clear();
			if (ids == null)
			{
				return;
			}
			foreach (string id in ids)
			{
				if (!string.IsNullOrEmpty(id))
				{
					_visibleLists.Add(id);
				}
			}
		}
	}

	private CoverArt GetArt(string url, int size, bool thumbnail)
	{
		if (string.IsNullOrEmpty(url))
		{
			return null;
		}
		string key = url + "|" + size + "|" + (thumbnail ? "t" : "f");
		lock (_lock)
		{
			if (_artResults.TryGetValue(key, out var value))
			{
				return value;
			}
			if (_artTasks.TryGetValue(key, out var value2))
			{
				if (!value2.IsCompleted)
				{
					return null;
				}
				try
				{
					value = value2.Result;
				}
				catch
				{
					value = CoverArt.Empty;
				}
				_artResults[key] = value;
				_artTasks.Remove(key);
				return value;
			}
			int num = (thumbnail ? 2 : 7);
			if (_artRunning >= num)
			{
				return null;
			}
			_artRunning++;
			string ffmpeg = FfmpegPath;
			_artTasks[key] = Task.Run(delegate
			{
				CoverArt coverArt;
				try
				{
					coverArt = CoverService.Load(url, ffmpeg, size, thumbnail);
				}
				catch
				{
					coverArt = CoverArt.Empty;
				}
				lock (_lock)
				{
					_artRunning--;
					_artResults[key] = coverArt;
					_artTasks.Remove(key);
					return coverArt;
				}
			});
			return null;
		}
	}

	private static string TextureKey(byte[] packet)
	{
		if (packet == null || packet.Length < 64)
		{
			return "";
		}
		return Encoding.ASCII.GetString(packet, 0, 64);
	}

	private void UpdateCovers()
	{
		if (!_artCleanupDone)
		{
			_artCleanupDone = true;
			try
			{
				string[] files = Directory.GetFiles(Root, "ncm_recommend_*.dat");
				for (int i = 0; i < files.Length; i++)
				{
					File.Delete(files[i]);
				}
			}
			catch
			{
			}
		}
		string text;
		List<Playlist> list;
		List<Playlist> list2;
		HashSet<string> hashSet;
		lock (_lock)
		{
			Track track = ((_index >= 0 && _index < _queueTracks.Count) ? _queueTracks[_index] : null);
			text = ((track == null) ? "" : track.CoverUrl);
			list = new List<Playlist>(_recommended);
			list2 = new List<Playlist>(_lists);
			hashSet = new HashSet<string>(_visibleLists);
			if (_coverUrl != (text ?? ""))
			{
				_coverUrl = text ?? "";
				_coverPreviewKey = "";
				_coverTextureKey = "";
				_miniKey = "";
			}
		}
		CoverArt art = GetArt(text, 160, thumbnail: false);
		if ((art != null && !string.IsNullOrEmpty(art.Tiles)) || (art != null && art.Texture.Length != 0))
		{
			string text2 = TextureKey(art.Texture);
			string text3 = TextureKey(art.Mini);
			try
			{
				if (!string.IsNullOrEmpty(text2) && text2 != CoverTextureKey)
				{
					Util.AtomicWrite(Path.Combine(Root, "ncm_cover_texture.dat"), art.Texture);
				}
				if (!string.IsNullOrEmpty(art.Preview) && text2 != CoverPreviewKey)
				{
					string text4 = "key=" + text2 + "\ncover=" + art.Preview + "\ncover_tiles=" + art.Tiles + "\n";
					Util.AtomicWrite(Path.Combine(Root, "ncm_cover_preview.dat"), text4);
				}
				if (!string.IsNullOrEmpty(text3) && text3 != MiniKey)
				{
					Util.AtomicWrite(Path.Combine(Root, "ncm_mini_cover.dat"), art.Mini);
				}
			}
			catch
			{
			}
			lock (_lock)
			{
				_coverTextureKey = text2;
				_coverPreviewKey = text2;
				_miniKey = text3;
			}
		}
		foreach (Playlist item in list)
		{
			if (item == null || string.IsNullOrEmpty(item.CoverUrl))
			{
				continue;
			}
			bool flag;
			lock (_lock)
			{
				flag = _recommendKeys.ContainsKey(item.Id);
			}
			if (flag)
			{
				continue;
			}
			CoverArt art2 = GetArt(item.CoverUrl, 160, thumbnail: true);
			if (art2 == null)
			{
				continue;
			}
			string text5 = TextureKey(art2.Texture);
			if (string.IsNullOrEmpty(text5))
			{
				continue;
			}
			try
			{
				string path = Path.Combine(Root, "ncm_recommend_" + text5 + ".dat");
				if (!File.Exists(path))
				{
					Util.AtomicWrite(path, art2.Texture);
				}
				lock (_lock)
				{
					_recommendTiles[item.Id] = art2.Tiles;
					_recommendKeys[item.Id] = text5;
					RememberArtFile(text5, path);
				}
			}
			catch
			{
			}
		}
		foreach (Playlist item2 in list2)
		{
			if (item2 == null || !hashSet.Contains(item2.Id) || string.IsNullOrEmpty(item2.CoverUrl))
			{
				continue;
			}
			bool flag2;
			lock (_lock)
			{
				flag2 = _listKeys.ContainsKey(item2.Id);
			}
			if (flag2)
			{
				continue;
			}
			CoverArt art3 = GetArt(item2.CoverUrl, 160, thumbnail: true);
			if (art3 == null)
			{
				continue;
			}
			string text6 = TextureKey(art3.Texture);
			if (string.IsNullOrEmpty(text6))
			{
				continue;
			}
			try
			{
				string path2 = Path.Combine(Root, "ncm_recommend_" + text6 + ".dat");
				if (!File.Exists(path2))
				{
					Util.AtomicWrite(path2, art3.Texture);
				}
				lock (_lock)
				{
					_listTiles[item2.Id] = art3.Tiles;
					_listKeys[item2.Id] = text6;
					RememberArtFile(text6, path2);
				}
			}
			catch
			{
			}
		}
	}

	private void RememberArtFile(string key, string path)
	{
		_recommendFiles[key] = path;
		while (_recommendFiles.Count > 24)
		{
			string text = null;
			using (Dictionary<string, string>.KeyCollection.Enumerator enumerator = _recommendFiles.Keys.GetEnumerator())
			{
				if (enumerator.MoveNext())
				{
					text = enumerator.Current;
				}
			}
			if (text != null)
			{
				string path2 = _recommendFiles[text];
				_recommendFiles.Remove(text);
				try
				{
					File.Delete(path2);
				}
				catch
				{
				}
				continue;
			}
			break;
		}
	}

	public Engine(string ffmpegPath)
	{
		Root = Util.Root;
		FfmpegPath = ffmpegPath;
	}

	public void Start()
	{
		Directory.CreateDirectory(Root);
		try
		{
			_lockFile = new FileStream(Path.Combine(Root, "ncm_helper.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
			_lockFile.Lock(0L, 1L);
		}
		catch
		{
			_lockedOut = true;
			_notice = "网易云在线助手已经在运行（另一个进程持有锁）";
		}
		Api = new NeteaseClient(new Session(Path.Combine(Root, "ncm_session.json")));
		Player = new StreamPlayer(FfmpegPath, Path.Combine(Root, "ncm_game_alive.txt"));
		Player.GameWatch.Start();
		LoadSettings();
		_worker = new Thread(WorkerRun)
		{
			IsBackground = true,
			Name = "NetMusic commands"
		};
		_loop = new Thread(LoopRun)
		{
			IsBackground = true,
			Name = "NetMusic state"
		};
		_spectrumLoop = new Thread(SpectrumRun)
		{
			IsBackground = true,
			Name = "NetMusic spectrum"
		};
		_worker.Start();
		_loop.Start();
		_spectrumLoop.Start();
		if (Api.Session.LoggedIn)
		{
			_notice = "正在读取已保存的登录状态…";
			Post(delegate
			{
				RefreshAccount(initial: true);
			});
		}
		else
		{
			_notice = "请扫码登录";
		}
		Publish();
	}

	public void Dispose()
	{
		_stopped = true;
		try
		{
			_queue.CompleteAdding();
		}
		catch
		{
		}
		try
		{
			if (_worker != null)
			{
				_worker.Join(1500);
			}
		}
		catch
		{
		}
		try
		{
			if (_loop != null)
			{
				_loop.Join(1500);
			}
		}
		catch
		{
		}
		try
		{
			if (_spectrumLoop != null)
			{
				_spectrumLoop.Join(1500);
			}
		}
		catch
		{
		}
		try
		{
			if (Player != null)
			{
				Player.GameWatch.Close();
				Player.Close();
			}
		}
		catch
		{
		}
		if (_lockFile != null)
		{
			try
			{
				_lockFile.Unlock(0L, 1L);
			}
			catch
			{
			}
			try
			{
				_lockFile.Close();
			}
			catch
			{
			}
			_lockFile = null;
		}
		// the files are the running helper's when this player never got the lock
		if (!_lockedOut)
		{
			try
			{
				File.Delete(Path.Combine(Root, "ncm_state.txt"));
			}
			catch
			{
			}
			try
			{
				File.Delete(Path.Combine(Root, "ncm_spectrum.txt"));
			}
			catch
			{
			}
		}
	}

	private double Now()
	{
		return _clock.Elapsed.TotalSeconds;
	}

	public void Post(Action action)
	{
		if (_stopped || action == null)
		{
			return;
		}
		try
		{
			_queue.Add(action);
		}
		catch
		{
		}
	}

	/// <summary>The mod helper owns ncm_helper.lock whenever F7 plays. This plugin cannot decode
	/// then, but both sides share ncm_cmd_*.txt: send the same commands the game UI sends so a
	/// click in the manager still drives the helper that is actually playing.</summary>
	void SendCommand(string line)
	{
		if (!_lockedOut || string.IsNullOrWhiteSpace(line))
		{
			return;
		}
		int number = Interlocked.Increment(ref _commandSerial);
		string file = Path.Combine(Root, "ncm_cmd_hd2qd_" + number.ToString(CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
		try
		{
			Util.AtomicWrite(file, line + "\n");
		}
		catch
		{
		}
		lock (_lock)
		{
			_notice = "已转交在线助手播放";
		}
	}

	static Dictionary<string, string> HelperState()
	{
		var values = new Dictionary<string, string>(StringComparer.Ordinal);
		string text = Util.ReadText(Path.Combine(Util.Root, "ncm_state.txt"));
		if (string.IsNullOrEmpty(text))
		{
			return values;
		}
		foreach (string line in text.Split('\n'))
		{
			int at = line.IndexOf('=');
			if (at > 0)
			{
				values[line.Substring(0, at)] = line.Substring(at + 1).Trim();
			}
		}
		return values;
	}

	static string Value(Dictionary<string, string> state, string key)
	{
		return state.TryGetValue(key, out string text) ? text : "";
	}

	static int IntValue(Dictionary<string, string> state, string key)
	{
		return int.TryParse(Value(state, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
	}

	static double DoubleValue(Dictionary<string, string> state, string key)
	{
		return double.TryParse(Value(state, key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0.0;
	}

	static string HexEncode(string text)
	{
		var bytes = Encoding.UTF8.GetBytes(text ?? "");
		var builder = new StringBuilder(bytes.Length * 2);
		foreach (byte value in bytes)
		{
			builder.Append(value.ToString("x2"));
		}
		return builder.ToString();
	}

	void DelegateTrack(int index)
	{
		string listId;
		lock (_lock)
		{
			if (index < 0 || index >= _queueTracks.Count)
			{
				return;
			}
			listId = _currentListId;
		}
		if (string.IsNullOrEmpty(listId))
		{
			return;
		}
		Dictionary<string, string> helper = HelperState();
		if (Value(helper, "list_id") != listId || IntValue(helper, "count") <= index)
		{
			SendCommand("playlist " + listId);
		}
		ThreadPool.QueueUserWorkItem(delegate
		{
			for (int i = 0; i < 60; i++)
			{
				Thread.Sleep(250);
				Dictionary<string, string> state = HelperState();
				if (Value(state, "list_id") == listId && IntValue(state, "count") > index)
				{
					SendCommand("track_play " + listId + " " + (index + 1).ToString(CultureInfo.InvariantCulture));
					return;
				}
			}
			lock (_lock)
			{
				_notice = "在线助手未响应播放指令，请用 F7 播放";
			}
		});
	}

	void DelegateSearchPlay(int index)
	{
		string keyword;
		lock (_lock)
		{
			keyword = _searchKeyword;
		}
		if (string.IsNullOrWhiteSpace(keyword))
		{
			return;
		}
		SendCommand("search " + HexEncode(keyword));
		ThreadPool.QueueUserWorkItem(delegate
		{
			for (int i = 0; i < 40; i++)
			{
				Thread.Sleep(250);
				Dictionary<string, string> state = HelperState();
				if (Value(state, "search_state") == "ready" && Value(state, "list_id") == "search" && IntValue(state, "count") > index)
				{
					SendCommand("track_play search " + (index + 1).ToString(CultureInfo.InvariantCulture));
					return;
				}
			}
			lock (_lock)
			{
				_notice = "在线助手搜索未完成，请用 F7 播放";
			}
		});
	}

	void MirrorHelper(UiState obj)
	{
		Dictionary<string, string> state = HelperState();
		if (state.Count == 0)
		{
			obj.Notice = "网易云在线助手已在运行，播放由助手接管";
			return;
		}
		obj.State = Value(state, "state");
		obj.Title = Value(state, "title");
		obj.Artist = Value(state, "artist");
		obj.PlaylistName = Value(state, "playlist");
		obj.Index = Math.Max(0, IntValue(state, "index") - 1);
		obj.Count = IntValue(state, "count");
		obj.Pos = DoubleValue(state, "pos");
		obj.Duration = DoubleValue(state, "duration");
		obj.Volume = IntValue(state, "volume");
		obj.Quality = Value(state, "quality");
		obj.Order = Value(state, "order");
		obj.LyricsEnabled = Value(state, "lyrics_on") == "1";
		obj.Automix = Value(state, "automix") == "1";
		obj.SeekReady = Value(state, "seek_ready") == "1";
		obj.PreviousLyric = Value(state, "previous_lyric");
		obj.Lyric = Value(state, "lyric");
		obj.NextLyric = Value(state, "next_lyric");
		obj.Translation = Value(state, "lyric_translation");
		obj.Notice = string.IsNullOrEmpty(Value(state, "notice")) ? "在线助手已接管播放" : Value(state, "notice");
	}

	private void WorkerRun()
	{
		while (!_stopped)
		{
			Action item;
			try
			{
				if (!_queue.TryTake(out item, 200))
				{
					continue;
				}
			}
			catch
			{
				break;
			}
			try
			{
				item();
			}
			catch (Exception error)
			{
				lock (_lock)
				{
					_notice = Friendly(error);
				}
			}
		}
	}

	private static string Friendly(Exception error)
	{
		if (error is QrVerificationRequired)
		{
			return error.Message;
		}
		if (error is SmsLoginError)
		{
			return error.Message;
		}
		return "请求失败：" + error.Message;
	}

	private void LoopRun()
	{
		while (!_stopped)
		{
			try
			{
				Step();
			}
			catch (Exception error)
			{
				lock (_lock)
				{
					_notice = Friendly(error);
				}
			}
			try
			{
				Publish();
			}
			catch
			{
			}
			Thread.Sleep(200);
		}
	}

	private void SpectrumRun()
	{
		while (!_stopped)
		{
			try
			{
				PublishSpectrum();
			}
			catch
			{
			}
			Thread.Sleep(35);
		}
	}

	private void Step()
	{
		if (_lockedOut)
		{
			return;
		}
		ProcessCommands();
		if (Player != null)
		{
			Player.Tick();
		}
		MixTick();
		PollLogin();
		CheckGame(prompt: false);
		if (Player != null)
		{
			int? num = Player.Result();
			if (num.HasValue && _state != "idle")
			{
				if (num == 0)
				{
					int next = NextPosition(manual: false);
					Post(delegate
					{
						StartPlay(next);
					});
				}
				else
				{
					Post(delegate
					{
						Recover();
					});
				}
			}
			if (Player.Active && !Player.AwaitingPlay && !Player.Outcome.HasValue)
			{
				string state = Player.Status.State;
				if (state == "playing" || state == "paused")
				{
					lock (_lock)
					{
						_state = state;
						_seeking = false;
						_resumePosition = null;
					}
				}
			}
		}
		FinishJobs();
	}

	private void FinishJobs()
	{
	}

	private void LoadSettings()
	{
		_quality = ReadSetting("ncm_quality.txt", "standard");
		if (!Qualities.Contains(_quality))
		{
			_quality = "standard";
		}
		_order = ReadSetting("ncm_order.txt", "order");
		if (!Orders.Contains(_order))
		{
			_order = "order";
		}
		_automix = ReadSetting("ncm_automix.txt", "1") != "0";
		_lyricsEnabled = ReadSetting("ncm_lyrics_enabled.txt", "1") != "0";
		lock (_lock)
		{
			Player.Volume = 65;
		}
	}

	private string ReadSetting(string name, string fallback)
	{
		try
		{
			string text = File.ReadAllText(Path.Combine(Root, name), Encoding.ASCII).Trim();
			return (text.Length == 0) ? fallback : text;
		}
		catch
		{
			return fallback;
		}
	}

	private void WriteSetting(string name, string value)
	{
		try
		{
			Util.AtomicWrite(Path.Combine(Root, name), value);
		}
		catch
		{
		}
	}

	private bool CheckGame(bool prompt)
	{
		if (Player == null)
		{
			return false;
		}
		if (Player.GameWatch.Token != null)
		{
			return true;
		}
		if (Player.Active || _state == "loading" || _state == "playing" || _state == "paused" || _state == "buffering")
		{
			Player.Close();
			lock (_lock)
			{
				_state = "idle";
				_seeking = false;
				_resumePosition = null;
				_resumeAttempts = 0;
				_lyricsRows.Clear();
			}
		}
		if (prompt)
		{
			lock (_lock)
			{
				_notice = "请先进入游戏并启用新版播放器";
			}
		}
		return false;
	}

	public UiState Snapshot()
	{
		lock (_lock)
		{
			double num = ((Player != null) ? Player.Status.Pos : 0.0);
			double num2 = ((Player != null) ? Player.Status.Dur : 0.0);
			Track track = ((_index >= 0 && _index < _queueTracks.Count) ? _queueTracks[_index] : null);
			if (num2 <= 0.0 && track != null)
			{
				num2 = track.Duration;
			}
			UiState obj = new UiState
			{
				Account = _account,
				Notice = _notice,
				GameRunning = Player != null && Player.GameWatch != null && Player.GameWatch.Token != null,
				State = _state,
				Title = ((track != null) ? track.Title : ""),
				Artist = ((track != null) ? track.Artist : ""),
				CoverUrl = ((track != null) ? track.CoverUrl : ""),
				PlaylistName = _currentList,
				Quality = _quality,
				Order = _order,
				QrMatrix = _matrix,
				QrResult = _qrResult,
				Key = _key,
				Index = _index,
				Count = _queueTracks.Count,
				TrackFirst = _trackFirst,
				SearchSerial = _searchSerial,
				SearchKeyword = _searchKeyword,
				SearchState = _searchState,
				RadioState = _radioState,
				PodcastState = _podcastState,
				RecommendState = _recommendState,
				SmsStage = _smsStage,
				SmsNotice = _smsNotice,
				Pos = num,
				Duration = num2,
				Volume = ((Player != null) ? Player.Volume : 65),
				LyricsEnabled = _lyricsEnabled,
				Automix = _automix,
				Seeking = _seeking,
				SeekReady = (Player != null && Player.Active && !Player.AwaitingPlay && num2 > 0.0 && !_seeking),
				Lists = new List<Playlist>(_lists),
				Recommended = new List<Playlist>(_recommended),
				Queue = new List<Track>(_queueTracks),
				SearchResults = new List<Track>(_searchResults),
				Radios = new List<RadioChannel>(_radios),
				PodcastTracks = new List<Track>(_podcastTracks)
			};
			LyricLines(num, out var previous, out var current, out var next, out var _, out var index, out var translation);
			obj.PreviousLyric = previous;
			obj.Lyric = current;
			obj.NextLyric = next;
			obj.Translation = translation;
			obj.LyricIndex = index;
			if (_lockedOut)
			{
				MirrorHelper(obj);
			}
			return obj;
		}
	}

	private void LyricLines(double position, out string previous, out string current, out string next, out string prefix, out int index, out string translation)
	{
		previous = (current = (next = (prefix = (translation = ""))));
		index = -1;
		if (!_lyricsEnabled || (_state != "playing" && _state != "paused" && _state != "buffering") || _lyricsRows.Count == 0)
		{
			return;
		}
		int num = -1;
		for (int i = 0; i < _lyricsRows.Count && _lyricsRows[i].At <= position; i++)
		{
			num = i;
		}
		index = num;
		if (num < 0)
		{
			return;
		}
		LyricLine lyricLine = _lyricsRows[num];
		current = lyricLine.Text;
		translation = lyricLine.Translation;
		previous = ((num > 0) ? _lyricsRows[num - 1].Text : "");
		for (int j = num + 1; j < _lyricsRows.Count; j++)
		{
			if (!string.IsNullOrEmpty(_lyricsRows[j].Text))
			{
				next = _lyricsRows[j].Text;
				break;
			}
		}
		if (lyricLine.Words == null)
		{
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (LyricWord word in lyricLine.Words)
		{
			if (word.At <= position)
			{
				stringBuilder.Append(word.Text);
			}
		}
		prefix = Util.Clean(stringBuilder.ToString());
	}

	public void RefreshAccount()
	{
		Post(delegate
		{
			RefreshAccount(initial: false);
		});
	}

	private void RefreshAccount(bool initial)
	{
		try
		{
			AccountInfo accountInfo = Api.Account();
			List<Playlist> list = null;
			if (!string.IsNullOrEmpty(accountInfo.Uid))
			{
				try
				{
					list = Api.Playlists(accountInfo.Uid);
				}
				catch
				{
					list = null;
				}
			}
			lock (_lock)
			{
				string account = _account;
				_account = ((!string.IsNullOrEmpty(accountInfo.Nickname)) ? accountInfo.Nickname : (string.IsNullOrEmpty(accountInfo.Uid) ? "" : "网易云账号"));
				_lists.Clear();
				if (list != null)
				{
					_lists.AddRange(list);
				}
				if (string.IsNullOrEmpty(accountInfo.Uid))
				{
					_notice = "请扫码登录";
				}
				else if (list == null)
				{
					_notice = "已登录，歌单读取失败，请点刷新歌单";
				}
				else
				{
					_notice = "";
				}
				if (!string.IsNullOrEmpty(account) && account != _account)
				{
					_queueTracks.Clear();
					_index = -1;
					_currentList = "";
					_currentListId = "";
					_state = "idle";
				}
			}
		}
		catch
		{
			lock (_lock)
			{
				_notice = (initial ? "已保存的登录状态不可用，请重新登录" : "无法读取网易云账号，请稍后刷新");
				if (initial)
				{
					_account = "";
				}
			}
		}
	}

	public void BeginQrLogin()
	{
		lock (_lock)
		{
			_key = "";
			_matrix = "";
			_qrResult = "";
			_notice = "正在获取网易云二维码…";
		}
		Post(delegate
		{
			try
			{
				string text = Api.Qr();
				Dictionary<string, string> values = new Dictionary<string, string> { { "codekey", text } };
				string matrix = QrCode.MakeMatrix("https://music.163.com/login" + HttpJson.Query(values));
				lock (_lock)
				{
					_key = text;
					_matrix = matrix;
					_qrResult = "";
					_notice = "请用网易云音乐 App 扫码";
					_qrExpires = Now() + 180.0;
					_nextScan = Now() + 2.0;
				}
			}
			catch (QrVerificationRequired qrVerificationRequired)
			{
				lock (_lock)
				{
					_key = "";
					_matrix = "";
					_qrResult = "verification";
					_notice = qrVerificationRequired.Message;
				}
			}
			catch (Exception ex)
			{
				lock (_lock)
				{
					_key = "";
					_matrix = "";
					_notice = "暂时无法获取网易云二维码，请稍后重试（" + ex.Message + "）";
				}
			}
		});
	}

	public void CancelLogin()
	{
		lock (_lock)
		{
			_key = "";
			_matrix = "";
			_qrResult = "";
			_notice = "";
		}
	}

	private void PollLogin()
	{
		string key;
		lock (_lock)
		{
			key = _key;
			double nextScan = _nextScan;
			double qrExpires = _qrExpires;
			if (string.IsNullOrEmpty(key) || Now() < nextScan)
			{
				return;
			}
			if (Now() >= qrExpires)
			{
				_key = "";
				_matrix = "";
				_notice = "二维码已过期，请重新登录";
				return;
			}
			_nextScan = Now() + 3.0;
		}
		Post(delegate
		{
			ScanLogin(key);
		});
	}

	private void ScanLogin(string key)
	{
		try
		{
			object obj = Api.Scan(key);
			if (obj is int && (int)obj == 803)
			{
				RefreshAccount(initial: false);
				lock (_lock)
				{
					_key = "";
					_matrix = "";
					_qrResult = "success";
					_notice = "扫码登录成功，歌单已更新";
					return;
				}
			}
			if (obj is int && (int)obj == 801)
			{
				lock (_lock)
				{
					_notice = "请用网易云音乐 App 扫码";
					return;
				}
			}
			if (obj is int && (int)obj == 802)
			{
				lock (_lock)
				{
					_notice = "已扫码，请在手机上确认";
					return;
				}
			}
			if (obj is int && (int)obj == 800)
			{
				lock (_lock)
				{
					_key = "";
					_matrix = "";
					_notice = "二维码已过期，请重新登录";
					return;
				}
			}
			if (string.Equals(obj as string, "missing_cookie"))
			{
				lock (_lock)
				{
					_key = "";
					_matrix = "";
					_notice = "扫码已确认，但网易云未返回登录凭证";
					return;
				}
			}
			lock (_lock)
			{
				_key = "";
				_matrix = "";
				_notice = "网易云登录未通过（状态 " + obj?.ToString() + "）";
			}
		}
		catch (QrVerificationRequired qrVerificationRequired)
		{
			lock (_lock)
			{
				_key = "";
				_matrix = "";
				_qrResult = "verification";
				_notice = qrVerificationRequired.Message;
			}
		}
		catch (Exception ex)
		{
			lock (_lock)
			{
				_notice = "暂时无法查询扫码状态：" + ex.Message;
			}
		}
	}

	public void CookieLogin(string raw)
	{
		raw = (raw ?? "").Trim();
		if (raw.StartsWith("cookie:", StringComparison.OrdinalIgnoreCase))
		{
			raw = raw.Substring(7).Trim();
		}
		if (raw.Length == 0 || raw.Length > 8192 || raw.Any((char ch) => ch < ' ' || ch > '~'))
		{
			lock (_lock)
			{
				_cookieNotice = "Cookie 格式不正确";
				return;
			}
		}
		Session candidate = new Session(Path.Combine(Root, "ncm_session.json"));
		candidate.ReplaceCookies(new Dictionary<string, string>());
		candidate.SetCookie(raw);
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		string[] array = new string[3] { "MUSIC_U", "__csrf", "NMTID" };
		foreach (string key in array)
		{
			if (candidate.Cookies.TryGetValue(key, out var value))
			{
				dictionary[key] = value;
			}
		}
		if (!dictionary.ContainsKey("MUSIC_U"))
		{
			lock (_lock)
			{
				_cookieNotice = "缺少 MUSIC_U，请复制完整的请求 Cookie";
				return;
			}
		}
		candidate.ReplaceCookies(dictionary);
		lock (_lock)
		{
			_cookieNotice = "正在验证 Cookie…";
		}
		Post(delegate
		{
			try
			{
				NeteaseClient neteaseClient = new NeteaseClient(candidate);
				AccountInfo accountInfo = neteaseClient.Account();
				if (string.IsNullOrEmpty(accountInfo.Uid))
				{
					throw new ApiException("account required");
				}
				List<Playlist> lists = null;
				try
				{
					lists = neteaseClient.Playlists(accountInfo.Uid);
				}
				catch
				{
				}
				candidate.Save();
				AcceptSession(candidate, accountInfo, lists);
				lock (_lock)
				{
					_cookieNotice = "登录成功";
				}
			}
			catch (Exception ex)
			{
				lock (_lock)
				{
					_cookieNotice = "Cookie 验证失败，原登录状态已保留（" + ex.Message + "）";
				}
			}
		});
	}

	private void AcceptSession(Session session, AccountInfo account, List<Playlist> lists)
	{
		lock (_lock)
		{
			string account2 = _account;
			Api.Session = session;
			_account = (string.IsNullOrEmpty(account.Nickname) ? "网易云账号" : account.Nickname);
			_lists.Clear();
			if (lists != null)
			{
				_lists.AddRange(lists);
			}
			_key = "";
			_matrix = "";
			_qrResult = "success";
			_notice = ((lists == null) ? "已登录，歌单读取失败，请刷新" : "登录成功");
			if (!string.IsNullOrEmpty(account2) && account2 != _account)
			{
				Player.Close();
				_queueTracks.Clear();
				_index = -1;
				_state = "idle";
				_currentList = "";
				_currentListId = "";
			}
		}
	}

	public void OpenPlaylist(Playlist playlist)
	{
		LoadPlaylist(playlist, play: true);
	}

	/// <summary>Reads a playlist into the queue without starting it, so the player can choose a
	/// song first (the library card's single click).</summary>
	public void LoadPlaylist(Playlist playlist)
	{
		LoadPlaylist(playlist, play: false);
	}

	void LoadPlaylist(Playlist playlist, bool play)
	{
		if (playlist == null)
		{
			return;
		}
		if (!play && Player != null)
		{
			// browsing another playlist replaces the queue: stop what the old queue was playing
			try
			{
				Player.Close();
			}
			catch
			{
			}
		}
		lock (_lock)
		{
			_currentList = playlist.Name;
			_currentListId = playlist.Id;
			_trackFirst = 1;
			_state = "loading";
			_notice = "正在读取歌单…";
			_queueTracks.Clear();
			_index = -1;
			_lyricsRows.Clear();
		}
		Post(delegate
		{
			try
			{
				List<Track> list = Api.Tracks(playlist.Id);
				lock (_lock)
				{
					_queueTracks.Clear();
					_queueTracks.AddRange(list);
					_index = -1;
					_state = "idle";
					_notice = list.Count == 0 ? "歌单中没有歌曲"
						: play ? "" : "已读取歌单，选择歌曲播放";
				}
				if (play && list.Count > 0)
				{
					StartPlay(0);
				}
			}
			catch (Exception ex)
			{
				lock (_lock)
				{
					_state = "idle";
					_notice = "歌单读取失败，请重新选择（" + ex.Message + "）";
				}
			}
		});
	}

	public void Search(string keyword)
	{
		keyword = Util.Clean(keyword).Trim();
		if (keyword.Length > 60)
		{
			keyword = keyword.Substring(0, 60);
		}
		lock (_lock)
		{
			_searchSerial++;
			_searchKeyword = keyword;
			_searchResults.Clear();
			_searchState = ((keyword.Length == 0) ? "empty" : "loading");
		}
		if (keyword.Length == 0)
		{
			return;
		}
		Dictionary<string, string> cookies = Api.Session.Cookies;
		Post(delegate
		{
			try
			{
				List<Track> collection = Api.SearchTracks(keyword, cookies);
				lock (_lock)
				{
					if (_searchKeyword == keyword)
					{
						_searchResults.Clear();
						_searchResults.AddRange(collection);
						_searchState = "ready";
					}
				}
			}
			catch (Exception ex)
			{
				lock (_lock)
				{
					if (_searchKeyword == keyword)
					{
						_searchState = "error";
					}
					_notice = "搜索失败：" + ex.Message;
				}
			}
		});
	}

	public void SearchClipboard()
	{
		string text = "";
		try
		{
			Thread thread = new Thread((ThreadStart)delegate
			{
				text = (Clipboard.ContainsText() ? Clipboard.GetText() : "");
			});
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			thread.Join(1500);
		}
		catch
		{
		}
		if (!string.IsNullOrEmpty(text))
		{
			Search(text);
		}
	}

	public void Daily()
	{
		StartStation("daily");
	}

	public void Fm()
	{
		StartStation("fm");
	}

	private void StartStation(string kind)
	{
		if (!Api.Session.LoggedIn)
		{
			lock (_lock)
			{
				_notice = "请先登录网易云音乐";
				return;
			}
		}
		lock (_lock)
		{
			_notice = ((kind == "daily") ? "正在获取每日推荐…" : "正在获取私人 FM…");
		}
		Dictionary<string, string> cookies = Api.Session.Cookies;
		Post(delegate
		{
			try
			{
				List<Track> list = Api.PersonalizedTracks(kind, cookies);
				if (list.Count == 0)
				{
					lock (_lock)
					{
						_state = "idle";
						_notice = "暂时无法获取歌曲，请稍后重试";
						return;
					}
				}
				lock (_lock)
				{
					_queueTracks.Clear();
					_queueTracks.AddRange(list);
					_index = -1;
					_currentListId = kind;
					_currentList = ((kind == "daily") ? "每日推荐" : "私人 FM");
					_trackFirst = 1;
					_state = "idle";
					_notice = "";
				}
				StartPlay(0);
			}
			catch (Exception ex)
			{
				lock (_lock)
				{
					_state = "idle";
					_notice = "获取失败：" + ex.Message;
				}
			}
		});
	}

	public void RadioDirectory()
	{
		Dictionary<string, string> cookies = Api.Session.Cookies;
		lock (_lock)
		{
			_radioState = "loading";
			_notice = "正在获取电台…";
		}
		Post(delegate
		{
			try
			{
				List<RadioChannel> collection = Api.RadioDirectory(cookies);
				lock (_lock)
				{
					_radios.Clear();
					_radios.AddRange(collection);
					_radioState = "ready";
					_notice = "";
				}
			}
			catch (Exception ex)
			{
				lock (_lock)
				{
					_radioState = "error";
					_notice = "电台读取失败：" + ex.Message;
				}
			}
		});
	}

	public void Podcast(string channelId)
	{
		Dictionary<string, string> cookies = Api.Session.Cookies;
		lock (_lock)
		{
			_podcastState = "loading";
			_podcastChannel = channelId;
			_podcastTracks.Clear();
		}
		Post(delegate
		{
			try
			{
				List<Track> collection = Api.RadioPrograms(channelId, cookies);
				lock (_lock)
				{
					_podcastTracks.Clear();
					_podcastTracks.AddRange(collection);
					_podcastState = "ready";
				}
			}
			catch (Exception ex)
			{
				lock (_lock)
				{
					_podcastState = "error";
					_notice = "播客读取失败：" + ex.Message;
				}
			}
		});
	}

	public void RadioPlay(RadioChannel channel)
	{
		if (channel == null)
		{
			return;
		}
		if (_lockedOut)
		{
			SendCommand("radio_play " + channel.Id);
			return;
		}
		Dictionary<string, string> cookies = Api.Session.Cookies;
		lock (_lock)
		{
			_notice = "正在获取电台节目…";
		}
		Post(delegate
		{
			try
			{
				List<Track> list = Api.RadioPrograms(channel.Id, cookies);
				if (list.Count == 0)
				{
					lock (_lock)
					{
						_notice = "电台没有可播放节目";
						return;
					}
				}
				lock (_lock)
				{
					_queueTracks.Clear();
					_queueTracks.AddRange(list);
					_index = -1;
					_currentListId = "radio_" + channel.Id;
					_currentList = channel.Name;
					_trackFirst = 1;
					_state = "idle";
					_notice = "";
					_podcastChannel = "";
				}
				StartPlay(0);
			}
			catch (Exception ex)
			{
				lock (_lock)
				{
					_notice = "电台读取失败：" + ex.Message;
				}
			}
		});
	}

	public void PodcastPlay(string channelId, string songId)
	{
		int index;
		lock (_lock)
		{
			index = _podcastTracks.FindIndex((Track t) => t.Id == songId);
			if (index < 0)
			{
				return;
			}
			_queueTracks.Clear();
			_queueTracks.AddRange(_podcastTracks);
			_currentListId = "podcast_" + channelId;
			_currentList = "网易云播客";
			_trackFirst = 1;
			_index = -1;
			_podcastChannel = channelId;
		}
		Post(delegate
		{
			StartPlay(index);
		});
	}

	public void PodcastPlay(int index)
	{
		lock (_lock)
		{
			if (index < 0 || index >= _podcastTracks.Count)
			{
				return;
			}
			_queueTracks.Clear();
			_queueTracks.AddRange(_podcastTracks);
			_currentListId = "podcast_" + _podcastChannel;
			_currentList = "网易云播客";
			_trackFirst = 1;
			_index = -1;
		}
		Post(delegate
		{
			StartPlay(index);
		});
	}

	public void LoadRecommendations()
	{
		lock (_lock)
		{
			_recommendState = "loading";
		}
		Post(delegate
		{
			try
			{
				List<Playlist> collection = Api.Recommendations();
				lock (_lock)
				{
					_recommended.Clear();
					_recommended.AddRange(collection);
					_recommendState = "ready";
				}
			}
			catch (Exception ex)
			{
				lock (_lock)
				{
					_recommendState = "error";
					_notice = "推荐读取失败：" + ex.Message;
				}
			}
		});
	}

	private void ResetMix()
	{
		_mixTask = null;
		_mixGame = null;
		_mixMarker = null;
		_mixAttempt = "";
		if (Player != null)
		{
			Player.CancelNext();
		}
	}

	private static bool SameMarker(MixMarker a, MixMarker b)
	{
		if (a != null && b != null && a.Serial == b.Serial && a.List == b.List)
		{
			return a.Index == b.Index;
		}
		return false;
	}

	private void UpdateMix()
	{
		if (Player == null)
		{
			return;
		}
		MixMarker mixMarker = Player.TakeTransition() as MixMarker;
		if (mixMarker != null)
		{
			lock (_lock)
			{
				if (mixMarker.Serial == _seekSerial && mixMarker.List == _currentListId && mixMarker.Index >= 0 && mixMarker.Index < _queueTracks.Count)
				{
					RecordPosition(mixMarker.Index);
					_index = mixMarker.Index;
					_trackFirst = Math.Max(1, Math.Min(_index + 1, Math.Max(1, _queueTracks.Count - 4)));
					_seekSerial++;
					_resumePosition = null;
					_resumeAttempts = 0;
					_seeking = false;
					_lyricsRows.Clear();
					_lyricsSong = "";
				}
				else
				{
					mixMarker = null;
				}
			}
			if (mixMarker != null)
			{
				string song;
				lock (_lock)
				{
					song = ((_index >= 0 && _index < _queueTracks.Count) ? _queueTracks[_index].Id : null);
				}
				if (!string.IsNullOrEmpty(song))
				{
					Post(delegate
					{
						LoadLyrics(song);
					});
				}
			}
		}
		object token = Player.GameWatch.Token;
		if (_mixTask != null && _mixTask.IsCompleted)
		{
			Task<string> mixTask = _mixTask;
			object mixGame = _mixGame;
			MixMarker mixMarker2 = _mixMarker;
			_mixTask = null;
			_mixGame = null;
			_mixMarker = null;
			if (mixGame == token && mixMarker2 != null)
			{
				string text = null;
				try
				{
					text = mixTask.Result;
				}
				catch
				{
				}
				if (!string.IsNullOrEmpty(text))
				{
					MixMarker b;
					lock (_lock)
					{
						b = new MixMarker
						{
							Serial = _seekSerial,
							List = _currentListId,
							Index = NextPosition(manual: false)
						};
					}
					if (SameMarker(mixMarker2, b))
					{
						double duration = 0.0;
						lock (_lock)
						{
							if (mixMarker2.Index >= 0 && mixMarker2.Index < _queueTracks.Count)
							{
								duration = _queueTracks[mixMarker2.Index].Duration;
							}
						}
						Player.PrepareNext(text, token, mixMarker2, duration);
					}
				}
			}
		}
		if (!_automix || token == null || !Player.Active || Player.AwaitingPlay || Player.Outcome.HasValue)
		{
			return;
		}
		string state;
		lock (_lock)
		{
			state = _state;
		}
		if (state != "playing")
		{
			return;
		}
		int num = NextPosition(manual: false);
		if (num < 0)
		{
			return;
		}
		string songId;
		double duration2;
		string currentListId;
		int seekSerial;
		lock (_lock)
		{
			if (_index < 0 || num >= _queueTracks.Count)
			{
				return;
			}
			songId = _queueTracks[num].Id;
			duration2 = _queueTracks[num].Duration;
			currentListId = _currentListId;
			seekSerial = _seekSerial;
		}
		double pos = Player.Status.Pos;
		MixMarker mixMarker3 = new MixMarker
		{
			Serial = seekSerial,
			List = currentListId,
			Index = num
		};
		string text2 = mixMarker3.ToString();
		bool flag = ((duration2 > 0.0) ? (duration2 - pos <= 35.0) : (pos >= 4.0));
		if (_mixTask == null && _mixAttempt != text2 && flag)
		{
			_mixAttempt = text2;
			_mixMarker = mixMarker3;
			_mixGame = token;
			string quality = _quality;
			Dictionary<string, string> cookies = Api.Session.Cookies;
			_mixTask = Task.Run(() => Api.PreferredStream(songId, quality, cookies));
		}
	}

	private void MixTick()
	{
		try
		{
			UpdateMix();
		}
		catch
		{
		}
	}

	public void Play(int index)
	{
		if (_lockedOut)
		{
			DelegateTrack(index);
			return;
		}
		Post(delegate
		{
			StartPlayAt(index, 0.0);
		});
	}

	public void StartPlay(int index)
	{
		if (_lockedOut)
		{
			DelegateTrack(index);
			return;
		}
		StartPlayAt(index, 0.0);
	}

	private void StartPlayAt(int index, double position, bool keepResumeAttempts = false)
	{
		if (_lockedOut)
		{
			return;
		}
		ResetMix();
		if (!CheckGame(prompt: true))
		{
			return;
		}
		Track track;
		object token;
		lock (_lock)
		{
			if (index < 0 || index >= _queueTracks.Count)
			{
				_state = "finished";
				_notice = "歌单已播完或没有更多可播歌曲";
				return;
			}
			track = _queueTracks[index];
			token = Player.GameWatch.Token;
			if (token == null)
			{
				_state = "idle";
				_notice = "请先进入游戏并启用新版播放器";
				return;
			}
			RecordPosition(index);
			_index = index;
			_state = "loading";
			_notice = "正在加载音源…";
			_lyricsRows.Clear();
			_lyricsSong = "";
			_seeking = false;
			_resumePosition = null;
			if (!keepResumeAttempts)
			{
				_resumeAttempts = 0;
			}
		}
		string text;
		try
		{
			text = Api.PreferredStream(track.Id, _quality);
		}
		catch (Exception ex)
		{
			lock (_lock)
			{
				_state = "idle";
				_notice = "音源请求失败：" + ex.Message;
				return;
			}
		}
		if (token != Player.GameWatch.Token)
		{
			lock (_lock)
			{
				_state = "idle";
				_notice = "请先进入游戏并启用新版播放器";
				return;
			}
		}
		if (string.IsNullOrEmpty(text))
		{
			lock (_lock)
			{
				_state = "idle";
				_notice = "当前歌曲没有可用音源（可能需要会员或版权限制）";
				return;
			}
		}
		if (!Player.Start(text, token, position, paused: false, track.Duration))
		{
			lock (_lock)
			{
				_state = "idle";
				_notice = "无法启动音频解码";
				return;
			}
		}
		lock (_lock)
		{
			_state = "playing";
			_notice = "";
			_seeking = false;
		}
		LoadLyrics(track.Id);
	}

	private void LoadLyrics(string song)
	{
		if (!_lyricsEnabled)
		{
			return;
		}
		try
		{
			List<LyricLine> collection = Api.LyricsFor(song);
			lock (_lock)
			{
				if (_index >= 0 && _index < _queueTracks.Count && _queueTracks[_index].Id == song)
				{
					_lyricsRows.Clear();
					_lyricsRows.AddRange(collection);
					_lyricsSong = song;
				}
			}
		}
		catch
		{
		}
	}

	private void RecordPosition(int index)
	{
		_shuffleNext = null;
		_shufflePlayed.Add(index);
		if (_shuffleHistory.Count == 0 || _shuffleHistory[_shuffleHistory.Count - 1] != index)
		{
			_shuffleHistory.Add(index);
			if (_shuffleHistory.Count > 80)
			{
				_shuffleHistory.RemoveAt(0);
			}
		}
	}

	private int NextPosition(bool manual)
	{
		lock (_lock)
		{
			if (_order == "repeat" && !manual && _index >= 0)
			{
				return _index;
			}
			if (_order != "shuffle" || _queueTracks.Count < 2)
			{
				return _index + 1;
			}
			if (!_shuffleNext.HasValue)
			{
				List<int> list = new List<int>();
				for (int i = 0; i < _queueTracks.Count; i++)
				{
					if (i != _index && !_shufflePlayed.Contains(i))
					{
						list.Add(i);
					}
				}
				if (list.Count == 0)
				{
					_shufflePlayed.Clear();
					_shufflePlayed.Add(_index);
					for (int j = 0; j < _queueTracks.Count; j++)
					{
						if (j != _index)
						{
							list.Add(j);
						}
					}
				}
				if (list.Count == 0)
				{
					return _index + 1;
				}
				_shuffleNext = list[new Random().Next(list.Count)];
			}
			return _shuffleNext.Value;
		}
	}

	public void Next()
	{
		if (_lockedOut)
		{
			SendCommand("next");
			return;
		}
		Post(delegate
		{
			StartPlayAt(NextPosition(manual: true), 0.0);
		});
	}

	public void Previous()
	{
		if (_lockedOut)
		{
			SendCommand("prev");
			return;
		}
		Post(delegate
		{
			int index;
			lock (_lock)
			{
				if (_order == "shuffle" && _shuffleHistory.Count > 1)
				{
					_shuffleHistory.RemoveAt(_shuffleHistory.Count - 1);
					index = _shuffleHistory[_shuffleHistory.Count - 1];
				}
				else
				{
					index = Math.Max(0, _index - 1);
				}
			}
			StartPlayAt(index, 0.0);
		});
	}

	public void Toggle()
	{
		if (_lockedOut)
		{
			SendCommand("toggle");
			return;
		}
		lock (_lock)
		{
			if (_state == "playing" || _state == "paused")
			{
				bool flag = _state == "playing";
				if (Player.Pause(flag))
				{
					_state = (flag ? "paused" : "playing");
				}
				return;
			}
		}
		Post(delegate
		{
			int num;
			lock (_lock)
			{
				num = ((_index >= 0) ? _index : 0);
			}
			StartPlayAt((num >= 0) ? num : 0, 0.0);
		});
	}

	public void Seek(double position)
	{
		if (_lockedOut)
		{
			Dictionary<string, string> state = HelperState();
			string listId = Value(state, "list_id");
			if (!string.IsNullOrEmpty(listId))
			{
				SendCommand("seek " + listId + " " + Value(state, "index") + " " + Value(state, "seek_serial") + " " + Number(position));
			}
			return;
		}
		ResetMix();
		lock (_lock)
		{
			if (_state != "playing" && _state != "paused")
			{
				return;
			}
			double num = TrackDuration();
			if (!(num <= 0.0))
			{
				position = Math.Max(0.0, Math.Min(position, num - 0.15));
				if (Player.Seek(position))
				{
					_seekSerial++;
					_seeking = true;
				}
			}
		}
	}

	private double TrackDuration()
	{
		if (_index >= 0 && _index < _queueTracks.Count)
		{
			double duration = _queueTracks[_index].Duration;
			if (duration > 0.0)
			{
				return duration;
			}
		}
		if (Player == null)
		{
			return 0.0;
		}
		return Player.Status.Dur;
	}

	public void StopPlayback()
	{
		if (_lockedOut)
		{
			SendCommand("stop");
			return;
		}
		ResetMix();
		if (Player != null)
		{
			Player.Close();
		}
		lock (_lock)
		{
			_state = "idle";
			_notice = "";
			_lyricsRows.Clear();
			_lyricsSong = "";
			_seeking = false;
			_resumePosition = null;
			_resumeAttempts = 0;
		}
	}

	public void SetVolume(int volume)
	{
		if (_lockedOut)
		{
			SendCommand("vol_set " + volume.ToString(CultureInfo.InvariantCulture));
			return;
		}
		if (Player != null)
		{
			Player.SetVolume(volume);
		}
	}

	public void SetQuality(string quality)
	{
		if (_lockedOut)
		{
			if (Qualities.Contains(quality))
			{
				SendCommand("quality " + quality);
			}
			return;
		}
		if (Qualities.Contains(quality))
		{
			ResetMix();
			lock (_lock)
			{
				_quality = quality;
			}
			WriteSetting("ncm_quality.txt", quality);
		}
	}

	public void SetOrder(string order)
	{
		if (!Orders.Contains(order))
		{
			return;
		}
		if (_lockedOut)
		{
			SendCommand("order " + order);
			return;
		}
		ResetMix();
		lock (_lock)
		{
			_order = order;
			_shufflePlayed.Clear();
			_shuffleNext = null;
			_shuffleHistory.Clear();
			if (_index >= 0)
			{
				RecordPosition(_index);
			}
		}
		WriteSetting("ncm_order.txt", order);
	}

	public void SetAutomix(bool enabled)
	{
		if (_lockedOut)
		{
			SendCommand("automix " + (enabled ? "1" : "0"));
			return;
		}
		lock (_lock)
		{
			_automix = enabled;
		}
		WriteSetting("ncm_automix.txt", enabled ? "1" : "0");
	}

	/// <summary>The decoder the player spawns: chosen once on the settings page, kept in
	/// ncm_ffmpeg.txt beside the manager's other settings.</summary>
	public void SetFfmpeg(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		FfmpegPath = path;
		if (Player != null)
		{
			Player.Executable = path;
		}
		WriteSetting("ncm_ffmpeg.txt", path);
	}

	public void SetLyrics(bool enabled)
	{
		if (_lockedOut)
		{
			SendCommand("lyrics " + (enabled ? "1" : "0"));
			return;
		}
		lock (_lock)
		{
			_lyricsEnabled = enabled;
			_lyricsRows.Clear();
			_lyricsSong = "";
		}
		WriteSetting("ncm_lyrics_enabled.txt", enabled ? "1" : "0");
		if (enabled && _index >= 0 && _index < _queueTracks.Count && (_state == "playing" || _state == "paused"))
		{
			string song = _queueTracks[_index].Id;
			Post(delegate
			{
				LoadLyrics(song);
			});
		}
	}

	private void Recover()
	{
		int index;
		double endedPosition;
		int resumeAttempts;
		lock (_lock)
		{
			index = _index;
			endedPosition = Player.EndedPosition;
			resumeAttempts = _resumeAttempts;
			if (index < 0 || index >= _queueTracks.Count)
			{
				_state = "idle";
				_notice = "";
				return;
			}
			if (resumeAttempts >= 2)
			{
				_state = "idle";
				_notice = "音源连接中断，可以重试当前歌曲或手动切歌";
				return;
			}
			_resumeAttempts = resumeAttempts + 1;
			_state = "buffering";
			_notice = "音源连接中断，正在续播当前歌曲…";
		}
		double duration = TrackDuration();
		if (duration > 0.0 && endedPosition >= duration - Math.Max(1.5, Math.Min(5.0, duration * 0.025)))
		{
			// the stream ended just before its declared duration: advance instead of
			// replaying the last half-second over and over
			Post(delegate
			{
				Next();
			});
			return;
		}
		Thread.Sleep(450 * (resumeAttempts + 1));
		StartPlayAt(index, Math.Max(0.0, endedPosition), keepResumeAttempts: true);
	}

	public void SearchPlay(int index)
	{
		if (_lockedOut)
		{
			DelegateSearchPlay(index);
			return;
		}
		lock (_lock)
		{
			if (index < 0 || index >= _searchResults.Count)
			{
				return;
			}
			_queueTracks.Clear();
			_queueTracks.AddRange(_searchResults);
			_currentList = "搜索结果";
			_currentListId = "search";
			_trackFirst = 1;
			_index = -1;
		}
		Post(delegate
		{
			StartPlayAt(index, 0.0);
		});
	}

	public void SetTrackFirst(int first)
	{
		lock (_lock)
		{
			_trackFirst = Math.Max(1, Math.Min(first, Math.Max(1, _queueTracks.Count - 4)));
		}
	}

	public void SendSms(string entered)
	{
		Match match = Regex.Match((entered ?? "").Trim(), "^(?:\\+(\\d{1,3})\\s*)?(\\d{5,20})$");
		if (!match.Success)
		{
			lock (_lock)
			{
				_smsStage = "";
				_smsNotice = "请输入手机号，例如 13800000000 或 +86 13800000000";
				return;
			}
		}
		string country = (match.Groups[1].Success ? match.Groups[1].Value : "86");
		string phone = match.Groups[2].Value;
		Session candidate = new Session(Path.Combine(Root, "ncm_session.json"));
		candidate.ReplaceCookies(new Dictionary<string, string>());
		PasswordCredentials sms = new PasswordCredentials
		{
			Country = country,
			Phone = phone,
			Session = candidate
		};
		lock (_lock)
		{
			_sms = sms;
			_smsStage = "sending";
			_smsNotice = "正在发送验证码…";
		}
		Post(delegate
		{
			try
			{
				Api.SendSms(candidate, country, phone);
				lock (_lock)
				{
					_smsStage = "code";
					_smsNotice = "验证码已发送，请输入手机收到的验证码";
				}
			}
			catch (Exception ex)
			{
				lock (_lock)
				{
					_smsStage = "";
					_smsNotice = ex.Message;
				}
			}
		});
	}

	public void SubmitSms(string code)
	{
		PasswordCredentials sms;
		lock (_lock)
		{
			sms = _sms;
			if (sms == null || _smsStage != "code")
			{
				_smsNotice = "请先发送短信验证码";
				return;
			}
			if (string.IsNullOrWhiteSpace(code) || !Regex.IsMatch(code.Trim(), "^\\d{4,8}$"))
			{
				_smsNotice = "请输入正确的短信验证码";
				return;
			}
			_smsStage = "checking";
			_smsNotice = "正在登录…";
		}
		string value = code.Trim();
		Post(delegate
		{
			try
			{
				NeteaseClient neteaseClient = new NeteaseClient(sms.Session);
				neteaseClient.ValidateSms(sms.Session, sms.Country, sms.Phone, value);
				AccountInfo accountInfo = neteaseClient.Account();
				if (string.IsNullOrEmpty(accountInfo.Uid))
				{
					throw new ApiException("无法读取账号信息");
				}
				List<Playlist> lists = null;
				try
				{
					lists = neteaseClient.Playlists(accountInfo.Uid);
				}
				catch
				{
				}
				sms.Session.Save();
				AcceptSession(sms.Session, accountInfo, lists);
				lock (_lock)
				{
					_sms = null;
					_smsStage = "";
					_smsNotice = "短信登录成功";
				}
			}
			catch (Exception ex)
			{
				lock (_lock)
				{
					_smsStage = "code";
					_smsNotice = ex.Message;
				}
			}
		});
	}

	private void Publish()
	{
		if (_lockedOut)
		{
			return;
		}
		UpdateCovers();
		string value = "";
		string value2 = "";
		double num = 0.0;
		double num2 = 0.0;
		int num3 = 65;
		string value3 = "";
		string value4 = "";
		string value5 = "";
		string value6 = "";
		int index = -1;
		string text = "";
		string text2 = "";
		string text3 = "";
		string text4 = "";
		string state;
		string account;
		string notice;
		string currentList;
		string currentListId;
		int index2;
		int trackFirst;
		int count;
		bool lyricsEnabled;
		bool automix;
		string quality;
		string order;
		string matrix;
		List<Playlist> list;
		List<Playlist> list2;
		List<Track> list3;
		List<Track> list4;
		List<RadioChannel> list5;
		List<Track> list6;
		List<LyricLine> list7;
		int searchSerial;
		string searchKeyword;
		string searchState;
		string radioState;
		string podcastState;
		string recommendState;
		string podcastChannel;
		Dictionary<string, string> dictionary;
		Dictionary<string, string> dictionary2;
		Dictionary<string, string> dictionary3;
		Dictionary<string, string> dictionary4;
		HashSet<string> hashSet;
		bool flag;
		lock (_lock)
		{
			state = _state;
			account = _account;
			notice = _notice;
			currentList = _currentList;
			currentListId = _currentListId;
			index2 = _index;
			trackFirst = _trackFirst;
			count = _queueTracks.Count;
			lyricsEnabled = _lyricsEnabled;
			automix = _automix;
			quality = _quality;
			order = _order;
			_ = _key;
			matrix = _matrix;
			list = new List<Playlist>(_lists);
			list2 = new List<Playlist>(_recommended);
			list3 = new List<Track>(_queueTracks);
			list4 = new List<Track>(_searchResults);
			list5 = new List<RadioChannel>(_radios);
			list6 = new List<Track>(_podcastTracks);
			list7 = new List<LyricLine>(_lyricsRows);
			searchSerial = _searchSerial;
			searchKeyword = _searchKeyword;
			searchState = _searchState;
			radioState = _radioState;
			podcastState = _podcastState;
			recommendState = _recommendState;
			text2 = _coverPreviewKey;
			text3 = _coverTextureKey;
			text4 = _miniKey;
			podcastChannel = _podcastChannel;
			dictionary = new Dictionary<string, string>(_recommendTiles, StringComparer.Ordinal);
			dictionary2 = new Dictionary<string, string>(_recommendKeys, StringComparer.Ordinal);
			dictionary3 = new Dictionary<string, string>(_listTiles, StringComparer.Ordinal);
			dictionary4 = new Dictionary<string, string>(_listKeys, StringComparer.Ordinal);
			hashSet = new HashSet<string>(_visibleLists, StringComparer.Ordinal);
			if (index2 >= 0 && index2 < list3.Count)
			{
				value = list3[index2].Title;
				value2 = list3[index2].Artist;
				num2 = list3[index2].Duration;
			}
			if (Player != null)
			{
				num3 = Player.Volume;
				num = Player.Status.Pos;
				if (num2 <= 0.0)
				{
					num2 = Player.Status.Dur;
				}
				flag = Player.Active && !Player.AwaitingPlay && !_seeking && TrackDuration() > 0.0;
			}
			else
			{
				flag = false;
			}
			if (lyricsEnabled)
			{
				switch (state)
				{
				case "playing":
				case "paused":
				case "buffering":
				{
					double num4 = num;
					int num5 = -1;
					for (int i = 0; i < list7.Count && list7[i].At <= num4; i++)
					{
						num5 = i;
					}
					index = num5;
					if (num5 < 0)
					{
						break;
					}
					LyricLine lyricLine = list7[num5];
					value3 = lyricLine.Text;
					value6 = lyricLine.Translation;
					value4 = ((num5 > 0) ? list7[num5 - 1].Text : "");
					for (int j = num5 + 1; j < list7.Count; j++)
					{
						if (!string.IsNullOrEmpty(list7[j].Text))
						{
							value5 = list7[j].Text;
							break;
						}
					}
					text = Marks(lyricLine, num4);
					break;
				}
				}
			}
		}
		List<string> list8 = new List<string>
		{
			"time=" + Epoch().ToString(CultureInfo.InvariantCulture),
			"account=" + Util.Clean(account),
			"notice=" + Util.Clean(notice),
			"account_notice=" + Util.Clean(_cookieNotice),
			"state=" + state,
			"qr=" + matrix,
			"playlist=" + Util.Clean(currentList),
			"title=" + Util.Clean(value),
			"artist=" + Util.Clean(value2),
			"index=" + Math.Max(0, index2 + 1),
			"count=" + count,
			"pos=" + Number(num),
			"duration=" + Number(num2),
			"volume=" + num3,
			"lyric=" + Util.Clean(value3),
			"seek_ready=" + (flag ? 1 : 0),
			"seek_serial=" + _seekSerial,
			"lyrics_on=" + (lyricsEnabled ? 1 : 0),
			"quality=" + quality,
			"automix=" + (automix ? 1 : 0),
			"order=" + order,
			"next_lyric=" + Util.Clean(value5),
			"previous_lyric=" + Util.Clean(value4),
			"lyric_index=" + index,
			"lyric_prefix=" + Util.Clean(Prefix(list7, index, num)),
			"lyric_marks=" + text,
			"list_id=" + Util.Clean(currentListId),
			"track_first=" + trackFirst,
			"lyric_translation=" + Util.Clean(value6),
			"cover_key=" + text2,
			"cover_texture_key=" + text3,
			"mini_texture_key=" + text4
		};
		if (lyricsEnabled)
		{
			switch (state)
			{
			case "playing":
			case "paused":
			case "buffering":
				list8.Add("world_timing=1");
				AppendWorldRows(list8, list7, index, num);
				break;
			}
		}
		foreach (Playlist item in list)
		{
			list8.Add("list=" + item.Id + "\t" + Util.Clean(item.Name));
		}
		foreach (Playlist item2 in list)
		{
			if (hashSet.Contains(item2.Id))
			{
				if (dictionary3.TryGetValue(item2.Id, out var value7))
				{
					list8.Add("list_art=" + item2.Id + "\t" + value7);
				}
				if (dictionary4.TryGetValue(item2.Id, out var value8))
				{
					list8.Add("list_key=" + item2.Id + "\t" + value8);
				}
			}
		}
		if (!string.IsNullOrEmpty(recommendState))
		{
			list8.Add("recommend_state=" + recommendState);
		}
		foreach (Playlist item3 in list2)
		{
			dictionary.TryGetValue(item3.Id, out var value9);
			list8.Add("recommend=" + item3.Id + "\t" + Util.Clean(item3.Name) + "\t" + value9);
			if (dictionary2.TryGetValue(item3.Id, out var value10))
			{
				list8.Add("recommend_key=" + item3.Id + "\t" + value10);
			}
		}
		if (!string.IsNullOrEmpty(podcastChannel))
		{
			list8.Add("podcast_id=" + podcastChannel);
		}
		if (!string.IsNullOrEmpty(radioState))
		{
			list8.Add("radio_state=" + radioState);
			foreach (RadioChannel item4 in list5)
			{
				list8.Add("radio=" + item4.Id + "\t" + Util.Clean(item4.Name) + "\t" + Util.Clean(item4.Description));
			}
		}
		if (!string.IsNullOrEmpty(podcastState))
		{
			list8.Add("podcast_state=" + podcastState);
			foreach (Track item5 in list6)
			{
				int num6 = ((item5.Duration > 0.0) ? ((int)item5.Duration) : 0);
				list8.Add("podcast_episode=" + item5.Id + "\t" + Util.Clean(item5.Title) + "\t" + num6);
			}
		}
		if (!string.IsNullOrEmpty(searchState))
		{
			list8.Add("search_state=" + searchState);
			list8.Add("search_serial=" + searchSerial);
			list8.Add("search_keyword=" + Util.Clean(searchKeyword));
			for (int k = 0; k < list4.Count; k++)
			{
				list8.Add("search_result=" + (k + 1) + "\t" + Util.Clean(list4[k].Title) + "\t" + Util.Clean(list4[k].Artist));
			}
		}
		for (int l = Math.Max(0, trackFirst - 1); l < Math.Min(trackFirst + 6, list3.Count); l++)
		{
			string text5 = Util.Clean(list3[l].Title);
			if (string.IsNullOrEmpty(text5))
			{
				text5 = "第 " + (l + 1) + " 首";
			}
			list8.Add("track=" + (l + 1) + "\t" + text5 + "\t");
		}
		try
		{
			Util.AtomicWrite(Path.Combine(Root, "ncm_state.txt"), string.Join("\n", list8) + "\n");
		}
		catch
		{
		}
	}

	private static string Number(double value)
	{
		if (double.IsNaN(value) || double.IsInfinity(value))
		{
			value = 0.0;
		}
		return value.ToString("0.###", CultureInfo.InvariantCulture);
	}

	private static long Epoch()
	{
		return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
	}

	private static string Marks(LyricLine line, double position)
	{
		if (line == null || line.Words == null || line.Words.Count == 0)
		{
			return "";
		}
		List<string> list = new List<string>();
		int num = 0;
		int num2 = 0;
		long val = 0L;
		foreach (LyricWord word in line.Words)
		{
			string text = Util.Clean(word.Text);
			if (text.Length != 0)
			{
				if (num + text.Length > 160)
				{
					text = text.Substring(0, Math.Max(0, 160 - num));
				}
				if (text.Length == 0)
				{
					break;
				}
				num += text.Length;
				num2 += Encoding.UTF8.GetByteCount(text);
				val = Math.Max(val, (long)Math.Round(word.At * 1000.0));
				list.Add(val + ":" + num2);
				if (num >= 160)
				{
					break;
				}
			}
		}
		return string.Join(",", list);
	}

	private static string Prefix(List<LyricLine> rows, int index, double position)
	{
		if (index < 0 || index >= rows.Count)
		{
			return "";
		}
		StringBuilder stringBuilder = new StringBuilder();
		foreach (LyricWord word in rows[index].Words)
		{
			if (word.At <= position)
			{
				stringBuilder.Append(word.Text);
			}
		}
		return Util.Clean(stringBuilder.ToString());
	}

	private static void AppendWorldRows(List<string> rows, List<LyricLine> lines, int index, double position)
	{
		for (int num = index - 1; num >= 0; num--)
		{
			double at = lines[num + 1].At;
			if (position > at + 5.0)
			{
				break;
			}
			string text = lines[num].Text;
			if (!string.IsNullOrEmpty(text))
			{
				rows.Add("world_timed=previous\t" + num + "\t" + Number(at) + "\t" + Util.Clean(text) + "\t" + Util.Clean(lines[num].Translation));
			}
		}
		for (int i = index + 1; i < lines.Count; i++)
		{
			if (!string.IsNullOrEmpty(lines[i].Text))
			{
				rows.Add("world_timed=next\t" + i + "\t" + Number(lines[i].At) + "\t" + Util.Clean(lines[i].Text) + "\t" + Util.Clean(lines[i].Translation));
				break;
			}
		}
		for (int num2 = index - 1; num2 >= 0; num2--)
		{
			double num3 = lines[num2 + 1].At + 2.0;
			if (position >= num3)
			{
				break;
			}
			string text2 = lines[num2].Text;
			if (!string.IsNullOrEmpty(text2))
			{
				rows.Add("world_cue=previous\t" + num2 + "\t" + Util.Clean(text2) + "\t" + Util.Clean(lines[num2].Translation));
				rows.Add("world_expiry=" + num2 + "\t" + Number(num3));
			}
		}
		for (int j = index + 1; j < lines.Count && !(position < lines[j].At - 2.0); j++)
		{
			if (!string.IsNullOrEmpty(lines[j].Text))
			{
				rows.Add("world_cue=next\t" + j + "\t" + Util.Clean(lines[j].Text) + "\t" + Util.Clean(lines[j].Translation));
				break;
			}
		}
	}

	private void PublishSpectrum()
	{
		if (_lockedOut || Player == null)
		{
			return;
		}
		string text = Player.Spectrum();
		string text2 = _seekSerial + "|" + text;
		double num = Now();
		if (!(text2 == _spectrumKey) || (Player.Active && !(num - _spectrumAt < 0.5)))
		{
			try
			{
				Util.AtomicWrite(Path.Combine(Root, "ncm_spectrum.txt"), "time=" + Epoch().ToString(CultureInfo.InvariantCulture) + "\nserial=" + _seekSerial + "\nbands=" + text + "\n");
			}
			catch
			{
				return;
			}
			_spectrumKey = text2;
			_spectrumAt = num;
		}
	}

	private void ProcessCommands()
	{
		string[] files;
		try
		{
			files = Directory.GetFiles(Root, "ncm_cmd_*.txt");
		}
		catch
		{
			return;
		}
		Array.Sort(files, StringComparer.Ordinal);
		List<string> list = new List<string>();
		foreach (string item in files.Take(64))
		{
			try
			{
				string text = File.ReadAllText(item, Encoding.ASCII);
				if (text.Length > 512)
				{
					File.Delete(item);
				}
				else if (text.EndsWith("\n", StringComparison.Ordinal))
				{
					bool num = (DateTime.UtcNow - File.GetLastWriteTimeUtc(item)).TotalSeconds < 45.0;
					File.Delete(item);
					if (num)
					{
						list.Add(text.Trim());
					}
				}
			}
			catch
			{
			}
		}
		int num2 = -1;
		int num3 = -1;
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].StartsWith("playlist ", StringComparison.Ordinal))
			{
				num2 = i;
			}
			if (list[i].StartsWith("track_page ", StringComparison.Ordinal))
			{
				num3 = i;
			}
		}
		for (int j = 0; j < list.Count; j++)
		{
			if ((j < num2 && list[j].StartsWith("playlist ", StringComparison.Ordinal)) || (j < num3 && list[j].StartsWith("track_page ", StringComparison.Ordinal)))
			{
				continue;
			}
			try
			{
				Command(list[j], Now());
			}
			catch
			{
				lock (_lock)
				{
					_notice = "请求失败，请稍后重试";
				}
			}
		}
	}

	public void Command(string text, double now)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		int num = text.IndexOf(' ');
		string text2 = ((num < 0) ? text : text.Substring(0, num));
		string value = ((num < 0) ? "" : text.Substring(num + 1));
		if (text2 == null)
		{
			return;
		}
		switch (text2.Length)
		{
		case 5:
			switch (text2[0])
			{
			case 'l':
				if (text2 == "login")
				{
					BeginQrLogin();
				}
				break;
			case 'd':
				if (text2 == "daily")
				{
					Daily();
				}
				break;
			case 'r':
				if (text2 == "radio")
				{
					RadioDirectory();
				}
				break;
			case 'o':
				if (text2 == "order")
				{
					SetOrder(value);
				}
				break;
			}
			break;
		case 6:
			switch (text2[0])
			{
			case 'c':
				if (text2 == "cancel")
				{
					CancelLogin();
				}
				break;
			case 's':
				if (!(text2 == "search"))
				{
					break;
				}
				try
				{
					Search(Encoding.UTF8.GetString(Hex(value)));
					break;
				}
				catch
				{
					break;
				}
			case 't':
				if (text2 == "toggle")
				{
					Toggle();
				}
				break;
			case 'l':
				if (text2 == "lyrics")
				{
					SetLyrics(value == "1");
				}
				break;
			}
			break;
		case 7:
			switch (text2[0])
			{
			case 'r':
				if (text2 == "refresh")
				{
					RefreshAccount();
				}
				break;
			case 'p':
				if (text2 == "podcast")
				{
					Podcast(value);
				}
				break;
			case 'q':
				if (text2 == "quality")
				{
					SetQuality(value);
				}
				break;
			case 'a':
				if (text2 == "automix")
				{
					SetAutomix(value == "1");
				}
				break;
			case 'v':
			{
				if (text2 == "vol_set" && int.TryParse(value, out var result2) && result2 >= 0 && result2 <= 100)
				{
					SetVolume(result2);
				}
				break;
			}
			}
			break;
		case 8:
			switch (text2[0])
			{
			case 'l':
				if (text2 == "list_art")
				{
					SetVisiblePlaylists(value.Split(new char[3] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries));
				}
				break;
			case 'p':
			{
				if (!(text2 == "playlist"))
				{
					break;
				}
				Playlist playlist;
				lock (_lock)
				{
					playlist = _lists.FirstOrDefault((Playlist p) => p.Id == value) ?? _recommended.FirstOrDefault((Playlist p) => p.Id == value);
				}
				if (playlist != null)
				{
					OpenPlaylist(playlist);
				}
				break;
			}
			}
			break;
		case 10:
			switch (text2[0])
			{
			case 'r':
				if (!(text2 == "radio_play"))
				{
					break;
				}
				lock (_lock)
				{
					RadioChannel radioChannel = _radios.FirstOrDefault((RadioChannel r) => r.Id == value);
					if (radioChannel != null)
					{
						RadioPlay(radioChannel);
					}
					break;
				}
			case 't':
				if (!(text2 == "track_page"))
				{
					if (!(text2 == "track_play"))
					{
						break;
					}
					int num3 = value.IndexOf(' ');
					if (num3 > 0 && value.Substring(0, num3) == _currentListId && int.TryParse(value.Substring(num3 + 1), out var number) && number >= 1 && number <= _queueTracks.Count)
					{
						Post(delegate
						{
							StartPlayAt(number - 1, 0.0);
						});
					}
				}
				else
				{
					int num4 = value.IndexOf(' ');
					if (num4 > 0 && value.Substring(0, num4) == _currentListId && int.TryParse(value.Substring(num4 + 1), out var result5))
					{
						SetTrackFirst(result5);
					}
				}
				break;
			}
			break;
		case 4:
			switch (text2[3])
			{
			case 't':
				if (text2 == "next")
				{
					Next();
				}
				break;
			case 'v':
				if (text2 == "prev")
				{
					Previous();
				}
				break;
			case 'k':
				if (text2 == "seek")
				{
					string[] array = value.Split(' ');
					if (array.Length == 4 && array[0] == _currentListId && array[1] == (_index + 1).ToString(CultureInfo.InvariantCulture) && array[2] == _seekSerial.ToString(CultureInfo.InvariantCulture) && double.TryParse(array[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var result4))
					{
						Seek(result4);
					}
				}
				break;
			case 'p':
				if (text2 == "stop")
				{
					StopPlayback();
				}
				break;
			}
			break;
		case 9:
			if (text2 == "recommend")
			{
				LoadRecommendations();
			}
			break;
		case 2:
			if (text2 == "fm")
			{
				Fm();
			}
			break;
		case 12:
			if (text2 == "podcast_play")
			{
				int num5 = value.IndexOf(' ');
				if (num5 > 0)
				{
					PodcastPlay(value.Substring(0, num5), value.Substring(num5 + 1));
				}
			}
			break;
		case 16:
			if (text2 == "search_clipboard")
			{
				SearchClipboard();
			}
			break;
		case 11:
			if (text2 == "search_play")
			{
				int num2 = value.IndexOf(' ');
				if (num2 > 0 && int.TryParse(value.Substring(num2 + 1), out var result3) && value.Substring(0, num2) == _searchSerial.ToString(CultureInfo.InvariantCulture))
				{
					SearchPlay(result3 - 1);
				}
			}
			break;
		case 3:
		{
			if (text2 == "vol" && int.TryParse(value, out var result))
			{
				SetVolume(Player.Volume + result);
			}
			break;
		}
		case 13:
		case 14:
		case 15:
			break;
		}
	}

	private static byte[] Hex(string text)
	{
		if (string.IsNullOrEmpty(text) || text.Length % 2 != 0)
		{
			return new byte[0];
		}
		byte[] array = new byte[text.Length / 2];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = byte.Parse(text.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		}
		return array;
	}
}
