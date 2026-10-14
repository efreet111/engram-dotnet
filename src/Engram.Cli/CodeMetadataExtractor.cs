using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Engram.Cli;

/// <summary>
/// HU-060: Code-aware metadata extraction for engram watch.
/// Extracts symbol (class/interface/etc.) and namespace from code files.
/// Uses regex-based extraction — no external parser dependencies.
/// </summary>
public static partial class CodeMetadataExtractor
{
    private const int MaxSymbolLength = 256;
    private const int MaxNamespaceLength = 512;

    /// <summary>
    /// Result of code metadata extraction.
    /// </summary>
    /// <param name="FilePath">Relative file path (always set).</param>
    /// <param name="Symbol">First top-level symbol name (class/interface/struct/etc.) or null.</param>
    /// <param name="Namespace">Inferred namespace from path or null.</param>
    public record CodeMetadata(string FilePath, string? Symbol, string? Namespace);

    /// <summary>
    /// Extrae el primer símbolo de nivel superior del contenido del archivo.
    /// Soporta: class, interface, struct, record, enum, delegate.
    /// Para delegates: busca el identifier que precede a "(" (ej: "ClickHandler" en "delegate void ClickHandler(...)")
    /// </summary>
    /// <param name="content">Contenido del archivo.</param>
    /// <param name="extension">Extensión sin punto (ej: "cs", "md").</param>
    /// <returns>Nombre del símbolo o null si no se encuentra.</returns>
    public static string? ExtractSymbol(string content, string extension)
    {
        if (!string.Equals(extension, "cs", StringComparison.OrdinalIgnoreCase))
        {
            Debug.WriteLine($"[CodeMetadataExtractor] Symbol extraction skipped: extension '{extension}' not supported (only C#).");
            return null;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            Debug.WriteLine("[CodeMetadataExtractor] No symbol found: content is empty or whitespace.");
            return null;
        }

        // Try compound keywords first: "record class", "partial class"
        var match = SymbolPatternCompound().Match(content);
        if (match.Success)
        {
            var symbol = match.Groups[2].Value; // group 2 = identifier (group 1 = keyword)
            if (IsValidIdentifier(symbol))
                return TruncateIfNeeded(symbol, MaxSymbolLength, "Symbol");
        }

        // Try standard keywords: class, interface, struct, record, enum
        match = SymbolPatternStandard().Match(content);
        if (match.Success)
        {
            var symbol = match.Groups[2].Value; // group 2 = identifier (group 1 = keyword)
            if (IsValidIdentifier(symbol))
                return TruncateIfNeeded(symbol, MaxSymbolLength, "Symbol");
        }

        // Try delegate: "delegate [return_type] [identifier]("
        // Extract the identifier that precedes "(" after "delegate"
        var delegateMatch = SymbolPatternDelegate().Match(content);
        if (delegateMatch.Success)
        {
            // The identifier is captured in group 1
            // This correctly captures "ClickHandler" from "delegate void ClickHandler("
            var symbol = delegateMatch.Groups[1].Value;
            if (IsValidIdentifier(symbol))
                return TruncateIfNeeded(symbol, MaxSymbolLength, "Symbol");
        }

        Debug.WriteLine("[CodeMetadataExtractor] No symbol found: no class/interface/struct/record/enum/delegate pattern in content.");
        return null;
    }

