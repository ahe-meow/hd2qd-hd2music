using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Hd2Qd.HD2Music;

internal sealed class WaveOut : IDisposable
{
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct WaveFormat
	{
		public ushort Format;

		public ushort Channels;

		public uint Rate;

		public uint ByteRate;

		public ushort Alignment;

		public ushort Bits;

		public ushort Extra;
	}

	private struct WaveHeader
	{
		public IntPtr Data;

		public uint Length;

		public uint Recorded;

		public IntPtr User;

		public uint Flags;

		public uint Loops;

		public IntPtr Next;

		public IntPtr Reserved;
	}

	private struct MediaTime
	{
		public uint Kind;

		public uint Value;
	}

	private sealed class Buffer
	{
		public IntPtr Header;

		public IntPtr DataPointer;

		public GCHandle Pin;

		public double Enqueued;

		public double Duration;

	}

	public const int Rate = 44100;

	public const int Frame = 4;

	private const uint WaveMapper = uint.MaxValue;

	private const uint WaveFormatPcm = 1u;

	private const uint WhdrDone = 1u;

	private IntPtr _handle;

	private readonly List<Buffer> _buffers = new List<Buffer>();

	private readonly Stopwatch _clock = Stopwatch.StartNew();

	private double _firstEnqueue = -1.0;

	private double? _pausedAt;

	private double _pausedTotal;

	public int BuffersCount => _buffers.Count;

	public uint LastPositionCode { get; private set; }

	public uint LastPositionKind { get; private set; }

	public uint LastPositionValue { get; private set; }

	public double EstimatedSeconds
	{
		get
		{
			if (_firstEnqueue < 0.0)
			{
				return 0.0;
			}
			double num = _pausedAt ?? _clock.Elapsed.TotalSeconds;
			return Math.Max(0.0, num - _firstEnqueue - _pausedTotal);
		}
	}

	[DllImport("winmm.dll")]
	private static extern uint waveOutOpen(ref IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);

	[DllImport("winmm.dll")]
	private static extern uint waveOutPrepareHeader(IntPtr handle, IntPtr header, uint size);

	[DllImport("winmm.dll")]
	private static extern uint waveOutWrite(IntPtr handle, IntPtr header, uint size);

