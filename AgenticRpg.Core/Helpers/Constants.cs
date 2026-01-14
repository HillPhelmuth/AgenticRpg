using System;
using System.Collections.Generic;
using System.Text;

namespace AgenticRpg.Core.Helpers;

public class Constants
{
    public const string SkillList =
        """
        [{"Name":"Athletics","Description":"Climbing, swimming, jumping, and feats of raw strength in rugged environments."},{"Name":"Melee Combat","Description":"Proficiency with close-range weapons such as swords, axes, maces, and hammers."},{"Name":"Ranged Combat","Description":"Accuracy with bows, crossbows, thrown knives, and other distance weapons."},{"Name":"Stealth","Description":"Moving silently, hiding, and slipping past alert foes or traps."},{"Name":"Acrobatics","Description":"Balance, tumbling, flips, and evasive maneuvers in combat or exploration."},{"Name":"Endurance","Description":"Resisting fatigue, poison, disease, harsh weather, and prolonged strain."},{"Name":"Survival","Description":"Tracking, foraging, navigation, and wilderness know-how."},{"Name":"Perception","Description":"Spotting ambushes, noticing hidden details, and reading the environment."},{"Name":"Crafting","Description":"Designing, repairing, and improving weapons, armor, tools, or alchemical devices."},{"Name":"Investigation","Description":"Analyzing clues, solving puzzles, and piecing together mysteries."},{"Name":"Persuasion","Description":"Diplomacy, negotiation, and inspiring cooperation through sincerity."},{"Name":"Intimidation","Description":"Commanding fear or obedience through force of personality."},{"Name":"Deception","Description":"Lies, disguises, bluffing, and sleight-of-hand misdirection."},{"Name":"Leadership","Description":"Guiding allies, issuing battlefield commands, and bolstering morale."}]
        """;

    public const string WizardSpellList =
        """
        **Wizard Spells:**
        - **Magic Missile**: Launch multiple bolts of pure force that never miss.
        - **Shield**: Form an invisible barrier that intercepts harm.
        - **Burning Hands**: Project a fan of roaring flame.
        - **Mage Armor**: Wrap a target in spectral plates of force.
        - **Arcane Sight**: Open your senses to magical auras.
        - **Illusory Disguise**: Layer a believable glamour over a creature.
        - **Feather Fall**: Slow falling creatures before impact.
        """;

    public const string LightClericOrPaladinSpellList =
        """
        
        **Cleric/Paladin Spells (Light Domain):**
        - **Healing Light**: Channel warm radiance to close wounds and steady an ally.
        - **Divine Shield**: Call down a shimmering ward of light that deflects incoming blows.
        - **Bless**: Bolster the resolve of nearby allies with gentle radiance.
        - **Turn Undead**: Radiant command that drives undead away.
        - **Sacred Flame**: Summon a column of brilliant fire to scour foes.
       
        """;
    public const string DarkClericOrPaladinSpellList =
        """
        
        **Cleric/Paladin Spells (Shadow Domain):**
        - **Soul Rot**: Inflict necrotic decay that feeds on vitality.
        - **Umbral Armor**: Sheathe an ally in solidified gloom.
        - **Despair**: Siphon courage from nearby foes.
        - **Invoke Terror**: Unleash nightmarish whispers that shatter morale.
        - **Void Lash**: Manifest a whip of hungry darkness.
        
        """;
}