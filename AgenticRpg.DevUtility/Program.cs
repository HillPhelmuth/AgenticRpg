// See https://aka.ms/new-console-template for more information

using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Services;
// Add user secrets config code here
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Threading;

var builder = new ConfigurationBuilder()
    .AddUserSecrets<Program>();

var configuration = builder.Build();
AgentStaticConfiguration.Configure(configuration);
Console.WriteLine("Hello, World!");

var monsterIds = DndMonsterService.GetAvailableMonsterNames().Split(',').Select(x => x.Trim());
var taskList = monsterIds.Select(CreateMonsterAsync).ToList();
var monsters = await Task.WhenAll(taskList);
var dataPath = @"C:\Users\adamh\source\repos\AgenticRpg\AgenticRpg.Core\Data\";
var outputPath = Path.Combine(dataPath, "RpgMonsters.json");
await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(monsters.Where(x => x != null), new JsonSerializerOptions(){WriteIndented = true}));

async Task<RpgMonster?> CreateMonsterAsync(string monsterId)
{
    Console.WriteLine($"Converting Monster: {monsterId}");
    return await DndMonsterService.CreateMonsterByNameAsync(monsterId);
}