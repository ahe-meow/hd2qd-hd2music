namespace Hd2Qd.HD2Music;

public sealed class Track
{
	public string Id;

	public string Title = "";

	public string Artist = "";

	public string Album = "";

	public double Duration;

	public string CoverUrl = "";

	public override string ToString()
	{
		return Title + (string.IsNullOrEmpty(Artist) ? "" : (" - " + Artist));
	}
}
