using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Hd2Qd.HD2Music;

/// <summary>Decoded audio for the player: 44.1 kHz, stereo, s16le, read like a pipe. Windows'
/// own Media Foundation decodes it (MP3 and FLAC ship with Windows 10 and later); ffmpeg.exe
/// is used only when Media Foundation cannot open the stream, for example on an N edition
/// without the Media Feature Pack.</summary>
public sealed class PcmSource
{
	public const int Rate = 44100;

	const int Channels = 2;

	const int FrameBytes = 4;

	/// <summary>About 1.5 s of audio decoded ahead; a full queue holds the decoder, as a full
	/// pipe held ffmpeg.</summary>
	const int QueuedChunks = 16;

	readonly BlockingCollection<byte[]> _chunks = new BlockingCollection<byte[]>(QueuedChunks);

	readonly CancellationTokenSource _kill = new CancellationTokenSource();

	readonly ManualResetEventSlim _ended = new ManualResetEventSlim(initialState: false);

	readonly string _url;

	readonly double _position;

	readonly string _ffmpeg;

	byte[] _current = new byte[0];

	int _offset;

	Process _process;

	static int _mf;

	/// <summary>Media Foundation started in this process: false on an N edition without the
	/// Media Feature Pack, where only ffmpeg.exe can decode.</summary>
	public static bool MfAvailable
	{
		get
		{
			if (_mf == 0)
			{
				try
				{
					Mf.Startup();
					_mf = 1;
				}
				catch
				{
					_mf = -1;
				}
			}
			return _mf > 0;
		}
	}

	/// <summary>The decoder the last stream that opened used, for the diagnostics file.</summary>
	public static string LastDecoder = "";

	/// <summary>"mf", "ffmpeg", or "" before the stream opened (and when nothing could).</summary>
	public string Decoder = "";

	/// <summary>Why Media Foundation did not open the stream, when it did not.</summary>
	public string MfError;

	PcmSource(string url, double position, string ffmpeg)
	{
		_url = url;
		_position = position;
		_ffmpeg = ffmpeg;
	}

	/// <summary>Opens nothing on the caller's thread: connecting happens on the pump thread,
	/// and the first Read waits for it, as the first read from ffmpeg's pipe did.</summary>
	public static PcmSource Open(string url, double position, string ffmpeg)
	{
		var source = new PcmSource(url, position, ffmpeg);
		new Thread(source.Pump)
		{
			IsBackground = true,
			Name = "NetMusic decoder"
		}.Start();
		return source;
	}

	/// <summary>Up to count bytes; fewer only at the end of the stream; 0 once it ended or
	/// was killed.</summary>
	public int Read(byte[] buffer, int offset, int count)
	{
		int done = 0;
		while (done < count)
		{
			if (_offset >= _current.Length)
			{
				byte[] next;
				try
				{
					if (!_chunks.TryTake(out next, Timeout.Infinite, _kill.Token))
					{
						break;
					}
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (InvalidOperationException)
				{
					break;
				}
				_current = next;
				_offset = 0;
				continue;
			}
			int take = Math.Min(count - done, _current.Length - _offset);
			Buffer.BlockCopy(_current, _offset, buffer, offset + done, take);
			_offset += take;
			done += take;
		}
		return done;
	}

	/// <summary>True once the decoder has stopped, at the end of the stream or on an error, as
	/// Process.WaitForExit was.</summary>
	public bool WaitForEnd(int milliseconds)
	{
		return _ended.Wait(milliseconds);
	}

	public void Kill()
	{
		try
		{
			_kill.Cancel();
		}
		catch
		{
		}
		Process process = _process;
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
		}
	}

	void Pump()
	{
		try
		{
			if (!PumpMediaFoundation() && !_kill.IsCancellationRequested && File.Exists(_ffmpeg))
			{
				PumpFfmpeg();
			}
		}
		catch
		{
		}
		finally
		{
			_chunks.CompleteAdding();
			_ended.Set();
		}
	}

