using AlexIsland.App.Services;
using AlexIsland.Core.Shelf;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // SECCIÓN SHELF: ARCHIVOS Y TEXTOS A MANO
    // ==============================================================

    private const int ExpandedShelfHeight = 204;

    private readonly ShelfStore _shelfStore = new();
    private readonly List<ShelfItem> _shelfItems = [];
    private bool _isDragOverIsland;

    private async Task LoadShelfAsync()
    {
        var loaded = await _shelfStore.LoadAsync();
        if (_isDisposed) return;

        _shelfItems.Clear();
        _shelfItems.AddRange(loaded);
        RenderShelf();
    }

    private void TabShelfButton_Click(object sender, RoutedEventArgs e) => SwitchTab(IslandTab.Shelf);

    // ------------------------------------------------------------ Soltar sobre la isla

    private void IslandContainer_DragEnter(object sender, DragEventArgs e)
    {
        if (!CanAcceptDrop(e.DataView)) return;

        // Al arrastrar algo sobre el notch, se abre directamente el shelf.
        _isDragOverIsland = true;
        _collapseTimer.Stop();
        _isPointerInside = true;
        if (_activeTab != IslandTab.Shelf)
        {
            SwitchTab(IslandTab.Shelf);
        }
        else
        {
            ApplyIslandState();
        }
    }

    private void IslandContainer_DragOver(object sender, DragEventArgs e)
    {
        if (!CanAcceptDrop(e.DataView))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        // "Vincular": solo se guarda la ruta, el archivo no se mueve ni se copia.
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Link
            : DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Guardar en el shelf";
        e.DragUIOverride.IsGlyphVisible = true;
    }

    private void IslandContainer_DragLeave(object sender, DragEventArgs e)
    {
        if (!_isDragOverIsland) return;
        _isDragOverIsland = false;
        _isPointerInside = false;
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private async void IslandContainer_Drop(object sender, DragEventArgs e)
    {
        _isDragOverIsland = false;
        if (!CanAcceptDrop(e.DataView)) return;

        var deferral = e.GetDeferral();
        try
        {
            await AddFromDataViewAsync(e.DataView);
        }
        catch
        {
            ShelfSummary.Text = "No se pudo agregar lo que soltaste";
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static bool CanAcceptDrop(DataPackageView data)
        => data.Contains(StandardDataFormats.StorageItems) || data.Contains(StandardDataFormats.Text);

    private async Task<bool> AddFromDataViewAsync(DataPackageView data)
    {
        var changed = false;

        if (data.Contains(StandardDataFormats.StorageItems))
        {
            var items = await data.GetStorageItemsAsync();
            var paths = items
                .Where(i => !string.IsNullOrWhiteSpace(i.Path))
                .Select(i => (i.Path, i.IsOfType(StorageItemTypes.Folder)))
                .ToList();

            var added = ShelfRules.AddPaths(_shelfItems, paths);
            changed = added > 0;

            // Archivos que no tienen ruta en disco (por ejemplo, adjuntos virtuales) no se pueden guardar.
            if (paths.Count < items.Count)
            {
                ShelfSummary.Text = "Algunos elementos no tienen ubicación en disco y no se agregaron";
            }
        }
        else if (data.Contains(StandardDataFormats.Text))
        {
            changed = ShelfRules.AddText(_shelfItems, await data.GetTextAsync());
        }

        if (changed)
        {
            RenderShelf();
            await SaveShelfAsync();
        }
        else if (_shelfItems.Count >= ShelfRules.MaxItems)
        {
            ShelfSummary.Text = $"El shelf está lleno ({ShelfRules.MaxItems} elementos)";
        }

        return changed;
    }

    private async void ShelfPasteBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var added = await AddFromDataViewAsync(Clipboard.GetContent());
            if (!added) ShelfSummary.Text = "No hay archivos ni texto nuevo en el portapapeles";
        }
        catch
        {
            ShelfSummary.Text = "No se pudo leer el portapapeles";
        }
    }

    private async void ShelfClearBtn_Click(object sender, RoutedEventArgs e)
    {
        var selectedIds = ShelfGrid.SelectedItems
            .OfType<FrameworkElement>()
            .Select(el => el.Tag as string)
            .Where(id => id is not null)
            .ToHashSet();

        // Sin selección se vacía todo; con selección, solo lo marcado.
        _shelfItems.RemoveAll(i => selectedIds.Count == 0 || selectedIds.Contains(i.Id));
        RenderShelf();
        await SaveShelfAsync();
    }

    private async Task SaveShelfAsync()
    {
        try
        {
            await _shelfStore.SaveAsync(_shelfItems);
        }
        catch
        {
            ShelfSummary.Text = "No se pudo guardar el shelf";
        }
    }

    // ------------------------------------------------------------ Sacar arrastrando

    private void ShelfGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var selected = e.Items
            .OfType<FrameworkElement>()
            .Select(el => _shelfItems.FirstOrDefault(i => i.Id == el.Tag as string))
            .Where(i => i is not null)
            .Cast<ShelfItem>()
            .ToList();

        var files = selected.Where(i => i.Kind != ShelfItemKind.Text && i.Path is not null).ToList();
        var texts = selected.Where(i => i.Kind == ShelfItemKind.Text).Select(i => i.Text).ToList();

        if (files.Count == 0 && texts.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        e.Data.RequestedOperation = DataPackageOperation.Copy;

        if (texts.Count > 0)
        {
            e.Data.SetText(string.Join(Environment.NewLine, texts));
        }

        if (files.Count > 0)
        {
            // Los archivos se resuelven recién cuando el destino los pide.
            e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
            {
                var deferral = request.GetDeferral();
                try
                {
                    var storageItems = new List<IStorageItem>();
                    foreach (var item in files)
                    {
                        try
                        {
                            storageItems.Add(item.Kind == ShelfItemKind.Folder
                                ? await StorageFolder.GetFolderFromPathAsync(item.Path)
                                : await StorageFile.GetFileFromPathAsync(item.Path));
                        }
                        catch
                        {
                            // El archivo ya no existe o no hay permiso: se omite.
                        }
                    }
                    request.SetData(storageItems);
                }
                finally
                {
                    deferral.Complete();
                }
            });
        }
    }

    private void ShelfGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var count = ShelfGrid.SelectedItems.Count;
        ShelfClearLabel.Text = count > 0 ? $"Quitar ({count})" : "Limpiar";
    }

    // ------------------------------------------------------------ Acciones sobre un elemento

    /// <summary>
    /// Doble clic: los archivos y carpetas se muestran en el Explorador (nunca
    /// se ejecutan desde la isla); los textos se copian al portapapeles.
    /// </summary>
    private async void ShelfTile_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { Tag: string id }) return;
        var item = _shelfItems.FirstOrDefault(i => i.Id == id);
        if (item is null) return;

        if (item.Kind == ShelfItemKind.Text)
        {
            CopyShelfText(item);
            return;
        }

        await RevealInExplorerAsync(item);
    }

    private static async Task RevealInExplorerAsync(ShelfItem item)
    {
        if (!ShelfRules.IsAcceptablePath(item.Path)) return;

        try
        {
            if (item.Kind == ShelfItemKind.Folder)
            {
                await Windows.System.Launcher.LaunchFolderPathAsync(item.Path);
                return;
            }

            var folder = Path.GetDirectoryName(item.Path);
            if (folder is null) return;

            var options = new Windows.System.FolderLauncherOptions();
            try
            {
                options.ItemsToSelect.Add(await StorageFile.GetFileFromPathAsync(item.Path));
            }
            catch
            {
                // Si el archivo ya no existe se abre igual la carpeta.
            }
            await Windows.System.Launcher.LaunchFolderPathAsync(folder, options);
        }
        catch
        {
        }
    }

    private void CopyShelfText(ShelfItem item)
    {
        var package = new DataPackage();
        package.SetText(item.Text ?? string.Empty);
        Clipboard.SetContent(package);
        ShelfSummary.Text = "Texto copiado al portapapeles";
    }

    private async void RemoveShelfItem(ShelfItem item)
    {
        _shelfItems.Remove(item);
        RenderShelf();
        await SaveShelfAsync();
    }

    // ------------------------------------------------------------ Dibujo

    private void RenderShelf()
    {
        ShelfGrid.Items.Clear();

        foreach (var item in _shelfItems)
        {
            ShelfGrid.Items.Add(CreateShelfTile(item));
        }

        var count = _shelfItems.Count;
        ShelfEmptyHint.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShelfSummary.Text = count == 0
            ? "Arrastra archivos o texto a la isla"
            : $"{count} {(count == 1 ? "elemento" : "elementos")} · doble clic para abrir su carpeta";
        HeaderShelfCount.Text = count.ToString();
        RefreshTabBadges();
        ShelfClearLabel.Text = "Limpiar";
    }

    private FrameworkElement CreateShelfTile(ShelfItem item)
    {
        // En rutas de red (\\servidor\...) no se comprueba la existencia: si el
        // servidor no responde, la comprobación bloquearía la isla varios segundos.
        var isNetworkPath = item.Path?.StartsWith(@"\\", StringComparison.Ordinal) == true;
        var exists = item.Kind == ShelfItemKind.Text
            || isNetworkPath
            || (item.Kind == ShelfItemKind.Folder ? Directory.Exists(item.Path) : File.Exists(item.Path));

        var tile = new Border
        {
            Tag = item.Id,
            Width = 76,
            Height = 58,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 26, 29, 37)),
            Padding = new Thickness(5, 4, 5, 4),
            Opacity = exists ? 1.0 : 0.45,
            Child = new StackPanel
            {
                Spacing = 3,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new FontIcon
                    {
                        Glyph = GlyphFor(item),
                        FontSize = 16,
                        Foreground = new SolidColorBrush(ColorFor(item)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = item.DisplayName,
                        FontSize = 9.5,
                        Foreground = new SolidColorBrush(Colors.White),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxLines = 2,
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            },
        };

        var tooltip = item.Kind == ShelfItemKind.Text ? ShelfRules.Preview(item.Text, 200) : item.Path;
        if (!exists) tooltip += "\n(no encontrado)";
        ToolTipService.SetToolTip(tile, tooltip);

        tile.DoubleTapped += ShelfTile_DoubleTapped;
        tile.ContextFlyout = CreateShelfMenu(item);
        return tile;
    }

    private MenuFlyout CreateShelfMenu(ShelfItem item)
    {
        var menu = new MenuFlyout();

        if (item.Kind == ShelfItemKind.Text)
        {
            var copy = new MenuFlyoutItem { Text = "Copiar texto", Icon = new FontIcon { Glyph = "\uE8C8" } };
            copy.Click += (_, _) => CopyShelfText(item);
            menu.Items.Add(copy);
        }
        else
        {
            var reveal = new MenuFlyoutItem { Text = "Mostrar en la carpeta", Icon = new FontIcon { Glyph = "\uE838" } };
            reveal.Click += async (_, _) => await RevealInExplorerAsync(item);
            menu.Items.Add(reveal);

            var copyPath = new MenuFlyoutItem { Text = "Copiar ruta", Icon = new FontIcon { Glyph = "\uE8C8" } };
            copyPath.Click += (_, _) =>
            {
                var package = new DataPackage();
                package.SetText(item.Path ?? string.Empty);
                Clipboard.SetContent(package);
                ShelfSummary.Text = "Ruta copiada al portapapeles";
            };
            menu.Items.Add(copyPath);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var remove = new MenuFlyoutItem { Text = "Quitar del shelf", Icon = new FontIcon { Glyph = "\uE711" } };
        remove.Click += (_, _) => RemoveShelfItem(item);
        menu.Items.Add(remove);

        return menu;
    }

    private static string GlyphFor(ShelfItem item)
    {
        if (item.Kind == ShelfItemKind.Text) return "\uE8C1";
        if (item.Kind == ShelfItemKind.Folder) return "\uE8B7";

        return Path.GetExtension(item.Path ?? string.Empty).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" => "\uEB9F",
            ".mp4" or ".mov" or ".mkv" or ".avi" => "\uE714",
            ".mp3" or ".wav" or ".flac" or ".m4a" => "\uE8D6",
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => "\uF012",
            ".cs" or ".js" or ".ts" or ".py" or ".json" or ".xml" or ".ps1" or ".sh" or ".sql" or ".yml" or ".yaml" => "\uE943",
            ".log" or ".txt" or ".md" => "\uE8A5",
            ".exe" or ".msi" or ".bat" or ".cmd" => "\uE756",
            _ => "\uE7C3",
        };
    }

    private static Windows.UI.Color ColorFor(ShelfItem item) => item.Kind switch
    {
        ShelfItemKind.Text => ColorHelper.FromArgb(255, 171, 71, 188),
        ShelfItemKind.Folder => ColorHelper.FromArgb(255, 255, 202, 40),
        _ => ColorHelper.FromArgb(255, 66, 165, 245),
    };
}
