CREATE TABLE IF NOT EXISTS games (
    id uuid NOT NULL,
    status character varying(20) NOT NULL,
    started_at timestamp with time zone NOT NULL,
    ended_at timestamp with time zone NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_games PRIMARY KEY (id)
);

CREATE INDEX ASYNC IF NOT EXISTS ix_games_status_created_at
ON games (status, created_at);
