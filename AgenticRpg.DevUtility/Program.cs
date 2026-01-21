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
Console.WriteLine("Convert Dnd To RPG. Select which area to convert. (input the associated number)");
const string options = """
                       1. Convert Monsters
                       2. Convert Spells
                       3. Create Spells Cache
                       4. Update Spells
                       5. Show Spells
                       """;
Console.WriteLine(options);
var input = Console.ReadLine();
var cachedJsonOptions = new JsonSerializerOptions() { WriteIndented = true };
if (!int.TryParse(input, out var selectedOption))
{
    Console.WriteLine("Invalid selection.");
    return;
}
switch (selectedOption)
{
    case 1:
        {
            var monsterIds = DndApiService.GetAvailableMonsterNames().Split(',').Select(x => x.Trim());
            var taskList = monsterIds.Select(CreateMonsterAsync).ToList();
            var monsters = await Task.WhenAll(taskList);
            var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dataPath = Path.Combine(userPath, @"source\repos\AgenticRpg\AgenticRpg.Core\Data");
            var outputPath = Path.Combine(dataPath, "RpgMonsters.json");

            await File.WriteAllTextAsync(outputPath,
                JsonSerializer.Serialize(monsters.Where(x => x != null), cachedJsonOptions));
            break;
        }
    case 2:
        {
            var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dataPath = Path.Combine(userPath, @"source\repos\AgenticRpg\AgenticRpg.Core\Data");
            var outputPath = Path.Combine(dataPath, "RpgConvertedSpells-level4_5.json");
            var spells = await DndApiService.RetrieveSpells();
            var taskList = spells.Select(DndApiService.ConvertSpellAsync).ToList();
            var convertedSpells = await Task.WhenAll(taskList);
            await File.WriteAllTextAsync(outputPath,
                JsonSerializer.Serialize(convertedSpells.Where(x => x != null), cachedJsonOptions));
            break;
        }
    case 3:
        {
            var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dataPath = Path.Combine(userPath, @"source\repos\AgenticRpg\AgenticRpg.Core\Data");
            var outputPath = Path.Combine(dataPath, "DndSpellCache-level4_5.json");
            var spells = await DndApiService.GetSpellData();
            await File.WriteAllTextAsync(outputPath,
                JsonSerializer.Serialize(spells, cachedJsonOptions));
            break;
        }
    case 4:
        {
            var wizardSpells = Spell.GetAllWizardSpellsFromFile();
            var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dataPath = Path.Combine(userPath, @"source\repos\AgenticRpg\AgenticRpg.Core\Data");
            var outputPathWizard = Path.Combine(dataPath, "WizardSpellsList.json");
            //foreach (var wSpell in wizardSpells)
            //    wSpell.SpellClass = SpellClass.WizardAndWarMage;
            //await File.WriteAllTextAsync(outputPathWizard, JsonSerializer.Serialize(wizardSpells, cachedJsonOptions));
            var outputPathCleric = Path.Combine(dataPath, "ClericSpellList.json");
            var clericSpells = Spell.GetAllClericSpellsFromFile();
            foreach (var cSpell in clericSpells)
            {
                cSpell.SpellClass = SpellClass.ClericAndPaladin;
            }
            await File.WriteAllTextAsync(outputPathCleric, JsonSerializer.Serialize(clericSpells, cachedJsonOptions));
            break;
        }
    case 5:
        {
            var selections = """
                         1. Wizard Spells
                         2. Cleric Spells
                         3. Both
                         """;
            Console.WriteLine(selections);
            var selection = Console.ReadLine();
            List<Spell> selectedSpells = [];
            switch (selection)
            {
                case "1":
                    {
                        selectedSpells.AddRange(Spell.GetAllWizardSpellsFromFile());
                        break;
                    }
                case "2":
                    {
                        selectedSpells.AddRange(Spell.GetAllClericSpellsFromFile());
                        break;
                    }
                case "3":
                    {
                        selectedSpells.AddRange(Spell.GetAllWizardSpellsFromFile());
                        selectedSpells.AddRange(Spell.GetAllClericSpellsFromFile());
                        break;
                    }
            }
            foreach (var spell in selectedSpells.OrderBy(x => x.Level))
            {
                Console.WriteLine($"{spell.Name} - {spell.Level}");
            }

            break;
        }
}
async Task<RpgMonster?> CreateMonsterAsync(string monsterId)
{
    Console.WriteLine($"Converting Monster: {monsterId}");
    return await DndApiService.CreateMonsterByNameAsync(monsterId);
}