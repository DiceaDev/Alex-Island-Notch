using AlexIsland.App.Native;
using AlexIsland.App.Services;
using AlexIsland.Core.Models;
using AlexIsland.Core.Security;
using AlexIsland.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.Media.Control;
using WinRT.Interop;
using System.Globalization;

namespace AlexIsland.App;

public sealed partial class MainWindow : UserControl
{
    // ==============================================================
    // SECCIÓN DEV BACKLOG / TO-DO
    // ==============================================================

    private async Task LoadTasksAsync()
    {
        try
        {
            var loaded = await _taskStore.LoadAsync();
            _tasks.Clear();
            _tasks.AddRange(loaded);
            UpdateBacklogUI();
        }
        catch
        {
        }
    }

    private void UpdateBacklogUI()
    {
        var pendingCount = _tasks.Count(t => !t.IsCompleted);

        // Actualizar badges
        HeaderBacklogCount.Text = pendingCount.ToString();
        RefreshTabBadges();
        CompactTaskCount.Text = pendingCount.ToString();
        CompactTaskBadge.Visibility = pendingCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        BacklogSummaryText.Text = pendingCount == 1 ? "1 pendiente" : $"{pendingCount} pendientes";

        RenderTasksList();
        PopulatePomodoroTaskCombo();
    }

    private void RenderTasksList()
    {
        TasksItemsPanel.Children.Clear();

        if (_tasks.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "⚡ Sin tareas en el backlog. ¡Escribe una arriba!",
                FontSize = 10,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(160, 143, 169, 181)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 0)
            };
            TasksItemsPanel.Children.Add(emptyText);
            return;
        }

        // Mostrar pendientes primero, luego completadas
        var ordered = _tasks.OrderBy(t => t.IsCompleted).ThenByDescending(t => t.CreatedAt).ToList();

        foreach (var task in ordered)
        {
            var itemContainer = new Border
            {
                Background = new SolidColorBrush(ColorHelper.FromArgb(200, 10, 50, 73)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 4, 6, 4),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(35, 251, 243, 242)),
                BorderThickness = new Thickness(1)
            };

            var rowGrid = new Grid { ColumnSpacing = 6 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Checkbox interactivo
            var checkBtn = new Button
            {
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (task.IsCompleted)
            {
                checkBtn.Background = new SolidColorBrush(Accent(50));
                checkBtn.BorderBrush = new SolidColorBrush(AccentColor);
                checkBtn.Content = new FontIcon
                {
                    Glyph = "\uE73E",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(AccentColor)
                };
            }
            else
            {
                checkBtn.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 10, 50, 73));
                checkBtn.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 31, 86, 112));
                checkBtn.Content = null;
            }

            checkBtn.Click += (_, _) =>
            {
                task.IsCompleted = !task.IsCompleted;
                _ = _taskStore.SaveAsync(_tasks);
                UpdateBacklogUI();
            };
            Grid.SetColumn(checkBtn, 0);
            rowGrid.Children.Add(checkBtn);

            // Chip de categoría
            var (tagBg, tagFg) = GetCategoryBrushes(task.Category);
            var tagBadge = new Border
            {
                Background = tagBg,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = task.Category,
                    FontSize = 9,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = tagFg
                }
            };
            Grid.SetColumn(tagBadge, 1);
            rowGrid.Children.Add(tagBadge);

            // Título de la tarea
            var titleBlock = new TextBlock
            {
                Text = task.Title,
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                Foreground = task.IsCompleted
                    ? new SolidColorBrush(ColorHelper.FromArgb(140, 143, 169, 181))
                    : new SolidColorBrush(ColorHelper.FromArgb(255, 251, 243, 242))
            };

            if (task.IsCompleted)
            {
                titleBlock.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
            }

            Grid.SetColumn(titleBlock, 2);
            rowGrid.Children.Add(titleBlock);

            // Botón eliminar tarea
            var deleteBtn = new Button
            {
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Content = new FontIcon
                {
                    Glyph = "\uE711",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(ColorHelper.FromArgb(140, 143, 169, 181))
                }
            };
            deleteBtn.Click += (_, _) =>
            {
                _tasks.Remove(task);
                _ = _taskStore.SaveAsync(_tasks);
                UpdateBacklogUI();
            };
            Grid.SetColumn(deleteBtn, 3);
            rowGrid.Children.Add(deleteBtn);

            itemContainer.Child = rowGrid;
            TasksItemsPanel.Children.Add(itemContainer);
        }
    }

    private static (SolidColorBrush Bg, SolidColorBrush Fg) GetCategoryBrushes(string category)
    {
        return category switch
        {
            "FEAT" => (
                new SolidColorBrush(ColorHelper.FromArgb(40, 93, 179, 204)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 93, 179, 204))),
            "FIX" => (
                new SolidColorBrush(ColorHelper.FromArgb(40, 255, 127, 120)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 255, 127, 120))),
            "REFACTOR" => (
                new SolidColorBrush(ColorHelper.FromArgb(40, 255, 194, 92)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 255, 194, 92))),
            "DOCS" => (
                new SolidColorBrush(ColorHelper.FromArgb(40, 196, 143, 214)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 196, 143, 214))),
            _ => (
                new SolidColorBrush(ColorHelper.FromArgb(40, 69, 196, 176)),
                new SolidColorBrush(ColorHelper.FromArgb(255, 69, 196, 176)))
        };
    }

    private void TagCycleButton_Click(object sender, RoutedEventArgs e)
    {
        _currentTagIndex = (_currentTagIndex + 1) % AvailableTags.Length;
        var tag = AvailableTags[_currentTagIndex];
        CurrentTagLabel.Text = tag;

        var (_, fg) = GetCategoryBrushes(tag);
        CurrentTagLabel.Foreground = fg;
    }

    private void NewTaskInput_GotFocus(object sender, RoutedEventArgs e) =>
        NewTaskInputBorder.BorderBrush = new SolidColorBrush(AccentColor);

    private void NewTaskInput_LostFocus(object sender, RoutedEventArgs e) =>
        NewTaskInputBorder.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 31, 86, 112));

    private void NewTaskInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            AddTaskFromInput();
            e.Handled = true;
        }
    }

    private void AddTaskButton_Click(object sender, RoutedEventArgs e)
    {
        AddTaskFromInput();
    }

    private void AddTaskFromInput()
    {
        var rawText = NewTaskInput.Text;
        if (string.IsNullOrWhiteSpace(rawText)) return;

        var (parsedCat, cleanTitle) = DevTask.ParseInput(rawText);
        var finalCategory = parsedCat != "TODO" ? parsedCat : AvailableTags[_currentTagIndex];

        var task = new DevTask
        {
            Title = cleanTitle,
            Category = finalCategory,
            IsCompleted = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _tasks.Insert(0, task);
        NewTaskInput.Text = string.Empty;

        _ = _taskStore.SaveAsync(_tasks);
        UpdateBacklogUI();
    }

    private void CopyMarkdownButton_Click(object sender, RoutedEventArgs e)
    {
        var md = DevTaskStore.ToMarkdown(_tasks);
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(md);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);

            CopyMarkdownLabel.Text = "¡Copiado!";
            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(1.5);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                CopyMarkdownLabel.Text = "Copiar MD";
                timer.Stop();
            };
            timer.Start();
        }
        catch
        {
        }
    }

    private void ClearCompletedButton_Click(object sender, RoutedEventArgs e)
    {
        _tasks.RemoveAll(t => t.IsCompleted);
        _ = _taskStore.SaveAsync(_tasks);
        UpdateBacklogUI();
    }
}
