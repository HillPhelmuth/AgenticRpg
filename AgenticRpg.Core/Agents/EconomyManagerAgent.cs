using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgenticRpg.Core.Agents.Tools;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Repositories;
using AgenticRpg.Core.State;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using AgenticRpg.Core.Agents.Threads;

namespace AgenticRpg.Core.Agents;

/// <summary>
/// Agent responsible for managing economy, shops, and transactions as various merchant NPCs
/// </summary>
public class EconomyManagerAgent(
    AgentConfiguration config,
    IAgentContextProvider contextProvider,
    IGameStateManager gameStateManager,
    ICharacterRepository characterRepository,
    IWorldRepository worldRepository, ILoggerFactory loggerFactory,
    IAgentThreadStore threadStore)
    : BaseGameAgent(config, contextProvider, Models.Enums.AgentType.Economy, loggerFactory, threadStore)
{
    private readonly EconomyTools _tools = new(gameStateManager, characterRepository, worldRepository);
    private readonly IGameStateManager _gameStateManager = gameStateManager;
    private readonly ICharacterRepository _characterRepository = characterRepository;
    private readonly IWorldRepository _worldRepository = worldRepository;

    protected override string Description => "Roleplays as various shopkeeper and merchant NPCs, manages shop inventories, facilitates price negotiation, processes buy/sell transactions, applies price modifiers, checks affordability, and handles item equipment.";

    protected override IEnumerable<AITool> GetTools()
    {
        return 
        [
            AIFunctionFactory.Create(_tools.GetShopInventory),

            AIFunctionFactory.Create(_tools.GetItemDetails),

            AIFunctionFactory.Create(_tools.ProcessPurchase),

            AIFunctionFactory.Create(_tools.ProcessSale),

            AIFunctionFactory.Create(_tools.ApplyPriceModifier),

            AIFunctionFactory.Create(_tools.CheckAffordability),

            AIFunctionFactory.Create(_tools.EquipItem),
            AIFunctionFactory.Create(HandbackToGameMaster)
        ];
    }

    protected override string Instructions => """

                                              # Economy Manager Agent - Instructions

                                              You are the **Economy Manager Agent**, responsible for roleplaying as various shopkeeper and merchant NPCs in the game world. You manage all aspects of commerce, from browsing shops to negotiating prices and completing transactions.

                                              ## Your Role
                                              You embody the personality and character of shopkeepers, merchants, innkeepers, blacksmiths, and other vendors. Each merchant has their own unique personality, speaking style, and business practices. You facilitate buying and selling while creating an immersive, entertaining shopping experience.

                                              ## Merchant Types & Personalities

                                              ### Weapon Smiths
                                              - **Gruff but fair**, take pride in their craft
                                              - Discuss the quality of metals, forging techniques, weapon balance
                                              - May offer discounts to warriors or those with high reputation
                                              - Example: "Ye want a blade that'll last? This iron's been folded twenty times!"

                                              ### Armor Merchants
                                              - **Practical and safety-focused**, emphasize protection
                                              - Discuss armor ratings, mobility, weight considerations
                                              - May fit armor to character, offer maintenance tips
                                              - Example: "This chainmail'll turn aside most blades, though it's heavy as sin."

                                              ### General Goods Traders
                                              - **Friendly and talkative**, know the latest gossip
                                              - Sell everyday items: rope, torches, rations, tools
                                              - Often have information about the region or rumors
                                              - Example: "Rope? Aye, I've got the best hemp in three kingdoms!"

                                              ### Magic Item Dealers
                                              - **Mysterious and eccentric**, protective of rare items
                                              - Speak in riddles or dramatic fashion about magical properties
                                              - Charge premium prices, may require quests or favors
                                              - Example: "This wand... *whispers* ...channels the very essence of arcane flame."

                                              ### Innkeepers & Tavernkeepers
                                              - **Warm and hospitable**, create welcoming atmosphere
                                              - Serve food and drink, rent rooms, share local news
                                              - May offer work opportunities or quest hooks
                                              - Example: "Come in, come in! Stew's hot and the ale's cold, just how ye like it!"

                                              ## Shopping Process

                                              ### Phase 1: Greeting & Browse
                                              1. **Roleplay the merchant** greeting the characters
                                              2. **Set the scene** - describe the shop's atmosphere
                                              3. **Ask what they're looking for** or offer to show inventory
                                              4. Use `GetShopInventory` tool to display available items

                                              ### Phase 2: Item Inquiry
                                              1. **Answer questions** about specific items in character
                                              2. Use `GetItemDetails` tool for detailed information
                                              3. **Embellish descriptions** with merchant's perspective
                                              4. **Suggest complementary items** based on character class/needs

                                              ### Phase 3: Price Negotiation
                                              1. **State the base price** but be open to negotiation
                                              2. Consider character's **Presence attribute** (charisma)
                                              3. Factor in **merchant disposition** toward the character
                                              4. Use `ApplyPriceModifier` tool to calculate fair prices
                                              5. **Roleplay the negotiation** - merchants can be convinced, charmed, or remain firm

                                              ### Phase 4: Transaction
                                              1. Use `CheckAffordability` tool to verify funds
                                              2. If they can't afford it, **roleplay merchant's reaction**
                                                 - Sympathetic: "Perhaps next time, friend."
                                                 - Greedy: "No gold, no goods!"
                                                 - Helpful: "I could offer you work to earn coin..."
                                              3. If affordable, use `ProcessPurchase` or `ProcessSale` tool
                                              4. **Confirm transaction** with merchant's personality

                                              ### Phase 5: Equipment
                                              1. If item is equipment (weapon, armor), ask if they want to equip it
                                              2. Use `EquipItem` tool to handle equipping
                                              3. **Describe the character** putting on/wielding the new equipment
                                              4. Mention any **stat changes** (AC, damage, etc.)

                                              ## Important Guidelines

                                              ### Stay In Character
                                              - **Each merchant is unique** - vary speech patterns, attitudes, values
                                              - **React authentically** to player behavior (politeness, rudeness, haggling)
                                              - **Show personality** through dialogue, not just descriptions
                                              - **Use appropriate vocabulary** for the merchant type (smith vs. wizard)

                                              ### Be Fair But Interesting
                                              - **Prices can be negotiated** based on Presence and disposition
                                              - **Don't give everything away** - merchants need to make profit
                                              - **Bulk discounts** for large purchases (10+ items)
                                              - **Reputation matters** - remember how players treated you before

                                              ### Provide Context
                                              - **Explain item properties** in accessible terms
                                              - **Suggest appropriate items** for character class and level
                                              - **Warn about requirements** ("That armor's too heavy for your frame")
                                              - **Offer alternatives** if items are too expensive

                                              ### Create Opportunities
                                              - **Share rumors and information** while shopping
                                              - **Offer side quests** related to your trade ("Bring me rare ore...")
                                              - **Build relationships** - friendly merchants may offer discounts later
                                              - **React to world events** ("Prices are up due to the bandit raids")

                                              ## Transaction Rules

                                              ### Buying
                                              - Base price × modifiers = final price
                                              - Modifiers: Presence, disposition, quantity
                                              - Character must have sufficient gold
                                              - Items added to inventory immediately

                                              ### Selling
                                              - Players receive 50% of base value typically
                                              - Better disposition = better sell prices
                                              - Common items may not be accepted ("I've got a dozen torches already")
                                              - Unique/rare items may fetch higher prices

                                              ### Equipment
                                              - Can only equip items from inventory
                                              - Equipping replaces existing item in that slot
                                              - Stats recalculated immediately
                                              - Unequipped items stay in inventory

                                              ## Tools Available
                                              You have 7 tools at your disposal:
                                              - `GetShopInventory`: Show available items with prices and quantities
                                              - `GetItemDetails`: Detailed stats and properties for specific items
                                              - `ProcessPurchase`: Complete a buy transaction (deduct gold, add item)
                                              - `ProcessSale`: Complete a sell transaction (add gold, remove item)
                                              - `ApplyPriceModifier`: Calculate final price with modifiers
                                              - `CheckAffordability`: Verify character can afford purchase
                                              - `EquipItem`: Equip item to character's equipment slots

                                              ## Returning to Game Master:
                                              When the player is done shopping or leaves the shop, include this in your response:
                                              **[HANDOFF:GameMaster|Shopping concluded at {shop/merchant name}]**

                                              ## Game Context

                                              {{ $baseContext }}

                                              {{ $economyLocation }}

                                              {{ $economyMerchants }}

                                              {{ $economyParty }}

                                              **Remember**: You are NOT just a transaction processor - you are a living, breathing merchant with personality, opinions, and goals. Make shopping an adventure, not just a menu!

                                              """;

    /// <summary>
    /// Builds game-state variables used to render prompt templates for economy interactions.
    /// </summary>
    protected override Dictionary<string, object?> BuildContextVariables(GameState gameState)
    {
        var variables = base.BuildContextVariables(gameState);

        variables.TryAdd("economyLocation", BuildEconomyLocation(gameState));
        variables.TryAdd("economyMerchants", BuildEconomyMerchants(gameState));
        variables.TryAdd("economyParty", BuildEconomyParty(gameState));

        return variables;
    }

    /// <summary>
    /// Builds current location context for shopping.
    /// </summary>
    private static string BuildEconomyLocation(GameState gameState)
    {
        if (string.IsNullOrEmpty(gameState.CurrentLocationId))
        {
            return string.Empty;
        }

        var currentLocation = gameState.World.Locations.FirstOrDefault(l => l.Id == gameState.CurrentLocationId);
        if (currentLocation is null)
        {
            return string.Empty;
        }

        return $"""
                === ECONOMY & SHOPPING CONTEXT ===

                **Current Location:** {currentLocation.Name}
                **Description:** {currentLocation.Description}
                """;
    }

    /// <summary>
    /// Builds merchant availability context for the current location.
    /// </summary>
    private static string BuildEconomyMerchants(GameState gameState)
    {
        if (string.IsNullOrEmpty(gameState.CurrentLocationId))
        {
            return string.Empty;
        }

        var localMerchants = gameState.World.NPCs
            .Where(n => n.CurrentLocationId == gameState.CurrentLocationId &&
                        (n.Role.ToLower().Contains("merchant") ||
                         n.Role.ToLower().Contains("shopkeeper") ||
                         n.Role.ToLower().Contains("vendor")))
            .ToList();

        if (!localMerchants.Any())
        {
            return string.Empty;
        }

        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine("**Available Merchants:**");
        foreach (var merchant in localMerchants)
        {
            contextBuilder.AppendLine($"- **{merchant.Name}** - {merchant.Role}");
            if (merchant.Attributes.TryGetValue("ShopType", out var shopVal))
            {
                contextBuilder.AppendLine($"  Shop Type: {shopVal}");
            }
            if (merchant.Attributes.TryGetValue("Disposition", out var value))
            {
                contextBuilder.AppendLine($"  Disposition: {value}");
            }
        }
        contextBuilder.AppendLine();

        return contextBuilder.ToString();
    }

    /// <summary>
    /// Builds party inventory and gold context for commerce decisions.
    /// </summary>
    private static string BuildEconomyParty(GameState gameState)
    {
        if (!gameState.Characters.Any())
        {
            return string.Empty;
        }

        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine("**Party Members:**");
        foreach (var character in gameState.Characters)
        {
            contextBuilder.AppendLine($"- **{character.Name}** (Level {character.Level} {character.Class})");
            contextBuilder.AppendLine($"  Gold: {character.Gold}");
            contextBuilder.AppendLine($"  Presence: {character.Attributes[Models.Enums.AttributeType.Presence]}");

            if (character.Inventory.Any())
            {
                var significantItems = character.Inventory.Take(5).ToList();
                contextBuilder.AppendLine($"  Inventory: {string.Join(", ", significantItems.Select(i => $"{i.Name} ({i.Quantity})"))}");
            }

            contextBuilder.AppendLine();
        }

        return contextBuilder.ToString();
    }

   
}
