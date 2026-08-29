using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Goals;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed partial class DocumentsHost
{
    internal async ValueTask NavigateToDebugAsync(string path, int line, GoalId? goalId)
    {
        await OpenAsync(path, goalId);
        SourceDocumentSession? target = sources.Values.FirstOrDefault(item =>
            item.View.GoalId == goalId && item.View.Path.Value == path);
        if (target is null) return;
        SetActive(target.Document);
        WorkbenchCodePosition position = new(Math.Max(0, line - 1), 0);
        target.Editor.SetCaretPosition(position);
        target.Editor.ScrollTo(position);
        target.Editor.Focus();
    }
}
