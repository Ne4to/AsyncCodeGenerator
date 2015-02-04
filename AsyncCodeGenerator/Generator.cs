using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CSharp;

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
			ns.Comments.Add(new CodeCommentStatement("The file was created by AsyncCodeGenerator."));
			var toolVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
			ns.Comments.Add(new CodeCommentStatement("Version " + toolVersion));
			ns.Comments.Add(new CodeCommentStatement("https://github.com/Ne4to/AsyncCodeGenerator"));

			targetUnit.Namespaces.Add(ns);

			var classTypeDec = new CodeTypeDeclaration(className);
			classTypeDec.IsClass = true;
			classTypeDec.TypeAttributes |= TypeAttributes.Public;
			classTypeDec.Attributes = MemberAttributes.Public;

			classTypeDec.CustomAttributes.Add(new CodeAttributeDeclaration(new CodeTypeReference(typeof (DebuggerStepThroughAttribute))));

			ns.Types.Add(classTypeDec);

			foreach (var beginMethod in q)
			{
				var methodName = beginMethod.Name.Substring("Begin".Length);
				var endMethodName = "End" + methodName;

				// ReSharper disable once PossibleNullReferenceException
				var endMethod = beginMethod.DeclaringType.GetMethod(endMethodName);
				if (endMethod == null)
					continue;

				var mth = CreateExtensionMethod(methodName, endMethod, beginMethod);

				if (docBuilder != null)
				{
					docBuilder.WriteDocs(mth, beginMethod, endMethod);
				}
				AddObsoleteAttribute(beginMethod, mth);

				var throwException =
					new CodeThrowExceptionStatement(
						new CodeObjectCreateExpression(new CodeTypeReference(typeof(ArgumentNullException)),
							new CodePrimitiveExpression(Constants.SourceObjectParameterName)));

				var ifS =
					new CodeConditionStatement(
						new CodeBinaryOperatorExpression(new CodeArgumentReferenceExpression(Constants.SourceObjectParameterName),
							CodeBinaryOperatorType.IdentityEquality, new CodePrimitiveExpression(null)), throwException);
				mth.Statements.Add(ifS);

				var factoryExpression =
					new CodeFieldReferenceExpression(new CodeTypeReferenceExpression(GetFactoryTaskType(endMethod)), "Factory");
				var beginMethodExpr =
					new CodeFieldReferenceExpression(new CodeArgumentReferenceExpression(Constants.SourceObjectParameterName),
						beginMethod.Name);
				var endMethodExpr =
					new CodeFieldReferenceExpression(new CodeArgumentReferenceExpression(Constants.SourceObjectParameterName),
						endMethodName);

				if (endMethod.GetParameters().Any(p => p.IsOut))
				{
					// ReSharper disable once PossibleNullReferenceException
					var resultTypeName = endMethod.DeclaringType.Name + methodName + "Result";

					if (ns.Types.Cast<CodeTypeDeclaration>().All(t => t.Name != resultTypeName))
					{
						var resultType = CreateResultType(resultTypeName, endMethod);
						ns.Types.Add(resultType);
					}

					DeclareTempVariables(endMethod, mth);
					var factoryMethodParameters = new List<CodeExpression>();
					var beginInvocation = GetBeginInvokeExpression(beginMethod);
					factoryMethodParameters.Add(beginInvocation);

					var endMethodParameters = new List<CodeExpression>();
					foreach (var parameterInfo in endMethod.GetParameters())
					{
						if (parameterInfo.IsOut)
						{
							var varName = parameterInfo.Name + "Tmp";
							endMethodParameters.Add(new CodeArgumentReferenceExpression("out " + varName));
						}
						else
						{
							endMethodParameters.Add(new CodeArgumentReferenceExpression("ar"));
						}
					}
					var endInvokeExpression = new CodeMethodInvokeExpression(new CodeArgumentReferenceExpression(Constants.SourceObjectParameterName), endMethod.Name, endMethodParameters.ToArray());

					var csc = new CSharpCodeProvider();
					var sw = new StringWriter();
					csc.GenerateCodeFromExpression(endInvokeExpression, sw, new CodeGeneratorOptions());

					var exxx = new CodeSnippetExpression("ar => " + sw);
					factoryMethodParameters.Add(exxx);

					var invocation = new CodeMethodInvokeExpression(factoryExpression, "FromAsync", factoryMethodParameters.ToArray());
					invocation = new CodeMethodInvokeExpression(invocation, "ConfigureAwait", new CodePrimitiveExpression(false));
					mth.Statements.Add(invocation);

					GetReturnStatement(resultTypeName, mth, endMethod);
				}
				else
				{
					var methodParameters = beginMethod.GetParameters();
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

						var beginInvocation =
							new CodeMethodInvokeExpression(new CodeArgumentReferenceExpression(Constants.SourceObjectParameterName),
								beginMethod.Name, beginInvocationMethodParameters.ToArray());

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
				}

				classTypeDec.Members.Add(mth);
			}

			var provider = CodeDomProvider.CreateProvider("CSharp");


			var options = new CodeGeneratorOptions
			{
				BracingStyle = "C",
				BlankLinesBetweenMembers = true,				
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

		private static CodeMemberMethod CreateExtensionMethod(string methodName, MethodInfo endMethod, MethodInfo beginMethod)
		{
			var method = new CodeMemberMethod();
			// ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
			method.Attributes = MemberAttributes.Public | MemberAttributes.Static;
			method.Name = methodName + "Async";
			method.ReturnType = GetExtensionMethodReturnType(endMethod);

			method.Parameters.Add(new CodeParameterDeclarationExpression("this " + beginMethod.DeclaringType, Constants.SourceObjectParameterName));

			var methodParameters = beginMethod.GetParameters();
			if (methodParameters.Length > 2)
			{
				foreach (var parameterInfo in methodParameters.Take(methodParameters.Length - 2))
				{
					method.Parameters.Add(new CodeParameterDeclarationExpression(parameterInfo.ParameterType, parameterInfo.Name));
				}
			}

			return method;
		}

		public static CodeTypeReference GetExtensionMethodReturnType(MethodInfo endMethod)
		{
			if (endMethod.GetParameters().Any(p => p.IsOut))
			{
				var methodName = endMethod.Name.Substring("End".Length);
				// ReSharper disable once PossibleNullReferenceException
				var resultTypeName = endMethod.DeclaringType.Name + methodName + "Result";
				return new CodeTypeReference(String.Format("async System.Threading.Tasks.Task<{0}>", resultTypeName));
			}

			if (endMethod.ReturnType == typeof(void))
			{
				return new CodeTypeReference(typeof(Task));
			}

			return new CodeTypeReference(typeof(Task<>).MakeGenericType(endMethod.ReturnType));
		}

		private static void GetReturnStatement(string resultTypeName, CodeMemberMethod mth, MethodInfo endMethod)
		{
			var resultValueDecl = new CodeVariableDeclarationStatement(resultTypeName, "result",
				new CodeObjectCreateExpression(resultTypeName));
			mth.Statements.Add(resultValueDecl);
			foreach (var parameterInfo in endMethod.GetParameters())
			{
				if (parameterInfo.IsOut)
				{
					var varName = parameterInfo.Name + "Tmp";
					var as1 =
						new CodeAssignStatement(
							new CodePropertyReferenceExpression(new CodeVariableReferenceExpression(resultValueDecl.Name), parameterInfo.Name),
							new CodeVariableReferenceExpression(varName));
					mth.Statements.Add(as1);
				}
			}

			var returnStatement = new CodeMethodReturnStatement(new CodeVariableReferenceExpression(resultValueDecl.Name));
			mth.Statements.Add(returnStatement);
		}

		private static CodeMethodInvokeExpression GetBeginInvokeExpression(MethodInfo methodInfo)
		{
			var methodParameters = methodInfo.GetParameters();

			var beginInvocationMethodParameters = new List<CodeExpression>();
			foreach (var parameterInfo in methodParameters.Take(methodParameters.Length - 2))
			{
				beginInvocationMethodParameters.Add(new CodeArgumentReferenceExpression(parameterInfo.Name));
			}
			beginInvocationMethodParameters.Add(new CodePrimitiveExpression(null));
			beginInvocationMethodParameters.Add(new CodePrimitiveExpression(null));

			return new CodeMethodInvokeExpression(new CodeArgumentReferenceExpression(Constants.SourceObjectParameterName), methodInfo.Name, beginInvocationMethodParameters.ToArray());
		}

		private static CodeTypeDeclaration CreateResultType(string resultTypeName, MethodInfo endMethod)
		{
			var resultType = new CodeTypeDeclaration(resultTypeName);
			resultType.IsClass = true;

			foreach (var parameterInfo in endMethod.GetParameters())
			{
				if (parameterInfo.IsOut)
				{
					var t = parameterInfo.ParameterType.FullName.Substring(0, parameterInfo.ParameterType.FullName.Length - 1);

					var field = new CodeMemberField
					{
						// ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
						Attributes = MemberAttributes.Public | MemberAttributes.Final,
						Name = parameterInfo.Name,
						Type = new CodeTypeReference(t),
					};

					field.Name += " { get; set; }//";

					// TODO add comment from out parameter

					resultType.Members.Add(field);
				}
			}
			return resultType;
		}

		private static void DeclareTempVariables(MethodInfo endMethod, CodeMemberMethod mth)
		{
			foreach (var parameterInfo in endMethod.GetParameters())
			{
				if (parameterInfo.IsOut)
				{
					var t = parameterInfo.ParameterType.FullName.Substring(0, parameterInfo.ParameterType.FullName.Length - 1);
					var varName = parameterInfo.Name + "Tmp";
					var x = new CodeVariableDeclarationStatement(t, varName, new CodeDefaultValueExpression(new CodeTypeReference(t)));
					mth.Statements.Add(x);
				}
			}
		}

		public static CodeTypeReference GetFactoryTaskType(MethodInfo endMethod)
		{
			if (endMethod.GetParameters().Any(p => p.IsOut))
			{
				return new CodeTypeReference("await System.Threading.Tasks.Task");
			}

			if (endMethod.ReturnType == typeof(void))
			{
				return new CodeTypeReference(typeof(Task));
			}

			return new CodeTypeReference(typeof(Task<>).MakeGenericType(endMethod.ReturnType));
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
			content = content.Replace(@"//;", String.Empty);
			File.WriteAllText(fileName, content);
		}
	}
}