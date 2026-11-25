using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hd2Qd.HD2Music;

internal static class Lyrics
{
	private static readonly Regex Mark = new Regex("\\[(\\d{1,3}):(\\d{2})(?:[.:](\\d{1,3}))?\\]", RegexOptions.Compiled);

	private static readonly Regex Offset = new Regex("^\\[offset:([+-]?\\d+)\\]", RegexOptions.Multiline | RegexOptions.Compiled);

	private static readonly Regex Header = new Regex("^\\[(\\d+),(\\d+)\\]", RegexOptions.Compiled);

	private static readonly Regex Word = new Regex("\\((\\d+),\\d+,\\d+\\)(.*?)(?=\\(\\d+,\\d+,\\d+\\)|$)", RegexOptions.Compiled);

	public static List<KeyValuePair<double, string>> Timed(string text)
	{
		SortedDictionary<double, string> sortedDictionary = new SortedDictionary<double, string>();
		if (string.IsNullOrEmpty(text))
		{
			return new List<KeyValuePair<double, string>>();
		}
		double result = 0.0;
		Match match = Offset.Match(text);
		if (match.Success)
		{
			double.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
		}
		result /= 1000.0;
		string[] array = text.Replace("\r\n", "\n").Split('\n');
		foreach (string text2 in array)
		{
			MatchCollection matchCollection = Mark.Matches(text2);
			if (matchCollection.Count == 0)
			{
				continue;
			}
			string value = Util.Clean(text2.Substring(matchCollection[matchCollection.Count - 1].Index + matchCollection[matchCollection.Count - 1].Length)).Trim();
			foreach (Match item in matchCollection)
			{
				int num = int.Parse(item.Groups[1].Value, CultureInfo.InvariantCulture);
				int num2 = int.Parse(item.Groups[2].Value, CultureInfo.InvariantCulture);
				string value2 = item.Groups[3].Value;
				double num3 = ((!string.IsNullOrEmpty(value2)) ? int.Parse(value2.PadRight(3, '0').Substring(0, 3), CultureInfo.InvariantCulture) : 0);
				double key = Math.Max(0.0, (double)(num * 60 + num2) + num3 / 1000.0 + result);
				sortedDictionary[key] = value;
			}
		}
		return new List<KeyValuePair<double, string>>(sortedDictionary);
	}

	public static List<LyricLine> WordLyrics(string text)
	{
		List<LyricLine> list = new List<LyricLine>();
		if (string.IsNullOrEmpty(text))
		{
			return list;
		}
		string[] array = text.Replace("\r\n", "\n").Split('\n');
		foreach (string text2 in array)
		{
			Match match = Header.Match(text2);
			if (!match.Success)
			{
				continue;
			}
			List<LyricWord> list2 = new List<LyricWord>();
			foreach (Match item in Word.Matches(text2.Substring(match.Index + match.Length)))
			{
				if (double.TryParse(item.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
				{
					list2.Add(new LyricWord
					{
						At = result / 1000.0,
						Text = Util.Clean(item.Groups[2].Value)
					});
				}
			}
			if (list2.Count <= 0)
			{
				continue;
			}
			StringBuilder stringBuilder = new StringBuilder();
			foreach (LyricWord item2 in list2)
			{
				stringBuilder.Append(item2.Text);
			}
			list.Add(new LyricLine
			{
				At = (double)int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) / 1000.0,
				Text = Util.Clean(stringBuilder.ToString()),
				Words = list2
			});
		}
		list.Sort((LyricLine a, LyricLine b) => a.At.CompareTo(b.At));
		return list;
	}

	public static List<LyricLine> Build(string wordText, string text, string translationText)
	{
		List<LyricLine> list = WordLyrics(wordText);
		List<KeyValuePair<double, string>> rows = Timed(translationText);
		List<LyricLine> list2 = new List<LyricLine>();
		if (list.Count > 0)
		{
			foreach (LyricLine item in list)
			{
				item.Translation = Nearest(rows, item.At);
				list2.Add(item);
			}
			return list2;
		}
		foreach (KeyValuePair<double, string> item2 in Timed(text))
		{
			list2.Add(new LyricLine
			{
				At = item2.Key,
				Text = item2.Value,
				Translation = Nearest(rows, item2.Key)
			});
		}
		return list2;
	}

	private static string Nearest(List<KeyValuePair<double, string>> rows, double at)
	{
		if (rows == null || rows.Count == 0)
		{
			return "";
		}
		double num = double.MaxValue;
		string result = "";
		foreach (KeyValuePair<double, string> row in rows)
		{
			double num2 = Math.Abs(row.Key - at);
			if (num2 < num)
			{
				num = num2;
				result = row.Value;
			}
		}
		if (!(num <= 0.32))
		{
			return "";
		}
		return result;
	}

	public static int IndexAt(List<LyricLine> rows, double position)
	{
		int result = -1;
		for (int i = 0; i < rows.Count && rows[i].At <= position; i++)
		{
			result = i;
		}
		return result;
	}
}
