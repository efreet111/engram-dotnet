using Xunit;

namespace Engram.Obsidian.Tests;

public class GraphConfigTests
{
    private readonly string _tempDir;

    public GraphConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [Theory]
    [InlineData("preserve", GraphConfigMode.Preserve)]
    [InlineData("force", GraphConfigMode.Force)]
    [InlineData("skip", GraphConfigMode.Skip)]
    public void Parse_ValidValues_ReturnsCorrectMode(string input, GraphConfigMode expected)
    {
        Assert.Equal(expected, GraphConfig.Parse(input));
    }

    [Theory]
    [InlineData("PRESERVE")]
    [InlineData("Force")]
    [InlineData("invalid")]
    [InlineData("")]
    public void Parse_InvalidValues_ThrowsArgumentException(string input)
    {
        Assert.Throws<ArgumentException>(() => GraphConfig.Parse(input));
    }

    [Fact]
    public void WriteGraphConfig_Skip_DoesNotCreateFile()
    {
        GraphConfig.WriteGraphConfig(_tempDir, GraphConfigMode.Skip);

        Assert.False(Directory.Exists(Path.Combine(_tempDir, ".obsidian")));
    }

    [Fact]
    public void WriteGraphConfig_Force_CreatesGraphJson()
    {
        GraphConfig.WriteGraphConfig(_tempDir, GraphConfigMode.Force);

        var graphPath = Path.Combine(_tempDir, ".obsidian", "graph.json");
        Assert.True(File.Exists(graphPath));
        var content = File.ReadAllText(graphPath);
        Assert.Contains("collapse-filter", content);
        Assert.Contains("colorGroups", content);
    }

    [Fact]
    public void WriteGraphConfig_Preserve_FileAbsent_CreatesGraphJson()
    {
        GraphConfig.WriteGraphConfig(_tempDir, GraphConfigMode.Preserve);

        var graphPath = Path.Combine(_tempDir, ".obsidian", "graph.json");
        Assert.True(File.Exists(graphPath));
    }

    [Fact]
    public void WriteGraphConfig_Preserve_FileExists_DoesNotOverwrite()
    {
        var obsidianDir = Path.Combine(_tempDir, ".obsidian");
        Directory.CreateDirectory(obsidianDir);
        var graphPath = Path.Combine(obsidianDir, "graph.json");
        File.WriteAllText(graphPath, "{\"custom\": true}");

        GraphConfig.WriteGraphConfig(_tempDir, GraphConfigMode.Preserve);

        var content = File.ReadAllText(graphPath);
        Assert.Equal("{\"custom\": true}", content);
    }

    [Fact]
    public void WriteGraphConfig_Force_OverwritesExisting()
    {
        var obsidianDir = Path.Combine(_tempDir, ".obsidian");
        Directory.CreateDirectory(obsidianDir);
        var graphPath = Path.Combine(obsidianDir, "graph.json");
        File.WriteAllText(graphPath, "{\"custom\": true}");

        GraphConfig.WriteGraphConfig(_tempDir, GraphConfigMode.Force);

        var content = File.ReadAllText(graphPath);
        Assert.Contains("collapse-filter", content);
        Assert.DoesNotContain("\"custom\": true", content);
    }
}
