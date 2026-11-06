using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Crush.Viewer;

/// <summary>Where a picture goes when it is printed.</summary>
internal enum PrintRoute {

  /// <summary>Straight to the spooler, without anything else opening.</summary>
  Printer,

  /// <summary>To whatever application the platform opens a PDF with, which then offers its own print dialog.</summary>
  Viewer,
}

/// <summary>One way off the machine, named as it will be offered to the user.</summary>
/// <param name="Route">Which of the two kinds of hand-off this is.</param>
/// <param name="Text">What the drop-down calls it.</param>
/// <param name="Program">The executable to run, or empty when the platform's shell is used instead.</param>
internal readonly record struct PrintDestination(PrintRoute Route, string Text, string Program);

/// <summary>Printing, by way of a PDF this repository writes and the platform's own print path.</summary>
/// <remarks>
/// <para>
/// The toolkit has no printing at all — no printer, no page setup, no device context — so the choice
/// is between a rasteriser talking to a spooler and handing the platform a file it already knows how
/// to print. The second is the honest one here, because this repository writes PDF and PostScript
/// and both are checked against Ghostscript, so the page it produces is a page somebody else has
/// already agreed is a page.
/// </para>
/// <para>
/// The page is the picture's own size in PDF units, which is what
/// <see cref="Hawkynt.FileFormats.Images.FormatRegistry"/>'s writer produces, so the platform scales
/// it onto whatever paper is loaded. That is the same arrangement as printing a photograph from a
/// file manager, and it keeps this class out of the business of paper sizes, margins and
/// orientation — none of which it could ask the platform about anyway.
/// </para>
/// <para>
/// Nothing is stubbed. When no hand-off exists — no spooler client, no opener, and not Windows —
/// <see cref="Unavailable"/> says so and the command is off.
/// </para>
/// </remarks>
internal static class PrintService {

  /// <summary>Where the temporary PDFs go, one folder so a stale one can be found and removed.</summary>
  private const string _SPOOL_FOLDER = "crush-viewer-print";

  /// <summary>How long a handed-off PDF is kept before the next print sweeps it up.</summary>
  private static readonly TimeSpan _KeepFor = TimeSpan.FromDays(1);

  private static readonly FormatEntry? _Pdf = _FindPdfWriter();
  private static readonly PrintDestination[] _Destinations = _FindDestinations();

  /// <summary>The ways off this machine, best first; empty when there are none.</summary>
  internal static IReadOnlyList<PrintDestination> Destinations => _Destinations;

  /// <summary>Why printing is not offered, or <c>null</c> when it is.</summary>
  internal static string? Unavailable =>
    _Pdf == null
      ? "No registered writer claims '.pdf', and the print path works by writing one."
      : _Destinations.Length == 0
        ? "Nothing on this machine will take a PDF: no lp or lpr to spool it and no xdg-open to hand "
          + "it to an application. Printing needs one of the two."
        : null;

  /// <summary>
  /// Writes the picture as a PDF and hands it to the chosen destination.
  /// </summary>
  /// <returns><c>null</c> when the hand-off was accepted, or what went wrong.</returns>
  /// <remarks>
  /// A spooler is told and not waited for: <c>lp</c> returns once the job is queued, and a viewer
  /// keeps running for as long as the user looks at it. So a successful return means the platform
  /// took the file, which is as far as this side of the fence can see.
  /// </remarks>
  internal static string? Send(RawImage image, string stem, PrintDestination destination) {
    var (document, error) = WriteDocument(image, stem);
    if (document == null)
      return error;

    try {
      var started = destination.Program.Length == 0
        ? Process.Start(new ProcessStartInfo(document.FullName) {
          UseShellExecute = true,
          Verb = destination.Route == PrintRoute.Printer ? "print" : "open",
        })
        : Process.Start(new ProcessStartInfo(destination.Program) {
          ArgumentList = { document.FullName },
          UseShellExecute = false,
        });

      return started == null ? "The platform refused to open the PDF." : null;
    } catch (Exception ex) {
      return $"The hand-off failed: {ex.Message}";
    }
  }

  /// <summary>Writes the picture into the spool folder as the PDF the platform will be handed.</summary>
  /// <returns>The document, or <c>null</c> and what went wrong.</returns>
  /// <remarks>Split from the hand-off so the page can be checked without anything being printed.</remarks>
  internal static (FileInfo? Document, string? Error) WriteDocument(RawImage image, string stem) {
    ArgumentNullException.ThrowIfNull(image);
    if (_Pdf == null)
      return (null, Unavailable);

    FileInfo document;
    try {
      document = _Spool(stem);
    } catch (IOException ex) {
      return (null, $"The temporary PDF could not be created: {ex.Message}");
    }

    var written = ConversionService.Write(image, _Pdf, document);
    return written.Succeeded
      ? (document, null)
      : (null, $"The picture could not be written as a PDF: {written.Message}");
  }

  /// <summary>Names the next temporary PDF and clears out the ones nobody can still be printing.</summary>
  private static FileInfo _Spool(string stem) {
    var folder = new DirectoryInfo(Path.Combine(Path.GetTempPath(), _SPOOL_FOLDER));
    folder.Create();

    var cutoff = DateTime.UtcNow - _KeepFor;
    foreach (var stale in folder.GetFiles("*.pdf"))
      if (stale.LastWriteTimeUtc < cutoff)
        try {
          stale.Delete();
        } catch (IOException) {
          // A file still held open by a viewer somebody left running is not worth failing a print over.
        }

    var safe = string.Join("_", Path.GetFileNameWithoutExtension(stem).Split(Path.GetInvalidFileNameChars()));
    if (safe.Length == 0)
      safe = "picture";

    return new(Path.Combine(folder.FullName, $"{safe}-{DateTime.Now:yyyyMMdd-HHmmss}.pdf"));
  }

  private static FormatEntry? _FindPdfWriter() {
    foreach (var entry in FormatRegistry.SupportedWriteFormats)
      if (string.Equals(entry.PrimaryExtension, ".pdf", StringComparison.OrdinalIgnoreCase))
        return entry;

    return null;
  }

  /// <remarks>
  /// Windows needs no search: the shell's <c>print</c> and <c>open</c> verbs are the platform's own
  /// answer for a registered type, and a PDF is one wherever a reader is installed. Elsewhere the
  /// route has to be found, because a machine with no spooler client and no opener genuinely has
  /// nowhere to send a document, and that is a thing to say rather than to discover at the click.
  /// </remarks>
  private static PrintDestination[] _FindDestinations() {
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      return [
        new(PrintRoute.Printer, "The default printer", ""),
        new(PrintRoute.Viewer, "Whatever opens a PDF here", ""),
      ];

    var found = new List<PrintDestination>(2);
    if (_OnPath("lp") is { } lp)
      found.Add(new(PrintRoute.Printer, "The default printer", lp));
    else if (_OnPath("lpr") is { } lpr)
      found.Add(new(PrintRoute.Printer, "The default printer", lpr));

    var opener = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";
    if (_OnPath(opener) is { } open)
      found.Add(new(PrintRoute.Viewer, "Whatever opens a PDF here", open));

    return [.. found];
  }

  private static string? _OnPath(string program) {
    var path = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrEmpty(path))
      return null;

    foreach (var folder in path.Split(Path.PathSeparator)) {
      if (folder.Length == 0)
        continue;

      string candidate;
      try {
        candidate = Path.Combine(folder, program);
      } catch (ArgumentException) {
        // A PATH entry with characters no path can hold is not a folder to look in.
        continue;
      }

      if (File.Exists(candidate))
        return candidate;
    }

    return null;
  }
}
