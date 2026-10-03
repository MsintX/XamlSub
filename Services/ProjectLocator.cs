using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace XamlSub.Services;

public static class ProjectLocator
{
    /// <summary>
    /// Returns the same-name code-behind candidate (for example 1.xaml -> 1.xaml.cs)
    /// without attempting to determine whether the XAML is actually associated with it.
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
    /// Resolves a code-behind file only when the XAML provides an explicit x:Class
    /// association and a C# file in the same directory actually declares that class.
    /// </summary>
    public static string? FindAssociatedCodeBehind(string xamlPath, string? xClass)
    {
        if (string.IsNullOrWhiteSpace(xClass)) return null;
        var directory = Path.GetDirectoryName(xamlPath);
        if (directory is null || !Directory.Exists(directory)) return null;

        var className = xClass.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(className)) return null;

        var sameName = FindSameNameCodeBehind(xamlPath);
        if (sameName is not null && ContainsClassDeclaration(sameName, className))
            return sameName;

        foreach (var csPath in Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
                     .Where(p => !Path.GetFileName(p).EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (sameName is not null && string.Equals(csPath, sameName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (ContainsClassDeclaration(csPath, className))
                return csPath;
        }

        return null;
    }


    public sealed record CodeBehindClassInfo(string QualifiedName, string ClassName, string? NamespaceName);

    /// <summary>
    /// Returns the primary code-behind class and its namespace. Partial classes are preferred.
    /// </summary>
    public static CodeBehindClassInfo? FindCodeBehindClassInfo(string csPath)
    {
        try
        {
            var source = File.ReadAllText(csPath);
            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            var classNode = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Modifiers.Any(t => t.RawKind == (int)SyntaxKind.PartialKeyword))
                ?? root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classNode is null) return null;

            var className = classNode.Identifier.ValueText;
            var namespaceParts = classNode.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
                .Select(n => n.Name.ToString())
                .Reverse()
                .ToArray();
            var namespaceName = namespaceParts.Length == 0 ? null : string.Join('.', namespaceParts);
            var qualifiedName = string.IsNullOrWhiteSpace(namespaceName)
                ? className
                : namespaceName + "." + className;
            return new CodeBehindClassInfo(qualifiedName, className, namespaceName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Finds the first declared namespace even when the file has no class yet.</summary>
    public static string? FindDeclaredNamespace(string csPath)
    {
        try
        {
            var source = File.ReadAllText(csPath);
            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            var namespaceParts = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>()
                .Select(n => n.Name.ToString())
                .FirstOrDefault();
            return namespaceParts;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a valid C# class identity for a same-name code-behind file. Invalid filename starts,
    /// such as "1.xaml", are prefixed so the result is still a valid C# identifier.
    /// </summary>
    public static string SuggestCodeBehindClassIdentity(string xamlPath, string? namespaceName = null)
    {
        var stem = Path.GetFileNameWithoutExtension(xamlPath);
        var builder = new System.Text.StringBuilder();
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

    public static string GetBroadSaveClassIdentity(string xamlPath, string csPath)
    {
        return FindCodeBehindClassInfo(csPath)?.QualifiedName
            ?? SuggestCodeBehindClassIdentity(xamlPath, FindDeclaredNamespace(csPath));
    }

    /// <summary>
    /// Backwards-compatible candidate lookup. New save logic should use the explicit
    /// FindSameNameCodeBehind/FindAssociatedCodeBehind methods instead.
    /// </summary>
    public static string? FindCodeBehind(string xamlPath) => FindSameNameCodeBehind(xamlPath);

    private static bool ContainsClassDeclaration(string csPath, string className)
    {
        try
        {
            var source = File.ReadAllText(csPath);
            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            return root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Any(c => string.Equals(c.Identifier.ValueText, className, StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
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
