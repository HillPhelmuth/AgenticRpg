#pragma warning disable MEAI001
#pragma warning disable OPENAI001
using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Agents.Llms;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.State;
using Azure.Storage.Blobs;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgenticRpg.Core.Models.Game;
using OpenAI.Images;
using Google.GenAI;
using Google.GenAI.Types;
using ImageGenerationOptions = Microsoft.Extensions.AI.ImageGenerationOptions;
using OpenAIImageGenerationOptions = OpenAI.Images.ImageGenerationOptions;



namespace AgenticRpg.Core.Services;

public class ImageGenService
{
    private static readonly AgentConfiguration Configuration = AgentStaticConfiguration.Default;
    private const string ImageContainerUrl = "https://npcimages.z20.web.core.windows.net/";
    private const string ImageDataPrefix = "data:image/png;base64,";
    private static string _apiKey = Configuration.GoogleApiKey;
    private static Client Client => new(apiKey: _apiKey);
    private static BlobServiceClient BlobServiceClient => new(Configuration.BlobStorageConnectionString);
    private static BlobContainerClient BlobContainerClient => BlobServiceClient.GetBlobContainerClient("$web");
    public static async Task<string> GenerateGameImage(string instructions, string playerId, string characterName)
    {
        string? aspectRatio = playerId == "worlds" ? "16:9" : null;
        ReadOnlyMemory<byte> data;
        string fileName= $"{playerId}/{ConvertNonAlphaNumericToUnderscore(characterName)}.png";
        try
        {

            //data = await GenerateImageData(instructions);
                data = await GoogleGenerateImageData(instructions, aspectRatio);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error on OpenAI image request. Fallback to google.\n\n{ex}");
            data = await GoogleGenerateImageData(instructions, aspectRatio);
        }

        return await SaveAsUrl(fileName, data.ToArray());
    }

    private static async Task<ReadOnlyMemory<byte>> GenerateImageData(string instructions)
    {
        var client = new OpenAIClient(new ApiKeyCredential(Configuration.OpenAIApiKey)/*, new OpenAIClientOptions(){Endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/") }*/);
        var imageClient = client.GetImageClient("gpt-image-1.5").AsIImageGenerator();

        var images = await imageClient.GenerateImagesAsync(
            instructions, new ImageGenerationOptions()
            {
                RawRepresentationFactory = _ => new OpenAIImageGenerationOptions(){ModerationLevel = GeneratedImageModerationLevel.Low, Size = GeneratedImageSize.W1024xH1536 }
            });
        var contents = images.Contents.OfType<DataContent>();
        var data = contents.First().Data;
        return data;
    }

    private static async Task<ReadOnlyMemory<byte>> GoogleGenerateImageData(string instructions, string? aspectRatio = null)
    {

        aspectRatio ??= "3:4";
        List<SafetySetting> safetySettings =
        [
            new()
            {
                Category = HarmCategory.HarmCategorySexuallyExplicit,
                Threshold = HarmBlockThreshold.BlockOnlyHigh
            }
        ];


        var config = new GenerateContentConfig
        {
            ResponseModalities =
            [
                "IMAGE",
                "TEXT"
            ],
            SafetySettings = safetySettings,
            ImageConfig = new ImageConfig(){AspectRatio = aspectRatio}
        };
        ReadOnlyMemory<byte> imageData = new();
        var fileIndex = 0;
        var response = await Client.Models.GenerateContentAsync("gemini-3.1-flash-image-preview", instructions, config);
        foreach (var part in response.Candidates[0].Content.Parts)
        {
            if (part.InlineData != null && part.InlineData.MimeType?.Contains("image/") == true)
            {
                // The image data is base64 encoded, convert it to bytes
                var imageBytes = part.InlineData.Data;
                var fileName = "generated_image.png"; // Or use the actual mimetype extension if available

                
                Console.WriteLine($"Image found as {imageBytes.Length}");
                imageData = imageBytes;
                break;
            }
        }

        return imageData;
    }
    public static async Task<string> GenerateCharacterImage(Character character, string? additionalInstructions = null)
    {

        var instructions = $"Character: {character.Name}, Race: {character.Race}, Class: {character.Class}, Background: {(string.IsNullOrEmpty(additionalInstructions) ? character.Background : additionalInstructions)}. \n Facing camera, full body shot.";

        if (!string.IsNullOrEmpty(additionalInstructions))
        {
            instructions += $", Additional Instructions: {additionalInstructions}";
        }

        return await GenerateGameImage(instructions, character.PlayerId, character.Name);
    }
    public static async Task<string>GenerateWorldImage(World world, string additionalInstructions = "")
    {
        var instructions = $"Generate a world map based on the following details:\nWorld Name: {world.Name}, Description: {world.Description}. Geography: {world.Geography}.";
        if (!string.IsNullOrEmpty(additionalInstructions))
        {
            instructions += $" Additional Instructions: {additionalInstructions}";
        }
        return await GenerateGameImage(instructions, "worlds", world.Name);
    }
    public static async Task<string> GenerateCampaignImage(string imageDescription, string campaignId)
    {
        var data = await GenerateImageData(imageDescription);
        var fileName = $"{campaignId}/tempImage_{Guid.NewGuid().ToString()}.png";
        return await SaveAsUrl(fileName, data.ToArray());
    }
    public static string ConvertNonAlphaNumericToUnderscore(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? "";

        return Regex.Replace(input, @"[^a-zA-Z0-9]", "_");
    }
    private static async Task<string> SaveAsUrl(string fileName, byte[] bytes)
    {
        var blobClient = BlobContainerClient.GetBlobClient(fileName);
        await blobClient.UploadAsync(new BinaryData(bytes), overwrite: true);
        return $"{ImageContainerUrl}/{fileName}";
    }
    public async Task<string> SaveUserImage(string fileName, ReadOnlyMemory<byte> data)
    {
        var blobName = Path.Combine("UserProfileImages", fileName);

        //var data = Convert.FromBase64String(dataBase64);
        return await SaveAsUrl(blobName, data.ToArray());

    }

}