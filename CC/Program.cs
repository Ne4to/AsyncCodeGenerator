using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Configuration.Assemblies;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.CSharp;

namespace AsyncHelperGenerator
{
	class Program
	{
		private const string SourceObjectParameterName = "obj";

		static void Main(string[] args)
		{
			AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += CurrentDomain_ReflectionOnlyAssemblyResolve;

			BuildCode(@"C:\src\nov-TFS02\I-AS-0114\AttendantConsole\Libs\UCMA 4.0\Microsoft.Rtc.Collaboration.dll",
					@"C:\src\nov-TFS02\I-AS-0114\AttendantConsole\Microsoft.Rtc.Collaboration.Extensions\AsyncExtensions2.cs",
						"Microsoft.Rtc.Collaboration.Extensions", "AsyncExtensions");

			BuildCode(@"C:\src\nov-TFS02\I-AS-0114\AttendantConsole\Libs\LyncSDK\Desktop\Microsoft.Lync.Model.dll",
					@"C:\src\nov-TFS02\I-AS-0114\AttendantConsole\LyncExtensions.Desktop\AsyncExtensions.cs",
						"Microsoft.Lync.Model.Extensions", "AsyncExtensions");

			BuildCode(@"C:\src\nov-TFS02\I-AS-0114\AttendantConsole\Libs\LyncSDK\Silverlight\Microsoft.Lync.Model.dll",
				@"C:\src\nov-TFS02\I-AS-0114\AttendantConsole\LyncExtensions.Silverlight\AsyncExtensions.cs",
					"Microsoft.Lync.Model.Extensions", "AsyncExtensions");

			Console.WriteLine("Press any key");
			Console.ReadKey();
		}

		static Assembly CurrentDomain_ReflectionOnlyAssemblyResolve(object sender, ResolveEventArgs args)
		{
			return Assembly.ReflectionOnlyLoadFrom(args.Name);
		}

		private static void BuildCode(string sourceAssembly, string outFile, string namespaceName, string className)
		{
			//var hash = MD5.Create().ComputeHash(File.ReadAllBytes(sourceAssembly));
			//var assembly = Assembly.LoadFrom(sourceAssembly, hash, AssemblyHashAlgorithm.MD5);

			//var assembly = Assembly.ReflectionOnlyLoadFrom(sourceAssembly);

			var assembly = Assembly.LoadFrom(sourceAssembly);

			var q = from type in assembly.GetTypes()
					from beginMethod in type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
					where type.IsPublic
						  && beginMethod.DeclaringType == type
						  && beginMethod.ReturnType == typeof(IAsyncResult)
						  && !beginMethod.IsSpecialName
						  && type.BaseType != typeof(MulticastDelegate)
						  && !beginMethod.Attributes.HasFlag(MethodAttributes.Private)
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

			int i = 0;
			foreach (var beginMethod in q)
			{
				var methodName = beginMethod.Name.Substring("Begin".Length);
				var endMethodName = "End" + methodName;

				var endMethod = beginMethod.DeclaringType.GetMethod(endMethodName);
				if (endMethod == null)
					continue;

				i++;

				var mth = CreateExtensionMethod(methodName, endMethod, beginMethod);

				var ifS = GetThrowStatement();
				mth.Statements.Add(ifS);

				var factoryExpression = new CodeFieldReferenceExpression(new CodeTypeReferenceExpression(GetFactoryTaskType(endMethod)), "Factory");
				var beginMethodExpr = new CodeFieldReferenceExpression(new CodeArgumentReferenceExpression(SourceObjectParameterName), beginMethod.Name);
				var endMethodExpr = new CodeFieldReferenceExpression(new CodeArgumentReferenceExpression(SourceObjectParameterName), endMethodName);

				if (endMethod.GetParameters().Any(p => p.IsOut))
				{
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
					var endInvokeExpression = new CodeMethodInvokeExpression(new CodeArgumentReferenceExpression(SourceObjectParameterName), endMethod.Name, endMethodParameters.ToArray());

					var csc = new CSharpCodeProvider();
					var sw = new StringWriter();
					csc.GenerateCodeFromExpression(endInvokeExpression, sw, new CodeGeneratorOptions());

					var exxx = new CodeSnippetExpression("ar => " + sw);
					factoryMethodParameters.Add(exxx);

					var invocation = new CodeMethodInvokeExpression(factoryExpression, "FromAsync", factoryMethodParameters.ToArray());
					mth.Statements.Add(invocation);

					GetReturnStatement(resultTypeName, mth, endMethod);
				}
				else
				{
					var methodParameters = beginMethod.GetParameters();
					IEnumerable<CodeExpression> factoryMethodParameters;
					if (methodParameters.Length > 5)
					{
						factoryMethodParameters = new CodeExpression[] { GetBeginInvokeExpression(beginMethod), endMethodExpr };
					}
					else
					{
						factoryMethodParameters = SimpleGenerator(beginMethodExpr, endMethodExpr, methodParameters);
					}

					var invocation = new CodeMethodInvokeExpression(factoryExpression, "FromAsync", factoryMethodParameters.ToArray());
					var returnStatement = new CodeMethodReturnStatement();
					returnStatement.Expression = invocation;

					mth.Statements.Add(returnStatement);
				}

				classTypeDec.Members.Add(mth);
			}

			var provider = CodeDomProvider.CreateProvider("CSharp");


			var options = new CodeGeneratorOptions();
			options.BracingStyle = "C";
			options.BlankLinesBetweenMembers = true;
			using (var sourceWriter = new StreamWriter(outFile))
			{
				//provider.GenerateCodeFromCompileUnit(new CodeSnippetCompileUnit("#pragma warning disable 618"), sourceWriter, options);
				provider.GenerateCodeFromCompileUnit(targetUnit, sourceWriter, options);
				//provider.GenerateCodeFromCompileUnit(new CodeSnippetCompileUnit("#pragma warning restore 618"), sourceWriter, options);
			}

			var fileContent = File.ReadAllText(outFile);
			fileContent = fileContent.Replace("public class " + className, "public static class " + className);
			File.WriteAllText(outFile, fileContent);

			Console.WriteLine("Done '{0}', {1} functions", assembly.GetName().Name, i);
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
						Attributes = MemberAttributes.Public | MemberAttributes.Final,
						Name = parameterInfo.Name,
						Type = new CodeTypeReference(t),
					};

