using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Hd2Qd.HD2Music;

public sealed class StreamPlayer
{
	private sealed class SpectrumFrame
	{
		public long Frame;

		public string Bands;
	}

	private sealed class NextTrack
	{
		public Process Process;

		public string Url;

		public object Marker;

		public byte[] Pcm = new byte[0];

		public double Duration;

		public Thread Reader;
	}

	private sealed class Transition
	{
		public long Frame;

		public string Url;

		public object Marker;

		public double Duration;
	}

	private const int Rate = 44100;

	private const int FrameBytes = 4;

	private const int BufferBytes = 17640;

	private const int SpectrumFrames = 2205;

	private const int SpectrumSize = 1024;

	private static readonly int[] SpectrumFrequencies = new int[16]
	{
		55, 80, 115, 165, 235, 330, 470, 660, 930, 1300,
		1830, 2500, 3450, 4700, 6400, 8700
	};

	private static readonly double[] SpectrumWindow = BuildWindow();

	private static readonly double[] SpectrumCoefficients = BuildCoefficients();

	public const string Silence = "00000000000000000000000000000000";

	public string Executable;

	public GameWatch GameWatch;

	public PlaybackStatus Status = new PlaybackStatus();

	public int Volume = 65;

	public bool Active;

	public bool AwaitingPlay;

	public Process Process;

	public object PlaybackGame;

	public string Url;

	public double Offset;

	public int? Outcome;

	public double EndedPosition;

	public double Started;

	private readonly object _lock = new object();

	private readonly object _lifecycle = new object();

	private readonly ManualResetEventSlim _halt = new ManualResetEventSlim(initialState: false);

	private Thread _worker;

	private WaveOut _output;

	private readonly LinkedList<SpectrumFrame> _spectrum = new LinkedList<SpectrumFrame>();

	private long _queuedFrames;

	private NextTrack _prefetch;

	private Transition _transition;

	private object _changedTrack;

	private long _trackOrigin;

	public StreamPlayer(string executable, string heartbeat)
	{
		Executable = executable;
		GameWatch = new GameWatch(heartbeat, StopOnGameChange);
	}

