using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace AsyncCodeGenerator
{
	public class Generator
	{	
		private readonly GeneratorParams _parameters;

		public Generator(GeneratorParams parameters)
		{
			if (parameters == null) throw new ArgumentNullException("parameters");
			_parameters = parameters;
		}

		public void Build()
		{
			BuildCode(_parameters);
		}

		private void BuildCode(GeneratorParams parameters)
		{
			var sourceAssembly = parameters.FilePath;
			var outFile = parameters.OutFile;
			var namespaceName = parameters.NamespaceName;
			var className = parameters.ClassName;
			var xmlFilePath = parameters.DocFile;

			var assembly = Assembly.ReflectionOnlyLoadFrom(sourceAssembly);
			//var assembly = Assembly.LoadFile(sourceAssembly);
			
			DocumentationBuilder docBuilder = null;
			if (parameters.WriteDoc && File.Exists(xmlFilePath))
			{
				docBuilder = new DocumentationBuilder(xmlFilePath);
			}

			var q = from type in assembly.GetTypes()
				from beginMethod in type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
				where type.IsPublic
				      && beginMethod.DeclaringType == type
				      && beginMethod.ReturnType == typeof(IAsyncResult)
				      && !beginMethod.IsSpecialName
				      && type.BaseType != typeof(MulticastDelegate)
				orderby type.Name, beginMethod.Name
				select beginMethod;


			var targetUnit = new CodeCompileUnit();
			var ns = new CodeNamespace(namespaceName);

			targetUnit.Namespaces.Add(ns);

			var classTypeDec = new CodeTypeDeclaration(className);
			classTypeDec.IsClass = true;
			classTypeDec.TypeAttributes |= TypeAttributes.Public;
			classTypeDec.Attributes = MemberAttributes.Public;

			ns.Types.Add(classTypeDec);
			
			foreach (var methodInfo in q)
			{
				var methodName = methodInfo.Name.Substring("Begin".Length);
				var endMethodName = "End" + methodName;

// ReSharper disable once PossibleNullReferenceException
				var endMethod = methodInfo.DeclaringType.GetMethod(endMethodName);
				if (endMethod == null)
					continue;

				var mth = new CodeMemberMethod();
// ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
				mth.Attributes = MemberAttributes.Public | MemberAttributes.Static;
				mth.Name = methodName + "Async";

				if (docBuilder != null)
				{
					docBuilder.WriteDocs(mth, methodInfo, endMethod);
				}
				AddObsoleteAttribute(methodInfo, mth);

				Type returnType;
				if (endMethod.ReturnType == typeof(void))
				{
					returnType = typeof(Task);
				}
				else
				{
					returnType = typeof(Task<>).MakeGenericType(endMethod.ReturnType);
				}

				mth.ReturnType = new CodeTypeReference(returnType);

				mth.Parameters.Add(new CodeParameterDeclarationExpression("this " + methodInfo.DeclaringType, Constants.SourceObjectParameterName));
				var methodParameters = methodInfo.GetParameters();
				if (methodParameters.Length > 2)
				{
					foreach (var parameterInfo in methodParameters.Take(methodParameters.Length - 2))
					{
						mth.Parameters.Add(new CodeParameterDeclarationExpression(parameterInfo.ParameterType, parameterInfo.Name));
					}
				}

				var throwException = new CodeThrowExceptionStatement(new CodeObjectCreateExpression(new CodeTypeReference(typeof(ArgumentNullException)), new CodePrimitiveExpression(Constants.SourceObjectParameterName)));

				var ifS = new CodeConditionStatement(new CodeBinaryOperatorExpression(new CodeArgumentReferenceExpression(Constants.SourceObjectParameterName), CodeBinaryOperatorType.IdentityEquality, new CodePrimitiveExpression(null)), throwException);
				mth.Statements.Add(ifS);

				var factoryExpression = new CodeFieldReferenceExpression(new CodeTypeReferenceExpression(returnType), "Factory");
				var beginMethodExpr = new CodeFieldReferenceExpression(new CodeArgumentReferenceExpression(Constants.SourceObjectParameterName), methodInfo.Name);
				var endMethodExpr = new CodeFieldReferenceExpression(new CodeArgumentReferenceExpression(Constants.SourceObjectParameterName), endMethodName);

				var factoryMethodParameters = new List<CodeExpression>();
				if (methodParameters.Length > 5)
				{
					var beginInvocationMethodParameters = new List<CodeExpression>();
					foreach (var parameterInfo in methodParameters.Take(methodParameters.Length - 2))
					{
						beginInvocationMethodParameters.Add(new CodeArgumentReferenceExpression(parameterInfo.Name));
					}
					beginInvocationMethodParameters.Add(new CodePrimitiveExpression(null));
					beginInvocationMethodParameters.Add(new CodePrimitiveExpression(null));

					var beginInvocation = new CodeMethodInvokeExpression(new CodeArgumentReferenceExpression(Constants.SourceObjectParameterName), methodInfo.Name, beginInvocationMethodParameters.ToArray());

					factoryMethodParameters.Add(beginInvocation);
					factoryMethodParameters.Add(endMethodExpr);
				}
				else
				{
					factoryMethodParameters.Add(beginMethodExpr);
					factoryMethodParameters.Add(endMethodExpr);
					if (methodParameters.Length > 2)
					{
						foreach (var parameterInfo in methodParameters.Take(methodParameters.Length - 2))
						{
							factoryMethodParameters.Add(new CodeArgumentReferenceExpression(parameterInfo.Name));
						}
					}
					factoryMethodParameters.Add(new CodePrimitiveExpression(null));
				}

				var invocation = new CodeMethodInvokeExpression(factoryExpression, "FromAsync", factoryMethodParameters.ToArray());
				var returnStatement = new CodeMethodReturnStatement
				{
					Expression = invocation
				};

				mth.Statements.Add(returnStatement);


				classTypeDec.Members.Add(mth);
			}

			var provider = CodeDomProvider.CreateProvider("CSharp");


			var options = new CodeGeneratorOptions
			{
				BracingStyle = "C",
				BlankLinesBetweenMembers = true
			};

			using (var sourceWriter = new StreamWriter(outFile))
			{
				//provider.GenerateCodeFromCompileUnit(new CodeSnippetCompileUnit("#pragma warning disable 618"), sourceWriter, options);
				provider.GenerateCodeFromCompileUnit(targetUnit, sourceWriter, options);
				//provider.GenerateCodeFromCompileUnit(new CodeSnippetCompileUnit("#pragma warning restore 618"), sourceWriter, options);
			}

			MakeClassStatic(outFile, className);

			//Console.WriteLine(i.ToString(CultureInfo.InvariantCulture));
			//Console.ReadKey();
		}

		private void AddObsoleteAttribute(MethodInfo beginMethodInfo, CodeMemberMethod resultMethod)
		{
			var attributes = beginMethodInfo.GetCustomAttributesData();
			var obsoleteAttr = attributes.FirstOrDefault(a => a.AttributeType == typeof(ObsoleteAttribute));

			if (obsoleteAttr != null)
			{
				resultMethod.CustomAttributes.Add(new CodeAttributeDeclaration(new CodeTypeReference(typeof(ObsoleteAttribute)),
					new CodeAttributeArgument(new CodePrimitiveExpression(obsoleteAttr.ConstructorArguments[0].Value))));
			}
		}

		private void MakeClassStatic(string fileName, string className)
		{
			var content = File.ReadAllText(fileName);
			content = content.Replace("class " + className, "static class " + className);
			File.WriteAllText(fileName, content);
		}
	}
}