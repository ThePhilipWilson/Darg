using Workbench.Core;

namespace Workbench.Core.Tests;

public class ShellsTests
{
    [Fact]
    public void WslListIsUtf16NoiseFreeAndSkipsDockersOwn()
    {
        // wsl --list --quiet read as the wrong encoding leaves NULs; Docker Desktop's distros aren't for people
        var list = Shells.ParseWslList("Ubuntu\0\r\n\0docker-desktop\r\ndocker-desktop-data\r\nDebian\r\n\r\n");
        Assert.Equal(["Ubuntu", "Debian"], list);
    }

    [Fact]
    public void UnixShellsKeepFirstOfEachNameAndDropNonInteractive()
    {
        string[] present = ["/opt/homebrew/bin/fish", "/bin/zsh", "/bin/bash", "/usr/bin/bash", "/bin/sh", "/usr/sbin/nologin"];
        var list = Shells.ParseUnixShells(
            ["/opt/homebrew/bin/fish", "/bin/sh", "/bin/zsh", "/bin/bash", "/usr/bin/bash", "/usr/sbin/nologin", "/bin/tcsh"],
            present.Contains);
        Assert.Equal(["/opt/homebrew/bin/fish", "/bin/zsh", "/bin/bash"], list);
    }

    [Fact]
    public void OnlyShIsStillAShell() =>
        Assert.Equal(["/bin/sh"], Shells.ParseUnixShells(["/bin/sh", "/usr/sbin/nologin"], _ => true));

    [Theory]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe", "-NoLogo|-NoExit|-Command|npm run dev")]
    [InlineData(@"C:\Windows\System32\cmd.exe", "/K|npm run dev")]
    [InlineData(@"C:\Program Files\Git\bin\bash.exe", "--login|-c|npm run dev; exec bash --login -i")]
    [InlineData(@"C:\Windows\System32\wsl.exe", "-d|Ubuntu|--|bash|-c|npm run dev; exec bash -l")]
    [InlineData("/bin/zsh", "-l|-c|npm run dev; exec zsh -i")]
    public void RunArgsLeaveTheShellOpen(string path, string expected)
    {
        IReadOnlyList<string> args = Path.GetFileNameWithoutExtension(path).ToLowerInvariant() switch
        {
            "bash" => ["--login", "-i"], "wsl" => ["-d", "Ubuntu"], "zsh" => ["-l"], "cmd" => [], _ => ["-NoLogo"],
        };
        Assert.Equal(expected, string.Join("|", new ShellInfo("x", path, args).RunArgs("npm run dev")));
    }

    [Fact]
    public void ThisMachineHasAShellAndPickFallsBackToIt()
    {
        var all = Shells.Detect();
        Assert.NotEmpty(all);
        Assert.Equal(all[0].Name, Shells.Pick("no such shell").Name);
    }
}
