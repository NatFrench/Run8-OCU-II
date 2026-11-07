using Beltpack.Core.Talk;
using Xunit;

namespace Beltpack.Core.Tests;

public class TalkerScriptTests
{
    [Fact]
    public void PenaltyPlaysTheCauseThenTheRecoveryInstruction()
    {
        var (clips, missing) = TalkerScript.Parse("Operator requested emergency. Recover emergency from beltpack.");

        Assert.Equal(["Phrases/OpRequestedEMG", "Phrases/RecoverEMGfromBelt"], clips.Select(c => c.Name));
        Assert.All(clips, c => Assert.True(c.EndsSentence));
        Assert.Empty(missing);
    }

    [Fact]
    public void StatusReadsTheRailroadDigitsAndOcuLetter()
    {
        var (clips, missing) = TalkerScript.Parse("B N S F 4 0 2 6, O C U A. Low brake pipe pressure. Brake pipe 52 P S I.");

        Assert.Equal(
            ["Railroads/BNSF", "Numbers/4", "Numbers/0", "Numbers/2", "Numbers/6", "Phrases/A", "Phrases/LowBrakePipePressure"],
            clips.Select(c => c.Name));
        Assert.False(clips[0].EndsSentence);
        Assert.True(clips[5].EndsSentence);
        Assert.Equal(["O C U", "Brake pipe 52 P S I"], missing);
    }

    [Fact]
    public void UnrecordedSentencesAreSilentAndListed()
    {
        var (clips, missing) = TalkerScript.Parse("Changeover to manual.");

        Assert.Empty(clips);
        Assert.Equal(["Changeover to manual"], missing);
    }

    [Fact]
    public void CauseAndUnknownRailroadsSkipJustTheUnrecordedWords()
    {
        var (clips, missing) = TalkerScript.Parse("Locomotive 1 2, O C U B. OK. Cause: Communication interrupted.");

        Assert.Equal(["Numbers/1", "Numbers/2", "Phrases/B", "Phrases/CommInterrupted"], clips.Select(c => c.Name));
        Assert.Equal(["Locomotive", "O C U", "OK", "Cause"], missing);
    }

    [Fact]
    public void TiltTimeoutAddsTheOcuLetter()
    {
        var (clips, _) = TalkerScript.Parse("Tilt timeout from B.");

        Assert.Equal(["Phrases/TiltTimeoutFrom", "Phrases/B"], clips.Select(c => c.Name));
    }
}
