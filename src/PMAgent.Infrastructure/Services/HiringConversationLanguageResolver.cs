namespace PMAgent.Infrastructure.Services;

internal static class HiringConversationLanguageResolver
{
    public static string ResolveInterviewLanguageChoice(string candidateMessage, string fallbackLanguage = "EN")
    {
        if (string.IsNullOrWhiteSpace(candidateMessage))
            return fallbackLanguage;

        var lower = candidateMessage.Trim().ToLowerInvariant();

        if (lower.Contains("english", StringComparison.Ordinal)
            || lower.Contains("speak english", StringComparison.Ordinal)
            || lower.Contains("in english", StringComparison.Ordinal))
            return "EN";

        if (lower.Contains("tiếng việt", StringComparison.Ordinal)
            || lower.Contains("tieng viet", StringComparison.Ordinal)
            || lower.Contains("nói tiếng việt", StringComparison.Ordinal)
            || lower.Contains("noi tieng viet", StringComparison.Ordinal)
            || lower.Contains("bằng tiếng việt", StringComparison.Ordinal)
            || lower.Contains("bang tieng viet", StringComparison.Ordinal))
            return "VI";

        return DetectLanguage(candidateMessage) switch
        {
            "VI" => "VI",
            "EN" => "EN",
            _ => fallbackLanguage
        };
    }

    public static string ResolveInitialLanguage(params string[] texts)
    {
        var combined = string.Join("\n", texts.Where(text => !string.IsNullOrWhiteSpace(text)));
        return DetectLanguage(combined);
    }

    public static string ResolveUpdatedLanguage(string currentLanguage, string latestMessage)
    {
        var detected = DetectLanguage(latestMessage);
        return string.Equals(detected, "VI", StringComparison.OrdinalIgnoreCase)
            ? "VI"
            : currentLanguage;
    }

    private static string DetectLanguage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "EN";

        if (ContainsVietnameseMarkers(text))
            return "VI";

        return "EN";
    }

    private static bool ContainsVietnameseMarkers(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower.IndexOfAny(['ă', 'â', 'đ', 'ê', 'ô', 'ơ', 'ư', 'á', 'à', 'ả', 'ã', 'ạ', 'ấ', 'ầ', 'ẩ', 'ẫ', 'ậ', 'ắ', 'ằ', 'ẳ', 'ẵ', 'ặ', 'é', 'è', 'ẻ', 'ẽ', 'ẹ', 'ế', 'ề', 'ể', 'ễ', 'ệ', 'í', 'ì', 'ỉ', 'ĩ', 'ị', 'ó', 'ò', 'ỏ', 'õ', 'ọ', 'ố', 'ồ', 'ổ', 'ỗ', 'ộ', 'ớ', 'ờ', 'ở', 'ỡ', 'ợ', 'ú', 'ù', 'ủ', 'ũ', 'ụ', 'ứ', 'ừ', 'ử', 'ữ', 'ự', 'ý', 'ỳ', 'ỷ', 'ỹ', 'ỵ']) >= 0
            || lower.Contains("tiếng việt", StringComparison.Ordinal)
            || lower.Contains("tieng viet", StringComparison.Ordinal)
            || lower.Contains("ứng viên", StringComparison.Ordinal)
            || lower.Contains("ung vien", StringComparison.Ordinal)
            || lower.Contains("bạn", StringComparison.Ordinal)
            || lower.Contains("ban ", StringComparison.Ordinal)
            || lower.Contains("câu hỏi", StringComparison.Ordinal)
            || lower.Contains("cau hoi", StringComparison.Ordinal)
            || lower.Contains("gợi ý", StringComparison.Ordinal)
            || lower.Contains("goi y", StringComparison.Ordinal);
    }
}