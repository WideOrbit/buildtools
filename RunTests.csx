// This is a script that runs the tests in parallel, intended for the pretest build.

// run with: csi.exe RunTests.csx

// Environment variables that can be set:
// ParallelThrottling
// ExcludeAssemblies
// IncludeCategories
// ExcludeCategories
// TEAMCITY_BUILD_PROPERTIES_FILE
// UseUnsafeMySqlMemoryDb
// TestTimeout

#r "System.Management.dll"
#r "System.Xml.Linq.dll"

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

public class Program
{
    private class TestProcess : Process
    {
        public string testdll { get; set; }
        public bool started { get; set; }
        public bool printed { get; set; }
    }

    private static List<TestProcess> _runningTests;

    public static int Main(string[] args)
    {
        int result = 0;

        if (args.Length != 0)
        {
            Log("Usage: csi.exe RunTests.csx");
            result = 1;
        }
        else
        {
            try
            {
                RunTests();
            }
            catch (ApplicationException ex)
            {
                LogColor(ex.Message, ConsoleColor.Red);
                result = 1;
            }
            catch (Exception ex)
            {
                LogColor(ex.ToString(), ConsoleColor.Red);
                result = 1;
            }
        }

        if (Environment.UserInteractive)
        {
            Log("Press any key to continue...");
            Console.ReadKey();
        }

        return result;
    }

    private static void RunTests()
    {
        KillLingeringNunitProcesses();

        RunUnitTests();

        KillLingeringNunitProcesses();

        ParseOutput();
    }

