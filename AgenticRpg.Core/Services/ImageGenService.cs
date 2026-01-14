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

#pragma warning disable MEAI001
#pragma warning disable OPENAI001

namespace AgenticRpg.Core.Services;

public class ImageGenService
{
    private static readonly AgentConfiguration configuration = AgentStaticConfiguration.Default;
    private const string ImageContainerUrl = "https://npcimages.z20.web.core.windows.net/";
    private const string ImageDataPrefix = "data:image/png;base64,";
    private static BlobServiceClient blobServiceClient => new BlobServiceClient(configuration.BlobStorageConnectionString);
    private static BlobContainerClient _blobContainerClient => blobServiceClient.GetBlobContainerClient("$web");
    public static async Task<string> GenerateCharacterImage(string instructions, string playerId, string characterName)
    {
        var data = await GenerateImageData(instructions);
        var fileName = $"{playerId}/{ConvertNonAlphaNumericToUnderscore(characterName)}.png";
        return await SaveAsUrl(fileName, data.ToArray());
        //return url.First().Uri.ToString();
    }

    private static async Task<ReadOnlyMemory<byte>> GenerateImageData(string instructions)
    {
        var client = new OpenAIClient(new ApiKeyCredential(configuration.OpenAIApiKey)/*, new OpenAIClientOptions(){Endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/") }*/);
        var imageClient = client.GetImageClient("gpt-image-1.5").AsIImageGenerator();

        var images = await imageClient.GenerateImagesAsync(
            instructions);
        var contents = images.Contents.OfType<DataContent>();
        var data = contents.First().Data;
        return data;
    }

    public static async Task<string> GenerateCharacterImage(Character character, string? additionalInstructions = null)
    {
        
        var instructions = $"Character: {character.Name}, Race: {character.Race}, Class: {character.Class}, Background: {character.Background}";

        if (!string.IsNullOrEmpty(additionalInstructions))
        {
            instructions += $", Additional Instructions: {additionalInstructions}";
        }

        return await GenerateCharacterImage(instructions, character.PlayerId, character.Name);
    }
    public static async Task<string>GenerateWorldImage(World world, string additionalInstructions = "")
    {
        var instructions = $"World Name: {world.Name}, Description: {world.Description}. Geography: {world.Geography}.";
        if (!string.IsNullOrEmpty(additionalInstructions))
        {
            instructions += $" Additional Instructions: {additionalInstructions}";
        }
        return await GenerateCharacterImage(instructions, "worlds", world.Name);
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
        var blobClient = _blobContainerClient.GetBlobClient(fileName);
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