using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace AgenticRpg.Core.Helpers;

public static class ClaimsPrincipalExtensions
{
    public static string SerializeClaimsPrincipal(this ClaimsPrincipal principal)
    {
        var claims = principal.Identities.SelectMany(identity => identity.Claims)
            .Select(c => new { c.Type, c.Value, c.Issuer, c.OriginalIssuer, c.ValueType })
            .ToList();

        return JsonSerializer.Serialize(claims);
    }

    public static ClaimsPrincipal DeserializeClaimsPrincipal(string json)
    {
        var claimsData = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json);
        var claims = claimsData.Select(c => new Claim(c["Type"], c["Value"], c["ValueType"], c["Issuer"], c["OriginalIssuer"]));
        var identity = new ClaimsIdentity(claims, "CustomAuthenticationType"); // You can specify the authentication type
        return new ClaimsPrincipal(identity);
    }
}
