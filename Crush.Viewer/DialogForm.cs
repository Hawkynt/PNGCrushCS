using System;
using System.Drawing;
using Hawkynt.NativeForms;

namespace Crush.Viewer;

/// <summary>A labelled column of inputs above an accept/cancel pair.</summary>
/// <remarks>
/// The toolkit lays out by rectangle, so a dialog either carries a layout panel per field or counts
/// rows itself. Counting rows is a dozen lines and keeps every dialog in this application the same
/// width, with its labels on the same column.
/// </remarks>
internal abstract class DialogForm : Form {

  private const int _MARGIN = 14;
  private const int _LABEL_WIDTH = 168;
  private const int _FIELD_WIDTH = 320;
  private const int _GAP = 8;
  private const int _ROW_HEIGHT = 28;

  /// <summary>Total client width every dialog shares.</summary>
  protected const int ClientWidth = _MARGIN * 2 + _LABEL_WIDTH + _GAP + _FIELD_WIDTH;

  private int _top = _MARGIN;

  protected DialogForm(string title) {
    this.Text = title;
    this.FormBorderStyle = FormBorderStyle.FixedDialog;
    this.StartPosition = FormStartPosition.CenterParent;
    this.MinimizeBox = false;
    this.MaximizeBox = false;
  }

  /// <summary>Adds a labelled input on its own row.</summary>
  protected T AddRow<T>(string label, T control, int height = _ROW_HEIGHT) where T : Control {
    this.Controls.Add(new Label {
      Text = label,
      Bounds = new(_MARGIN, this._top + 4, _LABEL_WIDTH, _ROW_HEIGHT - 6),
      TextAlign = Hawkynt.NativeForms.Drawing.ContentAlignment.TopLeft,
    });
    control.Bounds = new(_MARGIN + _LABEL_WIDTH + _GAP, this._top, _FIELD_WIDTH, height);
    this.Controls.Add(control);
    this._top += height + _GAP;
    return control;
  }

  /// <summary>Adds a control across the whole width, without a label.</summary>
  protected T AddWide<T>(T control, int height = _ROW_HEIGHT) where T : Control {
    control.Bounds = new(_MARGIN, this._top, ClientWidth - _MARGIN * 2, height);
    this.Controls.Add(control);
    this._top += height + _GAP;
    return control;
  }

  /// <summary>Adds an explanatory paragraph across the whole width.</summary>
  protected Label AddNote(string text, int height = 40)
    => this.AddWide(new Label { Text = text, TextAlign = Hawkynt.NativeForms.Drawing.ContentAlignment.TopLeft }, height);

  /// <summary>Closes the rows off with the buttons and sizes the window around everything.</summary>
  /// <remarks>
  /// The accept button deliberately carries no <see cref="Button.DialogResult"/>. A button that has
  /// one reports it to the form after its click handler has run, and the form closes on any result
  /// other than <see cref="DialogResult.None"/> — so a handler that refuses invalid input would show
  /// its complaint and then watch the dialog close as accepted anyway. Setting the result here, only
  /// once the input has passed, is what makes the refusal mean anything. Cancel needs no such care.
  /// </remarks>
  protected void AddButtons(string acceptText, bool withCancel = true) {
    const int buttonWidth = 104;
    const int buttonHeight = 30;
    var top = this._top + 6;
    var right = ClientWidth - _MARGIN;

    var accept = new Button {
      Text = acceptText,
      Bounds = new(right - buttonWidth, top, buttonWidth, buttonHeight),
    };
    accept.Click += (_, _) => {
      if (!this.Validate(out var error)) {
        MessageBox.Show(this, error, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
      }

      this.DialogResult = DialogResult.OK;
    };
    this.Controls.Add(accept);
    this.AcceptButton = accept;

    if (withCancel) {
      var cancel = new Button {
        Text = "Cancel",
        Bounds = new(right - buttonWidth * 2 - _GAP, top, buttonWidth, buttonHeight),
        DialogResult = DialogResult.Cancel,
      };
      this.Controls.Add(cancel);
      this.CancelButton = cancel;
    }

    this.ClientSize = new(ClientWidth, top + buttonHeight + _MARGIN);
  }

  /// <summary>Refuses the accept button and says why; returning false keeps the dialog open.</summary>
  protected virtual bool Validate(out string error) {
    error = "";
    return true;
  }
}
