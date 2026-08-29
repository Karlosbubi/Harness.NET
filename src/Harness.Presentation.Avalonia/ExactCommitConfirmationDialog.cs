using System.Globalization;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia;

internal sealed class ExactCommitConfirmationDialog : Window
{
    internal ExactCommitConfirmationDialog(GoalCommitApprovalView approval, bool resuming)
    {
        Title = resuming ? "Resume approved commit" : "Approve exact commit";
        Width = 720;
        Height = 460;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close(false);
        Button commit = new()
        {
            Content = resuming ? "Revalidate and resume commit" : "Approve and commit",
        };
        commit.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = resuming
                        ? "Resume the already-approved exact commit?"
                        : "Approve this exact fingerprint and create the local commit?",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = $"Branch: {approval.Branch.Value}\n" +
                           $"Expected HEAD: {approval.ExpectedHead.Value}\n" +
                           $"Complete diff SHA-256: {approval.DiffHash.Value}\n" +
                           $"Changed files: {approval.ChangedFileCount.Value}\n" +
                           $"Author: {approval.AuthorName.Value} <{approval.AuthorEmail.Value}>\n\n" +
                           approval.CommitMessage.Value,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "Harness.NET revalidates the branch, HEAD, and complete diff immediately " +
                           "before committing. It does not merge, rebase, cherry-pick, push, or use " +
                           "the network.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, commit },
                },
            },
        };
    }
}

