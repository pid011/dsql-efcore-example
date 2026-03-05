CREATE INDEX ASYNC IF NOT EXISTS ix_players_created_at_id
ON players (created_at, id);
