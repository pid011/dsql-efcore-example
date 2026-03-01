# Supabase Load Test Report

## 개요
- 대상 서비스: `GameBackend` API (`https://localhost:7179`)
- 실행 방식: `dotnet run -c Release --project GameBackend.AppHost/GameBackend.AppHost.csproj`
- 데이터베이스: 외부 Supabase PostgreSQL (pooler)
- 커넥션 풀 설정: `MaxPoolSize = 10`

## 테스트 시나리오
- `POST /players` 300회
- `PUT /players/{id}/stats` 300회
- `GET /players/{id}/profile` 300회
- `GET /players` 100회
- 동시성: 20

## 최신 결과 (Pool=10, Release)
- Total Requests: 994
- Success: 973
- Fail: 21 (HTTP 500)
- Elapsed: 13,427.63 ms
- Throughput: 74.03 RPS

### 단계별 결과
- create: total 300 / success 297 / fail 3 / avg 146.93 ms / p95 177.34 ms / p99 190.06 ms
- update-stats: total 297 / success 289 / fail 8 / avg 452.06 ms / p95 517.10 ms / p99 544.93 ms
- get-profile: total 297 / success 290 / fail 7 / avg 171.09 ms / p95 206.99 ms / p99 223.32 ms
- list-players: total 100 / success 97 / fail 3 / avg 177.49 ms / p95 228.89 ms / p99 240.35 ms

## 비교 결과 (Pool=15 대비)
- Pool=15 결과: Total 994 / Success 971 / Fail 23 / 75.5 RPS
- Pool=10 결과: Total 994 / Success 973 / Fail 21 / 74.03 RPS
- 해석: 실패율은 소폭 개선되었지만 500 오류가 여전히 발생하며, 처리량은 유사 수준입니다.

## 실패 원인 분석
백엔드 로그에서 다음 오류가 반복 확인되었습니다.
- `Npgsql.PostgresException: XX000`
- `MessageText: Max client connections reached`
- `MessageText: MaxClientsInSessionMode: max clients reached - in Session mode max clients are limited to pool_size`
- 일부 `EndOfStreamException` 동반

즉, 애플리케이션 풀 사이즈 조정만으로는 Supabase pooler/session 제한을 완전히 피하지 못하고 있습니다.

## 권장 사항
1. 부하 테스트 동시성을 10~15로 낮춘 안정 구간 측정
2. Supabase pooler 설정(세션 모드 client limit) 상향 검토
3. 필요 시 앱 단에서 DB 접근이 많은 API에 대해 재시도/백오프 적용 및 동시성 제한
