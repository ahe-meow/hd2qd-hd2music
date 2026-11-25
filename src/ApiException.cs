using System;

namespace Hd2Qd.HD2Music;

public class ApiException : Exception
{
	public int? Code;

	public string Body;

	public ApiException(string message, int? code = null, string body = null)
		: base(message)
	{
		Code = code;
		Body = body;
	}
}
