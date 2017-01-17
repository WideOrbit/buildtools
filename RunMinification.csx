// This is a pre-build C# script that minifies js_unminified\*.js to web root folder.

// Run from VS 2015 developer command prompt: csi.exe RunMinification.csx .

// If you add a new javascript to the js_unminified folder, also:
// * Add the minified file in the root folder to the project as "Content", "Do not copy".
//   (To tell VS to include it when publishing the web application)
// * Don't add it to Git.
//   (To prevent Git to indicate a change after each compilation)
// * Add the javascript filename to any build script that verifies that files referenced by projects actually exists in the file system.
//   (In our case it's the PreBuild.csx script that verifies this. If forgotten, it will cause build failures at the verification step in this script)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

public class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            LogColor(@"Usage: csi.exe RunMinification.csx [projectfolder]", ConsoleColor.Red);
            return 1;
        }

        string currentdir = null;
        try
        {
            currentdir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(args[0]);

            RunMinification();
        }
        catch (ApplicationException ex)
        {
            LogColor(ex.Message, ConsoleColor.Red);
            return 1;
        }
        finally
        {
            if (currentdir != null)
            {
                Directory.SetCurrentDirectory(currentdir);
            }
        }

        return 0;
    }

    private static void RunMinification()
    {
        LogColor("***** Minifying javascripts *****", ConsoleColor.Cyan);

        Log("Current Directory: '" + Directory.GetCurrentDirectory() + "'");


        string javaexe;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("dontminify")))
        {
            LogColor("Don't minify javscripts", ConsoleColor.Yellow);
            javaexe = null;
        }
        else
        {
            string[] paths = Environment.GetEnvironmentVariable("path")
                .Split(Path.PathSeparator)
                .Where(p => File.Exists(Path.Combine(p, "java.exe")))
                .ToArray();

            string[] javaexes = ExpandFolderPattern(@"C:\Program Files*\Java\*\bin")
                .Select(p => Path.Combine(p, "java.exe"))
                .Where(p => File.Exists(p))
                .ToArray();

            if (paths.Length > 0)
            {
                javaexe = Path.Combine(paths.First(), "java.exe");
            }
            else if (javaexes.Length > 0)
            {
                javaexe = javaexes.OrderBy(p => p).Last();
            }
            else
            {
                throw new ApplicationException("java.exe not found.");
            }

            Log("Found java.exe here: '" + javaexe + "'");
        }


        string[] unminifiedFiles = Directory.GetFiles("js_unminified", "*.js");
        Log("Found " + unminifiedFiles.Length + " javascripts.");

        foreach (string unminified in unminifiedFiles)
        {
            string minified = Path.GetFileName(unminified);
            string minified_gcc = Path.GetFileNameWithoutExtension(minified) + "_gcc.js";
            string minified_yui = Path.GetFileNameWithoutExtension(minified) + "_yui.js";
            string gccargs = @"-jar compiler.jar --compilation_level SIMPLE_OPTIMIZATIONS --js " + unminified + " --js_output_file " + minified_gcc;
            string yuiargs = @"-jar yuicompressor-2.4.8.jar " + unminified + " -o " + minified_yui;


            long sizebefore = new FileInfo(unminified).Length;
            if (sizebefore == 0)
            {
                throw new ApplicationException(Path.GetFullPath(unminified) + " 0 bytes!");
            }

            if (javaexe != null)
            {
                Log("Executing Google closure compiler...");
                int result = RunCommand(javaexe, gccargs);

                if (result != 0)
                {
                    throw new ApplicationException("Google closure compiler error: " + result);
                }

                long sizeafter_gcc = new FileInfo(minified_gcc).Length;
                if (sizeafter_gcc == 0)
                {
                    throw new ApplicationException(Path.GetFullPath(minified_gcc) + " 0 bytes!");
                }

                LogColor("Google closure compiler: " + minified + " minified from " + sizebefore + " -> " + sizeafter_gcc + ": " +
                    string.Format("{0:+0.00;-0.00}", (1 - sizeafter_gcc * 1.0 / sizebefore) * -100) + "%", ConsoleColor.Green);


                Log("Executing YUI compressor...");
                result = RunCommand(javaexe, yuiargs);

                if (result != 0)
                {
                    throw new ApplicationException("YUI compressor error: " + result);
                }

                long sizeafter_yui = new FileInfo(minified_yui).Length;
                if (sizeafter_yui == 0)
                {
                    throw new ApplicationException(Path.GetFullPath(minified_yui) + " 0 bytes!");
                }

                LogColor("YUI compressor: " + minified + " minified from " + sizebefore + " -> " + sizeafter_yui + ": " +
                    string.Format("{0:+0.00;-0.00}", (1 - sizeafter_yui * 1.0 / sizebefore) * -100) + "%", ConsoleColor.Green);


                if (sizeafter_gcc < sizeafter_yui)
                {
                    Log(minified + ": Selecting Google closure compiler: " + minified_gcc + " -> " + minified + "...");
                    if (File.Exists(minified))
                    {
                        File.Delete(minified);
                    }
                    File.Move(minified_gcc, minified);
                    Log("Moved " + minified_gcc + " -> " + minified + "!");

                    File.Delete(minified_yui);
                }
                else
                {
                    Log(minified + ": Selecting YUI compressor: " + minified_yui + " -> " + minified + "...");
                    if (File.Exists(minified))
                    {
                        File.Delete(minified);
                    }
                    File.Move(minified_yui, minified);
                    Log("Moved " + minified_yui + " -> " + minified + "!");

                    File.Delete(minified_gcc);
                }
            }
            else
            {
                Log("Copying " + unminified + " -> " + minified + "...");
                File.Copy(unminified, minified, true);
                LogColor("Copied " + unminified + " -> " + minified + "!", ConsoleColor.Green);
            }

            long sizeafter = new FileInfo(minified).Length;
            if (sizeafter == 0)
            {
                throw new ApplicationException(Path.GetFullPath(minified) + " 0 bytes!");
            }


            Log(minified + " minified from " + sizebefore + " -> " + sizeafter + ": " +
                string.Format("{0:+0.00;-0.00}", (1 - sizeafter * 1.0 / sizebefore) * -100) + "%");

            string cleanname = string.Join(string.Empty, minified.ToCharArray().Where(c => char.IsLetterOrDigit(c)));

            Console.WriteLine("##teamcity[buildStatisticValue key='" + cleanname + "Size' value='" + sizeafter + "']");

            if (sizeafter > sizebefore)
            {
                throw new ApplicationException(Path.GetFullPath(minified) + " got bigger!");
            }
        }
    }

    private static List<string> ExpandFolderPattern(string path)
    {
        List<string> returnpaths = new List<string>();
        bool firstlevel = true;

        foreach (string pattern in path.Split(Path.DirectorySeparatorChar))
        {
            if (firstlevel)
            {
                firstlevel = false;
                if (Path.IsPathRooted(pattern))
                {
                    try
                    {
                        Directory.GetDirectories(pattern + Path.DirectorySeparatorChar);
                    }
                    catch (DirectoryNotFoundException)
                    {
                        continue;
                    }
                    returnpaths.Add(pattern);
                }
                else
                {
                    returnpaths.AddRange(Directory.GetDirectories(".", pattern));
                }
            }
            else
            {
                foreach (string currentpath in returnpaths.ToArray())
                {
                    returnpaths.Remove(currentpath);
                    try
                    {
                        returnpaths.AddRange(Directory.GetDirectories(currentpath + Path.DirectorySeparatorChar, pattern));
                    }
                    catch (DirectoryNotFoundException)
                    {
                        continue;
                    }
                }
            }
        }

        if (Environment.GetEnvironmentVariable("verbose") == "true")
        {
            Log("Expanding: '" + path + "'");
            foreach (string returnpath in returnpaths)
            {
                Log("  '" + returnpath + "'");
            }
        }

        return returnpaths;
    }

    private static int RunCommand(string exefile, string args)
    {
        Process process = new Process();
        process.StartInfo = new ProcessStartInfo(exefile, args)
        {
            UseShellExecute = false
        };

        process.Start();
        process.WaitForExit();

        return process.ExitCode;
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
        Console.WriteLine(hostname + ": " + message);
    }
}

#if !DEBUG
return Program.Main(Environment.GetCommandLineArgs().Skip(2).ToArray());
#endif
