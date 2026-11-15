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
    private static readonly SigstoreVerifier Verifier = Trust.NewVerifier();

    private readonly RecentRepos _recent = RecentRepos.Default();
    private string? _path;
    private string? _hash;

    public MainWindow(string? path)
    {
        InitializeComponent();
        _ = Trust.WarmAsync();
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
        FileHash.Text = "Working out the file's fingerprint...";
        RepoBox.Text = string.Empty;
        UpdateCheckButton();

        _hash = await ProofChecker.HashAsync(path);
        FileHash.Text = "SHA-256 " + _hash;

        // Where it was downloaded from wins; otherwise ask the public log who signed this hash.
        RepoName? guess = DownloadSource.GuessRepo(path);

        if (guess is not null)
        {
            RepoBox.Text = guess.ToString();
            RepoHint.Text = "Filled in from where you downloaded the file. Change it if that's wrong.";

            if (await CheckAsync([guess]))
            {
                return;
            }
            // The download page's repo didn't build it; the public log may know who did.
        }

        if (guess is null)
        {
            ShowVerdict("Looking it up", "Finding who published this file...", "Searching Sigstore's public log for this file's fingerprint.", "Muted");
        }
        IReadOnlyList<RepoName> found = [];

        try
        {
            found = await new PublisherFinder(Http).FindAsync(_hash);
        }
        catch (HttpRequestException)
        {
            // Falls through to asking the person.
        }

        if (found.Count == 0 && guess is not null)
        {
            // The download page's answer stays on screen; nobody else signed this file either.
            return;
        }
        if (found.Count == 0)
        {
            ResultCard.Visibility = Visibility.Collapsed;
            RepoHint.Text = "No public build record names this file. If you know the project, type owner/repo, like cli/cli.";
            UpdateCheckButton();
            RepoBox.Focus();

            return;
        }

        RepoBox.ItemsSource = found.Select(r => r.ToString()).Concat(_recent.Load()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        RepoBox.Text = found[0].ToString();
        RepoHint.Text = found.Count == 1
            ? "Found in Sigstore's public log. You can type another repo to check against it instead."
            : $"{found.Count} repos have signed this file. Each one is checked below.";
        await CheckAsync(found);
    }

    private void UpdateCheckButton() =>
        CheckButton.IsEnabled = _hash is not null && RepoName.TryParse(RepoBox.Text, out _);

    private async void OnCheck(object sender, RoutedEventArgs e)
    {
        if (_path is not null && RepoName.TryParse(RepoBox.Text, out RepoName repo))
        {
            await CheckAsync([repo]);
        }
    }

    // A repo found in the log is only a lead; each one gets the full check, and every verified one is shown.
    private const int MaxCandidates = 5;

    /// <summary>Checks each repo and shows the result; true when at least one verified.</summary>
    private async Task<bool> CheckAsync(IReadOnlyList<RepoName> repos)
    {
        if (_path is null)
        {
            return false;
        }
        CheckButton.IsEnabled = false;
        ShowVerdict(repos[0].ToString(), "Checking...", "Asking GitHub for this file's build proof and checking its signature.", "Muted");

        try
        {
            ProofChecker checker = new(new AttestationClient(Http, Environment.GetEnvironmentVariable("GITHUB_TOKEN")), Verifier);
            List<ProofReport> reports = [];

            foreach (RepoName repo in repos.Take(MaxCandidates))
            {
                reports.Add(await checker.CheckAsync(_path, repo));
            }

            List<ProofReport> verified = [.. reports.Where(r => r.Verdict == Verdict.Verified)];
            ProofReport lead = verified.FirstOrDefault() ?? reports[0];
            ShowReport(lead);

            if (verified.Count > 1)
            {
                ResultOthers.Text = "Also signed by " + string.Join(", ", verified.Skip(1).Select(r => r.Repo)) +
                    ". The same file was built in more than one repo, so make sure you trust the one you meant to download from.";
                ResultOthers.Visibility = Visibility.Visible;
            }

            if (lead.Verdict is Verdict.Verified or Verdict.NoProof or Verdict.Failed)
            {
                _recent.Remember(lead.Repo);
                RepoBox.Text = lead.Repo.ToString();
            }

            return verified.Count > 0;
        }
        catch (HttpRequestException)
        {
            ShowVerdict(repos[0].ToString(), "Couldn't reach GitHub", "Check your internet connection and try again.", "Warn");

            return false;
        }
        finally
        {
            UpdateCheckButton();
        }
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
        ResultOthers.Visibility = Visibility.Collapsed;
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
        // Windows asks for admin rights here; if that's refused, the box goes back to what's really installed.
        ExplorerMenu.Set(MenuToggle.IsChecked == true);
        MenuToggle.IsChecked = ExplorerMenu.IsInstalled();
    }
}
