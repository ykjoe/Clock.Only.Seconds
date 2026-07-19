using System;
using System.Data;

public class TimeDisp
{
    public TimeDisp()
	{
	}

    public enum TimeDisplayMode : ushort
    {
        OnlySeconds = 0,
        LocalGeneral,
        UtcGeneral,

    };

    public static string DispTimeNow(TimeDisplayMode mode, bool use24h)
	{
        string format = use24h ? "HH:mm:ss" : "hh:mm:ss tt";

        switch (mode)
		{
            case TimeDisplayMode.OnlySeconds:
                long only_seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return only_seconds.ToString("N0") + " s";
            case TimeDisplayMode.UtcGeneral:
                return DateTime.UtcNow.ToString(format);
            case TimeDisplayMode.LocalGeneral:
                return DateTime.Now.ToString(format);
            default:
                return "";
        } /*END switch (mode)*/
	}

    public static string DispTimeSpan(TimeSpan timespan, TimeDisplayMode mode)
    {
        // definition
        DateTime nowBase = (mode == TimeDisplayMode.UtcGeneral) ? DateTime.UtcNow : DateTime.Now;

        string prefix = (timespan.TotalSeconds >= 0) ? "  T- " : "  T+ ";

        // display
        long totalSeconds = (long)Math.Abs(timespan.TotalSeconds);
        int days = Math.Abs(timespan.Days);
        int hours = Math.Abs(timespan.Hours);
        int minutes = Math.Abs(timespan.Minutes);
        int seconds = Math.Abs(timespan.Seconds);
        switch (mode)
        {
            case TimeDisplayMode.OnlySeconds:
                return prefix + totalSeconds.ToString("N0") + " s";
            case TimeDisplayMode.UtcGeneral:
            case TimeDisplayMode.LocalGeneral:
                string durationStr = $"{days:D3}d{hours:D2}h{minutes:D2}m{seconds:D2}s";
                return prefix + durationStr;
            default:
                return "";
        } /*END switch (mode)*/
    }

}
