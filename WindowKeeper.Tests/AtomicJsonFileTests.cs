using WindowKeeper;
using Xunit;

namespace WindowKeeper.Tests;

public sealed class AtomicJsonFileTests
{
    [Fact]
    public void ReadRecoversLastValidBackupWhenPrimaryIsCorrupt()
    {
        string directory = Path.Combine(Path.GetTempPath(), "WindowKeeperTests-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "positions.json");
        Directory.CreateDirectory(directory);
        try
        {
            AtomicJsonFile.Write(path, new Sample { Value = 1 });
            AtomicJsonFile.Write(path, new Sample { Value = 2 });
            File.WriteAllText(path, "{invalid json");

            Sample? recovered = AtomicJsonFile.Read<Sample>(path);

            Assert.NotNull(recovered);
            Assert.Equal(1, recovered.Value);
            Assert.Contains(Directory.EnumerateFiles(directory),
                file => Path.GetFileName(file).StartsWith("positions.corrupt-", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class Sample
    {
        public int Value { get; set; }
    }
}