    /// <summary>
    /// Infiere el namespace desde la ruta relativa del archivo.
    /// Solo soporta C# (extension="cs").
    /// Espera el patrón: src/{Namespace}/{...}/filename.cs
    /// </summary>
    /// <param name="relPath">Ruta relativa del archivo.</param>
    /// <param name="extension">Extensión sin punto.</param>
    /// <returns>Namespace inferido (ej: "Engram.Auth") o null si no matchea.</returns>
    public static string? InferNamespace(string relPath, string extension)
    {
        if (!string.Equals(extension, "cs", StringComparison.OrdinalIgnoreCase))
        {
            Debug.WriteLine($"[CodeMetadataExtractor] Namespace inference skipped: extension '{extension}' not supported (only C#).");
            return null;
        }

        if (string.IsNullOrWhiteSpace(relPath))
        {
            Debug.WriteLine("[CodeMetadataExtractor] No namespace inferred: relPath is empty or whitespace.");
            return null;
        }

        var match = NamespacePathPattern().Match(relPath);
        if (!match.Success)
        {
            Debug.WriteLine($"[CodeMetadataExtractor] No namespace inferred: path '{relPath}' does not match src/<Namespace>/... pattern.");
            return null;
        }

        var ns = match.Groups[1].Value;

        if (string.IsNullOrEmpty(ns))
        {
            Debug.WriteLine($"[CodeMetadataExtractor] No namespace inferred: path '{relPath}' matched pattern but captured empty namespace.");
            return null;
        }

        // Convert directory separators to dots for namespace
        // "Engram/Store/Sync" → "Engram.Store.Sync"
        ns = ns.Replace('/', '.').Replace('\\', '.');

        if (ns.Length > MaxNamespaceLength)
        {
            Debug.WriteLine($"[CodeMetadataExtractor] Namespace '{ns}' exceeds {MaxNamespaceLength} chars, truncating.");
            ns = ns[..MaxNamespaceLength];
        }

        return ns;
    }

    /// <summary>
    /// Combina extracción de symbol + namespace en una sola llamada.
    /// </summary>
    public static CodeMetadata ExtractAllMetadata(string content, string relPath, string extension)
    {
        var symbol = ExtractSymbol(content, extension);
        var ns = InferNamespace(relPath, extension);

        return new CodeMetadata(relPath, symbol, ns);
    }

    private static string? TruncateIfNeeded(string value, int maxLength, string name)
    {
        if (value.Length <= maxLength)
            return value;
        Debug.WriteLine($"[CodeMetadataExtractor] {name} '{value}' exceeds {maxLength} chars, truncating.");
        return value[..maxLength];
    }

    /// <summary>
    /// Valida que el string es un identificador válido en C#.
    /// </summary>
    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        // First char: letter or underscore
        var first = name[0];
        if (first != '_' && !char.IsLetter(first))
            return false;

        // Rest: letter, digit, or underscore
        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (c != '_' && !char.IsLetterOrDigit(c))
                return false;
        }

        return true;
    }

    // FR-001: Symbol extraction — compound keywords (record class, partial class)
    [GeneratedRegex(
        @"\b(?:public|private|internal|protected|file)?\s*" +
        @"(record\s+class|partial\s+class)" +
        @"\s+(\w+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex SymbolPatternCompound();

    // FR-001: Symbol extraction — standard keywords (class, interface, struct, record, enum)
    // Identifier MUST start with uppercase (C# convention: type names are PascalCase)
    // This prevents matching "class here" from comments like "// no class here"
    [GeneratedRegex(
        @"\b(?:public|private|internal|protected|file)?\s*" +
        @"(class|interface|struct|record(?!\s+class)|enum)" +
        @"\s+([A-Z]\w*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex SymbolPatternStandard();

    // FR-001: Symbol extraction — delegate
    // Pattern: "delegate" + whitespace + (return_type) + whitespace + (identifier) + "("
    // For "delegate void ClickHandler(" → captures "ClickHandler"
    // For "delegate int Comparator(" → captures "Comparator"
    // Uses \S+ for return type (non-whitespace, handles simple types; complex types like "Func<>" work because identifier before "(" is what we capture)
    [GeneratedRegex(
        @"\bdelegate\s+\S+\s+(\w+)\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex SymbolPatternDelegate();

    // FR-002: Namespace inference regex
    // src/Engram/Auth/Foo.cs → captura "Engram.Auth" (excluye filename)
    // src/Engram/Store/Sync/Foo.cs → captura "Engram.Store.Sync"
    // Requiere: src/ prefix + directory segments PascalCase + /filename.cs suffix
    // Group 1 captura los segmentos de directorio (excluye filename)
    [GeneratedRegex(
        @"^src/([A-Z][A-Za-z0-9]*(?:[./\\][A-Z][A-Za-z0-9]*)*)(?:[/\\][^/\\]+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex NamespacePathPattern();
}
