using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;
using Optimizer.Image;
using ImageRegistry = Hawkynt.FileFormats.Images.FormatRegistry;
using UiImage = Avalonia.Controls.Image;

namespace Crush.Viewer;

internal sealed class MainWindow : Window {

  private readonly ViewerLaunchOptions _launchOptions;
  private readonly ObservableCollection<BrowserFile> _browserFiles = [];
  private readonly Stack<RawImage> _undo = [];
  private readonly Stack<RawImage> _redo = [];
  private readonly UiImage _image = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
  private readonly ScrollViewer _viewport = new() {
    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
  };
  private readonly TextBlock _emptyState = new() {
    Text = "Open an image, choose a folder, or drop files here",
    HorizontalAlignment = HorizontalAlignment.Center,
    VerticalAlignment = VerticalAlignment.Center,
    FontSize = 22,
    Opacity = 0.55,
  };
  private readonly ListBox _browser = new();
  private readonly TextBox _folderBox = new() { IsReadOnly = true };
  private readonly TextBox _metadata = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
  private readonly TextBlock _formatStatus = new() { Text = "Ready" };
  private readonly TextBlock _dimensionStatus = new();
  private readonly TextBlock _fileStatus = new();
  private readonly TextBlock _positionStatus = new();
  private readonly TextBlock _zoomStatus = new() { Text = "100%" };
  private readonly Slider _zoomSlider = new() { Minimum = -4, Maximum = 4, Value = 0, Width = 180 };
  private readonly Border _browserPane = new();
  private readonly Border _metadataPane = new();
  private readonly DispatcherTimer _slideshowTimer = new();
  private readonly Button _slideshowButton;

  private CancellationTokenSource? _loadCts;
  private WriteableBitmap? _bitmap;
  private RawImage? _rawImage;
  private FileInfo? _currentFile;
  private FormatEntry? _currentEntry;
  private ImageFormat _currentFormat = ImageFormat.Unknown;
  private int _browserIndex = -1;
  private int _frameIndex;
  private int _frameCount = 1;
  private double _zoom = 1;
  private double _pixelAspect = 1;
  private bool _fitToWindow = true;
  private bool _dirty;
  private bool _suppressBrowserSelection;
  private bool _suppressZoomSlider;
  private bool _slideshowBusy;
  private DisplayFilter? _displayFilterOverride;
  private DisplayFilter _formatDisplayFilter;

  internal MainWindow(ViewerLaunchOptions launchOptions) {
    this._launchOptions = launchOptions;
    this.Title = "Crush Viewer";
    this.Width = launchOptions.ScreenshotPath != null ? 1280 : 1180;
    this.Height = launchOptions.ScreenshotPath != null ? 820 : 760;
    this.MinWidth = 760;
    this.MinHeight = 520;
    this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

    this._browser.ItemsSource = this._browserFiles;
    this._browser.SelectionChanged += this._OnBrowserSelectionChanged;
    this._zoomSlider.ValueChanged += (_, _) => {
      if (this._suppressZoomSlider) return;
      this._fitToWindow = false;
      this._SetZoom(Math.Pow(2, this._zoomSlider.Value));
    };
    this._viewport.PointerWheelChanged += this._OnPointerWheelChanged;
    this._viewport.SizeChanged += (_, _) => { if (this._fitToWindow) this._FitToWindow(); };

    this._slideshowTimer.Tick += async (_, _) => await this._OnSlideshowTickAsync();
    this._slideshowButton = this._Button("Slideshow", () => _ = this._ToggleSlideshowAsync());

    this.Content = this._BuildLayout();
    this.KeyDown += this._OnKeyDown;
    DragDrop.SetAllowDrop(this, true);
    DragDrop.AddDragOverHandler(this, this._OnDragOver);
    DragDrop.AddDropHandler(this, this._OnDrop);

    this.Opened += async (_, _) => await this._OnOpenedAsync();
    this.Closed += (_, _) => {
      this._loadCts?.Cancel();
      this._loadCts?.Dispose();
      this._bitmap?.Dispose();
    };
  }

