import http from "k6/http";
import { check, sleep } from "k6";
import { Rate, Trend } from "k6/metrics";

const BASE_URL = __ENV.BASE_URL || "http://localhost:5074";
const LIST_LIMIT = Number(__ENV.LIST_LIMIT || "100");

const errorRate = new Rate("dapper_errors");
const createPlayerDuration = new Trend("dapper_create_player_duration", true);
const createGameDuration = new Trend("dapper_create_game_duration", true);
const endGameDuration = new Trend("dapper_end_game_duration", true);
const listPlayersDuration = new Trend("dapper_list_players_duration", true);
const getPlayerDuration = new Trend("dapper_get_player_duration", true);
const getProfileDuration = new Trend("dapper_get_profile_duration", true);

export const options = {
  scenarios: {
    smoke: {
      executor: "constant-vus",
      vus: 5,
      duration: "30s",
      tags: { scenario: "smoke" },
    },
    load: {
      executor: "ramping-vus",
      startVUs: 0,
      stages: [
        { duration: "20s", target: 20 },
        { duration: "1m", target: 20 },
        { duration: "20s", target: 50 },
        { duration: "1m", target: 50 },
        { duration: "20s", target: 80 },
        { duration: "1m", target: 80 },
        { duration: "20s", target: 0 },
      ],
      startTime: "35s",
      tags: { scenario: "load" },
    },
  },
  thresholds: {
    http_req_duration: ["p(95)<500", "p(99)<1000"],
    dapper_errors: ["rate<0.1"],
  },
};

const JSON_HEADERS = { "Content-Type": "application/json" };

function randomName() {
  const adjectives = ["Swift", "Bold", "Dark", "Iron", "Fire", "Ice", "Storm"];
  const nouns = ["Knight", "Mage", "Rogue", "Hunter", "Wolf", "Dragon", "Hawk"];
  const adj = adjectives[Math.floor(Math.random() * adjectives.length)];
  const noun = nouns[Math.floor(Math.random() * nouns.length)];
  return `${adj}${noun}_${Date.now()}_${__VU}`;
}

function randomMatchResult() {
  const results = ["Win", "Loss", "Draw"];
  return results[Math.floor(Math.random() * results.length)];
}

function randomInt(min, max) {
  return Math.floor(Math.random() * (max - min + 1)) + min;
}

// POST /dapper/players
function createPlayer() {
  const payload = JSON.stringify({ name: randomName() });
  const res = http.post(`${BASE_URL}/dapper/players`, payload, {
    headers: JSON_HEADERS,
  });
  createPlayerDuration.add(res.timings.duration);

  const ok = check(res, {
    "create player: status 201": (r) => r.status === 201,
    "create player: has id": (r) => {
      const body = r.json();
      return body && body.id;
    },
  });
  errorRate.add(!ok);

  return ok ? res.json() : null;
}

// POST /dapper/game/create
function createGame() {
  const res = http.post(`${BASE_URL}/dapper/game/create`, null, {
    headers: JSON_HEADERS,
  });
  createGameDuration.add(res.timings.duration);

  const ok = check(res, {
    "create game: status 201": (r) => r.status === 201,
    "create game: has game id": (r) => {
      const body = r.json();
      return body && body.game && body.game.id;
    },
  });
  errorRate.add(!ok);

  return ok ? res.json().game : null;
}

// POST /dapper/game/end
function endGame(gameId, playerId) {
  const payload = JSON.stringify({
    gameId,
    results: [
      {
        playerId,
        matchResult: randomMatchResult(),
        kills: randomInt(0, 30),
        deaths: randomInt(0, 15),
        assists: randomInt(0, 20),
        score: randomInt(100, 5000),
      },
    ],
  });

  const res = http.post(`${BASE_URL}/dapper/game/end`, payload, {
    headers: JSON_HEADERS,
  });
  endGameDuration.add(res.timings.duration);

  const ok = check(res, {
    "end game: status 200": (r) => r.status === 200,
    "end game: has updated stats": (r) => {
      if (r.status !== 200) return false;
      try {
        const body = r.json();
        return body && Array.isArray(body.updatedStats) && body.updatedStats.length > 0;
      } catch (_) {
        return false;
      }
    },
  });
  errorRate.add(!ok);
}

// GET /dapper/players?limit={LIST_LIMIT}
function listPlayers() {
  const res = http.get(`${BASE_URL}/dapper/players?limit=${LIST_LIMIT}`);
  listPlayersDuration.add(res.timings.duration);

  const ok = check(res, {
    "list players: status 200": (r) => r.status === 200,
    "list players: is array": (r) => Array.isArray(r.json()),
  });
  errorRate.add(!ok);

  return ok ? res.json() : [];
}

// GET /dapper/players/{id}
function getPlayer(playerId) {
  const res = http.get(`${BASE_URL}/dapper/players/${playerId}`);
  getPlayerDuration.add(res.timings.duration);

  const ok = check(res, {
    "get player: status 200": (r) => r.status === 200,
    "get player: correct id": (r) => r.json().id === playerId,
  });
  errorRate.add(!ok);
}

// GET /dapper/players/{id}/profile
function getPlayerProfile(playerId) {
  const res = http.get(`${BASE_URL}/dapper/players/${playerId}/profile`);
  getProfileDuration.add(res.timings.duration);

  const ok = check(res, {
    "get profile: status 200": (r) => r.status === 200,
    "get profile: has player": (r) => r.json().player != null,
  });
  errorRate.add(!ok);
}

export default function () {
  // 1. Create player
  const player = createPlayer();
  if (!player) {
    sleep(1);
    return;
  }

  sleep(0.2);

  // 2. List players
  listPlayers();
  sleep(0.1);

  // 3. Get single player
  getPlayer(player.id);
  sleep(0.1);

  // 4. Create and end 3 games
  for (let i = 0; i < 3; i++) {
    const game = createGame();
    if (game) {
      endGame(game.id, player.id);
    }
    sleep(0.05);
  }

  // 5. Get profile (with stats)
  getPlayerProfile(player.id);
  sleep(0.2);
}
