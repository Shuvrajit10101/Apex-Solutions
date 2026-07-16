using Apex.Ledger.Domain;

namespace Apex.Ledger.Io;

/// <summary>
/// The <b>stubbed GSP seam</b> (Phase 9 slice 4a; RQ-30). A future GSP (GST Suvidha Provider) integration would wire in
/// here without reworking the seam — but it is <b>NOT built in Phase 9</b>: every member throws
/// <see cref="NotSupportedException"/>. The app never holds a GSP/vendor credential (ER-16), so this connector accepts
/// and stores none. Configure <see cref="OfflineJsonConnector"/> or the CustomerNicDirect live path instead.
/// </summary>
public sealed class GspConnector : IGstPortalConnector
{
    private const string NotBuilt =
        "GSP integration is not built in Phase 9 — configure OfflineJson or CustomerNicDirect.";

    /// <inheritdoc />
    public GstConnectorMode Mode => GstConnectorMode.Gsp;

    /// <inheritdoc />
    public IrnSubmissionResult SubmitForIrn(Inv01Request request) => throw new NotSupportedException(NotBuilt);

    /// <inheritdoc />
    public IrnCancelResult CancelIrn(IrnCancelRequest request) => throw new NotSupportedException(NotBuilt);

    /// <inheritdoc />
    public EwbSubmissionResult SubmitEway(Ewb01Request request) => throw new NotSupportedException(NotBuilt);

    /// <inheritdoc />
    public EwbCancelResult CancelEway(EwbCancelRequest request) => throw new NotSupportedException(NotBuilt);

    /// <inheritdoc />
    public EwbExtendResult ExtendEway(EwbExtendRequest request) => throw new NotSupportedException(NotBuilt);

    /// <inheritdoc />
    public EwbSubmissionResult SubmitConsolidatedEway(Ewb02Request request) => throw new NotSupportedException(NotBuilt);

    /// <inheritdoc />
    public Gstr2bImportResult FetchStatement(Gstr2bFetchRequest request) => throw new NotSupportedException(NotBuilt);
}
