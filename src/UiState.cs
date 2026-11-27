using System.Collections.Generic;

namespace Hd2Qd.HD2Music;

public sealed class UiState
{
	public string Account = "";

	public string Notice = "";

	public bool GameRunning;

	public string State = "idle";

	public string Title = "";

	public string Artist = "";

	public string CoverUrl = "";

	public string PlaylistName = "";

	public string Quality = "standard";

	public string Order = "order";

	public string Lyric = "";

	public string PreviousLyric = "";

	public string NextLyric = "";

	public string Translation = "";

	public string QrMatrix = "";

	public string QrResult = "";

	public string SearchKeyword = "";

	public string SearchState = "";

	public string SearchMore = "";

	public int SearchOffset;

	public string RadioKeyword = "";

	public string PodcastFilter = "";

	public int PodcastFilterSerial;

	public string RecommendMore = "";

	public bool AccountLoading;

	public string LyricPrefix = "";

	public string RadioState = "";

	public string PodcastState = "";

	public string RadioMore = "";

	public string PodcastMore = "";

	public int RadioOffset;

	public int PodcastOffset;

	public string RecommendState = "";

	public string SmsStage = "";

	public string SmsNotice = "";

	public string Key = "";

	public int Index;

	public int Count;

	public int TrackFirst = 1;

	public int QueueRevision;

	public int LyricIndex = -1;

	public int SearchSerial;

	public double Pos;

	public double Duration;

	public int Volume = 65;

	public bool LyricsEnabled = true;

	public bool Automix = true;

	public bool SeekReady;

	public bool Seeking;

	public bool Audition;

	public List<Playlist> Lists = new List<Playlist>();

	public List<Playlist> Recommended = new List<Playlist>();

	public List<Track> Queue = new List<Track>();

	public List<Track> SearchResults = new List<Track>();

	public List<RadioChannel> Radios = new List<RadioChannel>();

	public List<Track> PodcastTracks = new List<Track>();

	/// <summary>The whole lyric sheet, so the panel can list it and not just the current line.</summary>
	public List<LyricLine> LyricRows = new List<LyricLine>();
}
