var builder = DistributedApplication.CreateBuilder(args);

// DSQL 경로는 임시 비활성화 상태입니다.
// 외부 PostgreSQL 연결 문자열(ConnectionStrings:gamebackenddb)을 사용합니다.
var postgres = builder.AddConnectionString("gamebackenddb");

//var migrations = builder.AddProject<Projects.GameBackend_Migrations>("gamebackend-migrations")
//    .WithReference(postgres);

builder.AddProject<Projects.GameBackend>("backend")
    //.WaitForCompletion(migrations)
    .WithReference(postgres);

builder.Build().Run();
