using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameBackend.Players;

internal static class PostgresErrorClassifier
{
    public static bool IsUniquePlayerNameViolation(Exception exception)
    {
        return exception switch
        {
            PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: PlayerApiConstants.PlayerNameUniqueConstraint
            } => true,
            DbUpdateException
            {
                InnerException: PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                    ConstraintName: PlayerApiConstants.PlayerNameUniqueConstraint
                }
            } => true,
            _ => false
        };
    }

    public static bool IsSerializationFailure(Exception exception)
    {
        return exception switch
        {
            PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } => true,
            DbUpdateException
            {
                InnerException: PostgresException
                {
                    SqlState: PostgresErrorCodes.SerializationFailure
                }
            } => true,
            _ => false
        };
    }

    public static string ToProblemDetail(Exception exception)
    {
        return exception switch
        {
            PostgresException postgresException => postgresException.MessageText,
            DbUpdateException { InnerException: PostgresException postgresException } => postgresException.MessageText,
            DbUpdateException dbUpdateException => dbUpdateException.Message,
            _ => exception.Message
        };
    }
}
