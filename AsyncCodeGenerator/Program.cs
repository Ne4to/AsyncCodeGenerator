using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace AsyncCodeGenerator
{
	class Program
	{
		private const int HelpHeaderWidth = 28;
		private const string ParamsPrefix = "    ";
		private const int HelpTotalHeaderWidth = 32;

		static void Main(string[] args)
		{
			if (args.Length == 0 || args[0] == "/?")
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

			Console.WriteLine("FilePath: {0}", parameters.FilePath);
			Console.WriteLine("OutFile: {0}", parameters.OutFile);
			Console.WriteLine("WriteDoc: {0}", parameters.WriteDoc);
			Console.WriteLine("DocFile: {0}", parameters.DocFile);
			Console.WriteLine("NamespaceName: {0}", parameters.NamespaceName);
			Console.WriteLine("ClassName: {0}", parameters.ClassName);

			AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
			AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += CurrentDomain_ReflectionOnlyAssemblyResolve;

			var generator = new Generator(parameters);
			generator.Build();
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
			Console.WriteLine("https://github.com/Ne4to/AsyncCodeGenerator");
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
		}

		// C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\Silverlight\v5.0\System.Windows.Browser.dll

		static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
		{
			Console.WriteLine("Resolve {0}", args.Name);

			Assembly assembly;
			try
			{
				assembly = Assembly.LoadFrom(args.Name);
			}
			catch (FileNotFoundException)
			{
				if (args.RequestingAssembly == null)
					return null;

				var dirName = Path.GetDirectoryName(args.RequestingAssembly.Location);
				var assemblyName = new AssemblyName(args.Name);
				var fileName = String.Format("{0}.dll", assemblyName.Name);
				var path = Path.Combine(dirName, fileName);
				assembly = Assembly.LoadFrom(path);
			}

			return assembly;
		}

		static Assembly CurrentDomain_ReflectionOnlyAssemblyResolve(object sender, ResolveEventArgs args)
		{
			Console.WriteLine("Resolve {0}", args.Name);

			Assembly assembly = null;
			assembly = LoadByName(args.Name);

			var assemblyName = new AssemblyName(args.Name);
			var fileName = String.Format("{0}.dll", assemblyName.Name);

			if (assembly == null)
				assembly = LoadByRequestingAssemblyLocation(fileName, args.RequestingAssembly);

			if (assembly == null)
				assembly = LoadSilverlightAssembly(fileName);

			return assembly;
		}

		private static Assembly LoadByName(string assemblyString)
		{
			try
			{
				return Assembly.ReflectionOnlyLoad(assemblyString);
			}
			catch (FileNotFoundException)
			{
				return null;
			}
		}

		private static Assembly LoadByRequestingAssemblyLocation(string fileName, Assembly requestingAssembly)
		{
			try
			{
				var dirName = Path.GetDirectoryName(requestingAssembly.Location);
				var path = Path.Combine(dirName, fileName);
				return Assembly.ReflectionOnlyLoadFrom(path);
			}
			catch (FileNotFoundException)
			{
				return null;
			}
		}

		private static Assembly LoadSilverlightAssembly(string fileName)
		{
			var slRootKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SDKs\Silverlight");
			if (slRootKey == null)
				return null;

			var versions = slRootKey.GetSubKeyNames().OrderByDescending(k => k).ToList();

			foreach (var version in versions)
			{
				var installKey = slRootKey.OpenSubKey(version + "\\ReferenceAssemblies");
				if (installKey == null)
					continue;

				var dirName = installKey.GetValue("SLRuntimeInstallPath", String.Empty).ToString();

				if (String.IsNullOrEmpty(dirName))
					continue;

				var assemblyPath = Path.Combine(dirName, fileName);
				try
				{
					return Assembly.ReflectionOnlyLoadFrom(assemblyPath);
				}
				catch (FileNotFoundException) { }
			}

			return null;
		}
	}
}
