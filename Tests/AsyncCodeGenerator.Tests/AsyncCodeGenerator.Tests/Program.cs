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
			var generatorPath = @"C:\src\GitHub\AsyncCodeGenerator\AsyncCodeGenerator\bin\x64\Debug\AsyncCodeGenerator.exe";
			var exeLocation = Directory.GetParent(Assembly.GetExecutingAssembly().Location);
			var solutionDir = exeLocation.Parent.Parent.Parent.FullName;

			var parameters = String.Format("\"{0}\" /out:\"{1}\"", 
				Path.Combine(solutionDir, @"Libs\Desktop\Microsoft.Lync.Model.dll"),
				Path.Combine(solutionDir, @"LyncDesktop\AsyncExtensions.cs"));
			Process.Start(generatorPath, parameters);
			
			parameters = String.Format("\"{0}\" /out:\"{1}\"",
				Path.Combine(solutionDir, @"Libs\Silverlight\Microsoft.Lync.Model.dll"),
				Path.Combine(solutionDir, @"LyncSilverlight\AsyncExtensions.cs"));
			Process.Start(generatorPath, parameters);

			parameters = String.Format("\"{0}\" /out:\"{1}\"",
				Path.Combine(solutionDir, @"Libs\Desktop\Microsoft.Rtc.Collaboration.dll"),
				Path.Combine(solutionDir, @"Rtc\AsyncExtensions.cs"));
			Process.Start(generatorPath, parameters);
		}
	}
}
