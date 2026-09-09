using System.Diagnostics;
using System.Text;

namespace TerrariaSeedRoller.Core;

public sealed record GeneratedWorld(
    int Seed,
    string CopiedSeed,
    string WorldPath,
    string AttemptDirectory,
    TimeSpan Duration,
    string ServerOutput);

public sealed class TerrariaServerGenerator
{
    public async Task<GeneratedWorld> GenerateAsync(GenerationSettings settings, int seed,
        Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        EnsureDiskSpace(settings.CandidateDirectory, settings.MinimumFreeDiskGb);
        string root = Path.GetFullPath(settings.CandidateDirectory);
        string workRoot = Path.Combine(root, "_work");
        Directory.CreateDirectory(workRoot);
        string safeSeed = seed.ToString(System.Globalization.CultureInfo.InvariantCulture).Replace('-', 'n');
        string attemptDirectory = Path.Combine(workRoot,
            $"seed_{safeSeed}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(attemptDirectory);
        string saveDirectory = Path.Combine(attemptDirectory, "save");
        Directory.CreateDirectory(saveDirectory);
        string worldName = $"TSR_{safeSeed}";
        string worldPath = Path.Combine(attemptDirectory, worldName + ".wld");
        string configPath = Path.Combine(attemptDirectory, "serverconfig.txt");
        string copiedSeed = settings.BuildCopiedSeed(seed);
        await File.WriteAllTextAsync(configPath, BuildConfig(settings, copiedSeed, worldName, worldPath),
            new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

        ProcessStartInfo startInfo = new()
        {
            FileName = Path.GetFullPath(settings.TerrariaServerPath),
            WorkingDirectory = attemptDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-config");
        startInfo.ArgumentList.Add(configPath);
        startInfo.ArgumentList.Add("-savedirectory");
        startInfo.ArgumentList.Add(saveDirectory);

        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        StringBuilder output = new();
        object outputLock = new();
        string lastProgressStage = string.Empty;
        int lastProgressBucket = -1;
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            if (!process.Start()) throw new InvalidOperationException("TerrariaServer failed to start.");
            int ownedProcessId = process.Id;
            log?.Invoke($"种子 {seed}: 已启动后台 TerrariaServer (PID {ownedProcessId})");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using CancellationTokenSource timeout = new(settings.PerWorldTimeout);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeout.Token);
            Task exited = process.WaitForExitAsync(linked.Token);
            Task completed = await Task.WhenAny(ready.Task, exited).ConfigureAwait(false);
            if (completed == exited)
            {
                await exited.ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"TerrariaServer exited before world generation completed (exit {process.ExitCode}).\n{Tail(output, outputLock)}");
            }

            await ready.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            if (!File.Exists(worldPath))
                throw new InvalidDataException("TerrariaServer reported startup but the generated world file is missing.");
            await WaitForStableFileAsync(worldPath, linked.Token).ConfigureAwait(false);

            // TerrariaServer on Windows forces Console.InputEncoding to UTF-16LE.
            // Writing raw bytes avoids StandardInput's platform-dependent encoding.
            byte[] exitCommand = Encoding.Unicode.GetBytes("exit-nosave\r\n");
            await process.StandardInput.BaseStream.WriteAsync(exitCommand, linked.Token).ConfigureAwait(false);
            await process.StandardInput.BaseStream.FlushAsync(linked.Token).ConfigureAwait(false);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillOwnedProcess(process);
                throw;
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"TerrariaServer exited with code {process.ExitCode}.\n{Tail(output, outputLock)}");
            stopwatch.Stop();
            string captured;
            lock (outputLock) captured = output.ToString();
            return new GeneratedWorld(seed, copiedSeed, worldPath, attemptDirectory,
                stopwatch.Elapsed, captured);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillOwnedProcess(process);
            throw new TimeoutException($"World generation for seed {seed} exceeded {settings.PerWorldTimeout}.");
        }
        catch
        {
            KillOwnedProcess(process);
            throw;
        }

