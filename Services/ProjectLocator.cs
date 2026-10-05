using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Xml.Linq;

namespace XamlSub.Services;

public static class ProjectLocator
{
    public sealed record CodeBehindClassInfo(string QualifiedName, string ClassName, string? NamespaceName);

    /// <summary>
    /// Returns the conventional same-name code-behind file.
    /// MainWindow.xaml -> MainWindow.xaml.cs
    /// </summary>
    public static string? FindSameNameCodeBehind(string xamlPath)
    {
        var directory = Path.GetDirectoryName(xamlPath);
        if (directory is null) return null;

        var baseName = Path.GetFileNameWithoutExtension(xamlPath);
        var direct = Path.Combine(directory, baseName + ".xaml.cs");
        return File.Exists(direct) ? direct : null;
    }

    /// <summary>
    /// Strict lookup used only when broad-save mode is disabled.
    /// </summary>
    public static string? FindAssociatedCodeBehind(string xamlPath, string? xClass)
    {
        if (string.IsNullOrWhiteSpace(xClass)) return null;
        var directory = Path.GetDirectoryName(xamlPath);
        if (directory is null || !Directory.Exists(directory)) return null;

        var target = NormalizeTypeName(xClass);
        var sameName = FindSameNameCodeBehind(xamlPath);
        if (sameName is not null && ContainsClassDeclaration(sameName, target))
            return sameName;

        foreach (var csPath in Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
                     .Where(p => !Path.GetFileName(p).EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (sameName is not null && string.Equals(csPath, sameName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (ContainsClassDeclaration(csPath, target))
                return csPath;
        }

        return null;
    }

    /// <summary>
    /// Broad save is deliberately simple: when the user has enabled it, the same-name
    /// code-behind file is authoritative. No class/namespace association check is performed
    /// before saving. This is the behavior required by the editor's broad-save option.
    /// </summary>
    public static string? FindBroadSaveCodeBehind(string xamlPath)
        => FindSameNameCodeBehind(xamlPath);

    public static CodeBehindClassInfo? FindCodeBehindClassInfo(string csPath, string? preferredClassName = null)
    {
        try
        {
            var source = File.ReadAllText(csPath);
            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();

            if (!string.IsNullOrWhiteSpace(preferredClassName))
            {
                var preferred = classes.FirstOrDefault(c =>
                    string.Equals(c.Identifier.ValueText, preferredClassName, StringComparison.Ordinal));
                if (preferred is not null)
                    return CreateInfo(preferred);
            }

            var partial = classes.FirstOrDefault(c =>
                c.Modifiers.Any(t => t.RawKind == (int)SyntaxKind.PartialKeyword));
            if (partial is not null)
                return CreateInfo(partial);

            return classes.Count == 1 ? CreateInfo(classes[0]) : null;
        }
        catch
        {
            return FindCodeBehindClassInfoFromText(csPath, preferredClassName);
        }
    }

    public static string? FindDeclaredNamespace(string csPath)
    {
        try
        {
            var source = File.ReadAllText(csPath);
            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            var namespaceNode = root.DescendantNodes()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .FirstOrDefault();
            return namespaceNode?.Name.ToString() ?? FindNamespaceFromText(source);
        }
        catch
        {
            try { return FindNamespaceFromText(File.ReadAllText(csPath)); }
            catch { return null; }
        }
    }

    public static string SuggestCodeBehindClassIdentity(string xamlPath, string? namespaceName = null)
    {
        var stem = Path.GetFileNameWithoutExtension(xamlPath);
        var builder = new StringBuilder();
        foreach (var ch in stem)
            builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');

        var className = builder.ToString();
        if (string.IsNullOrWhiteSpace(className)) className = "XamlPage";
        if (char.IsDigit(className[0])) className = "Xaml" + className;
        if (string.Equals(className, "class", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(className, "namespace", StringComparison.OrdinalIgnoreCase))
            className = "Xaml" + className;

        return string.IsNullOrWhiteSpace(namespaceName)
            ? className
            : namespaceName + "." + className;
    }

    /// <summary>
    /// Repairs the XAML class identity from the conventional same-name code-behind.
    /// It prefers a class whose name equals the XAML filename, then a single partial class,
    /// then a single class. If source parsing cannot find one, it falls back to the declared
    /// namespace + XAML filename.
    /// </summary>
    public static string GetBroadSaveClassIdentity(string xamlPath, string csPath, string? existingXClass = null)
    {
        var expectedName = Path.GetFileNameWithoutExtension(xamlPath);

        // In broad-save mode the same-name .xaml.cs is authoritative. Resolve the
        // actual class from that file every time so a stale x:Class="MainWindow"
        // cannot survive when the real class is, for example, MyApp.MainWindow.
        var info = FindCodeBehindClassInfo(csPath, expectedName);
        if (info is not null)
        {
            // Roslyn is authoritative for the class name, but independently inspect
            // the source text for the namespace. This covers cases where a parser
            // version accepts the class declaration but does not expose the namespace
            // shape we expect (for example newer/file-scoped syntax).
            var textNamespace = TryReadNamespaceText(csPath);
            if (!string.IsNullOrWhiteSpace(textNamespace))
                return textNamespace + "." + info.ClassName;

            return string.IsNullOrWhiteSpace(info.NamespaceName)
                ? info.ClassName
                : info.QualifiedName;
        }

        // Roslyn can fail to parse a user's source when it uses syntax newer than
        // the version bundled with XamlSub. Use the text fallback before considering
        // any previous x:Class value.
        var textInfo = FindCodeBehindClassInfoFromText(csPath, expectedName);
        if (textInfo is not null)
        {
            return string.IsNullOrWhiteSpace(textInfo.NamespaceName)
                ? textInfo.ClassName
                : textInfo.QualifiedName;
        }

        // If no class can be parsed at all, preserve an already-qualified x:Class
        // rather than degrading it to the bare filename. Broad-save still proceeds.
        var existing = NormalizeTypeName(existingXClass ?? string.Empty);
        var existingSimple = GetSimpleName(existing);
        if (!string.IsNullOrWhiteSpace(existing) &&
            string.Equals(existingSimple, expectedName, StringComparison.Ordinal))
        {
            return existing;
        }

        var projectNamespace = FindProjectRootNamespace(xamlPath);
        if (!string.IsNullOrWhiteSpace(projectNamespace))
            return projectNamespace + "." + expectedName;

        var declaredNamespace = FindDeclaredNamespace(csPath);
        return SuggestCodeBehindClassIdentity(xamlPath, declaredNamespace);
    }


    private static string? FindProjectRootNamespace(string xamlPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(xamlPath);
            while (!string.IsNullOrWhiteSpace(directory))
            {
                var projects = Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (var project in projects)
                {
                    var doc = XDocument.Load(project);
                    var rootNamespace = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "RootNamespace")?.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(rootNamespace))
                        return rootNamespace;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }
        }
        catch
        {
            // Fall through to the other inference strategies.
        }

        return null;
    }

    public static string? FindCodeBehind(string xamlPath) => FindSameNameCodeBehind(xamlPath);

    private static bool ContainsClassDeclaration(string csPath, string targetClassName)
    {
        try
        {
            var source = File.ReadAllText(csPath);
            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            var target = NormalizeTypeName(targetClassName);
            var simple = GetSimpleName(target);

            return root.DescendantNodes().OfType<ClassDeclarationSyntax>().Any(c =>
                string.Equals(GetQualifiedClassName(c), target, StringComparison.Ordinal) ||
                string.Equals(c.Identifier.ValueText, simple, StringComparison.Ordinal));
        }
        catch
        {
            try
            {
                var source = File.ReadAllText(csPath);
                var simple = GetSimpleName(targetClassName);
                return Regex.IsMatch(
                    source,
                    $@"\bclass\s+{Regex.Escape(simple)}\b",
                    RegexOptions.CultureInvariant);
            }
            catch
            {
                return false;
            }
        }
    }

    private static CodeBehindClassInfo? FindCodeBehindClassInfoFromText(string csPath, string? preferredClassName)
    {
        try
        {
            var source = File.ReadAllText(csPath);
            var namespaceName = FindNamespaceFromText(source);
            var preferred = string.IsNullOrWhiteSpace(preferredClassName)
                ? Path.GetFileNameWithoutExtension(csPath).Replace(".xaml", "", StringComparison.OrdinalIgnoreCase)
                : preferredClassName;

            if (!string.IsNullOrWhiteSpace(preferred) &&
                Regex.IsMatch(source, $@"\bclass\s+{Regex.Escape(preferred)}\b", RegexOptions.CultureInvariant))
            {
                return CreateInfo(preferred, namespaceName);
            }

            var match = Regex.Match(
                source,
                @"\b(?:public|internal|protected|private)?\s*(?:sealed\s+|abstract\s+)?(?:partial\s+)?class\s+([A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.CultureInvariant);
            if (!match.Success)
                return null;

            return CreateInfo(match.Groups[1].Value, namespaceName);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadNamespaceText(string csPath)
    {
        try
        {
            return FindNamespaceFromText(File.ReadAllText(csPath));
        }
        catch
        {
            return null;
        }
    }

    private static string? FindNamespaceFromText(string source)
    {
        var fileScoped = Regex.Match(
            source,
            @"(?m)^\s*namespace\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*;",
            RegexOptions.CultureInvariant);
        if (fileScoped.Success)
            return fileScoped.Groups[1].Value;

        var block = Regex.Match(
            source,
            @"(?m)^\s*namespace\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*\{",
            RegexOptions.CultureInvariant);
        return block.Success ? block.Groups[1].Value : null;
    }

    private static CodeBehindClassInfo CreateInfo(ClassDeclarationSyntax classNode)
    {
        var namespaceParts = classNode.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(n => n.Name.ToString())
            .Reverse()
            .ToArray();
        var namespaceName = namespaceParts.Length == 0 ? null : string.Join('.', namespaceParts);
        return CreateInfo(classNode.Identifier.ValueText, namespaceName);
    }

    private static CodeBehindClassInfo CreateInfo(string className, string? namespaceName)
    {
        var qualifiedName = string.IsNullOrWhiteSpace(namespaceName)
            ? className
            : namespaceName + "." + className;
        return new CodeBehindClassInfo(qualifiedName, className, namespaceName);
    }

    private static string NormalizeTypeName(string value)
        => value.Trim().Replace("global::", "", StringComparison.Ordinal);

    private static string GetSimpleName(string value)
    {
        var normalized = NormalizeTypeName(value);
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

    public static IReadOnlyList<string> FindXamlFiles(string projectFile)
    {
        var directory = Path.GetDirectoryName(projectFile);
        if (directory is null || !Directory.Exists(directory)) return Array.Empty<string>();
        return Directory.EnumerateFiles(directory, "*.xaml", SearchOption.AllDirectories)
            .Where(p => !Path.GetFileName(p).Equals("App.xaml", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
