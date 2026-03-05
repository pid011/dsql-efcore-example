namespace GameBackend.Players;

internal static class PlayerApiConstants
{
    public const int DefaultListLimit = 100;
    public const int MaxListLimit = 200;
    public const int MaxSerializationRetryCount = 3;
    public const int ResetDeleteBatchSize = 1000;
    public const int MaxGameResultCount = 100;

    public const string PlayerNameUniqueConstraint = "ux_players_name";
}