  private Control _BuildLayout() {
    var root = new DockPanel();
    var menu = this._BuildMenu();
    DockPanel.SetDock(menu, Dock.Top);
    root.Children.Add(menu);
    var toolbar = this._BuildToolbar();
    DockPanel.SetDock(toolbar, Dock.Top);
    root.Children.Add(toolbar);
    var status = this._BuildStatusBar();
    DockPanel.SetDock(status, Dock.Bottom);
    root.Children.Add(status);

    var body = new Grid {
      ColumnDefinitions = new ColumnDefinitions {
        new ColumnDefinition { Width = new GridLength(245) },
        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
        new ColumnDefinition { Width = new GridLength(295) },
      },
    };

    this._browserPane.Child = this._BuildBrowserPane();
    this._browserPane.BorderThickness = new Thickness(0, 0, 1, 0);
    this._browserPane.BorderBrush = Brushes.DimGray;
    Grid.SetColumn(this._browserPane, 0);
    body.Children.Add(this._browserPane);

    var viewportGrid = new Grid();
    this._viewport.Content = this._image;
    viewportGrid.Children.Add(this._viewport);
    viewportGrid.Children.Add(this._emptyState);
    Grid.SetColumn(viewportGrid, 1);
    body.Children.Add(viewportGrid);

    this._metadataPane.Child = this._BuildMetadataPane();
    this._metadataPane.BorderThickness = new Thickness(1, 0, 0, 0);
    this._metadataPane.BorderBrush = Brushes.DimGray;
    Grid.SetColumn(this._metadataPane, 2);
    body.Children.Add(this._metadataPane);

    root.Children.Add(body);
    return root;
  }

  private Control _BuildMenu() {
    var file = new MenuItem {
      Header = "_File",
      ItemsSource = new object[] {
        this._MenuItem("_Open…\tCtrl+O", () => _ = this._OpenPickerAsync()),
        this._MenuItem("Open _folder…\tCtrl+Shift+O", () => _ = this._OpenFolderPickerAsync()),
        new Separator(),
        this._MenuItem("_Save\tCtrl+S", () => _ = this._SaveAsync()),
        this._MenuItem("Save _as…\tCtrl+Shift+S", () => _ = this._SaveAsAsync()),
        new Separator(),
        this._MenuItem("E_xit\tAlt+F4", this.Close),
      },
    };
    var edit = new MenuItem {
      Header = "_Edit",
      ItemsSource = new object[] {
        this._MenuItem("_Undo\tCtrl+Z", this._Undo),
        this._MenuItem("_Redo\tCtrl+Y", this._Redo),
        new Separator(),
        this._MenuItem("Rotate 90° clockwise", () => this._ApplyEdit(i => ImageTransformer.Rotate(i, RotateAngle.CW90))),
        this._MenuItem("Rotate 90° counter-clockwise", () => this._ApplyEdit(i => ImageTransformer.Rotate(i, RotateAngle.CW270))),
        this._MenuItem("Rotate 180°", () => this._ApplyEdit(i => ImageTransformer.Rotate(i, RotateAngle.CW180))),
        this._MenuItem("Flip horizontal", () => this._ApplyEdit(i => ImageTransformer.Flip(i, FlipDirection.Horizontal))),
        this._MenuItem("Flip vertical", () => this._ApplyEdit(i => ImageTransformer.Flip(i, FlipDirection.Vertical))),
        new Separator(),
        this._MenuItem("_Resize…\tCtrl+R", () => _ = this._ResizeAsync()),
        this._MenuItem("_Crop…\tCtrl+Shift+C", () => _ = this._CropAsync()),
        this._MenuItem("Reduce _colors…", () => _ = this._ReduceColorsAsync()),
        this._MenuItem("_Grayscale", () => this._ApplyEdit(_Grayscale)),
        this._MenuItem("_Invert", () => this._ApplyEdit(_Invert)),
      },
    };
    var view = new MenuItem {
      Header = "_View",
      ItemsSource = new object[] {
        this._MenuItem("Zoom _in", () => this._ZoomBy(Math.Sqrt(2))),
        this._MenuItem("Zoom _out", () => this._ZoomBy(1 / Math.Sqrt(2))),
        this._MenuItem("_Actual size\t1", this._ActualSize),
        this._MenuItem("_Fit to window\t0", () => { this._fitToWindow = true; this._FitToWindow(); }),
        new Separator(),
        this._MenuItem("Toggle file _browser\tF8", () => this._browserPane.IsVisible = !this._browserPane.IsVisible),
        this._MenuItem("Toggle _metadata\tF9", () => this._metadataPane.IsVisible = !this._metadataPane.IsVisible),
        this._MenuItem("_Fullscreen\tF11", this._ToggleFullscreen),
        new Separator(),
        this._MenuItem("Display filter: format default", () => this._SetDisplayFilter(null)),
        this._MenuItem("Display filter: off", () => this._SetDisplayFilter(DisplayFilter.None)),
        this._MenuItem("Display filter: NTSC composite", () => this._SetDisplayFilter(DisplayFilter.NtscComposite)),
        this._MenuItem("Display filter: NTSC S-Video", () => this._SetDisplayFilter(DisplayFilter.NtscSvideo)),
        this._MenuItem("Display filter: PAL", () => this._SetDisplayFilter(DisplayFilter.Pal)),
      },
    };
    var navigate = new MenuItem {
      Header = "_Navigate",
      ItemsSource = new object[] {
        this._MenuItem("_Previous file\tLeft", () => _ = this._NavigateSiblingAsync(-1, false)),
        this._MenuItem("_Next file\tRight", () => _ = this._NavigateSiblingAsync(1, false)),
        this._MenuItem("Previous frame/page", () => _ = this._NavigateFrameAsync(-1)),
        this._MenuItem("Next frame/page", () => _ = this._NavigateFrameAsync(1)),
        new Separator(),
        this._MenuItem("_Slideshow\tF5", () => _ = this._ToggleSlideshowAsync()),
      },
    };
    var help = new MenuItem {
      Header = "_Help",
      ItemsSource = new object[] {
        this._MenuItem("_About", () => _ = ViewerDialogs.ShowMessageAsync(this, "About Crush Viewer",
          "Crush Viewer\nCross-platform browser, viewer, converter and lightweight editor for PNGCrushCS.")),
      },
    };
    return new Menu { ItemsSource = new object[] { file, edit, view, navigate, help } };
  }

