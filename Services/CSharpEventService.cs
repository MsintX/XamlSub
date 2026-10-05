using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlSub.Models;

namespace XamlSub.Services;

public sealed class CSharpEventService
{
    public string EnsureCodeBehindClass(string source, string qualifiedClassName, string rootTypeName)
    {
        _ = rootTypeName;
        if (string.IsNullOrWhiteSpace(qualifiedClassName))
            return source;

        var newline = DetectNewline(source);
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var classNode = FindTargetClass(root, qualifiedClassName);
        if (classNode is null)
            return NormalizeNewlines(source, newline);

        if (classNode.Modifiers.Any(t => t.RawKind == (int)SyntaxKind.PartialKeyword))
            return NormalizeNewlines(source, newline);

        var result = source.Insert(classNode.Keyword.SpanStart, "partial ");
        return NormalizeNewlines(result, newline);
    }

    public string EnsureEventHandler(string source, ControlNode node, string eventName, string? targetClassName = null, XamlFrameworkKind framework = XamlFrameworkKind.WinUI3)
    {
        if (string.IsNullOrWhiteSpace(eventName) || !SyntaxFacts.IsValidIdentifier(eventName))
            return source;

        var eventKind = node.EventKind ?? node.DerivedEventKind;
        if (eventKind is not ("Click" or "Tapped"))
            return source;

        var frameworkEventName = ProjectFrameworkService.GetEventName(framework, eventKind);
        var argsType = ProjectFrameworkService.GetEventArgsType(framework, eventKind);
        if (string.IsNullOrWhiteSpace(frameworkEventName) || string.IsNullOrWhiteSpace(argsType))
            return source;

        var newline = DetectNewline(source);
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var classNode = FindTargetClass(root, targetClassName);
        if (classNode is null)
            return NormalizeNewlines(source, newline);

        var methodExists = classNode.Members
            .OfType<MethodDeclarationSyntax>()
            .Any(m => string.Equals(m.Identifier.ValueText, eventName, StringComparison.Ordinal));
        if (methodExists)
            return NormalizeNewlines(source, newline);

        var methodText = string.Join(newline,
            $"private void {eventName}(object sender, {argsType} e)",
            "{",
            "    // TODO",
            "}");

        return NormalizeNewlines(
            InsertIntoClass(source, classNode, methodText, newline),
            newline);
    }

    public string EnsureLoadedHandler(
        string source,
        string rootElementName = "ContentRoot",
        string methodName = "Window_Loaded",
        string? targetClassName = null,
        XamlFrameworkKind framework = XamlFrameworkKind.WinUI3)
    {
        if (!SyntaxFacts.IsValidIdentifier(rootElementName) || !SyntaxFacts.IsValidIdentifier(methodName))
            return source;

        var newline = DetectNewline(source);
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var classNode = FindTargetClass(root, targetClassName);
        if (classNode is null)
            return NormalizeNewlines(source, newline);

        var hasMethod = classNode.Members
            .OfType<MethodDeclarationSyntax>()
            .Any(m => string.Equals(m.Identifier.ValueText, methodName, StringComparison.Ordinal));

        if (!hasMethod)
        {
            var loadedArgsType = ProjectFrameworkService.GetLoadedEventArgsType(framework);
            var methodText = string.Join(newline,
                $"private void {methodName}(object sender, {loadedArgsType} e)",
                "{",
                "    // TODO",
                "}");
            source = InsertIntoClass(source, classNode, methodText, newline);
            root = CSharpSyntaxTree.ParseText(source).GetRoot();
            classNode = FindTargetClass(root, targetClassName);
            if (classNode is null)
                return NormalizeNewlines(source, newline);
        }

        // Never create a constructor and never invent InitializeComponent().
        var ctor = classNode.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (ctor?.Body is null)
            return NormalizeNewlines(source, newline);

        var statement = $"{rootElementName}.Loaded += {methodName};";
        if (ctor.Body.Statements.Any(s => s.ToString().Contains(statement, StringComparison.Ordinal)))
            return NormalizeNewlines(source, newline);

        var initializeStatement = ctor.Body.Statements.FirstOrDefault(s =>
            s.ToString().Contains("InitializeComponent", StringComparison.Ordinal));
        var bodyIndent = GetIndentAt(source, ctor.Body.CloseBraceToken.SpanStart) + "    ";

        if (initializeStatement is not null)
        {
            var lineEnd = FindLineEnd(source, initializeStatement.Span.End);
            source = source.Insert(lineEnd, newline + bodyIndent + statement);
        }
        else
        {
            source = source.Insert(
                ctor.Body.CloseBraceToken.SpanStart,
                newline + bodyIndent + statement + newline + GetIndentAt(source, ctor.Body.CloseBraceToken.SpanStart));
        }

        return NormalizeNewlines(source, newline);
    }

