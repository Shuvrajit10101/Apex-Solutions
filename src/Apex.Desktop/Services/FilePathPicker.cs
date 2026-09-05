using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apex.Desktop.Services;

/// <summary>What the operator is being asked to point at.</summary>
public enum FilePathPickKind
{
    /// <summary>An existing file to read from (a restore archive, an import file).</summary>
    OpenFile,

    /// <summary>A file to write to, which may or may not already exist (the <c>.eml</c> hand-off, a PDF).</summary>
    SaveFile,

    /// <summary>A folder to write into (the backup destination, an export destination).</summary>
    Folder,
}

/// <summary>
/// One named group of file patterns offered by the chooser, e.g. <c>("Apex backup archive", ["*.apexbak"])</c>.
/// Patterns are shell-style globs because that is what every desktop file dialog speaks.
/// </summary>
/// <param name="Name">The human label shown in the dialog's type dropdown.</param>
/// <param name="Patterns">The glob patterns this group matches.</param>
public sealed record FilePathFileType(string Name, IReadOnlyList<string> Patterns);

/// <summary>
/// A request for a data path, expressed so the view model can say <b>what</b> it needs without knowing <b>how</b>
/// the shell will ask for it. This is the whole of the seam: view models describe, the shell dialogs, and the
/// headless tests substitute.
/// </summary>
/// <param name="Kind">Open a file, save a file, or pick a folder.</param>
/// <param name="Title">The dialog title — says what the path is for, in the operator's words.</param>
/// <param name="StartFolder">Where the dialog should open. Empty means "wherever the OS last was".</param>
/// <param name="SuggestedFileName">
/// The file name to pre-fill for <see cref="FilePathPickKind.SaveFile"/>. Ignored for the other two kinds.
/// </param>
/// <param name="FileTypes">The type filters to offer. Empty means "any file".</param>
public sealed record FilePathPickRequest(
    FilePathPickKind Kind,
    string Title,
    string StartFolder,
    string SuggestedFileName,
    IReadOnlyList<FilePathFileType> FileTypes)
{
    /// <summary>Convenience factory for a folder request.</summary>
    public static FilePathPickRequest Folder(string title, string startFolder) =>
        new(FilePathPickKind.Folder, title, startFolder ?? string.Empty, string.Empty, Array.Empty<FilePathFileType>());

    /// <summary>Convenience factory for an open-file request.</summary>
    public static FilePathPickRequest OpenFile(string title, string startFolder, params FilePathFileType[] types) =>
        new(FilePathPickKind.OpenFile, title, startFolder ?? string.Empty, string.Empty,
            types ?? Array.Empty<FilePathFileType>());

    /// <summary>Convenience factory for a save-file request.</summary>
    public static FilePathPickRequest SaveFile(
        string title, string startFolder, string suggestedFileName, params FilePathFileType[] types) =>
        new(FilePathPickKind.SaveFile, title, startFolder ?? string.Empty, suggestedFileName ?? string.Empty,
            types ?? Array.Empty<FilePathFileType>());
}

/// <summary>
/// The one seam between "this screen needs a data path" and "the operating system knows how to ask for one".
///
/// <para><b>Why this exists at all</b> (census row 13.10 / <c>T1-20</c>): every data path in this product used to
/// be a typed string or a silent default to Documents — the backup destination, the restore source, the import
/// source, the export destination, the <c>.eml</c> hand-off and the print-preview PDF. A user restoring from a
/// backup had to type the full archive path from memory, and a mistyped restore path is the difference between a
/// backup feature and a data-loss event.</para>
///
/// <para><b>Why it is an interface</b>: a real file dialog cannot open in a headless test, and a capability no
/// test can reach is a capability no one can prove is reachable. Everything above this line — which screen asks
/// for what shape of path, and where the answer lands — is asserted through the real window and the real chord.
/// Only the dialog itself is substituted.</para>
/// </summary>
public interface IFilePathPicker
{
    /// <summary>
    /// Asks the operator for a path. Returns the chosen absolute path, or <c>null</c> when the operator cancelled
    /// — <b>cancel must change nothing</b>, so callers treat null as "leave the existing value alone".
    /// </summary>
    Task<string?> PickAsync(FilePathPickRequest request);
}
