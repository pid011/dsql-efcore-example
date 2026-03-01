using Amazon;

var builder = DistributedApplication.CreateBuilder(args);

var awsRegion = builder.Configuration["AWS:Region"] ?? "us-east-1";
var awsProfile = builder.Configuration["AWS:Profile"];

var awsConfig = builder.AddAWSSDKConfig()
    .WithRegion(RegionEndpoint.GetBySystemName(awsRegion));

if (!string.IsNullOrWhiteSpace(awsProfile))
{
    awsConfig.WithProfile(awsProfile);
}

var awsResources = builder
    .AddAWSCloudFormationTemplate("aws-resources", "app-resources.template", stackName: "gamebackend-local-dev")
    .WithReference(awsConfig);

var postgres = builder.AddPostgres("postgres");
var gameBackendDb = postgres.AddDatabase("gamebackenddb");

var backend = builder.AddProject<Projects.GameBackend>("backend")
    .WaitFor(awsResources)
    .WithReference(awsResources)
    .WithReference(gameBackendDb)
    .WithEnvironment("GAMEBACKEND_DSQL_CLUSTER_ENDPOINT", awsResources.GetOutput("GameBackendDsqlClusterEndpoint"))
    .WithEnvironment("AWS_REGION", awsConfig.Region!.SystemName);

if (!string.IsNullOrWhiteSpace(awsConfig.Profile))
{
    backend.WithEnvironment("AWS_PROFILE", awsConfig.Profile);
}

builder.Build().Run();
