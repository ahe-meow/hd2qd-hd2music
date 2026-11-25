using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Hd2Qd.HD2Music;

internal static class CoverService
{
	private sealed class Block
	{
		public long Variance;

		public int Left;

		public int Top;

		public int Right;

		public int Bottom;

		public int R;

		public int G;

		public int B;
	}

	private const int MaxImageBytes = 786432;

	public static CoverArt Load(string url, string ffmpeg, int size, bool thumbnail)
	{
		try
		{
			if (!TryImageUri(url, out var uri))
			{
				return CoverArt.Empty;
			}
			byte[] array = Download(uri, thumbnail);
			if (array == null)
			{
				return CoverArt.Empty;
			}
			int side = (thumbnail ? 200 : 360);
			byte[] array2 = Decode(ffmpeg, array, side);
			if (array2 == null)
			{
				return CoverArt.Empty;
			}
			if (thumbnail)
			{
				return new CoverArt
				{
					Tiles = Tiles(Sample(array2, side, size), size, 24),
					Texture = TexturePacket(array2, side),
					Mini = new byte[0]
				};
			}
			byte[] pixels = Sample(array2, side, 80);
			return new CoverArt
			{
				Preview = Hex(Sample(array2, side, 72)),
				Tiles = Tiles(Sample(array2, side, size), size, 900),
				Texture = TexturePacket(array2, side),
				Mini = TexturePacket(pixels, 80)
			};
		}
		catch
		{
			return CoverArt.Empty;
		}
	}

