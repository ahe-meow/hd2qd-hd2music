using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Hd2Qd.HD2Music;

public sealed class NeteaseClient
{
	public const string Base = "https://music.163.com";

	public const string QrApi = "https://interface.music.163.com/api/login/qrcode/";

	public static readonly string[] QualityLevels = new string[5] { "standard", "higher", "exhigh", "lossless", "hires" };

	private static readonly Dictionary<int, string> QrVerification = new Dictionary<int, string>
	{
		{ -462, "网易云要求完成安全验证（-462）" },
		{ 8810, "网易云提示当前网络环境存在风险（8810）" },
		{ 8821, "网易云要求切换登录方式（8821）" }
	};

	public Session Session { get; set; }

	public Session QrSession { get; private set; }

	private string Root
	{
		get
		{
			string directoryName = Path.GetDirectoryName(Session.FilePath);
			if (!string.IsNullOrEmpty(directoryName))
			{
				return directoryName;
			}
			return Util.Root;
		}
	}

	public NeteaseClient(Session session)
	{
		Session = session;
	}

	private Session SideSession(string name)
	{
		return new Session(Path.Combine(Root, name));
	}

	private KeyValuePair<int?, Dictionary<string, object>> QrRequest(string path, Dictionary<string, object> extra = null)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string> { { "type", "3" } };
		if (extra != null)
		{
			foreach (KeyValuePair<string, object> item in extra)
			{
				dictionary[item.Key] = HttpJson.FormValue(item.Value);
			}
		}
		Dictionary<string, object> dictionary2;
		try
		{
			dictionary2 = Util.Obj(Util.Parse(HttpJson.Send("https://interface.music.163.com/api/login/qrcode/" + path, QrSession, dictionary, "POST")));
		}
		catch (ApiException ex)
		{
			int? num = ex.Code ?? HttpJson.ExtractCode(ex.Body);
			if (num.HasValue && QrVerification.ContainsKey(num.Value))
			{
				throw new QrVerificationRequired(QrVerification[num.Value]);
			}
			throw;
		}
		if (dictionary2 == null)
		{
			throw new ApiException("invalid QR response");
		}
		int? key = (dictionary2.ContainsKey("code") ? new int?((int)Util.Long(dictionary2, "code", 0L)) : ((int?)null));
		if (key.HasValue && QrVerification.ContainsKey(key.Value))
		{
			throw new QrVerificationRequired(QrVerification[key.Value]);
		}
		return new KeyValuePair<int?, Dictionary<string, object>>(key, dictionary2);
	}

	public string Qr()
	{
		QrSession = SideSession("ncm_pending_login.json");
		QrSession.Cookies.Clear();
		KeyValuePair<int?, Dictionary<string, object>> keyValuePair = QrRequest("unikey");
		string text = Util.Str(keyValuePair.Value, "unikey");
		if (keyValuePair.Key != 200 || string.IsNullOrEmpty(text))
		{
			throw new ApiException("missing QR key");
		}
		return text;
	}

	public object Scan(string key)
	{
		if (QrSession == null)
		{
			throw new ApiException("no QR session");
		}
		KeyValuePair<int?, Dictionary<string, object>> keyValuePair = QrRequest("client/login", new Dictionary<string, object> { { "key", key } });
		int? key2 = keyValuePair.Key;
		if (key2 == 803)
		{
			object value = null;
			keyValuePair.Value.TryGetValue("cookie", out value);
			string text = value as string;
			if (text == null)
			{
				List<object> source = Util.List(value);
				text = string.Join("; ", source.OfType<string>());
			}
			if (!string.IsNullOrEmpty(text))
			{
				QrSession.SetCookie(text);
			}
			if (!QrSession.Cookies.TryGetValue("MUSIC_U", out var _))
			{
				return "missing_cookie";
			}
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			string[] array = new string[3] { "MUSIC_U", "__csrf", "NMTID" };
			foreach (string key3 in array)
			{
				if (QrSession.Cookies.TryGetValue(key3, out var value3))
				{
					dictionary[key3] = value3;
				}
			}
			Session.ReplaceCookies(dictionary);
			Session.Save();
		}
		if (!key2.HasValue)
		{
			return 0;
		}
		return key2.Value;
	}

	public Dictionary<string, object> ApiCall(string path, Dictionary<string, object> payload = null, string method = "GET")
	{
		return ApiCallWith(Session, path, payload, method);
	}

	private Dictionary<string, object> ApiCallWith(Session session, string path, Dictionary<string, object> payload, string method)
	{
		Dictionary<string, object> dictionary = payload ?? new Dictionary<string, object>();
		string text = "";
		try
		{
			string text2;
			if (method == "GET")
			{
				Dictionary<string, string> dictionary2 = new Dictionary<string, string>();
				foreach (KeyValuePair<string, object> item in dictionary)
				{
					dictionary2[item.Key] = HttpJson.FormValue(item.Value);
				}
				text2 = HttpJson.Send("https://music.163.com" + path + HttpJson.Query(dictionary2), session);
			}
			else
			{
				Dictionary<string, string> dictionary3 = new Dictionary<string, string>();
				foreach (KeyValuePair<string, object> item2 in dictionary)
				{
					dictionary3[item2.Key] = HttpJson.FormValue(item2.Value);
				}
				text2 = HttpJson.Send("https://music.163.com" + path, session, dictionary3, "POST");
			}
			Dictionary<string, object> dictionary4 = Util.Obj(Util.Parse(text2));
			if (dictionary4 == null)
			{
				throw new ApiException("invalid JSON from " + path);
			}
			int num = (int)Util.Long(dictionary4, "code", 200L);
			if (num == 200 || !dictionary4.ContainsKey("code"))
			{
				return dictionary4;
			}
			text = "plain endpoint answered code " + num;
		}
		catch (ApiException ex)
		{
			text = ex.Message;
		}
		Dictionary<string, string> form = Weapi.Params(dictionary, session.Csrf);
		string text3 = HttpJson.Send("https://music.163.com" + Weapi.Path(path), session, form, "POST");
		return Util.Obj(Util.Parse(text3)) ?? throw new ApiException(text + "\nweapi fallback returned invalid JSON", null, text3);
	}

	private List<Track> TrackRows(object rows)
	{
		List<Track> list = new List<Track>();
		foreach (object item in Util.List(rows))
		{
			Dictionary<string, object> dictionary = Util.Obj(item);
			if (dictionary == null || !dictionary.TryGetValue("id", out var value))
			{
				continue;
			}
			string text = Util.IdOf(value);
			if (!string.IsNullOrEmpty(text))
			{
				Track track = SongRow(dictionary);
				track.Id = text;
				if (track.Duration <= 0.0 && dictionary.ContainsKey("duration"))
				{
					track.Duration = Util.Double(dictionary, "duration") / 1000.0;
				}
				list.Add(track);
			}
		}
		return list;
	}

	public AccountInfo Account()
	{
		Dictionary<string, object> dictionary = ApiCall("/api/nuser/account/get");
		int num = (int)Util.Long(dictionary, "code", 0L);
		if (num != 0 && num != 200)
		{
			throw new ApiException("account request failed");
		}
		Dictionary<string, object> dictionary2 = Util.Obj(dictionary.ContainsKey("profile") ? dictionary["profile"] : null);
		Dictionary<string, object> dictionary3 = Util.Obj(dictionary.ContainsKey("account") ? dictionary["account"] : null);
		string text = ((dictionary2 != null) ? Util.Str(dictionary2, "userId") : null);
		if (string.IsNullOrEmpty(text) && dictionary3 != null)
		{
			text = Util.Str(dictionary3, "id");
		}
		return new AccountInfo
		{
			Uid = (text ?? ""),
			Nickname = ((dictionary2 != null) ? Util.Clean(Util.Str(dictionary2, "nickname")) : "")
		};
	}

	public List<Playlist> Playlists(string uid)
	{
		List<Playlist> list = new List<Playlist>();
		for (int i = 0; i < 2000; i += 100)
		{
			Dictionary<string, object> dictionary = ApiCall("/api/user/playlist", new Dictionary<string, object>
			{
				{ "uid", uid },
				{ "limit", 100 },
				{ "offset", i }
			});
			if ((int)Util.Long(dictionary, "code", 0L) != 200)
			{
				throw new ApiException("playlist request failed");
			}
			List<object> list2 = Util.List(dictionary.ContainsKey("playlist") ? dictionary["playlist"] : null);
			foreach (object item in list2)
			{
				Dictionary<string, object> dictionary2 = Util.Obj(item);
				if (dictionary2 != null)
				{
					list.Add(new Playlist
					{
						Id = Util.IdOf(dictionary2.ContainsKey("id") ? dictionary2["id"] : null),
						Name = Util.Clean(Util.Str(dictionary2, "name")),
						CoverUrl = (Util.Str(dictionary2, "coverImgUrl") ?? "")
					});
				}
			}
			if (!Util.Bool(dictionary, "more") || list2.Count == 0)
			{
				break;
			}
		}
		return list;
	}

	public List<Track> Tracks(string playlist)
	{
		Dictionary<string, object> dictionary = ApiCall("/api/v6/playlist/detail", new Dictionary<string, object>
		{
			{ "id", playlist },
			{ "n", 1000 },
			{ "s", 8 }
		});
		Dictionary<string, object> dictionary2 = Util.Obj(dictionary.ContainsKey("playlist") ? dictionary["playlist"] : null);
		if (dictionary2 == null)
		{
			throw new ApiException("playlist detail request failed");
		}
		Dictionary<string, Track> dictionary3 = new Dictionary<string, Track>(StringComparer.Ordinal);
		foreach (object item in Util.List(dictionary2.ContainsKey("tracks") ? dictionary2["tracks"] : null))
		{
			Dictionary<string, object> dictionary4 = Util.Obj(item);
			if (dictionary4 != null)
			{
				Track track = SongRow(dictionary4);
				dictionary3[track.Id] = track;
			}
		}
		List<Track> list = new List<Track>();
		foreach (object item2 in Util.List(dictionary2.ContainsKey("trackIds") ? dictionary2["trackIds"] : null))
		{
			Dictionary<string, object> dictionary5 = Util.Obj(item2);
			if (dictionary5 != null)
			{
				string text = Util.IdOf(dictionary5.ContainsKey("id") ? dictionary5["id"] : null);
				if (!dictionary3.TryGetValue(text, out var value))
				{
					value = new Track
					{
						Id = text
					};
				}
				list.Add(value);
			}
		}
		return list;
	}

	public List<Playlist> Recommendations()
	{
		Session session = SideSession("ncm_public_recommendations.json");
		List<Playlist> rows = new List<Playlist>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		Collect("/api/personalized/playlist", new Dictionary<string, object>
		{
			{ "limit", 48 },
			{ "total", true },
			{ "n", 48 }
		}, "result");
		if (rows.Count < 24)
		{
			Random random = new Random();
			Collect("/api/playlist/list", new Dictionary<string, object>
			{
				{ "cat", "全部" },
				{ "order", "hot" },
				{ "limit", 24 },
				{
					"offset",
					(new int[4] { 0, 24, 48, 72 })[random.Next(4)]
				},
				{ "total", true }
			}, "playlists");
		}
		if (rows.Count == 0)
		{
			throw new ApiException("recommendation request returned no playlists");
		}
		return rows;
		void Collect(string endpoint, Dictionary<string, object> parameters, string field)
		{
			try
			{
				Dictionary<string, object> dictionary = ApiCallOn(session, endpoint, parameters, "POST");
				if ((int)Util.Long(dictionary, "code", 0L) != 200)
				{
					return;
				}
				foreach (object item in Util.List(dictionary.ContainsKey(field) ? dictionary[field] : null))
				{
					Dictionary<string, object> dictionary2 = Util.Obj(item);
					if (dictionary2 != null)
					{
						string text = Util.IdOf(dictionary2.ContainsKey("id") ? dictionary2["id"] : null);
						string text2 = Util.Clean(Util.Str(dictionary2, "name"));
						if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(text2) && text.All(char.IsDigit) && !seen.Contains(text))
						{
							seen.Add(text);
							rows.Add(new Playlist
							{
								Id = text,
								Name = ((text2.Length > 120) ? text2.Substring(0, 120) : text2),
								CoverUrl = (Util.Str(dictionary2, "picUrl") ?? Util.Str(dictionary2, "coverImgUrl") ?? "")
							});
							if (rows.Count >= 48)
							{
								break;
							}
						}
					}
				}
			}
			catch
			{
			}
		}
	}

	public List<Track> PersonalizedTracks(string kind, Dictionary<string, string> cookies)
	{
		Session session = SideSession("ncm_personalized_tracks.json");
		session.Cookies.Clear();
		foreach (KeyValuePair<string, string> cookie in cookies)
		{
			session.Cookies[cookie.Key] = cookie.Value;
		}
		string path = ((kind == "daily") ? "/api/v3/discovery/recommend/songs" : "/api/v1/radio/get");
		Dictionary<string, object> dictionary = ApiCallOn(session, path, null, "POST");
		if ((int)Util.Long(dictionary, "code", 0L) != 200)
		{
			throw new ApiException("personalized music request failed");
		}
		object obj = (dictionary.ContainsKey("data") ? dictionary["data"] : null);
		if (kind == "daily")
		{
			Dictionary<string, object> dictionary2 = Util.Obj(obj);
			return TrackRows((dictionary2 != null && dictionary2.ContainsKey("dailySongs")) ? dictionary2["dailySongs"] : null);
		}
		return TrackRows(obj);
	}

	public BrowsePage<Track> SearchTracksPage(string keyword, Dictionary<string, string> cookies, int offset)
	{
		Session session = SideSession("ncm_song_search.json");
		session.Cookies.Clear();
		foreach (KeyValuePair<string, string> cookie in cookies)
		{
			session.Cookies[cookie.Key] = cookie.Value;
		}
		Dictionary<string, object> dictionary = ApiCallOn(session, "/api/cloudsearch/pc", new Dictionary<string, object>
		{
			{ "s", keyword },
			{ "type", 1 },
			{ "limit", 25 },
			{ "offset", offset }
		}, "POST");
		if ((int)Util.Long(dictionary, "code", 0L) != 200)
		{
			throw new ApiException("search request failed");
		}
		Dictionary<string, object> result = Util.Obj(dictionary.ContainsKey("result") ? dictionary["result"] : null);
		var page = new BrowsePage<Track>();
		page.Rows = TrackRows((result != null && result.ContainsKey("songs")) ? result["songs"] : null).Take(25).ToList();
		page.Offset = Math.Max(0, offset) + page.Rows.Count;
		long total = Util.Long(result, "songCount", 0L);
		page.More = page.Rows.Count > 0 && ((total > 0 && page.Offset < total) || page.Rows.Count == 25);
		return page;
	}

	public BrowsePage<Playlist> RecommendationPage(int offset, HashSet<string> exclude)
	{
		Session session = SideSession("ncm_public_recommendations.json");
		Dictionary<string, object> dictionary = ApiCallOn(session, "/api/playlist/list", new Dictionary<string, object>
		{
			{ "cat", "全部" },
			{ "order", "hot" },
			{ "limit", 8 },
			{ "offset", offset },
			{ "total", true }
		}, "POST");
		if ((int)Util.Long(dictionary, "code", 0L) != 200)
		{
			throw new ApiException("playlist page request failed");
		}
		var page = new BrowsePage<Playlist>();
		int next = Math.Max(0, offset);
		foreach (object item in Util.List(dictionary.ContainsKey("playlists") ? dictionary["playlists"] : null))
		{
			Dictionary<string, object> row = Util.Obj(item);
			next++;
			if (row == null)
			{
				continue;
			}
			string id = Util.Str(row, "id", "");
			string name = Util.Clean(Util.Str(row, "name", ""));
			if (string.IsNullOrEmpty(id) || !id.All(char.IsDigit) || string.IsNullOrEmpty(name)
				|| (exclude != null && exclude.Contains(id)))
			{
				continue;
			}
			page.Rows.Add(new Playlist
			{
				Id = id,
				Name = name.Length > 120 ? name.Substring(0, 120) : name,
				CoverUrl = Util.Str(row, "picUrl", Util.Str(row, "coverImgUrl", ""))
			});
			if (exclude != null)
			{
				exclude.Add(id);
			}
			if (page.Rows.Count == 8)
			{
				break;
			}
		}
		page.Offset = next;
		page.More = Util.Bool(dictionary, "more", page.Rows.Count == 8) && page.Rows.Count > 0;
		return page;
	}

	public BrowsePage<RadioChannel> SearchRadios(string keyword, Dictionary<string, string> cookies, int offset)
	{
		if (string.IsNullOrWhiteSpace(keyword))
		{
			return new BrowsePage<RadioChannel> { Offset = offset };
		}
		Session session = SideSession("ncm_radio_directory.json");
		session.Cookies.Clear();
		foreach (KeyValuePair<string, string> cookie in cookies)
		{
			session.Cookies[cookie.Key] = cookie.Value;
		}
		Dictionary<string, object> dictionary = ApiCallOn(session, "/api/search/get/web", new Dictionary<string, object>
		{
			{ "s", keyword },
			{ "type", 1009 },
			{ "limit", 30 },
			{ "offset", offset },
			{ "total", true }
		}, "POST");
		if ((int)Util.Long(dictionary, "code", 0L) != 200)
		{
			throw new ApiException("radio search request failed");
		}
		Dictionary<string, object> result = Util.Obj(dictionary.ContainsKey("result") ? dictionary["result"] : null);
		List<object> raw = Util.List((result != null && result.ContainsKey("djRadios")) ? result["djRadios"] : null).Take(30).ToList();
		BrowsePage<RadioChannel> page = new BrowsePage<RadioChannel>();
		page.Rows = RadioRows(raw);
		page.Offset = offset + raw.Count;
		long total = Util.Long(result, "djRadiosCount", Util.Long(result, "djRadioCount", 0L));
		page.More = raw.Count > 0 && (Util.Bool(result, "hasMore", false) || (total > 0 && page.Offset < total) || raw.Count == 30);
		return page;
	}

	/// <summary>The rows of a radio listing, dropping anything the game could not open: an id that
	/// is not a number, or a channel with no name.</summary>
	private static List<RadioChannel> RadioRows(List<object> raw)
	{
		var list = new List<RadioChannel>();
		foreach (object item in raw)
		{
			Dictionary<string, object> row = Util.Obj(item);
			if (row == null)
			{
				continue;
			}
			string id = Util.Str(row, "id", "");
			string name = Util.Clean(Util.Str(row, "name", ""));
			if (string.IsNullOrEmpty(id) || !id.All(char.IsDigit) || string.IsNullOrEmpty(name))
			{
				continue;
			}
			list.Add(new RadioChannel
			{
				Id = id,
				Name = name,
				Description = Util.Clean(Util.Str(row, "desc", Util.Str(row, "category", "")))
			});
		}
		return list;
	}

	public List<Track> SearchTracks(string keyword, Dictionary<string, string> cookies)
	{
		Session session = SideSession("ncm_song_search.json");
		session.Cookies.Clear();
		foreach (KeyValuePair<string, string> cookie in cookies)
		{
			session.Cookies[cookie.Key] = cookie.Value;
		}
		Dictionary<string, object> dictionary = ApiCallOn(session, "/api/cloudsearch/pc", new Dictionary<string, object>
		{
			{ "s", keyword },
			{ "type", 1 },
			{ "limit", 25 },
			{ "offset", 0 }
		}, "POST");
		if ((int)Util.Long(dictionary, "code", 0L) != 200)
		{
			throw new ApiException("search request failed");
		}
		Dictionary<string, object> dictionary2 = Util.Obj(dictionary.ContainsKey("result") ? dictionary["result"] : null);
		object rows = ((dictionary2 != null && dictionary2.ContainsKey("songs")) ? dictionary2["songs"] : null);
		return TrackRows(rows).Take(25).ToList();
	}

	public BrowsePage<RadioChannel> RadioDirectory(Dictionary<string, string> cookies, int offset)
	{
		Session session = SideSession("ncm_radio_directory.json");
		session.Cookies.Clear();
		foreach (KeyValuePair<string, string> cookie in cookies)
		{
			session.Cookies[cookie.Key] = cookie.Value;
		}
		Dictionary<string, object> dictionary = ApiCallOn(session, "/api/djradio/hot/v1", new Dictionary<string, object>
		{
			{ "limit", 20 },
			{ "offset", offset }
		}, "POST");
		if ((int)Util.Long(dictionary, "code", 0L) != 200)
		{
			throw new ApiException("radio directory request failed");
		}
		List<object> raw = Util.List(dictionary.ContainsKey("djRadios") ? dictionary["djRadios"] : null).Take(20).ToList();
		BrowsePage<RadioChannel> page = new BrowsePage<RadioChannel>();
		page.Rows = RadioRows(raw);
		page.Offset = offset + raw.Count;
		page.More = raw.Count > 0 && Util.Bool(dictionary, "more", raw.Count == 20);
		return page;
	}

	public BrowsePage<Track> RadioPrograms(string identifier, Dictionary<string, string> cookies, int offset)
	{
		Session session = SideSession("ncm_radio_programs.json");
		session.Cookies.Clear();
		foreach (KeyValuePair<string, string> cookie in cookies)
		{
			session.Cookies[cookie.Key] = cookie.Value;
		}
		Dictionary<string, object> dictionary = ApiCallOn(session, "/api/dj/program/byradio", new Dictionary<string, object>
		{
			{ "radioId", identifier },
			{ "limit", 20 },
			{ "offset", offset },
			{ "asc", false }
		}, "POST");
		if ((int)Util.Long(dictionary, "code", 0L) != 200)
		{
			throw new ApiException("radio programs request failed");
		}
		List<object> raw = Util.List(dictionary.ContainsKey("programs") ? dictionary["programs"] : null).Take(20).ToList();
		BrowsePage<Track> page = new BrowsePage<Track>();
		foreach (object item in raw)
		{
			Dictionary<string, object> dictionary2 = Util.Obj(item);
			if (dictionary2 == null)
			{
				continue;
			}
			Dictionary<string, object> dictionary3 = Util.Obj(dictionary2.ContainsKey("mainSong") ? dictionary2["mainSong"] : null);
			if (dictionary3 == null)
			{
				continue;
			}
			Track track = SongRow(dictionary3);
			if (!string.IsNullOrEmpty(track.Id))
			{
				track.Title = Util.Clean(Util.Str(dictionary2, "name")) ?? track.Title;
				track.CoverUrl = Util.Str(dictionary2, "coverUrl") ?? track.CoverUrl;
				Dictionary<string, object> dictionary4 = Util.Obj(dictionary2.ContainsKey("dj") ? dictionary2["dj"] : null);
				if (dictionary4 != null)
				{
					track.Artist = Util.Clean(Util.Str(dictionary4, "nickname")) ?? track.Artist;
				}
				double num = Util.Double(dictionary2, "duration") / 1000.0;
				if (num > 0.0)
				{
					track.Duration = num;
				}
				page.Rows.Add(track);
			}
		}
		page.Offset = offset + raw.Count;
		page.More = raw.Count > 0 && Util.Bool(dictionary, "more", raw.Count == 20);
		return page;
	}

	/// <summary>Pages until the filtered title shows up: the game filters only what it was handed,
	/// so a keyword that matches nothing on the first page would otherwise look like an empty show.
	/// Three pages is the ceiling — past that the listener is better served by a narrower word.</summary>
	public BrowsePage<Track> SearchProgramPages(string identifier, Dictionary<string, string> cookies, int offset, string keyword)
	{
		string terms = (keyword ?? "").Trim().ToLowerInvariant();
		BrowsePage<Track> page = new BrowsePage<Track> { Offset = offset };
		for (int attempt = 0; attempt < 3; attempt++)
		{
			int before = page.Offset;
			BrowsePage<Track> chunk = RadioPrograms(identifier, cookies, before);
			page.Rows.AddRange(chunk.Rows);
			page.Offset = chunk.Offset;
			bool hit = terms.Length == 0 || chunk.Rows.Any(t => (t.Title ?? "").ToLowerInvariant().Contains(terms));
			if (!chunk.More || chunk.Offset <= before || hit)
			{
				page.More = chunk.More && chunk.Offset > before;
				return page;
			}
		}
		page.More = false;
		return page;
	}

	public string Stream(string song, string level = "standard", Session session = null)
	{
		long result;
		object item = (long.TryParse(song, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? ((object)result) : song);
		Dictionary<string, object> dictionary = ApiCallOn(session ?? Session, "/api/song/enhance/player/url/v1", new Dictionary<string, object>
		{
			{
				"ids",
				Util.Serialize(new List<object> { item })
			},
			{ "level", level },
			{
				"encodeType",
				(level == "lossless" || level == "hires") ? "flac" : "mp3"
			}
		}, "POST");
		List<object> list = Util.List(dictionary.ContainsKey("data") ? dictionary["data"] : null);
		Dictionary<string, object> dictionary2 = ((list.Count > 0) ? Util.Obj(list[0]) : null);
		if (dictionary2 == null)
		{
			return null;
		}
		if ((int)Util.Long(dictionary2, "code", 0L) != 200)
		{
			return null;
		}
		string text = Util.Str(dictionary2, "url");
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		if (dictionary2.ContainsKey("freeTrialInfo") && dictionary2["freeTrialInfo"] != null)
		{
			return null;
		}
		if (!Uri.TryCreate(text, UriKind.Absolute, out var result2))
		{
			return null;
		}
		if (result2.Scheme != "http" && result2.Scheme != "https")
		{
			return null;
		}
		if (!string.IsNullOrEmpty(result2.UserInfo))
		{
			return null;
		}
		string host = result2.Host;
		if (!(host == "music.163.com") && !host.EndsWith(".music.163.com", StringComparison.OrdinalIgnoreCase) && !(host == "music.126.net") && !host.EndsWith(".music.126.net", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		return text;
	}

	public string PreferredStream(string song, string level, Dictionary<string, string> cookies = null)
	{
		Session session = null;
		if (cookies != null)
		{
			session = SideSession("ncm_prefetch_session.json");
			session.Cookies.Clear();
			foreach (KeyValuePair<string, string> cookie in cookies)
			{
				session.Cookies[cookie.Key] = cookie.Value;
			}
		}
		string text = Stream(song, level, session);
		if (text == null && level != "standard")
		{
			text = Stream(song, "standard", session);
		}
		return text;
	}

	public List<LyricLine> LyricsFor(string song)
	{
		Session session = SideSession("ncm_public_lyrics.json");
		Dictionary<string, object> dictionary;
		try
		{
			dictionary = ApiCallOn(session, "/api/song/lyric/v1", new Dictionary<string, object>
			{
				{ "id", song },
				{ "lv", 0 },
				{ "yv", 0 },
				{ "tv", 0 },
				{ "ytv", 0 }
			});
		}
		catch
		{
			dictionary = new Dictionary<string, object>();
		}
		Dictionary<string, object> dictionary2 = Util.Obj(dictionary.ContainsKey("yrc") ? dictionary["yrc"] : null);
		Dictionary<string, object> dictionary3 = Util.Obj(dictionary.ContainsKey("lrc") ? dictionary["lrc"] : null);
		string obj2 = ((dictionary2 != null) ? Util.Str(dictionary2, "lyric") : null);
		string text = ((dictionary3 != null) ? Util.Str(dictionary3, "lyric") : null);
		List<LyricLine> list = Lyrics.WordLyrics(obj2);
		if (string.IsNullOrEmpty(text) && list.Count == 0)
		{
			Dictionary<string, object> dictionary4 = ApiCallOn(session, "/api/song/lyric", new Dictionary<string, object>
			{
				{ "id", song },
				{ "lv", -1 },
				{ "tv", -1 }
			});
			Dictionary<string, object> dictionary5 = Util.Obj(dictionary4.ContainsKey("lrc") ? dictionary4["lrc"] : null);
			text = ((dictionary5 != null) ? Util.Str(dictionary5, "lyric") : null);
		}
		Dictionary<string, object> dictionary6 = Util.Obj(dictionary.ContainsKey("ytlrc") ? dictionary["ytlrc"] : null);
		Dictionary<string, object> dictionary7 = Util.Obj(dictionary.ContainsKey("tlyric") ? dictionary["tlyric"] : null);
		string text2 = ((dictionary6 != null) ? Util.Str(dictionary6, "lyric") : null);
		if (string.IsNullOrEmpty(text2) && dictionary7 != null)
		{
			text2 = Util.Str(dictionary7, "lyric");
		}
		return Lyrics.Build(obj2, text, text2);
	}

	public Dictionary<string, Track> TrackNames(IEnumerable<string> ids)
	{
		Session session = SideSession("ncm_public_lyrics.json");
		List<object> list = new List<object>();
		foreach (string id in ids)
		{
			list.Add(long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? ((object)result) : id);
		}
		Dictionary<string, object> dictionary = ApiCallOn(session, "/api/v3/song/detail", new Dictionary<string, object> { 
		{
			"c",
			Util.Serialize(list)
		} });
		Dictionary<string, Track> dictionary2 = new Dictionary<string, Track>(StringComparer.Ordinal);
		foreach (object item in Util.List(dictionary.ContainsKey("songs") ? dictionary["songs"] : null))
		{
			Dictionary<string, object> dictionary3 = Util.Obj(item);
			if (dictionary3 != null)
			{
				Track track = SongRow(dictionary3);
				if (!string.IsNullOrEmpty(track.Id))
				{
					dictionary2[track.Id] = track;
				}
			}
		}
		return dictionary2;
	}

	private Dictionary<string, object> ApiCallOn(Session session, string path, Dictionary<string, object> payload, string method = "GET")
	{
		return ApiCallWith(session, path, payload, method);
	}

	public static Track SongRow(Dictionary<string, object> song)
	{
		if (song == null)
		{
			return new Track();
		}
		Dictionary<string, object> dictionary = Util.Obj(song.ContainsKey("al") ? song["al"] : (song.ContainsKey("album") ? song["album"] : null));
		List<object> list = Util.List(song.ContainsKey("ar") ? song["ar"] : (song.ContainsKey("artists") ? song["artists"] : null));
		List<string> list2 = new List<string>();
		foreach (object item in list)
		{
			Dictionary<string, object> dictionary2 = Util.Obj(item);
			if (dictionary2 != null)
			{
				string text = Util.Str(dictionary2, "name");
				if (!string.IsNullOrEmpty(text))
				{
					list2.Add(text);
				}
			}
		}
		double num = Util.Double(song, "dt") / 1000.0;
		if (num <= 0.0)
		{
			num = Util.Double(song, "duration") / 1000.0;
		}
		string text2 = ((dictionary != null) ? Util.Str(dictionary, "picUrl") : null);
		return new Track
		{
			Id = Util.IdOf(song.ContainsKey("id") ? song["id"] : null),
			Title = (Util.Str(song, "name") ?? ""),
			Artist = string.Join(" / ", list2),
			Album = ((dictionary != null) ? (Util.Str(dictionary, "name") ?? "") : ""),
			Duration = num,
			CoverUrl = (text2 ?? "")
		};
	}

	public void SendSms(Session candidate, string country, string phone)
	{
		SmsRequest(candidate, "/api/sms/captcha/sent", new Dictionary<string, object>
		{
			{ "ctcode", country },
			{ "cellphone", phone },
			{ "secrete", "music_middleuser_pclogin" }
		}, "发送验证码失败，请检查号码或稍后重试");
	}

	public AccountInfo ValidateSms(Session candidate, string country, string phone, string code)
	{
		SmsRequest(candidate, "/api/w/login/cellphone", new Dictionary<string, object>
		{
			{ "type", "1" },
			{ "https", "true" },
			{ "phone", phone },
			{ "countrycode", country },
			{ "captcha", code },
			{ "remember", "true" },
			{ "secureCaptcha", "" }
		}, "短信登录失败，请检查验证码是否正确或过期");
		if (!candidate.LoggedIn)
		{
			throw new SmsLoginError("网易云未返回登录凭证，请尝试官网登录");
		}
		return new AccountInfo();
	}

	private void SmsRequest(Session candidate, string path, Dictionary<string, object> data, string failure)
	{
		string text;
		try
		{
			Dictionary<string, string> form = Weapi.Params(data, candidate.Csrf);
			text = HttpJson.Send("https://music.163.com" + Weapi.Path(path), candidate, form, "POST");
		}
		catch (ApiException ex)
		{
			int? code = ex.Code;
			if (code == -460 || (code.HasValue && QrVerification.ContainsKey(code.Value)))
			{
				throw new SmsLoginError("网易云要求安全验证（" + code.Value + "），请在官网登录");
			}
			throw new SmsLoginError(failure + (code.HasValue ? ("（" + code.Value + "）") : ""));
		}
		int num = (int)Util.Long(Util.Obj(Util.Parse(text)), "code", 0L);
		switch (num)
		{
		case 200:
			break;
		default:
			if (!QrVerification.ContainsKey(num))
			{
				throw new SmsLoginError(failure + ((num != 0) ? ("（" + num + "）") : ""));
			}
			goto case -460;
		case -460:
			throw new SmsLoginError("网易云要求安全验证（" + num + "），请在官网登录");
		}
	}

	public void SaveSession()
	{
		Session.Save();
	}
}
