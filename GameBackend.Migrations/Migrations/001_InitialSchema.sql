CREATE TABLE IF NOT EXISTS players (
    id uuid NOT NULL,
    name character varying(100) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_players PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS player_stats (
    player_id uuid NOT NULL,
    matches_played integer NOT NULL DEFAULT 0,
    wins integer NOT NULL DEFAULT 0,
    losses integer NOT NULL DEFAULT 0,
    draws integer NOT NULL DEFAULT 0,
    current_win_streak integer NOT NULL DEFAULT 0,
    best_win_streak integer NOT NULL DEFAULT 0,
    total_kills integer NOT NULL DEFAULT 0,
    total_deaths integer NOT NULL DEFAULT 0,
    total_assists integer NOT NULL DEFAULT 0,
    total_score integer NOT NULL DEFAULT 0,
    rating integer NOT NULL DEFAULT 1000,
    highest_rating integer NOT NULL DEFAULT 1000,
    last_match_at timestamp with time zone NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_player_stats PRIMARY KEY (player_id)
);

CREATE INDEX ASYNC IF NOT EXISTS ix_players_created_at ON players (created_at);