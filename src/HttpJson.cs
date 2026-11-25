using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace Hd2Qd.HD2Music;

internal static class HttpJson
{
	public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

	static HttpJson()
	{
		try
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		}
		catch
		{
		}
	}

	public static string Send(string url, Session session, Dictionary<string, string> form = null, string method = null, int timeoutSeconds = 8)
	{
		Uri uri = new Uri(url);
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(uri);
		httpWebRequest.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
		httpWebRequest.Referer = "https://music.163.com/";
		httpWebRequest.Accept = "application/json, text/plain, */*";
		httpWebRequest.Timeout = timeoutSeconds * 1000;
		httpWebRequest.ReadWriteTimeout = timeoutSeconds * 1000;
		httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		httpWebRequest.AllowAutoRedirect = true;
		httpWebRequest.Method = ((form != null) ? (method ?? "POST") : (method ?? "GET"));
		CookieContainer container = (httpWebRequest.CookieContainer = ((session != null) ? session.Container(uri) : new CookieContainer()));
		if (form != null)
		{
			string s = EncodeForm(form);
			byte[] bytes = Encoding.UTF8.GetBytes(s);
			httpWebRequest.ContentType = "application/x-www-form-urlencoded";
			httpWebRequest.ContentLength = bytes.Length;
			using Stream stream = httpWebRequest.GetRequestStream();
			stream.Write(bytes, 0, bytes.Length);
		}
		try
		{
			using HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
			if (session != null)
			{
				session.Merge(container);
				try
				{
					session.Absorb(httpWebResponse.Headers.GetValues("Set-Cookie"));
				}
				catch
				{
				}
			}
			using Stream stream2 = httpWebResponse.GetResponseStream();
			using StreamReader streamReader = new StreamReader(stream2, Encoding.UTF8);
			return streamReader.ReadToEnd();
		}
		catch (WebException ex)
		{
			string text = null;
			int? num = null;
			if (ex.Response is HttpWebResponse httpWebResponse2)
			{
				try
				{
					using Stream stream3 = httpWebResponse2.GetResponseStream();
					using StreamReader streamReader2 = new StreamReader(stream3, Encoding.UTF8);
					text = streamReader2.ReadToEnd();
				}
				catch
				{
				}
				num = ExtractCode(text);
				throw new ApiException("HTTP " + (int)httpWebResponse2.StatusCode + " " + httpWebResponse2.StatusDescription + " -- " + Trim(text), num, text);
			}
			throw new ApiException("network error for " + url + ": " + ex.Message);
		}
	}

	public static int? ExtractCode(string body)
	{
		if (string.IsNullOrEmpty(body))
		{
			return null;
		}
		try
		{
			Dictionary<string, object> dictionary = Util.Obj(Util.Parse(body));
			if (dictionary != null && dictionary.ContainsKey("code"))
			{
				long num = Util.Long(dictionary, "code", long.MinValue);
				if (num != long.MinValue)
				{
					return (int)num;
				}
			}
		}
		catch
		{
		}
		int num2 = body.IndexOf("\"code\"", StringComparison.Ordinal);
		if (num2 >= 0)
		{
			int num3 = body.IndexOf(':', num2);
			if (num3 > 0)
			{
				int i;
				for (i = num3 + 1; i < body.Length && char.IsWhiteSpace(body[i]); i++)
				{
				}
				int j = i;
				if (j < body.Length && (body[j] == '-' || char.IsDigit(body[j])))
				{
					j++;
				}
				for (; j < body.Length && char.IsDigit(body[j]); j++)
				{
				}
				if (int.TryParse(body.Substring(i, j - i), out var result))
				{
					return result;
				}
			}
		}
		return null;
	}

	public static string EncodeForm(Dictionary<string, string> form)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (KeyValuePair<string, string> item in form)
		{
			if (stringBuilder.Length > 0)
			{
				stringBuilder.Append('&');
			}
			stringBuilder.Append(Uri.EscapeDataString(item.Key)).Append('=').Append(Uri.EscapeDataString(item.Value ?? ""));
		}
		return stringBuilder.ToString().Replace("%20", "+");
	}

	public static string Query(Dictionary<string, string> values)
	{
		if (values == null || values.Count == 0)
		{
			return "";
		}
		return "?" + EncodeForm(values);
	}

	public static string Trim(string text)
	{
		if (text == null)
		{
			return "";
		}
		text = text.Replace('\r', ' ').Replace('\n', ' ');
		if (text.Length <= 240)
		{
			return text;
		}
		return text.Substring(0, 240);
	}

	public static string FormValue(object value)
	{
		if (value == null)
		{
			return "";
		}
		if (value is string)
		{
			return (string)value;
		}
		if (value is bool)
		{
			if (!(bool)value)
			{
				return "false";
			}
			return "true";
		}
		if (value is double num)
		{
			return num.ToString(CultureInfo.InvariantCulture);
		}
		if (value is float num2)
		{
			return num2.ToString(CultureInfo.InvariantCulture);
		}
		if (value is int || value is long || value is short || value is byte)
		{
			return Convert.ToString(value, CultureInfo.InvariantCulture);
		}
		return Util.Serialize(value);
	}
}