    private static ClassDeclarationSyntax? FindTargetClass(SyntaxNode root, string? targetClassName)
    {
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();
        if (classes.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(targetClassName))
        {
            var target = NormalizeTypeName(targetClassName);
            var exact = classes.FirstOrDefault(c =>
                string.Equals(GetQualifiedClassName(c), target, StringComparison.Ordinal));
            if (exact is not null)
                return exact;

            var simple = GetSimpleName(target);
            var simpleMatches = classes.Where(c =>
                string.Equals(c.Identifier.ValueText, simple, StringComparison.Ordinal)).ToList();
            if (simpleMatches.Count == 1)
                return simpleMatches[0];

            return classes.FirstOrDefault(c => c.Modifiers.Any(t =>
                t.RawKind == (int)SyntaxKind.PartialKeyword));
        }

        return classes.Count == 1
            ? classes[0]
            : classes.FirstOrDefault(c => c.Modifiers.Any(t =>
                t.RawKind == (int)SyntaxKind.PartialKeyword));
    }

    private static string InsertIntoClass(string source, ClassDeclarationSyntax classNode, string memberText, string newline)
    {
        var classIndent = GetIndentAt(source, classNode.Keyword.SpanStart);
        var memberIndent = classIndent + "    ";
        var indented = Indent(memberText, memberIndent, newline);
        return source.Insert(
            classNode.CloseBraceToken.SpanStart,
            newline + newline + indented + newline + classIndent);
    }

    private static string GetIndentAt(string source, int position)
    {
        if (position <= 0) return string.Empty;
        var safePosition = Math.Min(position, source.Length);
        var lineStart = source.LastIndexOf('\n', Math.Max(0, safePosition - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var count = 0;
        while (lineStart + count < source.Length && source[lineStart + count] is ' ' or '\t')
            count++;
        return source.Substring(lineStart, count);
    }

    private static int FindLineEnd(string source, int position)
    {
        var index = source.IndexOf('\n', Math.Max(0, position));
        return index >= 0 ? index : source.Length;
    }

    private static string Indent(string text, string indent, string newline)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return string.Join(newline, normalized.Split('\n').Select(line => indent + line));
    }

    private static string GetSimpleName(string qualifiedName)
    {
        var normalized = NormalizeTypeName(qualifiedName);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? string.Empty : parts[^1];
    }

    private static string GetQualifiedClassName(ClassDeclarationSyntax classNode)
    {
        var namespaceParts = classNode.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(n => n.Name.ToString())
            .Reverse()
            .ToArray();
        var namespaceName = namespaceParts.Length == 0 ? null : string.Join('.', namespaceParts);
        return string.IsNullOrWhiteSpace(namespaceName)
            ? classNode.Identifier.ValueText
            : namespaceName + "." + classNode.Identifier.ValueText;
    }

    private static string NormalizeTypeName(string value)
        => value.Trim().Replace("global::", "", StringComparison.Ordinal);

    private static string DetectNewline(string source)
        => source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string NormalizeNewlines(string text, string newline)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return newline == "\n" ? normalized : normalized.Replace("\n", newline);
    }
}
