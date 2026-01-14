using System.ComponentModel;

namespace AgenticRpg.Core.Models.Game;

public class InteractiveEntity
{
    /// <summary>
    /// Gets or sets the monster's physical strength and power.
    /// </summary>
    [Description("Physical strength and power attribute")]
    public virtual int Might { get; set; }

    /// <summary>
    /// Gets or sets the monster's speed, reflexes, and coordination.
    /// </summary>
    [Description("Speed, reflexes, and coordination attribute")]
    public virtual int Agility { get; set; }

    /// <summary>
    /// Gets or sets the monster's intelligence, perception, and mental acuity.
    /// </summary>
    [Description("Intelligence, perception, and mental acuity attribute")]
    public virtual int Wits { get; set; }

    /// <summary>
    /// Gets or sets the monster's endurance and physical resilience.
    /// </summary>
    [Description("Endurance and physical resilience attribute")]
    public virtual int Vitality { get; set; }

    /// <summary>
    /// Gets or sets the monster's charisma, force of personality, and social influence.
    /// </summary>
    [Description("Charisma and force of personality attribute")]
    public virtual int Presence { get; set; }

    /// <summary>
    /// Gets or sets the monster's current hit points, representing its remaining health.
    /// </summary>
    [Description("Current health points of the monster")]
    public virtual int CurrentHP { get; set; }

    /// <summary>
    /// Gets or sets the monster's maximum hit points, representing its total health capacity.
    /// </summary>
    [Description("Maximum health points of the monster")]
    public virtual int MaxHP { get; set; }

    /// <summary>
    /// Gets or sets the monster's armor class, representing how difficult it is to hit.
    /// </summary>
    [Description("Defensive rating determining how difficult the monster is to hit")]
    public virtual int ArmorClass { get; set; } = 10;

    /// <summary>
    /// Gets or sets the monster's movement speed in feet per turn.
    /// </summary>
    [Description("Movement speed in feet per turn")]
    public virtual int Speed { get; set; } = 30;
}