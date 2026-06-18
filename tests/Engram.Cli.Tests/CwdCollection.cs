using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// Collection for tests that change the current working directory (CWD).
/// Tests in this collection run serially to avoid CWD conflicts.
/// </summary>
[CollectionDefinition("CwdSensitive", DisableParallelization = true)]
public class CwdCollection : ICollectionFixture<object>;