    private static void RunUnitTests()
    {
        Log($"Current Directory: '{Directory.GetCurrentDirectory()}'");
        Log($"Processor Count: {Environment.ProcessorCount}");


        string parallelThrottling = Environment.GetEnvironmentVariable("ParallelThrottling");
        string excludeAssemblies = Environment.GetEnvironmentVariable("ExcludeAssemblies");
        string buildpropfile = Environment.GetEnvironmentVariable("TEAMCITY_BUILD_PROPERTIES_FILE");
        string includeCategories = Environment.GetEnvironmentVariable("IncludeCategories");
        string excludeCategories = Environment.GetEnvironmentVariable("ExcludeCategories");
        string testTimeout = Environment.GetEnvironmentVariable("TestTimeout");
        string useUnsafeMySqlMemoryDb = Environment.GetEnvironmentVariable("UseUnsafeMySqlMemoryDb");

        LogTCSection("Environment variables", () =>
        {
            Log($"Parallel throttling: '{parallelThrottling}'");
            Log($"Exclude assemblies: '{excludeAssemblies}'");
            Log($"Build propfile: '{buildpropfile}'");
            Log($"Include categories: '{includeCategories}'");
            Log($"Exclude categories: '{excludeCategories}'");
            Log($"Test timeout: '{testTimeout}'");
            Log($"UseUnsafeMySqlMemoryDb: '{useUnsafeMySqlMemoryDb}'");
        });


        int throttle = GetParallelThrottling(parallelThrottling, useUnsafeMySqlMemoryDb);
        Log($"Parallel throttling: {throttle}");


        string[] testAssemblies = GetTestAssemblies(excludeAssemblies, buildpropfile);


        int timeoutMinutes;

        if (testTimeout != null)
        {
            if (!int.TryParse(testTimeout, out timeoutMinutes))
            {
                throw new ApplicationException($"Couldn't parse timeout: '{testTimeout}'");
            }
        }
        else
        {
            if (useUnsafeMySqlMemoryDb == "" || useUnsafeMySqlMemoryDb == "false" || useUnsafeMySqlMemoryDb == "0")
            {
                timeoutMinutes = 30;
            }
            else
            {
                timeoutMinutes = 10;
            }
        }

        Log($"Using timeout: {timeoutMinutes} minutes.");

        foreach (string filename in Directory.GetFiles(".", "TestResult_*")
            .Select(f => f.StartsWith(@".\") ? f.Substring(2) : f))
        {
            File.Delete(filename);
        }


        List<TestProcess> tests = CreateTestProcesses(testAssemblies, includeCategories, excludeCategories, useUnsafeMySqlMemoryDb);

        Log("Starting tests...");

        RunTestProcesses(tests, throttle, timeoutMinutes);
    }

    private static int GetParallelThrottling(string parallelThrottling, string useUnsafeMySqlMemoryDb)
    {
        // What is the optimal number of concurrent test processes to start? It mainly depends on two things:
        // 1. How long the longest test assembly takes to execute, this is currently the most important limiting factor, something like 4-6 minutes.
        // 2. Secondly, as few concurrent tests as possible should execute to give each test process as much i/o resources as possible.
        // By empirical testing, at least when using memorydb, using half (6) of the cores on a 5930K with the current 45 sorted assemblies,
        // is the most optimal scheduling. Increase this value (by 1 or 2) if the most time consuming test is split up into smaller assemblies.
        // On an older many-core Xeon (build agents) number of logical cores/2 is also a quite good choice.

        int throttle;
        if (string.IsNullOrEmpty(parallelThrottling))
        {
            if (useUnsafeMySqlMemoryDb == "" || useUnsafeMySqlMemoryDb == "false" || useUnsafeMySqlMemoryDb == "0")
            {
                throttle = Environment.ProcessorCount / 2;
            }
            else
            {
                throttle = Environment.ProcessorCount / 2;
            }
        }
        else
        {
            if (!int.TryParse(parallelThrottling, out throttle))
            {
                throw new ApplicationException($"Couldn't parse parallel throttling: '{parallelThrottling}'");
            }

            if (throttle < 1)
            {
                if (useUnsafeMySqlMemoryDb == "" || useUnsafeMySqlMemoryDb == "false" || useUnsafeMySqlMemoryDb == "0")
                {
                    throttle = Environment.ProcessorCount / 2;
                }
                else
                {
                    throttle = Environment.ProcessorCount / 2;
                }
            }
        }

        return throttle;
    }

    private static string[] GetTestAssemblies(string excludeAssemblies, string buildpropfile)
    {
        string[] allfiles = Directory.GetFiles(".", "*Tests.dll", SearchOption.AllDirectories)
            .Select(f => f.StartsWith(@".\") ? f.Substring(2) : f)
            .ToArray();

        Log($"Found {allfiles.Length} test assemblies.");


        string[] excludeassembliesArray = excludeAssemblies?.Split(',') ?? new string[] { };


        string[] files = allfiles
            .Where(f => f.Split(Path.DirectorySeparatorChar).Any(d => d == "bin") &&
                !excludeassembliesArray.Contains(f))
            .ToArray();

        Log($"Found {files.Length} test assemblies in bin folders.");


        string[] excludetestassemblies = files
            .GroupBy(Path.GetFileName)
            .Where(g => g.Count() > 1)
            .SelectMany(g => GetExcludesInGroup(g.ToArray()))
            .ToArray();

        if (excludetestassemblies.Length > 0)
        {
            files = files
                .Where(f =>
                {
                    if (excludetestassemblies.Contains(f))
                    {
                        Log($"  {f}");
                        return false;
                    }

                    return true;
                })
                .ToArray();

            LogTCSection($"Excluding {excludetestassemblies.Length} duplicate test assemblies", excludetestassemblies.OrderBy(t => t));
        }

        // /test-results/test-suite@time = "123.456"

        // To make the total execution time shorter, dynamically retrieve previous
        // test times from the build server (TestResult.txt) and start the slowest
        // tests first (in parallel).
        // The ordering of the tests in the file is performed by the previous build
        // before the file is stored on the build server.
        // We're relying on a custom text file instead of TC's rest api, because when
        // using TC's rest api there's currently no convenient way of retrieving the
        // previous test results faster than what this optimization can improve.
        string[] putfirst = GetExectionOrderBasedOnPreviousTestResult(buildpropfile);

        files = putfirst
            .SelectMany(pf => files
                .Where(f => pf == Path.GetFileNameWithoutExtension(f)))
            .Concat(files
                .Where(f => !putfirst.Contains(Path.GetFileNameWithoutExtension(f))))
            .ToArray();

        LogTCSection($"Testing {files.Length} assemblies", files);

        return files;
    }

    private static string[] GetExectionOrderBasedOnPreviousTestResult(string buildpropfile)
    {
        if (string.IsNullOrEmpty(buildpropfile) || !File.Exists(buildpropfile))
        {
            Log($"Couldn't find Teamcity build properties file: {(buildpropfile == null ? "<null>" : $"'{buildpropfile}'")}");
            return new string[] { };
        }

        Log($"Reading Teamcity build properties file: '{buildpropfile}'");
        string[] rows = File.ReadAllLines(buildpropfile);

        string buildtypeid = FindPropRow(rows, "teamcity.buildType.id", buildpropfile);
        string username = FindPropRow(rows, "teamcity.auth.userId", buildpropfile);
        string password = FindPropRow(rows, "teamcity.auth.password", buildpropfile);
        string configpropfile = FindPropRow(rows, "teamcity.configuration.properties.file", buildpropfile);


        if (string.IsNullOrEmpty(configpropfile) || !File.Exists(configpropfile))
        {
            Log($"Couldn't find Teamcity config properties file: '{configpropfile}'");
            return new string[] { };
        }

        Log($"Reading Teamcity config properties file: {(configpropfile == null ? "<null>" : $"'{configpropfile}'")}");
        rows = File.ReadAllLines(configpropfile);
        string serverurl = FindPropRow(rows, "teamcity.serverUrl", configpropfile);


        string tcurl = $"{serverurl}/httpAuth/repository/download/{buildtypeid}/.lastSuccessful/TestTimes.txt";
        Log($"Teamcity username: '{username}'");
        Log($"Teamcity password: '{password}'");
        Log($"Teamcity url: '{tcurl}'");

        string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
        string[] previousTestResults;

        using (var webclient = new WebClient())
        {
            webclient.Headers[HttpRequestHeader.Authorization] = $"Basic {credentials}";

            try
            {
                string content = webclient.DownloadString(tcurl);

                previousTestResults = content
                    .Split(new char[] { '\r', '\n' })
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Select(t => t.Split('\t')[0])
                    .ToArray();

                Log($"Retrieved {previousTestResults.Length} test results from previous build.");

                return previousTestResults;
            }
            catch (WebException ex)
            {
                Log("Previous build didn't contain any stored TestTimes.txt.");
                Log(ex.Message);
                previousTestResults = new string[] { };
            }
        }

        return previousTestResults;
    }

    private static string FindPropRow(string[] rows, string propname, string filename)
    {
        string value = rows
            .Where(p => p.StartsWith($"{propname}="))
            .Select(p => Regex.Unescape(p.Substring(propname.Length + 1)))
            .FirstOrDefault();
        if (value == null)
        {
            throw new ApplicationException($"Couldn't find '{propname}=' in properties file: '{filename}'");
        }

        return value;
    }

    private static string[] GetExcludesInGroup(string[] filenames)
    {
        if (filenames.Length == 0)
        {
            LogColor("Empty set of test assemblues!", ConsoleColor.Red);
            return new string[] { };
        }

        if (filenames.Length == 1)
        {
            LogColor($"Keeping: '{filenames[0]}'", ConsoleColor.DarkGray);
            return new string[] { };
        }


        // Try to find a single path that matches the file name, based on strong naming convention
        string[] paths = filenames
            .Where(f => f.Split(Path.DirectorySeparatorChar).Any(ff => ff == Path.GetFileNameWithoutExtension(f)))
            .ToArray();

        if (paths.Length == 1)
        {
            LogColor($"Keeping: '{paths[0]}'", ConsoleColor.DarkGray);

            return filenames
                .Where(f => f != paths[0])
                .ToArray();
        }


        // Try to find a single path that matches the file name, based on weak naming convention
        paths = filenames
            .Where(f => f.Split(Path.DirectorySeparatorChar).Any(ff => Path.GetFileNameWithoutExtension(f).EndsWith(ff)))
            .ToArray();

        if (paths.Length == 1)
        {
            LogColor($"Keeping: '{paths[0]}'", ConsoleColor.DarkGray);

            return filenames
                .Where(f => f != paths[0])
                .ToArray();
        }


        LogColor($"Couldn't find any unique test assembly path, using first: '{filenames[0]}'", ConsoleColor.Red);

        return filenames
            .Skip(1)
            .ToArray();
    }

    private static List<TestProcess> CreateTestProcesses(string[] testAssemblies, string includeCategories, string excludeCategories, string useUnsafeMySqlMemoryDb)
    {
        List<TestProcess> tests = new List<TestProcess>();

        string command = @"..\Tools\NUnit\nunit-console.exe";

        foreach (string testAssembly in testAssemblies)
        {
            string args = testAssembly;

            if (!string.IsNullOrEmpty(includeCategories))
            {
                args += $" /include:{includeCategories}";
            }

            if (!string.IsNullOrEmpty(excludeCategories))
            {
                args += $" /exclude:{excludeCategories}";
            }

            string resultfile = $"TestResult_{Path.GetFileNameWithoutExtension(testAssembly)}_{Dns.GetHostName()}";

            args += $" /xml:{resultfile}.xml /out:{resultfile}.out /err:{resultfile}.err /timeout=120000";

            ProcessStartInfo psi = new ProcessStartInfo(command, args)
            {
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            };

            if (useUnsafeMySqlMemoryDb != null)
            {
                psi.EnvironmentVariables["UseUnsafeMySqlMemoryDb"] = useUnsafeMySqlMemoryDb;
            }

            TestProcess test = new TestProcess();

            test.testdll = testAssembly;
            test.started = false;
            test.printed = false;

            test.StartInfo = psi;

            tests.Add(test);
        }

        return tests;
    }

    private static void RunTestProcesses(List<TestProcess> tests, int throttle, int timeoutMinutes)
    {
        if (throttle < 1)
        {
            throw new ApplicationException($"Too low throttle count: {throttle}");
        }

        _runningTests = tests;

        System.Timers.Timer timer = new System.Timers.Timer();
        timer.Elapsed += new System.Timers.ElapsedEventHandler(StatusTimer);
        timer.Interval = 10000;
        timer.Enabled = true;

        do
        {
            foreach (var p in tests)
            {
                p.Refresh();
            }
            while (tests.Any(p => !p.started) && tests.Count(p => p.started && !p.HasExited) < throttle)
            {
                TestProcess process = tests.First(p => !p.started);
                Log($"Starting: '{process.StartInfo.FileName}' '{process.StartInfo.Arguments}'");
                process.Start();
                process.started = true;
            }

            Thread.Sleep(1000);

            foreach (var p in tests)
            {
                p.Refresh();
                if (p.started && !p.HasExited)
                {
                    p.Refresh();
                    TimeSpan ts = DateTime.Now - p.StartTime;
                    if (ts.Minutes >= timeoutMinutes)
                    {
                        Log($"Aborting: {Path.GetFileNameWithoutExtension(p.testdll)}: {ts.ToString()}");
                        KillProcessAndChildren(p.Id);
                    }
                    p.Refresh();
                }
                p.Refresh();
                if (p.started && p.HasExited && !p.printed)
                {
                    TimeSpan ts;
                    p.Refresh();
                    if (p.ExitTime == DateTime.FromFileTime(0))
                    {
                        ts = DateTime.Now - p.StartTime;
                    }
                    else
                    {
                        ts = p.ExitTime - p.StartTime;
                    }

                    Log($"Done: {Path.GetFileNameWithoutExtension(p.testdll)}: {ts.ToString()}");
                    p.printed = true;
                }
            }
        }
        while (tests.Any(p => !p.started || !p.HasExited));
    }

    private static void StatusTimer(object source, System.Timers.ElapsedEventArgs e)
    {
        int countTodo = 0;
        int countRunning = 0;
        int countDone = 0;
        List<string> tests = new List<string>();
        foreach (TestProcess process in _runningTests)
        {
            if (!process.started)
            {
                countTodo++;
            }
            else if (process.started && !process.HasExited)
            {
                countRunning++;
                tests.Add(Path.GetFileNameWithoutExtension(process.testdll));
            }
            else if (process.started && process.HasExited)
            {
                countDone++;
            }
        }

        if (countTodo > 0 || countRunning > 0)
        {
            tests.Sort();

            LogTCSection($"RunningTests: {countRunning} (done: {countDone}, todo: {countTodo})", tests);
        }
    }

    private static void KillLingeringNunitProcesses()
    {
        foreach (Process process in Process.GetProcessesByName("nunit-agent.exe"))
        {
            Log($"Killing: {process.MainModule}");
            process.Kill();
        }
        foreach (Process process in Process.GetProcessesByName("nunit-console.exe"))
        {
            Log($"Killing: {process.MainModule}");
            process.Kill();
        }
    }

    private static void ParseOutput()
    {
        string[] files = Directory.GetFiles(".", "TestResult_*")
            .Select(f => f.StartsWith(@".\") ? f.Substring(2) : f)
            .ToArray();
        foreach (string filename in files)
        {
            if (new FileInfo(filename).Length == 0)
            {
                File.Delete(filename);
            }
        }

        string[] errfiles = Directory.GetFiles(".", "TestResult_*.err")
            .Select(f => f.StartsWith(@".\") ? f.Substring(2) : f)
            .OrderBy(f => f)
            .ToArray();

        string[] outfiles = Directory.GetFiles(".", "TestResult_*.out")
            .Select(f => f.StartsWith(@".\") ? f.Substring(2) : f)
            .OrderBy(f => f)
            .ToArray();

        string[] xmlfiles = Directory.GetFiles(".", "TestResult_*.xml")
            .Select(f => f.StartsWith(@".\") ? f.Substring(2) : f)
            .OrderBy(f => f)
            .ToArray();


        LogTCSection($"Err files: {errfiles.Length}", errfiles.Select(f => $"{f}: {new FileInfo(f).Length}"));
        LogTCSection($"Out files: {outfiles.Length}", outfiles.Select(f => $"{f}: {new FileInfo(f).Length}"));
        LogTCSection($"Xml files: {xmlfiles.Length}", xmlfiles.Select(f => $"{f}: {new FileInfo(f).Length}"));

        CalculateTestTimes(xmlfiles);
    }

    private static void CalculateTestTimes(string[] xmlfiles)
    {
        var tests = xmlfiles
            .Select(x =>
                new
                {
                    filename = Path.GetFileName(x.Split('_')[1]),
                    time = new TimeSpan((long)(double.Parse(XDocument.Load(x)
                        .Elements("test-results")
                        .Elements("test-suite")
                        .Attributes("time")
                        .Single()
                        .Value, CultureInfo.InvariantCulture) * 10000000))
                })
            .ToList();


        TimeSpan totaltime = new TimeSpan(tests.Sum(t => t.time.Ticks));

        string[] rows =
            tests
                .OrderBy(t => -t.time)
                .Select(t => $"{t.filename}\t{t.time}")
                .ToArray();

        LogTCSection($"Total test time: {totaltime}", rows);

        File.WriteAllLines("TestTimes.txt", rows);
    }

    private static void KillProcessAndChildren(int pid)
    {
        ManagementObjectSearcher searcher = new ManagementObjectSearcher($"Select * From Win32_Process Where ParentProcessID={pid}");
        ManagementObjectCollection moc = searcher.Get();
        foreach (ManagementObject mo in moc)
        {
            KillProcessAndChildren(Convert.ToInt32(mo["ProcessID"]));
        }
        try
        {
            Process proc = Process.GetProcessById(pid);
            proc.Kill();
        }
        catch (ArgumentException)
        {
            // Process already exited.
        }
    }

    private static void LogTCSection(string message, Action action)
    {
        ConsoleColor oldColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"##teamcity[blockOpened name='{message}']");
        }
        finally
        {
            Console.ForegroundColor = oldColor;
        }

        action.Invoke();

        oldColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"##teamcity[blockClosed name='{message}']");
        }
        finally
        {
            Console.ForegroundColor = oldColor;
        }
    }

    private static void LogTCSection(string message, IEnumerable<string> collection)
    {
        Console.WriteLine(
            $"##teamcity[blockOpened name='{message}']{Environment.NewLine}" +
            string.Join(string.Empty, collection.Select(t => $"{t}{Environment.NewLine}")) +
            $"##teamcity[blockClosed name='{message}']");
    }

    private static void LogColor(string message, ConsoleColor color)
    {
        ConsoleColor oldColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            Log(message);
        }
        finally
        {
            Console.ForegroundColor = oldColor;
        }
    }

    private static void Log(string message)
    {
        string hostname = Dns.GetHostName();
        Console.WriteLine($"{hostname}: {message}");
    }
}

return Program.Main(Environment.GetCommandLineArgs().Skip(2).ToArray());
