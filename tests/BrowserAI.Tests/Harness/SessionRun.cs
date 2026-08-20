// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

using System.Text;
using System.Text.Json.Nodes;
using BrowserAI.Interop;
using BrowserAI.Sessions;

namespace BrowserAI.Tests.Harness;

/// <summary>
/// One scripted conversation with the published binary, exercising all six
/// authored session tools, captured once and asserted on by many tests.
/// </summary>
/// <remarks>
/// <para>
/// <b>One capture rather than one process per assertion.</b> Every fact below
/// then comes from the same run, which is both cheaper and stronger: the tool
/// list, the refusals, the config round trip and the profile on disk are known to
/// be true <i>of the same session</i> rather than of a dozen sessions that might
/// have differed. It is the shape <see cref="SliceRun"/> already uses.
/// </para>
/// <para>
/// <b>Two BrowserAI processes, and the second one is not optional.</b> A session
/// directory cannot be moved while it is open — the child's working directory is
/// the session directory and Windows refuses to rename a directory some process
/// is sitting in — so the move-versus-copy case needs a session that was created,
/// closed, and then met again by a different process. That is exactly the case
/// the feature exists for.
/// </para>
/// </remarks>
internal sealed record SessionRun
{
    private static readonly Lazy<Task<SessionRun>> Shared = new(CaptureAsync);

    /// <summary>The scratch tree every session in this run lives under.</summary>
    public required string Root { get; init; }

    /// <summary>Answers from the first process, keyed by a short label.</summary>
    public required IReadOnlyDictionary<string, JsonObject> Answers { get; init; }

    /// <summary>Whether the browser really wrote into the session's own profile directory.</summary>
    public required bool ProfileWasUsed { get; init; }

    /// <summary>Everything the first session's own <c>browserai.log</c> held while it ran.</summary>
    public required string SessionLog { get; init; }

    /// <summary>Everything the moved session's own log held after it was resumed.</summary>
    public required string MovedSessionLog { get; init; }

    /// <summary>Whether the destroyed session's <c>browserai.json</c> is gone.</summary>
    public required bool DestroyedLockFileIsGone { get; init; }

    /// <summary>Whether the file that was held open through the destroy survived.</summary>
    public required bool HeldFileSurvivedTheDestroy { get; init; }

    /// <summary>The one capture, run at most once per test process.</summary>
    /// <returns>The captured run.</returns>
    public static Task<SessionRun> SharedAsync() => Shared.Value;

    /// <summary>The text of an authored tool's answer.</summary>
    /// <param name="label">The label the answer was recorded under.</param>
    /// <returns>The joined text content.</returns>
    public string Text(string label) =>
        string.Join(
            "\n",
            (Answers[label]["content"]?.AsArray() ?? [])
                .Where(block => (string?)block!["type"] == "text")
                .Select(block => (string?)block!["text"] ?? string.Empty));

    /// <summary>Whether an authored tool's answer was a refusal.</summary>
    /// <param name="label">The label the answer was recorded under.</param>
    /// <returns>Its <c>isError</c>.</returns>
    public bool IsError(string label) => (bool?)Answers[label]["isError"] is true;

    private static async Task<SessionRun> CaptureAsync()
    {
        PublishedSlice.EnsureFresh();

        var root = Path.Combine(ScratchRoot.Path, $"sessions-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(root);

        var first = await FirstProcessAsync(root).ConfigureAwait(false);

        return await SecondProcessAsync(root, first).ConfigureAwait(false);
    }

    private static async Task<FirstProcess> FirstProcessAsync(string root)
    {
        var answers = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var alpha = Path.Combine(root, "alpha");
        var beta = Path.Combine(root, "beta");
        var gamma = Path.Combine(root, "gamma");
        var held = Path.Combine(beta, SessionLayout.OutputFolderName, "held.txt");

        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            [],
            root,
            PublishedSlice.InheritedEnvironment());

        {
            _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion).ConfigureAwait(false);

            answers["init"] = await CallAsync(client, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = alpha,
                ["purpose"] = "the first session's purpose",
            }).ConfigureAwait(false);

