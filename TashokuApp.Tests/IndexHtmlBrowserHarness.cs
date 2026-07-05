using Microsoft.Playwright;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Tashoku.UnitTests;

public sealed class IndexHtmlBrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private string? _html;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            ExecutablePath = LocateBrowserExecutable(),
        });

        _html = LoadIndexHtmlWithoutBootstrap();
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }

    public async Task<IndexHtmlPageScope> CreatePageAsync()
    {
        if (_browser is null)
        {
            throw new InvalidOperationException("Browser is not initialized.");
        }

        if (_html is null)
        {
            throw new InvalidOperationException("HTML is not initialized.");
        }

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BypassCSP = true,
        });

        await context.AddInitScriptAsync(@"Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value: undefined });");
        await context.RouteAsync("**/*", async route =>
        {
            var url = route.Request.Url;
            if (url.StartsWith("http://127.0.0.1/", StringComparison.Ordinal))
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    Body = _html,
                    ContentType = "text/html"
                });
                return;
            }

            if (url.Contains("googletagmanager.com", StringComparison.OrdinalIgnoreCase) || url.EndsWith("/service-worker.js", StringComparison.OrdinalIgnoreCase))
            {
                await route.AbortAsync();
                return;
            }

            await route.ContinueAsync();
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync("http://127.0.0.1/tashoku-test", new PageGotoOptions { WaitUntil = WaitUntilState.Load });
        await page.EvaluateAsync("cacheDOM(); buildGrid(); buildPalette(); updateSubtitle();");

        return new IndexHtmlPageScope(context, page);
    }

    private static string LoadIndexHtmlWithoutBootstrap()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var path = Path.Combine(root, "TashokuApp.Web", "wwwroot", "index.html");
        var html = File.ReadAllText(path);
        const string marker = "        /* ── Wire controls ────────────────────────────────────────── */";

        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("Could not find the page bootstrap block.");
        }

        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException("Could not find the closing script tag for the bootstrap block.");
        }

        return html.Remove(start, end - start);
    }

    private static string LocateBrowserExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not locate an installed Edge or Chrome executable.");
    }
}

public sealed class IndexHtmlPageScope : IAsyncDisposable
{
    public IndexHtmlPageScope(IBrowserContext context, IPage page)
    {
        Context = context;
        Page = page;
    }

    public IBrowserContext Context { get; }
    public IPage Page { get; }

    public ValueTask DisposeAsync() => new(Context.CloseAsync());
}
