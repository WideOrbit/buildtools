using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

public class Program
{
    private static int _totalresult;
    private static int _testresult;

    public static int Main(string[] args)
    {
        _totalresult = 0;
        bool result;

        Console.Clear();

        _testresult = 0;
        Test1();
        AssertTest("Test1");

        _testresult = 0;
        Test2();
        AssertTest("Test2");

        _testresult = 0;
        Test3();
        AssertTest("Test3");

        _testresult = 0;
        Test4();
        AssertTest("Test4");

        _testresult = 0;
        Test5();
        AssertTest("Test5");

        _testresult = 0;
        Test6();
        AssertTest("Test6");

        _testresult = 0;
        Test7();
        AssertTest("Test7");

        _testresult = 0;
        Test8();
        AssertTest("Test8");

        _testresult = 0;
        Test9();
        AssertTest("Test9");

        _testresult = 0;
        Test10();
        AssertTest("Test10");


        if (_totalresult == 0)
        {
            LogColor("***** SUCCESS *****", ConsoleColor.Green);
        }
        else
        {
            LogColor("***** FAIL *****", ConsoleColor.Red);
        }

        return _totalresult;
    }

    private static void AssertTest(string testname)
    {
        if (_testresult == 0)
        {
            LogColor(testname + " Success", ConsoleColor.Green);
        }
        else
        {
            LogColor(testname + " Fail", ConsoleColor.Red);
            _totalresult = 1;
        }
        Log(string.Empty);
    }

    private static void AssertInt(int compare1, int compare2)
    {
        if (compare1 == compare2)
        {
            LogColor("" + compare1 + " == " + compare2, ConsoleColor.Green);
        }
        else
        {
            LogColor("" + compare1 + " != " + compare2, ConsoleColor.Red);
            _testresult = 1;
        }
    }

    private static void AssertTCString(string compare1, string compare2)
    {
        string tcvalue = compare1.Replace("\r", string.Empty).Split('\n').Single(r => r.StartsWith("##"));

        if (tcvalue == compare2)
        {
            LogColor(tcvalue + " == " + compare2, ConsoleColor.Green);
        }
        else
        {
            LogColor(tcvalue + " != " + compare2, ConsoleColor.Red);
            _testresult = 1;
        }
    }

    private static void Test1()
    {
        LogColor("***** Running Test1 *****", ConsoleColor.Cyan);

        string output = RunScript();
        AssertTCString(output, "##teamcity[buildNumber '1.0']");
    }

    private static void Test2()
    {
        LogColor("***** Running Test2 *****", ConsoleColor.Cyan);

        string propfile1 = "propfile1.txt";
        File.WriteAllLines(propfile1, new string[] { });

        string output = RunScript();
        AssertTCString(output, "##teamcity[buildNumber '1.0']");

        File.Delete(propfile1);
    }

    private static void Test3()
    {
        LogColor("***** Running Test3 *****", ConsoleColor.Cyan);

        string propfile1 = "propfile1.txt";
        string propfile2 = "propfile2.txt";
        File.WriteAllLines(propfile1, new[] { "teamcity.configuration.properties.file=" + propfile2 });

        LogColor(File.ReadAllText(propfile1), ConsoleColor.Magenta);

        string output = RunScript();
        AssertTCString(output, "##teamcity[buildNumber '1.0']");

        File.Delete(propfile1);
    }

    private static void Test4()
    {
        LogColor("***** Running Test4 *****", ConsoleColor.Cyan);

        string propfile1 = "propfile1.txt";
        string propfile2 = "propfile2.txt";
        File.WriteAllLines(propfile1, new[] { "teamcity.configuration.properties.file=" + propfile2 });
        File.WriteAllLines(propfile2, new[] { "key1=value1", "key2=value2" });

        LogColor(File.ReadAllText(propfile1), ConsoleColor.Magenta);
        LogColor(File.ReadAllText(propfile2), ConsoleColor.Magenta);

        string output = RunScript();
        AssertTCString(output, "##teamcity[buildNumber '1.0']");

        File.Delete(propfile1);
        File.Delete(propfile2);
    }

    private static void Test5()
    {
        LogColor("***** Running Test5 *****", ConsoleColor.Cyan);

        string propfile1 = "propfile1.txt";
        string propfile2 = "propfile2.txt";
        File.WriteAllLines(propfile1, new[] { "teamcity.configuration.properties.file=" + propfile2 });
        File.WriteAllLines(propfile2, new[] { "key1=value1", "key2=value2" });

        LogColor(File.ReadAllText(propfile1), ConsoleColor.Magenta);
        LogColor(File.ReadAllText(propfile2), ConsoleColor.Magenta);

        string output = RunScript(new Dictionary<string, string> { ["TEAMCITY_BUILD_PROPERTIES_FILE"] = "" });
        AssertTCString(output, "##teamcity[buildNumber '1.0']");

        File.Delete(propfile1);
        File.Delete(propfile2);
    }

