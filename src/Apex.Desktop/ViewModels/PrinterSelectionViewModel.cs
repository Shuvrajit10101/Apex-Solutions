using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The <b>Printer</b> panel (census row 12.5 — physical printer output), hosted as its own cascading Miller
/// column to the RIGHT of the Print Preview it prints, never a stacked overlay, exactly like the F12
/// <see cref="PrintConfigViewModel"/> beside it. The operator picks a queue, sets the copy count, reads what
/// this platform will do with the bytes, and presses Ctrl+A to spool.
///
/// <para><b>What this row actually adds.</b> The renderer, the preview, the page config and the copy count were
/// already shipped; the missing last mile was enumerating the machine's physical printers, choosing one, and
/// handing the job to the spooler. Avalonia 12.0.5 has no printing API of any kind, so both halves arrive
/// through the <see cref="IPrinterDevices"/> / <see cref="IPrintJobSubmitter"/> seam and the platform shim
/// behind it.</para>
///
/// <para><b>🔴 The copy count is the preview's, not a second one.</b> <see cref="Copies"/> reads and writes
/// <c>PrintPreviewViewModel.Copies</c> directly. It is not a local field that is pushed on apply, because a
/// second copy count would be a second truth: the renderer collates copies INTO the PDF, so a panel-local count
/// plus the preview's count is how a three-copy job becomes nine sets of paper. There is one count, it lives on
/// the preview, and the spooler is always asked for exactly one — see <see cref="PrintJobBuilder.SpoolerCopies"/>.</para>
///
/// <para><b>Cancel changes nothing.</b> Opening the panel and pressing Esc without touching it leaves the
/// preview byte-identical and spools nothing; the only mutation the panel performs is the copy count the
/// operator typed, which is the preview's own knob and re-renders visibly in the preview beneath.</para>
/// </summary>
public sealed partial class PrinterSelectionViewModel : ViewModelBase
{
    private readonly PrintPreviewViewModel _preview;
    private readonly IPrintJobSubmitter _submitter;

    /// <summary>
    /// The empty-state line. It says the two things an operator with no printers needs: that the machine has
    /// none, and that Save PDF still works — the fallback that exists on every platform and every device.
    /// </summary>
    public const string NoPrinterNotice =
        "No printers are installed on this machine. Use Save PDF on the preview and print the file elsewhere.";

    /// <summary>Shown while a job is being handed to the spooler.</summary>
    public const string SubmittingStatus = "Sending to the printer…";

    /// <summary>The column title / heading.</summary>
    public string Title => "Printer";

    /// <summary>The document being printed (its heading line), shown under the title.</summary>
    public string DocumentTitle => _preview.ReportTitle;

    /// <summary>The queues this machine offers, in the order the OS reported them. May be empty.</summary>
    public ObservableCollection<PrinterDevice> Printers { get; } = new();

    /// <summary>The highlighted queue (two-way bound to the list's SelectedItem). Arrows move it; Ctrl+A prints to it.</summary>
    [ObservableProperty] private PrinterDevice? _selected;

    /// <summary>A status / empty-state / result line.</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>True while a submission is in flight, so the Print button cannot queue the same job twice.</summary>
    [ObservableProperty] private bool _isSubmitting;

    /// <summary>The result of the last submission attempt, or <c>null</c> if nothing has been submitted yet.</summary>
    public PrintJobResult? LastResult { get; private set; }

    /// <summary>True when there is at least one queue to print to.</summary>
    public bool HasPrinters => Printers.Count > 0;

    /// <summary>True when the machine offers no queue at all — drives the empty state, and hides the Print button.</summary>
    public bool HasNoPrinters => !HasPrinters;

    /// <summary>
    /// What this platform will do with the bytes on the selected queue, in the operator's words. This is the
    /// only place a Windows RAW submission's limit is stated, so it is shown always, not on failure.
    /// </summary>
    public string SubmissionNotice => Selected?.SubmissionNotice ?? NoPrinterNotice;

    /// <summary>
    /// The number of collated copies. Reads and writes the preview's own knob — see the class remarks. Values
    /// below one are clamped to one, matching <c>PageConfig.EffectiveCopies</c>, so the box can never describe
    /// a job that prints nothing.
    /// </summary>
    public int Copies
    {
        get => _preview.Copies;
        set
        {
            int wanted = value < 1 ? 1 : value;

            // 🔴 The notification is raised whenever the ACCEPTED value differs from the value that was OFFERED,
            // not only when the stored value moves. Typing 0 into the box clamps to 1, which on an unchanged
            // preview is no move at all — and returning silently there leaves the two-way binding showing "0"
            // while the job will print 1. A copies box that displays a number the paper will not match is the
            // screen lying about what is being printed, so the clamp must always push itself back to the box.
            if (_preview.Copies == wanted)
            {
                if (wanted != value) OnPropertyChanged();
                return;
            }

            _preview.Copies = wanted;
            OnPropertyChanged();
        }
    }

    public PrinterSelectionViewModel(
        PrintPreviewViewModel preview, IPrinterDevices devices, IPrintJobSubmitter submitter)
    {
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        ArgumentNullException.ThrowIfNull(devices);
        _submitter = submitter ?? throw new ArgumentNullException(nameof(submitter));

        // Enumerated ONCE, when the column opens. Re-listing on every keystroke would spawn a process per
        // character on the CUPS platforms; the operator re-opens the column to pick up a newly added queue.
        foreach (var device in devices.List())
            Printers.Add(device);

        // The OS's own default is pre-selected, so the common case is Ctrl+P, Ctrl+A. When the OS names no
        // default we take the first queue rather than none, because a panel that opens with nothing selected
        // makes the operator do work the OS already did.
        Selected = null;
        foreach (var device in Printers)
            if (device.IsDefault) { Selected = device; break; }
        if (Selected is null && Printers.Count > 0) Selected = Printers[0];

        Status = HasPrinters ? string.Empty : NoPrinterNotice;
    }

    partial void OnSelectedChanged(PrinterDevice? value) => OnPropertyChanged(nameof(SubmissionNotice));

    /// <summary>
    /// Ctrl+A / the Print button: builds the job from the previewed bytes and hands it to the spooler, then
    /// reports what happened. Never throws — a refusal is a status line, not a crash — and never submits twice
    /// concurrently.
    /// </summary>
    public async Task PrintAsync()
    {
        if (IsSubmitting) return;

        var built = PrintJobBuilder.Build(Selected, _preview.PdfBytes, _preview.ReportTitle);
        if (!built.CanPrint)
        {
            LastResult = null;
            Status = built.Message;
            return;
        }

        IsSubmitting = true;
        Status = SubmittingStatus;
        try
        {
            var result = await _submitter.SubmitAsync(built.Job!).ConfigureAwait(true);
            LastResult = result;
            Status = result.Message;
        }
        catch (Exception ex)
        {
            // The interface forbids throwing, but a third implementation could; a print button must not be able
            // to take the shell down, so the contract is enforced here as well as stated there.
            LastResult = new PrintJobResult(false, ex.Message);
            Status = "Could not print: " + ex.Message;
        }
        finally
        {
            IsSubmitting = false;
        }
    }
}
