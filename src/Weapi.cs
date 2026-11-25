using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Hd2Qd.HD2Music;

internal static class Weapi
{
	public const string Key = "0CoJUm6Qyw8W8jud";

	public const string Iv = "0102030405060708";

	public const int Exponent = 65537;

	public const string ModulusHex = "00e0b509f6259df8642dbc35662901477df22677ec152b5ff68ace615bb7b725152b3ab17a876aea8a5aa76d2e417629ec4ee341f56135fccf695280104e0312ecbda92557c93870114af6c9d05c4f7f0c3685b7a46bee255932575cce10b424d813cfe4875d3e82047b97ddef52741d546b8e289dc6935b3ece0462db0a22b8e7";

	private static readonly BigInteger Modulus = BigInteger.Parse("000e0b509f6259df8642dbc35662901477df22677ec152b5ff68ace615bb7b725152b3ab17a876aea8a5aa76d2e417629ec4ee341f56135fccf695280104e0312ecbda92557c93870114af6c9d05c4f7f0c3685b7a46bee255932575cce10b424d813cfe4875d3e82047b97ddef52741d546b8e289dc6935b3ece0462db0a22b8e7", NumberStyles.HexNumber);

	private const string Base62 = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

	public static string AesCbcB64(string text)
	{
		return AesCbcB64(text, firstPass: true);
	}

	private static string AesCbcB64(string text, bool firstPass)
	{
		using AesCryptoServiceProvider aesCryptoServiceProvider = new AesCryptoServiceProvider();
		aesCryptoServiceProvider.Key = Encoding.UTF8.GetBytes("0CoJUm6Qyw8W8jud");
		aesCryptoServiceProvider.IV = Encoding.UTF8.GetBytes("0102030405060708");
		aesCryptoServiceProvider.Mode = CipherMode.CBC;
		aesCryptoServiceProvider.Padding = PaddingMode.PKCS7;
		using ICryptoTransform cryptoTransform = aesCryptoServiceProvider.CreateEncryptor();
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		return Convert.ToBase64String(cryptoTransform.TransformFinalBlock(bytes, 0, bytes.Length));
	}

	private static string EncryptWithSecret(string text, string secret)
	{
		using AesCryptoServiceProvider aesCryptoServiceProvider = new AesCryptoServiceProvider();
		aesCryptoServiceProvider.Key = Encoding.UTF8.GetBytes(secret);
		aesCryptoServiceProvider.IV = Encoding.UTF8.GetBytes("0102030405060708");
		aesCryptoServiceProvider.Mode = CipherMode.CBC;
		aesCryptoServiceProvider.Padding = PaddingMode.PKCS7;
		using ICryptoTransform cryptoTransform = aesCryptoServiceProvider.CreateEncryptor();
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		return Convert.ToBase64String(cryptoTransform.TransformFinalBlock(bytes, 0, bytes.Length));
	}

	public static string Secret()
	{
		byte[] array = new byte[16];
		using (RNGCryptoServiceProvider rNGCryptoServiceProvider = new RNGCryptoServiceProvider())
		{
			rNGCryptoServiceProvider.GetBytes(array);
		}
		StringBuilder stringBuilder = new StringBuilder(16);
		for (int i = 0; i < 16; i++)
		{
			stringBuilder.Append("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"[array[i] % "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".Length]);
		}
		return stringBuilder.ToString();
	}

	public static string RsaEncrypt(string secret)
	{
		if (secret == null || secret.Length != 16)
		{
			throw new ArgumentException("secret must be 16 characters");
		}
		byte[] bytes = Encoding.UTF8.GetBytes(secret);
		Array.Reverse(bytes);
		byte[] array = new byte[bytes.Length + 1];
		Array.Copy(bytes, array, bytes.Length);
		return BigInteger.ModPow(new BigInteger(array), 65537, Modulus).ToString("x").PadLeft(256, '0');
	}

	public static Dictionary<string, string> Params(Dictionary<string, object> payload, string csrf)
	{
		Dictionary<string, object> obj = ((payload == null) ? new Dictionary<string, object>() : new Dictionary<string, object>(payload));
		obj["csrf_token"] = csrf ?? "";
		string text = AesCbcB64(Util.Serialize(obj));
		string secret = Secret();
		string value = EncryptWithSecret(text, secret);
		return new Dictionary<string, string>
		{
			{ "params", value },
			{
				"encSecKey",
				RsaEncrypt(secret)
			}
		};
	}

	public static string Path(string path)
	{
		if (path == null)
		{
			return "/weapi/";
		}
		if (path.StartsWith("/api/", StringComparison.Ordinal))
		{
			return "/weapi/" + path.Substring(5);
		}
		if (path.StartsWith("/weapi/", StringComparison.Ordinal))
		{
			return path;
		}
		return "/weapi/" + path.TrimStart('/');
	}

	public static bool SelfTest()
	{
		if (Convert.FromBase64String(AesCbcB64("{\"a\":1}")).Length % 16 != 0)
		{
			return false;
		}
		string text = RsaEncrypt(Secret());
		if (text.Length == 256)
		{
			return BigInteger.Parse("0" + text, NumberStyles.HexNumber) < Modulus;
		}
		return false;
	}
}
