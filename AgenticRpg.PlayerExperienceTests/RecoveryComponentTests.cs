using AgenticRpg.Components.ChatComponents;
using AgenticRpg.Components.GameComponents;
using AgenticRpg.Core.Messaging;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
using AgenticRpg.Core.Models.Enums;
using AgenticRpg.Core.Rules;
using Bunit;
using Microsoft.JSInterop;

namespace AgenticRpg.PlayerExperienceTests;
public class RecoveryComponentTests
{
    [Fact]
    public async Task ComposerRestoresScopedDraftAndClearsOnlyAfterAcceptance()
    {
        await using var context = new BunitContext();
        var module = context.JSInterop.SetupModule("./_content/AgenticRpg.Components/ChatComponents/draftInput.js");
        module.Setup<string>("get", _ => true).SetResult("Recovered text");
        module.SetupVoid("set", _ => true).SetVoidResult();
        var accepted = false;
        var input = context.Render<UserInput>(p => p.Add(c => c.AccountId, "alice").Add(c => c.ScopeId, "scope")
            .Add(c => c.SubmissionAccepted, () => accepted).Add(c => c.OnSubmit, _ => Task.CompletedTask));
        input.WaitForAssertion(() => Assert.Equal("Recovered text", input.Find("input").GetAttribute("value")));
        input.Find(".btn-submit").Click();
        Assert.Equal("Recovered text", input.Find("input").GetAttribute("value"));
        Assert.Contains("Delivery not confirmed", input.Markup);
        accepted = true; input.Find(".btn-submit").Click();
        Assert.Equal("", input.Find("input").GetAttribute("value"));
        Assert.Contains(module.Invocations["set"], i => (string)i.Arguments[0]! == "rpg-draft:alice:scope" && (string)i.Arguments[1]! == "");
    }
    [Fact]
    public async Task ProcessingShowsDiceOwnerAndInterruptedResponseStopsSpinner()
    {
        await using var context = new BunitContext();
        var message = new ChatMessage { IsUser = true, Status = MessageProcessingStatus.Processing,
            Phase = ProcessingPhase.WaitingForDice, ProgressDetail = "Waiting for Alice to roll: attack" };
        var cut = context.Render<MessageView>(p => p.Add(c => c.Message, message));
        Assert.Contains("Waiting for dice", cut.Markup); Assert.Contains("Waiting for Alice", cut.Markup);
        Assert.NotEmpty(cut.FindAll("[aria-live=polite]"));
        var reducer = new RuntimeChatState(); var chat = new List<ChatMessage>();
        reducer.Apply(chat, new RuntimeMessage { Id = "response", ClientMessageId = "client", PlayerId = "alice", Revision = 1,
            Status = MessageProcessingStatus.Interrupted, Phase = ProcessingPhase.Interrupted, Content = "Partial" }, "alice");
        Assert.False(reducer.HasPending("alice")); Assert.False(chat.Last().IsStreaming);
    }
    [Fact]
    public async Task CharacterSheetUsesCombatCalculationAndKeepsPageAcrossUpdates()
    {
        await using var context = new BunitContext();
        var character = TestRuntime.Character(); character.Skills["Melee Combat"] = 3;
        character.Equipment.MainHand = new WeaponItem { Name = "Sword", DamageDice = "1d8", WeaponType = WeaponType.Melee };
        var cut = context.Render<CharacterSheetView>(p => p.Add(c => c.Character, character));
        await cut.InvokeAsync(() => cut.Instance.NavigateToPage(2));
        var pageTwo = cut.Markup;
        cut.Render(p => p.Add(c => c.Character, character));
        Assert.Equal(pageTwo, cut.Markup);
        await cut.InvokeAsync(() => cut.Instance.NavigateToPage(1));
        Assert.Contains(WeaponMechanics.Signed(WeaponMechanics.For(character, character.Equipment.MainHand).Attack), cut.Markup);
        Assert.Contains(WeaponMechanics.For(character, character.Equipment.MainHand).DamageExplanation, cut.Markup);
    }
}
