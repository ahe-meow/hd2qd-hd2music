using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Hd2Qd.HD2Music;

public sealed class GameWatch
{
	private readonly string _heartbeat;

	private readonly Action _stopOnChange;

	private readonly Thread _worker;

	private readonly ManualResetEventSlim _stopped = new ManualResetEventSlim(initialState: false);

	private volatile object _token;

	private int _pid;

	private long _stamp;

	private volatile bool _forced;

	public object Token => _token;

	public int Pid => _pid;

	public long Stamp => _stamp;

	public GameWatch(string heartbeat, Action stopOnChange)
	{
		_heartbeat = heartbeat;
		_stopOnChange = stopOnChange;
		_worker = new Thread(Run)
		{
			IsBackground = true,
			Name = "NetMusic game watch"
		};
	}

	public void Start()
	{
		_worker.Start();
	}

	public void ForceToken()
	{
		_forced = true;
		_token = new object();
		_pid = 0;
		_stamp = Now();
	}

	public void Close()
	{
		_stopped.Set();
		try
		{
			if (_worker.IsAlive)
			{
				_worker.Join(1500);
			}
		}
		catch
		{
		}
	}

	private void Run()
	{
		while (!_stopped.IsSet)
		{
			try
			{
				Sample();
			}
			catch
			{
			}
			_stopped.Wait(250);
		}
	}

	public void Sample()
	{
		if (_forced)
		{
			if (_token == null)
			{
				_token = new object();
			}
			return;
		}
		int? num = GamePid();
		long? num2 = null;
		try
		{
			string text = File.ReadAllText(_heartbeat);
			if (text.EndsWith("\n", StringComparison.Ordinal) && long.TryParse(text.Trim(), out var result))
			{
				num2 = result;
			}
		}
		catch
		{
		}
		if (num2.HasValue)
		{
			_stamp = num2.Value;
		}
		long num3 = Now();
		object obj2 = ((!num.HasValue || num2.GetValueOrDefault() == 0L) ? null : ((_token != null && num.Value == _pid) ? _token : ((-1 > num3 - _stamp || num3 - _stamp >= 5) ? null : new object())));
		_pid = num.GetValueOrDefault();
		if (obj2 == _token)
		{
			return;
		}
		_token = obj2;
		try
		{
			_stopOnChange();
		}
		catch
		{
		}
	}

	private static int? GamePid()
	{
		try
		{
			Process[] processesByName = Process.GetProcessesByName("helldivers2");
			if (processesByName.Length == 0)
			{
				return null;
			}
			return processesByName[0].Id;
		}
		catch
		{
			return null;
		}
	}

	private static long Now()
	{
		return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
	}
}
