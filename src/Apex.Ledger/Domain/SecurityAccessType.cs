namespace Apex.Ledger.Domain;

/// <summary>
/// The six access types a security level's <b>"Disallow/Allow the following facilities"</b> list grades a
/// facility with (census 16.2).
///
/// <para><b>R7 — ATTESTED, verbatim.</b> help.tallysolutions.com/manage-users-in-tallyprime/ lists the access
/// types on the security-level screen as <b>Full Access · Create · Alter · Display · Print · Preview</b>,
/// corroborated on help.tallysolutions.com/tally-prime/access-control-data-security/data-security-faq/. The
/// ordinals are ours; the six members and their captions are the vendor's.</para>
///
/// <para>🔴 <b>The ordinals are PERSISTED</b> (<c>security_level_rules.access_type</c>, schema v56) and must
/// therefore only ever be APPENDED to — renumbering would silently re-interpret every stored rule in every
/// existing book, exactly the trap <c>NumberingMethod</c> records at v53.</para>
/// </summary>
public enum SecurityAccessType
{
    /// <summary>"Full Access" — the facility in every mode, including deletion. As a DISALLOW rule it removes the
    /// facility outright; as an ALLOW rule it grants every mode below.</summary>
    FullAccess = 0,

    /// <summary>"Create" — add new entries only.</summary>
    Create = 1,

    /// <summary>"Alter" — modify an existing entry.</summary>
    Alter = 2,

    /// <summary>"Display" — view on screen.</summary>
    Display = 3,

    /// <summary>"Print" — send to a printer.</summary>
    Print = 4,

    /// <summary>"Preview" — the on-screen print preview.</summary>
    Preview = 5,
}
