using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
using Azure;
using Azure.Storage.Blobs;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using OpenAI;
using OpenAI.Responses;
using OpenAI.Videos;
using System.ClientModel;
using System.Configuration;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static AgenticRpg.Core.Services.ImageGenService;
using BinaryContent = System.ClientModel.BinaryContent;
using Environment = System.Environment;

#pragma warning disable OPENAI001

namespace AgenticRpg.Core.Services;

public class VideoGenService()
{
    
    private const string VideoContainerUrl = "https://npcimages.z20.web.core.windows.net/";
    private static readonly AgentConfiguration Configuration = AgentStaticConfiguration.Default;
    private static string _apiKey = Configuration.GoogleApiKey;
    private static BlobServiceClient BlobServiceClient => new(Configuration.BlobStorageConnectionString);
    private static BlobContainerClient _blobContainerClient = BlobServiceClient.GetBlobContainerClient("$web");

    private static Client Client => new(apiKey: _apiKey);
    private static readonly ILogger<VideoGenService> _logger = Configuration.LoggerFactory.CreateLogger<VideoGenService>();

    public static async Task<string> GenerateSoraVideo(CombatEncounter novelInfo, string campaignId,
        string? additionalInstruction = null)
    {
        var promptData = await GenerateVideoPromptAsync(novelInfo, additionalInstruction);
        
        _logger.LogInformation("VideoGen instructions:\n--------------\n{instructions}\n------------------\n", promptData.VideoInstructions);
        var prompt = "Combat video. Side view profile perspective throughout. " + promptData.VideoInstructions;
        return await GenerateSoraFromPrompt(prompt, campaignId);
    }
    
    public static async Task<string> GenerateSoraFromPrompt(string prompt, string campaignId = "")
    {
        var client = new OpenAIClient(Configuration.OpenAIApiKey).GetVideoClient();
        // 2) Build the multipart/form-data payload with an explicit boundary
        // 2) Build the multipart/form-data payload with an explicit boundary
        var boundary = Guid.NewGuid().ToString();
        var contentType = $"multipart/form-data; boundary=\"{boundary}\"";
        using var multipart = new MultipartFormDataContent(boundary);

        multipart.Add(new StringContent("sora-2", Encoding.UTF8, "text/plain"), "model");
        multipart.Add(new StringContent(prompt, Encoding.UTF8, "text/plain"), "prompt");
        multipart.Add(new StringContent("8", Encoding.UTF8, "text/plain"), "seconds");
        multipart.Add(new StringContent("1280x720", Encoding.UTF8, "text/plain"), "size");

        // 3) Get a stream for the multipart body
        using var bodyStream = await multipart.ReadAsStreamAsync();

        // 4) Send the request
        var createResult = await client.CreateVideoAsync(BinaryContent.Create(bodyStream), contentType);
        var createRaw = createResult.GetRawResponse().Content;

        // 5) Parse the JSON response
        using var createdDoc = JsonDocument.Parse(createRaw);
        var id = createdDoc.RootElement.GetProperty("id").GetString();
        var status = createdDoc.RootElement.GetProperty("status").GetString();
        var sw = new Stopwatch();
        sw.Start();
        _logger.LogInformation("CreateVideo => id: {id}, status: {status}", id, status);
        while (status is "in_progress" or "queued")
        {
            try
            {
                await Task.Delay(10000);
                var result = await client.GetVideoAsync(id!);
                var raw = result.GetRawResponse().Content;
                using var doc = JsonDocument.Parse(raw);
                id = doc.RootElement.GetProperty("id").GetString();
                status = doc.RootElement.GetProperty("status").GetString();
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Task was cancelled while waiting.");
                break;
            }
            finally
            {
                _logger.LogInformation("Sora 2 Video still generating (id: {id}, status: {status}). Elapsed Time: {elapsed}", id, status, sw.Elapsed.ToString("g"));
            }
        }
        var download = await client.DownloadVideoAsync(id!);
        var bytes = download.GetRawResponse().Content.ToArray();
        var blobName = @$"{ConvertNonAlphaNumericToUnderscore(campaignId)}\CombatVideo_{Guid.NewGuid():N}.mp4";
        //Temp for Testing...
        return await SaveAsUrl(blobName, bytes);
    }