  private Control _BuildToolbar() {
    var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(6, 5) };
    panel.Children.Add(this._Button("Open", () => _ = this._OpenPickerAsync()));
    panel.Children.Add(this._Button("Folder", () => _ = this._OpenFolderPickerAsync()));
    panel.Children.Add(this._Button("Save as", () => _ = this._SaveAsAsync()));
    panel.Children.Add(this._Button("◀", () => _ = this._NavigateSiblingAsync(-1, false)));
    panel.Children.Add(this._Button("▶", () => _ = this._NavigateSiblingAsync(1, false)));
    panel.Children.Add(this._Button("−", () => this._ZoomBy(1 / Math.Sqrt(2))));
    panel.Children.Add(this._Button("1:1", this._ActualSize));
    panel.Children.Add(this._Button("Fit", () => { this._fitToWindow = true; this._FitToWindow(); }));
    panel.Children.Add(this._Button("+", () => this._ZoomBy(Math.Sqrt(2))));
    panel.Children.Add(this._Button("Rotate", () => this._ApplyEdit(i => ImageTransformer.Rotate(i, RotateAngle.CW90))));
    panel.Children.Add(this._Button("Resize", () => _ = this._ResizeAsync()));
    panel.Children.Add(this._slideshowButton);
    return panel;
  }

  private Control _BuildBrowserPane() {
    var panel = new DockPanel { Margin = new Thickness(8) };
    var title = new TextBlock { Text = "Folder browser", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
    DockPanel.SetDock(title, Dock.Top);
    panel.Children.Add(title);
    this._folderBox.Margin = new Thickness(0, 0, 0, 6);
    DockPanel.SetDock(this._folderBox, Dock.Top);
    panel.Children.Add(this._folderBox);
    panel.Children.Add(this._browser);
    return panel;
  }

  private Control _BuildMetadataPane() {
    var panel = new DockPanel { Margin = new Thickness(8) };
    var title = new TextBlock { Text = "Information", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
    DockPanel.SetDock(title, Dock.Top);
    panel.Children.Add(title);
    panel.Children.Add(this._metadata);
    return panel;
  }

  private Control _BuildStatusBar() {
    var grid = new Grid {
      Margin = new Thickness(8, 4),
      ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto"),
    };
    var controls = new Control[] { this._formatStatus, this._dimensionStatus, this._fileStatus, this._positionStatus, this._zoomSlider, this._zoomStatus };
    for (var i = 0; i < controls.Length; ++i) {
      controls[i].Margin = new Thickness(6, 0);
      Grid.SetColumn(controls[i], i);
      grid.Children.Add(controls[i]);
    }
    return new Border { BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Brushes.DimGray, Child = grid };
  }

  private MenuItem _MenuItem(string header, Action action) {
    var item = new MenuItem { Header = header };
    item.Click += (_, _) => action();
    return item;
  }

  private Button _Button(string text, Action action) {
    var button = new Button { Content = text, MinWidth = 42, Padding = new Thickness(9, 4) };
    button.Click += (_, _) => action();
    return button;
  }

  private async Task _OnOpenedAsync() {
    if (this._launchOptions.InitialPath is { } path) {
      if (Directory.Exists(path)) this._OpenFolder(new DirectoryInfo(path));
      else if (File.Exists(path)) await this._OpenFileAsync(new FileInfo(path), true);
    }

    if (this._launchOptions.ScreenshotPath != null) {
      await Task.Delay(500);
      this._CaptureScreenshot(this._launchOptions.ScreenshotPath);
      this.Close();
      return;
    }
    if (this._launchOptions.SmokeTest) {
      await Task.Delay(250);
      this.Close();
    }
  }

  private async Task _OpenPickerAsync() {
    var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
      Title = "Open image",
      AllowMultiple = false,
      FileTypeFilter = [_AllReadableType()],
    });
    if (files.Count > 0 && File.Exists(files[0].Path.LocalPath))
      await this._OpenFileAsync(new FileInfo(files[0].Path.LocalPath), true);
  }

  private async Task _OpenFolderPickerAsync() {
    var folders = await this.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open image folder", AllowMultiple = false });
    if (folders.Count > 0 && Directory.Exists(folders[0].Path.LocalPath))
      this._OpenFolder(new DirectoryInfo(folders[0].Path.LocalPath));
  }

  private void _OpenFolder(DirectoryInfo folder) {
    this._RefreshBrowser(folder, this._currentFile);
    if (this._browserFiles.Count > 0 && this._currentFile == null)
      _ = this._OpenBrowserIndexAsync(0);
  }

  private void _RefreshBrowser(DirectoryInfo folder, FileInfo? select) {
    var readable = ImageRegistry.SupportedReadFormats.SelectMany(e => e.AllExtensions).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var files = folder.EnumerateFiles()
      .Where(f => readable.Contains(f.Extension))
      .OrderBy(f => f.Name, NaturalStringComparer.Instance)
      .ToArray();
    this._suppressBrowserSelection = true;
    this._browserFiles.Clear();
    foreach (var file in files) this._browserFiles.Add(new(file));
    this._folderBox.Text = folder.FullName;
    this._browserIndex = select == null ? -1 : Array.FindIndex(files, f => string.Equals(f.FullName, select.FullName, StringComparison.OrdinalIgnoreCase));
    this._browser.SelectedIndex = this._browserIndex;
    this._suppressBrowserSelection = false;
    this._UpdateTitleAndStatus();
  }

  private async void _OnBrowserSelectionChanged(object? sender, SelectionChangedEventArgs e) {
    if (this._suppressBrowserSelection || this._browser.SelectedIndex < 0) return;
    await this._OpenBrowserIndexAsync(this._browser.SelectedIndex);
  }

  private async Task _OpenBrowserIndexAsync(int index) {
    if ((uint)index >= (uint)this._browserFiles.Count) return;
    await this._OpenFileAsync(this._browserFiles[index].File, false);
  }

  private async Task _OpenFileAsync(FileInfo file, bool refreshBrowser) {
    this._loadCts?.Cancel();
    this._loadCts?.Dispose();
    this._loadCts = new CancellationTokenSource();
    var ct = this._loadCts.Token;
    this._formatStatus.Text = $"Loading {file.Name}…";

    try {
      var result = await Task.Run(() => {
        ct.ThrowIfCancellationRequested();
        var format = ImageRegistry.DetectFromFile(file);
        var entry = ImageRegistry.GetEntry(format);
        var raw = ImageRegistry.Read(file);
        if (raw == null) return LoadResult.Empty(format, entry);
        var count = entry?.GetImageCount?.Invoke(file) ?? 1;
        return new LoadResult(format, entry, raw, Math.Max(1, count));
      }, ct);
      if (ct.IsCancellationRequested) return;
      if (result.RawImage == null) {
        await ViewerDialogs.ShowMessageAsync(this, "Open image", $"Could not decode {file.Name} ({result.Format}).");
        return;
      }

      this._currentFile = file;
      this._currentFormat = result.Format;
      this._currentEntry = result.Entry;
      this._rawImage = result.RawImage;
      this._frameCount = result.FrameCount;
      this._frameIndex = 0;
      this._dirty = false;
      this._undo.Clear();
      this._redo.Clear();
      this._PickVideoMode();
      this._RenderImage();
      if (refreshBrowser || this._browserFiles.Count == 0 || this._browserFiles.All(x => !string.Equals(x.File.FullName, file.FullName, StringComparison.OrdinalIgnoreCase))) {
        if (file.Directory != null) this._RefreshBrowser(file.Directory, file);
      } else {
        this._browserIndex = this._browserFiles.ToList().FindIndex(x => string.Equals(x.File.FullName, file.FullName, StringComparison.OrdinalIgnoreCase));
        this._suppressBrowserSelection = true;
        this._browser.SelectedIndex = this._browserIndex;
        this._suppressBrowserSelection = false;
      }
      this._UpdateMetadata();
      this._UpdateTitleAndStatus();
    } catch (OperationCanceledException) {
    } catch (Exception ex) {
      await ViewerDialogs.ShowMessageAsync(this, "Open image", ex.Message);
    }
  }

  private void _PickVideoMode() {
    this._pixelAspect = 1;
    this._formatDisplayFilter = DisplayFilter.None;
    if (this._rawImage == null || this._currentEntry?.VideoModes is not { Length: > 0 } modes) return;
    var mode = modes.FirstOrDefault(m => m.MatchesDimensions(this._rawImage.Width, this._rawImage.Height)) ?? modes[0];
    this._pixelAspect = mode.PixelAspectRatio?.Ratio ?? 1;
    this._formatDisplayFilter = mode.DisplayFilter;
  }

  private void _RenderImage() {
    if (this._rawImage == null) {
      this._image.Source = null;
      this._emptyState.IsVisible = true;
      return;
    }
    var filtered = DisplayFilterPipeline.Apply(this._rawImage, this._displayFilterOverride ?? this._formatDisplayFilter);
    var next = AvaloniaBitmapBridge.ToBitmap(filtered);
    var old = this._bitmap;
    this._bitmap = next;
    this._image.Source = next;
    this._emptyState.IsVisible = false;
    old?.Dispose();
    if (this._fitToWindow) this._FitToWindow(); else this._ApplyZoom();
  }

  private void _FitToWindow() {
    if (this._rawImage == null) return;
    var width = Math.Max(1, this._viewport.Bounds.Width - 20);
    var height = Math.Max(1, this._viewport.Bounds.Height - 20);
    var displayWidth = this._rawImage.Width * this._pixelAspect;
    this._SetZoom(Math.Min(width / displayWidth, height / this._rawImage.Height), updateSlider: true);
  }

  private void _ActualSize() { this._fitToWindow = false; this._SetZoom(1); }
  private void _ZoomBy(double factor) { this._fitToWindow = false; this._SetZoom(this._zoom * factor); }

  private void _SetZoom(double zoom, bool updateSlider = true) {
    this._zoom = Math.Clamp(zoom, 1.0 / 64, 64);
    this._ApplyZoom();
    if (updateSlider) {
      this._suppressZoomSlider = true;
      this._zoomSlider.Value = Math.Clamp(Math.Log2(this._zoom), this._zoomSlider.Minimum, this._zoomSlider.Maximum);
      this._suppressZoomSlider = false;
    }
  }

  private void _ApplyZoom() {
    if (this._rawImage == null) return;
    this._image.Width = Math.Max(1, this._rawImage.Width * this._pixelAspect * this._zoom);
    this._image.Height = Math.Max(1, this._rawImage.Height * this._zoom);
    this._zoomStatus.Text = $"{this._zoom * 100:0.#}%";
  }

  private void _OnPointerWheelChanged(object? sender, PointerWheelEventArgs e) {
    if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
    this._ZoomBy(e.Delta.Y > 0 ? Math.Sqrt(2) : 1 / Math.Sqrt(2));
    e.Handled = true;
  }

  private async Task _NavigateSiblingAsync(int delta, bool wrap) {
    if (this._browserFiles.Count == 0) return;
    var index = this._browserIndex < 0 ? 0 : this._browserIndex + delta;
    if (wrap) index = (index % this._browserFiles.Count + this._browserFiles.Count) % this._browserFiles.Count;
    else index = Math.Clamp(index, 0, this._browserFiles.Count - 1);
    if (index != this._browserIndex) await this._OpenBrowserIndexAsync(index);
  }

  private async Task _NavigateFrameAsync(int delta) {
    if (this._currentEntry?.LoadRawImageAtIndex == null || this._currentFile == null || this._frameCount < 2) return;
    var index = Math.Clamp(this._frameIndex + delta, 0, this._frameCount - 1);
    if (index == this._frameIndex) return;
    try {
      var raw = await Task.Run(() => this._currentEntry.LoadRawImageAtIndex(this._currentFile, index));
      if (raw == null) return;
      this._rawImage = raw;
      this._frameIndex = index;
      this._dirty = false;
      this._undo.Clear();
      this._redo.Clear();
      this._PickVideoMode();
      this._RenderImage();
      this._UpdateMetadata();
      this._UpdateTitleAndStatus();
    } catch (Exception ex) {
      await ViewerDialogs.ShowMessageAsync(this, "Navigate frame", ex.Message);
    }
  }

  private void _ApplyEdit(Func<RawImage, RawImage> transform) {
    if (this._rawImage == null) return;
    this._undo.Push(this._rawImage);
    this._redo.Clear();
    this._rawImage = transform(this._rawImage);
    this._dirty = true;
    this._RenderImage();
    this._UpdateMetadata();
    this._UpdateTitleAndStatus();
  }

  private void _Undo() {
    if (this._rawImage == null || this._undo.Count == 0) return;
    this._redo.Push(this._rawImage);
    this._rawImage = this._undo.Pop();
    this._dirty = true;
    this._RenderImage();
    this._UpdateMetadata();
    this._UpdateTitleAndStatus();
  }

  private void _Redo() {
    if (this._rawImage == null || this._redo.Count == 0) return;
    this._undo.Push(this._rawImage);
    this._rawImage = this._redo.Pop();
    this._dirty = true;
    this._RenderImage();
    this._UpdateMetadata();
    this._UpdateTitleAndStatus();
  }

  private async Task _ResizeAsync() {
    if (this._rawImage == null) return;
    var request = await ViewerDialogs.ShowResizeAsync(this, this._rawImage.Width, this._rawImage.Height);
    if (request == null) return;
    var r = request.Value;
    this._ApplyEdit(i => ImageTransformer.Resize(i, r.Width, r.Height, r.Mode, r.Interpolation));
  }

  private async Task _CropAsync() {
    if (this._rawImage == null) return;
    var region = await ViewerDialogs.ShowCropAsync(this, this._rawImage.Width, this._rawImage.Height);
    if (region != null) this._ApplyEdit(i => ImageTransformer.Crop(i, region.Value));
  }

  private async Task _ReduceColorsAsync() {
    if (this._rawImage == null) return;
    var colors = await ViewerDialogs.ShowPaletteSizeAsync(this, this._rawImage.IsIndexed ? Math.Clamp(this._rawImage.PaletteCount, 2, 256) : 256);
    if (colors != null) this._ApplyEdit(i => BitmapConverter.QuantizeRawImage(i, colors.Value));
  }

  private static RawImage _Grayscale(RawImage source) {
    var data = (byte[])source.ToBgra32().Clone();
    for (var i = 0; i < data.Length; i += 4) {
      var luma = (byte)((data[i + 2] * 77 + data[i + 1] * 150 + data[i] * 29) >> 8);
      data[i] = data[i + 1] = data[i + 2] = luma;
    }
    return new() { Width = source.Width, Height = source.Height, Format = FileFormat.Core.PixelFormat.Bgra32, PixelData = data, Metadata = source.Metadata, ColorInfo = source.ColorInfo };
  }

  private static RawImage _Invert(RawImage source) {
    var data = (byte[])source.ToBgra32().Clone();
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)(255 - data[i]);
      data[i + 1] = (byte)(255 - data[i + 1]);
      data[i + 2] = (byte)(255 - data[i + 2]);
    }
    return new() { Width = source.Width, Height = source.Height, Format = FileFormat.Core.PixelFormat.Bgra32, PixelData = data, Metadata = source.Metadata, ColorInfo = source.ColorInfo };
  }

  private void _SetDisplayFilter(DisplayFilter? filter) {
    this._displayFilterOverride = filter;
    this._RenderImage();
    this._UpdateMetadata();
  }

  private async Task _SaveAsync() {
    if (this._rawImage == null) return;
    if (!this._dirty || this._currentFile == null || this._currentEntry?.SupportsWrite != true) {
      await this._SaveAsAsync();
      return;
    }
    await this._WriteAsync(this._currentFile.FullName, this._currentEntry);
  }

  private async Task _SaveAsAsync() {
    if (this._rawImage == null) return;
    var writable = ImageRegistry.SupportedWriteFormats.OrderBy(e => e.Name).ToArray();
    var types = writable.Select(e => new FilePickerFileType(e.Name) { Patterns = e.AllExtensions.Select(x => $"*{x}").ToArray() }).ToArray();
    var target = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
      Title = "Save image as",
      SuggestedFileName = Path.GetFileNameWithoutExtension(this._currentFile?.Name ?? "image") + (this._currentEntry?.PrimaryExtension ?? ".png"),
      FileTypeChoices = types,
    });
    if (target == null) return;
    var path = target.Path.LocalPath;
    var extension = Path.GetExtension(path);
    var entry = writable.FirstOrDefault(e => e.AllExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    if (entry == null) {
      await ViewerDialogs.ShowMessageAsync(this, "Save image", $"No writer is registered for extension '{extension}'.");
      return;
    }
    await this._WriteAsync(path, entry);
  }

  private async Task _WriteAsync(string path, FormatEntry entry) {
    if (this._rawImage == null) return;
    try {
      var file = new FileInfo(path);
      if (!ImageRegistry.Write(this._rawImage, entry.Format, file)) throw new InvalidOperationException($"{entry.Name} refused the image.");
      this._currentFile = file;
      this._currentFormat = entry.Format;
      this._currentEntry = entry;
      this._dirty = false;
      this._UpdateTitleAndStatus();
      this._UpdateMetadata();
      if (file.Directory != null) this._RefreshBrowser(file.Directory, file);
    } catch (Exception ex) {
      await ViewerDialogs.ShowMessageAsync(this, "Save image", ex.Message);
    }
  }

  private async Task _ToggleSlideshowAsync() {
    if (this._slideshowTimer.IsEnabled) { this._StopSlideshow(); return; }
    if (this._browserFiles.Count < 2) return;
    var seconds = await ViewerDialogs.ShowSlideshowIntervalAsync(this, 3);
    if (seconds == null) return;
    this._slideshowTimer.Interval = TimeSpan.FromSeconds(seconds.Value);
    this._slideshowTimer.Start();
    this._slideshowButton.Content = "Stop show";
  }

  private void _StopSlideshow() { this._slideshowTimer.Stop(); this._slideshowButton.Content = "Slideshow"; }

  private async Task _OnSlideshowTickAsync() {
    if (this._slideshowBusy) return;
    this._slideshowBusy = true;
    try { await this._NavigateSiblingAsync(1, true); }
    finally { this._slideshowBusy = false; }
  }

  private void _UpdateMetadata() {
    if (this._rawImage == null || this._currentFile == null || this._currentEntry == null) { this._metadata.Text = "No image loaded."; return; }
    var image = this._rawImage;
    var md = image.Metadata;
    var sb = new StringBuilder();
    sb.AppendLine($"File: {this._currentFile.Name}");
    sb.AppendLine($"Path: {this._currentFile.FullName}");
    sb.AppendLine($"Format: {this._currentEntry.Name} ({this._currentFormat})");
    sb.AppendLine($"Size: {_FormatSize(this._currentFile.Exists ? this._currentFile.Length : 0)}");
    sb.AppendLine($"Dimensions: {image.Width} × {image.Height}");
    sb.AppendLine($"Pixel format: {image.Format}");
    sb.AppendLine($"Alpha: {(image.HasAlpha ? "yes" : "no")}");
    sb.AppendLine($"Indexed: {(image.IsIndexed ? $"yes ({image.PaletteCount} colors)" : "no")}");
    sb.AppendLine($"Frames/pages: {this._frameCount}");
    sb.AppendLine($"Writable: {(this._currentEntry.SupportsWrite ? "yes" : "no")}");
    sb.AppendLine($"Pixel aspect: {this._pixelAspect:0.###}:1");
    sb.AppendLine($"Display filter: {this._displayFilterOverride?.ToString() ?? $"format default ({this._formatDisplayFilter})"}");
    if (md != null && !md.IsEmpty) {
      sb.AppendLine();
      sb.AppendLine("Metadata");
      if (md.DpiX != null || md.DpiY != null) sb.AppendLine($"DPI: {md.DpiX?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"} × {md.DpiY?.ToString("0.##", CultureInfo.InvariantCulture) ?? "?"}");
      if (md.IccProfile != null) sb.AppendLine($"ICC: {md.IccProfileName ?? "embedded profile"} ({_FormatSize(md.IccProfile.LongLength)})");
      if (md.Exif != null) sb.AppendLine("EXIF: present");
      if (md.Iptc != null) sb.AppendLine("IPTC: present");
      if (md.XmpPacket != null) sb.AppendLine($"XMP: {_FormatSize(md.XmpPacket.LongLength)}");
      foreach (var text in md.TextEntries.Take(32)) sb.AppendLine($"{(string.IsNullOrEmpty(text.Keyword) ? "Comment" : text.Keyword)}: {text.Text}");
      if (md.TextEntries.Count > 32) sb.AppendLine($"… {md.TextEntries.Count - 32} more text entries");
    }
    this._metadata.Text = sb.ToString();
  }

  private void _UpdateTitleAndStatus() {
    if (this._currentFile == null || this._rawImage == null) { this.Title = "Crush Viewer"; this._positionStatus.Text = ""; return; }
    this.Title = $"{(this._dirty ? "* " : "")}{this._currentFile.Name} — Crush Viewer";
    this._formatStatus.Text = this._currentEntry?.Name ?? this._currentFormat.ToString();
    this._dimensionStatus.Text = $"{this._rawImage.Width} × {this._rawImage.Height}";
    this._fileStatus.Text = this._currentFile.Exists ? _FormatSize(this._currentFile.Length) : "";
    this._positionStatus.Text = this._frameCount > 1
      ? $"file {this._browserIndex + 1}/{this._browserFiles.Count} · frame {this._frameIndex + 1}/{this._frameCount}"
      : $"file {this._browserIndex + 1}/{this._browserFiles.Count}";
  }

  private void _ToggleFullscreen() => this.WindowState = this.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

  private async void _OnKeyDown(object? sender, KeyEventArgs e) {
    var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
    var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
    if (ctrl && e.Key == Key.O) { if (shift) await this._OpenFolderPickerAsync(); else await this._OpenPickerAsync(); e.Handled = true; return; }
    if (ctrl && e.Key == Key.S) { if (shift) await this._SaveAsAsync(); else await this._SaveAsync(); e.Handled = true; return; }
    if (ctrl && e.Key == Key.Z) { this._Undo(); e.Handled = true; return; }
    if (ctrl && e.Key == Key.Y) { this._Redo(); e.Handled = true; return; }
    if (ctrl && e.Key == Key.R) { await this._ResizeAsync(); e.Handled = true; return; }
    if (ctrl && shift && e.Key == Key.C) { await this._CropAsync(); e.Handled = true; return; }
    switch (e.Key) {
      case Key.Left: await this._NavigateSiblingAsync(-1, false); e.Handled = true; break;
      case Key.Right: await this._NavigateSiblingAsync(1, false); e.Handled = true; break;
      case Key.Home: await this._OpenBrowserIndexAsync(0); e.Handled = true; break;
      case Key.End: await this._OpenBrowserIndexAsync(this._browserFiles.Count - 1); e.Handled = true; break;
      case Key.Add: this._ZoomBy(Math.Sqrt(2)); e.Handled = true; break;
      case Key.Subtract: this._ZoomBy(1 / Math.Sqrt(2)); e.Handled = true; break;
      case Key.D0: this._fitToWindow = true; this._FitToWindow(); e.Handled = true; break;
      case Key.D1: this._ActualSize(); e.Handled = true; break;
      case Key.F5: await this._ToggleSlideshowAsync(); e.Handled = true; break;
      case Key.F8: this._browserPane.IsVisible = !this._browserPane.IsVisible; e.Handled = true; break;
      case Key.F9: this._metadataPane.IsVisible = !this._metadataPane.IsVisible; e.Handled = true; break;
      case Key.F11: this._ToggleFullscreen(); e.Handled = true; break;
      case Key.Escape when this.WindowState == WindowState.FullScreen: this.WindowState = WindowState.Normal; e.Handled = true; break;
    }
  }

  private void _OnDragOver(object? sender, DragEventArgs e)
    => e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

  private async void _OnDrop(object? sender, DragEventArgs e) {
    var first = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
    if (first == null) return;
    var path = first.Path.LocalPath;
    if (Directory.Exists(path)) this._OpenFolder(new DirectoryInfo(path));
    else if (File.Exists(path)) await this._OpenFileAsync(new FileInfo(path), true);
  }

  private void _CaptureScreenshot(string path) {
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(this.Bounds.Width)), Math.Max(1, (int)Math.Ceiling(this.Bounds.Height)));
    using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
    bitmap.Render(this);
    bitmap.Save(path);
  }

  private static FilePickerFileType _AllReadableType() => new("Supported images") {
    Patterns = ImageRegistry.SupportedReadFormats.SelectMany(e => e.AllExtensions).Distinct(StringComparer.OrdinalIgnoreCase).Select(e => $"*{e}").OrderBy(e => e).ToArray(),
  };

  private static string _FormatSize(long bytes) {
    string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
    var value = (double)Math.Max(0, bytes);
    var unit = 0;
    while (value >= 1024 && unit < units.Length - 1) { value /= 1024; ++unit; }
    return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
  }

  private readonly record struct LoadResult(ImageFormat Format, FormatEntry? Entry, RawImage? RawImage, int FrameCount) {
    internal static LoadResult Empty(ImageFormat format, FormatEntry? entry) => new(format, entry, null, 1);
  }

  private sealed record BrowserFile(FileInfo File) { public override string ToString() => this.File.Name; }

  private sealed class NaturalStringComparer : IComparer<string> {
    internal static readonly NaturalStringComparer Instance = new();
    public int Compare(string? x, string? y) {
      if (ReferenceEquals(x, y)) return 0;
      if (x == null) return -1;
      if (y == null) return 1;
      var ix = 0; var iy = 0;
      while (ix < x.Length && iy < y.Length) {
        if (char.IsDigit(x[ix]) && char.IsDigit(y[iy])) {
          long nx = 0; long ny = 0;
          while (ix < x.Length && char.IsDigit(x[ix])) nx = Math.Min(long.MaxValue / 10, nx) * 10 + (x[ix++] - '0');
          while (iy < y.Length && char.IsDigit(y[iy])) ny = Math.Min(long.MaxValue / 10, ny) * 10 + (y[iy++] - '0');
          var c = nx.CompareTo(ny); if (c != 0) return c; continue;
        }
        var cc = char.ToUpperInvariant(x[ix++]).CompareTo(char.ToUpperInvariant(y[iy++]));
        if (cc != 0) return cc;
      }
      return x.Length.CompareTo(y.Length);
    }
  }
}
