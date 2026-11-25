using System;

namespace Hd2Qd.HD2Music;

public sealed class SmsLoginError : Exception
{
	public SmsLoginError(string message)
		: base(message)
	{
	}
}
