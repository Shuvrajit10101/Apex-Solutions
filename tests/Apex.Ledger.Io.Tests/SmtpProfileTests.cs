using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Tests for the capture-only SMTP profile value (RQ-27, R13). It records host/port/TLS/from only — never a
/// password. (SQLite round-trip of this value is covered in the persistence test project.)
/// </summary>
public sealed class SmtpProfileTests
{
    [Fact]
    public void Complete_when_host_port_and_from_present()
    {
        var p = new SmtpProfile { Host = "smtp.apexco.example", Port = 587, UseTls = true, FromAddress = "a@x.example" };
        Assert.True(p.IsComplete);
    }

    [Theory]
    [InlineData("", 587, "a@x.example")]
    [InlineData("smtp.x.example", 0, "a@x.example")]
    [InlineData("smtp.x.example", 70000, "a@x.example")]
    [InlineData("smtp.x.example", 587, "")]
    public void Incomplete_when_a_required_field_is_missing_or_out_of_range(string host, int port, string from)
    {
        var p = new SmtpProfile { Host = host, Port = port, FromAddress = from };
        Assert.False(p.IsComplete);
    }

    [Fact]
    public void Has_no_password_member()
    {
        // R13: the type must not carry a password. Fail loudly if one is ever added.
        var names = typeof(SmtpProfile).GetProperties().Select(pr => pr.Name.ToLowerInvariant());
        Assert.DoesNotContain(names, n => n.Contains("password") || n.Contains("secret") || n.Contains("pwd"));
    }
}
