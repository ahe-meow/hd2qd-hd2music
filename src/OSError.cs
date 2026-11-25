using System;

namespace Hd2Qd.HD2Music;

public sealed class OSError : Exception
{
	public int Code { get; private set; }

	public OSError(string message, int code = 0)
		: base(message)
	{
		Code = code;
	}
}
