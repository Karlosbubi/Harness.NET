using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.VisualCapture;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
    [Fact]
    public void Role_catalog_keeps_compatible_models_from_every_provider_and_access_class()
    {
        GoalModelCandidate local = Candidate(
            "Ollama", "local-lead", ModelAccess.Local, [AgentRole.Lead]);
        GoalModelCandidate remote = Candidate(
            "OpenRouter", "remote-lead", ModelAccess.Remote, [AgentRole.Lead]);
        GoalModelCandidate incompatible = Candidate(
            "OpenRouter", "reviewer", ModelAccess.Remote, [AgentRole.Reviewer]);

        Assert.Equal(
            [local, remote],
            ModelSelectionCatalog.ForRole([local, remote, incompatible], AgentRole.Lead));
    }

}
