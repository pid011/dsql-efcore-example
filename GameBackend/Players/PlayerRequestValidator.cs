namespace GameBackend.Players;

internal static class PlayerRequestValidator
{
    public static Dictionary<string, string[]>? ValidatePlayerName(string? rawName, out string trimmedName)
    {
        trimmedName = rawName?.Trim() ?? string.Empty;
        if (trimmedName.Length is 0 or > 100)
        {
            return new Dictionary<string, string[]>
            {
                ["name"] = ["Name must be between 1 and 100 characters."]
            };
        }

        return null;
    }

    public static Dictionary<string, string[]>? ValidateListRequest(
        int? limit,
        DateTime? beforeCreatedAt,
        Guid? beforeId,
        out int effectiveLimit)
    {
        effectiveLimit = limit ?? PlayerApiConstants.DefaultListLimit;
        if (effectiveLimit is <= 0 or > PlayerApiConstants.MaxListLimit)
        {
            return new Dictionary<string, string[]>
            {
                ["limit"] = [$"limit must be between 1 and {PlayerApiConstants.MaxListLimit}."]
            };
        }

        if (beforeCreatedAt.HasValue != beforeId.HasValue)
        {
            return new Dictionary<string, string[]>
            {
                ["beforeCreatedAt"] = ["beforeCreatedAt and beforeId must be provided together."],
                ["beforeId"] = ["beforeCreatedAt and beforeId must be provided together."]
            };
        }

        return null;
    }

    public static Dictionary<string, string[]>? ValidateEndGameRequest(EndGameRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.GameId == Guid.Empty)
        {
            errors["gameId"] = ["gameId is required."];
        }

        if (request.Results is null || request.Results.Count == 0)
        {
            errors["results"] = ["At least one player result is required."];
            return errors;
        }

        if (request.Results.Count > PlayerApiConstants.MaxGameResultCount)
        {
            errors["results"] = [$"A game can include at most {PlayerApiConstants.MaxGameResultCount} player results."];
        }

        var duplicatedPlayerIds = request.Results
            .GroupBy(result => result.PlayerId)
            .Where(group => group.Key != Guid.Empty && group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicatedPlayerIds.Length > 0)
        {
            errors["results.playerId"] = ["Each playerId must be unique in a single game result payload."];
        }

        for (var index = 0; index < request.Results.Count; index++)
        {
            var result = request.Results[index];

            if (result.PlayerId == Guid.Empty)
            {
                errors[$"results[{index}].playerId"] = ["playerId is required."];
            }

            if (!Enum.IsDefined(result.MatchResult))
            {
                errors[$"results[{index}].matchResult"] = ["matchResult must be one of Win, Loss, or Draw."];
            }
        }

        return errors.Count == 0 ? null : errors;
    }
}
