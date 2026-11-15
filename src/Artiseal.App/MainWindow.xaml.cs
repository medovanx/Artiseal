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

    // A repo found in the log is only a lead; each one gets the full check, and every verified one is shown.
    private const int MaxCandidates = 5;

    private readonly RecentRepos _recent = RecentRepos.Default();
    private string? _path;
    private string? _hash;
    private bool _busy;

    public MainWindow(string? path)
    {
        InitializeComponent();
        _ = Trust.WarmAsync();
        RepoBox.ItemsSource = _recent.Load();
        RepoBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new System.Windows.Controls.TextChangedEventHandler((_, _) => UpdateCheckButton()));
        UpdateMenuLink();

        if (path is not null && File.Exists(path))
        {
            _ = LoadFileAsync(path);
        }
    }

    private async Task LoadFileAsync(string path)
    {
        _path = path;
        _hash = null;
        FileName.Text = Path.GetFileName(path);
        FileDetails.Text = FormatSize(new FileInfo(path).Length);
        FileName.ToolTip = null;
        RepoBox.Text = string.Empty;
        RepoPanel.Visibility = Visibility.Collapsed;
        OtherRepoLink.Visibility = Visibility.Collapsed;
        ShowVerdict(Path.GetFileName(path), "Reading the file...", "Working out its fingerprint.", "Muted");

        _hash = await ProofChecker.HashAsync(path);
        FileName.ToolTip = "SHA-256 " + _hash;

        // Where it was downloaded from wins; otherwise ask the public log who signed this hash.
        RepoName? guess = DownloadSource.GuessRepo(path);

        if (guess is not null && await CheckAsync([guess]))
        {
            return;
        }

        ShowVerdict(guess?.ToString() ?? "Looking it up", "Finding who published this file...", "Searching Sigstore's public log.", "Muted");
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
            // Nobody else signed it either: show the download page's answer again.
            await CheckAsync([guess]);

            return;
        }
        if (found.Count == 0)
        {
            ShowVerdict("Publisher unknown", "No public build record", "Nothing ties this file to a GitHub repo. If you know the project, check it below.", "Warn");
            ShowRepoInput("Which project published it? Type owner/repo or paste its GitHub link.");

            return;
        }

        RepoBox.ItemsSource = found.Select(r => r.ToString()).Concat(_recent.Load()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await CheckAsync(found);
    }

    /// <summary>Checks each repo and shows the result; true when at least one verified.</summary>
    private async Task<bool> CheckAsync(IReadOnlyList<RepoName> repos)
    {
        if (_path is null)
        {
            return false;
        }
        ShowVerdict(repos[0].ToString(), "Checking...", "Fetching the build proof and checking its signature.", "Muted");

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
                    ". Make sure you meant to download from " + lead.Repo + ".";
                ResultOthers.Visibility = Visibility.Visible;
            }

            if (lead.Verdict is Verdict.Verified or Verdict.NoProof or Verdict.Failed)
            {
                _recent.Remember(lead.Repo);
            }
            OtherRepoLink.Visibility = RepoPanel.IsVisible ? Visibility.Collapsed : Visibility.Visible;

            return verified.Count > 0;
        }
        catch (HttpRequestException)
        {
            ShowVerdict(repos[0].ToString(), "Couldn't reach GitHub", "Check your internet connection and try again.", "Warn");

            return false;
        }
    }

    private void ShowReport(ProofReport report)
    {
        (string verdict, string tone) = report.Verdict switch
        {
            Verdict.Verified => ("Verified", "Good"),
            Verdict.NoProof => ("No build proof", "Warn"),
            Verdict.Failed => ("Proof doesn't check out", "Bad"),
            _ => ("Couldn't check", "Warn"),
        };

        // The repo leads: a valid proof only means "built by this repo", which is only as good as the repo.
        ShowVerdict(report.Repo.ToString(), verdict, ExplainFor(report), tone);

        if (report.Facts is { } f)
        {
            FactCommit.Text = f.Commit is { Length: >= 7 } c ? "commit " + c[..7] : "unknown commit";
            FactCommitLink.NavigateUri = f.Commit is null ? null : new Uri($"{f.Repository}/commit/{f.Commit}");
            FactRef.Text = ShortRef(f.GitRef);
            FactSigned.Text = f.SignedAt?.ToLocalTime().ToString("d MMMM yyyy, HH:mm") ?? "Unknown";
            FactRunLink.NavigateUri = f.RunUrl is null ? null : new Uri(f.RunUrl);
            FactsGrid.Visibility = Visibility.Visible;
        }
    }

    private static string ExplainFor(ProofReport report) => report.Verdict switch
    {
        Verdict.Verified => $"Built by GitHub Actions in {report.Repo} and unchanged since.",
        Verdict.NoProof => $"{report.Repo} published no build proof for this file.",
        _ => report.Message,
    };

    private void ShowVerdict(string repo, string verdict, string message, string tone)
    {
        ResultRepo.Text = repo;
        ResultVerdict.Text = verdict;
        ResultVerdict.Foreground = (Brush)FindResource(tone);
        ResultMessage.Text = message;
        ResultOthers.Visibility = Visibility.Collapsed;
        FactsGrid.Visibility = Visibility.Collapsed;
        ResultCard.Visibility = Visibility.Visible;

        // "Muted" is only ever a step in progress: spin, and keep Check from starting a second run.
        _busy = tone == "Muted";
        Spinner.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        CheckButton.Content = _busy ? "Checking..." : "Check";

        if (_busy)
        {
            OtherRepoLink.Visibility = Visibility.Collapsed;
        }
        UpdateCheckButton();
    }

    private void ShowRepoInput(string hint)
    {
        RepoHint.Text = hint;
        RepoPanel.Visibility = Visibility.Visible;
        OtherRepoLink.Visibility = Visibility.Collapsed;
        RepoBox.Focus();
    }

    private void UpdateCheckButton() =>
        CheckButton.IsEnabled = !_busy && _hash is not null && RepoName.TryParse(RepoBox.Text, out _);

    private async void OnCheck(object sender, RoutedEventArgs e)
    {
        if (_path is not null && RepoName.TryParse(RepoBox.Text, out RepoName repo))
        {
            await CheckAsync([repo]);
        }
    }

    private void OnShowRepoInput(object sender, RoutedEventArgs e) =>
        ShowRepoInput("Check this file against another repo. Type owner/repo or paste its GitHub link.");

    private static string ShortRef(string? gitRef) => gitRef switch
    {
        null => string.Empty,
        _ when gitRef.StartsWith("refs/tags/", StringComparison.Ordinal) => ", tag " + gitRef["refs/tags/".Length..],
        _ when gitRef.StartsWith("refs/heads/", StringComparison.Ordinal) => ", branch " + gitRef["refs/heads/".Length..],
        _ => ", " + gitRef,
    };

    // Rounds down to one decimal, the way Explorer shows sizes, so both agree.
    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{Math.Floor(bytes * 10.0 / (1L << 30)) / 10:0.0} GB",
        >= 1L << 20 => $"{Math.Floor(bytes * 10.0 / (1L << 20)) / 10:0.0} MB",
        >= 1L << 10 => $"{Math.Floor(bytes / 1024.0):0} KB",
        _ => $"{bytes} bytes",
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
        // Windows asks for admin rights here; the link then shows what is really installed.
        ExplorerMenu.Set(!ExplorerMenu.IsInstalled());
        UpdateMenuLink();
    }

    private void UpdateMenuLink() =>
        MenuLinkText.Text = ExplorerMenu.IsInstalled() ? "Remove from right-click menu" : "Add to right-click menu";
}
