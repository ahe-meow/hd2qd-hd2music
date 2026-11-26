using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Hd2Qd.HD2Music;

internal static class Util
{
	private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer
	{
		MaxJsonLength = int.MaxValue,
		RecursionLimit = 1000
	};

	public static string Root
	{
		get
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HD2Phone");
			Directory.CreateDirectory(text);
			return text;
		}
	}

	public static string SettingsHome => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hd2-mod-cli");

	public static string PluginDir => Path.Combine(SettingsHome, "plugins");

	public static string Clean(object value)
	{
		string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
		StringBuilder stringBuilder = new StringBuilder(text.Length);
		string text2 = text;
		foreach (char c in text2)
		{
			stringBuilder.Append((c < ' ' || c == '\u007f') ? ' ' : c);
		}
		text = stringBuilder.ToString();
		if (text.Length <= 160)
		{
			return text;
		}
		return text.Substring(0, 160);
	}

	public static object Parse(string text)
	{
		return Serializer.DeserializeObject(text);
	}

	public static T Parse<T>(string text)
	{
		return Serializer.Deserialize<T>(text);
	}

	public static string Serialize(object value)
	{
		return Serializer.Serialize(value);
	}

	public static Dictionary<string, object> Obj(object value)
	{
		if (value is Dictionary<string, object> result)
		{
			return result;
		}
		if (value is IDictionary<string, object> dictionary)
		{
			return new Dictionary<string, object>(dictionary);
		}
		return null;
	}

	public static List<object> List(object value)
	{
		List<object> list = new List<object>();
		if (value == null)
		{
			return list;
		}
		if (!(value is IEnumerable enumerable))
		{
			return list;
		}
		foreach (object item in enumerable)
		{
			list.Add(item);
		}
		return list;
	}

	public static string Str(Dictionary<string, object> obj, string key, string fallback = null)
	{
		if (obj == null || !obj.TryGetValue(key, out var value))
		{
			return fallback;
		}
		if (value == null)
		{
			return fallback;
		}
		return Convert.ToString(value, CultureInfo.InvariantCulture);
	}

	public static long Long(Dictionary<string, object> obj, string key, long fallback = 0L)
	{
		if (obj == null || !obj.TryGetValue(key, out var value) || value == null)
		{
			return fallback;
		}
		try
		{
			return Convert.ToInt64(value, CultureInfo.InvariantCulture);
		}
		catch
		{
			return fallback;
		}
	}

	public static double Double(Dictionary<string, object> obj, string key, double fallback = 0.0)
	{
		if (obj == null || !obj.TryGetValue(key, out var value) || value == null)
		{
			return fallback;
		}
		try
		{
			return Convert.ToDouble(value, CultureInfo.InvariantCulture);
		}
		catch
		{
			return fallback;
		}
	}

	public static bool Bool(Dictionary<string, object> obj, string key, bool fallback = false)
	{
		if (obj == null || !obj.TryGetValue(key, out var value) || value == null)
		{
			return fallback;
		}
		if (value is bool)
		{
			return (bool)value;
		}
		try
		{
			return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
		}
		catch
		{
			return fallback;
		}
	}

	public static string IdOf(object value)
	{
		if (value == null)
		{
			return "";
		}
		return Convert.ToString(value, CultureInfo.InvariantCulture);
	}

	public static void AtomicWrite(string path, byte[] data)
	{
		string directoryName = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string text = path + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		try
		{
			File.WriteAllBytes(text, data);
			if (File.Exists(path))
			{
				File.Replace(text, path, null);
			}
			else
			{
				File.Move(text, path);
			}
		}
		finally
		{
			try
			{
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch
			{
			}
		}
	}

	public static void AtomicWrite(string path, string text)
	{
		AtomicWrite(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text ?? ""));
	}

	public static string ReadText(string path)
	{
		try
		{
			return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
		}
		catch
		{
			return null;
		}
	}

	public static string FormatTime(double seconds)
	{
		if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0)
		{
			seconds = 0.0;
		}
		int num = (int)seconds;
		return num / 60 + ":" + (num % 60).ToString("00", CultureInfo.InvariantCulture);
	}
}
