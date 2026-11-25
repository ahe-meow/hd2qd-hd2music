using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Hd2Qd.HD2Music;

public static class QrCode
{
	private static Assembly _assembly;

	private static Type _generatorType;

	private static Type _eccType;

	private static Type _eciType;

	private static MethodInfo _create;

	public static string MakeMatrix(string text)
	{
		EnsureLoaded();
		object obj = Enum.Parse(_eccType, "M");
		object obj2 = Activator.CreateInstance(_generatorType);
		object obj3 = _create.Invoke(obj2, new object[6]
		{
			text,
			obj,
			false,
			false,
			Activator.CreateInstance(_eciType),
			-1
		});
		try
		{
			object value = obj3.GetType().GetProperty("ModuleMatrix").GetValue(obj3, null);
			List<BitArray> list = new List<BitArray>();
			foreach (object item in (IEnumerable)value)
			{
				list.Add((BitArray)item);
			}
			int count = list.Count;
			if (count < 9)
			{
				throw new InvalidOperationException("QR matrix too small");
			}
			int num = 4;
			StringBuilder stringBuilder = new StringBuilder(count * count);
			for (int i = num; i < count - num; i++)
			{
				BitArray bitArray = list[i];
				if (stringBuilder.Length > 0)
				{
					stringBuilder.Append('/');
				}
				for (int j = num; j < bitArray.Length - num; j++)
				{
					stringBuilder.Append(bitArray[j] ? '1' : '0');
				}
			}
			return stringBuilder.ToString();
		}
		finally
		{
			if (obj3 is IDisposable disposable)
			{
				disposable.Dispose();
			}
		}
	}

	private static void EnsureLoaded()
	{
		if (_create != null)
		{
			return;
		}
		// the encoder travels inside this DLL (QRCoder.dll, MIT), so a plugin is one file
		using (Stream embedded = typeof(QrCode).Assembly.GetManifestResourceStream("QRCoder.dll"))
		{
			if (embedded != null)
			{
				using (MemoryStream buffer = new MemoryStream())
				{
					embedded.CopyTo(buffer);
					_assembly = Assembly.Load(buffer.ToArray());
				}
			}
		}
		if (_assembly == null)
		{
			_assembly = Assembly.LoadFrom(FindAssembly() ?? throw new InvalidOperationException("找不到二维码组件 QRCoder（内嵌资源缺失）"));
		}
		_generatorType = _assembly.GetType("QRCoder.QRCodeGenerator", throwOnError: true);
		_eccType = _assembly.GetType("QRCoder.QRCodeGenerator+ECCLevel", throwOnError: true);
		_eciType = _assembly.GetType("QRCoder.QRCodeGenerator+EciMode", throwOnError: true);
		MethodInfo[] methods = _generatorType.GetMethods(BindingFlags.Instance | BindingFlags.Public);
		foreach (MethodInfo methodInfo in methods)
		{
			if (!(methodInfo.Name != "CreateQrCode"))
			{
				ParameterInfo[] parameters = methodInfo.GetParameters();
				if (parameters.Length == 6 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == _eccType)
				{
					_create = methodInfo;
					break;
				}
			}
		}
		if (!(_create == null))
		{
			return;
		}
		throw new InvalidOperationException("QRCoder.dll 版本不兼容（缺少 CreateQrCode）");
	}

	private static string FindAssembly()
	{
		List<string> list = new List<string>();
		list.Add(Path.Combine(Util.PluginDir, "QRCoder.dll"));
		list.Add(Path.Combine(Util.SettingsHome, "QRCoder.dll"));
		list.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QRCoder.dll"));
		try
		{
			string location = Assembly.GetExecutingAssembly().Location;
			if (!string.IsNullOrEmpty(location))
			{
				list.Add(Path.Combine(Path.GetDirectoryName(location), "QRCoder.dll"));
			}
		}
		catch
		{
		}
		foreach (string item in list)
		{
			if (File.Exists(item))
			{
				return item;
			}
		}
		return null;
	}
}
