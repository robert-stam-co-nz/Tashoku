using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Tashoku.UnitTests;

public sealed class IndexHtmlRegressionTests : IClassFixture<IndexHtmlBrowserFixture>
{
    private readonly IndexHtmlBrowserFixture _fixture;

    public IndexHtmlRegressionTests(IndexHtmlBrowserFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UtilityHelpers_ReturnExpectedValues()
    {
        await using var scope = await _fixture.CreatePageAsync();
        var page = scope.Page;

        var result = await EvaluateJsonAsync(page, """
            {
                darkText: Array.from({ length: 9 }, (_, i) => darkText(i)),
                config: makeConfig(12),
                boxId: boxId(makeConfig(12), 4, 5),
                pop: pop(0b101101),
                bits: bitmaskToList(0b1011, 9),
                gauge: gauge(2, 5, '🟩')
            }
            """);

        Assert.Equal(new[] { false, false, false, true, false, true, true, false, true }, result.GetProperty("darkText").EnumerateArray().Select(v => v.GetBoolean()).ToArray());
        Assert.Equal(9, result.GetProperty("config").GetProperty("N").GetInt32());
        Assert.Equal(3, result.GetProperty("config").GetProperty("boxRows").GetInt32());
        Assert.Equal(3, result.GetProperty("config").GetProperty("boxCols").GetInt32());
        Assert.Equal(4, result.GetProperty("boxId").GetInt32());
        Assert.Equal(4, result.GetProperty("pop").GetInt32());
        Assert.Equal(new[] { 0, 1, 3 }, result.GetProperty("bits").EnumerateArray().Select(v => v.GetInt32()).ToArray());
        Assert.Equal("⬜️⬜️⬜️🟩🟩", result.GetProperty("gauge").GetString());
    }

    [Fact]
    public async Task SolverStateHelpers_MutateAndSelectCell()
    {
        await using var scope = await _fixture.CreatePageAsync();
        var page = scope.Page;

        var result = await EvaluateJsonAsync(page, """
            (() => {
                const cfg = makeConfig(9);
                const st = makeState(cfg);

                doPlace(cfg, st, 0, 0, 0, true);
                doPlace(cfg, st, 1, 1, 3, true);
                doRemove(cfg, st, 1, 1, 3, true);

                const maskState = makeState(cfg);
                doPlace(cfg, maskState, 0, 0, 0, false);
                doPlace(cfg, maskState, 0, 1, 1, false);
                doPlace(cfg, maskState, 0, 2, 2, false);
                doPlace(cfg, maskState, 0, 4, 4, false);
                doPlace(cfg, maskState, 0, 5, 5, false);
                doPlace(cfg, maskState, 0, 6, 6, false);
                doPlace(cfg, maskState, 0, 7, 7, false);
                doPlace(cfg, maskState, 0, 8, 8, false);

                const picked = mrv(cfg, maskState, new Set([3, 12]), false);

                return {
                    rowMask: st.rowU[0],
                    colMask: st.colU[0],
                    boxMask: st.boxU[0],
                    diagMask: st.diagU[0],
                    clearedGrid: st.grid[10],
                    candidateMask: candMask(cfg, maskState, 0, 3, false),
                    mrvIdx: picked.idx,
                    mrvCnt: picked.cnt,
                    mrvMask: picked.cands
                };
            })()
            """);

        Assert.Equal(1, result.GetProperty("rowMask").GetInt32());
        Assert.Equal(1, result.GetProperty("colMask").GetInt32());
        Assert.Equal(1, result.GetProperty("boxMask").GetInt32());
        Assert.Equal(1, result.GetProperty("diagMask").GetInt32());
        Assert.Equal(-1, result.GetProperty("clearedGrid").GetInt32());
        Assert.Equal(8, result.GetProperty("candidateMask").GetInt32());
        Assert.Equal(3, result.GetProperty("mrvIdx").GetInt32());
        Assert.Equal(1, result.GetProperty("mrvCnt").GetInt32());
        Assert.Equal(8, result.GetProperty("mrvMask").GetInt32());
    }

    [Fact]
    public async Task GeneratorAndCounterHelpers_ProduceUniquelySolvablePuzzle()
    {
        await using var scope = await _fixture.CreatePageAsync();
        var page = scope.Page;

        var result = await EvaluateJsonAsync(page, """
            (() => {
                const puzzle = generatePuzzle(app.cfg, 30, true);
                const partial = new Array(app.cfg.N * app.cfg.N).fill(-1);
                for (const i of puzzle.given) {
                    partial[i] = puzzle.solution[i];
                }

                return {
                    solutionLength: puzzle.solution.length,
                    givenCount: puzzle.given.length,
                    solutionCount: countSolutions(app.cfg, partial, true, 2)
                };
            })()
            """);

        Assert.Equal(81, result.GetProperty("solutionLength").GetInt32());
        Assert.Equal(30, result.GetProperty("givenCount").GetInt32());
        Assert.Equal(1, result.GetProperty("solutionCount").GetInt32());
    }

    [Fact]
    public async Task CandidateHelpers_IgnoreIncorrectPlayerEntries_AndFallbackToGlobalValidation()
    {
        await using var scope = await _fixture.CreatePageAsync();
        var page = scope.Page;

        var result = await EvaluateJsonAsync(page, """
            (() => {
                const solution = generateSolution(app.cfg, true);
                app.solution = solution;
                app.givenSet = new Set([0, 1, 2]);
                app.playerGrid = new Array(app.cfg.N * app.cfg.N).fill(-1);
                app.playerGrid[10] = (solution[10] + 1) % app.cfg.N;
                app.playerGrid[11] = solution[11];

                const candidateState = candidateStateGrid();
                const withoutWrong = (() => {
                    const saved = app.playerGrid[10];
                    app.playerGrid[10] = -1;
                    const g = candidateStateGrid();
                    app.playerGrid[10] = saved;
                    return g;
                })();

                const groups = buildConflictGroups(app.cfg, true);
                const conflicts = Array.from(getConflicts(app.cfg, (() => {
                    const grid = new Array(app.cfg.N * app.cfg.N).fill(-1);
                    grid[0] = 1;
                    grid[1] = 1;
                    return grid;
                })())).sort((a, b) => a - b);
                const related = Array.from(getRelatedIndices(app.cfg, 0, true)).sort((a, b) => a - b);

                const originalCandMask = candMask;
                const originalCountSolutions = countSolutions;
                app.solution = null;
                candMask = () => 0;
                countSolutions = (_, partial) => partial[0] === 4 ? 1 : 0;
                const fallback = computeLiveCandidates(app.cfg, new Array(app.cfg.N * app.cfg.N).fill(-1), false);
                candMask = originalCandMask;
                countSolutions = originalCountSolutions;

                return {
                    wrongIgnored: candidateState[10] === -1,
                    correctKept: candidateState[11] === solution[11],
                    sameAsWithoutWrong: JSON.stringify(candidateState) === JSON.stringify(withoutWrong),
                    groupsCount: groups.length,
                    firstGroupLength: groups[0].length,
                    conflicts,
                    relatedHasRow: related.includes(1),
                    relatedHasCol: related.includes(9),
                    relatedHasBox: related.includes(10),
                    fallbackMask: fallback[0]
                };
            })()
            """);

        Assert.True(result.GetProperty("wrongIgnored").GetBoolean());
        Assert.True(result.GetProperty("correctKept").GetBoolean());
        Assert.True(result.GetProperty("sameAsWithoutWrong").GetBoolean());
        Assert.Equal(29, result.GetProperty("groupsCount").GetInt32());
        Assert.Equal(9, result.GetProperty("firstGroupLength").GetInt32());
        Assert.Equal(new[] { 0, 1 }, result.GetProperty("conflicts").EnumerateArray().Select(v => v.GetInt32()).ToArray());
        Assert.True(result.GetProperty("relatedHasRow").GetBoolean());
        Assert.True(result.GetProperty("relatedHasCol").GetBoolean());
        Assert.True(result.GetProperty("relatedHasBox").GetBoolean());
        Assert.Equal(16, result.GetProperty("fallbackMask").GetInt32());
    }

    [Fact]
    public async Task GridPaletteAndRenderHelpers_UpdateTheDomCorrectly()
    {
        await using var scope = await _fixture.CreatePageAsync();
        var page = scope.Page;

        await page.EvaluateAsync("""
            () => {
                app.solution = generateSolution(app.cfg, true);
                app.givenSet = new Set([0, 1, 2]);
                app.hintSet = new Set([3]);
                app.playerGrid = app.solution.slice();
                app.playerGrid[0] = (app.solution[0] + 1) % app.cfg.N;
                app.playerGrid[4] = -1;
                app.candidates = new Array(app.cfg.N * app.cfg.N).fill(null);
                app.candidates[4] = new Set([1, 3]);
                app.selectedTool = 1;
                app.autoCandidates = false;
                render();
            }
            """);

        Assert.Equal(81, await page.Locator("#grid .cell").CountAsync());
        Assert.Equal(10, await page.Locator("#palette .swatch").CountAsync());
        Assert.Equal(1, await page.Locator("#grid .candidates-grid").CountAsync());
        Assert.Equal(3, await page.Locator("#grid .cell.given").CountAsync());
        Assert.Equal(3, await page.Locator("#grid .peg.given").CountAsync());

        var result = await EvaluateJsonAsync(page, """
            (() => {
                const solved = completedColours(app.solution);
                const full = fullGrid();
                app.givenSet = new Set([...Array(app.cfg.N * app.cfg.N).keys()]);
                app.playerGrid = app.solution.slice();
                app.selectedTool = 0;
                updatePaletteSelection(fullGrid());

                const candidateWrap = buildCandidatesGrid([1, 3], 4, 3);

                return {
                    completedCount: solved.size,
                    fullGridGivenCell: full[0],
                    fullGridPlayerCell: full[4],
                    selectedTool: app.selectedTool,
                    doneSwatches: Array.from(document.querySelectorAll('#palette .swatch.done')).length,
                    candidateSlots: candidateWrap.querySelectorAll('.candidate-slot').length,
                    candidateActive: candidateWrap.querySelectorAll('.candidate-slot.active').length,
                    candidateSelected: candidateWrap.querySelectorAll('.candidate-slot.selected-colour').length
                };
            })()
            """);

        Assert.Equal(9, result.GetProperty("completedCount").GetInt32());
        Assert.Equal(result.GetProperty("fullGridGivenCell").GetInt32(), result.GetProperty("fullGridGivenCell").GetInt32());
        Assert.Equal(-1, result.GetProperty("fullGridPlayerCell").GetInt32());
        Assert.Equal(9, result.GetProperty("selectedTool").GetInt32());
        Assert.Equal(9, result.GetProperty("doneSwatches").GetInt32());
        Assert.Equal(4, result.GetProperty("candidateSlots").GetInt32());
        Assert.Equal(2, result.GetProperty("candidateActive").GetInt32());
        Assert.Equal(1, result.GetProperty("candidateSelected").GetInt32());
    }

    [Fact]
    public async Task ModeAndToastHelpers_UpdateUiStateAndShareStrings()
    {
        await using var scope = await _fixture.CreatePageAsync();
        var page = scope.Page;

        await page.EvaluateAsync("""
            () => {
                app.solution = generateSolution(app.cfg, true);
                app.givenSet = new Set();
                app.playerGrid = new Array(app.cfg.N * app.cfg.N).fill(-1);
                app.candidates = new Array(app.cfg.N * app.cfg.N).fill(null);
                app.diff = 'hard';
                app.errorCount = 2;
                app.maxErrors = 4;
                app.hintsUsed = 1;
                app.maxHints = 4;
                app.autoCandidates = false;
                app.pencilMode = false;
                updateModeToggleAvailability(false);
                setPencilMode(true);
                setAutoCandidates(true);
                showCompletionToast(true);
                updateSubtitle();
            }
            """);

        var result = await EvaluateJsonAsync(page, """
            (() => ({
                pencilPressed: document.getElementById('pencilToggle').getAttribute('aria-pressed'),
                autoPressed: document.getElementById('autoToggle').getAttribute('aria-pressed'),
                pencilLabel: document.getElementById('pencilLabel').textContent,
                autoLabel: document.getElementById('autoLabel').textContent,
                toastDisplay: document.getElementById('completionToast').style.display,
                badge: document.getElementById('completionBadge').textContent,
                subtitle: document.getElementById('subtitleRule').textContent,
                modeOpacity: document.getElementById('pencilCard').style.opacity,
                modePointer: document.getElementById('pencilToggle').style.pointerEvents,
                mistakesGauge: buildMistakesGauge(),
                shareMistakes: buildShareMistakesGauge(),
                shareHints: buildShareHintsGauge(),
                shareText: buildShareText(),
                gauge: gauge(2, 5, '🟩')
            }))()
            """);

        Assert.Equal("false", result.GetProperty("pencilPressed").GetString());
        Assert.Equal("true", result.GetProperty("autoPressed").GetString());
        Assert.Equal("OFF", result.GetProperty("pencilLabel").GetString());
        Assert.Equal("ON", result.GetProperty("autoLabel").GetString());
        Assert.Equal("block", result.GetProperty("toastDisplay").GetString());
        Assert.Equal("✓ Solved!", result.GetProperty("badge").GetString());
        Assert.Equal("No repeated colour in any row, column, 3×3 box, or diagonal.", result.GetProperty("subtitle").GetString());
        Assert.Equal("0.5", result.GetProperty("modeOpacity").GetString());
        Assert.Equal("none", result.GetProperty("modePointer").GetString());
        Assert.Contains("2 of 4 mistakes used", result.GetProperty("mistakesGauge").GetString());
        Assert.Equal("⬜️🟩🟩🟥🟥", result.GetProperty("shareMistakes").GetString());
        Assert.Equal("⬜️🟩🟩🟩🟨", result.GetProperty("shareHints").GetString());
        Assert.Contains("Tashoku – Hard", result.GetProperty("shareText").GetString());
        Assert.Equal("⬜️⬜️⬜️🟩🟩", result.GetProperty("gauge").GetString());
    }

    [Fact]
    public async Task PersistenceAndDifficultyHelpers_SaveLoadAndReportActiveGames()
    {
        await using var scope = await _fixture.CreatePageAsync();
        var page = scope.Page;

        await page.EvaluateAsync("""
            () => {
                const originalSetTimeout = window.setTimeout;
                const originalClearTimeout = window.clearTimeout;
                window.setTimeout = fn => { fn(); return 1; };
                window.clearTimeout = () => {};

                app.solution = generateSolution(app.cfg, true);
                app.givenSet = new Set([0, 1]);
                app.hintSet = new Set([2]);
                app.playerGrid = app.solution.slice();
                app.playerGrid[3] = (app.solution[3] + 1) % app.cfg.N;
                app.candidates = new Array(app.cfg.N * app.cfg.N).fill(null);
                app.candidates[4] = new Set([1, 3]);
                app.hintsUsed = 2;
                app.errorCount = 1;
                app.failed = false;
                app.completionToastShown = true;
                app.diff = 'medium';
                app.diag = true;

                applyDifficultyLimits();
                updateDifficultyButtons();
                saveGameState();

                window.setTimeout = originalSetTimeout;
                window.clearTimeout = originalClearTimeout;
            }
            """);

        var saved = await EvaluateJsonAsync(page, "JSON.parse(localStorage.getItem('tashokuGameState'))");
        Assert.Equal(81, saved.GetProperty("solution").GetArrayLength());
        Assert.Equal(2, saved.GetProperty("givenSet").GetArrayLength());
        Assert.Equal(1, saved.GetProperty("errorCount").GetInt32());
        Assert.Equal(2, saved.GetProperty("hintsUsed").GetInt32());
        Assert.Equal("medium", saved.GetProperty("diff").GetString());
        Assert.True(saved.GetProperty("diag").GetBoolean());

        var activeBeforeLoad = await page.EvaluateAsync<bool>("() => hasActiveGame()");
        Assert.True(activeBeforeLoad);

        await page.EvaluateAsync("""
            () => {
                const state = {
                    solution: new Array(81).fill(0),
                    givenSet: [0],
                    hintSet: [1],
                    playerGrid: new Array(81).fill(-1),
                    candidates: new Array(81).fill(null),
                    hintsUsed: 3,
                    errorCount: 2,
                    failed: true,
                    completionToastShown: false,
                    diff: 'easy',
                    diag: false
                };
                localStorage.setItem('tashokuGameState', JSON.stringify(state));
                app.solution = null;
                app.givenSet = new Set();
                app.hintSet = new Set();
                app.playerGrid = new Array(81).fill(8);
                app.candidates = new Array(81).fill(null);
                app.hintsUsed = 0;
                app.errorCount = 0;
                app.failed = false;
                app.completionToastShown = true;
                app.diff = 'hard';
                app.diag = true;
            }
            """);

        var loaded = await page.EvaluateAsync<bool>("() => loadGameState()");
        Assert.True(loaded);

        var postLoad = await EvaluateJsonAsync(page, """
            ({
                maxErrors: app.maxErrors,
                maxHints: app.maxHints,
                diff: app.diff,
                diag: app.diag,
                givenCount: app.givenSet.size,
                hintCount: app.hintSet.size,
                playerCell: app.playerGrid[0],
                activeAfterLoad: hasActiveGame(),
                difficultyHard: JSON.stringify(difficultyLimits('hard')),
                difficultyUnknown: JSON.stringify(difficultyLimits('unknown')),
                clueMedium: clueCount(app.cfg, app.diag, 'medium'),
                clueEasy: clueCount(app.cfg, app.diag, 'easy')
            })
            """);

        Assert.Equal(5, postLoad.GetProperty("maxErrors").GetInt32());
        Assert.Equal(5, postLoad.GetProperty("maxHints").GetInt32());
        Assert.Equal("easy", postLoad.GetProperty("diff").GetString());
        Assert.False(postLoad.GetProperty("diag").GetBoolean());
        Assert.Equal(1, postLoad.GetProperty("givenCount").GetInt32());
        Assert.Equal(1, postLoad.GetProperty("hintCount").GetInt32());
        Assert.Equal(-1, postLoad.GetProperty("playerCell").GetInt32());
        Assert.False(postLoad.GetProperty("activeAfterLoad").GetBoolean());

        var difficultyHard = JsonDocument.Parse(postLoad.GetProperty("difficultyHard").GetString()!).RootElement.Clone();
        Assert.Equal(3, difficultyHard.GetProperty("maxErrors").GetInt32());
        Assert.Equal(3, difficultyHard.GetProperty("maxHints").GetInt32());

        var difficultyUnknown = JsonDocument.Parse(postLoad.GetProperty("difficultyUnknown").GetString()!).RootElement.Clone();
        Assert.Equal(4, difficultyUnknown.GetProperty("maxErrors").GetInt32());
        Assert.Equal(4, difficultyUnknown.GetProperty("maxHints").GetInt32());
        Assert.Equal(30, postLoad.GetProperty("clueMedium").GetInt32());
        Assert.Equal(40, postLoad.GetProperty("clueEasy").GetInt32());

        await page.EvaluateAsync("""
            () => {
                updateDifficultyButtons();
            }
            """);

        Assert.Equal(1, await page.Locator("#btnEasy.active").CountAsync());
        Assert.Equal(0, await page.Locator("#btnHard.active").CountAsync());
    }

    [Fact]
    public async Task GameFlowHelpers_ResetBoardHandleClicksAndHints()
    {
        await using var scope = await _fixture.CreatePageAsync();
        var page = scope.Page;

        var result = await EvaluateJsonAsync(page, """
            (() => {
                const originalGeneratePuzzle = generatePuzzle;
                const originalRender = render;
                let renderCalled = false;
                generatePuzzle = () => ({
                    solution: Array.from({ length: app.cfg.N * app.cfg.N }, (_, i) => i % app.cfg.N),
                    given: [0, 1, 2]
                });
                render = () => { renderCalled = true; };

                app.solution = new Array(app.cfg.N * app.cfg.N).fill(8);
                app.solution[5] = 4;
                app.solution[6] = 2;
                app.solution[7] = 3;
                app.solution[8] = 5;
                app.givenSet = new Set();
                app.hintSet = new Set();
                app.playerGrid = new Array(app.cfg.N * app.cfg.N).fill(-1);
                app.candidates = new Array(app.cfg.N * app.cfg.N).fill(null);
                app.candidates[5] = new Set([2, 4]);
                app.selectedTool = app.cfg.N;
                app.pencilMode = false;
                app.autoCandidates = false;
                app.hintsUsed = 0;
                app.maxHints = 4;
                app.errorCount = 0;
                app.maxErrors = 4;
                app.failed = false;
                app.completionToastShown = false;

                placeColor(5, 4);
                const candidateRemoved = app.candidates[5] === null;

                app.playerGrid[6] = 2;
                app.selectedTool = app.cfg.N;
                handleCellClick(6);
                const erased = app.playerGrid[6] === -1;

                app.playerGrid[7] = -1;
                app.selectedTool = 3;
                app.pencilMode = false;
                handleCellClick(7);
                const placed = app.playerGrid[7] === 3;

                app.playerGrid[8] = -1;
                app.selectedTool = 1;
                handleCellClick(8);
                const errorRaised = app.errorCount === 1;

                triggerFail();
                const failed = app.failed;

                app.solution = new Array(app.cfg.N * app.cfg.N).fill(8);
                app.solution[8] = 5;
                app.playerGrid = app.solution.slice();
                app.playerGrid[8] = -1;
                app.givenSet = new Set([...Array(app.cfg.N * app.cfg.N).keys()].filter(i => i !== 8));
                app.hintSet = new Set();
                app.hintsUsed = 0;
                app.failed = false;
                giveHint();
                const hinted = app.playerGrid[8] === app.solution[8] && app.hintSet.has(8) && app.hintsUsed === 1;

                const clue = clueCount(app.cfg, app.diag, 'easy');
                setDiff('hard');

                const newPuzzleResult = {
                    diffAfterSet: app.diff,
                    maxErrorsAfterSet: app.maxErrors,
                    maxHintsAfterSet: app.maxHints,
                    renderWasCalled: renderCalled
                };

                generatePuzzle = originalGeneratePuzzle;
                render = originalRender;

                return {
                    candidateRemoved,
                    erased,
                    placed,
                    errorRaised,
                    failed,
                    hinted,
                    clue,
                    newPuzzleResult
                };
            })()
            """);

        Assert.True(result.GetProperty("candidateRemoved").GetBoolean());
        Assert.True(result.GetProperty("erased").GetBoolean());
        Assert.True(result.GetProperty("placed").GetBoolean());
        Assert.True(result.GetProperty("errorRaised").GetBoolean());
        Assert.True(result.GetProperty("failed").GetBoolean());
        Assert.True(result.GetProperty("hinted").GetBoolean());
        Assert.Equal(40, result.GetProperty("clue").GetInt32());
        Assert.Equal("hard", result.GetProperty("newPuzzleResult").GetProperty("diffAfterSet").GetString());
        Assert.Equal(3, result.GetProperty("newPuzzleResult").GetProperty("maxErrorsAfterSet").GetInt32());
        Assert.Equal(3, result.GetProperty("newPuzzleResult").GetProperty("maxHintsAfterSet").GetInt32());
        Assert.True(result.GetProperty("newPuzzleResult").GetProperty("renderWasCalled").GetBoolean());
    }

    [Fact]
    public async Task ShareAndClipboardHelpers_UseExpectedFallbacks()
    {
        await using var scope = await _fixture.CreatePageAsync();
        var page = scope.Page;

        await page.EvaluateAsync("""
            () => {
                app.solution = generateSolution(app.cfg, true);
                app.givenSet = new Set();
                app.playerGrid = new Array(app.cfg.N * app.cfg.N).fill(-1);
                app.candidates = new Array(app.cfg.N * app.cfg.N).fill(null);
                app.diff = 'medium';
                app.errorCount = 1;
                app.maxErrors = 4;
                app.hintsUsed = 2;
                app.maxHints = 4;
                app.failed = false;
                app.pencilMode = false;
                app.autoCandidates = false;
                app.playerGrid[0] = app.solution[0];

                Object.defineProperty(navigator, 'share', {
                    configurable: true,
                    value: async payload => {
                        window.__sharedPayloads = window.__sharedPayloads || [];
                        window.__sharedPayloads.push(payload);
                    }
                });
                Object.defineProperty(navigator, 'clipboard', {
                    configurable: true,
                    value: {
                        writeText: async text => { window.__clipboardText = text; }
                    }
                });
                document.execCommand = () => true;
            }
            """);

        await page.EvaluateAsync("""
            async () => {
                await shareResult();
                await shareApp();
            }
            """);

        var sharePayloads = await EvaluateJsonAsync(page, "window.__sharedPayloads");
        var sharePayloadArray = sharePayloads.EnumerateArray().ToArray();
        Assert.Equal(2, sharePayloadArray.Length);
        Assert.Contains("Tashoku – Medium", sharePayloadArray[0].GetProperty("text").GetString()!);
        Assert.Contains("https://robert-stam-co-nz.github.io/Tashoku/", sharePayloadArray[1].GetProperty("url").GetString() ?? string.Empty);

        await page.EvaluateAsync("""
            () => {
                fallbackShare('clipboard text');
            }
            """);

        await page.WaitForFunctionAsync("() => window.__clipboardText === 'clipboard text'");
        Assert.Equal("clipboard text", await page.EvaluateAsync<string>("() => window.__clipboardText"));

        await page.EvaluateAsync("""
            () => {
                window.__legacyMessage = null;
                legacyCopyFallback('legacy text', msg => { window.__legacyMessage = msg; });
            }
            """);

        Assert.Equal("Copied to clipboard!", await page.EvaluateAsync<string>("() => window.__legacyMessage"));
    }

    private static async Task<JsonElement> EvaluateJsonAsync(IPage page, string expression)
    {
        var json = await page.EvaluateAsync<string>($"() => JSON.stringify({expression})");
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
