namespace NovaChat.Client.Services;

public static class IranTime
{
    private static readonly TimeZoneInfo TehranTimeZone = ResolveTehranTimeZone();

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TehranTimeZone);

    public static string Format(DateTime value, string format = "g")
    {
        // API timestamps are already Iran local time and intentionally have no offset.
        // UTC values are converted explicitly for callers that provide them.
        var iranTime = value.Kind == DateTimeKind.Utc
            ? TimeZoneInfo.ConvertTimeFromUtc(value, TehranTimeZone)
            : DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

        return iranTime.ToString(format);
    }

    private static TimeZoneInfo ResolveTehranTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
        }
    }
}
