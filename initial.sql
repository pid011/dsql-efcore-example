CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

CREATE TABLE "Players" (
    "Id" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Players" PRIMARY KEY ("Id")
);

CREATE TABLE "PlayerStats" (
    "PlayerId" uuid NOT NULL,
    "MatchesPlayed" integer NOT NULL,
    "Wins" integer NOT NULL,
    "Losses" integer NOT NULL,
    "Draws" integer NOT NULL,
    "CurrentWinStreak" integer NOT NULL,
    "BestWinStreak" integer NOT NULL,
    "TotalKills" integer NOT NULL,
    "TotalDeaths" integer NOT NULL,
    "TotalAssists" integer NOT NULL,
    "TotalScore" integer NOT NULL,
    "Rating" integer NOT NULL,
    "HighestRating" integer NOT NULL,
    "LastMatchAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_PlayerStats" PRIMARY KEY ("PlayerId")
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260301084649_InitialCreate', '10.0.3');