	[DllImport("winmm.dll")]
	private static extern uint waveOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);

	[DllImport("winmm.dll")]
	private static extern uint waveOutGetPosition(IntPtr handle, ref MediaTime time, uint size);

	[DllImport("winmm.dll")]
	private static extern uint waveOutPause(IntPtr handle);

	[DllImport("winmm.dll")]
	private static extern uint waveOutRestart(IntPtr handle);

	[DllImport("winmm.dll")]
	private static extern uint waveOutSetVolume(IntPtr handle, uint volume);

	[DllImport("winmm.dll")]
	private static extern uint waveOutReset(IntPtr handle);

	[DllImport("winmm.dll")]
	private static extern uint waveOutClose(IntPtr handle);

	public WaveOut()
	{
		WaveFormat format = new WaveFormat
		{
			Format = 1,
			Channels = 2,
			Rate = 44100u,
			ByteRate = 176400u,
			Alignment = 4,
			Bits = 16,
			Extra = 0
		};
		Check(waveOutOpen(ref _handle, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0u));
	}

	private static void Check(uint code)
	{
		if (code != 0)
		{
			throw new OSError("Windows audio error " + code, (int)code);
		}
	}

	/// <summary>
	/// Only release a buffer after winmm has set WHDR_DONE. The previous wall-clock
	/// timeout moved buffers into a retired list and freed their pinned PCM and WAVEHDR
	/// while the device could still be writing to them — native heap corruption that
	/// killed the host process instead of throwing a managed exception.
	/// </summary>
	public void Reap()
	{
		uint size = (uint)Marshal.SizeOf(typeof(WaveHeader));
		for (int i = _buffers.Count - 1; i >= 0; i--)
		{
			Buffer buffer = _buffers[i];
			uint flags = ((WaveHeader)Marshal.PtrToStructure(buffer.Header, typeof(WaveHeader))).Flags;
			if ((flags & WhdrDone) == 0)
			{
				continue;
			}
			try
			{
				Check(waveOutUnprepareHeader(_handle, buffer.Header, size));
			}
			catch
			{
				// still release the managed side below; winmm has reported the buffer done
			}
			FreeBuffer(buffer);
			_buffers.RemoveAt(i);
		}
	}

	public void Enqueue(byte[] pcm)
	{
		if (_firstEnqueue < 0.0)
		{
			_firstEnqueue = _clock.Elapsed.TotalSeconds;
		}
		Buffer buffer = new Buffer
		{
			Pin = GCHandle.Alloc(pcm, GCHandleType.Pinned),
			Enqueued = _clock.Elapsed.TotalSeconds,
			Duration = (double)pcm.Length / 176400.0
		};
		uint num = (uint)Marshal.SizeOf(typeof(WaveHeader));
		try
		{
			buffer.DataPointer = buffer.Pin.AddrOfPinnedObject();
			buffer.Header = Marshal.AllocHGlobal((int)num);
			Marshal.StructureToPtr(new WaveHeader
			{
				Data = buffer.DataPointer,
				Length = (uint)pcm.Length,
				Flags = 0u
			}, buffer.Header, fDeleteOld: false);
			Check(waveOutPrepareHeader(_handle, buffer.Header, num));
			_buffers.Add(buffer);
			Check(waveOutWrite(_handle, buffer.Header, num));
		}
		catch
		{
			_buffers.Remove(buffer);
			FreeBuffer(buffer);
			throw;
		}
	}

	private static void FreeBuffer(Buffer buffer)
	{
		if (buffer.Pin.IsAllocated)
		{
			buffer.Pin.Free();
		}
		if (buffer.Header != IntPtr.Zero)
		{
			Marshal.FreeHGlobal(buffer.Header);
			buffer.Header = IntPtr.Zero;
		}
	}

	public double? Seconds()
	{
		uint[] array = new uint[3] { 2u, 1u, 4u };
		foreach (uint kind in array)
		{
			MediaTime time = new MediaTime
			{
				Kind = kind
			};
			uint num = (LastPositionCode = waveOutGetPosition(_handle, ref time, (uint)Marshal.SizeOf(typeof(MediaTime))));
			LastPositionKind = time.Kind;
			LastPositionValue = time.Value;
			if (num == 0)
			{
				switch (time.Kind)
				{
				case 1u:
					return (double)time.Value / 1000.0;
				case 2u:
					return (double)time.Value / 44100.0;
				case 4u:
					return (double)time.Value / 176400.0;
				}
			}
		}
		return EstimatedSeconds;
	}

	public void SetVolume(int percent)
	{
		percent = Math.Max(0, Math.Min(100, percent));
		uint num = (uint)Math.Round((double)percent * 65535.0 / 100.0);
		Check(waveOutSetVolume(_handle, num | (num << 16)));
	}

	public void Pause()
	{
		if (_handle == IntPtr.Zero)
		{
			return;
		}
		Util.Log("waveout pause h=" + _handle.ToInt64());
		Check(waveOutPause(_handle));
		if (!_pausedAt.HasValue)
		{
			_pausedAt = _clock.Elapsed.TotalSeconds;
		}
	}

	public void Resume()
	{
		if (_handle == IntPtr.Zero)
		{
			return;
		}
		Util.Log("waveout resume h=" + _handle.ToInt64());
		Check(waveOutRestart(_handle));
		if (_pausedAt.HasValue)
		{
			_pausedTotal += _clock.Elapsed.TotalSeconds - _pausedAt.Value;
			_pausedAt = null;
		}
	}

	public void Close()
	{
		if (_handle == IntPtr.Zero)
		{
			return;
		}
		try
		{
			waveOutReset(_handle);
		}
		catch
		{
		}
		uint size = (uint)Marshal.SizeOf(typeof(WaveHeader));
		foreach (Buffer buffer in _buffers)
		{
			try
			{
				waveOutUnprepareHeader(_handle, buffer.Header, size);
			}
			catch
			{
			}
			FreeBuffer(buffer);
		}
		_buffers.Clear();
		try
		{
			waveOutClose(_handle);
		}
		catch
		{
		}
		_handle = IntPtr.Zero;
	}

	public void Dispose()
	{
		Close();
	}
}