	bool Add(byte[] chunk)
	{
		try
		{
			_chunks.Add(chunk, _kill.Token);
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>False only when the stream could not be opened, so ffmpeg may try; a stream
	/// that breaks after it opened ends here, as ffmpeg's would.</summary>
	bool PumpMediaFoundation()
	{
		Mf.IMFSourceReader reader = null;
		try
		{
			try
			{
				reader = Mf.OpenPcm(_url, Rate, Channels);
				if (_position > 0.0)
				{
					Mf.Seek(reader, _position);
				}
			}
			catch (Exception error)
			{
				MfError = error.Message;
				return false;
			}
			Decoder = LastDecoder = "mf";
			// Media Foundation seeks to the packet before the target; drop what lies before it
			long skip = _position > 0.0 ? (long)Math.Round(_position * Rate) : 0;
			bool first = true;
			while (!_kill.IsCancellationRequested)
			{
				int flags;
				long time;
				byte[] pcm = Mf.ReadPcm(reader, out flags, out time);
				if ((flags & Mf.ReaderEndOfStream) != 0 || (flags & Mf.ReaderError) != 0)
				{
					break;
				}
				if (pcm == null || pcm.Length == 0)
				{
					continue;
				}
				int length = pcm.Length / FrameBytes * FrameBytes;
				int start = 0;
				if (first && skip > 0)
				{
					long at = (long)Math.Round(time / 1e7 * Rate);
					long drop = Math.Max(0, skip - at) * FrameBytes;
					start = (int)Math.Min(length, drop);
					if (start >= length)
					{
						continue;
					}
				}
				first = false;
				if (start != 0 || length != pcm.Length)
				{
					byte[] trimmed = new byte[length - start];
					Buffer.BlockCopy(pcm, start, trimmed, 0, trimmed.Length);
					pcm = trimmed;
				}
				if (!Add(pcm))
				{
					break;
				}
			}
			return true;
		}
		finally
		{
			Mf.Release(reader);
		}
	}

	void PumpFfmpeg()
	{
		ProcessStartInfo info = new ProcessStartInfo(_ffmpeg)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		StringBuilder arguments = new StringBuilder();
		arguments.Append("-nostdin -hide_banner -loglevel error -protocol_whitelist http,https,tcp,tls,crypto,data ");
		arguments.Append("-rw_timeout 8000000 -reconnect 1 -reconnect_on_network_error 1 -reconnect_delay_max 2 ");
		if (_position > 0.0)
		{
			arguments.Append("-ss ").Append(_position.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ');
		}
		arguments.Append("-i \"").Append(_url.Replace("\"", "\\\"")).Append("\" ");
		arguments.Append("-map 0:a:0 -ac 2 -ar ").Append(Rate).Append(" -f s16le pipe:1");
		info.Arguments = arguments.ToString();
		Process process = new Process { StartInfo = info, EnableRaisingEvents = false };
		process.ErrorDataReceived += delegate
		{
		};
		if (!process.Start())
		{
			return;
		}
		_process = process;
		Decoder = LastDecoder = "ffmpeg";
		try
		{
			process.BeginErrorReadLine();
			if (_kill.IsCancellationRequested)
			{
				return;
			}
			Stream output = process.StandardOutput.BaseStream;
			while (!_kill.IsCancellationRequested)
			{
				byte[] buffer = new byte[17640];
				int read;
				try
				{
					read = output.Read(buffer, 0, buffer.Length);
				}
				catch
				{
					break;
				}
				if (read <= 0)
				{
					break;
				}
				if (read != buffer.Length)
				{
					byte[] part = new byte[read];
					Buffer.BlockCopy(buffer, 0, part, 0, read);
					buffer = part;
				}
				if (!Add(buffer))
				{
					break;
				}
			}
			process.WaitForExit(1000);
		}
		finally
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
				process.StandardOutput.Close();
			}
			catch
			{
			}
		}
	}
}

/// <summary>The few Media Foundation calls the player needs: a source reader on a URL that
/// hands out 16-bit PCM. Only the vtable slots up to the last method used are declared.</summary>
internal static class Mf
{
	public const int ReaderError = 0x1;

	public const int ReaderEndOfStream = 0x2;

	const int FirstAudioStream = unchecked((int)0xFFFFFFFD);

	const int AllStreams = unchecked((int)0xFFFFFFFE);

	const int Version = 0x00020070;

	static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");

	static readonly Guid Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");

	static readonly Guid AudioMajor = new Guid("73647561-0000-0010-8000-00aa00389b71");

	static readonly Guid Pcm = new Guid("00000001-0000-0010-8000-00aa00389b71");

	static readonly Guid NumChannels = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");

	static readonly Guid SamplesPerSecond = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");

	static readonly Guid BitsPerSample = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");

	static readonly Guid BlockAlignment = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");

	static readonly Guid AvgBytesPerSecond = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731");

	static readonly object Gate = new object();

	static int _started;

	[DllImport("mfplat.dll", ExactSpelling = true)]
	static extern int MFStartup(int version, int flags);

