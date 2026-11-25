namespace Hd2Qd.HD2Music;

public sealed class CoverArt
{
	public string Preview = "";

	public string Tiles = "";

	public byte[] Texture = new byte[0];

	public byte[] Mini = new byte[0];

	public static CoverArt Empty => new CoverArt();
}