	private static double[] BuildWindow()
	{
		double[] array = new double[1024];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = 0.5 - 0.5 * Math.Cos(Math.PI * 2.0 * (double)i / 1023.0);
		}
		return array;
	}

	private static double[] BuildCoefficients()
	{
		double[] array = new double[SpectrumFrequencies.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = 2.0 * Math.Cos(Math.PI * 2.0 * (double)SpectrumFrequencies[i] / 22050.0);
		}
		return array;
	}

	public static void OverlapPcm(byte[] ending, byte[] beginning, out byte[] firstPart, out byte[] secondPart, out int frames)
	{
		int val = Math.Min(ending.Length, beginning.Length);
		val = Math.Min(val, 14112);
		val = val / 4 * 4;
		if (val == 0)
		{
			firstPart = ending;
			secondPart = beginning;
			frames = 0;
			return;
		}
		short[] array = new short[val / 2];
		short[] array2 = new short[val / 2];
		Buffer.BlockCopy(ending, ending.Length - val, array, 0, val);
		Buffer.BlockCopy(beginning, 0, array2, 0, val);
		frames = val / 4;
		for (int i = 0; i < frames; i++)
		{
			double num = Math.PI * (double)i / (2.0 * (double)Math.Max(1, frames - 1));
			double num2 = Math.Cos(num);
			double num3 = Math.Sin(num);
			for (int j = 0; j < 2; j++)
			{
				int num4 = i * 2 + j;
				long val2 = (long)Math.Round((double)array[num4] * num2 + (double)array2[num4] * num3);
				array[num4] = (short)Math.Max(-32768L, Math.Min(32767L, val2));
			}
		}
		byte[] array3 = new byte[val];
		Buffer.BlockCopy(array, 0, array3, 0, val);
		byte[] array4 = new byte[beginning.Length - val];
		Buffer.BlockCopy(beginning, val, array4, 0, array4.Length);
		firstPart = new byte[ending.Length - val];
		Buffer.BlockCopy(ending, 0, firstPart, 0, firstPart.Length);
		secondPart = new byte[array3.Length + array4.Length];
		Buffer.BlockCopy(array3, 0, secondPart, 0, array3.Length);
		Buffer.BlockCopy(array4, 0, secondPart, array3.Length, array4.Length);
	}

	public static string SpectrumBands(byte[] pcm)
	{
		int num = Math.Min(1024, (pcm.Length + 3) / 4);
		if (num <= 0)
		{
			return "00000000000000000000000000000000";
		}
		short[] array = new short[pcm.Length / 2];
		Buffer.BlockCopy(pcm, 0, array, 0, array.Length * 2);
		double[] array2 = new double[num];
		double[] array3 = new double[num];
		double num2 = 0.0;
		double num3 = 0.0;
		for (int i = 0; i < num; i++)
		{
			array2[i] = array[4 * i];
			array3[i] = array[4 * i] + array[4 * i + 1];
			num2 += array2[i] * array2[i];
			num3 += array3[i] * array3[i];
		}
		double[] array4;
		double num4;
		if (num3 < num2)
		{
			array4 = array2;
			num4 = 32768.0;
		}
		else
		{
			array4 = array3;
			num4 = 65536.0;
		}
		double[] array5 = new double[num];
		for (int j = 0; j < num; j++)
		{
			array5[j] = array4[j] * (SpectrumWindow[j] / num4);
		}
		StringBuilder stringBuilder = new StringBuilder(32);
		double[] spectrumCoefficients = SpectrumCoefficients;
		foreach (double num5 in spectrumCoefficients)
		{
			double num6 = 0.0;
			double num7 = 0.0;
			for (int l = 0; l < num; l++)
			{
				double num8 = array5[l] + num5 * num6 - num7;
				num7 = num6;
				num6 = num8;
			}
			double val = num6 * num6 + num7 * num7 - num5 * num6 * num7;
			double val2 = Math.Sqrt(Math.Max(0.0, val)) * 4.0 / 1024.0;
			double num9 = 20.0 * Math.Log10(Math.Max(val2, 1E-06));
			stringBuilder.Append(((int)Math.Round(255.0 * Math.Min(1.0, Math.Max(0.0, (num9 + 55.0) / 48.0)))).ToString("x2"));
		}
		return stringBuilder.ToString();
	}

	private Process Decoder(string url, double position)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo(Executable)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("-nostdin -hide_banner -loglevel error -protocol_whitelist http,https,tcp,tls,crypto,data ");
		stringBuilder.Append("-rw_timeout 8000000 -reconnect 1 -reconnect_on_network_error 1 -reconnect_delay_max 2 ");
		if (position > 0.0)
		{
			stringBuilder.Append("-ss ").Append(position.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ');
		}
		stringBuilder.Append("-i \"").Append(url.Replace("\"", "\\\"")).Append("\" ");
		stringBuilder.Append("-map 0:a:0 -ac 2 -ar ").Append(44100).Append(" -f s16le pipe:1");
		processStartInfo.Arguments = stringBuilder.ToString();
		Process process = new Process();
		process.StartInfo = processStartInfo;
		process.EnableRaisingEvents = false;
		process.ErrorDataReceived += delegate
		{
		};
		if (!process.Start())
		{
			throw new OSError("cannot start ffmpeg");
		}
		process.BeginErrorReadLine();
		return process;
	}

	private static byte[] ReadPcm(Process process, int count)
	{
		Stream baseStream = process.StandardOutput.BaseStream;
		byte[] array = new byte[count];
		int i;
		int num;
		for (i = 0; i < count; i += num)
		{
			try
			{
				num = baseStream.Read(array, i, count - i);
			}
			catch
			{
				break;
			}
			if (num <= 0)
			{
				break;
			}
		}
		if (i == 0)
		{
			return new byte[0];
		}
		if (i == count)
		{
			return array;
		}
		byte[] array2 = new byte[i];
		Buffer.BlockCopy(array, 0, array2, 0, i);
		return array2;
	}

	public bool Start(string url, object game, double position = 0.0, bool paused = false, double duration = 0.0)
	{
		lock (_lifecycle)
		{
			if (game == null || game != GameWatch.Token)
			{
				return false;
			}
			Process decoder = Decoder(url, position);
			try
			{
				Close();
			}
			finally
			{
				if (Process != null || game != GameWatch.Token)
				{
					try
					{
						if (!decoder.HasExited)
						{
							decoder.Kill();
						}
					}
					catch
					{
					}
					try
					{
						decoder.WaitForExit(1000);
					}
					catch
					{
					}
					try
					{
						decoder.StandardOutput.Close();
					}
					catch
					{
					}
				}
			}
			if (game != GameWatch.Token)
			{
				return false;
			}
			_halt.Reset();
			Offset = position;
			Status = new PlaybackStatus
			{
				State = "loading",
				Pos = position,
				Dur = duration
			};
			Outcome = null;
			Started = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
			Url = url;
			Process = decoder;
			PlaybackGame = game;
			Active = true;
			AwaitingPlay = true;
			_worker = new Thread((ThreadStart)delegate
			{
				PlayPcm(decoder, paused);
			})
			{
				IsBackground = true,
				Name = "NetMusic player"
			};
			_worker.Start();
			return true;
		}
	}

	public bool PrepareNext(string url, object game, object marker, double duration = 0.0)
	{
		lock (_lifecycle)
		{
			if (!Active || game != PlaybackGame || game != GameWatch.Token)
			{
				return false;
			}
			lock (_lock)
			{
				if (_prefetch != null || _transition != null || Outcome.HasValue)
				{
					return false;
				}
				Process process = Decoder(url, 0.0);
				NextTrack next = new NextTrack
				{
					Process = process,
					Url = url,
					Marker = marker,
					Duration = duration
				};
				_prefetch = next;
				next.Reader = new Thread((ThreadStart)delegate
				{
					try
					{
						byte[] array = ReadPcm(process, 17640);
						lock (_lock)
						{
							next.Pcm = ((array.Length >= 4) ? Trim(array, array.Length / 4 * 4) : new byte[0]);
						}
					}
					catch
					{
					}
				})
				{
					IsBackground = true,
					Name = "NetMusic prefetch"
				};
				next.Reader.Start();
				return true;
			}
		}
	}

	private static byte[] Trim(byte[] data, int length)
	{
		if (length == data.Length)
		{
			return data;
		}
		byte[] array = new byte[length];
		Buffer.BlockCopy(data, 0, array, 0, length);
		return array;
	}

	public void CancelNext()
	{
		NextTrack prefetch;
		lock (_lock)
		{
			prefetch = _prefetch;
			_prefetch = null;
		}
		if (prefetch == null)
		{
			return;
		}
		try
		{
			if (!prefetch.Process.HasExited)
			{
				prefetch.Process.Kill();
			}
		}
		catch
		{
		}
		try
		{
			prefetch.Process.WaitForExit(3000);
		}
		catch
		{
		}
		try
		{
			if (prefetch.Reader != null)
			{
				prefetch.Reader.Join(1000);
			}
		}
		catch
		{
		}
		try
		{
			prefetch.Process.StandardOutput.Close();
		}
		catch
		{
		}
	}

	public object TakeTransition()
	{
		lock (_lock)
		{
			UpdatePositionLocked();
			object changedTrack = _changedTrack;
			_changedTrack = null;
			return changedTrack;
		}
	}

	private void PlayPcm(Process decoder, bool paused)
	{
		int value = 1;
		byte[] array = new byte[0];
		double num = Offset;
		double num2 = Status.Dur;
		bool flag = false;
		try
		{
			while (!_halt.IsSet)
			{
				bool flag2;
				lock (_lock)
				{
					if (_output != null)
					{
						_output.Reap();
					}
					flag2 = _output == null || (_output.BuffersCount < 4 && Status.State != "paused");
				}
				if (!flag2)
				{
					_halt.Wait(25);
					continue;
				}
				byte[] array2 = ReadPcm(decoder, 17640);
				if (array2.Length == 0)
				{
					// A CDN often closes the connection with a non-zero code at EOF. Once the
					// decoded audio has reached the track's end, that is a normal finish.
					flag = decoder.WaitForExit(1000) && (num2 <= 0.0 || num >= num2 - Math.Max(1.5, Math.Min(5.0, num2 * 0.025)));
					lock (_lock)
					{
						NextTrack nextTrack = ((flag && _transition == null && !_halt.IsSet) ? _prefetch : null);
						if (nextTrack != null && nextTrack.Pcm != null && nextTrack.Pcm.Length != 0)
						{
							OverlapPcm(array, nextTrack.Pcm, out var firstPart, out var secondPart, out var _);
							EnqueuePcmLocked(firstPart, paused);
							num2 = nextTrack.Duration;
							num = (double)nextTrack.Pcm.Length / 176400.0;
							_transition = new Transition
							{
								Frame = _queuedFrames,
								Url = nextTrack.Url,
								Marker = nextTrack.Marker,
								Duration = num2
							};
							EnqueuePcmLocked(secondPart, paused);
							try
							{
								decoder.StandardOutput.Close();
							}
							catch
							{
							}
							decoder = nextTrack.Process;
							Process = decoder;
							_prefetch = null;
							array = new byte[0];
							continue;
						}
						EnqueuePcmLocked(array, paused);
					}
					break;
				}
				int num3 = array2.Length - array2.Length % 4;
				if (num3 == 0 || _halt.IsSet)
				{
					break;
				}
				if (num3 != array2.Length)
				{
					array2 = Trim(array2, num3);
				}
				num += (double)num3 / 176400.0;
				if (array.Length == 0 && AwaitingPlay)
				{
					lock (_lock)
					{
						EnqueuePcmLocked(array2, paused);
					}
					continue;
				}
				lock (_lock)
				{
					EnqueuePcmLocked(array, paused);
				}
				array = array2;
			}
			while (!_halt.IsSet)
			{
				lock (_lock)
				{
					if (_output == null)
					{
						break;
					}
					UpdatePositionLocked();
					_output.Reap();
					if (_output.BuffersCount == 0)
					{
						value = ((!flag) ? 1 : 0);
						break;
					}
				}
				_halt.Wait(25);
			}
		}
		catch (Exception)
		{
		}
		finally
		{
			lock (_lock)
			{
				if (_output != null)
				{
					try
					{
						_output.Close();
					}
					catch
					{
					}
					_output = null;
				}
				if (!_halt.IsSet)
				{
					Outcome = value;
				}
			}
		}
	}

	private void EnqueuePcmLocked(byte[] pcm, bool paused)
	{
		if (pcm == null || pcm.Length == 0 || _halt.IsSet)
		{
			return;
		}
		if (_output == null)
		{
			_output = new WaveOut();
			_output.SetVolume(Volume);
			if (paused)
			{
				_output.Pause();
			}
		}
		_output.Enqueue(pcm);
		int num = 8820;
		for (int i = 0; i < pcm.Length; i += num)
		{
			int num2 = Math.Min(num, pcm.Length - i);
			byte[] array = new byte[num2];
			Buffer.BlockCopy(pcm, i, array, 0, num2);
			_queuedFrames += num2 / 4;
			_spectrum.AddLast(new SpectrumFrame
			{
				Frame = _queuedFrames,
				Bands = SpectrumBands(array)
			});
			while (_spectrum.Count > 24)
			{
				_spectrum.RemoveFirst();
			}
		}
		if (AwaitingPlay)
		{
			Status.State = (paused ? "paused" : "playing");
			AwaitingPlay = false;
		}
	}

	private void UpdatePositionLocked()
	{
		if (_output == null)
		{
			return;
		}
		double? num = _output.Seconds();
		if (num.HasValue)
		{
			long num2 = (long)(num.Value * 44100.0);
			if (_transition != null && num2 >= _transition.Frame)
			{
				_trackOrigin = _transition.Frame;
				Url = _transition.Url;
				_changedTrack = _transition.Marker;
				Status.Dur = _transition.Duration;
				Offset = 0.0;
				_transition = null;
			}
			Status.Pos = Offset + (double)Math.Max(0L, num2 - _trackOrigin) / 44100.0;
		}
	}

	public double? DebugSeconds()
	{
		lock (_lock)
		{
			return (_output != null) ? _output.Seconds() : ((double?)null);
		}
	}

	public string DebugPosition()
	{
		lock (_lock)
		{
			if (_output == null)
			{
				return "no output";
			}
			double? num = _output.Seconds();
			return "code=" + _output.LastPositionCode + " kind=" + _output.LastPositionKind + " value=" + _output.LastPositionValue + " seconds=" + (num.HasValue ? num.Value.ToString() : "null");
		}
	}

	public void Tick()
	{
		lock (_lock)
		{
			UpdatePositionLocked();
		}
	}

	public string Spectrum()
	{
		lock (_lock)
		{
			if (_output == null || Status.State != "playing" || Volume == 0)
			{
				return "00000000000000000000000000000000";
			}
			double? num = _output.Seconds();
			if (!num.HasValue)
			{
				return "00000000000000000000000000000000";
			}
			long num2 = Math.Max(0L, (long)(num.Value * 44100.0));
			while (_spectrum.Count > 0 && _spectrum.First.Value.Frame <= num2)
			{
				_spectrum.RemoveFirst();
			}
			return (_spectrum.Count > 0) ? _spectrum.First.Value.Bands : "00000000000000000000000000000000";
		}
	}

	public bool Pause(bool paused)
	{
		lock (_lock)
		{
			if (!Active || _output == null || Outcome.HasValue)
			{
				return false;
			}
			try
			{
				if (paused)
				{
					_output.Pause();
				}
				else
				{
					_output.Resume();
				}
				UpdatePositionLocked();
				Status.State = (paused ? "paused" : "playing");
				return true;
			}
			catch
			{
				return false;
			}
		}
	}

	public bool Seek(double position)
	{
		lock (_lifecycle)
		{
			string url;
			object playbackGame;
			double dur;
			bool paused;
			lock (_lock)
			{
				if (!Active || AwaitingPlay || Outcome.HasValue || string.IsNullOrEmpty(Url))
				{
					return false;
				}
				UpdatePositionLocked();
				if (_changedTrack != null)
				{
					return false;
				}
				url = Url;
				playbackGame = PlaybackGame;
				dur = Status.Dur;
				paused = Status.State == "paused";
			}
			return Start(url, playbackGame, position, paused, dur);
		}
	}

	public void SetVolume(int value)
	{
		lock (_lock)
		{
			Volume = Math.Max(0, Math.Min(100, value));
			if (_output != null)
			{
				_output.SetVolume(Volume);
			}
		}
	}

	public int? Result()
	{
		lock (_lifecycle)
		{
			if (!Active)
			{
				return null;
			}
			int? outcome;
			bool flag;
			double pos;
			lock (_lock)
			{
				UpdatePositionLocked();
				outcome = Outcome;
				flag = AwaitingPlay && Now() - Started > 18.0;
				pos = Status.Pos;
			}
			if (outcome.HasValue || flag)
			{
				EndedPosition = pos;
				Close();
				return outcome ?? 1;
			}
			return null;
		}
	}

	private static double Now()
	{
		return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
	}

	public void StopOnGameChange()
	{
		lock (_lifecycle)
		{
			if (PlaybackGame != null && PlaybackGame != GameWatch.Token)
			{
				Close();
			}
		}
	}

	public void Stop()
	{
		Close();
	}

	public void Close()
	{
		lock (_lifecycle)
		{
			_halt.Set();
			CancelNext();
			lock (_lock)
			{
				if (_output != null)
				{
					try
					{
						_output.Close();
					}
					catch
					{
					}
					_output = null;
				}
				Active = false;
				AwaitingPlay = false;
				_spectrum.Clear();
				_queuedFrames = 0L;
				_trackOrigin = 0L;
				_transition = null;
				_changedTrack = null;
			}
			if (Process != null)
			{
				try
				{
					if (!Process.HasExited)
					{
						Process.Kill();
					}
				}
				catch
				{
				}
				try
				{
					Process.WaitForExit(3000);
				}
				catch
				{
				}
			}
			Thread worker = _worker;
			if (worker != null)
			{
				try
				{
					worker.Join(3000);
				}
				catch
				{
				}
			}
			if (Process != null)
			{
				try
				{
					Process.StandardOutput.Close();
				}
				catch
				{
				}
			}
			Process = null;
			_worker = null;
			PlaybackGame = null;
			Url = null;
			Status = new PlaybackStatus();
		}
	}
}
