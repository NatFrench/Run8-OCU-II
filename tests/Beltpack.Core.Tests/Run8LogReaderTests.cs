using Beltpack.Core.Run8;

namespace Beltpack.Core.Tests;

public class Run8LogReaderTests
{
    private static Run8LogReader Read(params string[] lines)
    {
        var reader = new Run8LogReader();
        foreach (var line in lines)
            reader.ReadLine(line);
        return reader;
    }

    [Fact]
    public void PicksUpTheControllingUnit()
    {
        var r = Read(
            "2026-10-28 12:13:28,088 INFO  - Train now belongs to you",
            "2026-10-28 12:13:28,088 INFO  - Controlling-Unit is BNSF 8081",
            "2026-10-28 12:13:29,220 INFO  - BNSF8081 has been re-crewed by AI");
        Assert.Equal("8081", r.ControllingUnit);
    }

    [Fact]
    public void ForgetsTheUnitWhenItsTrainIsRemoved()
    {
        var r = Read(
            "2026-10-28 12:13:28,088 INFO  - Controlling-Unit is BNSF 8081",
            "2026-10-28 12:21:11,529 INFO  - Train Removed From World: 99991  BNSF8081  ");
        Assert.Null(r.ControllingUnit);
    }

    [Fact]
    public void KeepsTheUnitWhenAnotherTrainIsRemoved()
    {
        var r = Read(
            "2026-10-28 12:13:28,088 INFO  - Controlling-Unit is BNSF 8081",
            "2026-10-28 12:21:11,529 INFO  - Train Removed From World: 12345  UP4410  ");
        Assert.Equal("8081", r.ControllingUnit);
    }

    [Fact]
    public void ForgetsTheUnitOnLeavingTheSimOrRestarting()
    {
        Assert.Null(Read(
            "2026-10-28 12:13:28,088 INFO  - Controlling-Unit is BNSF 8081",
            "2026-10-28 12:21:13,890 INFO  - SimScreen Unload Content Complete").ControllingUnit);
        Assert.Null(Read(
            "2026-10-28 12:13:28,088 INFO  - Controlling-Unit is BNSF 8081",
            "2026-10-29 00:04:35,426 INFO  - ===== V3 Update24A Mar.30.2026 =====").ControllingUnit);
    }
}
