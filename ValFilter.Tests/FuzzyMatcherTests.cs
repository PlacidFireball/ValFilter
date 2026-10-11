using ValFilter;

namespace ValFilter.Tests;

public class FuzzyMatcherTests
{
    private static int Score(string query, string name, bool typos = true) =>
        FuzzyMatcher.Score(new FuzzyMatcher.Query(query), new FuzzyMatcher.Candidate(name), typos);

    private static bool IsClean(int score) => score >= FuzzyMatcher.CleanTier;
    private static bool IsTypo(int score) => score > 0 && score < FuzzyMatcher.CleanTier;

    private static readonly string[] Names =
    {
        "Wood arrow", "Fire arrow", "Flinthead arrow", "Bronzehead arrow", "Ironhead arrow", "Obsidian arrow",
        "Crude bow", "Finewood bow", "Huntsman bow",
        "Bronze sword", "Iron sword", "Silver sword",
        "Stone axe", "Flint axe", "Bronze axe", "Iron axe", "Battleaxe",
        "Club", "Bronze mace", "Iron mace",
        "Leather tunic", "Rag tunic", "Troll leather tunic", "Root harnesk", "Fenris coat", "Wolf fur cape",
        "Hammer", "Hoe", "Cultivator", "Wood shield", "Banded shield", "Black metal shield",
    };

    /// Names that match, best first, using the same tiebreak the plugin uses (shorter name first).
    private static List<string> Search(string query, bool typos = true) =>
        Names.Select(n => (Name: n, Score: Score(query, n, typos)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Name.Length)
            .Select(x => x.Name)
            .ToList();

    [Fact]
    public void EmptyQueryMatchesEverything()
    {
        Assert.Equal(Names.Length, Search("").Count);
        Assert.Equal(Names.Length, Search("   ").Count);
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        Assert.True(IsClean(Score("IRON", "Iron sword")));
    }

    [Theory]
    [InlineData("bzarw", "Bronzehead arrow")]   // abbreviation, letters in order
    [InlineData("flkn", "Flint knife")]
    [InlineData("finewodbow", "Finewood bow")]  // missing letter is still a subsequence
    [InlineData("bronze arrow", "Bronzehead arrow")]
    [InlineData("bronzearrow", "Bronzehead arrow")] // spaces don't matter
    public void SubsequenceMatchesAreClean(string query, string name)
    {
        Assert.True(IsClean(Score(query, name)));
    }

    [Fact]
    public void OutOfOrderLettersDontCleanMatch()
    {
        Assert.Equal(0, Score("wob", "Crude bow", typos: false));
    }

    [Fact]
    public void WordStartsBeatMidWordMatches()
    {
        // "ia": I(ron) A(xe) hits two word starts; F(i)re (a)rrow only one.
        Assert.True(Score("ia", "Iron axe") > Score("ia", "Fire arrow"));
    }

    [Fact]
    public void ContiguousBeatsScattered()
    {
        Assert.True(Score("axe", "Iron axe") > Score("axe", "Antler xbow elite"));
    }

    [Fact]
    public void BowFindsBowsFirst()
    {
        var results = Search("bow");
        Assert.Equal(new[] { "Crude bow", "Finewood bow", "Huntsman bow" }, results.Take(3).OrderBy(x => x));
    }

    [Fact]
    public void ShieldSearchFindsOnlyShields()
    {
        Assert.All(Search("shield"), name => Assert.EndsWith("shield", name));
    }

    [Theory]
    [InlineData("baer", "Bear cape")]           // transposition
    [InlineData("brnoze", "Bronze sword")]      // transposition
    [InlineData("bronse", "Bronze sword")]      // substitution
    [InlineData("hunstm", "Huntsman bow")]      // partially typed word with a typo
    [InlineData("cultivater", "Cultivator")]    // substitution in a long word
    [InlineData("fineowod bow", "Finewood bow")] // one typo'd word, one clean word
    public void TyposStillMatch(string query, string name)
    {
        Assert.True(IsTypo(Score(query, name)), $"expected '{query}' to typo-match '{name}', got {Score(query, name)}");
    }

    [Theory]
    [InlineData("baer", "Bear cape")]
    [InlineData("brnoze", "Bronze sword")]
    public void TyposCanBeDisabled(string query, string name)
    {
        Assert.Equal(0, Score(query, name, typos: false));
    }

    [Fact]
    public void TypoMatchesRankBelowCleanMatches()
    {
        Assert.True(Score("bronz", "Bronze sword") > Score("bronez", "Bronze sword"));
    }

    [Theory]
    [InlineData("iron", "Bronze sword")] // "iron" ~ "bron" is 1 edit, but the first letter differs
    [InlineData("axr", "Iron axe")]      // too short for typo tolerance
    [InlineData("hammr", "Iron mace")]
    public void NoNonsenseTypoMatches(string query, string name)
    {
        Assert.Equal(0, Score(query, name));
    }

    [Fact]
    public void IronDoesntReturnBronzeGear()
    {
        Assert.DoesNotContain(Search("iron"), name => name.StartsWith("Bronze"));
    }

    [Fact]
    public void EveryWordMustMatchForTypoMatches()
    {
        Assert.Equal(0, Score("baer zzzz", "Bear cape"));
    }
}
