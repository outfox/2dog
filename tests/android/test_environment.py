from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[2]


class NativeEnvironment(unittest.TestCase):
    def test_shared_helper_sets_and_overwrites_native_environment(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            shutil.copyfile(REPO / "global.json", root / "global.json")
            project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
            properties = ET.SubElement(project, "PropertyGroup")
            for name, value in (("TargetFramework", "net10.0"), ("OutputType", "Exe")):
                ET.SubElement(properties, name).text = value
            items = ET.SubElement(project, "ItemGroup")
            ET.SubElement(items, "Compile", Include=str(REPO / "twodog.engine/NativeEnvironment.cs"))
            ET.ElementTree(project).write(root / "environment.csproj", encoding="utf-8")
            (root / "NuGet.Config").write_text(
                '<configuration><packageSources><clear /></packageSources></configuration>', encoding="utf-8",
            )
            (root / "Program.cs").write_text('''
using System;
using System.Runtime.InteropServices;

class Program
{
    [DllImport("libc")]
    private static extern IntPtr getenv(string name);

    static void Main()
    {
        const string name = "TWODOG_NATIVE_ENVIRONMENT_TEST";
        foreach (var value in new[] { "first", "overwritten" })
        {
            twodog.NativeEnvironment.SetVariable(name, value);
            var observed = OperatingSystem.IsWindows()
                ? Environment.GetEnvironmentVariable(name)
                : Marshal.PtrToStringUTF8(getenv(name));
            if (observed != value) throw new Exception("Native environment did not receive " + value);
        }
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                twodog.NativeEnvironment.SetVariable("INVALID=NAME", "value");
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("native environment verified");
                return;
            }
            throw new Exception("Failed native setenv was not reported");
        }
        Console.WriteLine("native environment verified");
    }
}
''', encoding="utf-8")
            result = subprocess.run(
                ["dotnet", "run", "--project", str(root / "environment.csproj"), "--configuration", "Release",
                 "--property:NuGetAudit=false"],
                cwd=root, capture_output=True, text=True, timeout=60,
            )
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertIn("native environment verified", result.stdout)


if __name__ == "__main__":
    unittest.main()