	[DllImport("mfplat.dll", ExactSpelling = true)]
	static extern int MFCreateMediaType(out IMFMediaType mediaType);

	[DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
	static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IMFSourceReader reader);

	static void Check(int hr, string what)
	{
		if (hr < 0)
		{
			throw new COMException(what + " failed (0x" + hr.ToString("X8") + ")", hr);
		}
	}

	public static void Startup()
	{
		lock (Gate)
		{
			if (_started == 0)
			{
				Check(MFStartup(Version, 0), "MFStartup");
				_started = 1;
			}
		}
	}

	public static IMFSourceReader OpenPcm(string url, int rate, int channels)
	{
		Startup();
		IMFSourceReader reader;
		Check(MFCreateSourceReaderFromURL(url, IntPtr.Zero, out reader), "MFCreateSourceReaderFromURL");
		IMFMediaType type = null;
		try
		{
			Check(reader.SetStreamSelection(AllStreams, false), "SetStreamSelection");
			Check(reader.SetStreamSelection(FirstAudioStream, true), "SetStreamSelection");
			Check(MFCreateMediaType(out type), "MFCreateMediaType");
			Guid major = MajorType, audio = AudioMajor, sub = Subtype, pcm = Pcm;
			Check(type.SetGUID(ref major, ref audio), "SetGUID");
			Check(type.SetGUID(ref sub, ref pcm), "SetGUID");
			SetUInt32(type, NumChannels, channels);
			SetUInt32(type, SamplesPerSecond, rate);
			SetUInt32(type, BitsPerSample, 16);
			SetUInt32(type, BlockAlignment, channels * 2);
			SetUInt32(type, AvgBytesPerSecond, rate * channels * 2);
			Check(reader.SetCurrentMediaType(FirstAudioStream, IntPtr.Zero, type), "SetCurrentMediaType");
			// the reader may accept a type it then adjusts; play only what was asked for
			IMFMediaType current;
			Check(reader.GetCurrentMediaType(FirstAudioStream, out current), "GetCurrentMediaType");
			try
			{
				int gotRate = GetUInt32(current, SamplesPerSecond);
				int gotChannels = GetUInt32(current, NumChannels);
				int gotBits = GetUInt32(current, BitsPerSample);
				if (gotRate != rate || gotChannels != channels || gotBits != 16)
				{
					throw new InvalidDataException("Media Foundation gives " + gotRate + " Hz, " + gotChannels + " ch, " + gotBits + " bit");
				}
			}
			finally
			{
				Release(current);
			}
			return reader;
		}
		catch
		{
			Release(reader);
			throw;
		}
		finally
		{
			Release(type);
		}
	}

	public static void Seek(IMFSourceReader reader, double seconds)
	{
		PropVariant position = new PropVariant { Type = 20, Value = (long)Math.Round(seconds * 1e7) };
		Guid format = Guid.Empty;
		Check(reader.SetCurrentPosition(ref format, ref position), "SetCurrentPosition");
	}

	/// <summary>One decoded sample's bytes, or null when the reader handed out no sample
	/// (a stream tick, or the end, which flags says).</summary>
	public static byte[] ReadPcm(IMFSourceReader reader, out int flags, out long time)
	{
		int actual;
		IMFSample sample;
		int hr = reader.ReadSample(FirstAudioStream, 0, out actual, out flags, out time, out sample);
		if (hr < 0)
		{
			flags |= ReaderError;
			return null;
		}
		if (sample == null)
		{
			return null;
		}
		IMFMediaBuffer buffer = null;
		try
		{
			Check(sample.ConvertToContiguousBuffer(out buffer), "ConvertToContiguousBuffer");
			IntPtr data;
			int max, length;
			Check(buffer.Lock(out data, out max, out length), "Lock");
			try
			{
				byte[] pcm = new byte[length];
				Marshal.Copy(data, pcm, 0, length);
				return pcm;
			}
			finally
			{
				buffer.Unlock();
			}
		}
		finally
		{
			Release(buffer);
			Release(sample);
		}
	}

	static void SetUInt32(IMFMediaType attributes, Guid key, int value)
	{
		Check(attributes.SetUINT32(ref key, value), "SetUINT32");
	}

	static int GetUInt32(IMFMediaType attributes, Guid key)
	{
		int value;
		Check(attributes.GetUINT32(ref key, out value), "GetUINT32");
		return value;
	}

