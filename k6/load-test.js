import http from "k6/http";
import { check, sleep } from "k6";
import { Rate, Trend } from "k6/metrics";

const BASE_URL = __ENV.BASE_URL || "http://localhost:5074";

const errorRate = new Rate("errors");
const createPlayerDuration = new Trend("create_player_duration", true);
const listPlayersDuration = new Trend("list_players_duration", true);
const getPlayerDuration = new Trend("get_player_duration", true);
const getProfileDuration = new Trend("get_profile_duration", true);
const submitMatchDuration = new Trend("submit_match_duration", true);

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
        { duration: "30s", target: 20 },
        { duration: "1m", target: 20 },
        { duration: "30s", target: 50 },
        { duration: "1m", target: 50 },
        { duration: "30s", target: 0 },
      ],
      startTime: "35s",
      tags: { scenario: "load" },
    },
  },
  thresholds: {
    http_req_duration: ["p(95)<500", "p(99)<1000"],
    errors: ["rate<0.1"],
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

// POST /players
function createPlayer() {
  const payload = JSON.stringify({ name: randomName() });
  const res = http.post(`${BASE_URL}/players`, payload, {
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

// GET /players
function listPlayers() {
  const res = http.get(`${BASE_URL}/players`);
  listPlayersDuration.add(res.timings.duration);

  const ok = check(res, {
    "list players: status 200": (r) => r.status === 200,
    "list players: is array": (r) => Array.isArray(r.json()),
  });
  errorRate.add(!ok);

  return ok ? res.json() : [];
}

// GET /players/{id}
function getPlayer(playerId) {
  const res = http.get(`${BASE_URL}/players/${playerId}`);
  getPlayerDuration.add(res.timings.duration);

  const ok = check(res, {
    "get player: status 200": (r) => r.status === 200,
    "get player: correct id": (r) => r.json().id === playerId,
  });
  errorRate.add(!ok);
}

// GET /players/{id}/profile
function getPlayerProfile(playerId) {
  const res = http.get(`${BASE_URL}/players/${playerId}/profile`);
  getProfileDuration.add(res.timings.duration);

  const ok = check(res, {
    "get profile: status 200": (r) => r.status === 200,
    "get profile: has player": (r) => r.json().player != null,
  });
  errorRate.add(!ok);
}

// POST /players/{id}/match-results
function submitMatchResult(playerId) {
  const payload = JSON.stringify({
    matchResult: randomMatchResult(),
    kills: randomInt(0, 30),
    deaths: randomInt(0, 15),
    assists: randomInt(0, 20),
    score: randomInt(100, 5000),
  });

  const res = http.post(
    `${BASE_URL}/players/${playerId}/match-results`,
    payload,
    { headers: JSON_HEADERS }
  );
  submitMatchDuration.add(res.timings.duration);

  const ok = check(res, {
    "submit match: status 200": (r) => r.status === 200,
    "submit match: has rating": (r) => r.json().rating !== undefined,
  });
  errorRate.add(!ok);
}

export default function () {
  // 1. 플레이어 생성
  const player = createPlayer();
  if (!player) {
    sleep(1);
    return;
  }

  sleep(0.5);

  // 2. 플레이어 목록 조회
  listPlayers();
  sleep(0.3);

  // 3. 단일 플레이어 조회
  getPlayer(player.id);
  sleep(0.3);

  // 4. 매치 결과 3회 제출
  for (let i = 0; i < 3; i++) {
    submitMatchResult(player.id);
    sleep(0.2);
  }

  // 5. 프로필 조회 (스탯 포함)
  getPlayerProfile(player.id);
  sleep(0.5);
}
