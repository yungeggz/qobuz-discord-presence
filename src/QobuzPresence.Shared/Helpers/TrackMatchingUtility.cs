namespace QobuzPresence.Helpers;

public enum TrackTitleMatchStage
{
    None,
    Exact,
    StrippedDecoration,
    Substring
}

public static class TrackMatchingUtility
{
    public static bool ArtistMatches(string trackArtist, string? windowArtist)
    {
        string normalizedTrackArtist = TextUtility.NormalizeForComparison(trackArtist);
        string normalizedWindowArtist = TextUtility.NormalizeForComparison(windowArtist ?? string.Empty);

        return normalizedTrackArtist == normalizedWindowArtist ||
            normalizedTrackArtist.Contains(normalizedWindowArtist, StringComparison.OrdinalIgnoreCase) ||
            normalizedWindowArtist.Contains(normalizedTrackArtist, StringComparison.OrdinalIgnoreCase);
    }

    public static TrackTitleMatchStage GetTitleMatchStage(string left, string right)
    {
        if (TitlesMatchExactly(left, right))
        {
            return TrackTitleMatchStage.Exact;
        }

        if (TitlesMatchWithoutDecorations(left, right))
        {
            return TrackTitleMatchStage.StrippedDecoration;
        }

        if (TitlesContainEachOther(left, right))
        {
            return TrackTitleMatchStage.Substring;
        }

        return TrackTitleMatchStage.None;
    }

    public static bool ShouldPreferWindowTitle(string trackTitle, string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(trackTitle) || string.IsNullOrWhiteSpace(windowTitle))
        {
            return false;
        }

        string normalizedTrackTitle = TextUtility.NormalizeForComparison(trackTitle);
        string normalizedWindowTitle = TextUtility.NormalizeForComparison(windowTitle);

        return normalizedTrackTitle != normalizedWindowTitle &&
            TextUtility.IsEquivalentOrContained(trackTitle, windowTitle);
    }

    public static bool TrackMatchesWindow(string trackTitle, string trackArtist, string windowTitle, string? windowArtist)
    {
        return ArtistMatches(trackArtist, windowArtist) &&
            GetTitleMatchStage(trackTitle, windowTitle) is not TrackTitleMatchStage.None;
    }

    private static bool TitlesMatchExactly(string left, string right)
    {
        return TextUtility.NormalizeForComparison(left) == TextUtility.NormalizeForComparison(right);
    }

    private static bool TitlesMatchWithoutDecorations(string left, string right)
    {
        string strippedLeft = TextUtility.StripBracketedDecorations(left);
        string strippedRight = TextUtility.StripBracketedDecorations(right);

        if (string.IsNullOrWhiteSpace(strippedLeft) || string.IsNullOrWhiteSpace(strippedRight))
        {
            return false;
        }

        return TextUtility.NormalizeForComparison(strippedLeft) == TextUtility.NormalizeForComparison(strippedRight);
    }

    private static bool TitlesContainEachOther(string left, string right)
    {
        string normalizedLeft = TextUtility.NormalizeForComparison(left);
        string normalizedRight = TextUtility.NormalizeForComparison(right);

        return normalizedLeft.Contains(normalizedRight, StringComparison.Ordinal) ||
            normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal);
    }
}