        void Capture(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (outputLock)
            {
                if (output.Length > 256_000) output.Remove(0, 64_000);
                output.AppendLine(line);
            }
            bool emit = true;
            if (TryGetProgress(line, out string stage, out int bucket))
            {
                lock (outputLock)
                {
                    emit = !string.Equals(stage, lastProgressStage, StringComparison.Ordinal) ||
                           bucket != lastProgressBucket;
                    if (emit) { lastProgressStage = stage; lastProgressBucket = bucket; }
                }
            }
            if (emit) log?.Invoke($"[{seed}] {line}");
            if (line.Contains("Server started", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("服务器已启动", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Listening on port", StringComparison.OrdinalIgnoreCase))
                ready.TrySetResult();
        }
    }

    public static void DeleteAttemptDirectory(string attemptDirectory, string candidateDirectory)
    {
        string root = Path.GetFullPath(Path.Combine(candidateDirectory, "_work"));
        string target = Path.GetFullPath(attemptDirectory);
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to delete a directory outside the seed-roller work area.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    private static string BuildConfig(GenerationSettings settings, string copiedSeed,
        string worldName, string worldPath)
    {
        int difficulty = (int)settings.Difficulty - 1;
        return string.Join(Environment.NewLine,
        [
            $"world={worldPath}",
            $"autocreate={(int)settings.Size}",
            $"difficulty={difficulty}",
            $"worldname={worldName}",
            $"seed={copiedSeed}",
            "maxplayers=1",
            "port=0",
            "worldrollbackstokeep=0",
            "language=en-US",
            "upnp=0",
            "secure=0",
            "priority=1",
            string.Empty
        ]);
    }

    private static async Task WaitForStableFileAsync(string path, CancellationToken token)
    {
        long previous = -1;
        int stableChecks = 0;
        while (stableChecks < 3)
        {
            token.ThrowIfCancellationRequested();
            long current = new FileInfo(path).Length;
            if (current > 1024 && current == previous) stableChecks++;
            else stableChecks = 0;
            previous = current;
            await Task.Delay(350, token).ConfigureAwait(false);
        }
    }

    private static void EnsureDiskSpace(string directory, double minimumFreeGb)
    {
        string full = Path.GetFullPath(directory);
        Directory.CreateDirectory(full);
        string? root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(root)) throw new IOException("Cannot determine output drive.");
        DriveInfo drive = new(root);
        long required = checked((long)(minimumFreeGb * 1024 * 1024 * 1024));
        if (drive.AvailableFreeSpace < required)
            throw new IOException($"Free disk space is below the configured {minimumFreeGb:0.##} GB guard.");
    }

    private static void KillOwnedProcess(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static string Tail(StringBuilder builder, object gate)
    {
        string text;
        lock (gate) text = builder.ToString();
        return text.Length <= 4000 ? text : text[^4000..];
    }

    private static bool TryGetProgress(string line, out string stage, out int bucket)
    {
        stage = string.Empty;
        bucket = -1;
        int percent = line.IndexOf('%');
        if (percent < 1) return false;
        int start = percent - 1;
        while (start >= 0 && (char.IsDigit(line[start]) || line[start] == '.')) start--;
        string number = line[(start + 1)..percent];
        if (!double.TryParse(number, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double value)) return false;
        bucket = (int)Math.Floor(value);
        int separator = line.IndexOf(" - ", percent, StringComparison.Ordinal);
        if (separator >= 0)
        {
            int end = line.IndexOf(" - ", separator + 3, StringComparison.Ordinal);
            stage = end < 0 ? line[(separator + 3)..] : line[(separator + 3)..end];
        }
        else stage = line[..(start + 1)].Trim();
        return true;
    }
}

public sealed class PauseController
{
    private readonly object _gate = new();
    private TaskCompletionSource? _resume;

    public bool IsPaused { get { lock (_gate) return _resume is not null; } }

    public void Pause()
    {
        lock (_gate) _resume ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Resume()
    {
        TaskCompletionSource? resume;
        lock (_gate) { resume = _resume; _resume = null; }
        resume?.TrySetResult();
    }

    public Task WaitIfPausedAsync(CancellationToken token)
    {
        Task? task;
        lock (_gate) task = _resume?.Task;
        return task is null ? Task.CompletedTask : task.WaitAsync(token);
    }
}