            answers["initAgain"] = await CallAsync(client, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = alpha,
                ["purpose"] = "a second attempt on the same directory",
            }).ConfigureAwait(false);

            // No browser is launched by this one, which is what makes the round
            // trip cheap: it returns the merged config the child resolved.
            answers["getConfig"] = await CallAsync(client, "browser_get_config", new JsonObject
            {
                ["session"] = alpha,
                ["why"] = "the suite exercising this call",
            }).ConfigureAwait(false);

            answers["navigate"] = await CallAsync(client, "browser_navigate", new JsonObject
            {
                ["url"] = SliceRun.TargetUrl,
                ["session"] = alpha,
                ["why"] = "the suite exercising this call",
            }).ConfigureAwait(false);

            // Read while the browser is up: this is the difference between "the
            // key is in the config" and "the browser used it".
            var profileUsed = Directory.Exists(Path.Combine(alpha, SessionLayout.ProfileFolderName))
                && Directory.EnumerateFileSystemEntries(Path.Combine(alpha, SessionLayout.ProfileFolderName)).Any();

            // ⚠️ The sixth authored tool, called at the one moment it is
            // guaranteed to REFUSE: the navigation above left a real Chromium
            // running out of the real browsers root, and this process is driving
            // the session that owns it.
            //
            // The guard is not decoration. If no browser were live the tool would
            // do exactly what it says — delete 430 MiB and download it again —
            // in the middle of a suite whose other tests are driving browsers out
            // of that directory. Checked with the product's own image-path
            // enumeration rather than assumed, so a capture that somehow lost its
            // browser records the fact instead of destroying the machine's
            // install.
            var browsersLive = BrowserProcesses.RunningFrom(BrowserAiPaths.BrowsersDirectory).Count;

            answers["reinstallWhileLive"] = browsersLive is 0
                ? new JsonObject
                {
                    ["content"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = "SKIPPED: no browser was running out of the browsers root, so browserai_reinstall_browser was not called. It would have deleted and re-downloaded the machine's install.",
                    }),
                    ["isError"] = true,
                }
                : await CallAsync(client, SessionToolSurface.ReinstallBrowser, new JsonObject
                {
                    ["browser"] = SessionManager.DefaultBrowser,
                }).ConfigureAwait(false);

            answers["setPurpose"] = await CallAsync(client, SessionToolSurface.SetPurpose, new JsonObject
            {
                ["session"] = alpha,
                ["why"] = "the suite exercising this call",
                ["purpose"] = "a purpose set after the fact",
            }).ConfigureAwait(false);

            // ⚠️ The seventh authored tool, called on a session this process is
            // driving -- which is the arm that proves it takes no lock, because
            // anything that did would be refused by the holder above.
            answers["catchUp"] = await CallAsync(client, SessionToolSurface.CatchUp, new JsonObject
            {
                [SessionToolSurface.SessionParameter] = alpha,
            }).ConfigureAwait(false);

            answers["list"] = await CallAsync(client, SessionToolSurface.List, new JsonObject
            {
                ["directory"] = root,
            }).ConfigureAwait(false);

            answers["listElsewhere"] = await CallAsync(client, SessionToolSurface.List, new JsonObject
            {
                ["directory"] = Path.Combine(root, "nothing-here"),
            }).ConfigureAwait(false);

            answers["unknownSession"] = await CallAsync(client, "browser_snapshot", new JsonObject
            {
                ["session"] = Path.Combine(root, "never-a-session"),
                ["why"] = "the suite exercising this call",
            }).ConfigureAwait(false);

            foreach (var (label, directory) in new (string Label, JsonNode? Directory)[]
            {
                ("relative", "relative\\path"),
                ("empty", string.Empty),
                ("volumeRoot", Path.GetPathRoot(root)!),
                ("absent", null),
            })
            {
                var arguments = new JsonObject { ["purpose"] = "should never be created" };

                if (directory is not null)
                {
                    arguments["directory"] = directory;
                }

                answers["init-" + label] = await CallAsync(client, SessionToolSurface.Init, arguments).ConfigureAwait(false);

                var resumeArguments = new JsonObject { ["why"] = "the suite exercising this call" };

                if (directory is not null)
                {
                    resumeArguments["directory"] = directory.DeepClone();
                }

                answers["resume-" + label] = await CallAsync(client, SessionToolSurface.Resume, resumeArguments).ConfigureAwait(false);
            }

            // ⚠️ Was `init-badMode` until 2026-08-20, when session modes were
            // deleted and there was no longer a bad mode to give. What replaced
            // it is the same shape one layer down: an argument of the wrong TYPE
            // on the one boolean that took a mode's place.
            answers["init-badHeaded"] = await CallAsync(client, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = Path.Combine(root, "bad-headed"),
                ["purpose"] = "should never be created",
                ["headed"] = "yes",
            }).ConfigureAwait(false);

            var notASession = Path.Combine(root, "not-a-session");
            _ = Directory.CreateDirectory(notASession);

            answers["resumeNotASession"] = await CallAsync(client, SessionToolSurface.Resume, new JsonObject
            {
                ["why"] = "the suite exercising this call",
                ["directory"] = notASession,
            }).ConfigureAwait(false);

            // ⚠️ Was `resumeWithMode` until 2026-08-20. `mode` is not an
            // argument anywhere any more, so there is nothing for resume to
            // refuse about it; `browser` still is, for the reason that always
            // separated the two — a profile on disk belongs to the browser that
            // made it, and headedness belongs to nothing.
            answers["resumeWithBrowser"] = await CallAsync(client, SessionToolSurface.Resume, new JsonObject
            {
                ["why"] = "the suite exercising this call",
                ["directory"] = alpha,
                ["browser"] = "firefox",
            }).ConfigureAwait(false);

            // Documents, which has no browserai.json. Nothing is touched before the
            // refusal, which is the whole reason the check is a read.
            answers["destroyDocuments"] = await CallAsync(client, SessionToolSurface.Destroy, new JsonObject
            {
                ["why"] = "the suite exercising this call",
                ["directory"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.DoNotVerify),
            }).ConfigureAwait(false);

            answers["initBeta"] = await CallAsync(client, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = beta,
                ["purpose"] = "the session that gets destroyed",
                ["tracing"] = true,
                ["consoleLevel"] = "debug",
                ["debug"] = true,
            }).ConfigureAwait(false);

            var sessionLog = ReadSharing(Path.Combine(alpha, "browserai.log"));

            answers["initGamma"] = await CallAsync(client, SessionToolSurface.Init, new JsonObject
            {
                ["directory"] = gamma,
                ["purpose"] = "the session that gets moved",
            }).ConfigureAwait(false);

            bool lockGone;
            bool heldSurvived;

            // FileShare.None, so the destroy below meets a file it cannot remove
            // and has to report it rather than fail.
            using (var _ = new FileStream(held, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                answers["destroyBeta"] = await CallAsync(client, SessionToolSurface.Destroy, new JsonObject
                {
                    ["why"] = "the suite exercising this call",
                    ["directory"] = beta,
                }).ConfigureAwait(false);

                lockGone = !File.Exists(Path.Combine(beta, SessionLayout.LockFileName));
                heldSurvived = File.Exists(held);
            }

            // Closed rather than killed: BrowserAI's own graceful path, and what
            // releases the session directory so the move below can happen.
            _ = await client.CloseAndWaitForExitAsync(TestDefaults.ProcessHang).ConfigureAwait(false);

            return new FirstProcess
            {
                Answers = answers,
                ProfileWasUsed = profileUsed,
                SessionLog = sessionLog,
                DestroyedLockFileIsGone = lockGone,
                HeldFileSurvivedTheDestroy = heldSurvived,
            };
        }
    }

    /// <summary>
    /// Renames a directory once the exited process's children have let go of
    /// it.
    /// </summary>
    /// <remarks>
    /// Bounded and loud: if the pin never clears, the failure names the cause
    /// and how long it waited, rather than reporting a bare access-denied that
    /// reads like a permissions problem. It never retries anything but the pin —
    /// a genuinely wrong path throws <see cref="DirectoryNotFoundException"/>
    /// and is not caught here.
    /// </remarks>
    /// <param name="from">The directory to rename.</param>
    /// <param name="to">Its new name.</param>
    /// <returns>The wait.</returns>
    private static async Task MoveWhenReleasedAsync(string from, string to)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (Exception failure) when (failure is UnauthorizedAccessException or IOException)
            {
                if (waited.Elapsed > TestDefaults.ProcessHang)
                {
                    throw new InvalidOperationException(
                        $"'{from}' was still pinned {waited.Elapsed.TotalSeconds:F1} s after BrowserAI exited, so it could not be renamed. "
                        + "A directory that is a live process's working directory cannot be renamed, nor can any of its ancestors — so something the job object should have taken down is still running.",
                        failure);
                }

                await Task.Delay(25).ConfigureAwait(false);
            }
        }
    }

    private static async Task<SessionRun> SecondProcessAsync(string root, FirstProcess first)
    {
        var answers = new Dictionary<string, JsonObject>(first.Answers, StringComparer.Ordinal);
        var gamma = Path.Combine(root, "gamma");
        var moved = Path.Combine(root, "gamma-moved");
        var copy = Path.Combine(root, "gamma-copy");

        // The ordinary case of somebody renaming a folder between sessions.
        //
        // ⚠️ Retried, and the reason is a Windows fact rather than a defect in
        // the product. BrowserAI has exited and its job object has therefore
        // terminated the node child — but a terminated process is *signalled*
        // before the kernel has torn its handles down, and a directory that is
        // any live process's current directory cannot be renamed, nor can any
        // of its ancestors. `CloseAndWaitForExitAsync` returning is proof that
        // BrowserAI is gone, not proof that its children's working directories
        // have been released. Observed once as `UnauthorizedAccessException` on
        // this line, 2026-08-16, taking twelve published-binary tests with it,
        // because they all share this one capture.
        await MoveWhenReleasedAsync(gamma, moved).ConfigureAwait(false);

        await using var client = RawStdioClient.Start(
            PublishedSlice.Executable,
            [],
            root,
            PublishedSlice.InheritedEnvironment());

        _ = await client.InitializeAsync(SliceRun.OfferedProtocolVersion).ConfigureAwait(false);

        answers["resumeMoved"] = await CallAsync(client, SessionToolSurface.Resume, new JsonObject
        {
            ["why"] = "the suite exercising this call",
            ["directory"] = moved,
            ["purpose"] = "and resumed after the move",
        }).ConfigureAwait(false);

        // ⚠️ Copied AFTER the resume above, and the order is the test rather
        // than an accident. The recorded path discriminates a copy from a move
        // only while it is ACCURATE: copying `gamma-moved` before its record was
        // repaired would produce a copy whose record names `gamma`, a path that
        // no longer exists — which is the move signature exactly, and BrowserAI
        // repairs it silently. Measured 2026-08-16; the first version of this
        // capture copied first and the copy was accepted as a move.
        CopyTree(moved, copy);

        // ⚠️ ONE CALL, no flag. `acknowledgeCopy` was deleted on 2026-08-18 with
        // the refusal it unlocked: under schema 2 the record is an append-only
        // list of timestamped statements, so resuming a copy does not overwrite
        // the evidence that it IS one, and the answer hands the model the
        // directory's whole history instead of demanding a confirmation for it.
        // The step that used to be `resumeCopyAcknowledged` is gone rather than
        // renamed, because there is no second call to make.
        answers["resumeCopy"] = await CallAsync(client, SessionToolSurface.Resume, new JsonObject
        {
            ["why"] = "the suite exercising this call",
            ["directory"] = copy,
        }).ConfigureAwait(false);

        // A real session directory that this process is not driving: alpha was
        // created and closed by the first process, so its record is on disk and
        // nothing here holds it. That is the other half of the split step 13
        // made -- "not a session" and "not open here" want different recoveries.
        answers["strandedSession"] = await CallAsync(client, "browser_snapshot", new JsonObject
        {
            ["session"] = Path.Combine(root, "alpha"),
            ["why"] = "the suite exercising this call",
        }).ConfigureAwait(false);

        var movedLog = ReadSharing(Path.Combine(moved, "browserai.log"));

        _ = await client.CloseAndWaitForExitAsync(TestDefaults.ProcessHang).ConfigureAwait(false);

        return new SessionRun
        {
            Root = root,
            Answers = answers,
            ProfileWasUsed = first.ProfileWasUsed,
            SessionLog = first.SessionLog,
            MovedSessionLog = movedLog,
            DestroyedLockFileIsGone = first.DestroyedLockFileIsGone,
            HeldFileSurvivedTheDestroy = first.HeldFileSurvivedTheDestroy,
        };
    }

    /// <summary>What the first BrowserAI process produced, before the move.</summary>
    private sealed record FirstProcess
    {
        public required IReadOnlyDictionary<string, JsonObject> Answers { get; init; }

        public required bool ProfileWasUsed { get; init; }

        public required string SessionLog { get; init; }

        public required bool DestroyedLockFileIsGone { get; init; }

        public required bool HeldFileSurvivedTheDestroy { get; init; }
    }

    private static async Task<JsonObject> CallAsync(RawStdioClient client, string tool, JsonObject arguments)
    {
        var envelope = await client.EnvelopeAsync("tools/call", new JsonObject
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        }).ConfigureAwait(false);

        return envelope["result"]?.AsObject()
            ?? throw new InvalidOperationException(
                $"'{tool}' answered with a JSON-RPC error rather than a result, which no authored tool may do: {envelope.ToJsonString()}");
    }

    private static void CopyTree(string source, string destination)
    {
        _ = Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var child in Directory.EnumerateDirectories(source))
        {
            CopyTree(child, Path.Combine(destination, Path.GetFileName(child)));
        }
    }

    /// <summary>
    /// Reads a file another process has open for append, which is what a session
    /// log is while its session runs.
    /// </summary>
    private static string ReadSharing(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            return reader.ReadToEnd();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return $"<could not be read: {failure.Message}>";
        }
    }
}
