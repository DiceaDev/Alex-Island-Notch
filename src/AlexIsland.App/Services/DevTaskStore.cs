using System.Text;
using System.Text.Json;
using AlexIsland.Core.Models;

namespace AlexIsland.App.Services;

public sealed class DevTaskStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DevTaskStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlexIsland");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "dev_tasks.json");
    }

    public async Task<List<DevTask>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                return GetDefaultTasks();
            }

            await using var stream = File.OpenRead(_filePath);
            var items = await JsonSerializer.DeserializeAsync<List<DevTask>>(stream, JsonOptions, cancellationToken);
            return items ?? GetDefaultTasks();
        }
        catch
        {
            return GetDefaultTasks();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(IEnumerable<DevTask> tasks, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var tempFile = _filePath + ".tmp";
            await using (var stream = File.Create(tempFile))
            {
                await JsonSerializer.SerializeAsync(stream, tasks, JsonOptions, cancellationToken);
            }
            File.Move(tempFile, _filePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    public static string ToMarkdown(IEnumerable<DevTask> tasks)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### 📋 Alex Island - Dev Backlog ({DateTime.Now:yyyy-MM-dd})");
        sb.AppendLine();

        var taskList = tasks.ToList();
        if (taskList.Count == 0)
        {
            sb.AppendLine("_Sin tareas pendientes._");
            return sb.ToString();
        }

        foreach (var task in taskList)
        {
            var check = task.IsCompleted ? "[x]" : "[ ]";
            var tag = string.IsNullOrWhiteSpace(task.Category) ? "" : $"**[{task.Category}]** ";
            sb.AppendLine($"- {check} {tag}{task.Title}");
        }

        return sb.ToString();
    }

    private static List<DevTask> GetDefaultTasks()
    {
        return
        [
            new DevTask
            {
                Title = "Revisar backlog de programación",
                Category = "FEAT",
                IsCompleted = false
            },
            new DevTask
            {
                Title = "Optimizar consumo y rendimiento",
                Category = "REFACTOR",
                IsCompleted = true
            }
        ];
    }
}

