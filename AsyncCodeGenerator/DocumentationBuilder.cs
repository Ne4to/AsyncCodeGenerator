using System;
using System.CodeDom;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace AsyncCodeGenerator
{
	public class DocumentationBuilder
	{
		private readonly XDocument _doc;

		public DocumentationBuilder(string xmlFilePath)
		{
			if (File.Exists(xmlFilePath))
			{
				_doc = XDocument.Load(xmlFilePath);
			}
		}

		public void WriteDocs(CodeMemberMethod asyncMethod, MethodInfo beginMethod, MethodInfo endMethod)
		{
			if (asyncMethod == null) throw new ArgumentNullException("asyncMethod");
			if (beginMethod == null) throw new ArgumentNullException("beginMethod");
			if (endMethod == null) throw new ArgumentNullException("endMethod");

			if (_doc == null)
				return;

			var beginMethodMemberName = GetMemberName(beginMethod);
			var beginMethodNode = _doc.Descendants(XName.Get("member")).FirstOrDefault(m => m.Attribute("name").Value == beginMethodMemberName);

			var endMethodMemberName = GetMemberName(endMethod);
			var endMethodNode = _doc.Descendants(XName.Get("member")).FirstOrDefault(m => m.Attribute("name").Value == endMethodMemberName);

			WriteNode(asyncMethod, beginMethodNode, "summary");
			WriteParams(asyncMethod, beginMethod, beginMethodNode);
			WriteNode(asyncMethod, beginMethodNode, "remarks");
			WriteNode(asyncMethod, endMethodNode, "returns");
			
			WriteExceptions(asyncMethod, beginMethodNode);
			WriteExceptions(asyncMethod, endMethodNode);
		}

		private void WriteParams(CodeMemberMethod asyncMethod, MethodInfo beginMethod, XElement originalMethodNode)
		{			
			WriteParam(asyncMethod, Constants.SourceObjectParameterName, "The source object");

			if (originalMethodNode == null)
				return;

			var methodParameters = beginMethod.GetParameters();
			if (methodParameters.Length > 2)
			{
				foreach (var parameterInfo in methodParameters.Take(methodParameters.Length - 2))
				{					
					var paramNode = originalMethodNode.Elements("param").FirstOrDefault(p => p.Attribute("name").Value == parameterInfo.Name);
					if (paramNode != null)
					{
						WriteParam(asyncMethod, parameterInfo.Name, paramNode.Value.Trim());
					}
				}
			}
		}

		private static void WriteParam(CodeMemberMethod asyncMethod, string name, string value)
		{
			asyncMethod.Comments.Add(new CodeCommentStatement(String.Format("<param name=\"{0}\">", name), true));
			asyncMethod.Comments.Add(new CodeCommentStatement(value, true));
			asyncMethod.Comments.Add(new CodeCommentStatement("</param>", true));
		}

		private static void WriteNode(CodeMemberMethod asyncMethod, XElement originalMethodNode, string elementName)
		{
			if (originalMethodNode == null)
				return;

			var summaryNode = originalMethodNode.Element(elementName);
			if (summaryNode != null)
			{
				asyncMethod.Comments.Add(new CodeCommentStatement(String.Format("<{0}>", elementName), true));
				asyncMethod.Comments.Add(new CodeCommentStatement(summaryNode.Value.Trim(), true));
				asyncMethod.Comments.Add(new CodeCommentStatement(String.Format("</{0}>", elementName), true));
			}
		}

		private void WriteExceptions(CodeMemberMethod asyncMethod, XElement originalMethodNode)
		{
			if (originalMethodNode == null)
				return;

			foreach (var exElement in originalMethodNode.Elements("exception"))
			{
				var crefAttr = exElement.Attribute("cref");
				if (crefAttr == null)
					continue;

				var beginStatement = String.Format("<exception cref=\"{0}\">", crefAttr.Value);
				asyncMethod.Comments.Add(new CodeCommentStatement(beginStatement, true));
				asyncMethod.Comments.Add(new CodeCommentStatement(exElement.Value.Trim(), true));
				asyncMethod.Comments.Add(new CodeCommentStatement("</exception>", true));
			}
		}

		private string GetMemberName(MethodInfo methodInfo)
		{
			var result = new StringBuilder("M:");
			result.Append(methodInfo.DeclaringType.FullName);
			result.Append('.');
			result.Append(methodInfo.Name);
			result.Append('(');

			for (int parameterIndex = 0; parameterIndex < methodInfo.GetParameters().Length; parameterIndex++)
			{
				var parameterInfo = methodInfo.GetParameters()[parameterIndex];

				if (parameterIndex > 0)
				{
					result.Append(',');
				}

				if (parameterInfo.ParameterType.IsGenericType)
				{
					var name = parameterInfo.ParameterType.GetGenericTypeDefinition().FullName;
					var ind = name.IndexOf('`');
					result.Append(name.Substring(0, ind));
				}
				else
				{
					result.Append(parameterInfo.ParameterType.FullName);
				}

				if (parameterInfo.ParameterType.GenericTypeArguments.Length > 0)
				{
					result.Append('{');
					for (int argIndex = 0; argIndex < parameterInfo.ParameterType.GenericTypeArguments.Length; argIndex++)
					{
						var genericTypeArgument = parameterInfo.ParameterType.GenericTypeArguments[argIndex];

						if (argIndex > 0)
						{
							result.Append(',');
						}

						result.Append(genericTypeArgument.FullName);
					}
					result.Append('}');
				}
			}

			result.Append(')');

			return result.ToString();
		}
	}
}