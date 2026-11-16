using Darg.Core;
using Xunit;

namespace Darg.Core.Tests;

public class MigrationTests
{
    [Fact]
    public void The_Workbench_data_folder_moves_to_Darg_and_the_old_path_still_leads_there()
    {
        var appData = Path.Combine(Path.GetTempPath(), "darg-mig-" + Guid.NewGuid().ToString("N"));
        var inst = Path.Combine(appData, "Workbench", "home");
        Directory.CreateDirectory(inst);
        File.WriteAllText(Path.Combine(inst, "instance.json"), "{}");
        try
        {
            InstanceConfig.MigrateFromWorkbench(appData);
            Assert.True(File.Exists(Path.Combine(appData, "Darg", "home", "instance.json")));
            Assert.True(File.Exists(Path.Combine(inst, "instance.json"))); // via the link, if the machine allows one
            InstanceConfig.MigrateFromWorkbench(appData); // second run changes nothing
            Assert.True(File.Exists(Path.Combine(appData, "Darg", "home", "instance.json")));
        }
        finally { try { Directory.Delete(Path.Combine(appData, "Workbench")); } catch (Exception) { } Directory.Delete(appData, true); }
    }
}