    public async IAsyncEnumerable<string?> GetVideoIds()
    {
        var client = new OpenAIClient(Configuration.OpenAIApiKey).GetVideoClient();
        var list = client.GetVideosAsync();
        await foreach (var item in list.GetRawPagesAsync())
        {
            var raw = item.GetRawResponse().Content;
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                // Optional: log raw here; could be an error payload
                continue;
            }

            foreach (var video in data.EnumerateArray())
            {
                if (video.TryGetProperty("id", out var idProp))
                    yield return idProp.GetString();
            }
        }
    }
    public async Task<byte[]> DownloadFromId(string id)
    {
        var client = new OpenAIClient(Configuration.OpenAIApiKey).GetVideoClient();
        var download = await client.DownloadVideoAsync(id);
        var raw = download.GetRawResponse().Content; // BinaryData
        return raw.ToArray();
    }
    public async Task<string> DownloadFromIdAndSaveUrl(string id)
    {
        var client = new OpenAIClient(Configuration.OpenAIApiKey).GetVideoClient();
        var download = await client.DownloadVideoAsync(id);
        var bytes = download.GetRawResponse().Content.ToArray();

        var blobName = @$"Downloaded_{id}.mp4";
        // Save to user documents folder as mp4 file
        var userDocPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var filePath = Path.Combine(userDocPath, "Downloads", blobName);
        await System.IO.File.WriteAllBytesAsync(filePath, bytes);
        return filePath;
    }
    public static async Task<string> GenerateVideoAsync(CombatEncounter novelInfo, string campaignId, string? additionalInstruction = null, int chapter = 0, bool testSlow = false)
    {
        var promptData = await GenerateVideoPromptAsync(novelInfo, additionalInstruction, chapter);
        _logger.LogInformation("Video Gen prompt reasoning: {reasoning}", promptData.Reasoning);
        _logger.LogInformation("VideoGen instructions: {instructions}", promptData.VideoInstructions);
        var model = testSlow ? "veo-3.1-generate-preview" : "veo-3.1-fast-generate-preview";
        var negativePrompt = "facing camera";
        var prompt = "Combat video. Side view profile perspective throughout. " + promptData.VideoInstructions;
        var videoBytes = await GenerateVideoFromPrompt(prompt, model, new GenerateVideosConfig
        {
            NumberOfVideos = 1,
            DurationSeconds = 8,
            NegativePrompt = negativePrompt,
            Resolution = "720p"
        });
        var blobName = @$"{ConvertNonAlphaNumericToUnderscore(campaignId)}\CombatVideo_{Guid.NewGuid():N}.mp4";
        return await SaveAsUrl(blobName, videoBytes);
    }

    public static async Task<string> GenerateCharacterIntroVideo(Character character, IntroPromptType promptType)
    {
        var promptTemplateFactory = new KernelPromptTemplateFactory();
        var args = new KernelArguments() { ["CHARACTER_NAME"] = character.Name, ["Race"] = character.Race.ToString(), ["Class"] = character.Class.ToString() };
        var kernel = Kernel.CreateBuilder().Build();
        var template = IntroPrompts.GetPrompt(promptType);
        var templateConfig = new PromptTemplateConfig(template);
        var instructions = await promptTemplateFactory.Create(templateConfig).RenderAsync(kernel, args);
        Console.WriteLine($"Rendered instructions for {character.Name} Intro: {instructions}");
        // Get byte[] from character.ImageUrl;
        using var client = new HttpClient();
        var imageBytes = await client.GetByteArrayAsync(character.PortraitUrl!);
        string imageMimeType = character.PortraitUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
        var config = new GenerateVideosConfig
        {
            NumberOfVideos = 1,
            DurationSeconds = 8,
            Resolution = "720p",
            ReferenceImages = [new VideoGenerationReferenceImage(){ReferenceType = VideoGenerationReferenceType.ASSET, Image = new Image(){ImageBytes = imageBytes, MimeType = imageMimeType}}]
        };
        var videoBytes = await GenerateVideoFromPrompt(instructions, "veo-3.1-generate-preview", config);
        var blobName = @$"CharacterIntros\{ConvertNonAlphaNumericToUnderscore(character.Id)}_IntroVideo_{Guid.NewGuid():N}.mp4";
        return await SaveAsUrl(blobName, videoBytes);
    }
    private static async Task<byte[]> GenerateVideoFromPrompt(string prompt, string model, GenerateVideosConfig config)
    {
        var source = new GenerateVideosSource
        {
            Prompt = prompt,
        };

        var operation = await Client.Models.GenerateVideosAsync(
            model: model, source: source, config: config);
        var sw = new Stopwatch();
        sw.Start();
        while (operation.Done != true)
        {
            try
            {
                await Task.Delay(10000);
                operation = await Client.Operations.GetAsync(operation, null);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Task was cancelled while waiting.");
                break;
            }
            finally
            {
                _logger.LogInformation("Video still generating. Elapsed Time: {elapsed}", sw.Elapsed.ToString("g"));
            }
        }
        var video = operation.Response?.GeneratedVideos?.FirstOrDefault()?.Video;
        var videoData = await Client.Files.DownloadAsync(video.Uri);
        // convert Stream to byte[]
        byte[] videoBytes;
        using (var memoryStream = new MemoryStream())
        {
            await videoData.CopyToAsync(memoryStream);
            videoBytes = memoryStream.ToArray();
        }

        return videoBytes;
    }

    private static async Task<string> SaveAsUrl(string fileName, byte[] bytes)
    {
        var blobClient = _blobContainerClient.GetBlobClient(fileName);
        await blobClient.UploadAsync(new BinaryData(bytes), overwrite: true);
        return $"{VideoContainerUrl}/{fileName}";
    }
    private static async Task<VideoInstructionsOutput> GenerateVideoPromptAsync(CombatEncounter combatEncounter, string? additionalInstructions = null, int chapter = 0)
    {
        var agent = new OpenAIClient(Configuration.OpenAIApiKey)
            .GetResponsesClient("gpt-4.1")
            .AsAIAgent(new ChatClientAgentOptions()
            {
                Name = "Video Gen Instruction Agent",
                ChatOptions = new ChatOptions()
                {
                    Instructions = VideoGenInstructionGen,
                    ResponseFormat = ChatResponseFormat.ForJsonSchema<VideoInstructionsOutput>()
                }
            }, loggerFactory: Configuration.LoggerFactory);
        var prompt =
        $"""
         ## Player Party Characters
         {string.Join("\n\n", combatEncounter.PartyCharacters.Select(x => x.AsBasicDataMarkdown()))}

         ## Enemies/Monsters
         {string.Join("\n\n", combatEncounter.EnemyMonsters.Select(x => x.ToMarkdown()))}
         
         ## Combat Encounter Summary
         {string.Join("\n\n", combatEncounter.CombatLog.Select(x => $"{x.Timestamp:g}\n{x.Description}"))}
         """;
        var response = await agent.RunAsync<VideoInstructionsOutput>(prompt);
        return response.Result;


    }

    private const string VideoGenInstructionGen =
        $"""
        # Instructions
        
        ## Task
        
        Analyze a provided combat encounter and select the most visually compelling, pivotal, or dramatic moment. Generate step-by-step instructions for creating a 4–8 second video that captures the main elements, atmosphere, and actions of that critical moment.
        
        - Review the combat data (party, enemies, terrain, combat log) to identify the key moment.
        - Justify the moment selection with a brief explanation of its significance (e.g., turning point, heroic act, danger). The justification must come before video instructions.
        - After justification, describe all required visual details: character/monster descriptions, weapons, spell effects, actions, terrain, participant positions, and camera angles to depict a 6–8 second side view profile scene with combatants facing each other.
        - Include any quotes for silly battle cries, ridiculous incantations, or idiotic dramatic dialogue.
        - Always give reasoning (key moment selection and justification) first, before video instructions.
        
        ## Video Prompting Guide
        
        **General Guidelines:**
        Write descriptive, clear prompts. Start with your main concept, refine with relevant keywords/modifiers, and use terminology suitable for fantasy combat video generation.
        
        Include these elements:
        - Subject: List characters, monsters, weapons, spells, and environment central to the moment.
        - Locations: Clearly indicate where each combatant is positioned relative to each other (e.g., "left," "right," "15 feet apart").
        - Action: State the main combat move(s) (attack, dodge, spellcast, fall, defense, etc.).
        - Style: Indicate the fantasy film style (e.g., epic, dark, heroic, animated).
        - Composition: [Optional] State framing (wide for battlefield, close-up for emotion, over-the-shoulder, group shot).
        - Focus & Effects: [Optional] Note lens or focus details (shallow, deep, motion blur, slow-mo).
        - Ambiance: [Optional] Describe lighting, color, or atmosphere (fire-lit, shadowy, magical glow).
        
        **Prompting Tips:**
        - Begin with **Character Descriptions** and **Monster Descriptions** sections to describe all combatants so you don't have to repeat descriptions later.
        - Use vivid language and dynamic verbs.
        - Specify visual traits of all combatants—armor, weapons, race, class, injuries, expressions.
        - Emphasize energy and action.
        - Ensure the camera is at a side view profile throughout; combatants must face each other. **Note:** This instruction should come _before_ the action description.
        
        **Example video_instructions**
        ```
        **Character Descriptions**
         - Sir Muscles - A large, overly muscled man in full plate armor, and carrying a glowing longsword. 
         - Sarath the Blackflame - A dark skinned male elf in leather armor holding a glowing orb of power. 
        **Monster Descriptions** 
         - Red Dragon - A massive red dragon with layered crimson scales, a horned and spined head, glowing eyes, and broad membranous wings stands powerfully on clawed limbs. 20 feet tall at the shoulder.
         
         Wide, sandlit shot of the Gilded Coliseum floor: Left, Sir Muscles, stands beside Serath the Blackflame. Both face right. On the right, the towering Red Dragon, wings outspread and claws lowered, snarls menacingly. The coliseum crowd blurs along the upper edge, torches flickering. Keep camera at side view profile. Sir Muscles screeches (high pitched), 'I'm peeing in my armor!' as he lunges forward, sword blazing with brilliant light, and delivers a powerful downswing into the dragon's chest. The impact explodes with sparks and a shockwave of force. The Red Dragon staggers back, further to the right, roars 'GROOOOOSSSS!' as it collapses to its knees, then falls facedown, tail and wings twitching. Serath stands silent, stoic, as shadow tendrils coil around his boots. Audio: clang of blade, dragon's guttural roar, crowd erupts in cheers, a faint hum of residual magic. End with the fallen dragon, heroes poised in triumph.
        ```
        
        **Audio Prompting:**
        - Dialogue/Battle Cries: Use quotes for any speech - these should be funny and absurd, perhaps even mean-spirited (in a funny way).
        - SFX: Describe combat sounds (weapon clashing, spells, creature growls).
        - Ambiance: Note background/environmental sounds (crowds, echoes, fire, magic hum).
        
        ## Output Format
        Return a JSON object containing:
        - `"reasoning"`: Concise paragraph justifying the selected combat moment and its tactical/narrative importance.
        - `"video_instructions"`: Concrete directions for a 6–8 second scene, including visuals, composition, characters, monsters, environment, actions, dialogue, and audio cues.
        
        **Edge Cases/Considerations:**
        - If action detail is sparse, infer drama from stat changes (damage, criticals, status effects).
        - If several moments qualify, pick the most dynamic or significant visually or narratively.
        - Detail combatants: race, class, arms, wounds, energy, desperation, or heroism.
        - Incorporate relevant terrain/environmental factors.
        
        
        """;

    private const string SoraPromptingGuide =
        """
        # Crafting a successful video prompt
        
        ## Before you prompt
        Think of prompting like briefing a cinematographer who has never seen your storyboard. If you leave out details, they’ll improvise – and you may not get what you envisioned. By being specific about what the “shot” should achieve, you give the model more control and consistency to work with.
        
        But leaving some details open can be just as powerful. Giving the model more creative freedom can lead to surprising variations and unexpected, beautiful interpretations. Both approaches are valid: **detailed prompts give you control and consistency, while lighter prompts open space for creative outcomes.** The right balance depends on your goals and the result you’re aiming for.
        
        Treat your prompt as a creative wish list, not a contract. Using **the same prompt multiple times will lead to different results** – this is a feature, not a bug. Each generation is a fresh take, and sometimes the second or third option is better.
        
        Most importantly, be prepared to iterate. Small changes to camera, lighting, or action can shift the outcome dramatically. Collaborate with the model: you provide direction, and the model delivers creative variations.
        
        This isn’t an exact science—think of the guidance below as helpful suggestions learned from working with the model.
        
        
        ## Prompt anatomy that works
        A clear prompt describes a shot as if you were sketching it onto a storyboard. State the camera framing, note depth of field, describe the action in beats, and set the lighting and palette. Anchoring your subject with a few distinctive details keeps it recognizable, while a single, plausible action makes the shot easier to follow.
        
        Describing multiple shots in a single prompt is also valid if you need to cover a sequence. When you do this, keep each shot block distinct: one camera setup, one subject action, and one lighting recipe at a time. Treat each shot as a creative unit, whether you stitch them together later or let them play out continuously.
        
        - Shorter prompts give the model more creative freedom.
        - Longer, more detailed prompts restrict the model’s creativity but improve consistency.
        
        ### Short Prompt Example
        ```text
        In a torch-lit stone tavern, a weathered human knight sits at a wooden table and says, "I’ve seen this war before."
        ````
        
        Why this works:
        
        * `torch-lit stone tavern` establishes a fantasy environment and lighting logic.
        * `a weathered human knight` defines archetype without over-specifying armor or age.
        * The dialogue is short and grounded, making timing easier to hit.
        
        Many details remain open—era, faction, armor style, camera angle—so the model will invent them unless specified.
        
        ## Going Ultra-Detailed
        
        For complex, cinematic fantasy shots, you can go beyond standard prompts and describe the scene as if briefing a film crew working on a high-budget RPG cinematic or opening cutscene.
        
        You might describe **what the viewer notices first**, the **camera platform and lens**, **lighting sources such as fire, magic, or moonlight**, **material textures**, **ambient fantasy sound**, and **narrative purpose**.
        
        This approach works especially well for:
        
        * RPG intro cinematics
        * Boss reveals
        * Spellcasting moments
        * Narrative dialogue scenes
        
        ### Ultra-Detailed Fantasy Example
        
        ```
        Format & Look
        Duration 5s; 180° shutter; digital capture emulating 65 mm epic fantasy cinematics; subtle film grain; soft halation around magical light sources.
        
        Lenses & Filtration
        28 mm / 50 mm spherical primes; mild bloom filter to exaggerate arcane glow.
        
        Grade / Palette
        Highlights: cool moonlight silver.
        Mids: desaturated stone grays and steel blues.
        Blacks: deep with preserved shadow detail.
        Accent color: ember orange from firelight.
        
        Lighting & Atmosphere
        Primary: full moon from camera right, high angle.
        Secondary: torchlight flicker from ground level.
        Practical: arcane runes emitting faint cyan glow.
        Atmos: low-lying fog drifting across stone floor.
        
        Location & Framing
        Ancient ruin courtyard at night.
        Foreground: broken marble column with moss.
        Midground: armored knight kneeling, sword tip resting on stone.
        Background: towering rune-carved archway.
        Avoid modern symbols or readable text.
        
        Wardrobe / Props
        Knight: battered steel plate, faded blue tabard, longsword etched with runes.
        Props: torches in iron sconces, scattered rubble, glowing sigils.
        
        Sound
        Diegetic only: wind through ruins, distant owls, torch crackle, faint magical hum.
        
        Optimized Shot List (2 shots / 5 s total)
        
        0.00–2.80 — “Oath in Shadow”
        Wide shot, slow dolly forward. The knight kneels in silhouette as moonlight rims the armor. Fog curls around the sword blade.
        
        2.80–5.00 — “The Rise”
        Cut to medium shot. The knight grips the sword and stands, runes igniting one by one. The glow reflects in the visor. Purpose: signal resolve and impending conflict.
        
        Camera Notes
        Maintain silhouette clarity.
        Do not overexpose rune glow.
        Favor weighty, deliberate motion.
        
        Finishing
        Soft bloom on magic; restrained grain; cool-warm contrast LUT.
        Poster frame: knight mid-rise, sword glowing, ruins looming behind.
        ```
        
        ## Visual cues that steer the look
        
        Style is one of the strongest levers in fantasy video prompts. Calling out *“high fantasy cinematic,”* *“dark medieval realism,”* or *“storybook watercolor animation”* frames all other decisions.
        
        Clarity matters more than poetry. Describe visible outcomes.
        
        | Weak prompt           | Strong prompt                                                   |
        | --------------------- | --------------------------------------------------------------- |
        | “A magical forest”    | “Ancient oaks, glowing mushrooms, drifting fireflies”           |
        | “The warrior attacks” | “The warrior lunges once, shield raised, sword arcing downward” |
        | “Epic fantasy look”   | “Wide-angle lens, volumetric moonlight, drifting fog”           |
        
        ### Camera Framing Examples
        
        * wide establishing shot of a castle courtyard, eye level
        * low-angle medium shot as a dragon lands
        * aerial wide shot over marching armies
        * close-up on spellcaster’s hands as runes ignite
        
        ### Camera Motion Examples
        
        * slow push-in during spellcasting
        * lateral tracking with mounted cavalry
        * handheld shake during melee combat
        
        ## Control motion and timing
        
        Fantasy action benefits from restraint. One clear action per shot reads better than chaotic motion.
        
        **Weak**
        
        ```text
        The wizard casts a spell.
        ```
        
        **Strong**
        
        ```text
        The wizard raises one hand, chants briefly, and releases a single bolt of fire in the final second.
        ```
        
        ## Lighting and color consistency
        
        Fantasy lighting often comes from motivated sources: fire, moonlight, magic, lava, or bioluminescence.
        
        **Weak**
        
        ```text
        Lighting + palette: dark scene
        ```
        
        **Strong**
        
        ```text
        Lighting + palette: cold moonlight with warm torch fill
        Palette anchors: steel blue, ember orange, charcoal gray
        ```
        
        ## Dialogue and Audio
        
        Dialogue should feel natural to the fantasy world. Keep it short and purposeful.
        
        ### Fantasy Dialogue Example
        
        ```text
        A vaulted stone chamber lit by braziers. Banners hang tattered along the walls. A crowned queen stands before a war table carved with maps. A hooded ranger waits in the shadows near a pillar.
        
        Dialogue:
        - Queen: "The eastern pass has fallen."
        - Ranger: "Then the war comes sooner than we hoped."
        - Queen: "No. It comes exactly when it must."
        ```
        
        ### Background Sound Example
        
        ```text
        Crackling fire, distant thunder, faint echo of dripping water.
        ```
        
        # Prompt Templates and Examples
        
        ## Prompt Structure
        
        You may separate visual description, cinematography, actions, and dialogue for clarity. This is optional but effective for RPG cinematics and cutscenes.
        
        ### Descriptive Prompt Template
        
        ```text
        [Prose fantasy scene description. Describe characters, armor, weapons, environment, weather, and mood.]
        
        Cinematography:
        Camera shot: [framing and angle]
        Mood: [epic, ominous, heroic, tragic]
        
        Actions:
        - [Action beat 1]
        - [Action beat 2]
        - [Action beat 3]
        
        Dialogue:
        [Short lines if present]
        ```
        
        ## Prompt Examples
        
        ### Example 1: Whimsical Fantasy
        
        ```text
        Style: Hand-painted fantasy animation with storybook textures and soft lighting. The look evokes illustrated RPG manuals—warm, imperfect, tactile.
        
        Inside an alchemist’s workshop, shelves overflow with vials, scrolls, and glowing crystals. At the center, a small goblin artificer balances on a stool, nervously adjusting a sputtering arcane device. Rain taps against a stained-glass window depicting a forgotten hero.
        
        Cinematography:
        Camera: medium close-up, slow push-in
        Lens: 35 mm; shallow depth of field
        Lighting: warm candlelight with cool magical glow
        Mood: whimsical, tense, curious
        
        Actions:
        - The device sparks and hums.
        - The goblin winces and adjusts a dial.
        - A crystal stabilizes, glowing steadily.
        - Goblin mutters: "See? Perfectly safe."
        
        Background Sound:
        Rain, bubbling potions, faint arcane hum.
        ```
        
        ### Example 2: Romantic High Fantasy
        
        ```text
        Style: High fantasy cinematic, shot like a prestige RPG cutscene. Soft glow, warm sunset grade, subtle film grain.
        
        At sunset atop a cliffside ruin, two elven figures dance barefoot among fallen columns. Their cloaks flutter in the wind as golden light spills across the sea far below. Fireflies gather as if drawn to them.
        
        Cinematography:
        Camera: medium-wide shot, slow dolly in
        Lens: 40 mm; shallow focus
        Lighting: golden sun with soft rim light
        Mood: tender, nostalgic, mythic
        
        Actions:
        - One elf spins, cloak flaring.
        - Elf (laughing): "Even the old stones remember us."
        - The other takes their hand, pulling them close.
        - They pause as the sun dips below the horizon.
        
        Background Sound:
        Wind, distant waves, soft chimes from elven jewelry.
        ```
        
        """;
}
public class VideoInstructionsOutput
{
    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; set; }

    [JsonPropertyName("video_instructions")]
    public required string VideoInstructions { get; set; }
}

