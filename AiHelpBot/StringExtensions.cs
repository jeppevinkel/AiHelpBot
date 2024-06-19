namespace AiHelpBot;

public static class StringExtensions
{
    public static IEnumerable<string> SplitByLength(this string text, int maxLength, string newline)
    {
        List<string> splitText = [];
        while (!string.IsNullOrEmpty(text))
        {
            int currentSplittingIndex = SplittingIndex(text, maxLength, newline);
            splitText.Add(text[..currentSplittingIndex]);
            text = text[(currentSplittingIndex)..];
        }

        return splitText;
    }

    private static int SplittingIndex(string text, int maxLength, string newline)
    {
        if (text.Length <= maxLength)
        {
            return text.Length;
        }

        var remainingText = text[..(maxLength + 2 * newline.Length)];

        while (true)
        {
            var possibleIndex = remainingText.LastIndexOf(newline, StringComparison.Ordinal);
            remainingText = remainingText[..possibleIndex];

            if (remainingText.EndsWith(newline))
            {
                remainingText = remainingText.RightTrim(newline);
            }
            else if (possibleIndex <= maxLength)
            {
                return possibleIndex;
            }
        }
    }

    private static string RightTrim(this string text, string toBeTrimmed)
    {
        while (text.EndsWith(toBeTrimmed))
        {
            text = text[..^toBeTrimmed.Length];
        }

        return text;
    }
}