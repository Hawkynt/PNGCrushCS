using Hawkynt.NativeForms;

namespace Crush.Viewer;

/// <summary>Says how the picture will be printed and asks where to send it.</summary>
/// <remarks>
/// The explanation is on the dialog rather than in a tooltip because the route is not the one a print
/// command usually takes, and somebody who expects a page-setup dialog should be told why they are
/// not getting one before anything is spooled.
/// </remarks>
internal sealed class PrintOptionsDialog : DialogForm {

  private readonly ComboBox _destination;

  internal PrintOptionsDialog(string picture) : base("Print picture") {
    this.AddNote(
      $"{picture} is written to a temporary PDF with this suite's own writer, one page the size of "
      + "the picture, and handed to the platform to put on paper.",
      52);

    this._destination = this.AddRow("Send it to", _Destinations());
    this.AddButtons("Print");
  }

  /// <summary>Where the user chose to send it.</summary>
  internal PrintDestination Destination => (PrintDestination)this._destination.SelectedItem!;

  private static ComboBox _Destinations() {
    var box = new ComboBox {
      DropDownStyle = ComboBoxStyle.DropDownList,
      DisplaySelector = o => o is PrintDestination destination ? destination.Text : "",
    };

    foreach (var destination in PrintService.Destinations)
      box.Items.Add(destination);

    box.SelectedIndex = 0;
    return box;
  }
}