	public static void Release(object com)
	{
		if (com != null)
		{
			try
			{
				Marshal.ReleaseComObject(com);
			}
			catch
			{
			}
		}
	}

	/// <summary>VT_I8 in a PROPVARIANT; 24 bytes covers its size on x64 and x86.</summary>
	[StructLayout(LayoutKind.Explicit, Size = 24)]
	public struct PropVariant
	{
		[FieldOffset(0)]
		public ushort Type;

		[FieldOffset(8)]
		public long Value;
	}

	[ComImport]
	[Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface IMFMediaType
	{
		[PreserveSig] int GetItem(ref Guid key, IntPtr value);
		[PreserveSig] int GetItemType(ref Guid key, out int type);
		[PreserveSig] int CompareItem(ref Guid key, IntPtr value, [MarshalAs(UnmanagedType.Bool)] out bool result);
		[PreserveSig] int Compare(IntPtr other, int type, [MarshalAs(UnmanagedType.Bool)] out bool result);
		[PreserveSig] int GetUINT32(ref Guid key, out int value);
		[PreserveSig] int GetUINT64(ref Guid key, out long value);
		[PreserveSig] int GetDouble(ref Guid key, out double value);
		[PreserveSig] int GetGUID(ref Guid key, out Guid value);
		[PreserveSig] int GetStringLength(ref Guid key, out int length);
		[PreserveSig] int GetString(ref Guid key, IntPtr value, int size, out int length);
		[PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out int length);
		[PreserveSig] int GetBlobSize(ref Guid key, out int size);
		[PreserveSig] int GetBlob(ref Guid key, IntPtr buffer, int size, out int written);
		[PreserveSig] int GetAllocatedBlob(ref Guid key, out IntPtr buffer, out int size);
		[PreserveSig] int GetUnknown(ref Guid key, ref Guid iid, out IntPtr unknown);
		[PreserveSig] int SetItem(ref Guid key, IntPtr value);
		[PreserveSig] int DeleteItem(ref Guid key);
		[PreserveSig] int DeleteAllItems();
		[PreserveSig] int SetUINT32(ref Guid key, int value);
		[PreserveSig] int SetUINT64(ref Guid key, long value);
		[PreserveSig] int SetDouble(ref Guid key, double value);
		[PreserveSig] int SetGUID(ref Guid key, ref Guid value);
	}

	[ComImport]
	[Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface IMFSourceReader
	{
		[PreserveSig] int GetStreamSelection(int stream, [MarshalAs(UnmanagedType.Bool)] out bool selected);
		[PreserveSig] int SetStreamSelection(int stream, [MarshalAs(UnmanagedType.Bool)] bool selected);
		[PreserveSig] int GetNativeMediaType(int stream, int index, out IMFMediaType type);
		[PreserveSig] int GetCurrentMediaType(int stream, out IMFMediaType type);
		[PreserveSig] int SetCurrentMediaType(int stream, IntPtr reserved, IMFMediaType type);
		[PreserveSig] int SetCurrentPosition(ref Guid format, ref PropVariant position);
		[PreserveSig] int ReadSample(int stream, int control, out int actualStream, out int flags, out long timestamp, out IMFSample sample);
	}

	[ComImport]
	[Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface IMFSample
	{
		// IMFAttributes, 30 slots
		void A0(); void A1(); void A2(); void A3(); void A4(); void A5(); void A6(); void A7(); void A8(); void A9();
		void A10(); void A11(); void A12(); void A13(); void A14(); void A15(); void A16(); void A17(); void A18(); void A19();
		void A20(); void A21(); void A22(); void A23(); void A24(); void A25(); void A26(); void A27(); void A28(); void A29();
		[PreserveSig] int GetSampleFlags(out int flags);
		[PreserveSig] int SetSampleFlags(int flags);
		[PreserveSig] int GetSampleTime(out long time);
		[PreserveSig] int SetSampleTime(long time);
		[PreserveSig] int GetSampleDuration(out long duration);
		[PreserveSig] int SetSampleDuration(long duration);
		[PreserveSig] int GetBufferCount(out int count);
		[PreserveSig] int GetBufferByIndex(int index, out IMFMediaBuffer buffer);
		[PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
	}

	[ComImport]
	[Guid("045fa593-8799-42b8-bc8d-8968c6453507")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface IMFMediaBuffer
	{
		[PreserveSig] int Lock(out IntPtr data, out int maxLength, out int currentLength);
		[PreserveSig] int Unlock();
		[PreserveSig] int GetCurrentLength(out int length);
	}
}