	private static bool TryImageUri(string url, out Uri uri)
	{
		uri = null;
		if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var result))
		{
			return false;
		}
		if (result.Scheme != "http" && result.Scheme != "https")
		{
			return false;
		}
		if (!string.IsNullOrEmpty(result.UserInfo))
		{
			return false;
		}
		string host = result.Host;
		if (!host.EndsWith(".music.126.net", StringComparison.OrdinalIgnoreCase) && !(host == "music.126.net") && !host.EndsWith(".music.163.com", StringComparison.OrdinalIgnoreCase) && !(host == "music.163.com"))
		{
			return false;
		}
		UriBuilder uriBuilder = new UriBuilder("https", host);
		uriBuilder.Path = result.AbsolutePath;
		uriBuilder.Query = "param=" + ((url.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0) ? "256y256" : "512y512");
		uri = uriBuilder.Uri;
		return true;
	}

	private static byte[] Download(Uri uri, bool thumbnail)
	{
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(new UriBuilder(uri)
		{
			Query = (thumbnail ? "param=256y256" : "param=512y512")
		}.Uri);
		httpWebRequest.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
		httpWebRequest.AllowAutoRedirect = false;
		httpWebRequest.Timeout = 3000;
		httpWebRequest.ReadWriteTimeout = 3000;
		try
		{
			using HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
			if (httpWebResponse.StatusCode < HttpStatusCode.OK || httpWebResponse.StatusCode >= HttpStatusCode.MultipleChoices)
			{
				return null;
			}
			using Stream stream = httpWebResponse.GetResponseStream();
			if (stream == null)
			{
				return null;
			}
			MemoryStream memoryStream = new MemoryStream();
			byte[] array = new byte[8192];
			int num = 0;
			while (num <= 786432)
			{
				int num2 = stream.Read(array, 0, array.Length);
				if (num2 <= 0)
				{
					break;
				}
				num += num2;
				if (num > 786432)
				{
					return null;
				}
				memoryStream.Write(array, 0, num2);
			}
			byte[] array2 = memoryStream.ToArray();
			if (array2.Length < 3)
			{
				return null;
			}
			bool num3 = array2[0] == byte.MaxValue && array2[1] == 216;
			bool flag = array2[0] == 137 && array2[1] == 80 && array2[2] == 78;
			bool flag2 = array2[0] == 82 && array2[1] == 73 && array2[2] == 70;
			return (num3 || flag || flag2) ? array2 : null;
		}
		catch
		{
			return null;
		}
	}

	private static byte[] Decode(string ffmpeg, byte[] image, int side)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo(ffmpeg)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		processStartInfo.Arguments = "-v error -threads 1 -f image2pipe -i pipe:0 -vf \"scale=" + side + ":" + side + ":force_original_aspect_ratio=increase:flags=lanczos,crop=" + side + ":" + side + "\" -frames:v 1 -pix_fmt rgb24 -f rawvideo pipe:1";
		Process process = null;
		try
		{
			process = new Process
			{
				StartInfo = processStartInfo
			};
			process.ErrorDataReceived += delegate
			{
			};
			process.Start();
			process.BeginErrorReadLine();
			process.StandardInput.BaseStream.Write(image, 0, image.Length);
			process.StandardInput.Close();
			int num = side * side * 3;
			byte[] array = new byte[num];
			Stream baseStream = process.StandardOutput.BaseStream;
			int num2;
			int num3;
			for (num2 = 0; num2 < num; num2 += num3)
			{
				num3 = baseStream.Read(array, num2, num - num2);
				if (num3 <= 0)
				{
					break;
				}
			}
			process.WaitForExit(5000);
			if (num2 != num)
			{
				return null;
			}
			return array;
		}
		catch
		{
			return null;
		}
		finally
		{
			if (process != null)
			{
				try
				{
					if (!process.HasExited)
					{
						process.Kill();
					}
				}
				catch
				{
				}
				try
				{
					process.Dispose();
				}
				catch
				{
				}
			}
		}
	}

	private static byte[] Sample(byte[] pixels, int side, int resolution)
	{
		byte[] array = new byte[resolution * resolution * 3];
		int num = 0;
		for (int i = 0; i < resolution; i++)
		{
			int num2 = (2 * i + 1) * side / (2 * resolution);
			for (int j = 0; j < resolution; j++)
			{
				int num3 = (2 * j + 1) * side / (2 * resolution);
				int num4 = (num2 * side + num3) * 3;
				array[num++] = pixels[num4];
				array[num++] = pixels[num4 + 1];
				array[num++] = pixels[num4 + 2];
			}
		}
		return array;
	}

	private static string Tiles(byte[] rgb, int size, int limit)
	{
		int stride = size + 1;
		int num = stride * stride;
		long[] rSum = new long[num];
		long[] gSum = new long[num];
		long[] bSum = new long[num];
		long[] qSum = new long[num];
		for (int i = 0; i < size; i++)
		{
			long num2 = 0L;
			long num3 = 0L;
			long num4 = 0L;
			long num5 = 0L;
			for (int j = 0; j < size; j++)
			{
				int num6 = (i * size + j) * 3;
				int num7 = rgb[num6];
				int num8 = rgb[num6 + 1];
				int num9 = rgb[num6 + 2];
				num2 += num7;
				num3 += num8;
				num4 += num9;
				num5 += num7 * num7 + num8 * num8 + num9 * num9;
				int num10 = (i + 1) * stride + j + 1;
				rSum[num10] = rSum[num10 - stride] + num2;
				gSum[num10] = gSum[num10 - stride] + num3;
				bSum[num10] = bSum[num10 - stride] + num4;
				qSum[num10] = qSum[num10 - stride] + num5;
			}
		}
		Func<int, int, int, int, Block> func = delegate(int left, int top, int right, int bottom)
		{
			int num16 = top * stride + left;
			int num17 = top * stride + right;
			int num18 = bottom * stride + left;
			int num19 = bottom * stride + right;
			long num20 = rSum[num19] - rSum[num18] - rSum[num17] + rSum[num16];
			long num21 = gSum[num19] - gSum[num18] - gSum[num17] + gSum[num16];
			long num22 = bSum[num19] - bSum[num18] - bSum[num17] + bSum[num16];
			long num23 = qSum[num19] - qSum[num18] - qSum[num17] + qSum[num16];
			long num24 = (long)(right - left) * (long)(bottom - top);
			long variance = num23 - (num20 * num20 + num21 * num21 + num22 * num22) / num24;
			return new Block
			{
				Variance = variance,
				Left = left,
				Top = top,
				Right = right,
				Bottom = bottom,
				R = (int)Math.Round((double)num20 / (double)num24),
				G = (int)Math.Round((double)num21 / (double)num24),
				B = (int)Math.Round((double)num22 / (double)num24)
			};
		};
		List<Block> list = new List<Block> { func(0, 0, size, size) };
		List<Block> list2 = new List<Block>();
		while (list.Count + list2.Count < limit && list.Count > 0)
		{
			int index = 0;
			for (int num11 = 1; num11 < list.Count; num11++)
			{
				if (list[num11].Variance > list[index].Variance)
				{
					index = num11;
				}
			}
			Block block = list[index];
			list.RemoveAt(index);
			int num12 = block.Right - block.Left;
			int num13 = block.Bottom - block.Top;
			if (block.Variance <= 0 || Math.Max(num12, num13) <= 1)
			{
				list2.Add(block);
			}
			else if (num12 >= num13)
			{
				int num14 = (block.Left + block.Right) / 2;
				list.Add(func(block.Left, block.Top, num14, block.Bottom));
				list.Add(func(num14, block.Top, block.Right, block.Bottom));
			}
			else
			{
				int num15 = (block.Top + block.Bottom) / 2;
				list.Add(func(block.Left, block.Top, block.Right, num15));
				list.Add(func(block.Left, num15, block.Right, block.Bottom));
			}
		}
		List<byte> list3 = new List<byte>((list.Count + list2.Count) * 7);
		foreach (Block item in list)
		{
			AppendBlock(list3, item);
		}
		foreach (Block item2 in list2)
		{
			AppendBlock(list3, item2);
		}
		return Hex(list3.ToArray());
	}

	private static void AppendBlock(List<byte> bytes, Block block)
	{
		bytes.Add((byte)block.Left);
		bytes.Add((byte)block.Top);
		bytes.Add((byte)block.Right);
		bytes.Add((byte)block.Bottom);
		bytes.Add((byte)block.R);
		bytes.Add((byte)block.G);
		bytes.Add((byte)block.B);
	}

	private static byte[] TexturePacket(byte[] pixels, int side)
	{
		byte[] array = new byte[side * side * 4];
		for (int i = 0; i < side * side; i++)
		{
			array[i * 4] = pixels[i * 3];
			array[i * 4 + 1] = pixels[i * 3 + 1];
			array[i * 4 + 2] = pixels[i * 3 + 2];
			array[i * 4 + 3] = byte.MaxValue;
		}
		MemoryStream memoryStream = new MemoryStream();
		for (int j = 0; j < side; j += 20)
		{
			for (int k = 0; k < side; k += 20)
			{
				byte[] array2 = new byte[1936];
				int num = 0;
				for (int l = j - 1; l <= j + 20; l++)
				{
					int num2 = Math.Min(Math.Max(l, 0), side - 1) * side;
					for (int m = k - 1; m <= k + 20; m++)
					{
						int num3 = (num2 + Math.Min(Math.Max(m, 0), side - 1)) * 4;
						array2[num++] = array[num3];
						array2[num++] = array[num3 + 1];
						array2[num++] = array[num3 + 2];
						array2[num++] = array[num3 + 3];
					}
				}
				byte[] bytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(array2));
				memoryStream.Write(bytes, 0, bytes.Length);
			}
		}
		byte[] array3 = memoryStream.ToArray();
		string s;
		using (SHA256 sHA = SHA256.Create())
		{
			s = Hex(sHA.ComputeHash(array3)).Substring(0, 64);
		}
		byte[] bytes2 = Encoding.ASCII.GetBytes(s);
		byte[] array4 = new byte[bytes2.Length + 1 + array3.Length];
		Buffer.BlockCopy(bytes2, 0, array4, 0, bytes2.Length);
		array4[bytes2.Length] = 10;
		Buffer.BlockCopy(array3, 0, array4, bytes2.Length + 1, array3.Length);
		return array4;
	}

	private static string Hex(byte[] bytes)
	{
		StringBuilder stringBuilder = new StringBuilder(bytes.Length * 2);
		foreach (byte b in bytes)
		{
			stringBuilder.Append(b.ToString("x2"));
		}
		return stringBuilder.ToString();
	}
}
