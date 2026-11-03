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
        try { return await JsonFilePersistence.LoadAsync(_filePath, static () => new List<DevTask>(), JsonOptions, cancellationToken); }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(IEnumerable<DevTask> tasks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var snapshot = tasks.ToList();
        var json = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        await _lock.WaitAsync(cancellationToken);
        try { await JsonFilePersistence.SaveAsync(_filePath, json, cancellationToken); }
        finally { _lock.Release(); }
    }

    public static string ToMarkdown(IEnumerable<DevTask> tasks)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### 📋 Alex Island - Dev Backlog ({DateTime.Now:yyyy-MM-dd})");
        sb.AppendLine();
        var taskList = tasks.ToList();
        if (taskList.Count == 0) { sb.AppendLine("_Sin tareas pendientes._"); return sb.ToString(); }
        foreach (var task in taskList)
        {
            var check = task.IsCompleted ? "[x]" : "[ ]";
            var tag = string.IsNullOrWhiteSpace(task.Category) ? "" : $"**[{task.Category}]** ";
            sb.AppendLine($"- {check} {tag}{task.Title}");
        }
        return sb.ToString();
    }
}