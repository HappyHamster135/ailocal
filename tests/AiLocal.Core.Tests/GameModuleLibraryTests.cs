using AiLocal.Node.Hosting;
using AiLocal.Node.Hosting.GameModules;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>First coverage for the module library now that it is actually
/// wired into the agent (the game_module tool): the registry must expose
/// every module, and every module must resolve real code for all three
/// engines - a library entry that lists but can't be fetched would send the
/// agent in circles.</summary>
public class GameModuleLibraryTests
{
    [Fact]
    public void List_ExposesAllEightModulesWithDescriptions()
    {
        var modules = GameModuleLibrary.List();
        // v2.32: 8 -> 10. Butik och prestationer tillkom; composern hade
        // kryssrutor for dem sedan v2.30 utan att biblioteket kunde leverera.
        Assert.Equal(10, modules.Count);
        Assert.All(modules, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Name));
            Assert.False(string.IsNullOrWhiteSpace(m.Description));
        });
        Assert.Contains(modules, m => m.Name == "InventorySystem");
        Assert.Contains(modules, m => m.Name == "ParticleEffects");
        Assert.Contains(modules, m => m.Name == "ShopModule");
        Assert.Contains(modules, m => m.Name == "AchievementModule");
    }

    [Fact]
    public void GetCode_EveryListedModule_ResolvesForAllThreeEngines()
    {
        foreach (var module in GameModuleLibrary.List())
            foreach (var engine in new[] { "html5", "godot", "unity" })
            {
                var code = GameModuleLibrary.GetCode(module.Name, engine);
                Assert.False(string.IsNullOrWhiteSpace(code),
                    $"{module.Name}/{engine} returned no code");
            }
    }

    [Fact]
    public void GetCode_ShortAliasAndCaseInsensitive_Work()
    {
        Assert.NotNull(GameModuleLibrary.GetCode("inventory", "HTML5"));
        Assert.NotNull(GameModuleLibrary.GetCode("PARTICLES", "godot"));
    }

    [Fact]
    public void GetCode_UnknownModuleOrEngine_ReturnsNull()
    {
        Assert.Null(GameModuleLibrary.GetCode("teleporter", "html5"));
        Assert.Null(GameModuleLibrary.GetCode("inventory", "unreal"));
    }

    [Fact]
    public async Task HandleTool_ListAndGet_ProduceUsableOutput()
    {
        var (listOk, listOut) = await GameModuleTool.Handle("list", null, null);
        Assert.True(listOk);
        Assert.Contains("InventorySystem", listOut);

        var (getOk, getOut) = await GameModuleTool.Handle("get", "quest", "godot");
        Assert.True(getOk);
        Assert.Contains("Quest", getOut);

        var (badOk, badOut) = await GameModuleTool.Handle("get", "nope", "html5");
        Assert.False(badOk);
        Assert.Contains("list", badOut);
    }
}
