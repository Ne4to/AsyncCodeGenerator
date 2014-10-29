using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AsyncCodeGenerator
{
	class Program
	{
		private const int HelpHeaderWidth = 28;
		private const string ParamsPrefix = "    ";
		private const int HelpTotalHeaderWidth = 32;

		static void Main(string[] args)
		{
			if (args.Length == 0 || args[1] == "/?")
			{
				WriteInfo();
				return;
			}

			string filePath = args[0];
			var outFile = GetParameter(args, "out");
			var writeDoc = GetParameter(args, "writeDoc");
			var docFile = GetParameter(args, "docFile");
			var ns = GetParameter(args, "ns");
			var className = GetParameter(args, "class");

			var parameters = new GeneratorParams
			{
				FilePath = filePath,
				OutFile = outFile ?? Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath) + ".AsyncExtensions.cs"),
				WriteDoc = writeDoc == null || writeDoc == "yes",
				DocFile = docFile ?? Path.ChangeExtension(filePath, "xml"),
				NamespaceName = ns ?? Path.GetFileNameWithoutExtension(filePath) + ".Extensions",
				ClassName = className ?? "AsyncExtensions"
			};

			AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += CurrentDomain_ReflectionOnlyAssemblyResolve;

			var generator = new Generator(parameters);
			generator.Build();
			//BuildCode(@"C:\src\I-AS-0114\AttendantConsole\Libs\UCMA 4.0\Microsoft.Rtc.Collaboration.dll",
			//			@"C:\src\I-AS-0114\AttendantConsole\Microsoft.Rtc.Collaboration.Extensions\AsyncExtensions2.cs",
			//			"Microsoft.Rtc.Collaboration.Extensions", "AsyncExtensions");

			//BuildCode(@"C:\Program Files\Microsoft UCMA 4.0\SDK\Core\Bin\Microsoft.Rtc.Collaboration.dll",
			//			@"D:\temp\async\1.cs",
			//			"Microsoft.Rtc.Collaboration.Extensions", "AsyncExtensions");
		}

		private static string GetParameter(string[] args, string name)
		{
			var prefix = String.Format("/{0}:", name);
			var arg = args.FirstOrDefault(p => p.StartsWith(prefix));
			if (arg == null)
				return null;

			var index = arg.IndexOf(':');
			return arg.Substring(index + 1);
		}

		private static void WriteInfo()
		{
			Console.WriteLine("AsyncCodeGenerator - Task base async code generator");
			Console.WriteLine();

			Console.WriteLine("Syntax: AsyncCodeGenerator.exe <filePath> [/out:<outFilePath>] [/writeDoc:<yes|no>] [/docFile:<xmlDocFilePath>] [/ns:<ClassNamespace>] [/class:<ClassName>]");
			Console.WriteLine();
			
			Console.WriteLine("Options:");
			Console.WriteLine(ParamsPrefix + "<filePath>".PadRight(HelpHeaderWidth) + "The source assembly file path");
			Console.WriteLine(ParamsPrefix + "/out:<outFilePath>".PadRight(HelpHeaderWidth) + "The output source code file path");
			Console.WriteLine(ParamsPrefix + "/writeDoc:<yes|no>".PadRight(HelpHeaderWidth) + "Write or not XML documentation");
			Console.WriteLine(ParamsPrefix + "/docFile:<xmlDocFilePath>".PadRight(HelpHeaderWidth) + "The source assembly XML documentation file path");
			Console.WriteLine(String.Empty.PadRight(HelpTotalHeaderWidth) + "default '<filename>.xml'");
			Console.WriteLine(ParamsPrefix + "/ns:<ClassNamespace>".PadRight(HelpHeaderWidth) + "Output class namespace");
			Console.WriteLine(String.Empty.PadRight(HelpTotalHeaderWidth) + "default '<filename>.Extensions'");
			Console.WriteLine(ParamsPrefix + "/class:<ClassName>".PadRight(HelpHeaderWidth) + "Output class name");
			Console.WriteLine(String.Empty.PadRight(HelpTotalHeaderWidth) + "default 'AsyncExtensions'");
			Console.WriteLine();

			//Console.WriteLine(
			//	"Example: AsyncCodeGenerator.exe \"http://www.onvif.org/onvif/ver10/device/wsdl/devicemgmt.wsdl\" \"C:\\temp\\devicemgmt.wsdl.cs\" OnvifServices.DeviceManagement");
			//Console.WriteLine(
			//	"Example: AsyncCodeGenerator.exe \"http://www.onvif.org/onvif/ver10/device/wsdl/devicemgmt.wsdl\" \"C:\\temp\\devicemgmt.wsdl.cs\" OnvifServices.DeviceManagement /svcutil:\"C:\\Program Files (x86)\\Microsoft SDKs\\Windows\\v8.0A\\bin\\NETFX 4.0 Tools\\SvcUtil.exe\"");
			Console.WriteLine();
		}

		static Assembly CurrentDomain_ReflectionOnlyAssemblyResolve(object sender, ResolveEventArgs args)
		{
			var assembly = Assembly.ReflectionOnlyLoad(args.Name);
			return assembly;
		}
	}
}
