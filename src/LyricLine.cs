using System.Collections.Generic;

namespace Hd2Qd.HD2Music;

public sealed class LyricLine
{
	public double At;

	public string Text = "";

	public List<LyricWord> Words = new List<LyricWord>();

	public string Translation = "";
}