					field.Name += " { get; set; }//";

					resultType.Members.Add(field);
				}
			}
			return resultType;
		}

		private static CodeConditionStatement GetThrowStatement()
		{
			var condition = new CodeBinaryOperatorExpression(new CodeArgumentReferenceExpression(SourceObjectParameterName), CodeBinaryOperatorType.IdentityEquality, new CodePrimitiveExpression(null));
			var throwException = new CodeThrowExceptionStatement(new CodeObjectCreateExpression(new CodeTypeReference(typeof(ArgumentNullException)), new CodePrimitiveExpression(SourceObjectParameterName)));
			return new CodeConditionStatement(condition, throwException);
		}

		private static CodeMemberMethod CreateExtensionMethod(string methodName, MethodInfo endMethod, MethodInfo beginMethod)
		{
			var method = new CodeMemberMethod();
			method.Attributes = MemberAttributes.Public | MemberAttributes.Static;
			method.Name = methodName + "Async";
			method.ReturnType = GetExtensionMethodReturnType(endMethod);

			method.Parameters.Add(new CodeParameterDeclarationExpression("this " + beginMethod.DeclaringType, SourceObjectParameterName));

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
				var resultTypeName = endMethod.DeclaringType.Name + methodName + "Result";
				return new CodeTypeReference(String.Format("async System.Threading.Tasks.Task<{0}>", resultTypeName));
			}

			if (endMethod.ReturnType == typeof(void))
			{
				return new CodeTypeReference(typeof(Task));
			}

			return new CodeTypeReference(typeof(Task<>).MakeGenericType(endMethod.ReturnType));
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

		private static IEnumerable<CodeExpression> SimpleGenerator(CodeFieldReferenceExpression beginMethodExpr, CodeFieldReferenceExpression endMethodExpr, ParameterInfo[] methodParameters)
		{
			var result = new List<CodeExpression> { beginMethodExpr, endMethodExpr };

			if (methodParameters.Length > 2)
			{
				foreach (var parameterInfo in methodParameters.Take(methodParameters.Length - 2))
				{
					result.Add(new CodeArgumentReferenceExpression(parameterInfo.Name));
				}
			}

			result.Add(new CodePrimitiveExpression(null));

			return result;
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

			return new CodeMethodInvokeExpression(new CodeArgumentReferenceExpression(SourceObjectParameterName), methodInfo.Name, beginInvocationMethodParameters.ToArray());
		}
	}
}
