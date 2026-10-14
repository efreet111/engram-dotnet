using Engram.Cli;
using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// HU-060: Tests for <see cref="CodeMetadataExtractor"/> — symbol and namespace extraction.
/// FR-001: ExtractSymbol
/// FR-002: InferNamespace
/// FR-003/FR-004: Watch integration and graceful fallback (covered by integration)
/// </summary>
public class CodeMetadataExtractorTests
{
    // ─── ExtractSymbol tests ────────────────────────────────────────────────

    [Theory]
    [InlineData("public class JwtBearerHandler { }", "JwtBearerHandler")]
    [InlineData("private class SqliteStore { }", "SqliteStore")]
    [InlineData("internal class SyncManager { }", "SyncManager")]
    [InlineData("public class MyClass { }", "MyClass")]
    [InlineData("public    class    SpacedClass { }", "SpacedClass")]
    public void ExtractSymbol_CsharpClass_ReturnsClassName(string content, string expected)
    {
        var result = CodeMetadataExtractor.ExtractSymbol(content, "cs");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("public interface IAuthService { }", "IAuthService")]
    [InlineData("internal interface IStore { }", "IStore")]
    [InlineData("public interface IDatabase { }", "IDatabase")]
    public void ExtractSymbol_CsharpInterface_ReturnsInterfaceName(string content, string expected)
    {
        var result = CodeMetadataExtractor.ExtractSymbol(content, "cs");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("public struct Point { }", "Point")]
    [InlineData("readonly struct Config { }", "Config")]
    public void ExtractSymbol_CsharpStruct_ReturnsStructName(string content, string expected)
    {
        var result = CodeMetadataExtractor.ExtractSymbol(content, "cs");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("public record UserDto(int Id, string Name);", "UserDto")]
    [InlineData("public record class UserDto(int Id, string Name);", "UserDto")]
    public void ExtractSymbol_CsharpRecord_ReturnsRecordName(string content, string expected)
    {
        var result = CodeMetadataExtractor.ExtractSymbol(content, "cs");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("public enum Color { Red, Green, Blue }", "Color")]
    [InlineData("internal enum Status { }", "Status")]
    public void ExtractSymbol_CsharpEnum_ReturnsEnumName(string content, string expected)
    {
        var result = CodeMetadataExtractor.ExtractSymbol(content, "cs");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("public delegate void ClickHandler(object sender, EventArgs e);", "ClickHandler")]
    [InlineData("internal delegate int Comparator(int a, int b);", "Comparator")]
    public void ExtractSymbol_CsharpDelegate_ReturnsDelegateName(string content, string expected)
    {
        var result = CodeMetadataExtractor.ExtractSymbol(content, "cs");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractSymbol_NoSymbolFound_ReturnsNull()
    {
        // File with only using statements and namespace, no class
        var content = @"
using System;
using Engram.Store;
namespace Example {
    var x = 1;
}";
        var result = CodeMetadataExtractor.ExtractSymbol(content, "cs");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractSymbol_MultipleSymbols_ReturnsFirst()
    {
        // Class appears first → should return ClassName
        var content = @"
public class ClassName {
    // ...
}

public interface IInterfaceName {
    // ...
}
";
        var result = CodeMetadataExtractor.ExtractSymbol(content, "cs");
        Assert.Equal("ClassName", result);
    }

    [Fact]
    public void ExtractSymbol_PartialClass_ReturnsClassName()
    {
        // partial class
        var content = "public partial class PartialHandler { }";
        var result = CodeMetadataExtractor.ExtractSymbol(content, "cs");
        Assert.Equal("PartialHandler", result);
    }

    [Fact]
    public void ExtractSymbol_FileAccess_ReturnsFileLocalClassName()
    {
        // file-local class (C# 9+)
        var content = "file class LocalClass { }";
        var result = CodeMetadataExtractor.ExtractSymbol(content, "cs");
        Assert.Equal("LocalClass", result);
    }

    [Theory]
    [InlineData("cs")]
    [InlineData("CS")]
    [InlineData("Cs")]
    public void ExtractSymbol_CsharpCaseInsensitive_ReturnsSymbol(string extension)
    {
        var content = "public class CaseInsensitiveTest { }";
        var result = CodeMetadataExtractor.ExtractSymbol(content, extension);
        Assert.Equal("CaseInsensitiveTest", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void ExtractSymbol_EmptyOrWhitespaceContent_ReturnsNull(string? content)
    {
        var result = CodeMetadataExtractor.ExtractSymbol(content!, "cs");
        Assert.Null(result);
    }

    [Theory]
    [InlineData("md")]
    [InlineData("json")]
    [InlineData("ts")]
    [InlineData("py")]
    public void ExtractSymbol_NonCsharp_ReturnsNull(string extension)
    {
        var content = "public class ShouldNotMatch { }";
        var result = CodeMetadataExtractor.ExtractSymbol(content, extension);
        Assert.Null(result);
    }

    [Fact]
    public void ExtractSymbol_256CharLimit_Truncates()
    {
        // Create a class name with > 256 chars
        var longName = new string('A', 300);
        var content = $"public class {longName} {{ }}";
        var result = CodeMetadataExtractor.ExtractSymbol(content, "cs");
        Assert.NotNull(result);
        Assert.Equal(256, result.Length);
    }

    // ─── InferNamespace tests ────────────────────────────────────────────────

    [Theory]
    [InlineData("src/Engram/Auth/JwtBearer.cs", "Engram.Auth")]
    [InlineData("src/Engram/Store/SqliteStore.cs", "Engram.Store")]
    [InlineData("src/Engram/Cli/Program.cs", "Engram.Cli")]
    public void InferNamespace_StandardProject_ReturnsNamespace(string relPath, string expected)
    {
        var result = CodeMetadataExtractor.InferNamespace(relPath, "cs");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("src/Engram/Store/Sync/SqliteSyncStore.cs", "Engram.Store.Sync")]
    [InlineData("src/Engram/Foundation/Core/Diagnostics/LogWriter.cs", "Engram.Foundation.Core.Diagnostics")]
    public void InferNamespace_NestedProject_ReturnsDottedNamespace(string relPath, string expected)
    {
        var result = CodeMetadataExtractor.InferNamespace(relPath, "cs");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("tests/UnitTests/AuthTests.cs")]
    [InlineData("docs/README.md")]
    [InlineData("src/README.cs")] // no namespace segment before filename
    [InlineData("script.cs")]      // no src/ prefix
    public void InferNamespace_NonStandardPath_ReturnsNull(string relPath)
    {
        var result = CodeMetadataExtractor.InferNamespace(relPath, "cs");
        Assert.Null(result);
    }

    [Theory]
    [InlineData("md")]
    [InlineData("json")]
    [InlineData("ts")]
    [InlineData("py")]
    public void InferNamespace_NonCsharp_ReturnsNull(string extension)
    {
        var result = CodeMetadataExtractor.InferNamespace("src/Engram/Store/Data.cs", extension);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void InferNamespace_EmptyOrWhitespacePath_ReturnsNull(string? relPath)
    {
        var result = CodeMetadataExtractor.InferNamespace(relPath!, "cs");
        Assert.Null(result);
    }

    [Fact]
    public void InferNamespace_512CharLimit_Truncates()
    {
        // Create a deeply nested path that would produce a very long namespace
        var segments = Enumerable.Range(0, 50).Select(i => $"Segment{i}").ToArray();
        var relPath = "src/" + string.Join("/", segments) + "/File.cs";
        var result = CodeMetadataExtractor.InferNamespace(relPath, "cs");
        Assert.NotNull(result);
        Assert.True(result.Length <= 512);
    }

    // ─── ExtractAllMetadata tests ───────────────────────────────────────────

    [Fact]
    public void ExtractAllMetadata_CombinesBoth_ReturnsCodeMetadata()
    {
        var content = "namespace Engram.Store { public class SyncManager { } }";
        var relPath = "src/Engram/Store/SyncManager.cs";
        var extension = "cs";

        var result = CodeMetadataExtractor.ExtractAllMetadata(content, relPath, extension);

        Assert.Equal(relPath, result.FilePath);
        Assert.Equal("SyncManager", result.Symbol);
        Assert.Equal("Engram.Store", result.Namespace);
    }

    [Fact]
    public void ExtractAllMetadata_SymbolOnly_ReturnsPartialMetadata()
    {
        // Content has a class but path doesn't match namespace pattern
        var content = "public class OnlySymbol { }";
        var relPath = "tests/OnlySymbol.cs";
        var extension = "cs";

        var result = CodeMetadataExtractor.ExtractAllMetadata(content, relPath, extension);

        Assert.Equal(relPath, result.FilePath);
        Assert.Equal("OnlySymbol", result.Symbol);
        Assert.Null(result.Namespace);
    }

    [Fact]
    public void ExtractAllMetadata_NamespaceOnly_ReturnsPartialMetadata()
    {
        // Path matches namespace but content has no symbol (no valid C# type declaration)
        // Use content that avoids "class X" pattern with uppercase identifier
        var content = "// This file contains only configuration constants, no types defined";
        var relPath = "src/Engram/Store/NoSymbol.cs";
        var extension = "cs";

        var result = CodeMetadataExtractor.ExtractAllMetadata(content, relPath, extension);

        Assert.Equal(relPath, result.FilePath);
        Assert.Null(result.Symbol);
        Assert.Equal("Engram.Store", result.Namespace);
    }

    [Fact]
    public void ExtractAllMetadata_Neither_ReturnsEmptyMetadata()
    {
        var content = "// no class here";
        var relPath = "README.md";
        var extension = "md";

        var result = CodeMetadataExtractor.ExtractAllMetadata(content, relPath, extension);

        Assert.Equal(relPath, result.FilePath);
        Assert.Null(result.Symbol);
        Assert.Null(result.Namespace);
    }

    [Fact]
    public void ExtractAllMetadata_FilePathAlwaysSet()
    {
        // Even with empty content and non-standard path, FilePath should be set
        var result = CodeMetadataExtractor.ExtractAllMetadata("", "", "");
        Assert.Equal("", result.FilePath);
    }
}
