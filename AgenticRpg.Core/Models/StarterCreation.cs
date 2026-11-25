namespace AgenticRpg.Core.Models;

public static class StarterCreation
{
    public static readonly string[] Worlds = ["Classic fantasy", "Haunted frontier", "Arcane city"];
    public static readonly string[] Characters = ["Warrior", "Rogue", "Wizard"];
    public static string Prompt(bool character, string starter)
    {
        if (!(character ? Characters : Worlds).Contains(starter)) throw new ArgumentException("Unknown starter choice.");
        return character
            ? $"Quick create a complete, valid Human {starter} using your character creation tools. Use the selected starting level if provided; otherwise default to level 1. Choose a fitting name, background, attributes, skills, equipment and required spells. Complete every required creation step. Keep your insulting comedy. Do not finalize or save the character; present the completed draft for player review and the Finish Character button."
            : $"Quick create a complete {starter} world using your world building tools, with named locations, NPCs, and starter quests. Keep your insulting comedy. Do not finalize or save the world; present the completed draft for player review and the Save World button.";
    }
}