    private static void Test6()
    {
        LogColor("***** Running Test6 *****", ConsoleColor.Cyan);

        string propfile1 = "propfile1.txt";
        string propfile2 = "propfile2.txt";
        File.WriteAllLines(propfile1, new[] { "teamcity.configuration.properties.file=" + propfile2 });
        File.WriteAllLines(propfile2, new[] { "key1=value1", "key2=value2" });

        LogColor(File.ReadAllText(propfile1), ConsoleColor.Magenta);
        LogColor(File.ReadAllText(propfile2), ConsoleColor.Magenta);

        string output = RunScript(new Dictionary<string, string> { ["TEAMCITY_BUILD_PROPERTIES_FILE"] = propfile1 });
        AssertTCString(output, "##teamcity[buildNumber '1.0']");

        File.Delete(propfile1);
        File.Delete(propfile2);
    }

    private static void Test7()
    {
        LogColor("***** Running Test7 *****", ConsoleColor.Cyan);

        string propfile1 = "propfile1.txt";
        string propfile2 = "propfile2.txt";
        File.WriteAllLines(propfile1, new[] { "teamcity.configuration.properties.file=" + propfile2 });
        File.WriteAllLines(propfile2, new[] { "key1=value1", "teamcity.build.branch=branchname1", "vcsroot.branch=branchname2", "key2=value2" });

        LogColor(File.ReadAllText(propfile1), ConsoleColor.Magenta);
        LogColor(File.ReadAllText(propfile2), ConsoleColor.Magenta);

        string output = RunScript(new Dictionary<string, string> { ["TEAMCITY_BUILD_PROPERTIES_FILE"] = propfile1 });
        AssertTCString(output, "##teamcity[buildNumber '1.0']");

        File.Delete(propfile1);
        File.Delete(propfile2);
    }

    private static void Test8()
    {
        LogColor("***** Running Test8 *****", ConsoleColor.Cyan);

        string propfile1 = "propfile1.txt";
        string propfile2 = "propfile2.txt";
        File.WriteAllLines(propfile1, new[] { "teamcity.configuration.properties.file=" + propfile2 });
        File.WriteAllLines(propfile2, new[] { "key1=value1", "vcsroot.branch=branchname3", "key2=value2" });

        LogColor(File.ReadAllText(propfile1), ConsoleColor.Magenta);
        LogColor(File.ReadAllText(propfile2), ConsoleColor.Magenta);

        string output = RunScript(new Dictionary<string, string> { ["TEAMCITY_BUILD_PROPERTIES_FILE"] = propfile1 });
        AssertTCString(output, "##teamcity[buildNumber '1.0']");

        File.Delete(propfile1);
        File.Delete(propfile2);
    }

    private static void Test9()
    {
        LogColor("***** Running Test9 *****", ConsoleColor.Cyan);

        string propfile1 = "propfile1.txt";
        string propfile2 = "propfile2.txt";
        File.WriteAllLines(propfile1, new[] { "teamcity.configuration.properties.file=" + propfile2 });
        File.WriteAllLines(propfile2, new[] { "key1=value1", "teamcity.build.branch=1.2.3", "key2=value2" });

        LogColor(File.ReadAllText(propfile1), ConsoleColor.Magenta);
        LogColor(File.ReadAllText(propfile2), ConsoleColor.Magenta);

        string output = RunScript(new Dictionary<string, string>
        {
            ["TEAMCITY_BUILD_PROPERTIES_FILE"] = propfile1,
            ["BUILD_NUMBER"] = "789"
        });
        AssertTCString(output, "##teamcity[buildNumber '1.2.3.789']");

        File.Delete(propfile1);
        File.Delete(propfile2);
    }

    private static void Test10()
    {
        LogColor("***** Running Test10 *****", ConsoleColor.Cyan);

        string propfile1 = "propfile1.txt";
        string propfile2 = "propfile2.txt";
        File.WriteAllLines(propfile1, new[] { "teamcity.configuration.properties.file=" + propfile2 });
        File.WriteAllLines(propfile2, new[] { "key1=value1", "vcsroot.branch=1.2.x", "key2=value2" });

        LogColor(File.ReadAllText(propfile1), ConsoleColor.Magenta);
        LogColor(File.ReadAllText(propfile2), ConsoleColor.Magenta);

        string output = RunScript(new Dictionary<string, string>
        {
            ["TEAMCITY_BUILD_PROPERTIES_FILE"] = propfile1,
            ["BUILD_NUMBER"] = "789"
        });
        AssertTCString(output, "##teamcity[buildNumber '1.2.789']");

        File.Delete(propfile1);
        File.Delete(propfile2);
    }

    private static string RunScript(Dictionary<string, string> environmentVariables = null)
    {
        Process process = new Process();
        process.StartInfo = new ProcessStartInfo("csi.exe", "SetVersion.csx")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        if (environmentVariables != null)
        {
            LogColor(string.Join(Environment.NewLine, environmentVariables) + Environment.NewLine, ConsoleColor.Magenta);

            foreach (var variable in environmentVariables)
            {
                process.StartInfo.EnvironmentVariables[variable.Key] = variable.Value;
            }
        }

        process.Start();
        process.WaitForExit();

        string output = process.StandardOutput.ReadToEnd();
        Log(output);

        AssertInt(process.ExitCode, 0);

        return output;
    }

    private static void Log(string message)
    {
        Console.WriteLine(message);
    }

    private static void LogColor(string message, ConsoleColor color)
    {
        ConsoleColor oldcolor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
        }
        finally
        {
            Console.ForegroundColor = oldcolor;
        }
    }
}

return Program.Main(Environment.GetCommandLineArgs().Skip(2).ToArray());
