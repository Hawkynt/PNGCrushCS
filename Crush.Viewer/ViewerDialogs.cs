using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FileFormat.Core;
using Optimizer.Image;

namespace Crush.Viewer;

internal static class ViewerDialogs {

  internal static async Task ShowMessageAsync(Window owner, string title, string message) {
    var dialog = _CreateDialog(title, 520, 220);
    var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90 };
    ok.Click += (_, _) => dialog.Close();
    dialog.Content = new StackPanel {
      Margin = new Thickness(18),
      Spacing = 16,
      Children = {
        new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
        ok,
      },
    };
    await dialog.ShowDialog(owner);
  }

  internal static async Task<ResizeRequest?> ShowResizeAsync(Window owner, int width, int height) {
    var dialog = _CreateDialog("Resize image", 460, 390);
    var widthBox = new TextBox { Text = width.ToString(CultureInfo.InvariantCulture) };
    var heightBox = new TextBox { Text = height.ToString(CultureInfo.InvariantCulture) };
    var keepAspect = new CheckBox { Content = "Preserve aspect ratio", IsChecked = true };
    var mode = new ComboBox {
      ItemsSource = new[] { ResizeMode.Stretch, ResizeMode.Fit, ResizeMode.Fill },
      SelectedItem = ResizeMode.Stretch,
    };
    var interpolation = new ComboBox {
      ItemsSource = Enum.GetValues<InterpolationHint>(),
      SelectedItem = InterpolationHint.Bicubic,
    };
    var error = new TextBlock();
    var ratio = width / (double)Math.Max(1, height);
    var changing = false;

    widthBox.TextChanged += (_, _) => {
      if (changing || keepAspect.IsChecked != true || !int.TryParse(widthBox.Text, out var w) || w < 1)
        return;
      changing = true;
      heightBox.Text = Math.Max(1, (int)Math.Round(w / ratio)).ToString(CultureInfo.InvariantCulture);
      changing = false;
    };
    heightBox.TextChanged += (_, _) => {
      if (changing || keepAspect.IsChecked != true || !int.TryParse(heightBox.Text, out var h) || h < 1)
        return;
      changing = true;
      widthBox.Text = Math.Max(1, (int)Math.Round(h * ratio)).ToString(CultureInfo.InvariantCulture);
      changing = false;
    };

    ResizeRequest? result = null;
    var cancel = new Button { Content = "Cancel", MinWidth = 90 };
    cancel.Click += (_, _) => dialog.Close();
    var ok = new Button { Content = "Resize", MinWidth = 90 };
    ok.Click += (_, _) => {
      if (!int.TryParse(widthBox.Text, out var w) || !int.TryParse(heightBox.Text, out var h) || w < 1 || h < 1) {
        error.Text = "Width and height must be positive integers.";
        return;
      }
      result = new(w, h, (ResizeMode)(mode.SelectedItem ?? ResizeMode.Stretch),
        (InterpolationHint)(interpolation.SelectedItem ?? InterpolationHint.Bicubic));
      dialog.Close();
    };

    dialog.Content = _Form(
      ("Width", widthBox),
      ("Height", heightBox),
      ("", keepAspect),
      ("Mode", mode),
      ("Interpolation", interpolation),
      ("", error),
      ("", _Buttons(cancel, ok))
    );
    await dialog.ShowDialog(owner);
    return result;
  }

  internal static async Task<PixelRect?> ShowCropAsync(Window owner, int width, int height) {
    var dialog = _CreateDialog("Crop image", 460, 420);
    var x = new TextBox { Text = "0" };
    var y = new TextBox { Text = "0" };
    var w = new TextBox { Text = width.ToString(CultureInfo.InvariantCulture) };
    var h = new TextBox { Text = height.ToString(CultureInfo.InvariantCulture) };
    var error = new TextBlock();
    PixelRect? result = null;

    var cancel = new Button { Content = "Cancel", MinWidth = 90 };
    cancel.Click += (_, _) => dialog.Close();
    var ok = new Button { Content = "Crop", MinWidth = 90 };
    ok.Click += (_, _) => {
      if (!int.TryParse(x.Text, out var px) || !int.TryParse(y.Text, out var py)
          || !int.TryParse(w.Text, out var pw) || !int.TryParse(h.Text, out var ph)
          || pw < 1 || ph < 1) {
        error.Text = "Coordinates and dimensions must be integers; width/height must be positive.";
        return;
      }
      result = new(px, py, pw, ph);
      dialog.Close();
    };

    dialog.Content = _Form(
      ("X", x),
      ("Y", y),
      ("Width", w),
      ("Height", h),
      ("", new TextBlock { Text = $"Source: {width} × {height}. Values outside the source are clamped." }),
      ("", error),
      ("", _Buttons(cancel, ok))
    );
    await dialog.ShowDialog(owner);
    return result;
  }

  internal static async Task<int?> ShowPaletteSizeAsync(Window owner, int current = 256) {
    var dialog = _CreateDialog("Reduce colors", 430, 260);
    var colors = new TextBox { Text = Math.Clamp(current, 2, 256).ToString(CultureInfo.InvariantCulture) };
    var error = new TextBlock();
    int? result = null;
    var cancel = new Button { Content = "Cancel", MinWidth = 90 };
    cancel.Click += (_, _) => dialog.Close();
    var ok = new Button { Content = "Reduce", MinWidth = 90 };
    ok.Click += (_, _) => {
      if (!int.TryParse(colors.Text, out var value) || value is < 2 or > 256) {
        error.Text = "Palette size must be between 2 and 256.";
        return;
      }
      result = value;
      dialog.Close();
    };
    dialog.Content = _Form(
      ("Colors", colors),
      ("", new TextBlock { Text = "Uses the managed Median Cut + Floyd-Steinberg pipeline." }),
      ("", error),
      ("", _Buttons(cancel, ok))
    );
    await dialog.ShowDialog(owner);
    return result;
  }

  internal static async Task<double?> ShowSlideshowIntervalAsync(Window owner, double currentSeconds) {
    var dialog = _CreateDialog("Slideshow", 430, 240);
    var seconds = new TextBox { Text = currentSeconds.ToString("0.##", CultureInfo.InvariantCulture) };
    var error = new TextBlock();
    double? result = null;
    var cancel = new Button { Content = "Cancel", MinWidth = 90 };
    cancel.Click += (_, _) => dialog.Close();
    var ok = new Button { Content = "Start", MinWidth = 90 };
    ok.Click += (_, _) => {
      if (!double.TryParse(seconds.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0.2 || value > 3600) {
        error.Text = "Interval must be between 0.2 and 3600 seconds.";
        return;
      }
      result = value;
      dialog.Close();
    };
    dialog.Content = _Form(
      ("Seconds", seconds),
      ("", new TextBlock { Text = "The slideshow loops through supported images in the current folder." }),
      ("", error),
      ("", _Buttons(cancel, ok))
    );
    await dialog.ShowDialog(owner);
    return result;
  }

  private static Window _CreateDialog(string title, double width, double height) => new() {
    Title = title,
    Width = width,
    Height = height,
    CanResize = false,
    WindowStartupLocation = WindowStartupLocation.CenterOwner,
  };

  private static Control _Form(params (string Label, Control Control)[] rows) {
    var grid = new Grid {
      Margin = new Thickness(18),
      ColumnDefinitions = new ColumnDefinitions {
        new ColumnDefinition { Width = new GridLength(120) },
        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
      },
      RowDefinitions = new RowDefinitions(),
    };
    for (var i = 0; i < rows.Length; ++i)
      grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

    for (var i = 0; i < rows.Length; ++i) {
      var (label, control) = rows[i];
      if (!string.IsNullOrEmpty(label)) {
        var text = new TextBlock {
          Text = label,
          VerticalAlignment = VerticalAlignment.Center,
          Margin = new Thickness(0, 8, 12, 8),
        };
        Grid.SetRow(text, i);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);
      }
      control.Margin = new Thickness(0, 5, 0, 5);
      Grid.SetRow(control, i);
      Grid.SetColumn(control, string.IsNullOrEmpty(label) ? 0 : 1);
      if (string.IsNullOrEmpty(label))
        Grid.SetColumnSpan(control, 2);
      grid.Children.Add(control);
    }

    return grid;
  }

  private static Control _Buttons(params Button[] buttons) {
    var panel = new StackPanel {
      Orientation = Orientation.Horizontal,
      HorizontalAlignment = HorizontalAlignment.Right,
      Spacing = 8,
    };
    foreach (var button in buttons)
      panel.Children.Add(button);
    return panel;
  }
}

internal readonly record struct ResizeRequest(int Width, int Height, ResizeMode Mode, InterpolationHint Interpolation);
