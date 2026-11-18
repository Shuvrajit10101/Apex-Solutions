using System;
using System.Globalization;
using System.Linq;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>A culture whose calendar is hostile BY CONSTRUCTION, not by the runner's ICU default.</b>
///
/// <para><b>Why this exists.</b> The culture locks in this project used to take <c>new CultureInfo("ar-SA")</c>
/// and <c>"th-TH"</c> and rely on their <b>default</b> calendar being Umm al-Qura and Thai Buddhist. That default
/// is data, not law: it comes from the ICU/CLDR build on the machine. When GitHub's <c>ubuntu-latest</c> moved
/// from ubuntu-24.04 to ubuntu-26.04 (image 20261111) the newer CLDR gave <c>ar-SA</c> a <b>Gregorian</b> default
/// calendar, so the Kerala Flood Cess lock's non-vacuity guard fired on every PR, and every lock that had no
/// such guard silently began passing without exercising anything.</para>
///
/// <para>So the calendar is now SET explicitly — chosen from the culture's own
/// <see cref="CultureInfo.OptionalCalendars"/>, which is the only list <see cref="DateTimeFormatInfo.Calendar"/>
/// accepts — and every caller still proves, with <see cref="AssertHostile"/>, that the culture really formats a
/// date differently from the invariant one. If no runner can install a non-Gregorian calendar, the lock fails
/// loudly rather than going green.</para>
/// </summary>
internal static class HostileCalendarCulture
{
    /// <summary>
    /// A writable clone of <paramref name="name"/> with its calendar forced to the first non-Gregorian calendar
    /// it lists among its optional calendars: Umm al-Qura before Hijri for <c>ar-SA</c>, Thai Buddhist for
    /// <c>th-TH</c>. Returns the clone unchanged (still Gregorian) only when the culture offers no such
    /// calendar — <see cref="AssertHostile"/> then fails the test loudly.
    /// </summary>
    public static CultureInfo Create(string name)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo(name).Clone();
        var calendar = culture.OptionalCalendars
            .OrderBy(Preference)
            .FirstOrDefault(c => c is not GregorianCalendar);
        if (calendar is not null)
            culture.DateTimeFormat.Calendar = calendar;
        return culture;
    }

    private static int Preference(Calendar calendar) => calendar switch
    {
        UmAlQuraCalendar => 0,
        HijriCalendar => 1,
        ThaiBuddhistCalendar => 2,
        _ => 3,
    };

    /// <summary>
    /// The non-vacuity guard. A culture lock built on a culture that formats <c>2019-08</c> the same way the
    /// invariant culture does proves nothing; this refuses to let it pass.
    /// </summary>
    public static void AssertHostile(CultureInfo culture)
    {
        var probe = new DateOnly(2019, 8, 1);
        string rendered = probe.ToString("yyyy-MM", culture);
        Xunit.Assert.True(
            rendered != "2019-08",
            $"this runner could not give {culture.Name} a non-Gregorian calendar (calendar in use: "
            + $"{culture.DateTimeFormat.Calendar.GetType().Name}; optional: "
            + string.Join(", ", culture.OptionalCalendars.Select(c => c.GetType().Name))
            + "), so this culture lock would pass without exercising anything. Treat it as un-run, not as green.");
    }
}
