using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AsyncCodeGenerator.Tests
{
	class Program
	{
		static void Main(string[] args)
		{
			var generatorPath = @"C:\src\GitHub\AsyncCodeGenerator\src\AsyncCodeGenerator\bin\x64\Debug\AsyncCodeGenerator.exe";
			var exeLocation = Directory.GetParent(Assembly.GetExecutingAssembly().Location);
			var solutionDir = exeLocation.Parent.Parent.Parent.FullName;

			var parameters = String.Format("\"{0}\" /out:\"{1}\"", 
				Path.Combine(solutionDir, @"Libs\Desktop\Microsoft.Lync.Model.dll"),
				Path.Combine(solutionDir, @"LyncDesktop\AsyncExtensions.cs"));
			Process.Start(generatorPath, parameters).WaitForExit();
			
			parameters = String.Format("\"{0}\" /out:\"{1}\"",
				Path.Combine(solutionDir, @"Libs\Silverlight\Microsoft.Lync.Model.dll"),
				Path.Combine(solutionDir, @"LyncSilverlight\AsyncExtensions.cs"));
			Process.Start(generatorPath, parameters).WaitForExit();

			parameters = String.Format("\"{0}\" /out:\"{1}\"",
				Path.Combine(solutionDir, @"Libs\Desktop\Microsoft.Rtc.Collaboration.dll"),
				Path.Combine(solutionDir, @"Rtc\AsyncExtensions.cs"));
			Process.Start(generatorPath, parameters).WaitForExit();

			//var frameworkDir = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\";
			//foreach (var file in Directory.GetFiles(frameworkDir, "*.dll"))
			//{
			//	var outFile = Path.GetFileName(file).Replace(".dll", String.Empty);
			//	outFile += ".AsyncExtensions.cs";

			//	parameters = String.Format("\"{0}\" /out:\"{1}\"",
			//		file,
			//		Path.Combine(solutionDir, @"DotNet\" + outFile));
			//	Process.Start(generatorPath, parameters).WaitForExit();
			//}
		}
	}
}