public class IntroPrompts
{
    public const string RunwayWalkIntro =
        """
        STYLE: Cinematic fantasy, dramatic lighting, mideval fashion show vibe
        
        SCENE:
        A stone runway stretches forward, flanked by torches and shadowed onlookers. Smoke drifts low across the floor.
        
        ACTION:
        {{$CHARACTER_NAME}}, a {{$RACE}} {{$CLASS}}, walks directly toward the camera starting 20ft away and strutting with exaggerated confidence, like a high-fashion runway model - filling more of the frame as they go. Each step is deliberate and slow. 15ft down the runway, the character stops, pivots smoothly, and strikes an overconfident pose while holding their hands to their hips.
        
        CAMERA:
        Low-angle shot from front of runway. Subtle slow motion on the turn and pose.
        
        AUDIO:
        Overly epic orchestral swell. No dialogue.
        
        ENDING:
        Freeze-frame on the final pose. Off camera, a deep male voice announces "{{ $CHARACTER_NAME }}, the diva!", then quietly "and idiot...".
        
        """;
    public const string LateToIntroFilmingIntro =
        """
        STYLE: Cinematic fantasy, natural lighting, slightly handheld feel
        
        SCENE:
        An empty battlefield or dungeon chamber. Wind blows. Ambient silence.
        
        ACTION:
        The scene holds for 2 seconds. Then muffled shouting is heard offscreen for 1-2 seconds. {{$CHARACTER_NAME}}, a {{$RACE}} {{$CLASS}} suddenly runs into frame from the right side, slightly out of breath, hurriedly adjusting armor, robes, or gear.
        
        The character looks directly at the camera and says a quick, apologetic line such as:
        “Sorry. Did I miss the beginning?”
        
        CAMERA:
        Static wide shot. Slight shake when the character enters frame.
        
        AUDIO:
        Footsteps, clanking gear, short spoken line. No music until the very end.
        
        ENDING:
        Awkward pause. Off camera a voice says "You slow ass moron!".
        
        """;
    public const string EpicVisualMundaneActionIntro =
        """
        STYLE: Epic fantasy visuals contrasted with mundane action
        
        SCENE:
        Heroic lighting and framing. {{$CHARACTER_NAME}}, a {{$RACE}} {{$CLASS}} stands in a powerful silhouette.
        
        ACTION:
        An epic narrator voice delivers exaggerated accolades:
        “Behold {{$CHARACTER_NAME}}, A great and powerful {{$CLASS}}, Leader of Men, Slayer of Great Beasts…”

        While the narration continues, the visuals contradict it:
        – The character struggles to scratch an itch
        – Or repeatedly fails a simple task
        – Or looks confused while sparks fizzle harmlessly from a spell
        
        CAMERA:
        Heroic framing at first, then slowly reframes to reveal the awkward reality.
        
        AUDIO:
        Over-the-top epic narration, fully serious. Light ambient sound from the failed task.
        
        ENDING:
        Narration ends triumphantly. The character finally looks at the camera, mid-failure. Hard cut to title card: "{{$CHARACTER_NAME}}".
        
        """;
    public const string HotMicIntro =
        """
        TITLE: The Meta-Aware Intro – Accidental Hot-Mic
        
        STYLE: Behind-the-scenes fantasy set, casual framing
        
        SCENE:
        {{$CHARACTER_NAME}}, a {{$RACE}} {{$CLASS}} stands in position on a fantasy set. Lighting is partially set. The character believes the recording has not started.
        
        ACTION:
        The character mutters quietly:
        “Is this thing on?”
        and then
        “I swear if this takes another take…”
        
        They adjust their stance, smooth clothing, and suddenly snap into a flawless heroic pose the instant they realize the camera is live, and say "Oh Sh*t!".
        
        CAMERA:
        Medium shot, slightly off-center at first. Sharp refocus when the character poses.
        
        AUDIO:
        Casual room tone, quiet muttering, then a dramatic musical sting when the pose hits.
        
        ENDING:
        Immediate cut to a polished title card, as if nothing awkward happened.
        
        """;
    public const string BumblingOuttakeIntro =
        """
        TITLE: The Anti-Climax
        
        STYLE: Maximum epic fantasy buildup
        
        SCENE:
        Dark clouds churn overhead. Magical light gathers. The ground trembles.
        
        ACTION:
        {{$CHARACTER_NAME}}, a {{$RACE}} {{$CLASS}} rises dramatically into frame, eyes glowing, weapon or focus radiating power. The music swells to its peak.
        
        At the height of the buildup, the following happens (failure moment):
        – The character sneezes
        - Then farts loudly
        - Then scratches themselves, looking embarrassed
        
        CAMERA:
        Extreme low angle during the rise. Sudden neutral framing at the failure moment.
        
        AUDIO:
        Thunder, choir, magic hum. Abrupt stop or comedic silence at the anticlimax.
        
        ENDING:
        Freeze mid-reaction at most embarrasing point. Off-camera narrator says "{{$CHARACTER_NAME}} - Bumbling Fool, I guess".
        
        """;
    public static string GetPrompt(IntroPromptType type)
    {
        return type switch
        {
            IntroPromptType.RunwayWalk => RunwayWalkIntro,
            IntroPromptType.LateToIntroFilming => LateToIntroFilmingIntro,
            IntroPromptType.EpicVisualMundaneAction => EpicVisualMundaneActionIntro,
            IntroPromptType.HotMic => HotMicIntro,
            IntroPromptType.BumblingOuttake => BumblingOuttakeIntro,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }
}
public enum IntroPromptType
{
    RunwayWalk,
    LateToIntroFilming,
    EpicVisualMundaneAction,
    HotMic,
    BumblingOuttake
}