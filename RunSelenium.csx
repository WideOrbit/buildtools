// This is a script that runs the selenium tests, if it wasn't obvious enough.

#r "System.ServiceProcess.dll"

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceProcess;
using System.Text;
using System.Threading;

public class Program
{
    static string _toolsfolder = "Tools";
    static string _zipexe = Path.Combine(_toolsfolder, "7z.exe");

    public static int Main(string[] args)
    {
        int result = 0;

        if (args.Length != 1 || (
            args[0] != "SetupTestEnvironment" &&
            args[0] != "RunTests" &&
            args[0] != "GenerateReport"))
        {
            Log(
@"Usage: csi.exe RunSelenium.csx [SetupTestEnvironment|RunTests|GenerateReport]

Environment variables:
  When environment is setup:
    TestParameterFile/TestParameterFileContent (optional)
    CreateAdditionalFolders (optional)
    SeleniumLogfile
  When running tests:
    NUnitReportFilename
    TestAssembly
    IncludeCategories (optional)
    LogFolder              --> Artifact: Logfiles.zip
    ScreenshotFolder       --> Artifact: Screenshots.zip
  When generating report:
    DoxygenArchive
    DoxygenConfigfile
    DoxygenOutputFolder    --> Artifact: Docs.zip");

            result = 1;
        }
        else
        {
            try
            {
                CheckTools();

                switch (args[0])
                {
                    case "SetupTestEnvironment":
                        SetupTestEnvironment();
                        break;
                    case "RunTests":
                        RunSeleniumTests();
                        break;
                    case "GenerateReport":
                        GenerateReport();
                        break;
                }
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

    private static void CheckTools()
    {
        string[] toolsfiles =
        {
            "7z.exe",
            "7z.dll",
            "chromedriver.exe",
            "doxygen.zip",
            "jre.7z",
            "nssm.exe",
            "NUnit.7z",
            "selenium-server-standalone.jar",
        };

        List<string> missingtools = new List<string>();

        foreach (string filename in toolsfiles)
        {
            if (!File.Exists(Path.Combine(_toolsfolder, filename)))
            {
                missingtools.Add(Path.Combine(_toolsfolder, filename));
            }
        }

        if (missingtools.Count > 0)
        {
            throw new ApplicationException($"Missing tool files: '{string.Join("', '", missingtools)}'");
        }
    }

    private static void SetupTestEnvironment()
    {
        string testParameterFile = Environment.GetEnvironmentVariable("TestParameterFile");
        if (string.IsNullOrEmpty(testParameterFile))
        {
            Log("TestParameterFile environment variable not set!");
        }
        else
        {
            string content = Environment.GetEnvironmentVariable("TestParameterFileContent");
            if (string.IsNullOrEmpty(content))
            {
                Log("TestParameterFileContent environment variable not set!");
            }
            else
            {
                Log($"Writing to: '{testParameterFile}'");
                File.WriteAllText(testParameterFile, content);
            }
        }


        string createFolders = Environment.GetEnvironmentVariable("CreateFolders");
        if (string.IsNullOrEmpty(createFolders))
        {
            Log("CreateFolders environment variable not set!");
        }
        else
        {
            foreach (string folder in createFolders.Split(','))
            {
                if (Directory.Exists(createFolders))
                {
                    Log($"Folder already exists: '{folder}'");
                }
                else
                {
                    Log($"Creating folder: '{folder}'");
                    Directory.CreateDirectory(folder);
                }
            }
        }

        ReinstallSeleniumService();
    }

    private static void ArchiveOldLogFiles()
    {
        string logfolder = Environment.GetEnvironmentVariable("Logfolder");
        if (string.IsNullOrEmpty(logfolder))
        {
            Log("Logfolder environment variable not set!");
            return;
        }

        if (!Directory.Exists(logfolder))
        {
            Log($"Logfolder not found, creating: '{logfolder}'");
            Directory.CreateDirectory(logfolder);
            return;
        }

        string archivefolder = Path.Combine(Path.GetDirectoryName(logfolder), "oldlogs");
        if (!Directory.Exists(archivefolder))
        {
            Log($"Archive folder not found, creating: '{archivefolder}'");
            Directory.CreateDirectory(archivefolder);
        }

        string[] files = Directory.GetFiles(logfolder, "*");

        LogTCSection($"Archiving {files.Length} logfiles", () =>
        {
            foreach (string filename in files)
            {
                string targetfile = Path.Combine(archivefolder, Path.GetFileName(filename));
                if (File.Exists(targetfile))
                {
                    for (int i = 0; i < 10000; i++)
                    {
                        targetfile = Path.Combine(archivefolder, $"{Path.GetFileName(filename)}.{i + 1}");
                        if (!File.Exists(targetfile))
                        {
                            break;
                        }
                    }
                }

                Log($"Moving: '{filename}' -> '{targetfile}'");
                try
                {
                    File.Move(filename, targetfile);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                {
                    Log($"Couldn't move logfile: {ex.Message.Trim()}");
                }
            }
        });
    }

    private static void ReinstallSeleniumService()
    {
        string servicename = "Selenium-Server";

        if (ServiceController.GetServices().Any(s => s.ServiceName == servicename))
        {
            Log($"Stopping service: '{servicename}'");
            StopService(servicename);

            Log($"Uninstalling service: '{servicename}'");
            RunCommand("sc.exe", $"delete {servicename}");
        }


        ArchiveOldLogFiles();


        string jrefolder = @"\jre";
        string seleniumfolder = @"\Selenium";

        string curdir = Environment.CurrentDirectory;

        jrefolder = jrefolder[0] == Path.DirectorySeparatorChar ?
            Path.Combine(curdir.Substring(0, 2) + jrefolder) :
            Path.Combine(curdir, jrefolder);

        seleniumfolder = seleniumfolder[0] == Path.DirectorySeparatorChar ?
            Path.Combine(curdir.Substring(0, 2) + seleniumfolder) :
            Path.Combine(curdir, seleniumfolder);

        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (Directory.Exists(jrefolder))
                {
                    Log($"Removing directory: '{jrefolder}'");
                    Directory.Delete(jrefolder, true);
                }
                if (Directory.Exists(seleniumfolder))
                {
                    Log($"Removing directory: '{seleniumfolder}'");
                    Directory.Delete(seleniumfolder, true);
                }
            }
            catch (Exception ex) when (
                ex is DirectoryNotFoundException ||
                ex is IOException ||
                ex is UnauthorizedAccessException)
            {
                LogColor(ex.Message.Trim(), ConsoleColor.Yellow);
                Thread.Sleep(2000);
            }
        }
        if (Directory.Exists(jrefolder))
        {
            throw new ApplicationException($"Couldn't remove directory: '{jrefolder}'");
        }
        if (Directory.Exists(seleniumfolder))
        {
            throw new ApplicationException($"Couldn't remove directory: '{seleniumfolder}'");
        }


        Log($"Reinstalling {servicename} service to: '{seleniumfolder}'");

        string jreArchive = Path.Combine(_toolsfolder, "jre.7z");
        string outputPath = Path.GetDirectoryName(jrefolder);
        LogTCSection($"Extracting JRE: {jreArchive} -> {outputPath}...", () =>
        {
            RunCommand(_zipexe, $"x \"-o{outputPath}\" {jreArchive}");
        });

        string javaexe = Path.Combine(jrefolder, "bin", "java.exe");
        if (!File.Exists(javaexe))
        {
            throw new ApplicationException($"Couldn't find extracted file: '{javaexe}'");
        }


        Log($"Creating directory: '{seleniumfolder}'");
        Directory.CreateDirectory(seleniumfolder);

        string sourcefile, targetfile;

        sourcefile = Path.Combine(_toolsfolder, "chromedriver.exe");
        targetfile = Path.Combine(seleniumfolder, "chromedriver.exe");
        Log($"Copying: '{sourcefile}' -> '{seleniumfolder}'");
        File.Copy(sourcefile, targetfile);

        sourcefile = Path.Combine(_toolsfolder, "nssm.exe");
        targetfile = Path.Combine(seleniumfolder, "nssm.exe");
        Log($"Copying: '{sourcefile}' -> '{seleniumfolder}'");
        File.Copy(sourcefile, targetfile);

        sourcefile = Path.Combine(_toolsfolder, "selenium-server-standalone.jar");
        targetfile = Path.Combine(seleniumfolder, "selenium-server-standalone.jar"); ;
        Log($"Copying: '{sourcefile}' -> '{seleniumfolder}'");
        File.Copy(sourcefile, targetfile);


        string nssmexe = Path.Combine(seleniumfolder, @"nssm.exe");


        Log($"Installing service: '{servicename}'");
        string jar = Path.Combine(seleniumfolder, @"selenium-server-standalone.jar");
        string args = $"install {servicename} {javaexe} -jar {jar}";
        RunCommand(nssmexe, args);


        Log("Setting up nssm service.");

        args = $"set {servicename} Start SERVICE_DEMAND_START";
        RunCommand(nssmexe, args);

        string path = $"{seleniumfolder}{Path.PathSeparator}{Environment.GetFolderPath(Environment.SpecialFolder.Windows)}{Path.PathSeparator}{Environment.SystemDirectory}";
        args = $"set {servicename} AppEnvironmentExtra Path={path}";
        RunCommand(nssmexe, args);

        string seleniumLogfile = Environment.GetEnvironmentVariable("SeleniumLogfile");
        if (string.IsNullOrEmpty(seleniumLogfile))
        {
            throw new ApplicationException("SeleniumLogfile environment variable not set!");
        }

        args = $"set {servicename} AppStdout {seleniumLogfile}";
        RunCommand(nssmexe, args);

        args = $"set {servicename} AppStderr {seleniumLogfile}";
        RunCommand(nssmexe, args);


        Log($"Starting service: '{servicename}'");
        StartService(servicename);
    }

    private static void CopyFolder(string sourcefolder, string targetfolder)
    {
        string[] files = Directory.GetFiles(sourcefolder, "*");
        Log($"Found {files.Length} files in: '{sourcefolder}'");
        foreach (string filename in files)
        {
            string targetfile = Path.Combine(targetfolder, Path.GetFileName(filename));
            Log($"Copying '{filename}' -> '{targetfile}'");
            File.Copy(filename, targetfile);
        }
    }

    private static void StopService(string servicename)
    {
        ServiceController service = new ServiceController(servicename);
        if (service.StartType != ServiceStartMode.Disabled && service.Status != ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, new TimeSpan(0, 0, 30));
        }
    }

    private static void StartService(string servicename)
    {
        ServiceController service = new ServiceController(servicename);
        if (service.StartType != ServiceStartMode.Disabled && service.Status != ServiceControllerStatus.Running)
        {
            try
            {
                service.Start();
            }
            catch (InvalidOperationException)
            {
                LogColor($"Please run as admin, or start the {servicename} service manually.", ConsoleColor.Red);
                throw;
            }
            service.WaitForStatus(ServiceControllerStatus.Running, new TimeSpan(0, 0, 30));
        }
    }

    private static void RunSeleniumTests()
    {
        Log($"Current Directory: '{Directory.GetCurrentDirectory()}'");

        Log("Running nunit-console.exe...");


        string NUnitReportFilename = Environment.GetEnvironmentVariable("NUnitReportFilename");
        if (string.IsNullOrEmpty(NUnitReportFilename))
        {
            throw new ApplicationException("NUnitReportFilename environment variable not set!");
        }
        string hostname = Dns.GetHostName();
        string xmlfile = $"TestResult_{NUnitReportFilename}_{hostname}.xml";
        Log($"Xmlfile: '{xmlfile}'");


        string testAssembly = Environment.GetEnvironmentVariable("TestAssembly");
        if (string.IsNullOrEmpty(testAssembly))
        {
            throw new ApplicationException("TestAssembly environment variable not set!");
        }
        if (!File.Exists(testAssembly))
        {
            throw new ApplicationException($"Test assembly not found: '{testAssembly}'");
        }


        string IncludeCategories = Environment.GetEnvironmentVariable("IncludeCategories");
        string nunitargs = testAssembly;
        if (string.IsNullOrEmpty(IncludeCategories))
        {
            LogColor("IncludeCategories environment variable not set, running all tests!", ConsoleColor.Yellow);

            nunitargs += $" /xml:{xmlfile} /out:TestResult.out /err:TestResult.err";
        }
        else
        {
            Log($"Categories: '{IncludeCategories}'");

            nunitargs += $" /include:{IncludeCategories} /xml:{xmlfile} /out:TestResult.out /err:TestResult.err";
        }


        string nunitArchive = Path.Combine(_toolsfolder, "NUnit.7z");
        LogTCSection($"Extracting NUnit: {nunitArchive}...", () =>
        {
            RunCommand(_zipexe, $"x {nunitArchive}");
        });

        string nunitconsoleexe = @"NUnit\nunit-console.exe";
        if (!File.Exists(nunitconsoleexe))
        {
            throw new ApplicationException($"Couldn't find extracted file: '{nunitconsoleexe}'");
        }

        try
        {
            RunCommand(nunitconsoleexe, nunitargs);
        }
        catch (ApplicationException ex)
        {
            Log($"Tests failed: {ex.Message.Trim()}");
        }
        finally
        {
            KillLingeringNunitProcesses();
        }

        string[] files = Directory.GetFiles(".");
        LogTCSection($"{files.Length} files...", () =>
        {
            foreach (string filename in files)
            {
                Log($"'{filename}'");
            }
        });

        LogTCSection("-=-=- TestResult.out -=-=-", () =>
        {
            string testresult_out = File.ReadAllText("TestResult.out");
            Console.WriteLine(testresult_out);
        });

        LogTCSection($"-=-=- {xmlfile} -=-=-", () =>
        {
            string testresult_xml = File.ReadAllText(xmlfile);
            Console.WriteLine(testresult_xml);
        });

        LogTCSection("-=-=- TestResult.err -=-=-", () =>
        {
            string testresult_err = File.ReadAllText("TestResult.err");
            Console.WriteLine(testresult_err);
        });

        GatherFilesToArchive("ScreenshotFolder", "Screenshots.zip", false);
        GatherFilesToArchive("LogFolder", "Logfiles.zip", true);
    }

    private static void GenerateReport()
    {
        Log($"Current Directory: '{Directory.GetCurrentDirectory()}'");

        string doxygenArchive = Path.Combine(_toolsfolder, "doxygen.zip");
        LogTCSection($"Extracting Doxygen: {doxygenArchive}...", () =>
        {
            RunCommand(_zipexe, $"x {doxygenArchive}");
        });

        string doxygenexe = @"doxygen\bin\doxygen.exe";
        if (!File.Exists(doxygenexe))
        {
            throw new ApplicationException($"Couldn't find extracted file: '{doxygenexe}'");
        }


        string doxygenConfigfile = Environment.GetEnvironmentVariable("DoxygenConfigfile");
        if (string.IsNullOrEmpty(doxygenConfigfile))
        {
            throw new ApplicationException("DoxygenConfigfile environment variable not set!");
        }
        if (!File.Exists(doxygenConfigfile))
        {
            throw new ApplicationException($"DoxygenConfigfile not found: '{doxygenConfigfile}'");
        }


        RunCommand(doxygenexe, doxygenConfigfile);


        GatherFilesToArchive("DoxygenOutputFolder", "Docs.zip", true);
    }

    private static void GatherFilesToArchive(string environmentvariable, string targetzipfile, bool throwIfEmpty)
    {
        string sourcefolder = Environment.GetEnvironmentVariable(environmentvariable);
        if (string.IsNullOrEmpty(sourcefolder))
        {
            throw new ApplicationException($"{environmentvariable} environment variable not set!");
        }
        if (!Directory.Exists(sourcefolder))
        {
            throw new ApplicationException($"{environmentvariable} not found: '{sourcefolder}'");
        }

        string[] files = Directory.GetFiles(sourcefolder, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            string message = $"No files found in folder: '{sourcefolder}'";
            if (throwIfEmpty)
            {
                throw new ApplicationException(message);
            }
            else
            {
                Log(message);
                return;
            }
        }

        string filepath = Path.Combine(sourcefolder, "*");
        LogTCSection($"Gathering {files.Length} files: {filepath} -> {targetzipfile}", () =>
        {
            string curdir = null;
            try
            {
                curdir = Directory.GetCurrentDirectory();
                Directory.SetCurrentDirectory(sourcefolder);
                RunCommand(Path.Combine(curdir, _zipexe), $"a -mx9 \"{Path.Combine(curdir, targetzipfile)}\" -ssw *");
            }
            catch (ApplicationException ex)
            {
                Log($"Couldn't gather all files, some files were probably locked: {ex.Message.Trim()}");
            }
            finally
            {
                if (curdir != null)
                {
                    Directory.SetCurrentDirectory(curdir);
                }
            }
        });
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

    private static void RunCommand(string exefile, string args)
    {
        Log($"Running: '{exefile}' '{args}'");

        Process process = new Process();
        process.StartInfo = new ProcessStartInfo(exefile, args)
        {
            UseShellExecute = false
        };

        process.Start();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new ApplicationException($"Couldn't execute: '{exefile}', args: '{args}'");
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
