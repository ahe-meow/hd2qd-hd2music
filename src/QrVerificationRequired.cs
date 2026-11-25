using System;

namespace Hd2Qd.HD2Music;

public sealed class QrVerificationRequired : Exception
{
	public QrVerificationRequired(string message)
		: base(message)
	{
	}
}
