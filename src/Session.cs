using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Hd2Qd.HD2Music;

public sealed class Session
{
	public string FilePath { get; private set; }

	public Dictionary<string, string> Cookies { get; private set; }

	public string Csrf
	{
		get
		{
			if (!Cookies.TryGetValue("__csrf", out var value))
			{
				return "";
			}
			return value;
		}
	}

	public string Header
	{
		get
		{
			StringBuilder stringBuilder = new StringBuilder();
			foreach (KeyValuePair<string, string> cookie in Cookies)
			{
				if (stringBuilder.Length > 0)
				{
					stringBuilder.Append("; ");
				}
				stringBuilder.Append(cookie.Key).Append('=').Append(cookie.Value);
			}
			return stringBuilder.ToString();
		}
	}

	public bool LoggedIn
	{
		get
		{
			if (!Cookies.ContainsKey("MUSIC_U"))
			{
				return Cookies.ContainsKey("MUSIC_A");
			}
			return true;
		}
	}

	public Session(string path, string cookie = null)
	{
		FilePath = path;
		Cookies = new Dictionary<string, string>(StringComparer.Ordinal);
		if (!string.IsNullOrEmpty(cookie))
		{
			SetCookie(cookie);
		}
		else
		{
			if (!File.Exists(path))
			{
				return;
			}
			try
			{
				Dictionary<string, object> dictionary = Util.Obj(Util.Parse(File.ReadAllText(path, Encoding.UTF8)));
				Dictionary<string, object> dictionary2 = Util.Obj((dictionary == null) ? null : (dictionary.ContainsKey("cookies") ? dictionary["cookies"] : null));
				if (dictionary2 == null)
				{
					return;
				}
				foreach (KeyValuePair<string, object> item in dictionary2)
				{
					if (item.Value != null)
					{
						Cookies[item.Key] = Convert.ToString(item.Value);
					}
				}
			}
			catch
			{
				Cookies.Clear();
			}
		}
	}

	public void SetCookie(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		string[] array = text.Replace('\n', ';').Split(';');
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].Trim();
			if (text2.Length != 0)
			{
				int num = text2.IndexOf('=');
				if (num > 0)
				{
					Cookies[text2.Substring(0, num).Trim()] = text2.Substring(num + 1).Trim();
				}
			}
		}
	}

	public Session Clone(string path)
	{
		Session session = new Session(path);
		session.Cookies.Clear();
		foreach (KeyValuePair<string, string> cookie in Cookies)
		{
			session.Cookies[cookie.Key] = cookie.Value;
		}
		return session;
	}

	public void Save()
	{
		try
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			foreach (KeyValuePair<string, string> cookie in Cookies)
			{
				dictionary[cookie.Key] = cookie.Value;
			}
			Dictionary<string, object> value = new Dictionary<string, object> { { "cookies", dictionary } };
			Util.AtomicWrite(FilePath, Util.Serialize(value));
		}
		catch
		{
		}
	}

	public void ReplaceCookies(Dictionary<string, string> cookies)
	{
		Cookies.Clear();
		if (cookies == null)
		{
			return;
		}
		foreach (KeyValuePair<string, string> cookie in cookies)
		{
			Cookies[cookie.Key] = cookie.Value;
		}
	}

	public void Absorb(IEnumerable<string> setCookieHeaders)
	{
		if (setCookieHeaders == null)
		{
			return;
		}
		foreach (string setCookieHeader in setCookieHeaders)
		{
			if (string.IsNullOrEmpty(setCookieHeader))
			{
				continue;
			}
			string text = setCookieHeader.Split(';')[0];
			int num = text.IndexOf('=');
			if (num <= 0)
			{
				continue;
			}
			string text2 = text.Substring(0, num).Trim();
			string text3 = text.Substring(num + 1).Trim();
			if (text3.Length != 0)
			{
				switch (text2)
				{
				case "MUSIC_U":
				case "MUSIC_A":
				case "__csrf":
				case "NMTID":
					Cookies[text2] = text3;
					break;
				}
			}
		}
	}

	public CookieContainer Container(Uri uri)
	{
		CookieContainer cookieContainer = new CookieContainer();
		foreach (KeyValuePair<string, string> cookie in Cookies)
		{
			try
			{
				cookieContainer.Add(new Cookie(cookie.Key, cookie.Value, "/", ".music.163.com"));
			}
			catch
			{
			}
		}
		return cookieContainer;
	}

	public void Merge(CookieContainer container)
	{
		try
		{
			foreach (Cookie cookie3 in container.GetCookies(new Uri("https://music.163.com/")))
			{
				if (!string.IsNullOrEmpty(cookie3.Value))
				{
					Cookies[cookie3.Name] = cookie3.Value;
				}
			}
			foreach (Cookie cookie4 in container.GetCookies(new Uri("https://interface.music.163.com/")))
			{
				if (!string.IsNullOrEmpty(cookie4.Value))
				{
					Cookies[cookie4.Name] = cookie4.Value;
				}
			}
		}
		catch
		{
		}
	}
}
