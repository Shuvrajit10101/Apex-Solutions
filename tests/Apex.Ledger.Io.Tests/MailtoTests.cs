using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Tests for the RFC-6068 mailto: builder (RQ-25 quick attachment-less compose). Deterministic and
/// percent-encoded; no clock, no RNG.
/// </summary>
public sealed class MailtoTests
{
    [Fact]
    public void Simple_to_only()
    {
        string uri = Mailto.Build(new[] { "a@x.example" }, null, null, null);
        Assert.Equal("mailto:a@x.example", uri);
    }

    [Fact]
    public void To_cc_subject_body_percent_encoded()
    {
        string uri = Mailto.Build(
            new[] { "a@x.example" },
            new[] { "b@y.example" },
            "Trial Balance & P/L",
            "Hi there,\r\nsee attached.");

        // '@' is left literal (allowed in the 'to' path); the query is percent-encoded per RFC 6068.
        Assert.StartsWith("mailto:a@x.example?", uri);
        Assert.Contains("cc=b%40y.example", uri);
        Assert.Contains("subject=Trial%20Balance%20%26%20P%2FL", uri);
        Assert.Contains("body=Hi%20there%2C%0D%0Asee%20attached.", uri);
        // Fields are '&'-joined.
        Assert.Contains("&", uri);
    }

    [Fact]
    public void Multiple_to_are_comma_separated_in_the_path()
    {
        string uri = Mailto.Build(new[] { "a@x.example", "b@x.example" }, null, null, null);
        // A comma in the 'to' path is percent-encoded to keep the URI unambiguous.
        Assert.Equal("mailto:a@x.example%2Cb@x.example", uri);
    }

    [Fact]
    public void Deterministic()
    {
        string a = Mailto.Build(new[] { "a@x.example" }, null, "S", "B");
        string b = Mailto.Build(new[] { "a@x.example" }, null, "S", "B");
        Assert.Equal(a, b);
    }

    [Fact]
    public void No_tally_branding_from_a_clean_input()
    {
        string uri = Mailto.Build(new[] { "a@x.example" }, null, "Report", "See attached");
        Assert.DoesNotContain("tally", uri.ToLowerInvariant());
    }
}
