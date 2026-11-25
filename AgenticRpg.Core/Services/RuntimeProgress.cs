using AgenticRpg.Core.Models;
namespace AgenticRpg.Core.Services;

/// <summary>Decouples tool feedback from the coordinator's agent dependency graph.</summary>
public sealed class RuntimeProgress
{
    private Func<string, ProcessingPhase, string?, Task>? _handler;
    public void Bind(Func<string, ProcessingPhase, string?, Task> handler) => _handler = handler;
    public Task ReportAsync(string scopeId, ProcessingPhase phase, string? detail = null) =>
        _handler?.Invoke(scopeId, phase, detail) ?? Task.CompletedTask;
}
