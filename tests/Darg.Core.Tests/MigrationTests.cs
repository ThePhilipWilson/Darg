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

    /// <summary>WOR-0196: the dictation model was downloaded into Darg/models before the instance moved, and
    /// the whole-folder move then never ran, so the real instance stayed in Workbench/home for good.</summary>
    [Fact]
    public void The_instance_moves_into_a_Darg_folder_that_is_already_there()
    {
        var appData = Path.Combine(Path.GetTempPath(), "darg-mig-" + Guid.NewGuid().ToString("N"));
        var inst = Path.Combine(appData, "Workbench", "home");
        Directory.CreateDirectory(inst);
        File.WriteAllText(Path.Combine(inst, "instance.json"), "{}");
        var models = Path.Combine(appData, "Darg", "models");
        Directory.CreateDirectory(models);
        File.WriteAllText(Path.Combine(models, "model.bin"), "x");
        try
        {
            InstanceConfig.MigrateFromWorkbench(appData);
            Assert.True(File.Exists(Path.Combine(appData, "Darg", "home", "instance.json")));
            Assert.True(File.Exists(Path.Combine(models, "model.bin")));
            Assert.True(File.Exists(Path.Combine(inst, "instance.json"))); // via the link, if the machine allows one
            InstanceConfig.MigrateFromWorkbench(appData); // second run changes nothing
            Assert.True(File.Exists(Path.Combine(appData, "Darg", "home", "instance.json")));
        }
        finally { try { Directory.Delete(inst); } catch (Exception) { } Directory.Delete(appData, true); }
    }
}
