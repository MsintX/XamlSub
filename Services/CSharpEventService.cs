using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlSub.Models;

namespace XamlSub.Services;

public sealed class CSharpEventService
{
    /// <summary>
    /// Ensures that the target code-behind file contains a partial class that XAML can bind to.
    /// This implementation intentionally avoids newer Roslyn tree-mutation helpers so it works
    /// with older Microsoft.CodeAnalysis versions as well.
    /// </summary>
    public string EnsureCodeBehindClass(string source, string qualifiedClassName, string rootTypeName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedClassName))
            throw new InvalidOperationException("无法确定 code-behind class 名称。");

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var targetName = GetSimpleName(qualifiedClassName);
        var classNode = FindTargetClass(root, qualifiedClassName);

        if (classNode is not null)
        {
            if (!classNode.Modifiers.Any(t => t.RawKind == (int)SyntaxKind.PartialKeyword))
            {
                // Insert the keyword immediately before the class declaration.
                source = source.Insert(classNode.Keyword.SpanStart, "partial ");
            }
            return source;
        }

        var baseType = rootTypeName switch
        {
            "Window" => "Microsoft.UI.Xaml.Window",
            "Page" => "Microsoft.UI.Xaml.Controls.Page",
            "UserControl" => "Microsoft.UI.Xaml.Controls.UserControl",
            _ => "Microsoft.UI.Xaml.Controls.Grid"
        };

        var classText = $"public sealed partial class {targetName} : {baseType}\n{{\n    public {targetName}()\n    {{\n        InitializeComponent();\n    }}\n}}";
        var namespaceName = GetNamespaceName(qualifiedClassName);

        if (string.IsNullOrWhiteSpace(namespaceName))
            return AppendWithNewline(source, classText);

        // A file-scoped namespace must keep all members after the semicolon, while a block
        // namespace can safely receive a new class before its closing brace. Both cases are
        // handled via source insertion rather than AddMembers().
        var blockNamespace = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault(n => string.Equals(n.Name.ToString(), namespaceName, StringComparison.Ordinal));
        if (blockNamespace is not null)
        {
            return source.Insert(blockNamespace.CloseBraceToken.SpanStart,
                $"\n    {Indent(classText, "    ")}\n");
        }

        var fileNamespace = root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault(n => string.Equals(n.Name.ToString(), namespaceName, StringComparison.Ordinal));
        if (fileNamespace is not null)
            return source.Insert(fileNamespace.Span.End, $"\n\n{classText}");

        return AppendWithNewline(source, $"namespace {namespaceName}\n{{\n    {Indent(classText, "    ")}\n}}");
    }

    public string EnsureEventHandler(string source, ControlNode node, string eventName, string? targetClassName = null)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return source;

        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var methodExists = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Any(m => m.Identifier.ValueText == eventName);
        if (methodExists)
            return source;

        var classNode = FindTargetClass(root, targetClassName);
        if (classNode is null)
            throw new InvalidOperationException("没有找到 code-behind class。");

        var eventKind = node.EventKind ?? node.DerivedEventKind;
        var argsType = string.Equals(eventKind, "Click", StringComparison.Ordinal)
            ? "Microsoft.UI.Xaml.RoutedEventArgs"
            : "Microsoft.UI.Xaml.Input.TappedRoutedEventArgs";
        var methodText = $"private void {eventName}(object sender, {argsType} e)\n{{\n    // TODO\n}}";
        return InsertIntoClass(source, classNode, methodText);
    }

    public string EnsureLoadedHandler(
        string source,
        string rootElementName = "ContentRoot",
        string methodName = "Window_Loaded",
        string? targetClassName = null)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var classNode = FindTargetClass(root, targetClassName);
        if (classNode is null)
            throw new InvalidOperationException("没有找到 code-behind class。");

        var existing = classNode.Members.OfType<MethodDeclarationSyntax>()
            .Any(m => m.Identifier.ValueText == methodName);
        if (!existing)
        {
            var methodText = $"private void {methodName}(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)\n{{\n    // TODO\n}}";
            source = InsertIntoClass(source, classNode, methodText);
        }

        // Reparse after the possible insertion so spans are current.
        root = CSharpSyntaxTree.ParseText(source).GetRoot();
        classNode = FindTargetClass(root, targetClassName);
        if (classNode is null)
            throw new InvalidOperationException("没有找到 code-behind class。");

        var ctor = classNode.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (ctor is null)
        {
            var ctorText = $"public {classNode.Identifier.ValueText}()\n{{\n    InitializeComponent();\n}}";
            source = InsertIntoClass(source, classNode, ctorText);
        }

        root = CSharpSyntaxTree.ParseText(source).GetRoot();
        classNode = FindTargetClass(root, targetClassName);
        if (classNode is null)
            throw new InvalidOperationException("没有找到 code-behind class。");

        ctor = classNode.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (ctor?.Body is null)
            throw new InvalidOperationException("无法创建页面构造函数。");

        var statement = $"{rootElementName}.Loaded += {methodName};";
        var hasSubscription = ctor.Body.Statements.Any(s =>
            s.ToString().Contains(statement, StringComparison.Ordinal));
        if (!hasSubscription)
        {
            var initializeStatement = ctor.Body.Statements.FirstOrDefault(s =>
                s.ToString().Contains("InitializeComponent", StringComparison.Ordinal));
            if (initializeStatement is not null)
            {
                var insertAt = initializeStatement.Span.End;
                var lineEnd = FindLineEnd(source, insertAt);
                source = source.Insert(lineEnd, $"\n        {statement}");
            }
            else
            {
                // Insert before the constructor's closing brace.
                source = source.Insert(ctor.Body.CloseBraceToken.SpanStart, $"\n        {statement}\n    ");
            }
        }

        return source;
    }

    private static ClassDeclarationSyntax? FindTargetClass(SyntaxNode root, string? targetClassName)
    {
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();
        if (classes.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(targetClassName))
        {
            var simple = GetSimpleName(targetClassName);
            var exact = classes.FirstOrDefault(c =>
                string.Equals(c.Identifier.ValueText, simple, StringComparison.Ordinal));
            if (exact is not null) return exact;
        }

        return classes.FirstOrDefault(c =>
            c.Modifiers.Any(t => t.RawKind == (int)SyntaxKind.PartialKeyword)) ?? classes[0];
    }

    private static string InsertIntoClass(string source, ClassDeclarationSyntax classNode, string memberText)
    {
        var indent = GetIndentAt(source, classNode.Keyword.SpanStart) + "    ";
        return source.Insert(classNode.CloseBraceToken.SpanStart,
            $"\n\n{indent}{Indent(memberText, indent.Length)}\n");
    }

    private static string GetIndentAt(string source, int position)
    {
        if (position <= 0) return string.Empty;
        var lineStart = source.LastIndexOf('\n', Math.Min(position, source.Length) - 1);
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var count = 0;
        while (lineStart + count < source.Length && source[lineStart + count] is ' ' or '\t') count++;
        return source.Substring(lineStart, count);
    }

    private static int FindLineEnd(string source, int position)
    {
        var index = source.IndexOf('\n', Math.Max(0, position));
        return index >= 0 ? index : source.Length;
    }

    private static string AppendWithNewline(string source, string addition)
        => string.IsNullOrWhiteSpace(source)
            ? addition + Environment.NewLine
            : source.TrimEnd() + Environment.NewLine + Environment.NewLine + addition + Environment.NewLine;

    private static string Indent(string text, string indent)
        => string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(line => indent + line));

    private static string Indent(string text, int spaces)
        => Indent(text, new string(' ', spaces));

    private static string GetSimpleName(string qualifiedName)
        => qualifiedName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();

    private static string? GetNamespaceName(string qualifiedName)
    {
        var parts = qualifiedName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length <= 1 ? null : string.Join('.', parts[..^1]);
    }
}
