﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace WasmWrangler.BindingGenerator
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("ERROR: Please provide at least one file WasmWrangler bindings file.");
                return 1;
            }

            foreach (var inputFile in args)
            {
                var outputFile = Path.Combine(Path.GetDirectoryName(inputFile)!, Path.GetFileNameWithoutExtension(inputFile) + ".g.cs");

                Console.WriteLine($"{inputFile} => {outputFile}");

                var syntaxTree = CSharpSyntaxTree.ParseText(File.ReadAllText(inputFile));

                var output = new OutputBuffer();

                output.AppendLine("// <auto-generated />");
                output.AppendLine("#nullable enable");

                GenerateSyntaxNodes(output, syntaxTree.GetRoot().ChildNodes());

                //output.AppendLine("namespace WasmWrangler");
                //output.AppendLine("{");
                //output.IncreaseIndent();

                //switch (binding.Type)
                //{
                //    case "GlobalObject":
                //        GenerateGlobalObject(output, binding);
                //        break;

                //    case "JSObjectWrapper":
                //        GenerateJSObjectWrapper(output, binding);
                //        break;
                //}

                //output.DecreaseIndent();
                //output.AppendLine("}");

                File.WriteAllText(outputFile, output.ToString());
            }

            return 0;
        }

        private static string CreateErrorMessage(SyntaxNode node, string message)
        {
            FileLinePositionSpan span = node.SyntaxTree.GetLineSpan(node.Span);
            int lineNumber = span.StartLinePosition.Line;
            int characterNumber = span.StartLinePosition.Character;

            return $"({lineNumber}, {characterNumber}): {message}";
        }

        private static void GenerateSyntaxNodes(OutputBuffer output, IEnumerable<SyntaxNode> nodes)
        {
            foreach (var node in nodes)
            {
                switch (node)
                {
                    case NamespaceDeclarationSyntax namespaceDeclarationSyntax:
                        output.AppendLine($"namespace {namespaceDeclarationSyntax.Name}");
                        output.AppendLine("{");
                        output.IncreaseIndent();

                        GenerateSyntaxNodes(output, namespaceDeclarationSyntax.Members);

                        output.DecreaseIndent();
                        output.AppendLine("}");
                        break;

                    case InterfaceDeclarationSyntax interfaceDeclarationSyntax:
                        GenerateInterface(output, interfaceDeclarationSyntax);
                        break;

                    case UsingDirectiveSyntax usingDirectiveSyntax:
                        output.AppendLine(usingDirectiveSyntax.ToString());
                        break;

                    default:
                        throw new InvalidOperationException(CreateErrorMessage(node, $"{node.Kind()} was not expected."));
                }
            }
        }

        private static void GenerateInterface(OutputBuffer output, InterfaceDeclarationSyntax @interface)
        {
            if (@interface.BaseList == null)
                throw new InvalidOperationException(CreateErrorMessage(@interface, $"Expected interface {@interface.Identifier} to have base interface."));

            var baseClasses = @interface.BaseList.ChildNodes();

            if (baseClasses.Count() > 1)
                throw new InvalidOperationException(CreateErrorMessage(@interface, $"Expected interface {@interface.Identifier} to have only 1 base interface."));

            var baseClass = ((SimpleBaseTypeSyntax)baseClasses.Single()).ToString();

            switch (baseClass)
            {
                case "JSGlobalObject":
                    GenerateJSGlobalObject(output, @interface);
                    break;

                case "JSObjectWrapper":
                    GenerateJSObjectWrapper(output, @interface);
                    break;

                default:
                    throw new InvalidOperationException(CreateErrorMessage(@interface, $"Unexpected base interface: {baseClass}"));
            }
        }

        private static void GenerateJSGlobalObject(OutputBuffer output, InterfaceDeclarationSyntax @interface)
        {
            output.AppendLine("public static partial class JS");
            output.AppendLine("{");
            output.IncreaseIndent();

            output.AppendLine($"public static partial class {@interface.Identifier}");
            output.AppendLine("{");
            output.AppendLine("\tprivate static JSObject? __js;");
            output.AppendLine();
            output.AppendLine("\tprivate static JSObject _js");
            output.AppendLine("\t{");
            output.AppendLine("\t\tget");
            output.AppendLine("\t\t{");
            output.AppendLine("\t\t\tif (__js == null)");
            output.AppendLine($"\t\t\t\t__js = (JSObject)Runtime.GetGlobalObject(nameof({@interface.Identifier}));");
            output.AppendLine();
            output.AppendLine("\t\t\treturn __js;");
            output.AppendLine("\t\t}");
            output.AppendLine("\t}");
            output.AppendLine();

            output.IncreaseIndent();

            foreach (var member in @interface.Members)
                GenerateInterfaceMember(output, member, true);

            output.DecreaseIndent();

            output.AppendLine("}");
            output.DecreaseIndent();

            output.AppendLine("}");
            output.AppendLine();
        }

        private static void GenerateJSObjectWrapper(OutputBuffer output, InterfaceDeclarationSyntax @interface)
        {
            output.AppendLine($"public partial class {@interface.Identifier}");
            output.AppendLine("{");
            output.AppendLine("\tprivate readonly JSObject _js;");
            output.AppendLine();
            output.AppendLine($"\tpublic {@interface.Identifier}(JSObject js)");
            output.AppendLine("\t{");
            output.AppendLine("\t\t_js = js;");
            output.AppendLine("\t}");
            output.AppendLine();
            output.AppendLine($"\tpublic static {@interface.Identifier}? Wrap(JSObject? js) => js != null ? new {@interface.Identifier}(js) : null;");
            output.AppendLine();
            output.AppendLine($"\tpublic static implicit operator JSObject({@interface.Identifier} obj) => obj._js;");
            output.AppendLine();

            output.IncreaseIndent();

            foreach (var member in @interface.Members)
                GenerateInterfaceMember(output, member, false);

            output.DecreaseIndent();
            
            output.AppendLine("}");
            output.AppendLine();
        }

        private static void GenerateInterfaceMember(OutputBuffer output, MemberDeclarationSyntax member, bool asStatic)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method:
                    GenerateMethod(output, method, asStatic);
                    break;

                case PropertyDeclarationSyntax property:
                    GenerateProperty(output, property, asStatic);
                    break;

                default:
                    throw new InvalidOperationException(CreateErrorMessage(member, $"Unexpected member: {member}"));
            }
        }

        private static void GenerateMethod(OutputBuffer output, MethodDeclarationSyntax method, bool asStatic)
        {
            output.Append($"public ");

            if (asStatic)
                output.Append("static ");

            output.Append($"{method.ReturnType} {method.Identifier}");
            output.AppendLine(method.ParameterList.ToString());

            output.AppendLine("{");

            output.Append("\t");

            if (method.ReturnType.ToString() != "void")
            {
                output.Append($"return ({method.ReturnType})(JSObject?)");

                //if (method.WrapReturn)
                //{
                //    output.Append($"return {method.ReturnType.TrimEnd('?')}.Wrap((JSObject?)");
                //}
                //else
                //{
                //    output.Append($"return ({method.ReturnType})");
                //}
            }

            output.Append($"_js.Invoke(nameof({method.Identifier})");

            foreach (var parameter in method.ParameterList.Parameters)
                output.Append($", {parameter.Identifier}");

            output.Append(")");

            //if (method.ReturnType.ToString() != "void" && method.WrapReturn)
            //    output.Append(")");

            output.AppendLine(";");
            output.AppendLine("}");

            output.AppendLine();
        }

        private static void GenerateProperty(OutputBuffer output, PropertyDeclarationSyntax property, bool asStatic)
        {
            output.AppendLine($"public {property.Type} {property.Identifier}");
            output.AppendLine("{");

            //if (property.CanGet)
            //    output.AppendLine($"\tget => _js.GetObjectProperty<{property.Type}>(nameof({property.Name}));");

            //if (property.CanSet)
            //    output.AppendLine($"\tset => _js.SetObjectProperty(nameof({property.Name}), value);");

            output.AppendLine("}");
        }
    }
}
