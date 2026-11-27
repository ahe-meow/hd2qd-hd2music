using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Hd2Qd.HD2Music;

public sealed class GameWatch
{
	private readonly string _heartbeat;

	private readonly Thread _worker;

	private readonly ManualResetEventSlim _stopped = new ManualResetEventSlim(initialState: false);

	// One token for the life of the plugin, not for the life of a game session. StreamPlayer
	// uses it as a generation marker, so tying it to the game made playback impossible without
	// the game and killed the song the moment the game closed.
	private volatile object _token = new object();

	private int _pid;

	private long _stamp;

	private volatile bool _forced;

	private volatile bool _modern;

	private volatile bool _beat;

	public object Token => _token;

	/// <summary>True when the mod itself beat back recently — the only proof a body got loaded.</summary>
	public bool Beat
	{
		get
		{
			return _beat;
		}
	}

	/// <summary>
	/// Set while the Data Hub has answered a handshake for the pid now running. 1.2.6 moved
	/// the body out of the patch and behind that handshake, so the heartbeat can only appear
	/// after we deliver — gating on the heartbeat first leaves both sides waiting forever.
	/// </summary>
	public bool Modern
	{
		get
		{
			return _modern;
		}
		set
		{
			_modern = value;
		}
	}

	public int Pid => _pid;

	public long Stamp => _stamp;

	public GameWatch(string heartbeat, Action stopOnChange)
	{
		_heartbeat = heartbeat;
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
		long age = num3 - _stamp;
		// a 1.2.4-era body ships inside the patch and beats on its own; a 1.2.6+ body only
		// exists after we answer the handshake, so that handshake is the proof, not the beat
		_beat = num2.GetValueOrDefault() != 0L && !(-1 > age || age >= 5);
		// still watch the game for the HUD and the diagnostics, but never touch the token:
		// playback must survive the game closing, so there is nothing here to stop
		_pid = num.GetValueOrDefault();
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
