using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hawkynt.FileFormats.Images;
using Hawkynt.NativeForms;

namespace Crush.Viewer;

/// <summary>Asks which writer to use and where to put what it produces.</summary>
/// <remarks>
/// Converting one file, converting a folder and splitting a multi-page file all need the same three
/// answers, so they share this dialog and differ only in the sentence at the top.
/// </remarks>
internal sealed class ConvertOptionsDialog : DialogForm {

  private readonly ComboBox _format;
  private readonly TextBox _destination;
  private readonly CheckBox _overwrite;

  internal ConvertOptionsDialog(string title, string note, string acceptText, string destination, ImageFormat preselect)
    : base(title) {
    this.AddNote(note);

    var writable = FormatRegistry.SupportedWriteFormats.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    this._format = this.AddRow("Write as", new ComboBox {
      DropDownStyle = ComboBoxStyle.DropDownList,
      DisplaySelector = o => o is FormatEntry entry ? $"{entry.Name} ({entry.PrimaryExtension})" : "",
    });
    foreach (var entry in writable)
      this._format.Items.Add(entry);

    var preselected = Array.FindIndex(writable, e => e.Format == preselect);
    if (preselected < 0)
      preselected = Array.FindIndex(writable, e => e.Format == ImageFormat.Png);
    this._format.SelectedIndex = Math.Max(0, preselected);

    this._destination = this.AddRow("Into folder", new TextBox { Text = destination });

    var browse = this.AddWide(new Button { Text = "Choose folder…" });
    browse.Click += (_, _) => {
      var picker = new FolderBrowserDialog { Title = "Destination folder", SelectedPath = this._destination.Text };
      if (picker.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(picker.SelectedPath))
        this._destination.Text = picker.SelectedPath;
    };

    this._overwrite = this.AddWide(new CheckBox { Text = "Replace files that are already there" });
    this.AddButtons(acceptText);
  }

  /// <summary>The writer the user picked.</summary>
  internal FormatEntry Target => (FormatEntry)this._format.SelectedItem!;

  /// <summary>The folder the results go into.</summary>
  internal DirectoryInfo Destination => new(this._destination.Text.Trim());

  /// <summary>Whether an existing file of the same name may be replaced.</summary>
  internal bool Overwrite => this._overwrite.Checked;

  protected override bool Validate(out string error) {
    if (this._format.SelectedItem is not FormatEntry) {
      error = "Pick a format to write.";
      return false;
    }

    var path = this._destination.Text.Trim();
    if (path.Length == 0) {
      error = "Name a folder for the results.";
      return false;
    }

    try {
      _ = Path.GetFullPath(path);
    } catch (Exception ex) {
      error = $"That is not a usable path: {ex.Message}";
      return false;
    }

    error = "";
    return true;
  }

  /// <summary>Shows a report of what a conversion produced.</summary>
  internal static void ShowReport(Form owner, string title, IReadOnlyList<ConversionResult> results) {
    var failed = results.Count(r => !r.Succeeded);
    var summary = failed == 0
      ? $"{results.Count} file(s) written."
      : $"{results.Count - failed} of {results.Count} file(s) written; {failed} failed.";

    var dialog = new ReportDialog(title, summary, results);
    dialog.ShowDialog(owner);
  }

  private sealed class ReportDialog : DialogForm {

    internal ReportDialog(string title, string summary, IReadOnlyList<ConversionResult> results) : base(title) {
      this.AddNote(summary, 24);

      var list = this.AddWide(new ListView {
        View = ListViewView.Details,
        FullRowSelect = true,
        ShowColumnHeaders = true,
      }, 320);
      list.Columns.Add(new ColumnHeader("Source", 150));
      list.Columns.Add(new ColumnHeader("Result", 90));
      list.Columns.Add(new ColumnHeader("Detail", 250));

      foreach (var result in results)
        list.Items.Add(new ListViewItem(Path.GetFileName(result.Source.FullName), [
          result.Succeeded ? "written" : "failed",
          result.Succeeded ? Path.GetFileName(result.Target) : result.Message,
        ]));

      this.AddButtons("Close", withCancel: false);
    }
  }
}
