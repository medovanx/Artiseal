using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using Artiseal.Core;
using Microsoft.Win32;
using Sigstore;

namespace Artiseal.App;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new();

    // One verifier for the app's life: it fetches Sigstore's trust root once.
    private static readonly SigstoreVerifier Verifier = new();

    private readonly RecentRepos _recent = RecentRepos.Default();
    private string? _path;
    private string? _hash;

    public MainWindow(string? path)
    {
        InitializeComponent();
        RepoBox.ItemsSource = _recent.Load();
        RepoBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new System.Windows.Controls.TextChangedEventHandler((_, _) => UpdateCheckButton()));
        MenuToggle.IsChecked = ExplorerMenu.IsInstalled();

        if (path is not null && File.Exists(path))
        {
            _ = LoadFileAsync(path);
        }
    }

    private async Task LoadFileAsync(string path)
    {
        _path = path;
        _hash = null;
        ResultCard.Visibility = Visibility.Collapsed;
        FileName.Text = Path.GetFileName(path);
        FileHash.Text = "Working out the file's fingerprint…";
        UpdateCheckButton();

        _hash = await ProofChecker.HashAsync(path);
        FileHash.Text = "SHA-256 " + _hash;

        if (DownloadSource.GuessRepo(path) is { } guess)
        {
            RepoBox.Text = guess.ToString();
            RepoHint.Text = "Filled in from where you downloaded the file. Change it if that's wrong.";
        }
        else
        {
            RepoHint.Text = "Type owner/repo, like cli/cli, or paste the project's GitHub link.";
        }
        UpdateCheckButton();
        RepoBox.Focus();
    }

    private void UpdateCheckButton() =>
        CheckButton.IsEnabled = _hash is not null && RepoName.TryParse(RepoBox.Text, out _);

    private async void OnCheck(object sender, RoutedEventArgs e)
    {
        if (_path is null || !RepoName.TryParse(RepoBox.Text, out RepoName repo))
        {
            return;
        }
        CheckButton.IsEnabled = false;
        ShowWorking(repo);

        try
        {
            ProofChecker checker = new(new AttestationClient(Http, Environment.GetEnvironmentVariable("GITHUB_TOKEN")), Verifier);
            ProofReport report = await checker.CheckAsync(_path, repo);

            ShowReport(report);

            if (report.Verdict is Verdict.Verified or Verdict.NoProof or Verdict.Failed)
            {
                _recent.Remember(repo);
                RepoBox.ItemsSource = _recent.Load();
                RepoBox.Text = repo.ToString();
            }
        }
        catch (HttpRequestException)
        {
            ShowVerdict(repo.ToString(), "Couldn't reach GitHub", "Check your internet connection and try again.", "Warn");
        }
        finally
        {
            UpdateCheckButton();
        }
    }

    private void ShowWorking(RepoName repo)
    {
        ShowVerdict(repo.ToString(), "Checking…", "Asking GitHub for this file's build proof and checking its signature.", "Muted");
    }

    private void ShowReport(ProofReport report)
    {
        (string verdict, string tone) = report.Verdict switch
        {
            Verdict.Verified => ("Verified: built from this repo's code", "Good"),
            Verdict.NoProof => ("No build proof for this file", "Warn"),
            Verdict.Failed => ("Proof doesn't check out", "Bad"),
            _ => ("Couldn't check", "Warn"),
        };

        // The repo leads: a valid proof only means "built by this repo", which is only as good as the repo.
        ShowVerdict(report.Repo.ToString(), verdict, ExplainFor(report), tone);

        if (report.Facts is { } f)
        {
            FactWorkflow.Text = f.Workflow ?? "Unknown";
            FactRef.Text = ShortRef(f.GitRef);
            FactCommit.Text = f.Commit is { Length: >= 7 } c ? c[..7] : f.Commit ?? "Unknown";
            FactCommitLink.NavigateUri = f.Commit is null ? null : new Uri($"{f.Repository}/commit/{f.Commit}");
            FactTrigger.Text = f.Trigger ?? "Unknown";
            FactSigned.Text = f.SignedAt?.ToLocalTime().ToString("d MMMM yyyy, HH:mm") ?? "Unknown";
            FactRunLink.NavigateUri = f.RunUrl is null ? null : new Uri(f.RunUrl);
            FactsGrid.Visibility = Visibility.Visible;
        }
    }

    private static string ExplainFor(ProofReport report) => report.Verdict switch
    {
        Verdict.Verified => $"This exact file was built by GitHub Actions in {report.Repo}, and nothing has changed it since. Only trust it as much as you trust {report.Repo}.",
        Verdict.NoProof => $"{report.Message} The file may be fine, but nothing proves where it came from. Many projects don't publish build proofs yet.",
        _ => report.Message,
    };

    private void ShowVerdict(string repo, string verdict, string message, string tone)
    {
        ResultRepo.Text = repo;
        ResultVerdict.Text = verdict;
        ResultVerdict.Foreground = (Brush)FindResource(tone == "Muted" ? "Muted" : tone);
        ResultMessage.Text = message;
        FactsGrid.Visibility = Visibility.Collapsed;
        ResultCard.Visibility = Visibility.Visible;
    }

    private static string ShortRef(string? gitRef) => gitRef switch
    {
        null => "Unknown",
        _ when gitRef.StartsWith("refs/tags/", StringComparison.Ordinal) => "tag " + gitRef["refs/tags/".Length..],
        _ when gitRef.StartsWith("refs/heads/", StringComparison.Ordinal) => "branch " + gitRef["refs/heads/".Length..],
        _ => gitRef,
    };

    private void OnChooseFile(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new() { Title = "Choose a downloaded file" };

        if (dialog.ShowDialog(this) == true)
        {
            _ = LoadFileAsync(dialog.FileName);
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files && File.Exists(files[0]))
        {
            _ = LoadFileAsync(files[0]);
        }
    }

    private void OnRepoKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && CheckButton.IsEnabled)
        {
            OnCheck(sender, e);
        }
    }

    private void OnLink(object sender, RequestNavigateEventArgs e)
    {
        // Only GitHub links are ever set here; opened in the default browser.
        if (e.Uri is { Scheme: "https", Host: "github.com" })
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        e.Handled = true;
    }

    private void OnMenuToggle(object sender, RoutedEventArgs e)
    {
        if (MenuToggle.IsChecked == true)
        {
            ExplorerMenu.Install();
        }
        else
        {
            ExplorerMenu.Uninstall();
        }
    }
}
