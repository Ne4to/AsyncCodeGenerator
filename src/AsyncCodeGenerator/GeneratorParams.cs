namespace AsyncCodeGenerator
{
	public class GeneratorParams
	{
		public string FilePath { get; set; } 
		public string OutFile { get; set; }
		public bool WriteDoc { get; set; }
		public string DocFile { get; set; }
		public string NamespaceName { get; set; }
		public string ClassName { get; set; }
	}
}